using System;
using CoopSpectator.Campaign;
using System.Collections.Generic;
using CoopSpectator.Network.Messages;
using CoopSpectator.Infrastructure; // Підключаємо ModLogger для діагностики
using CoopSpectator.MissionBehaviors; // Етап 3.3: логування spectator/agent/spawn
using TaleWorlds.Core; // Підключаємо MissionInitializerRecord
using TaleWorlds.MountAndBlade; // Підключаємо Mission, MissionState
using TaleWorlds.MountAndBlade.Multiplayer; // Підключаємо MissionBasedMultiplayerGameMode та компоненти (MissionLobbyComponent, MultiplayerTimerComponent, тощо)

namespace CoopSpectator.GameMode // Простір імен для кастомного MP game mode CoopBattle
{
    /// <summary>
    /// Реєстрований у грі game mode "CoopBattle". Наслідує офіційний MissionBasedMultiplayerGameMode,
    /// перевизначає StartMultiplayerGame(scene) — відкриває місію з нашим набором MissionBehavior (лобі, таймер, наші server/client behaviors).
    /// </summary>
    public sealed class MissionMultiplayerCoopBattleMode : MissionBasedMultiplayerGameMode
    {
        private static string _lastRequestedRuntimeSceneName;
        private const string TeamDeathmatchMissionShell = "MultiplayerTeamDeathmatch";
        private const string BattleMissionShell = "MultiplayerBattle";
        /// <summary>Ідентифікатор режиму (збігається з ім'ям у AddMultiplayerGameMode).</summary>
        public const string GameModeId = "CoopBattle";

        /// <summary>Конструктор; name передається в базу для реєстрації в лобі/лаунчері.</summary>
        public MissionMultiplayerCoopBattleMode(string name) : base(name)
        {
        }

        /// <summary>Викликається грою при старті MP-місії (і на сервері, і на клієнті). Відкриває місію з ванільною назвою TDM, щоб рушій не падав при create_mission.</summary>
        public override void StartMultiplayerGame(string scene)
        {
            _lastRequestedRuntimeSceneName = scene ?? string.Empty;
            bool battleMapRuntime = IsBattleMapSceneName(scene);
            string missionShell = battleMapRuntime ? BattleMissionShell : TeamDeathmatchMissionShell;
            if (battleMapRuntime && GameNetwork.IsServer)
                TryApplyBattleMapTimerOptionOverrides();
            ModLogger.Info("StartMultiplayerGame CoopBattle called, scene=" + (scene ?? ""));
            if (GameNetwork.IsServer)
                ModLogger.Info("[CoopSpectator] Server starting mission, GameType=" + GameModeId + " (must match client GetMultiplayerGameMode and multiplayer_strings).");
            ModLogger.Info(
                "Opening mission CoopBattle, scene=" + (scene ?? "") +
                ", shell=" + missionShell +
                ", battleMapRuntime=" + battleMapRuntime +
                ", timestamp=" + DateTime.UtcNow.ToString("o"));
            MissionInitializerRecord record = new MissionInitializerRecord(scene);
            BattleMapContractDiagnostics.LogMissionInitializerRecordState(record, "CoopBattle mission init pre-apply");
            TryApplyCampaignMapPatchContext(ref record, scene);
            BattleMapContractDiagnostics.LogMissionInitializerRecordState(record, "CoopBattle mission init pre-open");
            MissionState.OpenNew(missionShell, record, CreateBehaviorsForMission);
        }

