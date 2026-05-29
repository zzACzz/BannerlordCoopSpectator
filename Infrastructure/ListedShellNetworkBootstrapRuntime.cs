using System;
using System.Reflection;
using System.Threading.Tasks;
using CoopSpectator.GameMode;
using CoopSpectator.MissionBehaviors;
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

                string sentMessages = CoopSessionTransportPrimitives.SendInitializeCustomGameBootstrapBundle(
                    targetPeer,
                    targetPeer.IsAdmin,
                    inMission,
                    gameType,
                    scene,
                    token);

                ModLogger.Info(
                    "ListedShellNetworkBootstrapRuntime: sent coop-owned listed new-client bootstrap. " +
                    "Peer=" + (targetPeer.UserName ?? "unknown") +
                    " Scene=" + scene +
                    " GameType=" + gameType +
                    " MissionSessionToken=" + token +
                    " SentMessages=" + sentMessages + ".");
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
            if (message == null)
                return true;

            if (!ShouldOwnInitializeCustomGameReceive(message))
            {
                bool shouldOwnReceiveBootstrap = ListedShellClientSessionOwnershipState.ShouldOwnReceiveBootstrap();
                ModLogger.Info(
                    "ListedShellNetworkBootstrapRuntime: observed listed InitializeCustomGame receive without ownership. " +
                    "InMission=" + message.InMission +
                    " GameType=" + (message.GameType ?? string.Empty) +
                    " Map=" + (message.Map ?? string.Empty) +
                    " BattleIndex=" + message.BattleIndex +
                    " ShouldOwnReceiveBootstrap=" + shouldOwnReceiveBootstrap +
                    " Source=" + Normalize(source) + ".");
                return true;
            }

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

            if (ListedShellClientSessionOwnershipState.ShouldOwnReceiveBootstrap())
                return true;

            return string.Equals(message.GameType, CoopGameModeIds.OfficialTeamDeathmatch, StringComparison.Ordinal);
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
                    if (!CoopSessionTransportPrimitives.TryStartMissionSessionGame(
                            message.GameType,
                            message.Map,
                            "ListedShellNetworkBootstrapRuntime.HandleListedInitializeCustomGameReceiveAsync"))
                    {
                        ModLogger.Info("ListedShellNetworkBootstrapRuntime: listed InitializeCustomGame receive path did not start mission session.");
                    }

                    _ = EnsureListedClientBattleRuntimeBehaviorsAsync(
                        message.Map,
                        message.BattleIndex,
                        "ListedShellNetworkBootstrapRuntime.HandleListedInitializeCustomGameReceiveAsync");

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
            if (message == null)
                return true;

            if (!ShouldOwnLoadMissionReceive(message))
            {
                bool shouldOwnReceiveBootstrap = ListedShellClientSessionOwnershipState.ShouldOwnReceiveBootstrap();
                ModLogger.Info(
                    "ListedShellNetworkBootstrapRuntime: observed listed LoadMission receive without ownership. " +
                    "GameType=" + (message.GameType ?? string.Empty) +
                    " Map=" + (message.Map ?? string.Empty) +
                    " BattleIndex=" + message.BattleIndex +
                    " ShouldOwnReceiveBootstrap=" + shouldOwnReceiveBootstrap +
                    " Source=" + Normalize(source) + ".");
                return true;
            }

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

            if (ListedShellClientSessionOwnershipState.ShouldOwnReceiveBootstrap())
                return true;

            return string.Equals(message.GameType, CoopGameModeIds.OfficialTeamDeathmatch, StringComparison.Ordinal);
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

                CoopSessionTransportPrimitives.BeginClientMissionReceiveTransition();

                ResetClientIntermissionState(baseNetworkComponentInstance);

                if (IsMatchingListedMissionAlreadyActive(message.Map, message.BattleIndex))
                {
                    ModLogger.Info(
                        "ListedShellNetworkBootstrapRuntime: listed mission already active for received LoadMission; skipped duplicate StartMultiplayerGame. " +
                        "Map=" + (message.Map ?? string.Empty) +
                        " BattleIndex=" + message.BattleIndex + ".");
                    _ = EnsureListedClientBattleRuntimeBehaviorsAsync(
                        message.Map,
                        message.BattleIndex,
                        "ListedShellNetworkBootstrapRuntime.HandleListedLoadMissionReceiveAsync duplicate-active");
                    return;
                }

                if (!CoopSessionTransportPrimitives.TryStartMissionSessionGame(
                        message.GameType,
                        message.Map,
                        "ListedShellNetworkBootstrapRuntime.HandleListedLoadMissionReceiveAsync"))
                {
                    ModLogger.Info("ListedShellNetworkBootstrapRuntime: listed LoadMission receive path did not start mission session.");
                }

                _ = EnsureListedClientBattleRuntimeBehaviorsAsync(
                    message.Map,
                    message.BattleIndex,
                    "ListedShellNetworkBootstrapRuntime.HandleListedLoadMissionReceiveAsync");
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

            if (ShouldSuppressListedBattleIndexMismatchUnload(message, out string suppressionSummary))
            {
                ModLogger.Info(
                    "ListedShellNetworkBootstrapRuntime: suppressed listed UnloadMission receive during active battle bootstrap. " +
                    suppressionSummary +
                    " Source=" + Normalize(source) + ".");
                return false;
            }

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

        private static bool ShouldSuppressListedBattleIndexMismatchUnload(UnloadMission message, out string summary)
        {
            summary = string.Empty;
            if (message == null || !message.UnloadingForBattleIndexMismatch)
                return false;

            Mission mission = Mission.Current;
            if (mission == null || !SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(mission.SceneName ?? string.Empty))
                return false;

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            if (currentPhase >= CoopBattlePhase.BattleActive || currentPhase == CoopBattlePhase.None)
                return false;

            if (!ListedShellMissionSessionState.TryResolveTransportToken(mission, out int token) || token <= 0)
                return false;

            if (!CoopMissionNetworkBridge.IsClientCurrentBattleSnapshotApplied(out string readinessSummary))
                return false;

            summary =
                "MissionScene=" + (mission.SceneName ?? "null") +
                " MissionPhase=" + currentPhase +
                " MissionToken=" + token +
                " SnapshotReadiness={" + (readinessSummary ?? "unknown") + "}";
            return true;
        }

        public static async Task HandleListedUnloadMissionReceiveAsync(object baseNetworkComponentInstance, UnloadMission message)
        {
            try
            {
                CoopSessionTransportPrimitives.BeginClientLobbyMissionUnload();

                ResetClientIntermissionState(baseNetworkComponentInstance);

                Mission currentMission = Mission.Current;
                ListedShellMissionLobbyClientComponent listedClient = currentMission?.GetMissionBehavior<ListedShellMissionLobbyClientComponent>();
                listedClient?.SetServerEndingBeforeClientLoaded(message.UnloadingForBattleIndexMismatch);

                await WaitForMissionUnloadAsync();
                ListedShellClientSessionOwnershipState.Disarm(
                    "ListedShellNetworkBootstrapRuntime.HandleListedUnloadMissionReceiveAsync");
                CoopSessionTransportPrimitives.CompleteClientLobbyMissionUnload();
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

        public static void BeginListedClientBattleRuntimeLateAttachFromTransportStart(string source)
        {
            if (!GameNetwork.IsClient)
                return;

            _ = EnsureListedClientBattleRuntimeBehaviorsFromTransportStartAsync(source);
        }

        private static async Task EnsureListedClientBattleRuntimeBehaviorsAsync(string sceneName, int token, string source)
        {
            if (!GameNetwork.IsClient)
                return;

            string normalizedScene = Normalize(sceneName);
            for (int i = 0; i < 5000; i++)
            {
                Mission mission = Mission.Current;
                if (mission != null &&
                    string.Equals(Normalize(mission.SceneName), normalizedScene, StringComparison.Ordinal))
                {
                    ListedShellMissionSessionState.InitializeMission(
                        mission,
                        Normalize(source) + " ensure-client-runtime");
                    TryEnsureListedClientBattleRuntimeBehaviors(
                        mission,
                        token,
                        source);
                    return;
                }

                await Task.Delay(1);
            }

            ModLogger.Info(
                "ListedShellNetworkBootstrapRuntime: timed out while waiting to ensure listed client battle runtime behaviors. " +
                "Scene=" + normalizedScene +
                " BattleIndex=" + token +
                " Source=" + Normalize(source) + ".");
        }

        private static async Task EnsureListedClientBattleRuntimeBehaviorsFromTransportStartAsync(string source)
        {
            if (!GameNetwork.IsClient)
                return;

            for (int i = 0; i < 20000; i++)
            {
                Mission mission = Mission.Current;
                if (mission != null &&
                    mission.Mode == MissionMode.Battle &&
                    mission.GetMissionBehavior<ListedShellMissionLobbyClientComponent>() != null)
                {
                    ListedShellMissionSessionState.InitializeMission(
                        mission,
                        Normalize(source) + " transport-start-fallback");
                    TryEnsureListedClientBattleRuntimeBehaviors(
                        mission,
                        0,
                        source + " transport-start-fallback");
                    return;
                }

                await Task.Delay(1);
            }

            ModLogger.Info(
                "ListedShellNetworkBootstrapRuntime: transport-start fallback timed out while waiting for listed battle mission. " +
                "Source=" + Normalize(source) + ".");
        }

        private static void TryEnsureListedClientBattleRuntimeBehaviors(Mission mission, int token, string source)
        {
            if (mission == null || !GameNetwork.IsClient)
                return;

            bool attachedModeClient = false;
            bool attachedDiagnostic = false;
            bool attachedNetworkBridge = false;

            try
            {
                if (mission.GetMissionBehavior<MissionMultiplayerCoopBattleClient>() == null)
                {
                    var modeClient = new MissionMultiplayerCoopBattleClient();
                    mission.AddMissionBehavior(modeClient);
                    modeClient.OnBehaviorInitialize();
                    modeClient.AfterStart();
                    attachedModeClient = true;
                }

                if (mission.GetMissionBehavior<MissionBehaviorDiagnostic>() == null)
                {
                    var diagnostic = new MissionBehaviorDiagnostic();
                    mission.AddMissionBehavior(diagnostic);
                    diagnostic.OnBehaviorInitialize();
                    diagnostic.AfterStart();
                    attachedDiagnostic = true;
                }

                if (mission.GetMissionBehavior<CoopMissionNetworkBridge>() == null)
                {
                    var bridge = new CoopMissionNetworkBridge();
                    mission.AddMissionBehavior(bridge);
                    bridge.OnAfterMissionCreated();
                    bridge.OnBehaviorInitialize();
                    bridge.AfterStart();
                    attachedNetworkBridge = true;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "ListedShellNetworkBootstrapRuntime.TryEnsureListedClientBattleRuntimeBehaviors failed. " +
                    "Scene=" + Normalize(mission.SceneName) +
                    " BattleIndex=" + token +
                    " Source=" + Normalize(source) + ".",
                    ex);
                return;
            }

            if (!attachedModeClient && !attachedDiagnostic && !attachedNetworkBridge)
                return;

            ModLogger.Info(
                "ListedShellNetworkBootstrapRuntime: attached missing listed client battle runtime behaviors after mission open. " +
                "Scene=" + Normalize(mission.SceneName) +
                " BattleIndex=" + token +
                " AttachedModeClient=" + attachedModeClient +
                " AttachedDiagnostic=" + attachedDiagnostic +
                " AttachedNetworkBridge=" + attachedNetworkBridge +
                " Source=" + Normalize(source) + ".");
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
