namespace CoopSpectator.Infrastructure
{
    public enum CoopCampaignMapCameraApplication
    {
        None,
        Snap,
        Interpolate
    }

    /// <summary>Per-view camera ownership only; never changes replicated visibility or campaign state.</summary>
    public sealed class CoopCampaignMapReplicaCameraPolicy
    {
        private bool _needsHostSnap = true;
        public bool IsFollowingHost { get; private set; } = true;

        public void ObserveManualMovement(bool hasMovement)
        {
            if (hasMovement) IsFollowingHost = false;
        }

        public void FollowHost()
        {
            IsFollowingHost = true;
            _needsHostSnap = true;
        }

        public CoopCampaignMapCameraApplication AdmitHostFrame(bool isValid)
        {
            if (!isValid || !IsFollowingHost) return CoopCampaignMapCameraApplication.None;
            bool snap = _needsHostSnap;
            _needsHostSnap = false;
            return snap ? CoopCampaignMapCameraApplication.Snap : CoopCampaignMapCameraApplication.Interpolate;
        }

        public void Reset() => FollowHost();
    }
}