        private static void TryApplyBattleMapTimerOptionOverrides()
        {
            try
            {
                ApplyBattleMapTimerOptionOverrides(MultiplayerOptions.MultiplayerOptionsAccessMode.CurrentMapOptions);
                ApplyBattleMapTimerOptionOverrides(MultiplayerOptions.MultiplayerOptionsAccessMode.NextMapOptions);
                ModLogger.Info(
                    "MissionMultiplayerCoopBattleMode: applied battle-map timer option overrides. " +
                    "RoundTimeLimit=" + MultiplayerOptions.OptionType.RoundTimeLimit.GetIntValue() +
                    " MapTimeLimit=" + MultiplayerOptions.OptionType.MapTimeLimit.GetIntValue() +
                    " WarmupTimeLimitInSeconds=" + MultiplayerOptions.OptionType.WarmupTimeLimitInSeconds.GetIntValue() +
                    " RoundPreparationTimeLimit=" + MultiplayerOptions.OptionType.RoundPreparationTimeLimit.GetIntValue() + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionMultiplayerCoopBattleMode: failed to apply battle-map timer option overrides: " + ex.Message);
            }
        }

        private static void ApplyBattleMapTimerOptionOverrides(MultiplayerOptions.MultiplayerOptionsAccessMode mode)
        {
            // Keep battle-shell timers effectively neutralized, but stay inside native
            // network compression bounds so peers can join an already active mission.
            MultiplayerOptions.OptionType.WarmupTimeLimitInSeconds.SetValue(
                MultiplayerOptions.OptionType.WarmupTimeLimitInSeconds.GetMinimumValue(),
                mode);
            MultiplayerOptions.OptionType.RoundPreparationTimeLimit.SetValue(
                MultiplayerOptions.OptionType.RoundPreparationTimeLimit.GetMinimumValue(),
                mode);
            MultiplayerOptions.OptionType.MapTimeLimit.SetValue(
                MultiplayerOptions.OptionType.MapTimeLimit.GetMaximumValue(),
                mode);
            MultiplayerOptions.OptionType.RoundTimeLimit.SetValue(
                MultiplayerOptions.OptionType.RoundTimeLimit.GetMaximumValue(),
                mode);
        }

        private static IEnumerable<MissionBehavior> CreateBehaviorsForMission(Mission mission)
        {
            bool isServer = GameNetwork.IsServer;
            bool isDedicated = IsDedicatedServerProcess();
            string resolvedRuntimeScene = ResolveRuntimeSceneName(mission);
            List<MissionBehavior> list = isServer
                ? BuildServerMissionBehaviorsForCoopBattle(mission, isDedicated)
                : BuildClientMissionBehaviorsForCoopBattle(mission, isDedicated);

            if (isServer)
                ValidateServerStackSanity(list);
            else
                ValidateClientStackSanity(list);

            try
            {
                ModLogger.Info(
                    "CoopBattle CreateBehaviorsForMission count=" + list.Count +
                    ", IsServer=" + isServer +
                    ", isDedicated=" + isDedicated +
                    ", ResolvedRuntimeScene=" + (resolvedRuntimeScene ?? "null"));
                for (int i = 0; i < list.Count; i++)
                    ModLogger.Info("  [" + i + "] " + list[i].GetType().FullName);
            }
            catch (Exception ex) { ModLogger.Info("CoopBattle behavior list log failed: " + ex.Message); }

            return list;
        }

        private static List<MissionBehavior> BuildServerMissionBehaviorsForCoopBattle(Mission mission, bool isDedicated)
        {
            bool minimalBattleMapRuntime = IsSceneAwareBattleMap(mission);
            var list = new List<MissionBehavior>();
            if (!minimalBattleMapRuntime)
                list.Add(MissionLobbyComponent.CreateBehavior());
            else
            {
                list.Add(MissionLobbyComponent.CreateBehavior());
                ModLogger.Info("CoopBattle server: retained MissionLobbyComponent for battle-map runtime while isolating post-AfterStart native crash without MissionScoreboardComponent.");
            }

            list.Add(new MissionMultiplayerCoopBattle());
            if (!minimalBattleMapRuntime)
                AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MultiplayerAchievementComponent"));
            else
                ModLogger.Info("CoopBattle server: skipped MultiplayerAchievementComponent for minimal battle-map runtime.");

            list.Add(new MultiplayerTimerComponent());
            ModLogger.Info("CoopBattle server: MultiplayerMissionAgentVisualSpawnComponent and MissionLobbyEquipmentNetworkComponent skipped (client-only).");
            if (!minimalBattleMapRuntime)
            {
                list.Add(new MultiplayerTeamSelectComponent());
            }
            else
            {
                list.Add(new MultiplayerTeamSelectComponent());
                ModLogger.Info("CoopBattle server: retained MultiplayerTeamSelectComponent for battle-map native peer-sync compatibility.");
            }

            if (!minimalBattleMapRuntime)
            {
                AddBoundaryBehaviorsForRuntime(list, mission, "server");
            }
            else
            {
                AddRequired(list, MissionBehaviorHelpers.TryCreateHardBorderPlacer(), "MissionHardBorderPlacer");
                AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryPlacer(), "MissionBoundaryPlacer");
                AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryCrossingHandler(mission), "MissionBoundaryCrossingHandler");
                ModLogger.Info("CoopBattle server: retained boundary behaviors for battle-map native peer-sync compatibility.");
            }

