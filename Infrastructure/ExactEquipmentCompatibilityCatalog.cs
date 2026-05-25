using System;
using System.Collections.Generic;
using System.Linq;

namespace CoopSpectator.Infrastructure
{
    internal enum ExactEquipmentCompatibilityEquivalenceKind
    {
        FallbackStandIn = 0,
        ExactEquivalent = 1
    }

    internal enum ExactEquipmentCompatibilityDeliveryMode
    {
        AllowCreateAgent = 0,
        ForcePostCreateStringIdOverlay = 1
    }

    internal sealed class ExactEquipmentCompatibilityRule
    {
        internal ExactEquipmentCompatibilityRule(
            string resolvedItemId,
            ExactEquipmentCompatibilityEquivalenceKind equivalenceKind,
            ExactEquipmentCompatibilityDeliveryMode deliveryMode)
        {
            ResolvedItemId = resolvedItemId;
            EquivalenceKind = equivalenceKind;
            DeliveryMode = deliveryMode;
        }

        internal string ResolvedItemId { get; }

        internal ExactEquipmentCompatibilityEquivalenceKind EquivalenceKind { get; }

        internal ExactEquipmentCompatibilityDeliveryMode DeliveryMode { get; }

        internal bool RequiresSyntheticPreload =>
            !string.IsNullOrWhiteSpace(ResolvedItemId) &&
            ResolvedItemId.StartsWith("cs_exact_", StringComparison.OrdinalIgnoreCase);
    }

    internal static class ExactEquipmentCompatibilityCatalog
    {
        internal static readonly IReadOnlyDictionary<string, ExactEquipmentCompatibilityRule> AliasRules =
            new Dictionary<string, ExactEquipmentCompatibilityRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["pointed_skullcap_over_mail_coif"] = ExactSynthetic("cs_exact_pointed_skullcap_over_mail_coif"),
                ["sling_braided"] = ExactSynthetic("cs_exact_sling_braided"),
                ["lordly_padded_mitten"] = ExactSynthetic("cs_exact_lordly_padded_mitten"),
                ["ladys_shoe"] = ExactSynthetic("cs_exact_ladys_shoe"),
                ["steel_druzhinnik_kite_shield"] = ExactSynthetic("cs_exact_steel_druzhinnik_kite_shield"),
                ["northern_spear_4_t5"] = ExactSynthetic("cs_exact_northern_spear_4_t5"),
                ["nordic_sloven"] = ExactSynthetic("cs_exact_nordic_sloven"),
                ["sturgia_infantry_shield_a"] = ExactSynthetic("cs_exact_sturgia_infantry_shield_a"),
                ["storm_charger"] = ExactSynthetic("cs_exact_storm_charger"),
                ["nomad_cap"] = ExactSynthetic("cs_exact_nomad_cap"),
                ["studded_leather_waistcoat"] = ExactSynthetic("cs_exact_studded_leather_waistcoat"),
                ["southern_spear_4_t3"] = ExactSynthetic("cs_exact_southern_spear_4_t3"),
                ["large_adarga"] = ExactSynthetic("cs_exact_large_adarga"),
                ["studded_adarga"] = ExactSynthetic("cs_exact_studded_adarga"),
                ["southern_throwing_axe_1_t4"] = ExactSynthetic("cs_exact_southern_throwing_axe_1_t4"),
                ["small_heater_shield"] = ExactSynthetic("cs_exact_small_heater_shield"),
                ["scale_shoulder_armor"] = ExactSynthetic("cs_exact_scale_shoulder_armor"),
                ["peasant_hammer_1_t1"] = ExactSynthetic("cs_exact_peasant_hammer_1_t1"),
                ["peasant_maul_t1_2"] = ExactSynthetic("cs_exact_peasant_maul_t1_2"),
                ["northern_2hsword_t4"] = ExactSynthetic("cs_exact_northern_2hsword_t4"),
                ["empire_polearm_1_t4"] = ExactOverlayEquivalent("mp_empire_menavlion"),
                ["imperial_spear_t2"] = ExactOverlayEquivalent("cs_exact_imperial_spear_t2"),
                ["imperial_throwing_spear_1_t4"] = ExactOverlayEquivalent("mp_pilum"),
                ["imperial_throwing_spear_1_t4_2"] = ExactOverlayEquivalent("mp_pilum_extraammo"),
                ["peasant_pitchfork_2_t1"] = FallbackStandIn("mp_western_pitchfork_wood"),
                ["seax"] = FallbackStandIn("mp_default_dagger"),
                ["torn_bandit_clothes"] = FallbackStandIn("mp_vlandia_bandit_c"),
                ["peasant_hammer_2_t1"] = ExactSynthetic("cs_exact_peasant_hammer_2_t1"),
                ["western_javelin_2_t3"] = ExactSynthetic("cs_exact_western_javelin_2_t3"),
                ["bolted_leather_strips"] = ExactSynthetic("cs_exact_bolted_leather_strips"),
                ["bolt_c"] = ExactSynthetic("cs_exact_bolt_c"),
                ["bolt_d"] = ExactSynthetic("cs_exact_bolt_d"),
                ["bolt_e"] = ExactSynthetic("cs_exact_bolt_e"),
                ["crossbow_c"] = ExactSynthetic("cs_exact_crossbow_c"),
                ["nordic_shortbow"] = ExactSynthetic("cs_exact_nordic_shortbow"),
                ["southern_spear_3_t3"] = ExactSynthetic("cs_exact_southern_spear_3_t3"),
                ["southern_spear_3_t4"] = ExactSynthetic("cs_exact_southern_spear_3_t4"),
                ["sumpter_horse"] = ExactSynthetic("cs_exact_sumpter_horse"),
                ["tournament_arrows"] = ExactSynthetic("cs_exact_tournament_arrows"),
                ["tribal_bow"] = ExactSynthetic("cs_exact_tribal_bow")
            };

