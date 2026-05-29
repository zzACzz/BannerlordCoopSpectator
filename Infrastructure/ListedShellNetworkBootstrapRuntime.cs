using System;
using System.Reflection;
using System.Threading.Tasks;
using CoopSpectator.GameMode;
using CoopSpectator.Patches;
using NetworkMessages.FromServer;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal static class ListedShellNetworkBootstrapRuntime
    {
        private static MethodInfo _syncRelevantGameOptionsToServerMethod;
        private static MethodInfo _setCurrentIntermissionTimerMethod;
        private static MethodInfo _setClientIntermissionStateMethod;
        private static bool _contractsInitialized;

        public static void InitializeBaseNetworkContracts(Type baseNetworkComponentType)
        {
            if (_contractsInitialized || baseNetworkComponentType == null)
                return;

            _syncRelevantGameOptionsToServerMethod = typeof(GameNetwork).GetMethod(
                "SyncRelevantGameOptionsToServer",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            _setCurrentIntermissionTimerMethod = baseNetworkComponentType.GetMethod(
                "set_CurrentIntermissionTimer",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _setClientIntermissionStateMethod = baseNetworkComponentType.GetMethod(
                "set_ClientIntermissionState",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _contractsInitialized = true;
        }

        public static bool TryHandleListedNewClientConnect(Mission mission, PlayerConnectionInfo playerConnectionInfo, string source)
        {
            if (!GameNetwork.IsServer)
                return true;

            NetworkCommunicator targetPeer = playerConnectionInfo?.NetworkPeer;
            if (targetPeer == null || targetPeer.IsServerPeer)
                return true;

            if (!ShouldOwnListedShellInitializeCustomGameIngress(mission))
                return true;

            bool shouldContinueNative = !TrySendListedNewClientBootstrap(mission, targetPeer);
            if (!shouldContinueNative)
            {
                ModLogger.Info(
                    "ListedShellNetworkBootstrapRuntime: owned listed HandleNewClientConnect bootstrap send. " +
                    "Peer=" + (targetPeer.UserName ?? "unknown") +
                    " Source=" + Normalize(source) + ".");
            }

            return shouldContinueNative;
        }

        public static bool TrySendListedNewClientBootstrap(Mission mission, NetworkCommunicator targetPeer)
        {
            try
            {
                if (targetPeer == null || targetPeer.IsServerPeer)
                    return false;

                CoopSessionTransportPrimitives.SendServerMessage(targetPeer, new MultiplayerOptionsInitial());
                CoopSessionTransportPrimitives.SendServerMessage(targetPeer, new MultiplayerOptionsImmediate());

                if (targetPeer.IsAdmin)
                    CoopSessionTransportPrimitives.SendServerMessage(targetPeer, new MultiplayerOptionsDefault());

                bool inMission = !GameNetwork.IsDedicatedServer || mission != null;
                string scene = inMission ? (mission?.SceneName ?? string.Empty) : string.Empty;
                string gameType = inMission ? CoopGameModeIds.OfficialTeamDeathmatch : string.Empty;
                if (!ListedShellMissionSessionState.TryResolveTransportToken(mission, out int token))
                {
                    ModLogger.Info(
                        "ListedShellNetworkBootstrapRuntime: listed mission-session token was unavailable during HandleNewClientConnect. " +
                        "Scene=" + (mission?.SceneName ?? string.Empty) +
                        " OwnershipState=missing-listed-session-token.");
                    return false;
                }

                CoopSessionTransportPrimitives.SendServerMessage(targetPeer, new InitializeCustomGameMessage(inMission, gameType, scene, token));

                ModLogger.Info(
                    "ListedShellNetworkBootstrapRuntime: sent coop-owned listed new-client bootstrap. " +
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
                ModLogger.Error("ListedShellNetworkBootstrapRuntime.TrySendListedNewClientBootstrap failed.", ex);
                return false;
            }
        }

        public static bool TryHandleInitializeCustomGameReceive(object baseMessage, string source)
        {
            if (!GameNetwork.IsClient)
                return true;

            InitializeCustomGameMessage message = baseMessage as InitializeCustomGameMessage;
            if (message == null || !ShouldOwnInitializeCustomGameReceive(message))
                return true;

            _ = HandleListedInitializeCustomGameReceiveAsync(message);
            ModLogger.Info(
                "ListedShellNetworkBootstrapRuntime: intercepted listed InitializeCustomGame receive. " +
                "InMission=" + message.InMission +
                " GameType=" + (message.GameType ?? string.Empty) +
                " Map=" + (message.Map ?? string.Empty) +
                " BattleIndex=" + message.BattleIndex +
                " Source=" + Normalize(source) + ".");
            return false;
        }

        public static bool ShouldOwnInitializeCustomGameReceive(InitializeCustomGameMessage message)
        {
            if (message == null)
                return false;

            if (!string.Equals(message.GameType, CoopGameModeIds.OfficialTeamDeathmatch, StringComparison.Ordinal))
                return false;

            return ListedShellClientSessionOwnershipState.ShouldOwnReceiveBootstrap();
        }

        public static async Task HandleListedInitializeCustomGameReceiveAsync(InitializeCustomGameMessage message)
        {
            try
            {
                await Task.Delay(200);
                await WaitForListedBootstrapReadinessAsync();

                if (message.InMission)
                {
                    ModLogger.Info(
                        "ListedShellNetworkBootstrapRuntime: starting listed mission from coop-owned receive path. " +
                        "GameType=" + (message.GameType ?? string.Empty) +
                        " Map=" + (message.Map ?? string.Empty) +
                        " BattleIndex=" + message.BattleIndex + ".");

                    ListedShellMissionSessionState.AdoptRemoteMissionToken(
                        message.Map,
                        message.BattleIndex,
                        "ListedShellNetworkBootstrapRuntime.HandleListedInitializeCustomGameReceiveAsync");
                    if (!TaleWorlds.MountAndBlade.Module.CurrentModule.StartMultiplayerGame(message.GameType, message.Map))
                    {
                        ModLogger.Info("ListedShellNetworkBootstrapRuntime: StartMultiplayerGame returned false for listed InitializeCustomGame receive path.");
                    }

                    return;
                }

                CoopSessionTransportPrimitives.DisableGlobalLoadingWindow();
                TrySyncRelevantGameOptionsToServer();
                ModLogger.Info("ListedShellNetworkBootstrapRuntime: completed listed non-mission InitializeCustomGame receive path.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellNetworkBootstrapRuntime.HandleListedInitializeCustomGameReceiveAsync failed.", ex);
            }
        }

        public static bool TryHandleLoadMissionReceive(object baseNetworkComponentInstance, object baseMessage, string source)
        {
            if (!GameNetwork.IsClient)
                return true;

            LoadMission message = baseMessage as LoadMission;
            if (message == null || !ShouldOwnLoadMissionReceive(message))
                return true;

            _ = HandleListedLoadMissionReceiveAsync(baseNetworkComponentInstance, message);
            ModLogger.Info(
                "ListedShellNetworkBootstrapRuntime: intercepted listed LoadMission receive. " +
                "GameType=" + (message.GameType ?? string.Empty) +
                " Map=" + (message.Map ?? string.Empty) +
                " BattleIndex=" + message.BattleIndex +
                " Source=" + Normalize(source) + ".");
            return false;
        }

        public static bool ShouldOwnLoadMissionReceive(LoadMission message)
        {
            if (message == null)
                return false;

            if (!string.Equals(message.GameType, CoopGameModeIds.OfficialTeamDeathmatch, StringComparison.Ordinal))
                return false;

            return ListedShellClientSessionOwnershipState.ShouldOwnReceiveBootstrap();
        }

        public static async Task HandleListedLoadMissionReceiveAsync(object baseNetworkComponentInstance, LoadMission message)
        {
            try
            {
                ListedShellMissionSessionState.AdoptRemoteMissionToken(
                    message.Map,
                    message.BattleIndex,
                    "ListedShellNetworkBootstrapRuntime.HandleListedLoadMissionReceiveAsync");

                await WaitForListedMissionOpenReadinessAsync(message.Map, message.BattleIndex);

                CoopSessionTransportPrimitives.MarkLocalPeerUnsynchronized();

                ResetClientIntermissionState(baseNetworkComponentInstance);

                if (IsMatchingListedMissionAlreadyActive(message.Map, message.BattleIndex))
                {
                    ModLogger.Info(
                        "ListedShellNetworkBootstrapRuntime: listed mission already active for received LoadMission; skipped duplicate StartMultiplayerGame. " +
                        "Map=" + (message.Map ?? string.Empty) +
                        " BattleIndex=" + message.BattleIndex + ".");
                    return;
                }

                if (!TaleWorlds.MountAndBlade.Module.CurrentModule.StartMultiplayerGame(message.GameType, message.Map))
                {
                    ModLogger.Info(
                        "ListedShellNetworkBootstrapRuntime: StartMultiplayerGame returned false for listed LoadMission receive path. " +
                        "GameType=" + (message.GameType ?? string.Empty) +
                        " Map=" + (message.Map ?? string.Empty) + ".");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellNetworkBootstrapRuntime.HandleListedLoadMissionReceiveAsync failed.", ex);
            }
        }

        public static bool TryHandleUnloadMissionReceive(object baseNetworkComponentInstance, object baseMessage, string source)
        {
            if (!GameNetwork.IsClient)
                return true;

            UnloadMission message = baseMessage as UnloadMission;
            if (message == null || !ShouldOwnUnloadMissionReceive())
                return true;

            _ = HandleListedUnloadMissionReceiveAsync(baseNetworkComponentInstance, message);
            ModLogger.Info(
                "ListedShellNetworkBootstrapRuntime: intercepted listed UnloadMission receive. " +
                "UnloadingForBattleIndexMismatch=" + message.UnloadingForBattleIndexMismatch +
                " Source=" + Normalize(source) + ".");
            return false;
        }

        public static bool ShouldOwnUnloadMissionReceive()
        {
            Mission mission = Mission.Current;
            if (mission != null &&
                (mission.GetMissionBehavior<ListedShellCompatibilityMode>() != null ||
                 mission.GetMissionBehavior<ListedShellCompatibilityModeClient>() != null))
            {
                return true;
            }

            return ListedShellClientSessionOwnershipState.ShouldOwnReceiveBootstrap();
        }

        public static async Task HandleListedUnloadMissionReceiveAsync(object baseNetworkComponentInstance, UnloadMission message)
        {
            try
            {
                CoopSessionTransportPrimitives.MarkLocalPeerUnsynchronized();

                ResetClientIntermissionState(baseNetworkComponentInstance);

                Mission currentMission = Mission.Current;
                ListedShellMissionLobbyClientComponent listedClient = currentMission?.GetMissionBehavior<ListedShellMissionLobbyClientComponent>();
                listedClient?.SetServerEndingBeforeClientLoaded(message.UnloadingForBattleIndexMismatch);

                CoopSessionTransportPrimitives.EndClientLobbyMissionAndResetChat();

                await WaitForMissionUnloadAsync();
                ListedShellClientSessionOwnershipState.Disarm(
                    "ListedShellNetworkBootstrapRuntime.HandleListedUnloadMissionReceiveAsync");
                CoopSessionTransportPrimitives.DisableGlobalLoadingWindow();
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellNetworkBootstrapRuntime.HandleListedUnloadMissionReceiveAsync failed.", ex);
            }
        }

        private static bool ShouldOwnListedShellInitializeCustomGameIngress(Mission mission)
        {
            if (mission == null)
                return false;

            return mission.GetMissionBehavior<ListedShellCompatibilityMode>() != null ||
                mission.GetMissionBehavior<ListedShellCompatibilityModeClient>() != null;
        }

        private static async Task WaitForListedBootstrapReadinessAsync()
        {
            for (int i = 0; i < 2000; i++)
            {
                if (IsListedBootstrapReady())
                    return;

                await Task.Delay(1);
            }

            ModLogger.Info("ListedShellNetworkBootstrapRuntime: listed bootstrap readiness wait timed out; proceeding with coop-owned receive path.");
        }

        private static bool IsListedBootstrapReady()
        {
            GameStateManager manager = GameStateManager.Current;
            return manager?.ActiveState != null && TaleWorlds.MountAndBlade.Module.CurrentModule != null;
        }

        private static void TrySyncRelevantGameOptionsToServer()
        {
            try
            {
                _syncRelevantGameOptionsToServerMethod?.Invoke(null, Array.Empty<object>());
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellNetworkBootstrapRuntime: SyncRelevantGameOptionsToServer invoke failed: " + ex.Message);
            }
        }

        private static async Task WaitForListedMissionOpenReadinessAsync(string sceneName, int token)
        {
            for (int i = 0; i < 2000; i++)
            {
                if (IsMatchingListedMissionAlreadyActive(sceneName, token))
                    return;

                GameState activeState = GameStateManager.Current?.ActiveState;
                if (!(activeState is MissionState))
                    return;

                await Task.Delay(1);
            }

            ModLogger.Info(
                "ListedShellNetworkBootstrapRuntime: listed LoadMission readiness wait timed out; proceeding with coop-owned receive path. " +
                "Scene=" + Normalize(sceneName) +
                " BattleIndex=" + token + ".");
        }

        private static bool IsMatchingListedMissionAlreadyActive(string sceneName, int token)
        {
            Mission currentMission = Mission.Current;
            if (currentMission == null)
                return false;

            if (currentMission.GetMissionBehavior<ListedShellCompatibilityMode>() == null &&
                currentMission.GetMissionBehavior<ListedShellCompatibilityModeClient>() == null)
            {
                return false;
            }

            if (!string.Equals(Normalize(currentMission.SceneName), Normalize(sceneName), StringComparison.Ordinal))
                return false;

            return ListedShellMissionSessionState.TryResolveTransportToken(currentMission, out int currentToken) &&
                currentToken == token;
        }

        private static async Task WaitForMissionUnloadAsync()
        {
            for (int i = 0; i < 5000; i++)
            {
                if (Mission.Current == null)
                    return;

                await Task.Delay(1);
            }

            ModLogger.Info("ListedShellNetworkBootstrapRuntime: mission unload wait timed out after coop-owned listed unload path.");
        }

        private static void ResetClientIntermissionState(object instance)
        {
            try
            {
                _setCurrentIntermissionTimerMethod?.Invoke(instance, new object[] { 0f });

                if (_setClientIntermissionStateMethod != null)
                {
                    object intermissionState = Enum.ToObject(_setClientIntermissionStateMethod.GetParameters()[0].ParameterType, 0);
                    _setClientIntermissionStateMethod.Invoke(instance, new[] { intermissionState });
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellNetworkBootstrapRuntime: failed to reset client intermission state: " + ex.Message);
            }
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
