using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using CampaignSystemRuntime = TaleWorlds.CampaignSystem.Campaign;

namespace CoopSpectator.Campaign
{
    internal static class CoopPlayerHeroFactory
    {
        public static bool TryCreateCompanion(CoopHeroDraft draft, CoopHeroCreationRules rules, out Hero hero, out string error)
        {
            hero = null;
            if (!CoopHeroCreationContract.ValidateDraft(draft, rules, out error)) return false;
            if (CampaignSystemRuntime.Current == null || Clan.PlayerClan == null || MobileParty.MainParty == null)
            {
                error = "campaign_context_missing";
                return false;
            }

            CultureObject culture = MBObjectManager.Instance?.GetObject<CultureObject>(draft.CultureId);
            if (culture == null) { error = "culture_object_missing:" + draft.CultureId; return false; }
            CharacterObject template = CharacterObject.All.FirstOrDefault(c =>
                c != null && c.IsTemplate && c.Occupation == Occupation.Wanderer && c.Culture == culture);
            if (template == null)
                template = CharacterObject.All.FirstOrDefault(c => c != null && c.IsTemplate && c.Occupation == Occupation.Wanderer);
            if (template == null) { error = "wanderer_template_missing"; return false; }

            BodyProperties body;
            if (!BodyProperties.FromString(draft.BodyProperties, out body))
            {
                error = "body_properties_parse_failed";
                return false;
            }

            try
            {
                if (Clan.PlayerClan.Companions.Count >= Clan.PlayerClan.CompanionLimit)
                    ModLogger.Info("CoopPlayerHeroFactory: player clan companion limit is already reached; native companion action remains authoritative.");
                Settlement bornSettlement = Clan.PlayerClan.HomeSettlement ?? Settlement.All.FirstOrDefault(s => s.Culture == culture);
                hero = HeroCreator.CreateSpecialHero(template, bornSettlement, null, null, draft.Age);
                if (hero == null) { error = "hero_creator_returned_null"; return false; }

                TextObject name = new TextObject("{=!}" + draft.Name.Trim());
                hero.SetName(name, name);
                hero.IsFemale = draft.IsFemale;
                hero.StaticBodyProperties = body.StaticProperties;
                hero.Weight = body.Weight;
                hero.Build = body.Build;
                hero.SetBirthDay(CampaignTime.YearsFromNow(-draft.Age));
                hero.ChangeState(Hero.CharacterStates.Active);
                hero.SetHasMet();

                ApplyDevelopment(hero, draft);
                AddCompanionAction.Apply(Clan.PlayerClan, hero);
                AddHeroToPartyAction.Apply(hero, MobileParty.MainParty, true);
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        public static void TryRepairExistingCompanion(Hero hero)
        {
            if (hero == null || hero.IsDead || hero.IsDisabled || hero.IsPrisoner) return;
            try
            {
                if (hero.CompanionOf == null)
                    AddCompanionAction.Apply(Clan.PlayerClan, hero);
                if (hero.PartyBelongedTo == null && hero.PartyBelongedToAsPrisoner == null && hero.StayingInSettlement == null)
                    AddHeroToPartyAction.Apply(hero, MobileParty.MainParty, true);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopPlayerHeroFactory: existing companion wiring repair failed. HeroId=" + hero.StringId + " Error=" + ex.Message);
            }
        }

        private static void ApplyDevelopment(Hero hero, CoopHeroDraft draft)
        {
            HeroDeveloper developer = hero.HeroDeveloper;
            developer.ClearHero();
            hero.Level = 1;
            developer.SetInitialLevel(hero.Level);

            Dictionary<string, CharacterAttribute> attributes = new Dictionary<string, CharacterAttribute>(StringComparer.Ordinal)
            {
                ["Vigor"] = DefaultCharacterAttributes.Vigor,
                ["Control"] = DefaultCharacterAttributes.Control,
                ["Endurance"] = DefaultCharacterAttributes.Endurance,
                ["Cunning"] = DefaultCharacterAttributes.Cunning,
                ["Social"] = DefaultCharacterAttributes.Social,
                ["Intelligence"] = DefaultCharacterAttributes.Intelligence
            };
            foreach (KeyValuePair<string, int> value in draft.Attributes)
                developer.AddAttribute(attributes[value.Key], value.Value, false);

            Dictionary<string, SkillObject> skills = BuildSkillMap();
            foreach (KeyValuePair<string, int> value in draft.Skills)
                developer.SetInitialSkillLevel(skills[value.Key], value.Value);
            foreach (KeyValuePair<string, int> value in draft.Focus)
            {
                if (value.Value > 0) developer.AddFocus(skills[value.Key], value.Value, false);
            }
            foreach (string perkId in draft.PerkIds)
            {
                PerkObject perk = MBObjectManager.Instance?.GetObject<PerkObject>(perkId);
                if (perk != null) developer.AddPerk(perk);
            }
            developer.ClearUnspentPoints();
        }

        internal static Dictionary<string, SkillObject> BuildSkillMap()
        {
            return new Dictionary<string, SkillObject>(StringComparer.Ordinal)
            {
                ["OneHanded"] = DefaultSkills.OneHanded,
                ["TwoHanded"] = DefaultSkills.TwoHanded,
                ["Polearm"] = DefaultSkills.Polearm,
                ["Bow"] = DefaultSkills.Bow,
                ["Crossbow"] = DefaultSkills.Crossbow,
                ["Throwing"] = DefaultSkills.Throwing,
                ["Riding"] = DefaultSkills.Riding,
                ["Athletics"] = DefaultSkills.Athletics,
                ["Smithing"] = DefaultSkills.Crafting,
                ["Scouting"] = DefaultSkills.Scouting,
                ["Tactics"] = DefaultSkills.Tactics,
                ["Roguery"] = DefaultSkills.Roguery,
                ["Charm"] = DefaultSkills.Charm,
                ["Leadership"] = DefaultSkills.Leadership,
                ["Trade"] = DefaultSkills.Trade,
                ["Steward"] = DefaultSkills.Steward,
                ["Medicine"] = DefaultSkills.Medicine,
                ["Engineering"] = DefaultSkills.Engineering
            };
        }
    }
}
