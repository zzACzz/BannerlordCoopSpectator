using System;
using System.Collections.Generic;

namespace CoopSpectator.Infrastructure
{
    public enum CoopBattleResultCampaignDecision
    {
        AllowModern = 0,
        AllowLegacy = 1,
        AlreadyApplied = 2,
        RejectMissingActiveCampaign = 3,
        RejectUnsupportedBindingVersion = 4,
        RejectInvalidCampaignId = 5,
        RejectCampaignMismatch = 6,
        RejectInvalidResultId = 7,
        RejectMissingStrongLegacyBattleIdentity = 8,
        RejectBattleInstanceMismatch = 9
    }

    public sealed class CoopBattleResultCampaignEvaluation
    {
        public CoopBattleResultCampaignDecision Decision { get; set; }
        public string JournalKey { get; set; }
        public bool IsLegacy { get; set; }

        public bool IsAllowed =>
            Decision == CoopBattleResultCampaignDecision.AllowModern ||
            Decision == CoopBattleResultCampaignDecision.AllowLegacy;
    }

    public sealed class CoopBattleResultApplicationLease
    {
        internal CoopBattleResultApplicationLease(string campaignId, string journalKey, Guid token)
        {
            CampaignId = campaignId;
            JournalKey = journalKey;
            Token = token;
        }

        public string CampaignId { get; }
        public string JournalKey { get; }
        internal Guid Token { get; }
    }

    public sealed class CoopBattleResultApplicationGate
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, Guid> _leasesByKey =
            new Dictionary<string, Guid>(StringComparer.Ordinal);

        public bool TryBegin(
            string campaignId,
            string journalKey,
            out CoopBattleResultApplicationLease lease)
        {
            lease = null;
            if (string.IsNullOrWhiteSpace(campaignId) ||
                string.IsNullOrWhiteSpace(journalKey))
            {
                return false;
            }

            string scopedKey = BuildScopedKey(campaignId, journalKey);
            lock (_sync)
            {
                if (_leasesByKey.ContainsKey(scopedKey))
                    return false;

                Guid token = Guid.NewGuid();
                _leasesByKey.Add(scopedKey, token);
                lease = new CoopBattleResultApplicationLease(campaignId, journalKey, token);
                return true;
            }
        }

        public bool IsOwned(CoopBattleResultApplicationLease lease)
        {
            if (lease == null)
                return false;

            string scopedKey = BuildScopedKey(lease.CampaignId, lease.JournalKey);
            lock (_sync)
            {
                return _leasesByKey.TryGetValue(scopedKey, out Guid token) &&
                       token == lease.Token;
            }
        }

        public bool Complete(CoopBattleResultApplicationLease lease)
        {
            return Release(lease);
        }

        public bool Fail(CoopBattleResultApplicationLease lease)
        {
            return Release(lease);
        }

        public void Reset()
        {
            lock (_sync)
                _leasesByKey.Clear();
        }

        private bool Release(CoopBattleResultApplicationLease lease)
        {
            if (lease == null)
                return false;

            string scopedKey = BuildScopedKey(lease.CampaignId, lease.JournalKey);
            lock (_sync)
            {
                if (!_leasesByKey.TryGetValue(scopedKey, out Guid token) ||
                    token != lease.Token)
                {
                    return false;
                }

                _leasesByKey.Remove(scopedKey);
                return true;
            }
        }

