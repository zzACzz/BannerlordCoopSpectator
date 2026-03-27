using System;
using System.Collections.Generic;
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
        /// <summary>Ідентифікатор режиму (збігається з ім'ям у AddMultiplayerGameMode).</summary>
        public const string GameModeId = "CoopBattle";

        /// <summary>Конструктор; name передається в базу для реєстрації в лобі/лаунчері.</summary>
        public MissionMultiplayerCoopBattleMode(string name) : base(name)
        {
        }

        /// <summary>Викликається грою при старті MP-місії (і на сервері, і на клієнті). Відкриває місію з ванільною назвою TDM, щоб рушій не падав при create_mission.</summary>
        public override void StartMultiplayerGame(string scene)
        {
            ModLogger.Info("StartMultiplayerGame CoopBattle called, scene=" + (scene ?? ""));
            if (GameNetwork.IsServer)
                ModLogger.Info("[CoopSpectator] Server starting mission, GameType=" + GameModeId + " (must match client GetMultiplayerGameMode and multiplayer_strings).");
            ModLogger.Info("Opening mission CoopBattle, scene=" + (scene ?? "") + ", timestamp=" + DateTime.UtcNow.ToString("o"));
            MissionInitializerRecord record = new MissionInitializerRecord(scene);
            MissionState.OpenNew("MultiplayerTeamDeathmatch", record, CreateBehaviorsForMission);
        }

        private static IEnumerable<MissionBehavior> CreateBehaviorsForMission(Mission mission)
        {
            bool isServer = GameNetwork.IsServer;
            bool isDedicated = IsDedicatedServerProcess();
            List<MissionBehavior> list = isServer
                ? BuildServerMissionBehaviorsForCoopBattle(mission, isDedicated)
                : BuildClientMissionBehaviorsForCoopBattle(mission, isDedicated);

            if (isServer)
                ValidateServerStackSanity(list);
            else
                ValidateClientStackSanity(list);

            try
            {
                ModLogger.Info("CoopBattle CreateBehaviorsForMission count=" + list.Count + ", IsServer=" + isServer + ", isDedicated=" + isDedicated);
                for (int i = 0; i < list.Count; i++)
                    ModLogger.Info("  [" + i + "] " + list[i].GetType().FullName);
            }
            catch (Exception ex) { ModLogger.Info("CoopBattle behavior list log failed: " + ex.Message); }

            return list;
        }

        private static List<MissionBehavior> BuildServerMissionBehaviorsForCoopBattle(Mission mission, bool isDedicated)
        {
            var list = new List<MissionBehavior>();
            list.Add(MissionLobbyComponent.CreateBehavior());
            list.Add(new MissionMultiplayerCoopBattle());
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MultiplayerAchievementComponent"));
            list.Add(new MultiplayerTimerComponent());
            ModLogger.Info("CoopBattle server: MultiplayerMissionAgentVisualSpawnComponent and MissionLobbyEquipmentNetworkComponent skipped (client-only).");
            ModLogger.Info("CoopBattle server: skipped MultiplayerTeamSelectComponent; custom coop selection overlay owns side/unit selection.");
            AddRequired(list, MissionBehaviorHelpers.TryCreateHardBorderPlacer(), "MissionHardBorderPlacer");
            AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryPlacer(), "MissionBoundaryPlacer");
            AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryCrossingHandler(mission), "MissionBoundaryCrossingHandler");
            list.Add(new MultiplayerPollComponent());
            list.Add(new MultiplayerAdminComponent());
            if (!isDedicated)
                list.Add(new MultiplayerGameNotificationsComponent());
            AddOptional(list, MissionBehaviorHelpers.TryCreateMissionOptionsComponent(mission), "MissionOptionsComponent");
            if (!isDedicated)
                list.Add(new MissionScoreboardComponent(new TDMScoreboardData()));
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MissionMatchHistoryComponent"));
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.EquipmentControllerLeaveLogic"));
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MissionRecentPlayersComponent"));
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MultiplayerPreloadHelper"));
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MissionAgentPanicHandler"));
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.AgentHumanAILogic"));
            list.Add(new MissionBehaviorDiagnostic());
            list.Add(new CoopMissionSpawnLogic());
            return list;
        }

        private static List<MissionBehavior> BuildClientMissionBehaviorsForCoopBattle(Mission mission, bool isDedicated)
        {
            var list = new List<MissionBehavior>();
            list.Add(MissionLobbyComponent.CreateBehavior());
            list.Add(new MissionMultiplayerCoopBattleClient());
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MultiplayerAchievementComponent"));
            list.Add(new MultiplayerTimerComponent());

            MissionBehavior visualSpawn = MissionBehaviorHelpers.TryCreateMissionAgentVisualSpawnComponent();
            if (visualSpawn != null)
            {
                list.Add(visualSpawn);
                list.Add(new MissionLobbyEquipmentNetworkComponent());
            }
            else
            {
                ModLogger.Info("CoopBattle client: skip MissionLobbyEquipmentNetworkComponent (MultiplayerMissionAgentVisualSpawnComponent unavailable).");
            }

            ModLogger.Info("CoopBattle client: skipped MultiplayerTeamSelectComponent; custom coop selection overlay will be used instead.");
            AddRequired(list, MissionBehaviorHelpers.TryCreateHardBorderPlacer(), "MissionHardBorderPlacer");
            AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryPlacer(), "MissionBoundaryPlacer");
            AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryCrossingHandler(mission), "MissionBoundaryCrossingHandler");
            list.Add(new MultiplayerPollComponent());
            list.Add(new MultiplayerAdminComponent());
            if (!isDedicated)
                list.Add(new MultiplayerGameNotificationsComponent());
            AddOptional(list, MissionBehaviorHelpers.TryCreateMissionOptionsComponent(mission), "MissionOptionsComponent");
            if (!isDedicated)
                list.Add(new MissionScoreboardComponent(new TDMScoreboardData()));
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MissionMatchHistoryComponent"));
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.EquipmentControllerLeaveLogic"));
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MissionRecentPlayersComponent"));
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MultiplayerPreloadHelper"));
            list.Add(new MissionBehaviorDiagnostic());
            list.Add(new CoopMissionClientLogic());
#if !COOPSPECTATOR_DEDICATED
            if (ExperimentalFeatures.EnableCustomCoopSelectionOverlay)
                list.Add(new CoopSpectator.UI.CoopMissionSelectionView());
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
