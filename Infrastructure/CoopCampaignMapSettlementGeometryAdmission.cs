using System;
using System.Collections.Generic;

namespace CoopSpectator.Infrastructure
{
    public static class CoopCampaignMapRaidPresentationPolicy
    {
        // 0 unknown/undisclosed, 1 clear, 2 active raid, 3 already raided.
        public static bool IsValid(int state) => state >= 0 && state <= 3;
        public static int Capture(bool hostVisible, bool isVillage, bool activeRaid, bool isRaided) =>
            !hostVisible ? 0 : !isVillage ? 1 : activeRaid ? 2 : isRaided ? 3 : 1;
        public static string ParticleSystem(int state)
        {
            if (!IsValid(state)) throw new ArgumentOutOfRangeException(nameof(state));
            return state == 2 ? "psys_fire_smoke_env_point" :
                state == 3 ? "map_icon_village_plunder_fx" : string.Empty;
        }
    }

    public static class CoopCampaignMapSettlementPresentationPolicy
    {
        public const int MaxWallSections = 32;
        // Preparation: 0 unknown, 1 hidden, 2 visible. Empty walls mean unknown
        // or not applicable; each known section is 0 intact / 1 breached.
        public static bool IsValid(string walls, int preparation)
        {
            if (walls == null || walls.Length > MaxWallSections || preparation < 0 || preparation > 2)
                return false;
            foreach (char wall in walls) if (wall != '0' && wall != '1') return false;
            return true;
        }

        public static string CaptureWalls(IEnumerable<float> healthRatios)
        {
            if (healthRatios == null) return string.Empty;
            var walls = new List<char>();
            foreach (float ratio in healthRatios)
            {
                if (walls.Count >= MaxWallSections || float.IsNaN(ratio) || float.IsInfinity(ratio))
                    return string.Empty;
                walls.Add(ratio <= 0f ? '1' : '0');
            }
            return new string(walls.ToArray());
        }

        public static int CapturePreparation(bool underSiege, float? progress)
        {
            if (!underSiege || !progress.HasValue) return 1;
            if (float.IsNaN(progress.Value) || float.IsInfinity(progress.Value)) return 0;
            return progress.Value >= 1f ? 2 : 1;
        }

        public static bool IsWallBroken(string walls, int nativeWallIndex)
        {
            if (!IsValid(walls, 0) || walls.Length == 0 || nativeWallIndex < 0)
                throw new ArgumentException("Unknown wall presentation.");
            return walls[nativeWallIndex % walls.Length] == '1';
        }
    }

    public static class CoopCampaignMapSettlementAuthoringPolicy
    {
        public static bool IsFortificationHelper(string tag) =>
            string.Equals(tag, "main_map_city_gate", StringComparison.Ordinal) ||
            string.Equals(tag, "map_banner_placeholder", StringComparison.Ordinal);

        // Resolve the entire removal plan before touching native state. Removing
        // an ancestor also invalidates descendants, which must not be removed twice.
        public static List<T> SelectRemovalRoots<T>(IReadOnlyList<T> candidates,
            Func<T, UIntPtr> identity, Func<T, T> parent) where T : class
        {
            if (candidates == null || identity == null || parent == null)
                throw new ArgumentNullException();
            var byId = new Dictionary<UIntPtr, T>();
            foreach (T candidate in candidates)
            {
                if (candidate == null || identity(candidate) == UIntPtr.Zero)
                    throw new InvalidOperationException("Invalid authoring helper identity.");
                byId[identity(candidate)] = candidate;
            }
            var roots = new List<T>();
            foreach (T candidate in byId.Values)
            {
                var ancestors = new HashSet<UIntPtr> { identity(candidate) };
                bool covered = false;
                for (T ancestor = parent(candidate); ancestor != null; ancestor = parent(ancestor))
                {
                    UIntPtr id = identity(ancestor);
                    if (id == UIntPtr.Zero || !ancestors.Add(id) || ancestors.Count > 4096)
                        throw new InvalidOperationException("Invalid authoring helper ancestry.");
                    if (byId.ContainsKey(id)) covered = true;
                }
                if (!covered) roots.Add(candidate);
            }
            return roots;
        }
    }

