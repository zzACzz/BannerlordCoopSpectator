using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Network.Messages;

namespace CoopSpectator.Infrastructure
{
    public sealed class CaptainPerkBonusAccumulator
    {
        private readonly float _baseValue;
        private float _additiveBonus;
        private float _factorBonus;

        public CaptainPerkBonusAccumulator(float baseValue)
        {
            _baseValue = baseValue;
        }

        public bool HasEffects { get; private set; }

        public void Add(CaptainPerkEffectSnapshotMessage effect)
        {
            if (effect == null)
                return;

            HasEffects = true;
            if (!string.IsNullOrWhiteSpace(effect.IncrementType) &&
                effect.IncrementType.IndexOf("factor", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _factorBonus += effect.Bonus;
            }
            else
            {
                _additiveBonus += effect.Bonus;
            }
        }

        public float Result => (_baseValue + _additiveBonus) * Math.Max(0f, 1f + _factorBonus);
    }

    public static class GlobalCaptainPerkRuntimeState
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Dictionary<string, CaptainPerkEffectSnapshotMessage>> EffectsByCombatGroup =
            new Dictionary<string, Dictionary<string, CaptainPerkEffectSnapshotMessage>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> CombatGroupByEntryId =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly List<string> FrozenCaptainEntryIds = new List<string>();
        private static string _battleInstanceId;
        private static bool _isFrozen;

        public static bool IsFrozenFor(BattleRuntimeState state)
        {
            string battleInstanceId = ResolveBattleInstanceId(state);
            lock (Sync)
            {
                return _isFrozen &&
                    !string.IsNullOrWhiteSpace(battleInstanceId) &&
                    string.Equals(_battleInstanceId, battleInstanceId, StringComparison.Ordinal);
            }
        }

        public static bool Freeze(BattleRuntimeState state, IEnumerable<string> captainEntryIds, string source)
        {
            if (state == null)
                return false;

            string battleInstanceId = ResolveBattleInstanceId(state);
            if (string.IsNullOrWhiteSpace(battleInstanceId))
                return false;

            lock (Sync)
            {
                if (_isFrozen && string.Equals(_battleInstanceId, battleInstanceId, StringComparison.Ordinal))
                    return true;

                EffectsByCombatGroup.Clear();
                CombatGroupByEntryId.Clear();
                FrozenCaptainEntryIds.Clear();
                _battleInstanceId = battleInstanceId;

                foreach (RosterEntryState entry in state.EntriesById.Values.Where(candidate => candidate != null))
                {
                    if (string.IsNullOrWhiteSpace(entry.EntryId) || string.IsNullOrWhiteSpace(entry.PartyId))
                        continue;

                    if (!state.PartiesById.TryGetValue(entry.PartyId, out BattlePartyState party) || party == null)
                        continue;

                    string combatGroupId = string.IsNullOrWhiteSpace(party.CombatGroupId)
                        ? (party.SideId ?? entry.SideId ?? "side") + "|party|" + party.PartyId
                        : party.CombatGroupId;
                    CombatGroupByEntryId[entry.EntryId] = combatGroupId;
                }

                foreach (FrozenCaptainCombatGroupSnapshotMessage frozenGroup in
                    state.Snapshot?.FrozenCaptainCombatGroups ?? Enumerable.Empty<FrozenCaptainCombatGroupSnapshotMessage>())
                {
                    if (frozenGroup == null || string.IsNullOrWhiteSpace(frozenGroup.CombatGroupId))
                        continue;

                    if (!EffectsByCombatGroup.TryGetValue(
                            frozenGroup.CombatGroupId,
                            out Dictionary<string, CaptainPerkEffectSnapshotMessage> groupEffects))
                    {
                        groupEffects = new Dictionary<string, CaptainPerkEffectSnapshotMessage>(StringComparer.OrdinalIgnoreCase);
                        EffectsByCombatGroup[frozenGroup.CombatGroupId] = groupEffects;
                    }

                    foreach (CaptainPerkEffectSnapshotMessage effect in
                        frozenGroup.Effects ?? Enumerable.Empty<CaptainPerkEffectSnapshotMessage>())
                    {
                        if (effect == null || string.IsNullOrWhiteSpace(effect.PerkId) || groupEffects.ContainsKey(effect.PerkId))
                            continue;

                        groupEffects[effect.PerkId] = CloneEffect(effect);
                    }
                }

                List<string> requestedCaptainEntryIds = (captainEntryIds?
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal) ?? Enumerable.Empty<string>())
                    .ToList();
                FrozenCaptainEntryIds.AddRange(requestedCaptainEntryIds);
                foreach (string captainEntryId in requestedCaptainEntryIds)
                {
                    if (!state.EntriesById.TryGetValue(captainEntryId, out RosterEntryState captainEntry) ||
                        captainEntry?.IsHero != true ||
                        captainEntry.CaptainPerkEffects == null ||
                        !CombatGroupByEntryId.TryGetValue(captainEntryId, out string combatGroupId))
                    {
                        continue;
                    }

                    if (!EffectsByCombatGroup.TryGetValue(
                            combatGroupId,
                            out Dictionary<string, CaptainPerkEffectSnapshotMessage> groupEffects))
                    {
                        groupEffects = new Dictionary<string, CaptainPerkEffectSnapshotMessage>(StringComparer.OrdinalIgnoreCase);
                        EffectsByCombatGroup[combatGroupId] = groupEffects;
                    }

                    foreach (CaptainPerkEffectSnapshotMessage effect in captainEntry.CaptainPerkEffects)
                    {
                        if (effect == null || string.IsNullOrWhiteSpace(effect.PerkId) || groupEffects.ContainsKey(effect.PerkId))
                            continue;

                        groupEffects[effect.PerkId] = CloneEffect(effect);
                    }
                }

                _isFrozen = true;
                if (IsVerboseDiagnosticsEnabled())
                {
                    ModLogger.Info(
                        "GlobalCaptainPerkRuntimeState: frozen. " +
                        "BattleInstanceId=" + _battleInstanceId +
                        " Captains=" + FrozenCaptainEntryIds.Count +
                        " CombatGroups=" + EffectsByCombatGroup.Count +
                        " UniqueEffects=" + EffectsByCombatGroup.Values.Sum(group => group.Count) +
                        " Source=" + (source ?? "unknown"));
                }

                return true;
            }
        }

