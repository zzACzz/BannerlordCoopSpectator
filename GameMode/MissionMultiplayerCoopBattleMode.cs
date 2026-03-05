using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure; // Підключаємо ModLogger для діагностики
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
            var list = new List<MissionBehavior>();
            bool isServer = GameNetwork.IsServer;
            bool isDedicated = IsDedicatedServerProcess();

            list.Add(MissionLobbyComponent.CreateBehavior());
            if (isServer)
                list.Add(new MissionMultiplayerCoopBattle());
            list.Add(new MissionMultiplayerCoopBattleClient());

            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MultiplayerAchievementComponent"));
            list.Add(new MultiplayerTimerComponent());
            list.Add(new MultiplayerMissionAgentVisualSpawnComponent());
            list.Add(new MissionLobbyEquipmentNetworkComponent());
            list.Add(new MultiplayerTeamSelectComponent());
            AddRequired(list, MissionBehaviorHelpers.TryCreateHardBorderPlacer(), "MissionHardBorderPlacer");
            AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryPlacer(), "MissionBoundaryPlacer");
            AddOptional(list, MissionBehaviorHelpers.TryCreateBoundaryCrossingHandler(mission), "MissionBoundaryCrossingHandler");
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
            if (isServer)
            {
                AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MissionAgentPanicHandler"));
                AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.AgentHumanAILogic"));
            }

            list.Add(new MissionBehaviorDiagnostic());

            try
            {
                ModLogger.Info("CoopBattle CreateBehaviorsForMission count=" + list.Count + ", IsServer=" + isServer + ", isDedicated=" + isDedicated);
                for (int i = 0; i < list.Count; i++)
                    ModLogger.Info("  [" + i + "] " + list[i].GetType().FullName);
            }
            catch (Exception ex) { ModLogger.Info("CoopBattle behavior list log failed: " + ex.Message); }

            return list;
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
