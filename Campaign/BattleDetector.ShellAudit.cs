using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TaleWorlds.Core;
using CoopSpectator.Infrastructure;

namespace CoopSpectator.Campaign
{
    public sealed partial class BattleDetector
    {
        private const string CampaignShellAuditFileName = "campaign_shell_audit_latest.json";

        private sealed class CampaignShellAuditReport
        {
            public string GeneratedAtUtc { get; set; }

            public int EligibleTroopCount { get; set; }

            public int VariantCount { get; set; }

            public int CurrentShellGroupCount { get; set; }

            public int SuggestedShellGroupCount { get; set; }

            public int AmbiguousCurrentShellCount { get; set; }

            public List<CampaignShellAuditVariantRecord> Variants { get; set; } = new List<CampaignShellAuditVariantRecord>();

            public List<CampaignShellAuditCurrentShellGroup> CurrentShellGroups { get; set; } = new List<CampaignShellAuditCurrentShellGroup>();

            public List<CampaignShellAuditSuggestedShellGroup> SuggestedShellGroups { get; set; } = new List<CampaignShellAuditSuggestedShellGroup>();
        }

        private sealed class CampaignShellAuditVariantRecord
        {
            public string CharacterId { get; set; }

            public string CultureId { get; set; }

            public int Tier { get; set; }

            public string DefaultFormationClass { get; set; }

            public int VariantIndex { get; set; }

            public int VariantCount { get; set; }

            public string VariantSignature { get; set; }

            public string CurrentShellTemplateId { get; set; }

            public string RuntimeSignatureKey { get; set; }

            public bool IsMounted { get; set; }

            public bool HasShield { get; set; }

            public bool HasThrown { get; set; }

            public string RangedFamily { get; set; }

            public string MeleeFamily { get; set; }

            public string TwoHandedSubtype { get; set; }

            public string PrimaryWeaponClass { get; set; }

            public string PrimaryRangedWeaponClass { get; set; }

            public string PrimaryMeleeWeaponClass { get; set; }

            public string SecondaryMeleeWeaponClass { get; set; }

            public string CombatItem0Id { get; set; }

            public int? CombatItem0Amount { get; set; }

            public string CombatItem1Id { get; set; }

            public int? CombatItem1Amount { get; set; }

            public string CombatItem2Id { get; set; }

            public int? CombatItem2Amount { get; set; }

            public string CombatItem3Id { get; set; }

            public int? CombatItem3Amount { get; set; }

            public string CombatHorseId { get; set; }

            public string CombatHorseHarnessId { get; set; }
        }

        private sealed class CampaignShellAuditCurrentShellGroup
        {
            public string ShellTemplateId { get; set; }

            public int VariantCount { get; set; }

            public List<string> RuntimeSignatureKeys { get; set; } = new List<string>();

            public List<string> TroopIds { get; set; } = new List<string>();
        }

        private sealed class CampaignShellAuditSuggestedShellGroup
        {
            public string RuntimeSignatureKey { get; set; }

            public int VariantCount { get; set; }

            public List<string> CurrentShellTemplateIds { get; set; } = new List<string>();

            public List<string> TroopIds { get; set; } = new List<string>();
        }

