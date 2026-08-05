using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CoopSpectator.Infrastructure
{
    public enum CoopHeroCreationParticipantState
    {
        Invited = 0,
        Editing = 1,
        Completed = 2,
        Declined = 3,
        Disconnected = 4,
        Reconnected = 5,
        TimedOut = 6,
        IdentityUnavailable = 7,
        AlreadyExists = 8,
        Late = 9
    }

    public enum CoopHeroCreationClientCommandKind
    {
        BeginEditing = 0,
        Submit = 1,
        Decline = 2
    }

    public sealed class CoopHeroCreationRules
    {
        public string RulesVersion { get; set; } = "bannerlord-1.4.7-coop-creator-v2";
        public int MinimumAge { get; set; } = 20;
        public int MaximumAge { get; set; } = 50;
        public int MaximumNameLength { get; set; } = 64;
        public int MaximumPayloadCharacters { get; set; } = 24576;
        public int EnrollmentSeconds { get; set; } = 30;
        public int SessionSeconds { get; set; } = 900;
        public int DisconnectGraceSeconds { get; set; } = 30;
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<int> AllowedAges { get; set; } = Enumerable.Range(20, 31).ToList();

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> AllowedCultureIds { get; set; } = new List<string>
        {
            "empire", "vlandia", "sturgia", "aserai", "khuzait", "battania"
        };

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> AttributeIds { get; set; } = new List<string>
        {
            "Vigor", "Control", "Endurance", "Cunning", "Social", "Intelligence"
        };

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> SkillIds { get; set; } = new List<string>
        {
            "OneHanded", "TwoHanded", "Polearm", "Bow", "Crossbow", "Throwing",
            "Riding", "Athletics", "Smithing", "Scouting", "Tactics", "Roguery",
            "Charm", "Leadership", "Trade", "Steward", "Medicine", "Engineering"
        };
        public List<CoopHeroCreationPerkRule> Perks { get; set; } = new List<CoopHeroCreationPerkRule>();

        public static int GetAttributeBudget(int age)
        {
            if (age < 20 || age > 50) return -1;
            return age < 30 ? 18 : age < 40 ? 19 : age < 50 ? 20 : 21;
        }

        public static int GetFocusBudget(int age)
        {
            if (age < 20 || age > 50) return -1;
            return age < 30 ? 12 : age < 40 ? 14 : age < 50 ? 16 : 18;
        }

        public string ComputeHash()
        {
            return CoopHeroCreationHash.ComputeCanonicalJsonHash(this);
        }
    }

    public sealed class CoopHeroCreationPerkRule
    {
        public string PerkId { get; set; }
        public string Name { get; set; }
        public string SkillId { get; set; }
        public int RequiredSkillValue { get; set; }
        public string AlternativePerkId { get; set; }
    }

    public sealed class CoopHeroCreationRequest
    {
        public int ProtocolVersion { get; set; } = CoopHeroCreationContract.ProtocolVersion;
        public string CampaignScopeId { get; set; }
        public string RequestId { get; set; }
        public string SessionId { get; set; }
        public string Nonce { get; set; }
        public string RulesHash { get; set; }
        public string CreatedUtc { get; set; }
        public CoopHeroCreationRules Rules { get; set; }
        public List<string> ExistingPlayerHashes { get; set; } = new List<string>();
    }

    public sealed class CoopHeroDraft
    {
        public int SchemaVersion { get; set; } = 1;
        public string Name { get; set; }
        public string CultureId { get; set; }
        public int Age { get; set; }
        public bool IsFemale { get; set; }
        public string BodyProperties { get; set; }
        public Dictionary<string, int> Attributes { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int> Focus { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int> Skills { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public List<string> PerkIds { get; set; } = new List<string>();
        public Dictionary<string, int> TraitLevels { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    public sealed class CoopHeroCreationParticipantResult
    {
        public string PlayerIdentityHash { get; set; }
        public string LogicalHeroId { get; set; }
        public CoopHeroCreationParticipantState State { get; set; }
        public string Reason { get; set; }
        public int Revision { get; set; }
        public string SubmissionId { get; set; }
        public string PayloadHash { get; set; }
        public CoopHeroDraft Draft { get; set; }
    }

    public sealed class CoopHeroCreationResult
    {
        public int ProtocolVersion { get; set; } = CoopHeroCreationContract.ProtocolVersion;
        public string CampaignScopeId { get; set; }
        public string RequestId { get; set; }
        public string SessionId { get; set; }
        public string Nonce { get; set; }
        public string RulesHash { get; set; }
        public string ResultId { get; set; }
        public string CompletedUtc { get; set; }
        public string FailureReason { get; set; }
        public List<CoopHeroCreationParticipantResult> Participants { get; set; } = new List<CoopHeroCreationParticipantResult>();
    }

    public sealed class CoopHeroCreationServerEnvelope
    {
        public int ProtocolVersion { get; set; } = CoopHeroCreationContract.ProtocolVersion;
        public string RequestId { get; set; }
        public string SessionId { get; set; }
        public string Nonce { get; set; }
        public string RulesHash { get; set; }
        public CoopHeroCreationParticipantState State { get; set; }
        public string Reason { get; set; }
        public string EnrollmentDeadlineUtc { get; set; }
        public string SessionDeadlineUtc { get; set; }
        public int RelevantCount { get; set; }
        public int TerminalCount { get; set; }
        public CoopHeroCreationRules Rules { get; set; }
    }

    public static class CoopHeroCreationContract
    {
        public const int ProtocolVersion = 1;

        public static string BuildLogicalHeroId(string campaignScopeId, string playerIdentityHash)
        {
            return CoopHeroCreationHash.ComputeSha256(
                "CoopHero/v1|" + (campaignScopeId ?? string.Empty).Trim() + "|" +
                (playerIdentityHash ?? string.Empty).Trim().ToLowerInvariant());
        }

        public static string ComputeResultId(CoopHeroCreationResult result)
        {
            if (result == null) return string.Empty;
            return CoopHeroCreationHash.ComputeCanonicalJsonHash(new
            {
                result.CampaignScopeId,
                result.RequestId,
                result.SessionId,
                result.Nonce,
                result.RulesHash,
                result.FailureReason,
                result.Participants
            });
        }

        public static bool IsTerminal(CoopHeroCreationParticipantState state)
        {
            return state == CoopHeroCreationParticipantState.Completed ||
                   state == CoopHeroCreationParticipantState.Declined ||
                   state == CoopHeroCreationParticipantState.TimedOut ||
                   state == CoopHeroCreationParticipantState.IdentityUnavailable ||
                   state == CoopHeroCreationParticipantState.AlreadyExists ||
                   state == CoopHeroCreationParticipantState.Late;
        }

        public static bool ValidateRequest(CoopHeroCreationRequest request, out string error)
        {
            if (request == null) return Fail("request_missing", out error);
            if (request.ProtocolVersion != ProtocolVersion) return Fail("protocol_mismatch", out error);
            if (string.IsNullOrWhiteSpace(request.CampaignScopeId)) return Fail("campaign_scope_missing", out error);
            if (string.IsNullOrWhiteSpace(request.RequestId)) return Fail("request_id_missing", out error);
            if (string.IsNullOrWhiteSpace(request.SessionId)) return Fail("session_id_missing", out error);
            if (string.IsNullOrWhiteSpace(request.Nonce)) return Fail("nonce_missing", out error);
            if (request.Rules == null) return Fail("rules_missing", out error);
            if (!string.Equals(request.RulesHash, request.Rules.ComputeHash(), StringComparison.OrdinalIgnoreCase))
                return Fail("rules_hash_mismatch", out error);
            error = string.Empty;
            return true;
        }

        public static bool ValidateDraft(CoopHeroDraft draft, CoopHeroCreationRules rules, out string error)
        {
            if (draft == null) return Fail("draft_missing", out error);
            if (rules == null) return Fail("rules_missing", out error);
            string name = (draft.Name ?? string.Empty).Trim();
            if (name.Length < 2 || name.Length > rules.MaximumNameLength || name.Any(char.IsControl))
                return Fail("name_invalid", out error);
            if (!rules.AllowedCultureIds.Contains(draft.CultureId ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                return Fail("culture_invalid", out error);
            if (!rules.AllowedAges.Contains(draft.Age)) return Fail("age_invalid", out error);
            if (string.IsNullOrWhiteSpace(draft.BodyProperties) || draft.BodyProperties.Length > 4096)
                return Fail("body_properties_invalid", out error);
            if (!ValidateExactKeys(draft.Attributes, rules.AttributeIds, out error)) return false;
            if (!ValidateExactKeys(draft.Focus, rules.SkillIds, out error)) return false;
            if (!ValidateExactKeys(draft.Skills, rules.SkillIds, out error)) return false;

            if (draft.Attributes.Any(p => p.Value < 2 || p.Value > 10)) return Fail("attribute_out_of_range", out error);
            if (draft.Attributes.Values.Sum() != CoopHeroCreationRules.GetAttributeBudget(draft.Age))
                return Fail("attribute_budget_mismatch", out error);
            if (draft.Focus.Any(p => p.Value < 0 || p.Value > 5)) return Fail("focus_out_of_range", out error);
            if (draft.Focus.Values.Sum() != CoopHeroCreationRules.GetFocusBudget(draft.Age))
                return Fail("focus_budget_mismatch", out error);
            if (draft.Skills.Any(p => p.Value < 0 || p.Value > 50 || p.Value % 10 != 0))
                return Fail("skill_out_of_range", out error);
            if (draft.Skills.Values.Sum() != 100) return Fail("skill_budget_mismatch", out error);
            foreach (string skillId in rules.SkillIds)
            {
                if (draft.Focus[skillId] < draft.Skills[skillId] / 10)
                    return Fail("skill_focus_dependency_mismatch:" + skillId, out error);
            }

            if (draft.PerkIds == null || draft.PerkIds.Count != draft.PerkIds.Distinct(StringComparer.Ordinal).Count())
                return Fail("perks_invalid", out error);
            if (draft.PerkIds.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 128))
                return Fail("perk_id_invalid", out error);
            Dictionary<string, CoopHeroCreationPerkRule> perkRules = (rules.Perks ?? new List<CoopHeroCreationPerkRule>())
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.PerkId))
                .GroupBy(p => p.PerkId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            foreach (string perkId in draft.PerkIds)
            {
                CoopHeroCreationPerkRule perkRule;
                if (!perkRules.TryGetValue(perkId, out perkRule)) return Fail("perk_unknown:" + perkId, out error);
                int skillValue;
                if (!draft.Skills.TryGetValue(perkRule.SkillId ?? string.Empty, out skillValue) ||
                    skillValue < perkRule.RequiredSkillValue)
                    return Fail("perk_skill_requirement_mismatch:" + perkId, out error);
                if (!string.IsNullOrWhiteSpace(perkRule.AlternativePerkId) &&
                    draft.PerkIds.Contains(perkRule.AlternativePerkId, StringComparer.Ordinal))
                    return Fail("perk_alternative_conflict:" + perkId, out error);
            }
            if (draft.TraitLevels == null || draft.TraitLevels.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value < -2 || p.Value > 2))
                return Fail("traits_invalid", out error);
            if (draft.TraitLevels.Count > 0) return Fail("traits_not_supported", out error);

            error = string.Empty;
            return true;
        }

        private static bool ValidateExactKeys(Dictionary<string, int> values, List<string> allowedKeys, out string error)
        {
            if (values == null || values.Count != allowedKeys.Count)
                return Fail("stat_key_count_mismatch", out error);
            foreach (string key in allowedKeys)
            {
                if (!values.ContainsKey(key)) return Fail("stat_key_missing:" + key, out error);
            }
            if (values.Keys.Any(k => !allowedKeys.Contains(k, StringComparer.Ordinal)))
                return Fail("stat_key_unknown", out error);
            error = string.Empty;
            return true;
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }

    public static class CoopHeroCreationHash
    {
        public static string ComputeCanonicalJsonHash(object value)
        {
            JToken token = value == null ? JValue.CreateNull() : JToken.FromObject(value);
            return ComputeSha256(Canonicalize(token).ToString(Formatting.None));
        }

        public static string ComputeSha256(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                StringBuilder result = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes) result.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }

        private static JToken Canonicalize(JToken token)
        {
            JObject obj = token as JObject;
            if (obj != null)
            {
                JObject sorted = new JObject();
                foreach (JProperty property in obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                    sorted.Add(property.Name, Canonicalize(property.Value));
                return sorted;
            }
            JArray array = token as JArray;
            if (array != null) return new JArray(array.Select(Canonicalize));
            return token.DeepClone();
        }
    }
}
