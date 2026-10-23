using System;

namespace CoopSpectator.Infrastructure
{
    // A prepared prefab skeleton channel, not an Agent action-set identifier.
    // Enclosing transport must bind this to a disclosed engine and sample clock.
    // Syntactic validation is not authorization to load a native resource.
    public sealed class CoopCampaignMapSiegeAnimation
    {
        public const int MaximumNameLength = 128;
        public string ClipName { get; set; } = string.Empty;
        public float Progress { get; set; }
        public float Speed { get; set; }

        public bool IsValid()
        {
            if (string.IsNullOrEmpty(ClipName) || ClipName.Length > MaximumNameLength ||
                !Finite(Progress) || Progress < 0f || Progress > 1f ||
                !Finite(Speed) || Speed < 0f || Speed > 16f) return false;
            foreach (char c in ClipName)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                    !(c >= '0' && c <= '9') && c != '_') return false;
            return true;
        }

        public bool TrySnapshot(out CoopCampaignMapSiegeAnimation snapshot)
        {
            snapshot = IsValid() ? (CoopCampaignMapSiegeAnimation)MemberwiseClone() : null;
            return snapshot != null;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
