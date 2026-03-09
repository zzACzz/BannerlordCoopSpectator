using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.GameMode
{
    /// <summary>
    /// Мінімальний серверний behavior для ізоляції крашу нижче за vanilla TDM bootstrap.
    /// Не створює команди, спавн чи представників — лише логує lifecycle місії.
    /// </summary>
    public sealed class MissionMinimalServerDiagnosticMode : MissionLogic
    {
        public override void OnBehaviorInitialize()
        {
            ModLogger.Info("MissionMinimalServerDiagnosticMode OnBehaviorInitialize ENTER");
            base.OnBehaviorInitialize();
            ModLogger.Info("MissionMinimalServerDiagnosticMode OnBehaviorInitialize EXIT");
        }

        public override void AfterStart()
        {
            ModLogger.Info("MissionMinimalServerDiagnosticMode AfterStart ENTER");
            base.AfterStart();
            ModLogger.Info("MissionMinimalServerDiagnosticMode AfterStart EXIT");
        }
    }
}
