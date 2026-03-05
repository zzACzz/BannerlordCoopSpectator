using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Multiplayer;

namespace CoopSpectator.GameMode
{
    /// <summary>
    /// Реєстрований game mode "TdmClone" — клон TDM 1:1 з тією ж логікою що й CoopTdm.
    /// Єдина відмінність — ім'я (GameModeId) для перевірки узгодження з host payload та dedicated startup config.
    /// </summary>
    public sealed class MissionMultiplayerTdmCloneMode : MissionBasedMultiplayerGameMode
    {
        /// <summary>Має збігатися з CoopGameModeIds.TdmClone та з конфігом дедика (GameType / addmap).</summary>
        public static string GameModeId => CoopGameModeIds.TdmClone;

        public MissionMultiplayerTdmCloneMode(string name) : base(name)
        {
        }

        public override void StartMultiplayerGame(string scene)
        {
            ModLogger.Info("StartMultiplayerGame TdmClone called, scene=" + (scene ?? ""));
            if (GameNetwork.IsServer)
                ModLogger.Info("[CoopSpectator] Server starting mission, GameType=" + GameModeId + " (must match client GetMultiplayerGameMode and multiplayer_strings).");
            ModLogger.Info("Opening mission TdmClone, scene=" + (scene ?? "") + ", timestamp=" + DateTime.UtcNow.ToString("o"));
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
                list.Add(new MissionMultiplayerTdmClone());
            list.Add(new MissionMultiplayerTdmCloneClient());

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
                ModLogger.Info("TdmClone CreateBehaviorsForMission count=" + list.Count + ", IsServer=" + isServer + ", isDedicated=" + isDedicated);
                for (int i = 0; i < list.Count; i++)
                    ModLogger.Info("  [" + i + "] " + list[i].GetType().FullName);
            }
            catch (Exception ex) { ModLogger.Info("TdmClone behavior list log failed: " + ex.Message); }

            return list;
        }

        private static void AddIfNotNull(List<MissionBehavior> list, MissionBehavior behavior)
        {
            if (behavior != null) list.Add(behavior);
        }

        private static void AddOptional(List<MissionBehavior> list, MissionBehavior behavior, string name)
        {
            if (behavior == null)
            {
                ModLogger.Info("TdmClone: " + name + " skipped with warning (optional).");
                return;
            }
            list.Add(behavior);
        }

        private static void AddRequired(List<MissionBehavior> list, MissionBehavior behavior, string name)
        {
            if (behavior == null)
            {
                ModLogger.Info("TdmClone: Required mission behavior '" + name + "' could not be created. Aborting mission open.");
                throw new InvalidOperationException("Required mission behavior '" + name + "' could not be created. Check logs for assembly/type resolution.");
            }
            list.Add(behavior);
        }

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