        public static bool TryGetEffect(
            string beneficiaryEntryId,
            string perkId,
            out CaptainPerkEffectSnapshotMessage effect)
        {
            effect = null;
            if (string.IsNullOrWhiteSpace(beneficiaryEntryId) || string.IsNullOrWhiteSpace(perkId))
                return false;

            lock (Sync)
            {
                return _isFrozen &&
                    CombatGroupByEntryId.TryGetValue(beneficiaryEntryId, out string combatGroupId) &&
                    EffectsByCombatGroup.TryGetValue(combatGroupId, out Dictionary<string, CaptainPerkEffectSnapshotMessage> groupEffects) &&
                    groupEffects.TryGetValue(perkId, out effect);
            }
        }

        public static bool AddEffect(
            string beneficiaryEntryId,
            string perkId,
            CaptainPerkBonusAccumulator accumulator)
        {
            if (accumulator == null || !TryGetEffect(beneficiaryEntryId, perkId, out CaptainPerkEffectSnapshotMessage effect))
                return false;

            accumulator.Add(effect);
            return true;
        }

        public static IReadOnlyList<string> GetFrozenCaptainEntryIds()
        {
            lock (Sync)
            {
                return FrozenCaptainEntryIds.ToArray();
            }
        }

        public static IReadOnlyList<CaptainPerkEffectSnapshotMessage> GetEffectsForEntry(string entryId)
        {
            if (string.IsNullOrWhiteSpace(entryId))
                return Array.Empty<CaptainPerkEffectSnapshotMessage>();

            lock (Sync)
            {
                if (!_isFrozen ||
                    !CombatGroupByEntryId.TryGetValue(entryId, out string combatGroupId) ||
                    !EffectsByCombatGroup.TryGetValue(combatGroupId, out Dictionary<string, CaptainPerkEffectSnapshotMessage> effects))
                {
                    return Array.Empty<CaptainPerkEffectSnapshotMessage>();
                }

                return effects.Values
                    .OrderBy(effect => effect.PerkId, StringComparer.OrdinalIgnoreCase)
                    .Select(CloneEffect)
                    .ToArray();
            }
        }

        public static IReadOnlyList<FrozenCaptainCombatGroupSnapshotMessage> GetFrozenCombatGroups()
        {
            lock (Sync)
            {
                if (!_isFrozen)
                    return Array.Empty<FrozenCaptainCombatGroupSnapshotMessage>();

                return EffectsByCombatGroup
                    .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => new FrozenCaptainCombatGroupSnapshotMessage
                    {
                        CombatGroupId = pair.Key,
                        Effects = pair.Value.Values
                            .OrderBy(effect => effect.PerkId, StringComparer.OrdinalIgnoreCase)
                            .Select(CloneEffect)
                            .ToList()
                    })
                    .ToArray();
            }
        }

        private static CaptainPerkEffectSnapshotMessage CloneEffect(CaptainPerkEffectSnapshotMessage effect)
        {
            return new CaptainPerkEffectSnapshotMessage
            {
                PerkId = effect?.PerkId,
                Bonus = effect?.Bonus ?? 0f,
                IncrementType = effect?.IncrementType
            };
        }

        private static string ResolveBattleInstanceId(BattleRuntimeState state)
        {
            return state?.Snapshot?.BattleInstanceId ?? state?.Snapshot?.BattleId;
        }

        private static bool IsVerboseDiagnosticsEnabled()
        {
            string value = Environment.GetEnvironmentVariable("COOPSPECTATOR_CAPTAIN_PERK_DIAGNOSTICS");
            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim();
            return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("on", StringComparison.OrdinalIgnoreCase);
        }
    }
}
