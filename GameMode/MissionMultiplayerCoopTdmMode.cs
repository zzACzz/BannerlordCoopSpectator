using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Multiplayer;

namespace CoopSpectator.GameMode
{
    /// <summary>
    /// Реєстрований game mode "CoopTdm" — клон TDM 1:1. StartMultiplayerGame(scene) відкриває місію з тими ж behaviors що й TDM (лобі, таймер, team select, TDM scoreboard).
    /// Пізніше: max players, spawn/respawn під кампанію, roster sync.
    /// </summary>
    public sealed class MissionMultiplayerCoopTdmMode : MissionBasedMultiplayerGameMode
    {
        public const string GameModeId = "CoopTdm";

        public MissionMultiplayerCoopTdmMode(string name) : base(name)
        {
        }

        public override void StartMultiplayerGame(string scene)
        {
            ModLogger.Info("StartMultiplayerGame CoopTdm called, scene=" + (scene ?? ""));
            if (GameNetwork.IsServer)
                ModLogger.Info("[CoopSpectator] Server starting mission, GameType=" + GameModeId + " (must match client GetMultiplayerGameMode and multiplayer_strings).");
            ModLogger.Info("Opening mission CoopTdm, scene=" + (scene ?? "") + ", timestamp=" + DateTime.UtcNow.ToString("o"));
            MissionInitializerRecord record = new MissionInitializerRecord(scene);
            MissionState.OpenNew("MultiplayerTeamDeathmatch", record, CreateBehaviorsForMission);
        }

        private static IEnumerable<MissionBehavior> CreateBehaviorsForMission(Mission mission)
        {
            var list = new List<MissionBehavior>();
            bool isServer = GameNetwork.IsServer;
            bool isDedicated = IsDedicatedServerProcess();

            // Порядок 1:1 з офіційного прикладу (moddocs.bannerlord.com/multiplayer/custom_game_mode/)
            list.Add(MissionLobbyComponent.CreateBehavior());
            if (isServer)
                list.Add(new MissionMultiplayerCoopTdm());
            list.Add(new MissionMultiplayerCoopTdmClient());

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
                ModLogger.Info("CoopTdm CreateBehaviorsForMission count=" + list.Count + ", IsServer=" + isServer + ", isDedicated=" + isDedicated);
                for (int i = 0; i < list.Count; i++)
                    ModLogger.Info("  [" + i + "] " + list[i].GetType().FullName);
            }
            catch (Exception ex) { ModLogger.Info("CoopTdm behavior list log failed: " + ex.Message); }

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
                ModLogger.Info("CoopTdm: " + name + " skipped with warning (optional).");
                return;
            }
            list.Add(behavior);
        }

        /// <summary>Додає обов'язковий behavior; якщо null — лог і виняток (fail-fast), щоб не відкривати місію без UI-критичних компонентів.</summary>
        private static void AddRequired(List<MissionBehavior> list, MissionBehavior behavior, string name)
        {
            if (behavior == null)
            {
                ModLogger.Info("CoopTdm: Required mission behavior '" + name + "' could not be created. Aborting mission open.");
                throw new InvalidOperationException("Required mission behavior '" + name + "' could not be created. Check logs for assembly/type resolution.");
            }
            list.Add(behavior);
        }

        /// <summary>Чи це dedicated server (без UI). Крихка перевірка по імені процесу; якщо з’явиться GameNetwork.IsDedicatedServer — замінити. Якщо таймаут лишиться після зміни сцени — тест з мінімальним списком behaviors на дедику (лише MissionLobby + CoopTdm + таймер), далі додавати по одному.</summary>
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
