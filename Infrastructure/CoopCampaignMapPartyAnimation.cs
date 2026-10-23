using System;

namespace CoopSpectator.Infrastructure
{
    // Detached channel-zero sample, not authority to select a party or a native action.
    // Transport must bind this to the disclosed entity and appearance revision.
    public sealed class CoopCampaignMapPartyAnimation
    {
        public const int MaximumNameLength = 128;
        public string ActionName { get; set; } = string.Empty;
        public float Progress { get; set; }
        public float Speed { get; set; }

        // Only an actor that previously received an explicit action needs rebuilding
        // when native locomotion resumes or the pose lease is revoked.
        public static bool RequiresVisualRecreation(string appliedAction, string nextAction) =>
            !string.IsNullOrEmpty(appliedAction) && appliedAction != "act_none" &&
            (string.IsNullOrEmpty(nextAction) || nextAction == "act_none");

        public bool IsValid()
        {
            if (string.IsNullOrEmpty(ActionName) || ActionName.Length > MaximumNameLength ||
                !Finite(Progress) || Progress < 0f || Progress > 1f ||
                !Finite(Speed) || Speed < 0f || Speed > 16f) return false;
            foreach (char c in ActionName)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                    !(c >= '0' && c <= '9') && c != '_') return false;
            return true;
        }

        public CoopCampaignMapPartyAnimation Clone() =>
            (CoopCampaignMapPartyAnimation)MemberwiseClone();

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
