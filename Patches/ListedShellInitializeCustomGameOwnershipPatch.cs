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
        private static FieldInfo _baseNetworkComponentDataField;
        private static MethodInfo _ensureBaseNetworkComponentDataMethod;

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
                MethodInfo postfixMethod = typeof(ListedShellInitializeCustomGameOwnershipPatch).GetMethod(
                    nameof(HandleNewClientConnect_Postfix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (targetMethod == null || prefixMethod == null || postfixMethod == null)
                {
                    ModLogger.Info("ListedShellInitializeCustomGameOwnershipPatch: target/prefix/postfix method not found. Skip.");
                    return;
                }

                _baseNetworkComponentDataField = targetType.GetField("_baseNetworkComponentData", BindingFlags.Instance | BindingFlags.NonPublic);
                _ensureBaseNetworkComponentDataMethod = targetType.GetMethod("EnsureBaseNetworkComponentData", BindingFlags.Instance | BindingFlags.NonPublic);
                harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod), postfix: new HarmonyMethod(postfixMethod));
                ModLogger.Info("ListedShellInitializeCustomGameOwnershipPatch: patched BaseNetworkComponent.HandleNewClientConnect.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellInitializeCustomGameOwnershipPatch.Apply failed.", ex);
            }
        }

        private static void HandleNewClientConnect_Prefix(object __instance, PlayerConnectionInfo playerConnectionInfo, ref bool __state)
        {
            __state = false;

            if (!GameNetwork.IsServer)
                return;

            NetworkCommunicator targetPeer = playerConnectionInfo?.NetworkPeer;
            if (targetPeer == null || targetPeer.IsServerPeer)
                return;

            Mission mission = Mission.Current;
            if (!ShouldOwnListedShellInitializeCustomGameIngress(mission))
                return;

            EnsureBaseNetworkComponentData(__instance);
            __state = true;
        }

        private static void HandleNewClientConnect_Postfix(object __instance, PlayerConnectionInfo playerConnectionInfo, bool __state)
        {
            if (!__state || !GameNetwork.IsServer)
                return;

            NetworkCommunicator targetPeer = playerConnectionInfo?.NetworkPeer;
            if (targetPeer == null || targetPeer.IsServerPeer)
                return;

            Mission mission = Mission.Current;
            if (!ShouldOwnListedShellInitializeCustomGameIngress(mission))
                return;

            bool inMission = !GameNetwork.IsDedicatedServer || mission != null;
            string scene = inMission ? (mission?.SceneName ?? string.Empty) : string.Empty;
            string gameType = inMission ? CoopGameModeIds.OfficialTeamDeathmatch : string.Empty;
            int currentBattleIndex = GetCurrentBattleIndex(__instance);

            GameNetwork.BeginModuleEventAsServer(targetPeer);
            GameNetwork.WriteMessage(new InitializeCustomGameMessage(inMission, gameType, scene, currentBattleIndex));
            GameNetwork.EndModuleEventAsServer();

            ModLogger.Info(
                "ListedShellInitializeCustomGameOwnershipPatch: sent coop-owned listed InitializeCustomGameMessage from HandleNewClientConnect. " +
                "Peer=" + (targetPeer.UserName ?? "unknown") +
                " Scene=" + scene +
                " GameType=" + gameType +
                " CurrentBattleIndex=" + currentBattleIndex +
                " OwnershipState=owned-listed-bootstrap.");
        }

        private static bool ShouldOwnListedShellInitializeCustomGameIngress(Mission mission)
        {
            if (mission == null)
                return false;

            return mission.GetMissionBehavior<ListedShellCompatibilityMode>() != null ||
                mission.GetMissionBehavior<ListedShellCompatibilityModeClient>() != null;
        }

        private static void EnsureBaseNetworkComponentData(object instance)
        {
            try
            {
                _ensureBaseNetworkComponentDataMethod?.Invoke(instance, Array.Empty<object>());
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellInitializeCustomGameOwnershipPatch: EnsureBaseNetworkComponentData invoke failed: " + ex.Message);
            }
        }

        private static int GetCurrentBattleIndex(object instance)
        {
            try
            {
                BaseNetworkComponentData data = _baseNetworkComponentDataField?.GetValue(instance) as BaseNetworkComponentData;
                return data?.CurrentBattleIndex ?? -1;
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellInitializeCustomGameOwnershipPatch: failed to read CurrentBattleIndex: " + ex.Message);
                return -1;
            }
        }
    }
}
