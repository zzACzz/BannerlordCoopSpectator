using System;
using System.Reflection;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using SandBox.View.Map;
using SandBox.View.Map.Navigation.NavigationElements;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapBar;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Campaign
{
    internal static class CoopCampaignMapBarCapture
    {
        private static MethodInfo _getter;
        private static FieldInfo _layerField, _sourceField;
        private static readonly CoopCampaignMapBarHintIdentity<MapInfoItemVM> HintIdentity = new CoopCampaignMapBarHintIdentity<MapInfoItemVM>();
        private static object _hintOwner;
        internal sealed class ControlSource
        {
            internal string Key;
            internal object Owner;
        }
        private static readonly Dictionary<string, ControlSource> ControlSources = new Dictionary<string, ControlSource>(StringComparer.Ordinal);
        private static readonly CoopCampaignMapBarHintIdentity<ControlSource> ControlIdentity = new CoopCampaignMapBarHintIdentity<ControlSource>();
        internal static void ResetHintIdentity()
        { HintIdentity.Clear(); ControlIdentity.Clear(); ControlSources.Clear(); _hintOwner = null; }

        internal static bool TryResolveCurrentControlHint(string session, string token, out ControlSource source)
        {
            source = null;
            return TryCapture(session, out var current, out _) && current != null &&
                CoopCampaignMapTooltipTargetAdmission.ContainsBarHint(current, token) &&
                ControlIdentity.TryResolve(_hintOwner, session, token, out source);
        }

        internal static bool TryResolveCurrentInfoHint(string session, string token, out MapInfoItemVM source)
        {
            source = null;
            // Revalidate prepared ownership/visibility now, not just at last publication.
            return TryCapture(session, out var current, out _) && current != null && current.IsInfoBarEnabled &&
                HintIdentity.TryResolve(_hintOwner, session, token, out source);
        }

        // Owning campaign/UI thread only. A null result revokes the previous bar.
        // Reads prepared presentation; never calls native Refresh/Tick or commands.
        internal static bool TryCapture(string session, out CoopCampaignMapBarPresentation snapshot, out string reason)
        {
            snapshot = null;
            reason = null;
            try
            {
                if (string.IsNullOrEmpty(session)) { reason = "map-bar-session-missing"; return false; }
                var screen = MapScreen.Instance;
                var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
                if (screen == null || campaign == null || Mission.Current != null ||
                    !(Game.Current?.GameStateManager?.ActiveState is MapState) ||
                    campaign.ConversationManager?.IsConversationInProgress == true) return true;
                if (!ResolveMetadata()) { reason = "native-map-bar-layout-unavailable"; return false; }
                var owner = _getter.Invoke(screen, null);
                var layer = owner == null ? null : _layerField.GetValue(owner);
                var source = layer == null ? null : _sourceField.GetValue(layer) as MapBarVM;
                if (source == null) { reason = "native-map-bar-source-unavailable"; return false; }
                if (!source.IsEnabled) return true;
                var time = source.MapTimeControl;
                var info = source.MapInfo;
                var navigation = source.MapNavigation;
                if (time == null || info?.PrimaryInfoItems == null || info.SecondaryInfoItems == null ||
                    navigation?.NavigationItems == null ||
                    info.PrimaryInfoItems.Count > CoopCampaignMapBarPresentation.MaxItems ||
                    info.SecondaryInfoItems.Count > CoopCampaignMapBarPresentation.MaxItems ||
                    navigation.NavigationItems.Count > CoopCampaignMapBarPresentation.MaxItems)
                { reason = "native-map-bar-lists-invalid"; return false; }
                var captured = new CoopCampaignMapBarPresentation
                {
                    IsEnabled = source.IsEnabled, IsGatherArmyVisible = source.IsGatherArmyVisible,
                    CanGatherArmy = source.CanGatherArmy, IsCenterPanelEnabled = time.IsCenterPanelEnabled,
                    IsInfoBarEnabled = info.IsInfoBarEnabled, IsInfoBarExtended = info.IsInfoBarExtended,
                    Date = time.Date ?? string.Empty, PausedText = time.PausedText ?? string.Empty,
                    TutorialElementId = source.TutorialNotification?.ElementID ?? string.Empty,
                    Time = time.Time, HoursInDay = CampaignTime.HoursInDay, TimeFlowState = time.TimeFlowState
                };
                foreach (var item in info.PrimaryInfoItems) captured.PrimaryInfoItems.Add(CopyInfo(item));
                foreach (var item in info.SecondaryInfoItems) captured.SecondaryInfoItems.Add(CopyInfo(item));
                foreach (var item in navigation.NavigationItems)
                    captured.NavigationItems.Add(item == null ? null : new CoopCampaignMapBarNavigation
                    {
                        ItemId = item.ItemId, AlertText = item.AlertText ?? string.Empty,
                        IsEnabled = item.IsEnabled, IsActive = item.IsActive, HasAlert = item.HasAlert
                    });
                var keys = new List<string>();
                var sources = new List<MapInfoItemVM>();
                foreach (var item in info.PrimaryInfoItems) { keys.Add(item == null ? null : "primary_" + item.ItemId); sources.Add(item); }
                foreach (var item in info.SecondaryInfoItems) { keys.Add(item == null ? null : "secondary_" + item.ItemId); sources.Add(item); }
                if (!HintIdentity.TryUpdate(source, session, source.IsEnabled,
                    keys, sources, out var hintTokens))
                { reason = "native-map-bar-hint-identity-invalid"; return false; }
                int hintIndex = 0;
                foreach (var item in captured.PrimaryInfoItems) item.HintToken = hintTokens[hintIndex++];
                foreach (var item in captured.SecondaryInfoItems) item.HintToken = hintTokens[hintIndex++];
                // Use stable prepared submodel ownership, not TimeOfDayHint: native code
                // replaces that small wrapper each tick without changing its provider.
                if (!CaptureControlTargets(source, session, captured))
                { reason = "native-map-bar-control-identity-invalid"; return false; }
                if (!captured.IsValid()) { reason = "native-map-bar-presentation-invalid"; return false; }
                if (!ReferenceEquals(screen, MapScreen.Instance) || !ReferenceEquals(campaign, TaleWorlds.CampaignSystem.Campaign.Current) ||
                    !ReferenceEquals(owner, _getter.Invoke(screen, null)) ||
                    !ReferenceEquals(layer, _layerField.GetValue(owner)) ||
                    !ReferenceEquals(source, _sourceField.GetValue(layer)))
                { reason = "native-map-bar-source-replaced"; return false; }
                _hintOwner = source;
                snapshot = captured;
                return true;
            }
            catch (Exception ex)
            { reason = "native-map-bar-capture-" + ex.GetType().Name; return false; }
            finally { if (snapshot == null) ResetHintIdentity(); }
        }

        private static CoopCampaignMapBarInfo CopyInfo(MapInfoItemVM item) => item == null ? null : new CoopCampaignMapBarInfo
        { ItemId = item.ItemId, VisualId = item.VisualId, Value = item.Value ?? string.Empty, IntValue = item.IntValue, HasWarning = item.HasWarning };

        private static bool CaptureControlTargets(MapBarVM bar, string session, CoopCampaignMapBarPresentation captured)
        {
            var keys = new List<string>();
            var sources = new List<ControlSource>();
            var availability = new List<bool>();
            foreach (string key in new[] { "PauseHint", "PlayHint", "FastForwardHint", "TimeOfDayHint" })
            {
                if (!ControlSources.TryGetValue(key, out var source) || !ReferenceEquals(source.Owner, bar.MapTimeControl))
                    ControlSources[key] = source = new ControlSource { Key = key, Owner = bar.MapTimeControl };
                keys.Add(key); sources.Add(source);
                availability.Add(captured.IsCenterPanelEnabled);
            }
            foreach (var item in bar.MapNavigation.NavigationItems)
            {
                if (item?.GetType() != typeof(MapNavigationItemVM) ||
                    (!IsReadOnlyNavigation(item.NavigationElement) && item.NavigationElement?.GetType() != typeof(ClanNavigationElement))) continue;
                string key = "Navigation_" + item.ItemId;
                if (!ControlSources.TryGetValue(key, out var source) || !ReferenceEquals(source.Owner, item))
                    ControlSources[key] = source = new ControlSource { Key = key, Owner = item };
                keys.Add(key); sources.Add(source); availability.Add(true);
                if (item.HasAlert)
                {
                    key = "Alert_" + item.ItemId;
                    if (!ControlSources.TryGetValue(key, out source) || !ReferenceEquals(source.Owner, item))
                        ControlSources[key] = source = new ControlSource { Key = key, Owner = item };
                    keys.Add(key); sources.Add(source); availability.Add(true);
                }
            }
            AddPreparedHint("GatherArmyHint", bar.GatherArmyHint, captured.IsGatherArmyVisible, keys, sources, availability);
            AddPreparedHint("ExtendHint", bar.MapInfo.ExtendHint, captured.IsInfoBarEnabled, keys, sources, availability);
            var removed = new List<string>();
            foreach (var key in ControlSources.Keys) if (!keys.Contains(key)) removed.Add(key);
            foreach (var key in removed) ControlSources.Remove(key);
            if (!ControlIdentity.TryUpdate(bar, session, bar.IsEnabled, keys, sources, out var tokens)) return false;
            for (int i = 0; i < keys.Count; i++)
                captured.ControlHints.Add(new CoopCampaignMapBarControlTarget
                { Key = keys[i], HintToken = tokens[i], IsAvailable = availability[i] });
            return true;
        }

        internal static bool IsReadOnlyNavigation(INavigationElement element)
        {
            var type = element?.GetType();
            // Clan permission dispatches an event: it needs a separate observed-result path.
            return type == typeof(PartyNavigationElement) || type == typeof(InventoryNavigationElement) ||
                type == typeof(CharacterDeveloperNavigationElement) || type == typeof(KingdomNavigationElement) ||
                type == typeof(QuestsNavigationElement) || type == typeof(EscapeMenuNavigationElement);
        }

        private static void AddPreparedHint(string key, HintViewModel hint, bool visible,
            List<string> keys, List<ControlSource> sources, List<bool> availability)
        {
            if (hint?.GetType() != typeof(HintViewModel)) return;
            if (!ControlSources.TryGetValue(key, out var source) || !ReferenceEquals(source.Owner, hint))
                ControlSources[key] = source = new ControlSource { Key = key, Owner = hint };
            keys.Add(key); sources.Add(source); availability.Add(visible);
        }

        private static bool ResolveMetadata()
        {
            if (_getter != null) return true;
            Type ownerType = null, layerType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetName().Name == "SandBox.GauntletUI")
                {
                    ownerType = assembly.GetType("SandBox.GauntletUI.Map.GauntletMapBarView", false);
                    layerType = assembly.GetType("SandBox.GauntletUI.Map.GauntletMapBarGlobalLayer", false);
                }
            if (ownerType == null || layerType == null || !typeof(MapView).IsAssignableFrom(ownerType)) return false;
            var layer = ownerType.GetField("_mapBarGlobalLayer", BindingFlags.Instance | BindingFlags.NonPublic);
            var source = layerType.GetField("_dataSource", BindingFlags.Instance | BindingFlags.NonPublic);
            var getter = typeof(MapScreen).GetMethod("GetMapView", BindingFlags.Instance | BindingFlags.Public);
            if (layer?.FieldType != layerType || source?.FieldType != typeof(MapBarVM) || getter == null ||
                !getter.IsGenericMethodDefinition || getter.GetParameters().Length != 0) return false;
            _layerField = layer; _sourceField = source;
            _getter = getter.MakeGenericMethod(ownerType);
            return true;
        }
    }
}
