using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.GameMode
{
    /// <summary>
    /// Minimal client-side mission behavior for isolating crashes before the normal
    /// MP UI/components finish bootstrapping.
    /// </summary>
    public sealed class MissionMinimalClientDiagnosticMode : MissionLogic
    {
        public override void OnBehaviorInitialize()
        {
            ModLogger.Info("MissionMinimalClientDiagnosticMode OnBehaviorInitialize ENTER");
            base.OnBehaviorInitialize();
            ModLogger.Info("MissionMinimalClientDiagnosticMode OnBehaviorInitialize EXIT");
        }

        public override void AfterStart()
        {
            ModLogger.Info("MissionMinimalClientDiagnosticMode AfterStart ENTER");
            base.AfterStart();
            ModLogger.Info("MissionMinimalClientDiagnosticMode AfterStart EXIT");
        }
    }
}
