using System;
using CoopSpectator.Infrastructure;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Campaign
{
    public sealed class CoopCampaignMapPrototypePublisherBehavior :
        CampaignBehaviorBase
    {
        private const float PublishIntervalSeconds = 0.2f;
        private static readonly TimeSpan FailureLogInterval =
            TimeSpan.FromSeconds(10d);
        private static CoopCampaignMapPrototypePublisherBehavior
            _applicationTickPublisher;

        private string _sessionId = Guid.NewGuid().ToString("N");
        private int _revision;
        private int _lastHeading;
        private float _timeUntilNextPublish;
        private bool _initialPublishLogged;
        private DateTime _nextFailureLogUtc = DateTime.MinValue;

        public override void RegisterEvents()
        {
            _applicationTickPublisher = this;
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        public static void PumpApplicationTick(float dt)
        {
            if (!ExperimentalFeatures.EnableCampaignMapPrototype)
                return;

            CoopCampaignMapPrototypePublisherBehavior publisher =
                _applicationTickPublisher;
            if (publisher == null ||
                TaleWorlds.CampaignSystem.Campaign.Current == null ||
                Mission.Current != null)
            {
                return;
            }

            if (!IsMapCameraAvailable())
                return;

            publisher.PublishTick(dt);
        }

        public static void ResetApplicationTickPublisher()
        {
            _applicationTickPublisher = null;
        }

        private static bool IsMapCameraAvailable()
        {
            try
            {
                MapScreen mapScreen = MapScreen.Instance;
                return mapScreen?.MapCameraView?.Camera != null;
            }
            catch
            {
                return false;
            }
        }

        private void PublishTick(float dt)
        {
            _timeUntilNextPublish -= Math.Max(0f, dt);
            if (_timeUntilNextPublish > 0f)
                return;
            _timeUntilNextPublish = PublishIntervalSeconds;

            if (!TryCaptureSnapshot(out CoopCampaignMapPrototypeHostSnapshot snapshot, out string reason))
            {
                LogFailureRateLimited("capture:" + (reason ?? "unknown"));
                return;
            }

            if (!CoopCampaignMapPrototypeBridgeFile.TryWrite(snapshot, out reason))
            {
                LogFailureRateLimited("write:" + (reason ?? "unknown"));
                return;
            }

            if (!_initialPublishLogged)
            {
                _initialPublishLogged = true;
                ModLogger.Info(
                    "CoopCampaignMapPrototypePublisher: authoritative host map state active. Session=" +
                    _sessionId + " Revision=" + snapshot.Revision +
                    " Camera=" + (snapshot.Camera != null) + ".");
            }
        }

        private bool TryCaptureSnapshot(
            out CoopCampaignMapPrototypeHostSnapshot snapshot,
            out string reason)
        {
            snapshot = null;
            reason = null;
            TaleWorlds.CampaignSystem.Campaign campaign =
                TaleWorlds.CampaignSystem.Campaign.Current;
            if (campaign == null)
            {
                reason = "campaign-missing";
                return false;
            }

            MobileParty mainParty = campaign.MainParty;
            if (mainParty == null)
            {
                reason = "main-party-missing";
                return false;
            }
            if (Mission.Current != null)
            {
                reason = "mission-active";
                return false;
            }

            try
            {
                campaign.MapSceneWrapper.GetMapBorders(
                    out Vec2 minimum,
                    out Vec2 maximum,
                    out _);
                Vec2 position = mainParty.VisualPosition2DWithoutError;
                if (!CoopCampaignMapPrototypeContract.TryNormalizeMapPosition(
                        position.x,
                        position.y,
                        minimum.x,
                        minimum.y,
                        maximum.x,
                        maximum.y,
                        out int normalizedX,
                        out int normalizedY))
                {
                    reason = "invalid-map-borders-or-position";
                    return false;
                }

                Vec2 bearing = mainParty.Bearing;
                _lastHeading = CoopCampaignMapPrototypeContract.QuantizeDirection(
                    bearing.x,
                    bearing.y,
                    _lastHeading);
                if (_revision == int.MaxValue)
                {
                    _sessionId = Guid.NewGuid().ToString("N");
                    _revision = 0;
                }

                _revision++;
                CoopCampaignMapPrototypeCameraState cameraState =
                    TryCaptureMapCamera();
                snapshot = new CoopCampaignMapPrototypeHostSnapshot
                {
                    SchemaVersion =
                        CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
                    SessionId = _sessionId,
                    Revision = _revision,
                    NormalizedX = normalizedX,
                    NormalizedY = normalizedY,
                    Heading = _lastHeading,
                    SampleTimeMilliseconds = Environment.TickCount & int.MaxValue,
                    IsMoving = mainParty.IsMoving,
                    IsActive = true,
                    Camera = cameraState,
                    UpdatedUtc = DateTime.UtcNow
                };
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        private static CoopCampaignMapPrototypeCameraState TryCaptureMapCamera()
        {
            try
            {
                MapScreen mapScreen = MapScreen.Instance;
                MapCameraView mapCameraView = mapScreen?.MapCameraView;
                Camera camera = mapCameraView?.Camera;
                if (mapScreen == null || mapCameraView == null || camera == null)
                    return null;

                MatrixFrame frame = mapCameraView.CameraFrame;
                Vec3 direction = -frame.rotation.u;
                Vec3 up = frame.rotation.f;
                return CoopCampaignMapPrototypeContract.TryQuantizeCamera(
                    frame.origin.x,
                    frame.origin.y,
                    frame.origin.z,
                    direction.x,
                    direction.y,
                    direction.z,
                    up.x,
                    up.y,
                    up.z,
                    camera.GetFovVertical(),
                    out CoopCampaignMapPrototypeCameraState cameraState)
                    ? cameraState
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private void LogFailureRateLimited(string reason)
        {
            DateTime utcNow = DateTime.UtcNow;
            if (utcNow < _nextFailureLogUtc)
                return;

            _nextFailureLogUtc = utcNow + FailureLogInterval;
            ModLogger.Info(
                "CoopCampaignMapPrototypePublisher: state unavailable. Reason=" +
                (reason ?? "unknown") + ".");
        }
    }
}
