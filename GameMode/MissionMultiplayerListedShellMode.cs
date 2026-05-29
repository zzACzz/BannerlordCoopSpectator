using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Multiplayer;

namespace CoopSpectator.GameMode
{
    /// <summary>
    /// Explicit listed-server ingress mode bound to the official TeamDeathmatch id.
    /// It preserves the public listed contract while bypassing native TDM mission assembly.
    /// </summary>
    public sealed class MissionMultiplayerListedShellMode : MissionBasedMultiplayerGameMode
    {
        private const string OfficialTeamDeathmatchMissionShell = "MultiplayerTeamDeathmatch";

        public MissionMultiplayerListedShellMode(string name)
            : base(name)
        {
        }

        public override void StartMultiplayerGame(string scene)
        {
            if (GameNetwork.IsServer)
                ListedShellMissionSessionState.ArmServerStartup(scene, "MissionMultiplayerListedShellMode.StartMultiplayerGame");

            ModLogger.Info(
                "MissionMultiplayerListedShellMode: opening explicit listed ingress. " +
                "Scene=" + (scene ?? string.Empty) +
                " OfficialId=" + ((MultiplayerGameMode)this).Name +
                " MissionShell=" + OfficialTeamDeathmatchMissionShell + ".");
            MissionInitializerRecord record = new MissionInitializerRecord(scene);
            MissionState.OpenNew(
                OfficialTeamDeathmatchMissionShell,
                record,
                ListedShellMissionBehaviorFactory.CreateMissionBehaviors);
        }
    }
}
