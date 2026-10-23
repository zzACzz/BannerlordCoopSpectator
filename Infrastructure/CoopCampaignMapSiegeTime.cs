using System;

namespace CoopSpectator.Infrastructure
{
    // Presentation timing only. No damage, campaign tick or mission authority.
    public sealed class CoopCampaignMapSiegeTime
    {
        public long Epoch { get; set; }
        public double SampleSeconds { get; set; }
        public double CampaignHours { get; set; }
        public float TimeScale { get; set; }

        public bool IsValid() => Epoch > 0 && Finite(SampleSeconds) && SampleSeconds >= 0 &&
            Finite(CampaignHours) && CampaignHours >= 0 &&
            !float.IsNaN(TimeScale) && !float.IsInfinity(TimeScale) && TimeScale >= 0 && TimeScale <= 16;

        public CoopCampaignMapSiegeTime Clone() => (CoopCampaignMapSiegeTime)MemberwiseClone();
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
