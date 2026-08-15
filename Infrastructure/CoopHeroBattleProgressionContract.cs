using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    public static class CoopHeroBattleProgressionContract
    {
        private const string StaticMirrorItemIdPrefix = "cs_mirror_";

        public static string SelectCampaignWeaponItemId(string battleWeaponItemId, string mappedOriginalItemId)
        {
            if (!string.IsNullOrWhiteSpace(mappedOriginalItemId))
                return mappedOriginalItemId.Trim();

            if (TryDecodeStaticMirrorOriginalItemId(battleWeaponItemId, out string decodedOriginalItemId))
                return decodedOriginalItemId;

            return battleWeaponItemId?.Trim() ?? string.Empty;
        }

        public static IReadOnlyList<string> BuildWeaponResolutionCandidates(
            string campaignWeaponItemId,
            string battleWeaponItemId)
        {
            var candidates = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddCandidate(candidates, seen, campaignWeaponItemId);
            AddCandidate(candidates, seen, battleWeaponItemId);

            if (TryDecodeStaticMirrorOriginalItemId(campaignWeaponItemId, out string campaignDecodedItemId))
                AddCandidate(candidates, seen, campaignDecodedItemId);
            if (TryDecodeStaticMirrorOriginalItemId(battleWeaponItemId, out string battleDecodedItemId))
                AddCandidate(candidates, seen, battleDecodedItemId);

            return candidates;
        }

        public static bool TryDecodeStaticMirrorOriginalItemId(string mirrorItemId, out string originalItemId)
        {
            originalItemId = null;
            if (string.IsNullOrWhiteSpace(mirrorItemId))
                return false;

            string trimmedMirrorItemId = mirrorItemId.Trim();
            if (!trimmedMirrorItemId.StartsWith(StaticMirrorItemIdPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            int hashSeparatorIndex = trimmedMirrorItemId.LastIndexOf('_');
            if (hashSeparatorIndex <= StaticMirrorItemIdPrefix.Length ||
                hashSeparatorIndex + 9 != trimmedMirrorItemId.Length)
                return false;

            string hashToken = trimmedMirrorItemId.Substring(hashSeparatorIndex + 1);
            if (!uint.TryParse(hashToken, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
                return false;

            string decodedItemId = trimmedMirrorItemId.Substring(
                StaticMirrorItemIdPrefix.Length,
                hashSeparatorIndex - StaticMirrorItemIdPrefix.Length);
            if (string.IsNullOrWhiteSpace(decodedItemId) ||
                !string.Equals(BuildStaticMirrorItemId(decodedItemId), trimmedMirrorItemId, StringComparison.OrdinalIgnoreCase))
                return false;

            originalItemId = decodedItemId;
            return true;
        }

        public static Dictionary<string, float> CalculatePositiveSkillXpDeltas(
            IReadOnlyDictionary<string, float> beforeSkillXpById,
            IReadOnlyDictionary<string, float> afterSkillXpById)
        {
            var deltas = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            if (afterSkillXpById == null)
                return deltas;

            foreach (KeyValuePair<string, float> after in afterSkillXpById)
            {
                if (string.IsNullOrWhiteSpace(after.Key))
                    continue;

                float beforeXp = 0f;
                beforeSkillXpById?.TryGetValue(after.Key, out beforeXp);
                float delta = after.Value - beforeXp;
                if (delta > 0.0001f)
                    deltas[after.Key] = delta;
            }

            return deltas;
        }

        public static bool IsFatalCombatEventMatch(
            string combatEventVictimEntryId,
            string combatEventAttackerEntryId,
            string removedVictimEntryId,
            string removalAttackerEntryId,
            bool combatEventIsAlreadyFatal)
        {
            if (combatEventIsAlreadyFatal ||
                string.IsNullOrWhiteSpace(removedVictimEntryId) ||
                !string.Equals(combatEventVictimEntryId, removedVictimEntryId, StringComparison.OrdinalIgnoreCase))
                return false;

            return !string.IsNullOrWhiteSpace(removalAttackerEntryId) &&
                   !string.IsNullOrWhiteSpace(combatEventAttackerEntryId) &&
                   string.Equals(combatEventAttackerEntryId, removalAttackerEntryId, StringComparison.OrdinalIgnoreCase);
        }

        private static void AddCandidate(ICollection<string> candidates, ISet<string> seen, string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return;

            string trimmedItemId = itemId.Trim();
            if (seen.Add(trimmedItemId))
                candidates.Add(trimmedItemId);
        }

        private static string BuildStaticMirrorItemId(string originalItemId)
        {
            return StaticMirrorItemIdPrefix + NormalizeMirrorIdToken(originalItemId) + "_" + ComputeStableMirrorHash(originalItemId);
        }

        private static string NormalizeMirrorIdToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length);
            foreach (char character in value.Trim())
            {
                if ((character >= 'a' && character <= 'z') ||
                    (character >= 'A' && character <= 'Z') ||
                    (character >= '0' && character <= '9') ||
                    character == '_')
                    builder.Append(char.ToLowerInvariant(character));
                else
                    builder.Append('_');
            }

            return builder.ToString();
        }

        private static string ComputeStableMirrorHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
                for (int i = 0; i < normalized.Length; i++)
                {
                    hash ^= normalized[i];
                    hash *= 16777619u;
                }

                return hash.ToString("x8", CultureInfo.InvariantCulture);
            }
        }
    }
}