        private static string BuildScopedKey(string campaignId, string journalKey)
        {
            return (campaignId ?? string.Empty) + "\n" + (journalKey ?? string.Empty);
        }
    }

    public static class CoopBattleResultCampaignGuardContract
    {
        public const int CurrentCampaignBindingVersion = 1;
        public const int MaxRememberedResultIds = 64;

        public static CoopBattleResultCampaignEvaluation Evaluate(
            int campaignBindingVersion,
            bool hasActiveCampaign,
            string activeCampaignId,
            string resultCampaignId,
            string resultId,
            string resultBattleInstanceId,
            bool hasStrongActiveBattleIdentity,
            string activeBattleInstanceId,
            bool requireModernBattleInstanceMatch,
            Func<string, bool> isAlreadyApplied)
        {
            if (!hasActiveCampaign || !TryNormalizeGuidN(activeCampaignId, out string normalizedActiveCampaignId))
            {
                return Reject(CoopBattleResultCampaignDecision.RejectMissingActiveCampaign);
            }

            bool hasModernBindingMetadata =
                campaignBindingVersion != 0 ||
                resultCampaignId != null;
            if (hasModernBindingMetadata)
            {
                if (campaignBindingVersion != CurrentCampaignBindingVersion)
                {
                    return Reject(CoopBattleResultCampaignDecision.RejectUnsupportedBindingVersion);
                }

                if (!TryNormalizeGuidN(resultCampaignId, out string normalizedResultCampaignId))
                    return Reject(CoopBattleResultCampaignDecision.RejectInvalidCampaignId);

                if (!string.Equals(
                        normalizedActiveCampaignId,
                        normalizedResultCampaignId,
                        StringComparison.Ordinal))
                {
                    return Reject(CoopBattleResultCampaignDecision.RejectCampaignMismatch);
                }

                if (!IsValidResultId(resultId))
                    return Reject(CoopBattleResultCampaignDecision.RejectInvalidResultId);

                if (requireModernBattleInstanceMatch &&
                    !BattleInstancesMatch(resultBattleInstanceId, activeBattleInstanceId))
                {
                    return Reject(CoopBattleResultCampaignDecision.RejectBattleInstanceMismatch);
                }

                if (isAlreadyApplied != null && isAlreadyApplied(resultId))
                    return AlreadyApplied(resultId, isLegacy: false);

                return Allow(resultId, isLegacy: false);
            }

            if (!hasStrongActiveBattleIdentity ||
                !BattleInstancesMatch(resultBattleInstanceId, activeBattleInstanceId))
            {
                return Reject(CoopBattleResultCampaignDecision.RejectMissingStrongLegacyBattleIdentity);
            }

            string legacyJournalKey;
            if (!string.IsNullOrWhiteSpace(resultId))
            {
                if (!IsValidResultId(resultId))
                    return Reject(CoopBattleResultCampaignDecision.RejectInvalidResultId);

                legacyJournalKey = resultId;
            }
            else if (!TryBuildLegacyDeduplicationKey(resultBattleInstanceId, out legacyJournalKey))
            {
                return Reject(CoopBattleResultCampaignDecision.RejectInvalidResultId);
            }

            if (isAlreadyApplied != null && isAlreadyApplied(legacyJournalKey))
                return AlreadyApplied(legacyJournalKey, isLegacy: true);

            return Allow(legacyJournalKey, isLegacy: true);
        }

        public static bool TryBuildStableResultId(
            string battleInstanceId,
            string battleStage,
            out string resultId)
        {
            resultId = null;
            if (!TryNormalizeGuidN(battleInstanceId, out string normalizedBattleInstanceId) ||
                !IsValidStage(battleStage))
            {
                return false;
            }

            resultId = normalizedBattleInstanceId + "|" + battleStage.Trim();
            return true;
        }

        public static bool TryBuildLegacyDeduplicationKey(
            string battleInstanceId,
            out string journalKey)
        {
            journalKey = null;
            if (!TryNormalizeGuidN(battleInstanceId, out string normalizedBattleInstanceId))
                return false;

            journalKey = "legacy:" + normalizedBattleInstanceId;
            return true;
        }

        public static bool IsValidCampaignId(string campaignId)
        {
            return TryNormalizeGuidN(campaignId, out _);
        }

        public static bool IsValidResultId(string resultId)
        {
            if (string.IsNullOrWhiteSpace(resultId) || resultId.Length > 256)
                return false;

            int separatorIndex = resultId.IndexOf('|');
            if (separatorIndex <= 0 || separatorIndex != resultId.LastIndexOf('|'))
                return false;

            string battleInstanceId = resultId.Substring(0, separatorIndex);
            string battleStage = resultId.Substring(separatorIndex + 1);
            return TryNormalizeGuidN(battleInstanceId, out _) && IsValidStage(battleStage);
        }

        public static List<string> NormalizeJournal(IEnumerable<string> resultIds)
        {
            var source = new List<string>();
            if (resultIds != null)
            {
                foreach (string resultId in resultIds)
                {
                    if (!string.IsNullOrWhiteSpace(resultId))
                        source.Add(resultId);
                }
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var reversed = new List<string>();
            for (int index = source.Count - 1; index >= 0; index--)
            {
                string resultId = source[index];
                if (!seen.Add(resultId))
                    continue;

                reversed.Add(resultId);
                if (reversed.Count >= MaxRememberedResultIds)
                    break;
            }

            reversed.Reverse();
            return reversed;
        }

        private static bool BattleInstancesMatch(string left, string right)
        {
            return TryNormalizeGuidN(left, out string normalizedLeft) &&
                   TryNormalizeGuidN(right, out string normalizedRight) &&
                   string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);
        }

        private static bool TryNormalizeGuidN(string value, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(value) ||
                !Guid.TryParseExact(value.Trim(), "N", out Guid parsed))
            {
                return false;
            }

            normalized = parsed.ToString("N");
            return true;
        }

        private static bool IsValidStage(string battleStage)
        {
            if (string.IsNullOrWhiteSpace(battleStage))
                return false;

            string trimmed = battleStage.Trim();
            if (trimmed.Length > 64)
                return false;

            for (int index = 0; index < trimmed.Length; index++)
            {
                char character = trimmed[index];
                if (!char.IsLetterOrDigit(character) &&
                    character != '_' &&
                    character != '-' &&
                    character != '.')
                {
                    return false;
                }
            }

            return true;
        }

        private static CoopBattleResultCampaignEvaluation Allow(string journalKey, bool isLegacy)
        {
            return new CoopBattleResultCampaignEvaluation
            {
                Decision = isLegacy
                    ? CoopBattleResultCampaignDecision.AllowLegacy
                    : CoopBattleResultCampaignDecision.AllowModern,
                JournalKey = journalKey,
                IsLegacy = isLegacy
            };
        }

        private static CoopBattleResultCampaignEvaluation AlreadyApplied(string journalKey, bool isLegacy)
        {
            return new CoopBattleResultCampaignEvaluation
            {
                Decision = CoopBattleResultCampaignDecision.AlreadyApplied,
                JournalKey = journalKey,
                IsLegacy = isLegacy
            };
        }

        private static CoopBattleResultCampaignEvaluation Reject(
            CoopBattleResultCampaignDecision decision)
        {
            return new CoopBattleResultCampaignEvaluation
            {
                Decision = decision,
                JournalKey = null,
                IsLegacy = false
            };
        }
    }
}