        internal static readonly IReadOnlyDictionary<string, string> AliasItemIds =
            AliasRules.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.ResolvedItemId,
                StringComparer.OrdinalIgnoreCase);

        internal static bool TryGetAliasItemId(string sourceItemId, out string aliasItemId)
        {
            aliasItemId = null;
            if (!TryGetAliasRule(sourceItemId, out ExactEquipmentCompatibilityRule aliasRule))
                return false;

            aliasItemId = aliasRule.ResolvedItemId;
            return !string.IsNullOrWhiteSpace(aliasItemId);
        }

        internal static bool TryGetAliasRule(string sourceItemId, out ExactEquipmentCompatibilityRule aliasRule)
        {
            aliasRule = null;
            if (string.IsNullOrWhiteSpace(sourceItemId))
                return false;

            return AliasRules.TryGetValue(sourceItemId.Trim(), out aliasRule) &&
                   aliasRule != null &&
                   !string.IsNullOrWhiteSpace(aliasRule.ResolvedItemId);
        }

        internal static bool IsExactEquivalentAlias(string sourceItemId, string resolvedItemId)
        {
            return
                TryGetAliasRule(sourceItemId, out ExactEquipmentCompatibilityRule aliasRule) &&
                string.Equals(aliasRule.ResolvedItemId, resolvedItemId, StringComparison.OrdinalIgnoreCase) &&
                aliasRule.EquivalenceKind == ExactEquipmentCompatibilityEquivalenceKind.ExactEquivalent;
        }

        internal static bool RequiresPostCreateStringIdOverlay(string sourceItemId, string resolvedItemId)
        {
            return
                TryGetAliasRule(sourceItemId, out ExactEquipmentCompatibilityRule aliasRule) &&
                string.Equals(aliasRule.ResolvedItemId, resolvedItemId, StringComparison.OrdinalIgnoreCase) &&
                aliasRule.DeliveryMode == ExactEquipmentCompatibilityDeliveryMode.ForcePostCreateStringIdOverlay;
        }

        internal static IEnumerable<string> EnumerateSyntheticAliasItemIds(IEnumerable<string> sourceItemIds)
        {
            if (sourceItemIds == null)
                yield break;

            var emittedAliasIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string sourceItemId in sourceItemIds.Where(itemId => !string.IsNullOrWhiteSpace(itemId)))
            {
                if (!TryGetAliasRule(sourceItemId, out ExactEquipmentCompatibilityRule aliasRule) ||
                    !aliasRule.RequiresSyntheticPreload ||
                    !emittedAliasIds.Add(aliasRule.ResolvedItemId))
                {
                    continue;
                }

                yield return aliasRule.ResolvedItemId;
            }
        }

        private static ExactEquipmentCompatibilityRule ExactSynthetic(string resolvedItemId)
        {
            return new ExactEquipmentCompatibilityRule(
                resolvedItemId,
                ExactEquipmentCompatibilityEquivalenceKind.ExactEquivalent,
                ExactEquipmentCompatibilityDeliveryMode.AllowCreateAgent);
        }

        private static ExactEquipmentCompatibilityRule ExactOverlayEquivalent(string resolvedItemId)
        {
            return new ExactEquipmentCompatibilityRule(
                resolvedItemId,
                ExactEquipmentCompatibilityEquivalenceKind.ExactEquivalent,
                ExactEquipmentCompatibilityDeliveryMode.ForcePostCreateStringIdOverlay);
        }

        private static ExactEquipmentCompatibilityRule FallbackStandIn(string resolvedItemId)
        {
            return new ExactEquipmentCompatibilityRule(
                resolvedItemId,
                ExactEquipmentCompatibilityEquivalenceKind.FallbackStandIn,
                ExactEquipmentCompatibilityDeliveryMode.AllowCreateAgent);
        }
    }
}
