using System;

namespace CoopSpectator.Infrastructure
{
    internal sealed class CoopNavalMapWaterPreparation
    {
        private object _scene;
        internal bool Attempted { get; private set; }
        internal bool InitializationStarted { get; private set; }
        internal bool RetirementAttempted { get; private set; }

        internal bool Ensure(object scene, bool canonicalNaval, bool ready, Func<bool> prerequisites, Action initialize)
        {
            if (!ReferenceEquals(scene, _scene)) { Reset(); _scene = scene; }
            if (scene == null || !canonicalNaval || !ready || Attempted) return false;
            if (prerequisites == null || initialize == null) throw new ArgumentNullException("water preparation");
            Attempted = true;
            if (!prerequisites()) throw new InvalidOperationException("Canonical Naval shader prerequisites are unavailable.");
            InitializationStarted = true;
            initialize();
            return true;
        }

        internal bool Retire(object scene, Action clearView, Action deleteWake)
        {
            if (scene == null || !ReferenceEquals(scene, _scene) || !InitializationStarted || RetirementAttempted)
                return false;
            if (clearView == null || deleteWake == null) throw new ArgumentNullException("water retirement");
            RetirementAttempted = true;
            clearView();
            deleteWake();
            return true;
        }

        internal void Reset()
        {
            _scene = null;
            Attempted = false;
            InitializationStarted = false;
            RetirementAttempted = false;
        }
    }
}