            if (!minimalBattleMapRuntime)
            {
                list.Add(new MultiplayerPollComponent());
                list.Add(new MultiplayerAdminComponent());
            }
            else
            {
                list.Add(new MultiplayerPollComponent());
                list.Add(new MultiplayerAdminComponent());
                ModLogger.Info("CoopBattle server: retained MultiplayerPollComponent and MultiplayerAdminComponent for battle-map native peer-sync compatibility.");
            }

            if (!isDedicated && !minimalBattleMapRuntime)
                list.Add(new MultiplayerGameNotificationsComponent());
            else if (minimalBattleMapRuntime)
                ModLogger.Info("CoopBattle server: skipped MultiplayerGameNotificationsComponent for minimal battle-map runtime.");

            AddOptional(list, MissionBehaviorHelpers.TryCreateMissionOptionsComponent(mission), "MissionOptionsComponent");
            if (minimalBattleMapRuntime)
                ModLogger.Info("CoopBattle server: retained MissionOptionsComponent for battle-map mission-entry bootstrap.");

            if (!isDedicated && !minimalBattleMapRuntime)
            {
                list.Add(new MissionScoreboardComponent(new TDMScoreboardData()));
            }
            else if (minimalBattleMapRuntime)
            {
                MissionBehavior serverScoreboard = MissionBehaviorHelpers.TryCreateMissionScoreboardComponent();
                AddOptional(list, serverScoreboard, "MissionScoreboardComponent");
                if (serverScoreboard != null)
                    ModLogger.Info("CoopBattle server: retained MissionScoreboardComponent for battle-map MissionCustomGameServerComponent.AfterStart compatibility.");
                else
                    ModLogger.Info("CoopBattle server: MissionScoreboardComponent unavailable for battle-map runtime; continuing with known crash risk.");
            }

            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MissionRecentPlayersComponent"));
            if (!minimalBattleMapRuntime)
            {
                AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MissionMatchHistoryComponent"));
                AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.EquipmentControllerLeaveLogic"));
                AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MultiplayerPreloadHelper"));
                AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MissionAgentPanicHandler"));
                AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.AgentHumanAILogic"));
            }
            else
            {
                AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MissionMatchHistoryComponent"));
                AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.EquipmentControllerLeaveLogic"));
                AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MultiplayerPreloadHelper"));
                AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MissionAgentPanicHandler"));
                AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.AgentHumanAILogic"));
                ModLogger.Info("CoopBattle server: retained recent players, match history, equipment leave logic, preload helper, panic handler, and human AI for battle-map native peer-sync compatibility.");
            }

            list.Add(new MissionBehaviorDiagnostic());
            list.Add(new CoopMissionNetworkBridge());
            list.Add(new CoopMissionSpawnLogic());
            return list;
        }

