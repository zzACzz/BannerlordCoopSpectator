using System;
using System.Reflection;
using System.Threading;
using CoopSpectator.Infrastructure;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapBar;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Localization;
using SandBox.View.Map.Navigation;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.Core.ViewModelCollection.Information;
using SandBox.View.Map.Navigation.NavigationElements;
using TaleWorlds.CampaignSystem;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Campaign
{
    internal sealed partial class CoopCampaignMapTooltipCapture
    {
        private static readonly FieldInfo TimeShortcuts = typeof(MapTimeControlVM).GetField("_shortcuts", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo NavigationTracker = typeof(MapNavigationElementBase).GetField("_viewDataTracker", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ClanPermission = typeof(ClanNavigationElement).GetField("_mostRecentClanScreenPermission", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo NavigationHandler = typeof(MapNavigationElementBase).GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic);

        internal bool TryCaptureControl(CoopCampaignMapBarCapture.ControlSource source, string session, string token,
            long generation, long sequence, bool extended, out CoopCampaignMapTooltipPayload payload, out string reason)
        {
            payload = null; reason = "bar-control-unavailable";
            if (Thread.CurrentThread.ManagedThreadId != _ownerThread || _cleanupFaulted ||
                !CoopCampaignMapTooltipPayloadCodec.IsValidCorrelation(session, token, generation, sequence) ||
                Interlocked.CompareExchange(ref _capturing, 1, 0) != 0) return false;
            CoopCampaignMapTooltipTextTransaction<TextObject> textScope = null;
            CoopCampaignMapTooltipThreadStateScope languageScope = null;
            bool success = false;
            try
            {
                if (!IsCurrentControl(source, session, token) ||
                    !TryBeginTextScope(out textScope) ||
                    !CoopCampaignMapTooltipLanguageState.AreControlWritableTemplatesIsolated() ||
                    !CoopCampaignMapTooltipLanguageState.TryBegin(_ownerThread, out languageScope)) return false;
                var hint = new CoopCampaignMapControlHint();
                if (source.Owner?.GetType() == typeof(HintViewModel))
                {
                    if (source.Key != "GatherArmyHint" && source.Key != "ExtendHint") return false;
                    var text = ((HintViewModel)source.Owner).HintText;
                    if (TextObject.IsNullOrEmpty(text)) return false;
                    hint.Text = text.ToString();
                }
                else if (source.Owner is MapNavigationItemVM navigation)
                {
                    if (navigation.GetType() != typeof(MapNavigationItemVM)) return false;
                    var element = navigation.NavigationElement;
                    if (element?.GetType() == typeof(ClanNavigationElement))
                    {
                        if (source.Key != "Navigation_clan" || navigation.ItemId != "clan" ||
                            !TryReadPreparedClanHint(navigation, out var clanText)) return false;
                        hint.Text = clanText;
                    }
                    else if (!CoopCampaignMapBarCapture.IsReadOnlyNavigation(element)) return false;
                    else if (source.Key == "Alert_" + navigation.ItemId)
                    {
                        if (!navigation.HasAlert || !element.HasAlert || !IsAlertTemplateIsolated(navigation)) return false;
                        hint.Text = element.AlertTooltip?.ToString();
                    }
                    else if (source.Key == "Navigation_" + navigation.ItemId)
                    {
                        var permission = element.Permission;
                        hint.Text = (permission.IsAuthorized || element.IsActive
                            ? element.Tooltip : permission.ReasonString)?.ToString();
                    }
                    else return false;
                }
                else if (source.Owner?.GetType() != typeof(MapTimeControlVM)) return false;
                else if (source.Key == "TimeOfDayHint")
                {
                    // Audited native read-only provider: localized day part and camera hint.
                    foreach (var row in CampaignUIHelper.GetTimeOfDayAndResetCameraTooltip())
                    {
                        var color = row.TextColor;
                        hint.Rows.Add(new CoopCampaignMapTooltipRow { Definition = row.DefinitionLabel,
                            Value = row.ValueLabel, TextHeight = row.TextHeight, PropertyModifier = row.PropertyModifier,
                            Red = color.Red, Green = color.Green, Blue = color.Blue, Alpha = color.Alpha });
                    }
                }
                else
                {
                    string textId, shortcutField;
                    switch (source.Key)
                    {
                        case "PauseHint": textId = "str_pause"; shortcutField = "PauseHotkey"; break;
                        case "PlayHint": textId = "str_play"; shortcutField = "PlayHotkey"; break;
                        case "FastForwardHint": textId = "str_fast_forward"; shortcutField = "FastForwardHotkey"; break;
                        default: return false;
                    }
                    hint.Text = GameTexts.FindText(textId).ToString();
                    if (!Input.IsGamepadActive)
                    {
                        var shortcuts = TimeShortcuts?.GetValue(source.Owner);
                        var hotkey = shortcuts?.GetType().GetField(shortcutField)?.GetValue(shortcuts) as string;
                        if (hotkey == null) return false;
                        GameTexts.SetVariable("TEXT", hint.Text);
                        GameTexts.SetVariable("HOTKEY", hotkey);
                        hint.Text = GameTexts.FindText("str_hotkey_with_hint").ToString();
                    }
                }
                var result = new CoopCampaignMapTooltipPayload { SessionId = session, EntityId = token,
                    RequestGeneration = generation, Sequence = sequence, IsExtended = extended,
                    Kind = CoopCampaignMapTooltipKind.BarControl, ControlHint = hint };
                if (!IsCurrentControl(source, session, token) || !CoopCampaignMapTooltipPayloadCodec.IsValid(result)) return false;
                payload = result; reason = null; success = true;
            }
            catch (Exception) { reason = "bar-control-capture-failed"; }
            finally
            {
                try { try { languageScope?.Dispose(); } finally { textScope?.Dispose(); } }
                catch (Exception) { _cleanupFaulted = true; payload = null; success = false; reason = "bar-control-cleanup-failed"; }
                finally { Volatile.Write(ref _capturing, 0); }
            }
            return success;
        }

        private static bool IsCurrentControl(CoopCampaignMapBarCapture.ControlSource source, string session, string token) =>
            source != null && IsMapContext() &&
            CoopCampaignMapBarCapture.TryResolveCurrentControlHint(session, token, out var current) && ReferenceEquals(source, current);

        private static bool IsAlertTemplateIsolated(MapNavigationItemVM navigation)
        {
            var tracker = NavigationTracker?.GetValue(navigation.NavigationElement);
            if (tracker?.GetType() != typeof(ViewDataTrackerCampaignBehavior)) return false;
            string field;
            switch (navigation.ItemId)
            {
                case "party": field = "_recruitNotificationText"; break;
                case "character_developer": field = "_characterNotificationText"; break;
                case "quest": field = "_questNotificationText"; break;
                default: return false;
            }
            var template = typeof(ViewDataTrackerCampaignBehavior).GetField(field,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(tracker) as TextObject;
            return template != null && (template.Attributes == null || template.Attributes.Count > 0);
        }

        private static bool TryReadPreparedClanHint(MapNavigationItemVM navigation, out string text)
        {
            text = null;
            // Never call Permission/Tooltip: both can dispatch ClanScreenPermissionEvent.
            if (ClanPermission?.FieldType != typeof(NavigationPermissionItem?)) return false;
            var handler = NavigationHandler?.GetValue(navigation.NavigationElement) as MapNavigationHandler;
            // Native outer gates return a null reason before consulting the cached event
            // result. A retained denial must not leak through saving/modal/navigation locks.
            if (handler == null || !MapNavigationHelper.IsNavigationBarEnabled(handler)) return false;
            var cached = ClanPermission.GetValue(navigation.NavigationElement);
            if (!navigation.IsEnabled && !navigation.IsActive)
            {
                if (!(cached is NavigationPermissionItem denied) || denied.IsAuthorized) return false;
                text = denied.ReasonString?.ToString();
                return !string.IsNullOrEmpty(text);
            }
            if (cached is NavigationPermissionItem permission && !permission.IsAuthorized && !navigation.IsActive) return false;
            text = GameTexts.FindText("str_clan").ToString();
            if (!Input.IsGamepadActive)
            {
                var template = GameTexts.FindText("str_hotkey_with_hint");
                template.SetTextVariable("TEXT", text);
                template.SetTextVariable("HOTKEY", Game.Current.GameTextManager.GetHotKeyGameText(
                    "GenericCampaignPanelsGameKeyCategory", 41).ToString());
                text = template.ToString();
            }
            return !string.IsNullOrEmpty(text);
        }
    }
}
