using System;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.Localization.TextProcessor;

namespace CoopSpectator.Campaign
{
    internal static class CoopCampaignMapTooltipLanguageState
    {
        private static readonly FieldInfo ProcessorField = typeof(MBTextManager).GetField(
            "_languageProcessor", BindingFlags.Static | BindingFlags.NonPublic);

        internal static bool TryBegin(int ownerThread, out CoopCampaignMapTooltipThreadStateScope scope)
        {
            scope = null;
            Type type = ProcessorField?.GetValue(null)?.GetType();
            if (type == null || type.Assembly != typeof(MBTextManager).Assembly ||
                type.BaseType != typeof(LanguageSpecificTextProcessor)) return false;
            string[] expected;
            switch (type.FullName)
            {
                case "TaleWorlds.Localization.TextProcessor.DefaultTextProcessor":
                case "TaleWorlds.Localization.TextProcessor.LanguageProcessors.EnglishTextProcessor":
                    expected = Array.Empty<string>(); break;
                case "TaleWorlds.Localization.TextProcessor.LanguageProcessors.FrenchTextProcessor":
                    expected = new[] { "_wordGroups", "_curGender", "_isPlural" }; break;
                case "TaleWorlds.Localization.TextProcessor.LanguageProcessors.ItalianTextProcessor":
                    expected = new[] { "_wordGroups", "_curGender" }; break;
                case "TaleWorlds.Localization.TextProcessor.LanguageProcessors.GermanTextProcessor":
                case "TaleWorlds.Localization.TextProcessor.LanguageProcessors.PolishTextProcessor":
                case "TaleWorlds.Localization.TextProcessor.LanguageProcessors.RussianTextProcessor":
                    expected = new[] { "_wordGroups", "_curGender", "_wordGroupsNoTags", "_linkList", "_doesComeFromWordGroup" }; break;
                case "TaleWorlds.Localization.TextProcessor.LanguageProcessors.SpanishTextProcessor":
                    expected = new[] { "_curGender" }; break;
                case "TaleWorlds.Localization.TextProcessor.LanguageProcessors.TurkishTextProcessor":
                    expected = new[] { "_linkList" }; break;
                default: return false;
            }
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
            FieldInfo[] fields = type.GetFields(flags).Where(f => f.IsDefined(typeof(ThreadStaticAttribute), false)).ToArray();
            if (fields.Length != expected.Length || expected.Any(name => fields.All(f => f.Name != name))) return false;
            FieldInfo[] baseFields = typeof(LanguageSpecificTextProcessor).GetFields(flags)
                .Where(f => f.IsDefined(typeof(ThreadStaticAttribute), false)).ToArray();
            if (baseFields.Length != 1 || baseFields[0].Name != "_lowerMarkers") return false;
            return CoopCampaignMapTooltipThreadStateScope.TryBegin(fields.Concat(baseFields).ToArray(), ownerThread, out scope);
        }

        // These are the game templates directly assigned local variables by the
        // audited five native map providers and their troop/ship, party-size,
        // duration and army-behavior helpers.
        // CopyTextObject shares a nonnull EMPTY attribute dictionary; refuse
        // that shape. Nonempty dictionaries are copied before direct assignment.
        internal static bool AreWritableTemplatesIsolated()
        {
            return AreTemplatesIsolated(new[]
            {
                "str_map_tooltip_info", "str_NUMBER_percent", "str_men_count_in_paranthesis_wo_wounded",
                "str_party_health", "str_hours", "str_days_hours", "str_days",
                "str_army_going_to_settlement", "str_army_waiting_in_settlement", "str_army_gathering",
                "str_army_besieging", "str_army_besieging_travelling", "str_army_patrolling_travelling",
                "str_army_raiding", "str_army_raiding_travelling", "str_army_defending_travelling", "str_army_defending"
            });
        }

        // Audited installed RefreshItemTooltip: flag concatenation and banner
        // percent formatting assign variables to these GameTexts copies.
        // ItemHelper damage descriptions allocate a new TextObject instead.
        // Global RANK/NUMBER writes still require the surrounding text transaction.
        // Separate from map admission so missing item templates cannot disable
        // ordinary settlement/party tooltips. Not yet a live item capture route.
        internal static bool AreItemWritableTemplatesIsolated()
        {
            return AreTemplatesIsolated(new[] { "str_STR1_space_STR2", "str_NUMBER_percent" });
        }

        internal static bool AreRundownWritableTemplatesIsolated() => AreTemplatesIsolated(new[]
            { "str_LEFT_colon_RIGHT_wSpaceAfterColon", "str_map_tooltip_info", "str_finance_trade_agreement_income" });

        internal static bool AreBarWritableTemplatesIsolated() => AreWritableTemplatesIsolated() &&
            AreItemWritableTemplatesIsolated() && AreTemplatesIsolated(new[] { "str_LEFT_over_RIGHT_no_space" });

        // Native navigation tooltip getters assign TEXT/HOTKEY on this copied template.
        internal static bool AreControlWritableTemplatesIsolated() =>
            AreTemplatesIsolated(new[] { "str_hotkey_with_hint" });

        internal static bool AreSettlementWritableTemplatesIsolated(bool hero)
        {
            if (!AreWritableTemplatesIsolated()) return false;
            if (!hero) return true;
            // Installed GetHeroKingdomRank writes HERO to a copied variation;
            // a non-null empty source dictionary would be shared by CopyTextObject.
            foreach (var entry in new[] { new[] { "str_hero_rank_of_faction", "0" }, new[] { "str_hero_rank_of_faction", "1" },
                new[] { "str_hero_rank_of_faction_mercenary", (string)null }, new[] { "str_hero_rank_of_faction_nobleman", (string)null } })
            {
                var text = GameTexts.FindText(entry[0], entry[1]);
                if (text == null || (text.Attributes != null && text.Attributes.Count == 0)) return false;
            }
            return true;
        }

        internal static bool AreAutoBattleWritableTemplatesIsolated() => AreTemplatesIsolated(new[] { "str_plus_with_number" });

        private static bool AreTemplatesIsolated(string[] ids)
        {
            var manager = Game.Current?.GameTextManager;
            if (manager == null) return false;
            foreach (string id in ids)
            {
                TextObject template = manager.GetGameText(id)?.DefaultText;
                if (template == null || (template.Attributes != null && template.Attributes.Count == 0)) return false;
            }
            return true;
        }
    }
}
