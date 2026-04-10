using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
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
        private const string MinimalDedicatedFallbackScene = "mp_tdm_map_001";
        /// <summary>Має збігатися з CoopGameModeIds.TdmClone та з конфігом дедика (GameType / addmap).</summary>
        public static string GameModeId => CoopGameModeIds.TdmClone;

        public MissionMultiplayerTdmCloneMode(string name) : base(name)
        {
        }

        public override void StartMultiplayerGame(string scene)
        {
            ModLogger.Info("TdmClone StartMultiplayerGame ENTER scene=" + (scene ?? ""));
            try
            {
                if (GameNetwork.IsServer && IsDedicatedServerProcess() && MinimalDedicatedMissionMode.UseMinimalDedicatedMode &&
                    string.Equals(scene, "mp_skirmish_spawn_test", StringComparison.OrdinalIgnoreCase))
                {
                    ModLogger.Info("TdmClone StartMultiplayerGame overriding scene from mp_skirmish_spawn_test to " + MinimalDedicatedFallbackScene + " for dedicated minimal-mode compatibility test.");
                    scene = MinimalDedicatedFallbackScene;
                }

                ModLogger.Info("[ID consistency] TdmClone GameModeId=" + GameModeId + " CoopGameModeIds.TdmClone=" + CoopGameModeIds.TdmClone);
                if (GameNetwork.IsServer)
                    ModLogger.Info("[CoopSpectator] Server starting mission, GameType=" + GameModeId);
                ModLogger.Info("Opening mission TdmClone, scene=" + (scene ?? "") + ", timestamp=" + DateTime.UtcNow.ToString("o"));

                MissionInitializerRecord record = new MissionInitializerRecord(scene);
                ModLogger.Info("TdmClone about to call MissionState.OpenNew (before this: create_mission / behavior factory not yet invoked).");
                MissionState.OpenNew("MultiplayerTeamDeathmatch", record, GetBehaviorsForMissionWrapper);
                ModLogger.Info("TdmClone MissionState.OpenNew returned (mission bootstrap may still run asynchronously).");
            }
            catch (Exception ex)
            {
                ModLogger.Error("TdmClone StartMultiplayerGame EXCEPTION", ex);
                throw;
            }
        }

        /// <summary>Обгортка, щоб залогувати момент виклику фабрики behaviors движком (після create_mission, до першого yield).</summary>
        private static IEnumerable<MissionBehavior> GetBehaviorsForMissionWrapper(Mission mission)
        {
            ModLogger.Info("TdmClone behavior factory delegate INVOKED by engine mission=" + (mission != null) + " (this is after create_mission, first point we get control).");
            List<MissionBehavior> list;
            try
            {
                list = new List<MissionBehavior>();
                foreach (var b in CreateBehaviorsForMission(mission))
                    list.Add(b);
            }
            catch (Exception ex)
            {
                ModLogger.Error("TdmClone GetBehaviorsForMissionWrapper EXCEPTION", ex);
                throw;
            }
            return list;
        }

        private static IEnumerable<MissionBehavior> CreateBehaviorsForMission(Mission mission)
        {
            bool isServer = GameNetwork.IsServer;
            bool isDedicated = IsDedicatedServerProcess();
            bool useMinimal = isServer && isDedicated && MinimalDedicatedMissionMode.UseMinimalDedicatedMode;
            ModLogger.Info("TdmClone CreateBehaviorsForMission ENTER IsServer=" + isServer + " IsDedicated=" + isDedicated + " GameType=" + GameModeId + " UseMinimalDedicatedMode=" + useMinimal);

            if (isDedicated && isServer)
                LogDedicatedBuildVersion();
            if (!isServer)
                LogClientBuildVersionAndMismatchWarning();

            List<MissionBehavior> list = isServer
                ? (useMinimal
                    ? BuildMinimalDedicatedMissionBehaviorsForTdmClone(mission)
                    : BuildServerMissionBehaviorsForTdmClone(mission, isDedicated))
                : (MinimalDedicatedMissionMode.UseMinimalDedicatedMode
                    ? BuildMinimalClientMissionBehaviorsForTdmClone(mission)
                    : BuildClientMissionBehaviorsForTdmClone(mission, isDedicated));

            if (isServer)
                ValidateServerStackSanity(list);
            else
            {
                int removed = ValidateBehaviorDependencies(list, isServer);
                if (removed > 0)
                    ModLogger.Info("TdmClone client validation removed " + removed + " invalid behavior(s).");
                else
                    ModLogger.Info("TdmClone client validation passed.");
            }

            LogFinalBehaviorStack(list, isServer, isDedicated);
            return YieldBehaviorsWithLog(list, isServer);
        }

        /// <summary>Серверний stack для TdmClone: без client-only behaviors, з обов'язковим MissionScoreboardComponent (потрібен для MissionCustomGameServerComponent.AfterStart).</summary>
        private static List<MissionBehavior> BuildServerMissionBehaviorsForTdmClone(Mission mission, bool isDedicated)
        {
            var list = new List<MissionBehavior>();
            list.Add(MissionLobbyComponent.CreateBehavior());
            list.Add(new MissionMultiplayerTdmClone());
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MultiplayerAchievementComponent"));
            list.Add(new MultiplayerTimerComponent());
            ModLogger.Info("TdmClone server: MultiplayerMissionAgentVisualSpawnComponent and MissionLobbyEquipmentNetworkComponent skipped (client-only).");
            list.Add(new MultiplayerTeamSelectComponent());
            AddRequired(list, MissionBehaviorHelpers.TryCreateHardBorderPlacer(), "MissionHardBorderPlacer");
            AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryPlacer(), "MissionBoundaryPlacer");
            AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryCrossingHandler(mission), "MissionBoundaryCrossingHandler");
            list.Add(new MultiplayerPollComponent());
            list.Add(new MultiplayerAdminComponent());
            if (!isDedicated)
                list.Add(new MultiplayerGameNotificationsComponent());
            AddOptional(list, MissionBehaviorHelpers.TryCreateMissionOptionsComponent(mission), "MissionOptionsComponent");
            MissionBehavior scoreboard = MissionBehaviorHelpers.TryCreateMissionScoreboardComponent();
            if (scoreboard != null)
                list.Add(scoreboard);
            else
                ModLogger.Error("[TdmCloneStack] MissionScoreboardComponent could not be created; MissionCustomGameServerComponent.AfterStart may crash. Check Multiplayer assembly.", null);
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MissionMatchHistoryComponent"));
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.EquipmentControllerLeaveLogic"));
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MissionRecentPlayersComponent"));
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MultiplayerPreloadHelper"));
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MissionAgentPanicHandler"));
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.AgentHumanAILogic"));
            list.Add(new MissionBehaviorDiagnostic());
            list.Add(new CoopMissionNetworkBridge());
            list.Add(new CoopMissionSpawnLogic());
            return list;
        }

        private static List<MissionBehavior> BuildMinimalDedicatedMissionBehaviorsForTdmClone(Mission mission)
        {
            var list = new List<MissionBehavior>();
            ModLogger.Info("TdmClone server: using client-safe minimal dedicated stack for join crash isolation.");
            ModLogger.Info("TdmClone minimal stack: MissionMinimalServerDiagnosticMode + TeamSelect/Poll/Options/Boundary + MissionBehaviorDiagnostic, without MissionScoreboardComponent.");
            list.Add(new MissionMinimalServerDiagnosticMode());
            list.Add(new MultiplayerTeamSelectComponent());
            AddRequired(list, MissionBehaviorHelpers.TryCreateHardBorderPlacer(), "MissionHardBorderPlacer");
            AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryPlacer(), "MissionBoundaryPlacer");
            AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryCrossingHandler(mission), "MissionBoundaryCrossingHandler");
            list.Add(new MultiplayerPollComponent());
            AddOptional(list, MissionBehaviorHelpers.TryCreateMissionOptionsComponent(mission), "MissionOptionsComponent");
            list.Add(new MissionBehaviorDiagnostic());
            return list;
        }

        private static List<MissionBehavior> BuildMinimalClientMissionBehaviorsForTdmClone(Mission mission)
        {
            var list = new List<MissionBehavior>();
            ModLogger.Info("TdmClone client: using minimal client-compatible stack for join crash isolation.");
            ModLogger.Info("TdmClone client minimal stack: MissionMinimalClientDiagnosticMode + MissionBehaviorDiagnostic only (MissionMultiplayerTdmCloneClient excluded for bisection).");
            list.Add(new MissionMinimalClientDiagnosticMode());
            list.Add(new MissionBehaviorDiagnostic());
            return list;
        }

        /// <summary>Клієнтський stack для TdmClone: з MissionMultiplayerTdmCloneClient, visual spawn, MissionScoreboardComponent, UI behaviors.</summary>
        private static List<MissionBehavior> BuildClientMissionBehaviorsForTdmClone(Mission mission, bool isDedicated)
        {
            var list = new List<MissionBehavior>();
            list.Add(MissionLobbyComponent.CreateBehavior());
            list.Add(new MissionMultiplayerTdmCloneClient());
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MultiplayerAchievementComponent"));
            list.Add(new MultiplayerTimerComponent());
            MissionBehavior visualSpawn = MissionBehaviorHelpers.TryCreateMissionAgentVisualSpawnComponent();
            if (visualSpawn != null)
            {
                list.Add(visualSpawn);
                ModLogger.Info("TdmClone client: adding MultiplayerMissionAgentVisualSpawnComponent and MissionLobbyEquipmentNetworkComponent.");
                list.Add(new MissionLobbyEquipmentNetworkComponent());
            }
            else
                ModLogger.Info("TdmClone client: skip MissionLobbyEquipmentNetworkComponent (MultiplayerMissionAgentVisualSpawnComponent unavailable).");
            list.Add(new MultiplayerTeamSelectComponent());
            AddRequired(list, MissionBehaviorHelpers.TryCreateHardBorderPlacer(), "MissionHardBorderPlacer");
            AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryPlacer(), "MissionBoundaryPlacer");
            AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryCrossingHandler(mission), "MissionBoundaryCrossingHandler");
            list.Add(new MultiplayerPollComponent());
            list.Add(new MultiplayerAdminComponent());
            if (!isDedicated)
                list.Add(new MultiplayerGameNotificationsComponent());
            AddOptional(list, MissionBehaviorHelpers.TryCreateMissionOptionsComponent(mission), "MissionOptionsComponent");
            MissionBehavior clientScoreboard = MissionBehaviorHelpers.TryCreateMissionScoreboardComponent();
            if (clientScoreboard != null) list.Add(clientScoreboard);
            else ModLogger.Info("[TdmCloneStack] client: MissionScoreboardComponent could not be created (optional for UI).");
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MissionMatchHistoryComponent"));
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.EquipmentControllerLeaveLogic"));
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MissionRecentPlayersComponent"));
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MultiplayerPreloadHelper"));
            list.Add(new MissionBehaviorDiagnostic());
            list.Add(new CoopMissionNetworkBridge());
            list.Add(new CoopMissionClientLogic());
            return list;
        }

        /// <summary>Перевірка server stack: немає client-only behaviors; є MissionScoreboardComponent (інакше MissionCustomGameServerComponent.AfterStart крашить). При порушенні — виправляємо список і логуємо ERROR.</summary>
        private static void ValidateServerStackSanity(List<MissionBehavior> list)
        {
            if (list == null) return;
            bool changed = false;
            string[] clientOnlyNames = { "MissionMultiplayerTdmCloneClient", "MissionCustomGameClientComponent", "CoopMissionClientLogic", "MissionLobbyEquipmentNetworkComponent", "MultiplayerMissionAgentVisualSpawnComponent" };
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] == null) continue;
                string name = list[i].GetType().Name;
                foreach (string forbidden in clientOnlyNames)
                {
                    if (name == forbidden)
                    {
                        ModLogger.Error("[TdmCloneStack] server stack sanity: removed client-only behavior " + name + ". It must not be on server.", null);
                        list.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
            }
            bool hasCustomServer = MissionBehaviorHelpers.ListContainsBehaviorType(list, "MissionCustomGameServerComponent");
            bool hasScoreboard = MissionBehaviorHelpers.ListContainsBehaviorType(list, "MissionScoreboardComponent");
            if (hasCustomServer && !hasScoreboard)
            {
                MissionBehavior scoreboard = MissionBehaviorHelpers.TryCreateMissionScoreboardComponent();
                if (scoreboard != null)
                {
                    list.Add(scoreboard);
                    changed = true;
                    ModLogger.Error("[TdmCloneStack] server stack sanity: MissionScoreboardComponent was missing; added it (MissionCustomGameServerComponent.AfterStart would have crashed).", null);
                }
                else
                    ModLogger.Error("[TdmCloneStack] server stack sanity: MissionScoreboardComponent missing and could not be created (sanity add failed). MissionCustomGameServerComponent.AfterStart may crash.", null);
            }
            else if (!hasCustomServer && hasScoreboard)
            {
                ModLogger.Info("[TdmCloneStack] server stack sanity: MissionScoreboardComponent present without MissionCustomGameServerComponent; leaving as-is.");
            }
            else if (!hasCustomServer && !hasScoreboard)
            {
                ModLogger.Info("[TdmCloneStack] server stack sanity: scoreboard not required because MissionCustomGameServerComponent is absent.");
            }
            if (changed)
                ModLogger.Info("[TdmCloneStack] server stack sanity: list was corrected; final count=" + list.Count);
        }

        /// <summary>Фінальний лог: count, нумерований список, HasMissionScoreboardComponent, HasClientOnlyBehaviorOnServer.</summary>
        private static void LogFinalBehaviorStack(List<MissionBehavior> list, bool isServer, bool isDedicated)
        {
            try
            {
                int count = list?.Count ?? 0;
                ModLogger.Info("[TdmCloneStack] CreateBehaviorsForMission final count=" + count + " IsServer=" + isServer + " IsDedicated=" + isDedicated);
                bool hasScoreboard = MissionBehaviorHelpers.ListContainsBehaviorType(list, "MissionScoreboardComponent");
                bool hasCustomServer = MissionBehaviorHelpers.ListContainsBehaviorType(list, "MissionCustomGameServerComponent");
                bool hasClientOnlyOnServer = isServer && (MissionBehaviorHelpers.ListContainsBehaviorType(list, "MissionMultiplayerTdmCloneClient") || MissionBehaviorHelpers.ListContainsBehaviorType(list, "MissionCustomGameClientComponent") || MissionBehaviorHelpers.ListContainsBehaviorType(list, "CoopMissionClientLogic"));
                ModLogger.Info("[TdmCloneStack] HasMissionScoreboardComponent=" + hasScoreboard + " HasMissionCustomGameServerComponent=" + hasCustomServer + " HasClientOnlyBehaviorOnServer=" + hasClientOnlyOnServer);
                if (hasClientOnlyOnServer)
                    ModLogger.Error("[TdmCloneStack] server stack still contains client-only behavior (sanity check should have removed it).", null);
                if (isServer && hasCustomServer && !hasScoreboard)
                    ModLogger.Error("[TdmCloneStack] server stack missing MissionScoreboardComponent.", null);
                for (int i = 0; i < count; i++)
                    ModLogger.Info("[TdmCloneStack]   [" + i + "] " + (list[i]?.GetType().FullName ?? "null"));
            }
            catch (Exception ex) { ModLogger.Info("[TdmCloneStack] behavior list log failed: " + ex.Message); }
        }

        /// <summary>Видаляє behaviors, у яких не виконано hard dependency (наприклад MissionLobbyEquipmentNetworkComponent потребує MultiplayerMissionAgentVisualSpawnComponent). Повертає кількість видалених. </summary>
        private static int ValidateBehaviorDependencies(List<MissionBehavior> list, bool isServer)
        {
            if (list == null || list.Count == 0) return 0;
            int removed = 0;
            bool hasVisualSpawn = MissionBehaviorHelpers.ListContainsBehaviorType(list, "MultiplayerMissionAgentVisualSpawnComponent");
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] == null) continue;
                string name = list[i].GetType().Name;
                if (name == "MissionLobbyEquipmentNetworkComponent" && !hasVisualSpawn)
                {
                    ModLogger.Info("TdmClone client validation: MissionLobbyEquipmentNetworkComponent skipped, missing MultiplayerMissionAgentVisualSpawnComponent.");
                    list.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>Повертає behaviors по одному з логом перед кожним yield — щоб знайти останній успішно повернутий перед крашем движка.</summary>
        private static IEnumerable<MissionBehavior> YieldBehaviorsWithLog(List<MissionBehavior> list, bool isServer)
        {
            for (int i = 0; i < list.Count; i++)
            {
                MissionBehavior b = list[i];
                string name = b?.GetType().FullName ?? "null";
                ModLogger.Info("TdmClone [" + (isServer ? "server" : "client") + "] GetMissionBehaviors yielding #" + i + " " + name);
                yield return b;
            }
            ModLogger.Info("TdmClone [" + (isServer ? "server" : "client") + "] GetMissionBehaviors yielded all " + list.Count + " behaviors.");
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

        /// <summary>Логує build dedicated server для порівняння з клієнтом (mismatch 109797 vs 110062 може спричиняти краші).</summary>
        private static void LogDedicatedBuildVersion()
        {
            string version = "unknown";
            try
            {
                Type avType = Type.GetType("TaleWorlds.Library.ApplicationVersion, TaleWorlds.Library", false);
                if (avType != null)
                {
                    var pi = avType.GetProperty("ApplicationVersion", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (pi != null)
                    {
                        object v = pi.GetValue(null);
                        if (v != null) version = v.ToString();
                    }
                }
            }
            catch (Exception ex) { ModLogger.Info("TdmClone dedicated: could not get ApplicationVersion: " + ex.Message); }
            ModLogger.Info("TdmClone dedicated server build version: " + version + " (client must use same build to avoid mismatch crashes).");
        }

        /// <summary>Логує поточний build клієнта та попередження про несумісність з сервером (наприклад 109797 vs 110062).</summary>
        private static void LogClientBuildVersionAndMismatchWarning()
        {
            string version = "unknown";
            try
            {
                Type avType = Type.GetType("TaleWorlds.Library.ApplicationVersion, TaleWorlds.Library", false);
                if (avType != null)
                {
                    System.Reflection.PropertyInfo pi = avType.GetProperty("ApplicationVersion", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (pi != null)
                    {
                        object v = pi.GetValue(null);
                        if (v != null) version = v.ToString();
                    }
                }
            }
            catch (Exception ex) { ModLogger.Info("TdmClone: could not get ApplicationVersion: " + ex.Message); }
            ModLogger.Info("TdmClone client build version: " + version);
            ModLogger.Info("TdmClone WARNING: Client/server Bannerlord build mismatch (e.g. 109797 vs 110062) can cause crashes; use same build for testing.");
        }
    }
}



