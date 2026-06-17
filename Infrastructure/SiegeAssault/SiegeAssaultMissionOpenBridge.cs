using System;
using CoopSpectator.Campaign;
using CoopSpectator.Network.Messages;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal static class SiegeAssaultMissionOpenBridge
    {
        private const string MultiplayerBattleShell = "MultiplayerBattle";
        private const string SiegeMissionWithDeploymentShell = "SiegeMissionWithDeployment";
        private static readonly object Sync = new object();
        private static readonly TimeSpan ContractTtl = TimeSpan.FromMinutes(5);

        private static PreOpenContract _activeContract;
        private static DateTime _activeContractUtc = DateTime.MinValue;

        internal readonly struct PreOpenContract
        {
            public PreOpenContract(
                string runtimeScene,
                string scenarioKind,
                string siegeSubtype,
                string requestedMissionShell,
                string liveMissionShell,
                bool disableHybridBattleShellDeploymentBridge,
                bool enableLiveDeploymentControllers,
                string diagnostics)
            {
                RuntimeScene = Normalize(runtimeScene);
                ScenarioKind = Normalize(scenarioKind);
                SiegeSubtype = Normalize(siegeSubtype);
                RequestedMissionShell = Normalize(requestedMissionShell);
                LiveMissionShell = Normalize(liveMissionShell);
                DisableHybridBattleShellDeploymentBridge = disableHybridBattleShellDeploymentBridge;
                EnableLiveDeploymentControllers = enableLiveDeploymentControllers;
                Diagnostics = Normalize(diagnostics);
            }

            public string RuntimeScene { get; }

            public string ScenarioKind { get; }

            public string SiegeSubtype { get; }

            public string RequestedMissionShell { get; }

            public string LiveMissionShell { get; }

            public bool DisableHybridBattleShellDeploymentBridge { get; }

            public bool EnableLiveDeploymentControllers { get; }

            public string Diagnostics { get; }

            public bool IsValid =>
                !string.IsNullOrWhiteSpace(RuntimeScene) &&
                !string.IsNullOrWhiteSpace(LiveMissionShell);

            public bool IsSiegeAssaultWithDeployment =>
                string.Equals(SiegeSubtype, "SiegeAssault", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(RequestedMissionShell, SiegeMissionWithDeploymentShell, StringComparison.Ordinal);

            public bool UsesFallbackLiveShell =>
                IsSiegeAssaultWithDeployment &&
                !string.Equals(LiveMissionShell, RequestedMissionShell, StringComparison.Ordinal);

            public string Describe()
            {
                return
                    "RuntimeScene=" + (RuntimeScene ?? string.Empty) +
                    " ScenarioKind=" + (ScenarioKind ?? string.Empty) +
                    " SiegeSubtype=" + (SiegeSubtype ?? string.Empty) +
                    " RequestedMissionShell=" + (RequestedMissionShell ?? string.Empty) +
                    " LiveMissionShell=" + (LiveMissionShell ?? string.Empty) +
                    " UsesFallbackLiveShell=" + UsesFallbackLiveShell +
                    " DisableHybridBattleShellDeploymentBridge=" + DisableHybridBattleShellDeploymentBridge +
                    " EnableLiveDeploymentControllers=" + EnableLiveDeploymentControllers +
                    " Diagnostics=" + (Diagnostics ?? string.Empty);
            }
        }

        public static PreOpenContract Resolve(string runtimeScene, string defaultMissionShell)
        {
            string normalizedScene = Normalize(runtimeScene);
            string normalizedDefaultMissionShell = Normalize(defaultMissionShell);
            BattleScenarioContextMessage scenarioContext = TryResolveScenarioContext();
            string siegeSubtype = scenarioContext?.SiegeContext?.SiegeSubtype ?? string.Empty;
            string requestedMissionShell = scenarioContext?.SiegeContext?.MissionShell ?? string.Empty;

            if (string.IsNullOrWhiteSpace(normalizedScene))
            {
                return new PreOpenContract(
                    normalizedScene,
                    scenarioContext?.ScenarioKind,
                    siegeSubtype,
                    normalizedDefaultMissionShell,
                    normalizedDefaultMissionShell,
                    disableHybridBattleShellDeploymentBridge: false,
                    enableLiveDeploymentControllers: false,
                    diagnostics: "runtime-scene-empty");
            }

            if (scenarioContext?.IsSiegeBattle != true)
            {
                return new PreOpenContract(
                    normalizedScene,
                    scenarioContext?.ScenarioKind,
                    siegeSubtype,
                    normalizedDefaultMissionShell,
                    normalizedDefaultMissionShell,
                    disableHybridBattleShellDeploymentBridge: false,
                    enableLiveDeploymentControllers: false,
                    diagnostics: "not-siege-battle");
            }

            if (SceneRuntimeClassifier.IsOfficialMultiplayerBattleScene(normalizedScene))
            {
                return new PreOpenContract(
                    normalizedScene,
                    scenarioContext?.ScenarioKind,
                    siegeSubtype,
                    normalizedDefaultMissionShell,
                    normalizedDefaultMissionShell,
                    disableHybridBattleShellDeploymentBridge: false,
                    enableLiveDeploymentControllers: false,
                    diagnostics: "official-multiplayer-battle-scene");
            }

            if (!string.Equals(siegeSubtype, "SiegeAssault", StringComparison.OrdinalIgnoreCase))
            {
                return new PreOpenContract(
                    normalizedScene,
                    scenarioContext?.ScenarioKind,
                    siegeSubtype,
                    Normalize(requestedMissionShell.Length > 0 ? requestedMissionShell : normalizedDefaultMissionShell),
                    normalizedDefaultMissionShell,
                    disableHybridBattleShellDeploymentBridge: false,
                    enableLiveDeploymentControllers: false,
                    diagnostics: "non-siege-assault-subtype");
            }

            bool withDeploymentShell =
                CampaignMissionShellRuntimeState.IsWithDeploymentMissionShell(requestedMissionShell);
            if (!withDeploymentShell)
            {
                return new PreOpenContract(
                    normalizedScene,
                    scenarioContext?.ScenarioKind,
                    siegeSubtype,
                    Normalize(requestedMissionShell.Length > 0 ? requestedMissionShell : normalizedDefaultMissionShell),
                    normalizedDefaultMissionShell,
                    disableHybridBattleShellDeploymentBridge: false,
                    enableLiveDeploymentControllers: false,
                    diagnostics: "siege-assault-without-with-deployment-shell");
            }

            return new PreOpenContract(
                normalizedScene,
                scenarioContext?.ScenarioKind,
                siegeSubtype,
                SiegeMissionWithDeploymentShell,
                SiegeMissionWithDeploymentShell,
                disableHybridBattleShellDeploymentBridge: true,
                enableLiveDeploymentControllers: true,
                diagnostics: "requested-native-with-deployment-shell-live-shell-rerouted-to-siegemissionwithdeployment-via-isolated-coop-siege-behavior-factory-live-deployment-controllers-enabled");
        }

        public static bool TryResolveOfficialBattleMissionOpenNewReroute(
            string missionName,
            string runtimeScene,
            bool isCoopBattleFactory,
            out PreOpenContract contract,
            out string diagnostics)
        {
            contract = default;

            if (!string.Equals(Normalize(missionName), MultiplayerBattleShell, StringComparison.Ordinal))
            {
                diagnostics = "mission-shell-not-official-battle";
                return false;
            }

            if (isCoopBattleFactory)
            {
                diagnostics = "already-coop-battle-factory";
                return false;
            }

            PreOpenContract resolvedContract = Resolve(runtimeScene, MultiplayerBattleShell);
            if (!resolvedContract.IsSiegeAssaultWithDeployment)
            {
                diagnostics = "not-siege-assault-with-deployment " + resolvedContract.Describe();
                return false;
            }

            if (string.Equals(resolvedContract.LiveMissionShell, MultiplayerBattleShell, StringComparison.Ordinal))
            {
                diagnostics = "live-shell-remains-official-battle " + resolvedContract.Describe();
                return false;
            }

            contract = resolvedContract;
            diagnostics = resolvedContract.Describe();
            return true;
        }

        public static void Capture(PreOpenContract contract, string source)
        {
            lock (Sync)
            {
                _activeContract = contract;
                _activeContractUtc = DateTime.UtcNow;
            }

            ModLogger.Info(
                "SiegeAssaultMissionOpenBridge: captured pre-open mission contract. " +
                contract.Describe() +
                " Source=" + Normalize(source) + ".");
        }

        public static bool TryGetActiveContract(
            string runtimeScene,
            out PreOpenContract contract,
            out string diagnostics)
        {
            string normalizedScene = Normalize(runtimeScene);
            lock (Sync)
            {
                contract = _activeContract;
                if (!contract.IsValid)
                {
                    diagnostics = "contract-empty";
                    return false;
                }

                if (_activeContractUtc == DateTime.MinValue ||
                    DateTime.UtcNow - _activeContractUtc > ContractTtl)
                {
                    contract = default;
                    diagnostics = "contract-expired";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(normalizedScene) &&
                    !string.IsNullOrWhiteSpace(contract.RuntimeScene) &&
                    !string.Equals(normalizedScene, contract.RuntimeScene, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics =
                        "scene-mismatch RequestedScene=" + normalizedScene +
                        " ActiveScene=" + contract.RuntimeScene;
                    contract = default;
                    return false;
                }

                diagnostics = contract.Describe();
                return true;
            }
        }

        public static bool ShouldAllowWrappedBattleDeploymentBridge(
            Mission mission,
            BattleScenarioContextMessage scenarioContext,
            out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            if (scenarioContext?.IsSiegeBattle != true)
            {
                diagnostics = "not-siege-battle";
                return false;
            }

            string siegeSubtype = scenarioContext.SiegeContext?.SiegeSubtype ?? string.Empty;
            string requestedMissionShell = scenarioContext.SiegeContext?.MissionShell ?? string.Empty;
            if (!string.Equals(siegeSubtype, "SiegeAssault", StringComparison.OrdinalIgnoreCase) ||
                !CampaignMissionShellRuntimeState.IsWithDeploymentMissionShell(requestedMissionShell))
            {
                diagnostics = "not-siege-assault-with-deployment";
                return false;
            }

            if (!TryGetActiveContract(mission.SceneName ?? string.Empty, out PreOpenContract contract, out string activeContractDiagnostics))
            {
                diagnostics = "suppressed-without-active-pre-open-contract " + activeContractDiagnostics;
                return false;
            }

            if (!contract.IsSiegeAssaultWithDeployment)
            {
                diagnostics = "suppressed-active-contract-not-siege-assault-with-deployment " + activeContractDiagnostics;
                return false;
            }

            if (contract.DisableHybridBattleShellDeploymentBridge)
            {
                diagnostics = "suppressed-by-pre-open-contract " + activeContractDiagnostics;
                return false;
            }

            if (!string.Equals(contract.LiveMissionShell, SiegeMissionWithDeploymentShell, StringComparison.Ordinal))
            {
                diagnostics = "suppressed-live-shell-not-native-with-deployment " + activeContractDiagnostics;
                return false;
            }

            diagnostics = "enabled " + activeContractDiagnostics;
            return true;
        }

        public static bool ShouldAllowLiveDeploymentControllers(Mission mission, out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            if (!TryGetActiveContract(mission.SceneName ?? string.Empty, out PreOpenContract contract, out string activeContractDiagnostics))
            {
                diagnostics = "suppressed-without-active-pre-open-contract " + activeContractDiagnostics;
                return false;
            }

            if (!contract.IsSiegeAssaultWithDeployment)
            {
                diagnostics = "suppressed-active-contract-not-siege-assault-with-deployment " + activeContractDiagnostics;
                return false;
            }

            if (!contract.EnableLiveDeploymentControllers)
            {
                diagnostics = "suppressed-pre-open-contract-disables-live-deployment-controllers " + activeContractDiagnostics;
                return false;
            }

            if (!string.Equals(contract.LiveMissionShell, SiegeMissionWithDeploymentShell, StringComparison.Ordinal))
            {
                diagnostics = "suppressed-live-shell-not-native-with-deployment " + activeContractDiagnostics;
                return false;
            }

            diagnostics = "enabled " + activeContractDiagnostics;
            return true;
        }

        private static BattleScenarioContextMessage TryResolveScenarioContext()
        {
            try
            {
                BattleScenarioContextMessage scenarioContext = BattleSnapshotRuntimeState.GetScenarioContext();
                if (scenarioContext != null)
                    return scenarioContext;
            }
            catch
            {
            }

            try
            {
                BattleSnapshotMessage runtimeSnapshot = BattleSnapshotRuntimeState.GetCurrent();
                if (runtimeSnapshot?.ScenarioContext != null)
                    return runtimeSnapshot.ScenarioContext;
            }
            catch
            {
            }

            try
            {
                if (!GameNetwork.IsClient || CustomGameJoinContextState.ShouldAllowLocalBattleRosterFileFallback())
                {
                    BattleSnapshotMessage rosterSnapshot = BattleRosterFileHelper.ReadSnapshot();
                    if (rosterSnapshot?.ScenarioContext != null)
                        return rosterSnapshot.ScenarioContext;
                }
            }
            catch
            {
            }

            return null;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