    // Semantic bits, not engine-specific scene bit positions. Zero means unknown.
    public static class CoopCampaignMapSettlementLevelPolicy
    {
        public static string LevelName(int index)
        {
            switch (index)
            {
                case 0: return "level_1";
                case 1: return "level_2";
                case 2: return "level_3";
                case 3: return "civilian";
                case 4: return "looted";
                case 5: return "siege";
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public static bool IsValid(int flags)
        {
            if (flags == 0) return true;
            if ((flags & ~63) != 0) return false;
            int level = flags & 7;
            int state = flags & 56;
            return (level == 1 || level == 2 || level == 4) &&
                   (state == 8 || state == 16 || state == 32 || (state == 0 && level == 1));
        }

        public static int Capture(uint mask, Func<string, uint> resolve)
        {
            if (mask == 0 || resolve == null) return 0;
            int flags = 0;
            uint reconstructed = 0;
            for (int i = 0; i < 6; i++)
            {
                uint bit = resolve(LevelName(i));
                if (bit == 0 || (bit & (bit - 1)) != 0) return 0;
                if ((mask & bit) == 0) continue;
                flags |= 1 << i;
                reconstructed |= bit;
            }
            return reconstructed == mask && IsValid(flags) ? flags : 0;
        }

        public static uint Resolve(int flags, Func<string, uint> resolve)
        {
            if (flags == 0 || !IsValid(flags) || resolve == null) return 0;
            uint mask = 0;
            for (int i = 0; i < 6; i++)
            {
                if ((flags & (1 << i)) == 0) continue;
                uint bit = resolve(LevelName(i));
                if (bit == 0 || (bit & (bit - 1)) != 0 || (mask & bit) != 0) return 0;
                mask |= bit;
            }
            return mask;
        }

        public static bool Matches(uint childMask, uint hostMask) =>
            hostMask != 0 && (childMask & hostMask) == hostMask;
    }

    /// <summary>
    /// Per-owned-scene admission of static settlement geometry. Registration
    /// starts hidden; only an admitted settlement projection may reveal it.
    /// Optional variant application must succeed before a root can be revealed.
    /// </summary>
    public sealed class CoopCampaignMapSettlementGeometryAdmission<T> where T : class
    {
        private sealed class Binding
        {
            public T Root;
            public bool Visible;
            public int LevelFlags;
            public string WallStates;
            public int PreparationState;
            public int RaidState;
        }

        private readonly Dictionary<string, Binding> _bindings =
            new Dictionary<string, Binding>(StringComparer.OrdinalIgnoreCase);
        private readonly Action<T, bool> _setVisibility;
        private readonly Func<T, int, bool> _applyLevels;
        private readonly Func<T, string, int, bool> _applyPresentation;
        private readonly Func<T, int, bool> _applyRaid;

        public CoopCampaignMapSettlementGeometryAdmission(Action<T, bool> setVisibility,
            Func<T, int, bool> applyLevels = null,
            Func<T, string, int, bool> applyPresentation = null,
            Func<T, int, bool> applyRaid = null)
        {
            _setVisibility = setVisibility ?? throw new ArgumentNullException(nameof(setVisibility));
            _applyLevels = applyLevels;
            _applyPresentation = applyPresentation;
            _applyRaid = applyRaid;
        }

        public void Register(string sceneEntityName, T root)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));
            _setVisibility(root, false);
            if (_applyRaid != null && !_applyRaid(root, 0))
                throw new InvalidOperationException("Could not clear settlement authoring particles.");
            if (string.IsNullOrWhiteSpace(sceneEntityName))
                return;

            string id = "settlement:" + sceneEntityName;
            if (_bindings.TryGetValue(id, out Binding existing))
            {
                // Native adapter deduplicates handles. Different roots with
                // the same wire identity are ambiguous and must stay hidden.
                if (existing != null)
                {
                    _setVisibility(existing.Root, false);
                    _applyRaid?.Invoke(existing.Root, 0);
                }
                _bindings[id] = null;
                return;
            }
            if (_bindings.Count >= CoopCampaignMapPrototypeContract.MaxCatalogEntities)
                throw new InvalidOperationException("Settlement geometry binding limit exceeded.");
            _bindings.Add(id, new Binding { Root = root });
        }

        public void Apply(string entityId, bool isAdmittedSettlement, int levelFlags = 0,
            string wallStates = "", int preparationState = 0, int raidState = 0)
        {
            if (entityId == null || !_bindings.TryGetValue(entityId, out Binding binding) ||
                binding == null)
                return;

            if ((_applyLevels != null || _applyPresentation != null) && isAdmittedSettlement &&
                (!binding.Visible || binding.LevelFlags != levelFlags ||
                 binding.WallStates != wallStates || binding.PreparationState != preparationState))
            {
                _setVisibility(binding.Root, false);
                binding.Visible = false;
                if (!CoopCampaignMapSettlementPresentationPolicy.IsValid(wallStates, preparationState) ||
                    levelFlags == 0 || !CoopCampaignMapSettlementLevelPolicy.IsValid(levelFlags) ||
                    (_applyLevels != null && !_applyLevels(binding.Root, levelFlags)) ||
                    (_applyPresentation != null && !_applyPresentation(binding.Root, wallStates, preparationState)))
                    isAdmittedSettlement = false;
                else
                {
                    binding.LevelFlags = levelFlags;
                    binding.WallStates = wallStates;
                    binding.PreparationState = preparationState;
                }
            }
            int admittedRaid = isAdmittedSettlement && CoopCampaignMapRaidPresentationPolicy.IsValid(raidState)
                ? raidState : 0;
            if (_applyRaid != null && binding.RaidState != admittedRaid)
            {
                if (!_applyRaid(binding.Root, admittedRaid))
                {
                    // Adapter also clears partially created effects on failure.
                    _setVisibility(binding.Root, false); binding.Visible = false;
                    binding.RaidState = -1; // Retry rather than cache a failed application.
                    return;
                }
                binding.RaidState = admittedRaid;
            }
            if (binding.Visible == isAdmittedSettlement) return;
            _setVisibility(binding.Root, isAdmittedSettlement);
            binding.Visible = isAdmittedSettlement;
        }

        // Called before native scene destruction; never dereference old roots.
        public void Clear()
        {
            _bindings.Clear();
        }
    }
}
