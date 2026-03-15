using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Commands
{
    public static class CoopBattlePhaseConsoleCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("phase_status", "coop")]
        public static string PhaseStatus(List<string> args)
        {
            CoopBattlePhaseStateSnapshot snapshot = GameNetwork.IsServer
                ? CoopBattlePhaseRuntimeState.GetCurrent()
                : CoopBattlePhaseBridgeFile.ReadStatus();

            if (snapshot == null)
                snapshot = CoopBattlePhaseRuntimeState.GetCurrent();

            return "Phase=" + snapshot.Phase +
                   " Source=" + (snapshot.Source ?? "unknown") +
                   " Mission=" + (snapshot.MissionName ?? string.Empty) +
                   " UpdatedUtc=" + snapshot.UpdatedUtc.ToString("O");
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("start_battle", "coop")]
        public static string StartBattle(List<string> args)
        {
            if (!GameNetwork.IsServer)
            {
                bool written = CoopBattlePhaseBridgeFile.WriteStartBattleRequest("SP console");
                return written
                    ? "Battle start requested for dedicated."
                    : "ERROR: Failed to write battle start request.";
            }

            Mission mission = Mission.Current;
            if (mission == null)
                return "ERROR: No active mission.";

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            if (currentPhase < CoopBattlePhase.PreBattleHold)
                return "ERROR: Battle is not ready. Current phase=" + currentPhase + ".";

            if (currentPhase >= CoopBattlePhase.BattleActive)
                return "Battle already active.";

            CoopBattlePhaseRuntimeState.SetPhase(
                CoopBattlePhase.BattleActive,
                "coop.start_battle",
                mission,
                allowRegression: false);

            return "Battle started. Phase=BattleActive.";
        }
    }
}
