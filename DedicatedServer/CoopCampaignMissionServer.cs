using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using CoopSpectator.Campaign;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.Hideout;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.Network;
using Newtonsoft.Json;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.DedicatedServer
{
    // Production main-thread adapter, independent of test automation. Native commands
    // retain ListedServer's LoadMission/topology flow; never call StartMultiplayerGame.
    internal static class CoopCampaignMissionServer
    {
        private static string _session, _failure, _instance, _completedBattleKey;
        private static CoopCampaignMissionLifecycle _lifecycle;
        private static CoopCampaignMissionEnvelope _battle;
        private static CoopMissionProcessIdentity _self;
        private static CoopCampaignMissionAdmission _admission;
        private static Mission _ownedMission;
        private static bool _ownedMap, _opening, _initialized;
        private static long _sequence, _origin, _nextTick;
        private static MethodInfo _idle, _startMission, _endMission;
        private static FieldInfo _nativeBattleIndex;
        private static PropertyInfo _managerInstance, _listedServer, _registered, _playing;
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static double _lastFreshAt;
        private static CoopCampaignMissionOpening<Mission> _dispatchOpening;
        private static Mission _closingMission;
        private static bool _dispatchClosing;

        private static bool IsOwnedBattle(Mission mission) => _lifecycle != null && !_ownedMap &&
            mission != null && ReferenceEquals(mission, _ownedMission);

        // Only this session's adapter can authorize a completed battle close.
        // Native warmup must not reset the scene while the host applies its result.
        private static bool AllowWarmupTick(MultiplayerWarmupComponent __instance) =>
            !IsOwnedBattle(__instance.Mission) || _completedBattleKey != _battle?.Intent.BattleKey;

        private static bool AllowLobbyEnding(MissionLobbyComponent __instance) =>
            !IsOwnedBattle(__instance.Mission) ||
            (_dispatchClosing && ReferenceEquals(__instance.Mission, _closingMission));

        private static void SelectCloseTimer(MultiplayerTimerComponent __instance, ref float duration)
        {
            // Zero removes presentation pacing, not a persistence/readiness gate.
            // Keep native Ending broadcast, UDP tick, UnloadMission and teardown.
            if (_dispatchClosing && ReferenceEquals(__instance.Mission, _closingMission) &&
                __instance.Mission.GetMissionBehavior<MissionLobbyComponent>()?.CurrentMultiplayerState ==
                    MissionLobbyComponent.MultiplayerGameState.Ending)
                duration = 0f;
        }

        private static void InstallCloseGuards()
        {
            var harmony = new Harmony("com.coopspectator.dedicated.owned-mission-close");
            harmony.Patch(AccessTools.Method(typeof(MultiplayerWarmupComponent), "OnPreDisplayMissionTick"),
                prefix: new HarmonyMethod(typeof(CoopCampaignMissionServer), nameof(AllowWarmupTick)));
            harmony.Patch(AccessTools.Method(typeof(MissionLobbyComponent), "SetStateEndingAsServer"),
                prefix: new HarmonyMethod(typeof(CoopCampaignMissionServer), nameof(AllowLobbyEnding)));
            harmony.Patch(AccessTools.Method(typeof(MultiplayerTimerComponent), "StartTimerAsServer"),
                prefix: new HarmonyMethod(typeof(CoopCampaignMissionServer), nameof(SelectCloseTimer)));
        }

        // Called by the existing OpenNew postfix; no native state mutation.
        internal static void ObserveNativeOpening(Mission mission)
        {
            _dispatchOpening?.Observe(mission, System.Threading.Thread.CurrentThread.ManagedThreadId);
        }

        public static void Tick()
        {
            if (!ExperimentalFeatures.EnableAutomaticCampaignMissions || _failure != null) return;
            long now = Stopwatch.GetTimestamp();
            if (now < _nextTick) return;
            _nextTick = now + Stopwatch.Frequency / 4;
            try
            {
                if (!_initialized)
                {
                    _initialized = true;
                    _session = Environment.GetEnvironmentVariable(CoopCampaignMissionChannel.SessionEnvironment);
                    if (string.IsNullOrEmpty(_session)) return; // Manual server: no ownership to adopt.
                    InstallCloseGuards();
                    _lifecycle = new CoopCampaignMissionLifecycle(_session, 120);
                    using (var process = Process.GetCurrentProcess()) _self = CoopMissionProcessIdentity.Capture(process);
                    _admission = new CoopCampaignMissionAdmission(_session, _self);
                    CoopCampaignMissionChannel.ResultPublished = OnResultPublished;
                }
                if (_lifecycle == null) return;
                CoopCampaignMissionEnvelope envelope = ReadAdmitted(now);
                if (envelope != null) _lastFreshAt = Clock.Elapsed.TotalSeconds;
                else if (Clock.Elapsed.TotalSeconds - _lastFreshAt > 300)
                { Fail("host-session-expired"); return; }

                object manager = GetManager();
                bool ready = manager != null && (bool)_idle.Invoke(manager, null) && ListedReady(manager);
                object active = GameStateManager.Current?.ActiveState;
                Mission mission = Mission.Current;
                bool canStart = ready && mission == null && active != null && active.GetType().GetInterfaces().Any(
                    type => type.FullName == "TaleWorlds.MountAndBlade.ListedServer.IIntermissionState");
                bool canEnd = ready && active is MissionState && mission != null && !mission.MissionEnded &&
                    mission.GetMissionBehavior<MissionLobbyComponent>() != null;
                bool owned = mission != null && ReferenceEquals(mission, _ownedMission);
                bool readyObservation = !_opening || (owned && mission.CurrentState == Mission.State.Continuing);
                if (_opening && readyObservation) _opening = false;
                var observed = new CoopCampaignMissionObservation {
                    Session = _session, Sequence = checked(++_sequence), Fresh = readyObservation,
                    Kind = mission == null ? CoopCampaignObservedMissionKind.Intermission : !owned ? CoopCampaignObservedMissionKind.Other :
                        _ownedMap ? CoopCampaignObservedMissionKind.Map : CoopCampaignObservedMissionKind.Battle,
                    MissionInstance = owned ? _instance : mission == null ? null : "unowned",
                    OriginCommand = owned ? _origin : 0, BattleKey = owned && !_ownedMap ? _battle?.Intent.BattleKey : null,
                    CanStart = canStart, CanEnd = canEnd, CompletedBattleKey = _completedBattleKey };
                var decision = _lifecycle.Step(envelope?.Intent, observed, Clock.Elapsed.TotalSeconds);
                if (decision.Action == CoopCampaignMissionAction.Fault) { Fail(decision.Reason); return; }
                if (decision.Action == CoopCampaignMissionAction.Wait) return;
                if (decision.Action == CoopCampaignMissionAction.EndMap || decision.Action == CoopCampaignMissionAction.EndBattle)
                {
                    bool closingMap = decision.Action == CoopCampaignMissionAction.EndMap;
                    if (!owned || _ownedMap != closingMap || decision.MissionInstance != _instance || !canEnd ||
                        (!closingMap && (decision.BattleKey != _completedBattleKey || decision.BattleKey != _battle?.Intent.BattleKey)))
                        throw new InvalidOperationException("mission-close-identity-mismatch");
                    _closingMission = mission;
                    _dispatchClosing = true;
                    try { _endMission.Invoke(manager, null); }
                    finally { _dispatchClosing = false; _closingMission = null; }
                }
                else
                {
                    if (!canStart || envelope == null) throw new InvalidOperationException("start-readiness-lost");
                    Start(decision, envelope);
                }
                ModLogger.Info("CampaignMissionServer: dispatched " + decision.Action + " command=" + decision.Command);
            }
            catch (Exception ex) { Fail("adapter-" + ex.GetType().Name); }
        }

        private static CoopCampaignMissionEnvelope ReadAdmitted(long now)
        {
            var value = _admission.Admit(CoopCampaignMissionChannel.Read(_session), now, Stopwatch.Frequency, host => host.IsAlive());
            return value;
        }

        private static void Start(CoopCampaignMissionDecision decision, CoopCampaignMissionEnvelope value)
        {
            bool map = decision.Action == CoopCampaignMissionAction.StartMap;
            string scene = CoopCampaignMapPrototypeContract.MissionBootstrapScene;
            string gameType = CoopCampaignMapPrototypeContract.GameModeId;
            if (!map)
            {
                byte[] bytes = CoopCampaignMissionChannel.ReadBounded(
                    Path.Combine(CoopCampaignMissionChannel.DirectoryFor(_session), "host_roster.json"), CoopCampaignMissionChannel.MaxRosterBytes);
                if (CoopCampaignMissionChannel.Digest(bytes) != value.RosterSha256 || decision.BattleKey != value.RosterSha256)
                    throw new InvalidDataException("Roster digest mismatch.");
                var roster = JsonConvert.DeserializeObject<BattleRosterFileDto>(System.Text.Encoding.UTF8.GetString(bytes));
                var battle = roster?.Snapshot;
                if (battle == null || battle.BattleId != value.BattleId || battle.BattleInstanceId != value.BattleInstanceId ||
                    battle.CampaignId != value.CampaignId || battle.MultiplayerScene != value.Scene || battle.MultiplayerGameType != value.GameType ||
                    battle.Sides == null || battle.Sides.Count == 0) throw new InvalidDataException("Roster identity mismatch.");
                scene = value.Scene; gameType = value.GameType;
                bool day = CoopHideoutBossPhaseContract.IsHideoutScenario(battle.ScenarioContext?.ScenarioKind);
                bool night = CoopHideoutAmbushContract.IsHideoutAmbushScenario(battle.ScenarioContext?.ScenarioKind);
                if (day || night || gameType == CoopHideoutBossPhaseContract.GameModeId || gameType == CoopHideoutAmbushContract.GameModeId)
                {
                    if (!((day && gameType == CoopHideoutBossPhaseContract.GameModeId) ||
                        (night && gameType == CoopHideoutAmbushContract.GameModeId)) ||
                        !CoopHideoutBossPhaseContract.TryNormalizeDayHideoutSceneName(scene, out string normalized) || normalized != scene)
                        throw new InvalidDataException("Hideout scene contract mismatch.");
                }
                else if (gameType != CoopGameModeIds.OfficialBattle && gameType != CoopGameModeIds.CoopBattle)
                    throw new InvalidDataException("Unsupported battle mode; no fallback.");
                // Pin this exact stage for every existing native roster/topology reader.
                // A later campaign capture writes host_roster.json, never this active file.
                CoopCampaignMissionChannel.WriteAtomic(BattleRosterFileHelper.GetRosterFilePath(), bytes);
                BattleSnapshotRuntimeState.SetCurrent(battle, "automatic-mission-pinned-roster");
            }
            GameNetwork.HandleConsoleCommand("add_map_to_usable_maps " + scene + " " + gameType);
            GameNetwork.HandleConsoleCommand("GameType " + gameType);
            GameNetwork.HandleConsoleCommand("Map " + scene);
            if (MultiplayerOptions.OptionType.Map.GetStrValue() != scene || MultiplayerOptions.OptionType.GameType.GetStrValue() != gameType)
                throw new InvalidOperationException("Native options not applied.");
            if (!map)
            {
                MultiplayerOptions.OptionType.WarmupTimeLimitInSeconds.SetValue(MultiplayerOptions.OptionType.WarmupTimeLimitInSeconds.GetMinimumValue());
                MultiplayerOptions.OptionType.RoundPreparationTimeLimit.SetValue(MultiplayerOptions.OptionType.RoundPreparationTimeLimit.GetMinimumValue());
                MultiplayerOptions.OptionType.MapTimeLimit.SetValue(MultiplayerOptions.OptionType.MapTimeLimit.GetMaximumValue());
                MultiplayerOptions.OptionType.RoundTimeLimit.SetValue(MultiplayerOptions.OptionType.RoundTimeLimit.GetMaximumValue());
                if (CoopPreMissionTopologyNetworkComponent.RequiresPreMissionContract(gameType, scene))
                {
                    int previousIndex = (int)_nativeBattleIndex.GetValue(GetManager());
                    if (previousIndex < 0 || previousIndex > 10) throw new InvalidOperationException("Native battle index out of range.");
                    var topology = GameNetwork.GetNetworkComponent<CoopPreMissionTopologyNetworkComponent>();
                    if (topology == null || !topology.TryPublishPreparedMission(previousIndex == 10 ? 0 : previousIndex + 1))
                        throw new InvalidOperationException("Pre-mission topology not published.");
                }
            }
            // OpenNew returns the object synchronously, but Mission.Current is set
            // only by the later Mission.Initialize. Capture the exact return inside
            // this dispatch, not an unrelated later Current or matching scene.
            if (_dispatchOpening != null) throw new InvalidOperationException("Nested mission dispatch.");
            int thread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            var capture = new CoopCampaignMissionOpening<Mission>(thread);
            Mission opened;
            _dispatchOpening = capture;
            try
            {
                _startMission.Invoke(GetManager(), null);
                opened = capture.Complete(thread);
            }
            finally { _dispatchOpening = null; }
            if (opened == null || ReferenceEquals(opened, _ownedMission) || opened.SceneName != scene ||
                (map != (opened.GetMissionBehavior<CoopCampaignMapPrototypeNetworkController>() != null)))
                throw new InvalidOperationException("Native opening origin not proven.");
            _ownedMission = opened; _ownedMap = map; _opening = true;
            _instance = Guid.NewGuid().ToString("N"); _origin = decision.Command;
            _battle = map ? null : value;
        }

        private static void OnResultPublished(object mission, string campaign, string battle, string instance)
        {
            if (_failure == null && !_ownedMap && ReferenceEquals(mission, _ownedMission) && _battle != null &&
                campaign == _battle.CampaignId && battle == _battle.BattleId && instance == _battle.BattleInstanceId)
                _completedBattleKey = _battle.Intent.BattleKey;
        }

        private static object GetManager()
        {
            if (_managerInstance == null)
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "TaleWorlds.MountAndBlade.ListedServer");
                Type type = assembly?.GetType("TaleWorlds.MountAndBlade.ListedServer.ServerSideIntermissionManager");
                if (type == null) return null;
                _managerInstance = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                _idle = type.GetMethod("IsNewTaskAssignable", BindingFlags.Instance | BindingFlags.NonPublic);
                _startMission = type.GetMethod("StartMission", Type.EmptyTypes);
                _endMission = type.GetMethod("EndMission", Type.EmptyTypes);
                _nativeBattleIndex = type.GetField("_currentAutomatedBattleIndex", BindingFlags.Instance | BindingFlags.NonPublic);
                _listedServer = type.GetProperty("ListedServer");
                _registered = _listedServer?.PropertyType.GetProperty("IsRegistered");
                _playing = _listedServer?.PropertyType.GetProperty("IsPlaying");
                if (_managerInstance == null || _idle == null || _registered == null || _playing == null ||
                    _nativeBattleIndex == null || _nativeBattleIndex.FieldType != typeof(int) ||
                    _startMission == null || _endMission == null || _idle.ReturnType != typeof(bool) ||
                    _idle.GetParameters().Length != 0 || _startMission.ReturnType != typeof(void) || _endMission.ReturnType != typeof(void))
                    throw new InvalidOperationException("Native lifecycle contract unavailable.");
            }
            return _managerInstance.GetValue(null, null);
        }
        private static bool ListedReady(object manager)
        {
            object listed = _listedServer.GetValue(manager, null);
            return listed != null && (bool)_registered.GetValue(listed, null) && (bool)_playing.GetValue(listed, null);
        }
        private static void Fail(string reason)
        {
            if (_failure != null) return;
            _failure = reason;
            ModLogger.Info("CampaignMissionServer: automatic transitions stopped: " + reason);
            // Leave any current mission alone. No blind retry, process termination,
            // adoption of a different server or legacy-command fallback.
        }
    }
}
