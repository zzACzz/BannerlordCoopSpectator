namespace CoopSpectator.Infrastructure
{
    // All channels belong to one appearance revision. Session/entity identity is
    // carried by the enclosing dynamic state and must be checked before use.
    public sealed class CoopCampaignMapPartyPose
    {
        public int AppearanceRevision { get; set; }
        public float TimeScale { get; set; } = 1f;
        public CoopCampaignMapPartyAnimation Human { get; set; }
        public CoopCampaignMapPartyAnimation Mount { get; set; }
        public CoopCampaignMapPartyAnimation Caravan { get; set; }

        public bool IsValid() => AppearanceRevision >= 0 &&
            !float.IsNaN(TimeScale) && !float.IsInfinity(TimeScale) && TimeScale >= 0f && TimeScale <= 16f &&
            Human != null && Human.IsValid() &&
            (Mount == null || Mount.IsValid()) && (Caravan == null || Caravan.IsValid());

        public bool MatchesAppearance(int revision, bool hasHuman, bool hasMount, bool hasCaravan) =>
            IsValid() && AppearanceRevision == revision && hasHuman &&
            (Mount != null) == hasMount && (Caravan != null) == hasCaravan;

        public CoopCampaignMapPartyPose Clone() => new CoopCampaignMapPartyPose {
            AppearanceRevision = AppearanceRevision,
            TimeScale = TimeScale,
            Human = Human?.Clone(), Mount = Mount?.Clone(), Caravan = Caravan?.Clone() };

        // Installed Campaign.TickMapTime: campaign dt = 0.25 * real dt,
        // optionally multiplied by SpeedUpMultiplier, or stopped by mode/wait.
        // Numeric modes are the installed CampaignTimeControlMode enum0..6.
        public static bool TryResolveTimeScale(int mode, bool waiting, float speedUp, out float scale)
        {
            scale = 0f;
            if (mode == 0 || mode == 6 || (waiting && (mode == 3 || mode == 4))) return true;
            if (mode == 1 || mode == 3) { scale = 0.25f; return true; }
            if (mode != 2 && mode != 4 && mode != 5) return false;
            if (float.IsNaN(speedUp) || float.IsInfinity(speedUp) || speedUp < 0f || speedUp > 64f) return false;
            scale = 0.25f * speedUp; return true;
        }
    }
}
