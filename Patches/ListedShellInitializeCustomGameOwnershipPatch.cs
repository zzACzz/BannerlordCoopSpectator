using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using NetworkMessages.FromServer;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Owns the listed-shell InitializeCustomGameMessage send gate so late join bootstrap
    /// is driven directly from the explicit listed mission marker instead of custom/community lobby state.
    /// </summary>
    internal static class ListedShellInitializeCustomGameOwnershipPatch
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                Type targetType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.Multiplayer.NetworkComponents.BaseNetworkComponent");
                if (targetType == null)
                {
                    ModLogger.Info("ListedShellInitializeCustomGameOwnershipPatch: BaseNetworkComponent type not found. Skip.");
                    return;
                }

                MethodInfo targetMethod = targetType.GetMethod(
                    "HandleNewClientConnect",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(PlayerConnectionInfo) },
                    null);
                MethodInfo prefixMethod = typeof(ListedShellInitializeCustomGameOwnershipPatch).GetMethod(
                    nameof(HandleNewClientConnect_Prefix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (targetMethod == null || prefixMethod == null)
                {
                    ModLogger.Info("ListedShellInitializeCustomGameOwnershipPatch: target/prefix method not found. Skip.");
                    return;
                }

                harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod));
                ModLogger.Info("ListedShellInitializeCustomGameOwnershipPatch: patched BaseNetworkComponent.HandleNewClientConnect.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellInitializeCustomGameOwnershipPatch.Apply failed.", ex);
            }
        }

        private static bool HandleNewClientConnect_Prefix(object __instance, PlayerConnectionInfo playerConnectionInfo)
        {
            if (!GameNetwork.IsServer)
                return true;

            NetworkCommunicator targetPeer = playerConnectionInfo?.NetworkPeer;
            if (targetPeer == null || targetPeer.IsServerPeer)
                return true;

            Mission mission = Mission.Current;
            if (!ShouldOwnListedShellInitializeCustomGameIngress(mission))
                return true;

            return !TryOwnListedShellNewClientConnect(mission, targetPeer);
        }

        private static bool ShouldOwnListedShellInitializeCustomGameIngress(Mission mission)
        {
            if (mission == null)
                return false;

            return mission.GetMissionBehavior<ListedShellCompatibilityMode>() != null ||
                mission.GetMissionBehavior<ListedShellCompatibilityModeClient>() != null;
        }

        private static bool TryOwnListedShellNewClientConnect(Mission mission, NetworkCommunicator targetPeer)
        {
            try
            {
                SendServerMessage(targetPeer, new MultiplayerOptionsInitial());
                SendServerMessage(targetPeer, new MultiplayerOptionsImmediate());

                if (targetPeer.IsAdmin)
                    SendServerMessage(targetPeer, new MultiplayerOptionsDefault());

                bool inMission = !GameNetwork.IsDedicatedServer || mission != null;
                string scene = inMission ? (mission?.SceneName ?? string.Empty) : string.Empty;
                string gameType = inMission ? CoopGameModeIds.OfficialTeamDeathmatch : string.Empty;
                if (!ListedShellMissionSessionState.TryResolveTransportToken(mission, out int token))
                {
                    ModLogger.Info(
                        "ListedShellInitializeCustomGameOwnershipPatch: listed mission-session token was unavailable during HandleNewClientConnect. " +
                        "Scene=" + (mission?.SceneName ?? string.Empty) +
                        " OwnershipState=missing-listed-session-token.");
                    return false;
                }

                SendServerMessage(targetPeer, new InitializeCustomGameMessage(inMission, gameType, scene, token));

                ModLogger.Info(
                    "ListedShellInitializeCustomGameOwnershipPatch: owned listed HandleNewClientConnect bootstrap without native intermission/custom-lobby replay. " +
                    "Peer=" + (targetPeer.UserName ?? "unknown") +
                    " Scene=" + scene +
                    " GameType=" + gameType +
                    " MissionSessionToken=" + token +
                    " SentMessages=MultiplayerOptionsInitial,MultiplayerOptionsImmediate" +
                    (targetPeer.IsAdmin ? ",MultiplayerOptionsDefault" : string.Empty) +
                    ",InitializeCustomGameMessage.");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellInitializeCustomGameOwnershipPatch.TryOwnListedShellNewClientConnect failed.", ex);
                return false;
            }
        }

        private static void SendServerMessage(NetworkCommunicator targetPeer, TaleWorlds.MountAndBlade.Network.Messages.GameNetworkMessage message)
        {
            GameNetwork.BeginModuleEventAsServer(targetPeer);
            GameNetwork.WriteMessage(message);
            GameNetwork.EndModuleEventAsServer();
        }
    }
}
