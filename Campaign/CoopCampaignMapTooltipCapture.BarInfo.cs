using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapBar;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace CoopSpectator.Campaign
{
    internal sealed partial class CoopCampaignMapTooltipCapture
    {
        private static bool IsCurrentBarInfo(MapInfoItemVM source, string session, string token) =>
            source?.GetType() == typeof(MapInfoItemVM) &&
            CoopCampaignMapBarCapture.TryResolveCurrentInfoHint(session, token, out var current) && ReferenceEquals(source, current);

        private static List<TooltipProperty> CaptureBarInfoRows(string id)
        {
            switch (id)
            {
                case "influence": return CampaignUIHelper.GetInfluenceTooltip(Clan.PlayerClan);
                case "hit_points": return CampaignUIHelper.GetPlayerHitpointsTooltip();
                case "troops": return CampaignUIHelper.GetMainPartyHealthTooltip();
                case "food": return CampaignUIHelper.GetPartyFoodTooltip(MobileParty.MainParty);
                case "morale": return CampaignUIHelper.GetPartyMoraleTooltip(MobileParty.MainParty);
                case "view_distance": return CampaignUIHelper.GetViewDistanceTooltip();
                case "troop_wage": return CampaignUIHelper.GetPartyWageTooltip(MobileParty.MainParty);
                case "speed": return CaptureSpeedWithoutInspectionEvent();
                default: return null;
            }
        }

        private static List<TooltipProperty> CaptureSpeedWithoutInspectionEvent()
        {
            // Exact installed GetPartySpeedTooltip(true) presentation branch;
            // omit only PlayerInspectedPartySpeedEvent, not host-native hover behavior.
            if (Hero.MainHero.IsPrisoner)
                return new List<TooltipProperty> { new TooltipProperty(string.Empty,
                    GameTexts.FindText("str_main_hero_is_imprisoned").ToString(), 0) };
            var title = typeof(CampaignUIHelper).GetField("_partySpeedStr", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as TextObject;
            if (title == null) return null;
            var party = MobileParty.MainParty.Army?.LeaderParty ?? MobileParty.MainParty;
            ExplainedNumber speed = party.SpeedExplained;
            return CampaignUIHelper.GetTooltipForAccumulatingPropertyWithResult(title.ToString(), speed.ResultNumber, ref speed);
        }
    }
}