        private static List<MissionBehavior> BuildClientMissionBehaviorsForCoopBattle(Mission mission, bool isDedicated)
        {
            bool minimalBattleMapRuntime = IsSceneAwareBattleMap(mission);
            var list = new List<MissionBehavior>();
            list.Add(MissionLobbyComponent.CreateBehavior());
            if (minimalBattleMapRuntime)
                ModLogger.Info("CoopBattle client: retained MissionLobbyComponent for battle-map mission-entry bootstrap.");

            list.Add(new MissionMultiplayerCoopBattleClient());
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MultiplayerAchievementComponent"));
            if (minimalBattleMapRuntime)
                ModLogger.Info("CoopBattle client: retained MultiplayerAchievementComponent for battle-map native bootstrap compatibility.");

            list.Add(new MultiplayerTimerComponent());

            MissionBehavior visualSpawn = MissionBehaviorHelpers.TryCreateMissionAgentVisualSpawnComponent();
            if (visualSpawn != null)
            {
                list.Add(visualSpawn);

                bool includeEquipmentNetworkComponent =
                    !minimalBattleMapRuntime ||
                    ExperimentalFeatures.EnableBattleMapClientEquipmentNetworkComponent;
                if (includeEquipmentNetworkComponent)
                {
                    list.Add(new MissionLobbyEquipmentNetworkComponent());
                    if (minimalBattleMapRuntime)
                        ModLogger.Info("CoopBattle client: retained MissionLobbyEquipmentNetworkComponent for battle-map native bootstrap compatibility.");
                }
                else
                {
                    ModLogger.Info("CoopBattle client: skipped MissionLobbyEquipmentNetworkComponent for battle-map spawn crash isolation while retaining MultiplayerMissionAgentVisualSpawnComponent.");
                }

                if (minimalBattleMapRuntime)
                    ModLogger.Info("CoopBattle client: retained MultiplayerMissionAgentVisualSpawnComponent for battle-map native bootstrap compatibility.");
            }
            else
            {
                ModLogger.Info("CoopBattle client: skip MissionLobbyEquipmentNetworkComponent (MultiplayerMissionAgentVisualSpawnComponent unavailable).");
            }

            if (!minimalBattleMapRuntime)
            {
                ModLogger.Info("CoopBattle client: skipped MultiplayerTeamSelectComponent; custom coop selection overlay will be used instead.");
                AddBoundaryBehaviorsForRuntime(list, mission, "client");
            }
            else
            {
                list.Add(new MultiplayerTeamSelectComponent());
                ModLogger.Info("CoopBattle client: retained MultiplayerTeamSelectComponent for battle-map native bootstrap compatibility.");
                AddRequired(list, MissionBehaviorHelpers.TryCreateHardBorderPlacer(), "MissionHardBorderPlacer");
                AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryPlacer(), "MissionBoundaryPlacer");
                AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryCrossingHandler(mission), "MissionBoundaryCrossingHandler");
                ModLogger.Info("CoopBattle client: retained boundary behaviors for battle-map native bootstrap compatibility.");
            }

            if (!minimalBattleMapRuntime)
            {
                list.Add(new MultiplayerPollComponent());
                list.Add(new MultiplayerAdminComponent());
            }
            else
            {
                list.Add(new MultiplayerPollComponent());
                list.Add(new MultiplayerAdminComponent());
                ModLogger.Info("CoopBattle client: retained MultiplayerPollComponent and MultiplayerAdminComponent for battle-map native bootstrap compatibility.");
            }

            if (!isDedicated)
                list.Add(new MultiplayerGameNotificationsComponent());
            else if (minimalBattleMapRuntime)
                ModLogger.Info("CoopBattle client: skipped MultiplayerGameNotificationsComponent for minimal battle-map runtime.");
            if (minimalBattleMapRuntime && !isDedicated)
                ModLogger.Info("CoopBattle client: retained MultiplayerGameNotificationsComponent for battle-map native bootstrap compatibility.");

            AddOptional(list, MissionBehaviorHelpers.TryCreateMissionOptionsComponent(mission), "MissionOptionsComponent");
            if (minimalBattleMapRuntime)
                ModLogger.Info("CoopBattle client: retained MissionOptionsComponent for battle-map mission-entry bootstrap.");

            if (!isDedicated)
            {
                MissionBehavior clientScoreboard = MissionBehaviorHelpers.TryCreateMissionScoreboardComponent();
                if (clientScoreboard != null)
                {
                    list.Add(clientScoreboard);
                    if (minimalBattleMapRuntime)
                        ModLogger.Info("CoopBattle client: retained MissionScoreboardComponent for battle-map native bootstrap compatibility.");
                }
                else if (minimalBattleMapRuntime)
                {
                    ModLogger.Info("CoopBattle client: MissionScoreboardComponent unavailable for battle-map bootstrap; continuing without it.");
                }
            }

            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MissionRecentPlayersComponent"));
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MultiplayerPreloadHelper"));
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MissionMatchHistoryComponent"));
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.EquipmentControllerLeaveLogic"));
            if (minimalBattleMapRuntime)
                ModLogger.Info("CoopBattle client: retained recent players, preload, match history, and equipment leave logic for battle-map native bootstrap compatibility.");

            list.Add(new MissionBehaviorDiagnostic());
            list.Add(new CoopMissionNetworkBridge());
            if (minimalBattleMapRuntime && !isDedicated)
            {
                AddOptional(list, TryCreateMissionAgentLabelUiHandler(mission), "MissionAgentLabelUIHandler");
                AddOptional(
                    list,
                    MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.View.MissionViews.MissionFormationTargetSelectionHandler"),
                    "MissionFormationTargetSelectionHandler");
                AddOptional(list, TryCreateMissionFormationMarkerUiHandler(mission), "MissionFormationMarkerUIHandler");
                ModLogger.Info("CoopBattle client: injected agent label and formation marker mission views for battle-map runtime parity with native multiplayer battle/practice stacks.");
            }
            if (!minimalBattleMapRuntime)
            {
                list.Add(new CoopMissionClientLogic());
            }
            else
            {
                ModLogger.Info("CoopBattle client: skipped CoopMissionClientLogic for battle-map crash isolation.");
            }
