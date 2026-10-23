using System;
using System.Reflection;
using System.Threading;
using CoopSpectator.Infrastructure;
using SandBox.View.Map;
using SandBox.ViewModelCollection.MapSiege;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Campaign
{
    // Construct/call on the campaign UI thread, after native view updates.
    // Reads prepared bindings only; never invokes native VM update/commands.
    // Caller integration and native timing acceptance are still pending.
    internal sealed class CoopCampaignMapSiegeOverlayCapture
    {
        private readonly int _threadId = Thread.CurrentThread.ManagedThreadId;
        private MethodInfo _getter;
        private FieldInfo _sourceField;
        private SiegeEvent _timeOwner;
        private long _timeEpoch;
        private double _lastHours;

        public bool TryCapture(out CoopCampaignMapSiegeOverlay snapshot, out string reason)
        {
            snapshot = null; reason = null;
            if (Thread.CurrentThread.ManagedThreadId != _threadId)
            { reason = "siege-overlay-thread-mismatch"; return false; }
            try
            {
                var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
                var screen = MapScreen.Instance;
                if (campaign == null || screen == null || Mission.Current != null ||
                    !(Game.Current?.GameStateManager?.ActiveState is MapState) ||
                    campaign.ConversationManager?.IsConversationInProgress == true) return true;
                var siege = PlayerSiege.PlayerSiegeEvent;
                if (siege == null) return true;
                if (!ResolveMetadata())
                { reason = "siege-overlay-layout-unavailable"; return false; }
                var view = _getter.Invoke(screen, null);
                // No view means no host siege overlay to replicate.
                if (view == null) return true;
                var source = _sourceField.GetValue(view) as MapSiegeVM;
                if (source?.PointsOfInterest == null)
                { reason = "siege-overlay-source-unavailable"; return false; }
                double hours = TaleWorlds.CampaignSystem.CampaignTime.Now.ToHours;
                if (!CoopCampaignMapPartyPose.TryResolveTimeScale((int)campaign.TimeControlMode,
                    campaign.IsMainPartyWaiting, campaign.SpeedUpMultiplier, out var timeScale))
                { reason = "siege-overlay-time-mode-invalid"; return false; }
                if (!ReferenceEquals(_timeOwner, siege) || hours < _lastHours)
                {
                    if (_timeEpoch == long.MaxValue) { reason = "siege-overlay-epoch-exhausted"; return false; }
                    _timeEpoch++; _timeOwner = siege;
                }
                _lastHours = hours;
                var candidate = new CoopCampaignMapSiegeOverlay {
                    Time = new CoopCampaignMapSiegeTime {
                        Epoch = _timeEpoch,
                        SampleSeconds = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency,
                        CampaignHours = hours, TimeScale = timeScale
                    },
                    PreparationTitleText = source.PreparationTitleText,
                    PreparationProgress = source.PreparationProgress,
                    CameraDistance = screen.MapCameraView.CameraDistance,
                    IsPreparationsCompleted = source.IsPreparationsCompleted
                };
                foreach (var item in source.PointsOfInterest)
                {
                    if (item == null || candidate.Points.Count >= CoopCampaignMapSiegeOverlay.MaximumPoints)
                    { reason = "siege-overlay-points-invalid"; return false; }
                    var position = item.MapSceneLocationFrame.origin;
                    candidate.Points.Add(new CoopCampaignMapSiegePoint {
                        Type = (int)item.Type, MachineIndex = item.MachineIndex,
                        X = position.x, Y = position.y, Z = position.z,
                        PrimaryColor = item.SidePrimaryColor.ToUnsignedInteger(),
                        SecondaryColor = item.SideSecondaryColor.ToUnsignedInteger(),
                        MachineType = item.MachineType, QueueIndex = item.QueueIndex,
                        CurrentHitpoints = item.CurrentHitpoints, MaxHitpoints = item.MaxHitpoints,
                        HasItem = item.HasItem, IsConstructing = item.IsConstructing,
                        IsPlayerSidePOI = item.IsPlayerSidePOI, IsFireVersion = item.IsFireVersion,
                        IsSelected = item.IsSelected
                    });
                }
                if (!ReferenceEquals(campaign, TaleWorlds.CampaignSystem.Campaign.Current) ||
                    !ReferenceEquals(screen, MapScreen.Instance) || !ReferenceEquals(siege, PlayerSiege.PlayerSiegeEvent) ||
                    !ReferenceEquals(view, _getter.Invoke(screen, null)) ||
                    !ReferenceEquals(source, _sourceField.GetValue(view)))
                { reason = "siege-overlay-owner-changed"; return false; }
                if (!candidate.TrySnapshot(out snapshot))
                { reason = "siege-overlay-values-invalid"; return false; }
                return true;
            }
            catch (Exception ex)
            { reason = "siege-overlay-capture-" + ex.GetType().Name; return false; }
        }

        private bool ResolveMetadata()
        {
            if (_getter != null) return true;
            Type owner = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetName().Name == "SandBox.GauntletUI")
                    owner = assembly.GetType("SandBox.GauntletUI.Map.GauntletMapSiegeOverlayView", false);
            if (owner == null || !typeof(MapView).IsAssignableFrom(owner)) return false;
            var field = owner.GetField("_dataSource", BindingFlags.Instance | BindingFlags.NonPublic);
            var getter = typeof(MapScreen).GetMethod("GetMapView", BindingFlags.Instance | BindingFlags.Public);
            if (field?.FieldType != typeof(MapSiegeVM) || getter == null ||
                !getter.IsGenericMethodDefinition || getter.GetParameters().Length != 0) return false;
            _sourceField = field;
            _getter = getter.MakeGenericMethod(owner);
            return true;
        }
    }
}
