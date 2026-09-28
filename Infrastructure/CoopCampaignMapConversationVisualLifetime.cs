using System;

namespace CoopSpectator.Infrastructure
{
    public enum CoopConversationVisualChange { Keep, Replace, Clear }

    // Resource-independent ownership decisions, exercised without native objects.
    public sealed class CoopCampaignMapConversationVisualLifetime
    {
        public const double ReadinessTimeoutSeconds = 15d;
        private CoopCampaignMapConversationTableauState _attempted;
        private double _elapsed;
        public bool IsReady { get; private set; }
        public bool IsFailed { get; private set; }

        // Operational readiness of this off-screen target, not pixel/visual parity.
        // SceneView.ReadyToRender remains false for the observed drawing tableau.
        public static bool IsTargetOperational(bool targetConfigured, bool loadedTargetSubmitted,
            int actorCount, int actorReadyMask) => targetConfigured && loadedTargetSubmitted &&
            actorCount > 0 && actorCount <= CoopCampaignMapConversationTableauCodec.MaxActors &&
            actorReadyMask == (1 << actorCount) - 1;

        public CoopConversationVisualChange Apply(CoopCampaignMapConversationTableauState state)
        {
            if (!CoopCampaignMapConversationTableauCodec.IsValid(state))
            {
                bool hadAttempt = _attempted != null;
                Reset();
                return hadAttempt ? CoopConversationVisualChange.Clear : CoopConversationVisualChange.Keep;
            }
            if (CoopCampaignMapConversationTableauCodec.Equivalent(_attempted, state))
                return CoopConversationVisualChange.Keep;
            _attempted = state.Clone();
            _elapsed = 0;
            IsReady = false;
            IsFailed = false;
            return CoopConversationVisualChange.Replace;
        }

        public bool Advance(double dt, bool resourcesReady)
        {
            if (_attempted == null || IsFailed) return false;
            if (IsReady) return true;
            if (double.IsNaN(dt) || double.IsInfinity(dt) || dt < 0) { Fail(); return false; }
            _elapsed += dt;
            if (resourcesReady) { IsReady = true; return true; }
            if (_elapsed >= ReadinessTimeoutSeconds) Fail();
            return false;
        }

        // A resized off-screen target must prove readiness again without
        // rebuilding actors or granting a retry to an already failed scene.
        public void InvalidateRenderTarget()
        {
            if (_attempted == null || IsFailed) return;
            _elapsed = 0d;
            IsReady = false;
        }

        public void Fail() { IsFailed = true; IsReady = false; }
        public void Reset() { _attempted = null; _elapsed = 0; IsReady = false; IsFailed = false; }
    }
}
