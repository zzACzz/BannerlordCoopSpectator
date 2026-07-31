using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;

namespace CoopSpectator.UI
{
    /// <summary>
    /// Client-only overlay that follows agents currently controlled by remote players.
    /// Peer discovery is intentionally throttled; only screen projection runs each frame.
    /// </summary>
    public sealed class CoopPlayerNameplateMissionView : MissionView
    {
        private const string MovieName = "CoopPlayerNameplates";
        private const float PeerRefreshIntervalSeconds = 0.2f;
        private const int LayerOrder = 4;

        private GauntletLayer _layer;
        private GauntletMovieIdentifier _movie;
        private CoopPlayerNameplateListVM _dataSource;
        private float _peerRefreshTimer;

        public override void OnMissionScreenInitialize()
        {
            base.OnMissionScreenInitialize();

            if (!GameNetwork.IsClient || MissionScreen == null)
                return;

            ViewOrderPriority = LayerOrder;
            _peerRefreshTimer = 0f;

            try
            {
                _dataSource = new CoopPlayerNameplateListVM();
                _layer = new GauntletLayer("CoopPlayerNameplateLayer", LayerOrder, false)
                {
                    IsFocusLayer = false
                };
                MissionScreen.AddLayer(_layer);
                _movie = _layer.LoadMovie(MovieName, _dataSource);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopPlayerNameplateMissionView: failed to initialize player nameplates: " +
                    ex.Message);
                ReleaseLayer();
            }
        }

        public override void OnMissionScreenTick(float dt)
        {
            base.OnMissionScreenTick(dt);

            if (_dataSource == null || MissionScreen?.CombatCamera == null)
                return;

            _peerRefreshTimer -= dt;
            if (_peerRefreshTimer <= 0f)
            {
                _peerRefreshTimer = PeerRefreshIntervalSeconds;
                _dataSource.RefreshPeers(Mission, ResolveLocalSide());
            }

            _dataSource.UpdateScreenPositions(MissionScreen.CombatCamera);
        }

        public override void OnMissionScreenFinalize()
        {
            ReleaseLayer();
            base.OnMissionScreenFinalize();
        }

        public override void OnRemoveBehavior()
        {
            ReleaseLayer();
            base.OnRemoveBehavior();
        }

        private BattleSideEnum ResolveLocalSide()
        {
            try
            {
                MissionPeer missionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
                Team team = missionPeer?.Team ?? missionPeer?.ControlledAgent?.Team ?? Mission?.MainAgent?.Team;
                return team?.Side ?? BattleSideEnum.None;
            }
            catch
            {
                return BattleSideEnum.None;
            }
        }

        private void ReleaseLayer()
        {
            try
            {
                if (_layer != null && _movie != null)
                    _layer.ReleaseMovie(_movie);
            }
            catch
            {
            }

            _movie = null;

            try
            {
                _dataSource?.OnFinalize();
            }
            catch
            {
            }

            _dataSource = null;

            try
            {
                if (_layer != null)
                    MissionScreen?.RemoveLayer(_layer);
            }
            catch
            {
            }

            _layer = null;
        }
    }
}