        public static string ExportCampaignShellAudit()
        {
            try
            {
                List<BasicCharacterObject> characters = CollectSyntheticAllCampaignTroops();
                if (characters.Count == 0)
                    return "Campaign shell audit skipped: no eligible campaign troops loaded.";

                List<CampaignShellAuditVariantRecord> variants = BuildCampaignShellAuditVariantRecords(characters);
                var report = new CampaignShellAuditReport
                {
                    GeneratedAtUtc = DateTime.UtcNow.ToString("o"),
                    EligibleTroopCount = characters.Count,
                    VariantCount = variants.Count,
                    Variants = variants,
                    CurrentShellGroups = BuildCurrentShellGroups(variants),
                    SuggestedShellGroups = BuildSuggestedShellGroups(variants)
                };

                report.CurrentShellGroupCount = report.CurrentShellGroups.Count;
                report.SuggestedShellGroupCount = report.SuggestedShellGroups.Count;
                report.AmbiguousCurrentShellCount = report.CurrentShellGroups.Count(group => group.RuntimeSignatureKeys.Count > 1);

                string path = GetCampaignShellAuditFilePath();
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(path, JsonConvert.SerializeObject(report, Formatting.Indented));

                ModLogger.Info(
                    "BattleDetector: wrote campaign shell audit. " +
                    "Path=" + path +
                    " Troops=" + report.EligibleTroopCount +
                    " Variants=" + report.VariantCount +
                    " CurrentShells=" + report.CurrentShellGroupCount +
                    " SuggestedShells=" + report.SuggestedShellGroupCount +
                    " AmbiguousCurrentShells=" + report.AmbiguousCurrentShellCount + ".");

                return
                    "Campaign shell audit exported to " + path +
                    ". Troops=" + report.EligibleTroopCount +
                    " Variants=" + report.VariantCount +
                    " CurrentShells=" + report.CurrentShellGroupCount +
                    " SuggestedShells=" + report.SuggestedShellGroupCount +
                    " AmbiguousCurrentShells=" + report.AmbiguousCurrentShellCount + ".";
            }
            catch (Exception ex)
            {
                ModLogger.Error("BattleDetector: failed to export campaign shell audit.", ex);
                return "Campaign shell audit export failed: " + ex.Message;
            }
        }

        private static List<CampaignShellAuditVariantRecord> BuildCampaignShellAuditVariantRecords(IEnumerable<BasicCharacterObject> characters)
        {
            var records = new List<CampaignShellAuditVariantRecord>();
            foreach (BasicCharacterObject character in characters ?? Enumerable.Empty<BasicCharacterObject>())
            {
                if (character == null || string.IsNullOrWhiteSpace(character.StringId))
                    continue;

                List<CombatEquipmentVariantSnapshot> variants = BuildAllDistinctAuditVariants(character);
                if (variants.Count == 0)
                    continue;

                int variantCount = variants.Count;
                for (int variantIndex = 0; variantIndex < variants.Count; variantIndex++)
                {
                    CombatEquipmentVariantSnapshot variant = variants[variantIndex];
                    CompatibilityShellTemplateResolver.ShellAuditDescriptor descriptor =
                        CompatibilityShellTemplateResolver.ResolveAuditDescriptor(
                            variant.CombatItem0Id,
                            variant.CombatItem1Id,
                            variant.CombatItem2Id,
                            variant.CombatItem3Id,
                            variant.CombatHorseId,
                            TryGetBoolProperty(character, "IsMounted"));

                    records.Add(new CampaignShellAuditVariantRecord
                    {
                        CharacterId = character.StringId,
                        CultureId = TryGetCultureId(character),
                        Tier = TryGetIntProperty(character, "Tier"),
                        DefaultFormationClass = character.DefaultFormationClass.ToString(),
                        VariantIndex = variantIndex + 1,
                        VariantCount = variantCount,
                        VariantSignature = variant.Signature,
                        CurrentShellTemplateId = descriptor?.TroopTemplateId,
                        RuntimeSignatureKey = descriptor?.RuntimeSignatureKey,
                        IsMounted = descriptor?.IsMounted ?? TryGetBoolProperty(character, "IsMounted"),
                        HasShield = descriptor?.HasShield ?? false,
                        HasThrown = descriptor?.HasThrown ?? false,
                        RangedFamily = descriptor?.Ranged.ToString() ?? "Unknown",
                        MeleeFamily = descriptor?.Melee.ToString() ?? "Unknown",
                        TwoHandedSubtype = descriptor?.TwoHandedSubtype ?? "Unknown",
                        PrimaryWeaponClass = descriptor?.PrimaryWeaponClass,
                        PrimaryRangedWeaponClass = descriptor?.PrimaryRangedWeaponClass,
                        PrimaryMeleeWeaponClass = descriptor?.PrimaryMeleeWeaponClass,
                        SecondaryMeleeWeaponClass = descriptor?.SecondaryMeleeWeaponClass,
                        CombatItem0Id = variant.CombatItem0Id,
                        CombatItem0Amount = variant.CombatItem0Amount,
                        CombatItem1Id = variant.CombatItem1Id,
                        CombatItem1Amount = variant.CombatItem1Amount,
                        CombatItem2Id = variant.CombatItem2Id,
                        CombatItem2Amount = variant.CombatItem2Amount,
                        CombatItem3Id = variant.CombatItem3Id,
                        CombatItem3Amount = variant.CombatItem3Amount,
                        CombatHorseId = variant.CombatHorseId,
                        CombatHorseHarnessId = variant.CombatHorseHarnessId
                    });
                }
            }

            return records
                .OrderBy(record => record.CultureId ?? "neutral_culture", StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.Tier)
                .ThenBy(record => record.CharacterId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.VariantIndex)
                .ToList();
        }

