using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.ViewModelCollection.Conversation;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapConversation;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Campaign
{
    /// <summary>
    /// Reads the already formatted native map-conversation view model on the
    /// campaign host. It never advances the conversation or selects an option.
    /// </summary>
    internal static class CoopCampaignMapConversationCapture
    {
        private const string NativeDataSourceFieldName = "_dataSource";
        private static Type _cachedOwnerType;
        private static FieldInfo _cachedDataSourceField;

        public static bool TryCapture(
            out CoopCampaignMapConversationState state,
            out CoopCampaignMapConversationPose pose,
            out string reason)
        {
            state = CoopCampaignMapPrototypeContract.CreateInactiveConversation();
            pose = null;
            reason = null;
            try
            {
                TaleWorlds.CampaignSystem.Campaign campaign =
                    TaleWorlds.CampaignSystem.Campaign.Current;
                ConversationManager manager = campaign?.ConversationManager;
                if (manager == null || !manager.IsConversationInProgress)
                {
                    CoopCampaignMapMissionConversationVisualCapture.Reset();
                    return true;
                }

                MapConversationView conversationView = null;
                MapConversationVM dataSource = null;
                TaleWorlds.MountAndBlade.View.MissionViews.MissionView standaloneView = null;
                MissionConversationVM dialog;
                if (Mission.Current != null)
                {
                    // Read only the existing standalone mission VM. Barter,
                    // loading and teardown publish inactive, never the old line.
                    if (!CoopCampaignMapMissionConversationContext.TryGetActiveView(
                            Mission.Current, manager.Handler, out var missionView))
                    { CoopCampaignMapMissionConversationVisualCapture.Reset(); return true; }
                    standaloneView = missionView;
                    dialog = ResolveDataSource(missionView) as MissionConversationVM;
                }
                else
                {
                    CoopCampaignMapMissionConversationVisualCapture.Reset();
                    conversationView = MapScreen.Instance?.GetMapView<MapConversationView>();
                    if (conversationView == null || !conversationView.IsConversationActive)
                    {
                        reason = "native-view-not-active";
                        return false;
                    }
                    object owner = (object)manager.Handler ?? conversationView;
                    dataSource = ResolveDataSource(owner) as MapConversationVM;
                    dialog = dataSource?.DialogController;
                }
                if (dialog == null || !dialog.IsLoadingOver)
                {
                    reason = "native-dialog-not-ready";
                    return false;
                }

                // Trading is outside the current milestone. Hide the dialogue
                // while the native barter panel owns the host's map UI.
                if (dataSource != null && dataSource.IsBarterActive)
                    return true;

                var captured = new CoopCampaignMapConversationState
                {
                    Revision = 0,
                    IsActive = true,
                    SpeakerName = Bound(
                        dialog.CurrentCharacterNameLbl,
                        CoopCampaignMapPrototypeContract
                            .MaxConversationSpeakerCharacters),
                    DialogText = Bound(
                        dialog.DialogText,
                        CoopCampaignMapPrototypeContract
                            .MaxConversationDialogCharacters),
                    GoldText = Bound(
                        dialog.GoldText,
                        CoopCampaignMapPrototypeContract
                            .MaxConversationGoldCharacters),
                    ContinueText = Bound(
                        dialog.ContinueText,
                        CoopCampaignMapPrototypeContract
                            .MaxConversationContinueCharacters)
                };

                int optionCount = dialog.AnswerList?.Count ?? 0;
                if (optionCount >
                    CoopCampaignMapPrototypeContract.MaxConversationOptions)
                {
                    reason = "native-option-count";
                    return false;
                }
                for (int index = 0; index < optionCount; index++)
                {
                    ConversationItemVM option = dialog.AnswerList[index];
                    if (option == null)
                    {
                        reason = "native-option-missing";
                        return false;
                    }
                    captured.Options.Add(
                        new CoopCampaignMapConversationOptionState
                        {
                            Text = Bound(
                                option.ItemText,
                                CoopCampaignMapPrototypeContract
                                    .MaxConversationOptionCharacters),
                            IsEnabled = option.IsEnabled,
                            IsSpecial = option.IsSpecial
                        });
                }

                // Optional visual projection: unavailable native presentation does
                // not suppress the already valid read-only text dialogue.
                try { captured.Hud = CaptureHud(dialog); }
                catch (Exception) { captured.Hud = null; }
                if (conversationView != null &&
                    CoopCampaignMapConversationTableauCapture.TryCapture(conversationView, out var tableau, out pose))
                    captured.Tableau = tableau;
                else if (standaloneView != null && CoopCampaignMapMissionConversationVisualCapture.TryCapture(
                    standaloneView, out var missionTableau, out pose)) captured.Tableau = missionTableau;

                if (!CoopCampaignMapPrototypeContract.TryValidateConversationState(
                        captured,
                        out reason))
                {
                    return false;
                }

                state = captured;
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ":" + ex.Message;
                state = CoopCampaignMapPrototypeContract.CreateInactiveConversation();
                pose = null;
                return false;
            }
        }

        private static CoopCampaignMapConversationHud CaptureHud(MissionConversationVM dialog)
        {
            // Read the prepared native presentation, NOT campaign strength/quest
            // objects. In particular native 0/1 hidden-information powers survive.
            var hud = new CoopCampaignMapConversationHud();
            if (dialog.IsAggressive && dialog.PowerComparer != null &&
                dialog.AttackerParties != null && dialog.DefenderParties != null &&
                dialog.AttackerParties.Count <= CoopConversationHudCodec.MaxPartiesPerSide &&
                dialog.DefenderParties.Count <= CoopConversationHudCodec.MaxPartiesPerSide)
            {
                hud.IsAggressive = true;
                hud.AttackerLeader = CaptureParty(dialog.AttackerLeader);
                hud.DefenderLeader = CaptureParty(dialog.DefenderLeader);
                foreach (var party in dialog.AttackerParties) hud.AttackerParties.Add(CaptureParty(party));
                foreach (var party in dialog.DefenderParties) hud.DefenderParties.Add(CaptureParty(party));
                var power = dialog.PowerComparer;
                hud.AttackerPower = power.AttackerBattlePowerValue;
                hud.DefenderPower = power.DefenderBattlePowerValue;
                hud.InitialAttackerPower = power.InitialAttackerBattlePowerValue;
                hud.InitialDefenderPower = power.InitialDefenderBattlePowerValue;
                hud.AttackerColor = power.AttackerColor;
                hud.DefenderColor = power.DefenderColor;
            }
            if (dialog.IsBannerEnabled && dialog.ConversedHeroBanner != null &&
                dialog.ConversedHeroBanner.TextureProviderName == "BannerImageTextureProvider" &&
                (dialog.ConversedHeroBanner.AdditionalArgs == string.Empty || dialog.ConversedHeroBanner.AdditionalArgs == "ninegrid"))
            {
                hud.IsBannerEnabled = true;
                hud.BannerCode = dialog.ConversedHeroBanner.Id ?? string.Empty;
                hud.BannerNineGrid = dialog.ConversedHeroBanner.AdditionalArgs == "ninegrid";
                hud.FactionHint = Bound(dialog.FactionHint?.HintText?.ToString(), 512);
            }
            // Unsupported presentation is optional; never suppress valid text.
            return (hud.IsAggressive || hud.IsBannerEnabled) && CoopConversationHudCodec.IsValid(hud) ? hud : null;
        }

        private static CoopConversationHudParty CaptureParty(ConversationAggressivePartyItemVM party)
        {
            if (party == null || (party.Quests?.Count ?? 0) > 2) return null;
            var image = party.LeaderVisual;
            if (image != null && (image.TextureProviderName != "CharacterImageTextureProvider" ||
                !string.IsNullOrEmpty(image.AdditionalArgs))) return null;
            var captured = new CoopConversationHudParty
            {
                PortraitCode = image?.Id ?? string.Empty,
                HealthyAmount = party.HealthyAmount
            };
            if (party.Quests != null)
                foreach (var marker in party.Quests)
                {
                    if (marker == null) return null;
                    captured.QuestMarkers.Add(marker.QuestMarkerType);
                }
            return captured;
        }

        private static object ResolveDataSource(object owner)
        {
            if (owner == null)
                return null;

            Type ownerType = owner.GetType();
            if (_cachedOwnerType != ownerType)
            {
                _cachedOwnerType = ownerType;
                _cachedDataSourceField = FindInstanceField(
                    ownerType,
                    NativeDataSourceFieldName);
            }
            return _cachedDataSourceField?.GetValue(owner);
        }

        private static FieldInfo FindInstanceField(Type type, string fieldName)
        {
            for (Type current = type;
                 current != null;
                 current = current.BaseType)
            {
                FieldInfo field = current.GetField(
                    fieldName,
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }
            return null;
        }

        private static string Bound(string value, int maximumCharacters)
        {
            return CoopCampaignMapPrototypeContract.BoundConversationText(
                value,
                maximumCharacters);
        }
    }
}
