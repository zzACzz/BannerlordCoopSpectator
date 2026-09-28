using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.ViewModelCollection.Conversation;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapConversation;

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
                    return true;

                MapScreen mapScreen = MapScreen.Instance;
                MapConversationView conversationView =
                    mapScreen?.GetMapView<MapConversationView>();
                if (conversationView == null ||
                    !conversationView.IsConversationActive)
                {
                    reason = "native-view-not-active";
                    return false;
                }

                object owner = (object)manager.Handler ?? conversationView;
                MapConversationVM dataSource = ResolveDataSource(owner);
                MissionConversationVM dialog = dataSource?.DialogController;
                if (dataSource == null || dialog == null || !dialog.IsLoadingOver)
                {
                    reason = "native-dialog-not-ready";
                    return false;
                }

                // Trading is outside the current milestone. Hide the dialogue
                // while the native barter panel owns the host's map UI.
                if (dataSource.IsBarterActive)
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
                if (CoopCampaignMapConversationTableauCapture.TryCapture(conversationView, out var tableau, out pose))
                    captured.Tableau = tableau;

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

        private static MapConversationVM ResolveDataSource(object owner)
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
            return _cachedDataSourceField?.GetValue(owner) as MapConversationVM;
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