        private static List<CampaignShellAuditCurrentShellGroup> BuildCurrentShellGroups(IEnumerable<CampaignShellAuditVariantRecord> variants)
        {
            return (variants ?? Enumerable.Empty<CampaignShellAuditVariantRecord>())
                .GroupBy(record => record.CurrentShellTemplateId ?? "unresolved", StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new CampaignShellAuditCurrentShellGroup
                {
                    ShellTemplateId = group.Key,
                    VariantCount = group.Count(),
                    RuntimeSignatureKeys = group
                        .Select(record => record.RuntimeSignatureKey ?? "unresolved")
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    TroopIds = group
                        .Select(record => record.CharacterId ?? "unknown")
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                        .Take(24)
                        .ToList()
                })
                .ToList();
        }

        private static List<CampaignShellAuditSuggestedShellGroup> BuildSuggestedShellGroups(IEnumerable<CampaignShellAuditVariantRecord> variants)
        {
            return (variants ?? Enumerable.Empty<CampaignShellAuditVariantRecord>())
                .GroupBy(record => record.RuntimeSignatureKey ?? "unresolved", StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new CampaignShellAuditSuggestedShellGroup
                {
                    RuntimeSignatureKey = group.Key,
                    VariantCount = group.Count(),
                    CurrentShellTemplateIds = group
                        .Select(record => record.CurrentShellTemplateId ?? "unresolved")
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    TroopIds = group
                        .Select(record => record.CharacterId ?? "unknown")
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                        .Take(24)
                        .ToList()
                })
                .ToList();
        }

        private static List<CombatEquipmentVariantSnapshot> BuildAllDistinctAuditVariants(BasicCharacterObject character)
        {
            var variants = new List<CombatEquipmentVariantSnapshot>();
            var seenSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (object equipment in EnumerateCharacterEquipments(character))
            {
                if (equipment == null || TryGetBoolProperty(equipment, "IsCivilian"))
                    continue;

                CombatEquipmentVariantSnapshot variant = BuildCombatEquipmentVariantSnapshot(equipment);
                string signature = variant?.Signature ?? string.Empty;
                if (variant == null || !seenSignatures.Add(signature))
                    continue;

                variants.Add(variant);
            }

            if (variants.Count > 0)
                return variants;

            CombatEquipmentVariantSnapshot fallback = BuildCombatEquipmentVariantSnapshot(TryResolvePrimaryCombatEquipment(character));
            if (fallback != null)
                variants.Add(fallback);

            return variants;
        }

        private static string GetCampaignShellAuditFilePath()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string folder = Path.Combine(docs, "Mount and Blade II Bannerlord", "CoopSpectator");
            return Path.Combine(folder, CampaignShellAuditFileName);
        }
    }
}