#if !COOPSPECTATOR_DEDICATED
            if (ExperimentalFeatures.EnableCustomCoopSelectionOverlay)
            {
                list.Add(new CoopSpectator.UI.CoopMissionSelectionView());
                if (minimalBattleMapRuntime)
                    ModLogger.Info("CoopBattle client: re-enabled custom coop selection overlay for battle-map runtime while retaining native bootstrap behaviors.");
            }
#endif
            return list;
        }

        private static void ValidateServerStackSanity(List<MissionBehavior> list)
        {
            if (list == null)
                return;

            string[] clientOnlyNames =
            {
                nameof(MissionMultiplayerCoopBattleClient),
                nameof(CoopMissionClientLogic),
                nameof(MissionLobbyEquipmentNetworkComponent),
                "MultiplayerMissionAgentVisualSpawnComponent"
            };

            int removed = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                MissionBehavior behavior = list[i];
                if (behavior == null)
                    continue;

                string typeName = behavior.GetType().Name;
                for (int j = 0; j < clientOnlyNames.Length; j++)
                {
                    if (!string.Equals(typeName, clientOnlyNames[j], StringComparison.Ordinal))
                        continue;

                    list.RemoveAt(i);
                    removed++;
                    ModLogger.Info("CoopBattle server validation: removed client-only behavior " + typeName + ".");
                    break;
                }
            }

            if (removed == 0)
                ModLogger.Info("CoopBattle server validation passed.");

            bool hasCustomServer = MissionBehaviorHelpers.ListContainsBehaviorType(list, "MissionCustomGameServerComponent");
            bool hasScoreboard = MissionBehaviorHelpers.ListContainsBehaviorType(list, "MissionScoreboardComponent");
            if (hasCustomServer && !hasScoreboard)
            {
                MissionBehavior scoreboard = MissionBehaviorHelpers.TryCreateMissionScoreboardComponent();
                if (scoreboard != null)
                {
                    list.Add(scoreboard);
                    ModLogger.Error("CoopBattle server validation: MissionScoreboardComponent was missing; added it because MissionCustomGameServerComponent.AfterStart may crash without it.", null);
                }
                else
                {
                    ModLogger.Error("CoopBattle server validation: MissionScoreboardComponent missing and could not be created. MissionCustomGameServerComponent.AfterStart may crash.", null);
                }
            }
        }

        private static void ValidateClientStackSanity(List<MissionBehavior> list)
        {
            if (list == null)
                return;

            bool hasVisualSpawn = MissionBehaviorHelpers.ListContainsBehaviorType(list, "MultiplayerMissionAgentVisualSpawnComponent");
            int removed = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                MissionBehavior behavior = list[i];
                if (behavior == null)
                    continue;

                string typeName = behavior.GetType().Name;
                if (string.Equals(typeName, nameof(MissionLobbyEquipmentNetworkComponent), StringComparison.Ordinal) && !hasVisualSpawn)
                {
                    list.RemoveAt(i);
                    removed++;
                    ModLogger.Info("CoopBattle client validation: removed MissionLobbyEquipmentNetworkComponent because MultiplayerMissionAgentVisualSpawnComponent is missing.");
                }
            }

            if (removed == 0)
                ModLogger.Info("CoopBattle client validation passed.");
        }

        private static void AddIfNotNull(List<MissionBehavior> list, MissionBehavior behavior)
        {
            if (behavior != null) list.Add(behavior);
        }

        private static void AddBoundaryBehaviorsForRuntime(List<MissionBehavior> list, Mission mission, string runtimeSide)
        {
            if (IsSceneAwareBattleMap(mission))
            {
                ModLogger.Info(
                    "CoopBattle " + (runtimeSide ?? "runtime") +
                    ": skipped boundary placers for scene-aware battle map runtime. ResolvedScene=" +
                    (ResolveRuntimeSceneName(mission) ?? "null"));
                return;
            }

            AddRequired(list, MissionBehaviorHelpers.TryCreateHardBorderPlacer(), "MissionHardBorderPlacer");
            AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryPlacer(), "MissionBoundaryPlacer");
            AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryCrossingHandler(mission), "MissionBoundaryCrossingHandler");
        }

        private static MissionBehavior TryCreateMissionFormationMarkerUiHandler(Mission mission)
        {
            try
            {
                Type viewCreatorType = Type.GetType("TaleWorlds.MountAndBlade.View.ViewCreator, TaleWorlds.MountAndBlade.View", throwOnError: false);
                if (viewCreatorType == null)
                {
                    ModLogger.Info("CoopBattle: MissionFormationMarkerUIHandler creation skipped because TaleWorlds.MountAndBlade.View is unavailable in current runtime.");
                    return null;
                }

                var createMethod = viewCreatorType.GetMethod(
                    "CreateMissionFormationMarkerUIHandler",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    binder: null,
                    types: new[] { typeof(Mission) },
                    modifiers: null);
                if (createMethod == null)
                {
                    ModLogger.Info("CoopBattle: MissionFormationMarkerUIHandler creation skipped because ViewCreator.CreateMissionFormationMarkerUIHandler was not found.");
                    return null;
                }

                return createMethod.Invoke(null, new object[] { mission }) as MissionBehavior;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattle: MissionFormationMarkerUIHandler creation failed: " + ex.Message);
                return null;
            }
        }

        private static MissionBehavior TryCreateMissionAgentLabelUiHandler(Mission mission)
        {
            try
            {
                Type viewCreatorType = Type.GetType("TaleWorlds.MountAndBlade.View.ViewCreator, TaleWorlds.MountAndBlade.View", throwOnError: false);
                if (viewCreatorType == null)
                {
                    ModLogger.Info("CoopBattle: MissionAgentLabelUIHandler creation skipped because TaleWorlds.MountAndBlade.View is unavailable in current runtime.");
                    return null;
                }

                var createMethod = viewCreatorType.GetMethod(
                    "CreateMissionAgentLabelUIHandler",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    binder: null,
                    types: new[] { typeof(Mission) },
                    modifiers: null);
                if (createMethod == null)
                {
                    ModLogger.Info("CoopBattle: MissionAgentLabelUIHandler creation skipped because ViewCreator.CreateMissionAgentLabelUIHandler was not found.");
                    return null;
                }

                return createMethod.Invoke(null, new object[] { mission }) as MissionBehavior;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattle: MissionAgentLabelUIHandler creation failed: " + ex.Message);
                return null;
            }
        }

        internal static bool IsBattleMapSceneName(string sceneName)
        {
            return SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(sceneName);
        }

        private static bool IsSceneAwareBattleMap(Mission mission)
        {
            string sceneName = ResolveRuntimeSceneName(mission);
            return SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(sceneName);
        }

        private static string ResolveRuntimeSceneName(Mission mission)
        {
            string missionSceneName = mission?.SceneName ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(missionSceneName))
                return missionSceneName;

            if (!string.IsNullOrWhiteSpace(_lastRequestedRuntimeSceneName))
                return _lastRequestedRuntimeSceneName;

            try
            {
                string snapshotSceneName = BattleSnapshotRuntimeState.GetCurrent()?.MultiplayerScene ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(snapshotSceneName))
                    return snapshotSceneName;
            }
            catch
            {
            }

            try
            {
                string rosterSceneName = BattleRosterFileHelper.ReadSnapshot()?.MultiplayerScene ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(rosterSceneName))
                    return rosterSceneName;
            }
            catch
            {
            }

            return string.Empty;
        }

        private static void TryApplyCampaignMapPatchContext(ref MissionInitializerRecord record, string runtimeScene)
        {
            CampaignMapPatchMissionInit.TryApply(ref record, runtimeScene, "CoopBattle mission init");
        }

        /// <summary>Додає опційний behavior; якщо null — лише лог warning, місія продовжує без нього (без винятку).</summary>
        private static void AddOptional(List<MissionBehavior> list, MissionBehavior behavior, string name)
        {
            if (behavior == null)
            {
                ModLogger.Info("CoopBattle: " + name + " skipped with warning (optional).");
                return;
            }
            list.Add(behavior);
        }

        /// <summary>Додає обов'язковий behavior; якщо null — лог і виняток (fail-fast).</summary>
        private static void AddRequired(List<MissionBehavior> list, MissionBehavior behavior, string name)
        {
            if (behavior == null)
            {
                ModLogger.Info("CoopBattle: Required mission behavior '" + name + "' could not be created. Aborting mission open.");
                throw new InvalidOperationException("Required mission behavior '" + name + "' could not be created. Check logs for assembly/type resolution.");
            }
            list.Add(behavior);
        }

        /// <summary>Чи це dedicated server. Див. коментар у MissionMultiplayerCoopTdmMode.IsDedicatedServerProcess.</summary>
        private static bool IsDedicatedServerProcess()
        {
            try
            {
                string name = System.Diagnostics.Process.GetCurrentProcess().ProcessName ?? "";
                return name.IndexOf("Dedicated", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
