using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.UI
{
    public sealed class CoopPlayerNameplateListVM : ViewModel
    {
        private readonly Dictionary<NetworkCommunicator, CoopPlayerNameplateTargetVM> _markersByPeer =
            new Dictionary<NetworkCommunicator, CoopPlayerNameplateTargetVM>();
        private readonly HashSet<NetworkCommunicator> _visiblePeers =
            new HashSet<NetworkCommunicator>();

        private MBBindingList<CoopPlayerNameplateTargetVM> _markers =
            new MBBindingList<CoopPlayerNameplateTargetVM>();

        [DataSourceProperty]
        public MBBindingList<CoopPlayerNameplateTargetVM> Markers
        {
            get => _markers;
            private set
            {
                if (ReferenceEquals(_markers, value))
                    return;

                _markers = value;
                OnPropertyChangedWithValue(value, nameof(Markers));
            }
        }

        public void RefreshPeers(Mission mission, BattleSideEnum localSide)
        {
            _visiblePeers.Clear();

            if (mission == null || GameNetwork.NetworkPeers == null)
            {
                RemoveMissingMarkers();
                return;
            }

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (!TryResolveRemoteControlledAgent(
                        mission,
                        peer,
                        out MissionPeer missionPeer,
                        out Agent controlledAgent))
                {
                    continue;
                }

                _visiblePeers.Add(peer);
                if (!_markersByPeer.TryGetValue(peer, out CoopPlayerNameplateTargetVM marker))
                {
                    marker = new CoopPlayerNameplateTargetVM();
                    _markersByPeer.Add(peer, marker);
                    Markers.Add(marker);
                }

                marker.Bind(
                    controlledAgent,
                    ResolvePeerName(peer, missionPeer),
                    localSide,
                    controlledAgent.Team?.Side ?? BattleSideEnum.None);
            }

            RemoveMissingMarkers();
        }

        public void UpdateScreenPositions(Camera camera)
        {
            if (camera == null)
            {
                foreach (CoopPlayerNameplateTargetVM marker in Markers)
                    marker.Hide();
                return;
            }

            foreach (CoopPlayerNameplateTargetVM marker in Markers)
                marker.UpdateScreenPosition(camera);
        }

        public override void OnFinalize()
        {
            foreach (CoopPlayerNameplateTargetVM marker in Markers)
                marker.OnFinalize();

            Markers.Clear();
            _markersByPeer.Clear();
            _visiblePeers.Clear();
            base.OnFinalize();
        }

        private void RemoveMissingMarkers()
        {
            NetworkCommunicator[] removedPeers = _markersByPeer.Keys
                .Where(peer => !_visiblePeers.Contains(peer))
                .ToArray();

            foreach (NetworkCommunicator peer in removedPeers)
            {
                CoopPlayerNameplateTargetVM marker = _markersByPeer[peer];
                marker.Hide();
                marker.OnFinalize();
                Markers.Remove(marker);
                _markersByPeer.Remove(peer);
            }
        }

        private static bool TryResolveRemoteControlledAgent(
            Mission mission,
            NetworkCommunicator peer,
            out MissionPeer missionPeer,
            out Agent controlledAgent)
        {
            missionPeer = null;
            controlledAgent = null;

            if (peer == null ||
                peer.IsMine ||
                peer.IsServerPeer ||
                !peer.IsConnectionActive ||
                !peer.IsSynchronized)
            {
                return false;
            }

            missionPeer = peer.GetComponent<MissionPeer>();
            controlledAgent = missionPeer?.ControlledAgent ?? peer.ControlledAgent;
            return missionPeer != null &&
                   controlledAgent != null &&
                   controlledAgent.IsActive() &&
                   controlledAgent.IsHuman &&
                   !controlledAgent.IsMount &&
                   ReferenceEquals(controlledAgent.Mission, mission);
        }

        private static string ResolvePeerName(NetworkCommunicator peer, MissionPeer missionPeer)
        {
            string displayedName = missionPeer?.DisplayedName;
            if (!string.IsNullOrWhiteSpace(displayedName))
                return displayedName.Trim();

            string userName = peer?.UserName;
            return !string.IsNullOrWhiteSpace(userName)
                ? userName.Trim()
                : "Player";
        }
    }

    public sealed class CoopPlayerNameplateTargetVM : ViewModel
    {
        private const string AllyTextColor = "#74E8FFFF";
        private const string AllyBackgroundColor = "#102B35D9";
        private const string EnemyTextColor = "#FF9478FF";
        private const string EnemyBackgroundColor = "#3A1712D9";
        private const string NeutralTextColor = "#F2D16BFF";
        private const string NeutralBackgroundColor = "#332A12D9";

        private Agent _agent;
        private Vec2 _position = new Vec2(-1000f, -1000f);
        private bool _isVisible;
        private string _name = string.Empty;
        private string _textColor = NeutralTextColor;
        private string _backgroundColor = NeutralBackgroundColor;

        [DataSourceProperty]
        public Vec2 Position
        {
            get => _position;
            private set
            {
                if (_position == value)
                    return;

                _position = value;
                OnPropertyChangedWithValue(value, nameof(Position));
            }
        }

        [DataSourceProperty]
        public bool IsVisible
        {
            get => _isVisible;
            private set
            {
                if (_isVisible == value)
                    return;

                _isVisible = value;
                OnPropertyChangedWithValue(value, nameof(IsVisible));
            }
        }

        [DataSourceProperty]
        public string Name
        {
            get => _name;
            private set
            {
                string normalized = value ?? string.Empty;
                if (string.Equals(_name, normalized, StringComparison.Ordinal))
                    return;

                _name = normalized;
                OnPropertyChangedWithValue(_name, nameof(Name));
            }
        }

        [DataSourceProperty]
        public string TextColor
        {
            get => _textColor;
            private set
            {
                if (string.Equals(_textColor, value, StringComparison.Ordinal))
                    return;

                _textColor = value;
                OnPropertyChangedWithValue(_textColor, nameof(TextColor));
            }
        }

        [DataSourceProperty]
        public string BackgroundColor
        {
            get => _backgroundColor;
            private set
            {
                if (string.Equals(_backgroundColor, value, StringComparison.Ordinal))
                    return;

                _backgroundColor = value;
                OnPropertyChangedWithValue(_backgroundColor, nameof(BackgroundColor));
            }
        }

        public void Bind(
            Agent agent,
            string name,
            BattleSideEnum localSide,
            BattleSideEnum remoteSide)
        {
            _agent = agent;
            Name = name;

            if (localSide == BattleSideEnum.None || remoteSide == BattleSideEnum.None)
            {
                TextColor = NeutralTextColor;
                BackgroundColor = NeutralBackgroundColor;
            }
            else if (localSide == remoteSide)
            {
                TextColor = AllyTextColor;
                BackgroundColor = AllyBackgroundColor;
            }
            else
            {
                TextColor = EnemyTextColor;
                BackgroundColor = EnemyBackgroundColor;
            }
        }

        public void UpdateScreenPosition(Camera camera)
        {
            if (_agent == null || !_agent.IsActive() || camera == null)
            {
                Hide();
                return;
            }

            try
            {
                Vec3 worldPosition = _agent.GetEyeGlobalPosition();
                worldPosition.z += 0.65f;

                float screenX = -1000f;
                float screenY = -1000f;
                float depth = 0f;
                MBWindowManager.WorldToScreenInsideUsableArea(
                    camera,
                    worldPosition,
                    ref screenX,
                    ref screenY,
                    ref depth);

                if (depth <= 0f)
                {
                    Hide();
                    return;
                }

                Position = new Vec2(screenX, screenY);
                IsVisible = screenX >= -180f &&
                            screenX <= Screen.RealScreenResolutionWidth + 180f &&
                            screenY >= -80f &&
                            screenY <= Screen.RealScreenResolutionHeight + 80f;
            }
            catch
            {
                Hide();
            }
        }

        public void Hide()
        {
            IsVisible = false;
        }

        public override void OnFinalize()
        {
            _agent = null;
            IsVisible = false;
            base.OnFinalize();
        }
    }
}
