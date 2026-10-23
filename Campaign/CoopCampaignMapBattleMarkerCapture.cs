using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.Infrastructure;
using SandBox.View.Map;
using SandBox.ViewModelCollection.Map;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Campaign
{
    // Campaign/UI-thread only. Never constructs a campaign view or event.
    // Integration is pending; IDs are snapshot-local, so consumers must replace
    // the entire list atomically and must not interpolate by these IDs.
    internal static class CoopCampaignMapBattleMarkerCapture
    {
        private static MethodInfo _getter;
        private static FieldInfo _sourceField;
        private static readonly FieldInfo DisclosedField = typeof(MapEventVisualItemVM)
            .GetField("_isAVisibleEvent", BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool TryCapture(out CoopCampaignMapBattleMarker[] snapshot, out string reason)
        {
            snapshot = Array.Empty<CoopCampaignMapBattleMarker>();
            reason = null;
            try
            {
                var screen = MapScreen.Instance;
                var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
                if (campaign == null || screen == null || Mission.Current != null ||
                    !(Game.Current?.GameStateManager?.ActiveState is MapState) ||
                    campaign.ConversationManager?.IsConversationInProgress == true)
                    return true;
                if (!ResolveMetadata() || DisclosedField?.FieldType != typeof(bool))
                { reason = "native-event-layout-unavailable"; return false; }
                var view = _getter.Invoke(screen, null);
                var source = view == null ? null : _sourceField.GetValue(view) as MapEventVisualsVM;
                if (source?.MapEvents == null)
                { reason = "native-event-source-unavailable"; return false; }
                var result = new List<CoopCampaignMapBattleMarker>();
                var seen = new HashSet<MapEvent>();
                foreach (var item in source.MapEvents)
                {
                    var mapEvent = item?.MapEvent;
                    if (mapEvent == null)
                    { reason = "native-event-entry-invalid"; return false; }
                    // Require both the prepared host view and current disclosure;
                    // do not inherit the host camera's screen-space culling.
                    bool disclosed = DisclosedField.GetValue(item) is bool visible && visible && mapEvent.IsVisible;
                    if (!CoopCampaignMapBattleMarkerPolicy.Admit(disclosed, mapEvent.MapEventSettlement != null))
                        continue;
                    if (!seen.Add(mapEvent)) continue;
                    if (result.Count == CoopCampaignMapBattleMarkerPolicy.MaximumEvents)
                    { reason = "native-event-capacity-exceeded"; return false; }
                    var position = mapEvent.Position.AsVec3();
                    var marker = new CoopCampaignMapBattleMarker(result.Count + 1, position.x, position.y, position.z);
                    if (!marker.IsValid)
                    { reason = "native-event-position-invalid"; return false; }
                    result.Add(marker);
                }
                if (!CoopCampaignMapBattleMarkerPolicy.TrySnapshot(result, out var captured))
                { reason = "native-event-snapshot-invalid"; return false; }
                snapshot = captured;
                return true;
            }
            catch (Exception ex)
            {
                reason = "native-event-capture-" + ex.GetType().Name;
                return false;
            }
        }

        private static bool ResolveMetadata()
        {
            if (_getter != null) return true;
            Type ownerType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetName().Name == "SandBox.GauntletUI")
                    ownerType = assembly.GetType("SandBox.GauntletUI.Map.GauntletMapEventVisualsView", false);
            if (ownerType == null || !typeof(MapView).IsAssignableFrom(ownerType)) return false;
            var field = ownerType.GetField("_dataSource", BindingFlags.Instance | BindingFlags.NonPublic);
            var getter = typeof(MapScreen).GetMethod("GetMapView", BindingFlags.Instance | BindingFlags.Public);
            if (field?.FieldType != typeof(MapEventVisualsVM) || getter == null ||
                !getter.IsGenericMethodDefinition || getter.GetParameters().Length != 0) return false;
            _sourceField = field;
            _getter = getter.MakeGenericMethod(ownerType);
            return true;
        }
    }
}
