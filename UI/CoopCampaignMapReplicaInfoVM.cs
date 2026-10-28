using System;
using TaleWorlds.Library;

namespace CoopSpectator.UI
{
    internal sealed class CoopCampaignMapReplicaInfoVM : ViewModel
    {
        private readonly Action _followHost;
        private string _cameraModeText = "Host camera";
        public CoopCampaignMapReplicaInfoVM(Action followHost) { _followHost = followHost; }

        [DataSourceProperty]
        public string CameraModeText => _cameraModeText;
        [DataSourceProperty]
        public string FollowHostText => "Return to host camera";
        [DataSourceProperty]
        public string ControlsText =>
            "WASD / Arrow keys — Move camera • Mouse wheel — Zoom • Right mouse button — Rotate camera";

        public void ExecuteFollowHost() => _followHost?.Invoke();
        internal void SetCameraMode(bool isFollowingHost)
        {
            string next = isFollowingHost ? "Host camera" : "Free camera";
            if (_cameraModeText == next) return;
            _cameraModeText = next;
            OnPropertyChangedWithValue(next, nameof(CameraModeText));
        }
    }
}
