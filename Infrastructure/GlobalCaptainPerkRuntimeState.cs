using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
        private static string _frozenStateSignature;
        private static bool _isFrozen;
        private static bool _agentStatEffectsReady;

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

        public static bool AreAgentStatEffectsReadyFor(BattleRuntimeState state)
        {
            string battleInstanceId = ResolveBattleInstanceId(state);
            lock (Sync)
            {
                return _isFrozen &&
                    _agentStatEffectsReady &&
                    !string.IsNullOrWhiteSpace(_frozenStateSignature) &&
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
                _frozenStateSignature = string.Empty;
                _agentStatEffectsReady = false;

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

                _frozenStateSignature = ComputeFrozenStateSignature(
                    _battleInstanceId,
                    FrozenCaptainEntryIds,
                    EffectsByCombatGroup);
                _isFrozen = true;
                _agentStatEffectsReady = !string.IsNullOrWhiteSpace(_frozenStateSignature);
                if (IsVerboseDiagnosticsEnabled())
                {
                    ModLogger.Info(
                        "GlobalCaptainPerkRuntimeState: frozen. " +
                        "BattleInstanceId=" + _battleInstanceId +
                        " Captains=" + FrozenCaptainEntryIds.Count +
                        " CombatGroups=" + EffectsByCombatGroup.Count +
                        " UniqueEffects=" + EffectsByCombatGroup.Values.Sum(group => group.Count) +
                        " Signature=" + _frozenStateSignature +
                        " Source=" + (source ?? "unknown"));
                }

                return _agentStatEffectsReady;
            }
        }

        public static bool ApplyAuthoritativeFrozenState(
            BattleRuntimeState state,
            string battleInstanceId,
            IEnumerable<string> captainEntryIds,
            IEnumerable<FrozenCaptainCombatGroupSnapshotMessage> frozenCombatGroups,
            string expectedSignature,
            string source)
        {
            string localBattleInstanceId = ResolveBattleInstanceId(state);
            if (state == null ||
                string.IsNullOrWhiteSpace(localBattleInstanceId) ||
                string.IsNullOrWhiteSpace(battleInstanceId) ||
                string.IsNullOrWhiteSpace(expectedSignature) ||
                !string.Equals(localBattleInstanceId, battleInstanceId, StringComparison.Ordinal))
            {
                return false;
            }

            Dictionary<string, string> combatGroupByEntryId = BuildCombatGroupByEntryId(state);
            Dictionary<string, Dictionary<string, CaptainPerkEffectSnapshotMessage>> effectsByCombatGroup =
                BuildEffectsByCombatGroup(frozenCombatGroups);
            List<string> authoritativeCaptainEntryIds = (captainEntryIds ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();
            string actualSignature = ComputeFrozenStateSignature(
                battleInstanceId,
                authoritativeCaptainEntryIds,
                effectsByCombatGroup);
            if (!string.Equals(actualSignature, expectedSignature, StringComparison.OrdinalIgnoreCase))
                return false;

            lock (Sync)
            {
                if (_isFrozen &&
                    _agentStatEffectsReady &&
                    string.Equals(_battleInstanceId, battleInstanceId, StringComparison.Ordinal) &&
                    string.Equals(_frozenStateSignature, actualSignature, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                EffectsByCombatGroup.Clear();
                foreach (KeyValuePair<string, Dictionary<string, CaptainPerkEffectSnapshotMessage>> groupPair in effectsByCombatGroup)
                {
                    EffectsByCombatGroup[groupPair.Key] = groupPair.Value.ToDictionary(
                        pair => pair.Key,
                        pair => CloneEffect(pair.Value),
                        StringComparer.OrdinalIgnoreCase);
                }

                CombatGroupByEntryId.Clear();
                foreach (KeyValuePair<string, string> entryPair in combatGroupByEntryId)
                    CombatGroupByEntryId[entryPair.Key] = entryPair.Value;

                FrozenCaptainEntryIds.Clear();
                FrozenCaptainEntryIds.AddRange(authoritativeCaptainEntryIds);
                _battleInstanceId = battleInstanceId;
                _frozenStateSignature = actualSignature;
                _isFrozen = true;
                _agentStatEffectsReady = true;

                if (IsVerboseDiagnosticsEnabled())
                {
                    ModLogger.Info(
                        "GlobalCaptainPerkRuntimeState: applied authoritative frozen state. " +
                        "BattleInstanceId=" + _battleInstanceId +
                        " Captains=" + FrozenCaptainEntryIds.Count +
                        " CombatGroups=" + EffectsByCombatGroup.Count +
                        " UniqueEffects=" + EffectsByCombatGroup.Values.Sum(group => group.Count) +
                        " Signature=" + _frozenStateSignature +
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

        public static string GetFrozenBattleInstanceId()
        {
            lock (Sync)
            {
                return _isFrozen ? _battleInstanceId ?? string.Empty : string.Empty;
            }
        }

        public static string GetFrozenStateSignature()
        {
            lock (Sync)
            {
                return _isFrozen && _agentStatEffectsReady
                    ? _frozenStateSignature ?? string.Empty
                    : string.Empty;
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

        private static Dictionary<string, string> BuildCombatGroupByEntryId(BattleRuntimeState state)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (state == null)
                return result;

            foreach (RosterEntryState entry in state.EntriesById.Values.Where(candidate => candidate != null))
            {
                if (string.IsNullOrWhiteSpace(entry.EntryId) || string.IsNullOrWhiteSpace(entry.PartyId))
                    continue;

                if (!state.PartiesById.TryGetValue(entry.PartyId, out BattlePartyState party) || party == null)
                    continue;

                string combatGroupId = string.IsNullOrWhiteSpace(party.CombatGroupId)
                    ? (party.SideId ?? entry.SideId ?? "side") + "|party|" + party.PartyId
                    : party.CombatGroupId;
                result[entry.EntryId] = combatGroupId;
            }

            return result;
        }

        private static Dictionary<string, Dictionary<string, CaptainPerkEffectSnapshotMessage>> BuildEffectsByCombatGroup(
            IEnumerable<FrozenCaptainCombatGroupSnapshotMessage> frozenCombatGroups)
        {
            var result = new Dictionary<string, Dictionary<string, CaptainPerkEffectSnapshotMessage>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (FrozenCaptainCombatGroupSnapshotMessage frozenGroup in
                frozenCombatGroups ?? Enumerable.Empty<FrozenCaptainCombatGroupSnapshotMessage>())
            {
                if (frozenGroup == null || string.IsNullOrWhiteSpace(frozenGroup.CombatGroupId))
                    continue;

                if (!result.TryGetValue(
                        frozenGroup.CombatGroupId,
                        out Dictionary<string, CaptainPerkEffectSnapshotMessage> groupEffects))
                {
                    groupEffects = new Dictionary<string, CaptainPerkEffectSnapshotMessage>(StringComparer.OrdinalIgnoreCase);
                    result[frozenGroup.CombatGroupId] = groupEffects;
                }

                foreach (CaptainPerkEffectSnapshotMessage effect in
                    frozenGroup.Effects ?? Enumerable.Empty<CaptainPerkEffectSnapshotMessage>())
                {
                    if (effect == null || string.IsNullOrWhiteSpace(effect.PerkId) || groupEffects.ContainsKey(effect.PerkId))
                        continue;

                    groupEffects[effect.PerkId] = CloneEffect(effect);
                }
            }

            return result;
        }

        private static string ComputeFrozenStateSignature(
            string battleInstanceId,
            IEnumerable<string> captainEntryIds,
            IReadOnlyDictionary<string, Dictionary<string, CaptainPerkEffectSnapshotMessage>> effectsByCombatGroup)
        {
            if (string.IsNullOrWhiteSpace(battleInstanceId))
                return string.Empty;

            var builder = new StringBuilder();
            builder.Append("battle=").Append(battleInstanceId).Append('\n');
            foreach (string captainEntryId in (captainEntryIds ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal))
            {
                builder.Append("captain=").Append(captainEntryId).Append('\n');
            }

            foreach (KeyValuePair<string, Dictionary<string, CaptainPerkEffectSnapshotMessage>> groupPair in
                (effectsByCombatGroup ??
                    new Dictionary<string, Dictionary<string, CaptainPerkEffectSnapshotMessage>>(StringComparer.OrdinalIgnoreCase))
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append("group=").Append(groupPair.Key).Append('\n');
                foreach (CaptainPerkEffectSnapshotMessage effect in (groupPair.Value?.Values ??
                    Enumerable.Empty<CaptainPerkEffectSnapshotMessage>())
                    .Where(value => value != null && !string.IsNullOrWhiteSpace(value.PerkId))
                    .OrderBy(value => value.PerkId, StringComparer.OrdinalIgnoreCase))
                {
                    builder
                        .Append("effect=")
                        .Append(effect.PerkId)
                        .Append('|')
                        .Append(effect.Bonus.ToString("R", CultureInfo.InvariantCulture))
                        .Append('|')
                        .Append(effect.IncrementType ?? string.Empty)
                        .Append('\n');
                }
            }

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
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
