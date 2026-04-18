// Файл: Mission/CoopMissionBehaviors.cs
// Призначення: логіка місії для кооп-спектатора — клієнтський зворотний зв'язок (етап 3.5), логування стану для Етапу 3.3 (spectator/unit/spawn), заглушка серверного спавну (етап 3.4).

using System; // Exception
using System.Collections.Generic; // List<string>
using System.Collections; // IEnumerable
using System.Linq; // Distinct, FirstOrDefault
using System.Reflection; // MethodInfo
using System.Runtime.CompilerServices; // RuntimeHelpers
using TaleWorlds.Core; // BasicCharacterObject, MBObjectManager (для спавну)
using TaleWorlds.Engine;
using TaleWorlds.InputSystem; // Input, InputKey
using TaleWorlds.Library; // Vec2, Vec3
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade; // Mission, MissionLogic, GameNetwork, Agent, Team, MissionMode
using TaleWorlds.MountAndBlade.Multiplayer;
using TaleWorlds.ObjectSystem; // MBObjectManager
using CoopSpectator.Campaign; // BattleRosterFileHelper (варіант A: roster з кампанії)
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure; // ModLogger, UiFeedback
using CoopSpectator.MissionModels;
using CoopSpectator.Network.Messages;

namespace CoopSpectator.MissionBehaviors
{
    /// <summary>
    /// Клієнтська логіка в MP-місії: логування стану (mission entered, has agent, is spectator, team, spawn, returned to spectator), статус "у битві", завершення місії (етап 3.3 / 3.5).
    /// </summary>
    public sealed class CoopMissionClientLogic : MissionLogic
    {
        private const float LogStateIntervalSeconds = 5f; // Інтервал між повторними логами стану (щоб не спамити).
        private float _timeUntilNextStateLog;
        private float _timeUntilNextEntryHint;
        private Agent _lastControlledAgent; // Попередній Agent.Main для детекції зміни контролю / повернення в spectator.
        private string _lastRequestedPreferredTroopKey;
        private string _lastObservedSpectatorTroopSelectionKey;
        private string _lastMirroredLegacyOverlaySelectionKey;
        private string _lastMirroredAuthoritativeStatusKey;
        private bool _visualSpawnAutoConfirmHooked;
        private bool _visualSpawnAutoConfirmTriggered;
        private object _visualSpawnComponent;
        private Delegate _onMyAgentVisualSpawnedDelegate;
        private string _visualSpawnLifecycleEventName;
        private readonly HashSet<string> _loggedAutoConfirmBehaviorDiagnostics = new HashSet<string>(StringComparer.Ordinal);
        private bool _hasLoggedMissionBehaviorCatalog;
        private bool _hasLoggedClassLoadoutDiagnostics;
        private bool _hasLoggedActiveClassLoadoutDiagnostics;
        private bool _hasLoggedClassLoadoutPendingInitialization;
        private bool _hasLoggedTeamSelectDiagnostics;
        private bool _hasLoggedScoreboardDiagnostics;
        private string _lastLoggedEntryPolicySnapshot;
        private string _lastAppliedClassLoadoutFilterKey;
        private string _lastAppliedTeamSelectCultureSyncKey;
        private string _lastAppliedScoreboardCultureSyncKey;
        private string _lastSuppressedVanillaEntryUiKey;
        private string _lastAutoRequestedTeamChangeKey;
        private string _lastRequestedVanillaSpawnKey;
        private readonly HashSet<int> _loggedControlFinalizeDiagnosticsByAgentIndex = new HashSet<int>();
        private int _pendingPostControlDiagnosticAgentIndex = -1;
        private float _pendingPostControlDiagnosticDelay;
        private readonly HashSet<int> _loggedPostControlDiagnosticsByAgentIndex = new HashSet<int>();
        private BattleSideEnum _lastObservedLegacyOverlaySide = BattleSideEnum.None;
        private string _lastShownOwnEntryHintKey;
        private string _lastShownOwnEntryMenuKey;
        private string _lastAnnouncedBattlePhaseKey;
        private bool _battleActiveAnnouncementShown;
        private bool _hasObservedBattleActivePhaseThisMission;
        private float _legacyOverlayAutoRequestCooldownRemaining;
        private float _timeUntilNextOwnEntryHotkey;
        private float _timeUntilNextOwnEntryMenuRefresh;
        private bool _showOwnEntryMenu;
        private const float EntryHintIntervalSeconds = 4f;
        private const float EntryHotkeyCooldownSeconds = 0.25f;
        private const float EntryMenuRefreshIntervalSeconds = 1.25f;
        private const float LegacyOverlayAutoRequestCooldownSeconds = 2f;
        private const bool EnableClientPreferredTroopRequestExperiment = false;
        private const bool EnableVisualSpawnAutoConfirmExperiment = true;
        private const bool EnableFixedClientTeamSelectCulturesExperiment = true;
        private const bool EnableVanillaSelectionUiExperiment = true;
        private const bool EnableVanillaEntryUiSuppressionExperiment = false;
        private const string FixedClientAttackerCultureId = "empire";
        private const string FixedClientDefenderCultureId = "vlandia";

        public override void AfterStart()
        {
            ModLogger.Info("CoopMissionClientLogic AfterStart ENTER");
            base.AfterStart();
            Mission mission = Mission;
            if (mission == null) return;

            ResetSelectionUiBridgeStateForNewMission("CoopMissionClientLogic.AfterStart");
            CoopMissionSpawnLogic.ResetClientMissionRuntimeState(
                "CoopMissionClientLogic.AfterStart:" + (mission.SceneName ?? "null"));
            CoopSpectator.Patches.BattleMapSpawnHandoffPatch.ResetRuntimeState(
                "CoopMissionClientLogic.AfterStart:" + (mission.SceneName ?? "null"));
            LogMissionEntered(mission);
            LogCurrentState(mission);
            if (SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(mission.SceneName))
            {
                ExactCampaignObjectCatalogBootstrap.EnsureLoaded("client-afterstart:" + (mission.SceneName ?? "null"));
                ExactCampaignRuntimeItemRegistry.EnsureLoadedFromState(
                    BattleSnapshotRuntimeState.GetState(),
                    "client-afterstart:" + (mission.SceneName ?? "null"));
                BattleMapContractDiagnostics.LogMissionRuntimeContract(mission, "CoopMissionClientLogic.AfterStart");
            }
            _lastControlledAgent = Agent.Main;
            _timeUntilNextStateLog = LogStateIntervalSeconds;
            _timeUntilNextEntryHint = 0f;
            _timeUntilNextOwnEntryHotkey = 0f;
            _timeUntilNextOwnEntryMenuRefresh = 0f;
            _showOwnEntryMenu = !EnableVanillaSelectionUiExperiment;
            _lastAnnouncedBattlePhaseKey = null;
            _battleActiveAnnouncementShown = false;
            _hasObservedBattleActivePhaseThisMission = false;
            if (EnableVisualSpawnAutoConfirmExperiment && !EnableVanillaSelectionUiExperiment)
                TryHookVisualSpawnAutoConfirm(mission);
            LogClassLoadoutDiagnosticsOnce(mission);
            LogActiveClassLoadoutDiagnosticsOnce(mission);
            LogEntryPolicySnapshot(mission);
            if (!ExperimentalFeatures.EnableCustomCoopSelectionOverlay)
                TryMirrorLegacyOverlaySelectionToBridge(mission);
            if (!EnableVanillaSelectionUiExperiment)
            {
                TryAutoRequestTeamChangeFromBridge(mission);
                TrySuppressVanillaEntryUi(mission);
                TrySyncFixedTeamSelectCultures(mission);
                TrySyncFixedScoreboardCultures(mission);
                TryFilterClassLoadoutToCoopUnits(mission);
            }

#if !COOPSPECTATOR_DEDICATED
            UiFeedback.ShowMessageDeferred("Coop: in battle. (Leave battle on host to return to lobby.)");
#endif
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            Mission mission = Mission;
            if (mission == null) return;

            TryReleaseStaleClientMainAgent(mission);
            TryRestoreClientMainAgentFromMissionPeer(mission);
            CoopMissionSpawnLogic.TryRunClientExactCampaignVisualObserver(mission);

            if (_legacyOverlayAutoRequestCooldownRemaining > 0f)
                _legacyOverlayAutoRequestCooldownRemaining = Math.Max(0f, _legacyOverlayAutoRequestCooldownRemaining - dt);

            Agent currentMain = Agent.Main;
            if (currentMain != _lastControlledAgent)
            {
                if (currentMain != null)
                {
                    ModLogger.Info("CoopMissionClientLogic: controlled agent changed — now controlling agent " + (currentMain.Name?.ToString() ?? currentMain.Index.ToString()));
                    TryRepairMainAgentControllerState(mission, currentMain);
                    LogControlFinalizeDiagnosticsForCurrentAgent(mission, currentMain);
                    _pendingPostControlDiagnosticAgentIndex = currentMain.Index;
                    _pendingPostControlDiagnosticDelay = 0.9f;
                    _lastRequestedPreferredTroopKey = null;
                    _lastObservedSpectatorTroopSelectionKey = null;
                    _lastMirroredLegacyOverlaySelectionKey = null;
                    _lastMirroredAuthoritativeStatusKey = null;
                    _lastRequestedVanillaSpawnKey = null;
                    _lastObservedLegacyOverlaySide = BattleSideEnum.None;
                    _visualSpawnAutoConfirmTriggered = false;
                }
                else if (_lastControlledAgent != null)
                {
                    ModLogger.Info("CoopMissionClientLogic: returned to spectator (agent lost or died).");
                    _pendingPostControlDiagnosticAgentIndex = -1;
                    _pendingPostControlDiagnosticDelay = 0f;
                    _lastRequestedPreferredTroopKey = null;
                    _lastObservedSpectatorTroopSelectionKey = null;
                    _lastMirroredLegacyOverlaySelectionKey = null;
                    _lastMirroredAuthoritativeStatusKey = null;
                    _lastRequestedVanillaSpawnKey = null;
                    _lastObservedLegacyOverlaySide = BattleSideEnum.None;
                    _visualSpawnAutoConfirmTriggered = false;
                    _lastAppliedClassLoadoutFilterKey = null;
                    _showOwnEntryMenu = !EnableVanillaSelectionUiExperiment;
                    _timeUntilNextOwnEntryMenuRefresh = 0f;
                    _lastShownOwnEntryMenuKey = null;

                    CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot phaseSnapshot = CoopBattleEntryStatusBridgeFile.ReadStatus();
                    string currentBattlePhase = phaseSnapshot?.BattlePhase ?? string.Empty;
                    if (!string.Equals(currentBattlePhase, CoopBattlePhase.BattleActive.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        _lastAnnouncedBattlePhaseKey = null;
                        _battleActiveAnnouncementShown = false;
                    }
                }
                _lastControlledAgent = currentMain;
            }

            if (EnableVisualSpawnAutoConfirmExperiment && !EnableVanillaSelectionUiExperiment)
                TryHookVisualSpawnAutoConfirm(mission);
            LogClassLoadoutDiagnosticsOnce(mission);
            LogActiveClassLoadoutDiagnosticsOnce(mission);
            LogEntryPolicySnapshot(mission);
            if (!ExperimentalFeatures.EnableCustomCoopSelectionOverlay)
                TryMirrorLegacyOverlaySelectionToBridge(mission);
            if (!EnableVanillaSelectionUiExperiment)
            {
                TryMirrorAuthoritativeStatusToBridge();
                TryAutoRequestTeamChangeFromBridge(mission);
                TrySuppressVanillaEntryUi(mission);
                TrySyncFixedTeamSelectCultures(mission);
                TrySyncFixedScoreboardCultures(mission);
                TryFilterClassLoadoutToCoopUnits(mission);
                TryRequestVanillaSpawnFromAuthoritativeStatus(mission);
            }
            TryResetSpawnPreviewStateForPreferredTroopChange(mission);
            TryHandleBattlePhaseUiTransition();
            TryHandleOwnEntryHotkeys(mission, dt);
            TryShowOwnEntryHint(mission, dt);
            TryShowOwnEntryMenu(mission, dt);
            TryLogPostControlAgentDiagnostics(mission, dt);
            if (EnableClientPreferredTroopRequestExperiment)
                TryTriggerClientPreferredTroopRequest(mission);

            _timeUntilNextStateLog -= dt;
            if (_timeUntilNextStateLog <= 0f)
            {
                _timeUntilNextStateLog = LogStateIntervalSeconds;
                LogCurrentState(mission);
            }
        }

        public override void OnMissionResultReady(MissionResult missionResult)
        {
            base.OnMissionResultReady(missionResult);
            ModLogger.Info("CoopMissionClientLogic: mission result ready — returning to lobby.");
        }

        private void TryShowOwnEntryHint(Mission mission, float dt)
        {
            if (mission == null)
                return;

            if (EnableVanillaSelectionUiExperiment)
                return;

            if (_showOwnEntryMenu)
                return;

            _timeUntilNextEntryHint -= dt;
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot = CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (snapshot == null || !snapshot.HasPeer)
                return;

            string lifecycle = snapshot.LifecycleState ?? string.Empty;
            string battlePhase = snapshot.BattlePhase ?? string.Empty;
            bool canStartFromBattlePrompt =
                snapshot.CanStartBattle &&
                (snapshot.HasAgent || string.Equals(lifecycle, "Alive", StringComparison.OrdinalIgnoreCase));
            bool shouldHint =
                !snapshot.HasAgent ||
                snapshot.CanRespawn ||
                canStartFromBattlePrompt ||
                string.Equals(lifecycle, "SpawnQueued", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycle, "AwaitingSelection", StringComparison.OrdinalIgnoreCase);
            if (!shouldHint)
                return;

            string side = snapshot.AssignedSide ?? snapshot.RequestedSide ?? snapshot.IntentSide ?? "none";
            string selectionId = ResolveStatusSelectionId(snapshot);
            string selectionLabel = ResolveSelectionDisplayLabel(selectionId);
            string spawn = snapshot.SpawnStatus ?? string.Empty;
            BattleSideEnum resolvedSide = ResolveStatusSide(snapshot);
            string[] sideSelections = GetAllowedSelectionIdsForSide(snapshot, resolvedSide);
            int selectionIndex = Array.FindIndex(sideSelections, candidate => string.Equals(candidate, selectionId, StringComparison.OrdinalIgnoreCase));
            string selectionOrderText = selectionIndex >= 0
                ? " [" + (selectionIndex + 1) + "/" + sideSelections.Length + "]"
                : (sideSelections.Length > 0 ? " [0/" + sideSelections.Length + "]" : string.Empty);
            string hintKey = side + "|" + (selectionId ?? string.Empty) + "|" + spawn + "|" + battlePhase + "|" + lifecycle + "|" + snapshot.CanRespawn + "|" + snapshot.CanStartBattle + "|" + snapshot.HasAgent;

            if (_timeUntilNextEntryHint > 0f && string.Equals(_lastShownOwnEntryHintKey, hintKey, StringComparison.Ordinal))
                return;

            _lastShownOwnEntryHintKey = hintKey;
            _timeUntilNextEntryHint = EntryHintIntervalSeconds;

            string message = canStartFromBattlePrompt
                        ? "Coop Battle: host can start. Press H."
                : "Coop Entry: side=" + side +
                  " unit=" + selectionLabel +
                  selectionOrderText +
                  " phase=" + battlePhase +
                  " state=" + lifecycle +
                  " spawn=" + spawn +
                  (snapshot.CanRespawn ? " | respawn ready" : string.Empty) +
                  (snapshot.CanStartBattle ? " | host can start" : string.Empty) +
                    " | keys: Ctrl+1/2 side, Ctrl+Q/E troop, Ctrl+R spawn, H start";
#if !COOPSPECTATOR_DEDICATED
            UiFeedback.ShowMessageDeferred(message);
#endif
        }

        private void TryHandleBattlePhaseUiTransition()
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return;

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot = CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (snapshot == null)
                return;

            string battlePhase = snapshot.BattlePhase ?? string.Empty;
            bool isBattleActive = string.Equals(battlePhase, CoopBattlePhase.BattleActive.ToString(), StringComparison.OrdinalIgnoreCase);
            bool isBattleEnded = string.Equals(battlePhase, CoopBattlePhase.BattleEnded.ToString(), StringComparison.OrdinalIgnoreCase);
            if (isBattleActive)
                _hasObservedBattleActivePhaseThisMission = true;
            if (!isBattleActive)
                _battleActiveAnnouncementShown = false;

            if (isBattleEnded && !_hasObservedBattleActivePhaseThisMission)
                return;

            if (string.Equals(_lastAnnouncedBattlePhaseKey, battlePhase, StringComparison.Ordinal))
                return;

            _lastAnnouncedBattlePhaseKey = battlePhase;
            if (isBattleEnded)
            {
                _showOwnEntryMenu = false;
                _timeUntilNextOwnEntryMenuRefresh = 0f;
                _lastShownOwnEntryMenuKey = null;

                string winnerSide = snapshot.WinnerSide ?? string.Empty;
                string completionReason = snapshot.BattleCompletionReason ?? string.Empty;
                ModLogger.Info(
                    "CoopMissionClientLogic: observed BattleEnded phase. " +
                    "WinnerSide=" + (string.IsNullOrWhiteSpace(winnerSide) ? "none" : winnerSide) +
                    " Reason=" + (string.IsNullOrWhiteSpace(completionReason) ? "unknown" : completionReason) +
                    " Action=wait-for-dedicated-lobby-transition.");

                string endMessage = "Coop Battle: ended";
                if (!string.IsNullOrWhiteSpace(winnerSide))
                    endMessage += " | winner=" + winnerSide;
                if (!string.IsNullOrWhiteSpace(completionReason))
                    endMessage += " | " + completionReason;
                OnOwnEntryHotkeyHandled(endMessage);
                return;
            }

            if (!isBattleActive || _battleActiveAnnouncementShown)
                return;

            _battleActiveAnnouncementShown = true;
            _showOwnEntryMenu = false;
            _timeUntilNextOwnEntryMenuRefresh = 0f;
            _lastShownOwnEntryMenuKey = null;
            OnOwnEntryHotkeyHandled("Coop Battle: started");
        }

        private void TryHandleOwnEntryHotkeys(Mission mission, float dt)
        {
            if (mission == null || !GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return;

            _timeUntilNextOwnEntryHotkey -= dt;
            if (_timeUntilNextOwnEntryHotkey > 0f)
                return;

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot = CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (snapshot == null)
                return;

            string lifecycle = snapshot.LifecycleState ?? string.Empty;
            if (Input.IsKeyPressed(InputKey.H))
            {
                bool canStartBattleNow =
                    snapshot.CanStartBattle &&
                    (snapshot.HasAgent || string.Equals(lifecycle, "Alive", StringComparison.OrdinalIgnoreCase));
                if (!canStartBattleNow)
                {
                    OnOwnEntryHotkeyHandled("Coop Entry: start battle not ready");
                    return;
                }

            if (CoopBattlePhaseBridgeFile.WriteStartBattleRequest("MP client H hotkey"))
                    OnOwnEntryHotkeyHandled("Coop Entry: battle start requested");
                return;
            }

            if (!Input.IsKeyDown(InputKey.LeftControl) && !Input.IsKeyDown(InputKey.RightControl))
                return;

            if (Input.IsKeyPressed(InputKey.D1))
            {
                TryWriteOwnSideSelection(snapshot, BattleSideEnum.Attacker);
                return;
            }

            if (Input.IsKeyPressed(InputKey.D2))
            {
                TryWriteOwnSideSelection(snapshot, BattleSideEnum.Defender);
                return;
            }

            if (Input.IsKeyPressed(InputKey.Q))
            {
                TryCycleOwnTroopSelection(snapshot, moveNext: false);
                return;
            }

            if (Input.IsKeyPressed(InputKey.E))
            {
                TryCycleOwnTroopSelection(snapshot, moveNext: true);
                return;
            }

            if (Input.IsKeyPressed(InputKey.R))
            {
                if (CoopBattleSpawnBridgeFile.WriteSpawnNowRequest("MP client hotkey"))
                {
                    OnOwnEntryHotkeyHandled("Coop Entry: spawn queued");
                }
                return;
            }

            if (Input.IsKeyPressed(InputKey.T))
            {
                if (CoopBattleSpawnBridgeFile.WriteForceRespawnableRequest("MP client hotkey"))
                    OnOwnEntryHotkeyHandled("Coop Entry: reset queued");
                return;
            }

            if (Input.IsKeyPressed(InputKey.M))
            {
                _showOwnEntryMenu = !_showOwnEntryMenu;
                _timeUntilNextOwnEntryMenuRefresh = 0f;
                _lastShownOwnEntryMenuKey = null;
                OnOwnEntryHotkeyHandled(_showOwnEntryMenu ? "Coop Entry Menu: shown" : "Coop Entry Menu: hidden");
            }
        }

        private void TryMirrorAuthoritativeStatusToBridge()
        {
            if (EnableVanillaSelectionUiExperiment)
                return;

            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return;

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot = CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (snapshot == null || !snapshot.HasPeer)
                return;

            BattleSideEnum side = ResolveStatusSide(snapshot);
            string selectionId = ResolveStatusSelectionId(snapshot);
            string mirrorKey = side + "|" + (selectionId ?? string.Empty);
            if (string.Equals(_lastMirroredAuthoritativeStatusKey, mirrorKey, StringComparison.Ordinal))
                return;

            bool wroteSide = side != BattleSideEnum.None &&
                             CoopBattleSelectionBridgeFile.WriteSelectSideRequest(side.ToString(), "MP client authoritative status");
            bool wroteTroop = !string.IsNullOrWhiteSpace(selectionId) &&
                              CoopBattleSelectionBridgeFile.WriteSelectTroopRequest(selectionId, "MP client authoritative status");
            if (!wroteSide && !wroteTroop)
                return;

            _lastMirroredAuthoritativeStatusKey = mirrorKey;
            if (wroteSide)
            {
                _legacyOverlayAutoRequestCooldownRemaining = LegacyOverlayAutoRequestCooldownSeconds;
                _lastAutoRequestedTeamChangeKey = null;
            }

            ModLogger.Info(
                "CoopMissionClientLogic: mirrored authoritative entry status into coop bridge. " +
                "Side=" + side +
                " SelectionId=" + (selectionId ?? "null"));
        }

        private void TryRequestVanillaSpawnFromAuthoritativeStatus(Mission mission)
        {
            if (EnableVanillaSelectionUiExperiment)
                return;

            if (mission == null || !GameNetwork.IsClient || !GameNetwork.IsSessionActive || HasClientControlledAgent())
                return;

            NetworkCommunicator myPeer = GameNetwork.MyPeer;
            MissionPeer missionPeer = myPeer?.GetComponent<MissionPeer>();
            if (missionPeer == null || missionPeer.Team == null || ReferenceEquals(missionPeer.Team, mission.SpectatorTeam))
                return;

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot = CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (snapshot == null || !snapshot.HasPeer || snapshot.HasAgent)
                return;

            string troopId = ResolveStatusTroopId(snapshot);
            if (string.IsNullOrWhiteSpace(snapshot.SpawnRequestSide) || string.IsNullOrWhiteSpace(troopId))
                return;

            string lifecycle = snapshot.LifecycleState ?? string.Empty;
            bool shouldRequestSpawn =
                string.Equals(lifecycle, "AwaitingSelection", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycle, "Respawnable", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycle, "SpawnQueued", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycle, "Waiting", StringComparison.OrdinalIgnoreCase);
            if (!shouldRequestSpawn)
                return;

            string spawnKey =
                snapshot.PeerIndex + "|" +
                (snapshot.SpawnRequestSide ?? string.Empty) + "|" +
                troopId + "|" +
                (snapshot.SpawnRequestEntryId ?? string.Empty) + "|" +
                lifecycle;
            if (string.Equals(_lastRequestedVanillaSpawnKey, spawnKey, StringComparison.Ordinal))
                return;

            try
            {
                GameNetwork.BeginModuleEventAsClient();
                GameNetwork.WriteMessage(new NetworkMessages.FromClient.RequestToSpawnAsBot());
                GameNetwork.EndModuleEventAsClient();
                _lastRequestedVanillaSpawnKey = spawnKey;
                ModLogger.Info(
                    "CoopMissionClientLogic: requested vanilla spawn from authoritative status. " +
                    "Lifecycle=" + lifecycle +
                    " TeamIndex=" + missionPeer.Team.TeamIndex +
                    " Side=" + missionPeer.Team.Side +
                    " TroopId=" + troopId +
                    " EntryId=" + (snapshot.SpawnRequestEntryId ?? "null"));
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: vanilla spawn request failed: " + ex.Message);
            }
        }

        private void TryMirrorLegacyOverlaySelectionToBridge(Mission mission)
        {
            if (ExperimentalFeatures.EnableCustomCoopSelectionOverlay)
                return;

            if (mission == null || !GameNetwork.IsClient || !GameNetwork.IsSessionActive || Agent.Main != null)
                return;

            NetworkCommunicator myPeer = GameNetwork.MyPeer;
            if (myPeer == null || !myPeer.IsConnectionActive || !myPeer.IsSynchronized)
                return;

            MissionPeer missionPeer = myPeer.GetComponent<MissionPeer>();
            if (missionPeer == null || missionPeer.Team == null)
                return;

            BattleSideEnum observedSide = !ReferenceEquals(missionPeer.Team, mission.SpectatorTeam)
                ? missionPeer.Team.Side
                : BattleSideEnum.None;
            if (observedSide != BattleSideEnum.None && observedSide != _lastObservedLegacyOverlaySide)
            {
                if (CoopBattleSelectionBridgeFile.WriteSelectSideRequest(observedSide.ToString(), "MP client legacy overlay team change"))
                {
                    _legacyOverlayAutoRequestCooldownRemaining = LegacyOverlayAutoRequestCooldownSeconds;
                    _lastAutoRequestedTeamChangeKey = null;
                    ModLogger.Info(
                        "CoopMissionClientLogic: mirrored legacy overlay side into coop bridge. " +
                        "Side=" + observedSide +
                        " Cooldown=" + LegacyOverlayAutoRequestCooldownSeconds.ToString("0.##"));
                }

                _lastObservedLegacyOverlaySide = observedSide;
            }

            if (observedSide == BattleSideEnum.None)
                return;

            int selectedTroopIndex = missionPeer.SelectedTroopIndex;
            string selectedTroopId = ResolveLegacyOverlaySelectedTroopId(missionPeer, observedSide, selectedTroopIndex);
            string selectionKey = observedSide + "|" + selectedTroopIndex + "|" + (selectedTroopId ?? "null");
            if (string.Equals(_lastMirroredLegacyOverlaySelectionKey, selectionKey, StringComparison.Ordinal))
                return;

            bool wroteSide = CoopBattleSelectionBridgeFile.WriteSelectSideRequest(observedSide.ToString(), "MP client legacy overlay");
            bool wroteTroop = !string.IsNullOrWhiteSpace(selectedTroopId) &&
                              CoopBattleSelectionBridgeFile.WriteSelectTroopRequest(selectedTroopId, "MP client legacy overlay");
            if (!wroteSide && !wroteTroop)
                return;

            _lastMirroredLegacyOverlaySelectionKey = selectionKey;
            _legacyOverlayAutoRequestCooldownRemaining = LegacyOverlayAutoRequestCooldownSeconds;
            _lastAutoRequestedTeamChangeKey = null;
            ModLogger.Info(
                "CoopMissionClientLogic: mirrored legacy overlay selection into coop bridge. " +
                "Side=" + observedSide +
                " TroopIndex=" + selectedTroopIndex +
                " TroopId=" + (selectedTroopId ?? "null"));
        }

        private static string ResolveLegacyOverlaySelectedTroopId(MissionPeer missionPeer, BattleSideEnum side, int selectedTroopIndex)
        {
            if (missionPeer == null || selectedTroopIndex < 0)
                return null;

            List<MultiplayerClassDivisions.MPHeroClass> cultureClasses = MultiplayerClassDivisions
                .GetMPHeroClasses(missionPeer.Culture)
                ?.Where(heroClass => heroClass?.HeroCharacter != null)
                .ToList();
            if (cultureClasses == null || selectedTroopIndex >= cultureClasses.Count)
                return null;

            string heroClassTroopId = cultureClasses[selectedTroopIndex]?.HeroCharacter?.StringId;
            if (string.IsNullOrWhiteSpace(heroClassTroopId))
                return null;

            string[] allowedTroopIds = GetAllowedTroopIdsForSide(CoopBattleEntryStatusBridgeFile.ReadStatus(), side);
            if (allowedTroopIds.Length == 0)
                return heroClassTroopId;

            string normalizedHeroClassTroopId = NormalizeOverlayTroopMatchId(heroClassTroopId);
            foreach (string allowedTroopId in allowedTroopIds)
            {
                if (string.IsNullOrWhiteSpace(allowedTroopId))
                    continue;

                if (string.Equals(NormalizeOverlayTroopMatchId(allowedTroopId), normalizedHeroClassTroopId, StringComparison.OrdinalIgnoreCase))
                    return allowedTroopId;
            }

            return heroClassTroopId;
        }

        private static string NormalizeOverlayTroopMatchId(string troopId)
        {
            if (string.IsNullOrWhiteSpace(troopId))
                return string.Empty;

            string normalized = troopId.Trim();
            if (normalized.StartsWith("mp_coop_", StringComparison.OrdinalIgnoreCase))
                normalized = "mp_" + normalized.Substring("mp_coop_".Length);

            return normalized;
        }

        private void TryShowOwnEntryMenu(Mission mission, float dt)
        {
            if (EnableVanillaSelectionUiExperiment)
                return;

            if (!_showOwnEntryMenu || mission == null || !GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return;

            _timeUntilNextOwnEntryMenuRefresh -= dt;

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot = CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (snapshot == null)
                return;

            BattleSideEnum side = ResolveStatusSide(snapshot);
            string sideLabel = side == BattleSideEnum.None ? "none" : side.ToString();
            string selectionId = ResolveStatusSelectionId(snapshot);
            string unitLabel = ResolveSelectionDisplayLabel(selectionId);
            string lifecycle = snapshot.LifecycleState ?? string.Empty;
            string lifecycleSource = snapshot.LifecycleSource ?? string.Empty;
            string battlePhase = string.IsNullOrWhiteSpace(snapshot.BattlePhase) ? "unknown" : snapshot.BattlePhase;
            string spawn = snapshot.SpawnStatus ?? string.Empty;
            string[] selectionIds = GetAllowedSelectionIdsForSide(snapshot, side);
            int selectionIndex = Array.FindIndex(selectionIds, candidate => string.Equals(candidate, selectionId, StringComparison.OrdinalIgnoreCase));
            string selectionOrderText = selectionIndex >= 0
                ? "[" + (selectionIndex + 1) + "/" + selectionIds.Length + "]"
                : (selectionIds.Length > 0 ? "[0/" + selectionIds.Length + "]" : "[0/0]");
            string selectionOptionsText = selectionIds.Length == 0
                ? "none"
                : string.Join(" | ", selectionIds.Select((candidate, index) =>
                    (index + 1) + "=" + ResolveSelectionDisplayLabel(candidate) + (string.Equals(candidate, selectionId, StringComparison.OrdinalIgnoreCase) ? "*" : string.Empty)));

            string menuKey =
                sideLabel + "|" +
                (selectionId ?? string.Empty) + "|" +
                selectionOrderText + "|" +
                battlePhase + "|" +
                lifecycle + "|" +
                spawn + "|" +
                snapshot.CanRespawn + "|" +
                snapshot.CanStartBattle + "|" +
                snapshot.HasAgent + "|" +
                selectionOptionsText;

            if (_timeUntilNextOwnEntryMenuRefresh > 0f &&
                string.Equals(_lastShownOwnEntryMenuKey, menuKey, StringComparison.Ordinal))
            {
                return;
            }

            _lastShownOwnEntryMenuKey = menuKey;
            _timeUntilNextOwnEntryMenuRefresh = EntryMenuRefreshIntervalSeconds;

            string message =
                "Coop Entry Menu\n" +
                "Phase: " + battlePhase + (snapshot.CanStartBattle ? " | host can start" : string.Empty) + "\n" +
                "Side: " + sideLabel + "\n" +
                "Unit: " + unitLabel + " " + selectionOrderText + "\n" +
                "State: " + lifecycle + " | Deaths: " + snapshot.DeathCount + "\n" +
                "Source: " + lifecycleSource + "\n" +
                "Spawn: " + spawn + (snapshot.CanRespawn ? " | respawn ready" : string.Empty) + "\n" +
                "Options: " + selectionOptionsText + "\n" +
                "Keys: Ctrl+1/2 side | Ctrl+Q/E troop | Ctrl+R spawn | Ctrl+T reset | H start | Ctrl+M menu";
#if !COOPSPECTATOR_DEDICATED
            UiFeedback.ShowMessageDeferred(message);
#endif
        }

        private void TryWriteOwnSideSelection(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot, BattleSideEnum side)
        {
            if (snapshot == null || side == BattleSideEnum.None)
                return;

            string sideToken = side.ToString();
            if (!CoopBattleSelectionBridgeFile.WriteSelectSideRequest(sideToken, "MP client hotkey"))
                return;

            string[] selectionIds = GetAllowedSelectionIdsForSide(snapshot, side);
            string currentSelectionId = ResolveStatusSelectionId(snapshot);
            if (selectionIds.Length > 0 && !selectionIds.Contains(currentSelectionId, StringComparer.OrdinalIgnoreCase))
                CoopBattleSelectionBridgeFile.WriteSelectTroopRequest(selectionIds[0], "MP client hotkey side default troop");

            string currentSelectionLabel = selectionIds.Length > 0
                ? ResolveSelectionDisplayLabel(selectionIds[0]) + " [1/" + selectionIds.Length + "]"
                : "none";
            OnOwnEntryHotkeyHandled("Coop Entry: side -> " + sideToken + " unit=" + currentSelectionLabel);
        }

        private void TryCycleOwnTroopSelection(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot, bool moveNext)
        {
            if (snapshot == null)
                return;

            BattleSideEnum side = ResolveStatusSide(snapshot);
            string[] selectionIds = GetAllowedSelectionIdsForSide(snapshot, side);
            if (selectionIds.Length == 0)
                return;

            string currentSelectionId = ResolveStatusSelectionId(snapshot);
            int currentIndex = Array.FindIndex(selectionIds, candidate => string.Equals(candidate, currentSelectionId, StringComparison.OrdinalIgnoreCase));
            int nextIndex;
            if (currentIndex < 0)
            {
                nextIndex = moveNext ? 0 : selectionIds.Length - 1;
            }
            else
            {
                int delta = moveNext ? 1 : -1;
                nextIndex = (currentIndex + delta + selectionIds.Length) % selectionIds.Length;
            }

            string nextSelectionId = selectionIds[nextIndex];
            if (!CoopBattleSelectionBridgeFile.WriteSelectTroopRequest(nextSelectionId, "MP client hotkey troop cycle"))
                return;

            OnOwnEntryHotkeyHandled("Coop Entry: unit -> " + ResolveSelectionDisplayLabel(nextSelectionId) + " [" + (nextIndex + 1) + "/" + selectionIds.Length + "] side=" + side);
        }

        private void OnOwnEntryHotkeyHandled(string message)
        {
            _timeUntilNextOwnEntryHotkey = EntryHotkeyCooldownSeconds;
            ModLogger.Info("CoopMissionClientLogic: own entry hotkey action: " + message);
#if !COOPSPECTATOR_DEDICATED
            UiFeedback.ShowMessageDeferred(message);
#endif
        }

        private static BattleSideEnum ResolveStatusSide(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot)
        {
            if (snapshot == null)
                return BattleSideEnum.None;

            string lifecycle = snapshot.LifecycleState ?? string.Empty;
            bool preferRequestedSide =
                !snapshot.HasAgent ||
                string.Equals(lifecycle, "NoSide", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycle, "AwaitingSelection", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycle, "Respawnable", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycle, "SpawnQueued", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycle, "Waiting", StringComparison.OrdinalIgnoreCase);

            if (preferRequestedSide)
            {
                BattleSideEnum requestedSide = ParseBattleSide(snapshot.RequestedSide);
                if (requestedSide != BattleSideEnum.None)
                    return requestedSide;
            }

            BattleSideEnum side = ParseBattleSide(snapshot.AssignedSide);
            if (side != BattleSideEnum.None)
                return side;

            side = ParseBattleSide(snapshot.RequestedSide);
            if (side != BattleSideEnum.None)
                return side;

            return ParseBattleSide(snapshot.IntentSide);
        }

        private static BattleSideEnum ParseBattleSide(string rawSide)
        {
            if (string.IsNullOrWhiteSpace(rawSide))
                return BattleSideEnum.None;

            if (string.Equals(rawSide, "Attacker", StringComparison.OrdinalIgnoreCase))
                return BattleSideEnum.Attacker;

            if (string.Equals(rawSide, "Defender", StringComparison.OrdinalIgnoreCase))
                return BattleSideEnum.Defender;

            return BattleSideEnum.None;
        }

        private static string ResolveStatusTroopId(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot)
        {
            if (snapshot == null)
                return string.Empty;

            string selectedEntryId = ResolveStatusEntryId(snapshot);
            if (!string.IsNullOrWhiteSpace(selectedEntryId))
            {
                RosterEntryState selectedEntry = BattleSnapshotRuntimeState.GetEntryState(selectedEntryId);
                if (!string.IsNullOrWhiteSpace(selectedEntry?.CharacterId))
                    return selectedEntry.CharacterId;
            }

            return snapshot.SelectedTroopId ??
                   snapshot.IntentTroopOrEntryId ??
                   snapshot.SelectionRequestTroopId ??
                   string.Empty;
        }

        private static string ResolveStatusEntryId(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot)
        {
            if (snapshot == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(snapshot.SelectedEntryId))
                return snapshot.SelectedEntryId;
            if (!string.IsNullOrWhiteSpace(snapshot.SpawnRequestEntryId))
                return snapshot.SpawnRequestEntryId;
            if (!string.IsNullOrWhiteSpace(snapshot.SelectionRequestEntryId))
                return snapshot.SelectionRequestEntryId;
            if (!string.IsNullOrWhiteSpace(snapshot.IntentTroopOrEntryId) &&
                BattleSnapshotRuntimeState.GetEntryState(snapshot.IntentTroopOrEntryId) != null)
            {
                return snapshot.IntentTroopOrEntryId;
            }

            return string.Empty;
        }

        private static string ResolveStatusSelectionId(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot)
        {
            string entryId = ResolveStatusEntryId(snapshot);
            return !string.IsNullOrWhiteSpace(entryId)
                ? entryId
                : ResolveStatusTroopId(snapshot);
        }

        private static string[] GetAllowedTroopIdsForSide(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot, BattleSideEnum side)
        {
            if (snapshot == null)
                return Array.Empty<string>();

            string rawTroopIds =
                side == BattleSideEnum.Attacker ? snapshot.AttackerAllowedTroopIds :
                side == BattleSideEnum.Defender ? snapshot.DefenderAllowedTroopIds :
                snapshot.AllowedTroopIds;
            return CoopBattleEntryStatusBridgeFile.DeserializeIdList(rawTroopIds)
                .Where(troopId => !string.IsNullOrWhiteSpace(troopId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string[] GetAllowedEntryIdsForSide(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot, BattleSideEnum side)
        {
            if (snapshot == null)
                return Array.Empty<string>();

            string selectableEntrySource = GetSelectableEntrySourceForSide(snapshot, side);
            string rawSelectableEntryIds =
                side == BattleSideEnum.Attacker ? snapshot.AttackerSelectableEntryIds :
                side == BattleSideEnum.Defender ? snapshot.DefenderSelectableEntryIds :
                snapshot.SelectableEntryIds;
            string[] selectableEntryIds = CoopBattleEntryStatusBridgeFile.DeserializeIdList(rawSelectableEntryIds)
                .Where(entryId => !string.IsNullOrWhiteSpace(entryId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (selectableEntryIds.Length > 0 || !string.IsNullOrWhiteSpace(selectableEntrySource))
                return selectableEntryIds;

            string rawAllowedEntryIds =
                side == BattleSideEnum.Attacker ? snapshot.AttackerAllowedEntryIds :
                side == BattleSideEnum.Defender ? snapshot.DefenderAllowedEntryIds :
                snapshot.AllowedEntryIds;
            return CoopBattleEntryStatusBridgeFile.DeserializeIdList(rawAllowedEntryIds)
                .Where(entryId => !string.IsNullOrWhiteSpace(entryId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string[] GetAllowedSelectionIdsForSide(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot, BattleSideEnum side)
        {
            string[] entryIds = GetAllowedEntryIdsForSide(snapshot, side);
            if (entryIds.Length > 0 || HasExplicitSelectableEntrySource(snapshot, side))
                return entryIds;

            return GetAllowedTroopIdsForSide(snapshot, side);
        }

        private static bool HasExplicitSelectableEntrySource(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot, BattleSideEnum side)
        {
            return !string.IsNullOrWhiteSpace(GetSelectableEntrySourceForSide(snapshot, side));
        }

        private static string GetSelectableEntrySourceForSide(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot, BattleSideEnum side)
        {
            if (snapshot == null)
                return string.Empty;

            return
                side == BattleSideEnum.Attacker ? snapshot.AttackerSelectableEntrySource :
                side == BattleSideEnum.Defender ? snapshot.DefenderSelectableEntrySource :
                snapshot.SelectableEntrySource;
        }

        private static string ResolveSelectionDisplayLabel(string selectionId)
        {
            if (string.IsNullOrWhiteSpace(selectionId))
                return "none";

            RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(selectionId);
            if (entryState != null)
            {
                if (!string.IsNullOrWhiteSpace(entryState.TroopName))
                    return entryState.TroopName;
                if (!string.IsNullOrWhiteSpace(entryState.OriginalCharacterId))
                    return entryState.OriginalCharacterId;
                if (!string.IsNullOrWhiteSpace(entryState.CharacterId))
                    return entryState.CharacterId;
            }

            return selectionId;
        }

        protected override void OnEndMission()
        {
            string sceneName = Mission?.SceneName ?? "null";
            ResetSelectionUiBridgeStateForNewMission("CoopMissionClientLogic.OnEndMission");
            CoopMissionSpawnLogic.ResetClientMissionRuntimeState(
                "CoopMissionClientLogic.OnEndMission:" + sceneName);
            CoopSpectator.Patches.BattleMapSpawnHandoffPatch.ResetRuntimeState(
                "CoopMissionClientLogic.OnEndMission:" + sceneName);
            if (EnableVisualSpawnAutoConfirmExperiment && !EnableVanillaSelectionUiExperiment)
                UnhookVisualSpawnAutoConfirm();
            base.OnEndMission();
        }

        private static void ResetSelectionUiBridgeStateForNewMission(string source)
        {
            CoopBattleSelectionBridgeFile.ClearAll(source);
            CoopBattleSpawnBridgeFile.ClearPendingRequests(source);
        }

        private static void LogMissionEntered(Mission mission)
        {
            ModLogger.Info("CoopMissionClientLogic: mission entered.");
            try
            {
                string mode = mission.Mode.ToString();
                ModLogger.Info("CoopMissionClientLogic: current mission mode = " + mode);
            }
            catch (Exception ex) { ModLogger.Info("CoopMissionClientLogic: mission mode log failed: " + ex.Message); }
        }

        private static void LogCurrentState(Mission mission)
        {
            try
            {
                bool hasAgent = HasClientControlledAgent();
                bool hasDetachedAgent = Agent.Main != null && Agent.Main.MissionPeer == null;
                bool isSpectator = !hasAgent;
                ModLogger.Info(
                    "CoopMissionClientLogic: player has agent = " + hasAgent +
                    " player is spectator = " + isSpectator +
                    " detached main agent = " + hasDetachedAgent);

                if (GameNetwork.IsSessionActive)
                {
                    var myPeer = GameNetwork.MyPeer;
                    ModLogger.Info("CoopMissionClientLogic: peer connected/synchronized = " + (myPeer != null));
                    if (hasAgent && Agent.Main?.Team != null)
                    {
                        int teamIndex = Agent.Main.Team?.TeamIndex ?? -1;
                        var side = Agent.Main.Team?.Side ?? BattleSideEnum.None;
                        ModLogger.Info("CoopMissionClientLogic: team index = " + teamIndex + " side = " + side);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: LogCurrentState failed: " + ex.Message);
            }
        }

        private static bool HasClientControlledAgent()
        {
            Agent mainAgent = Agent.Main;
            return mainAgent != null && mainAgent.MissionPeer != null;
        }

        private static void TryReleaseStaleClientMainAgent(Mission mission)
        {
            if (mission == null || !GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return;

            Agent mainAgent = Agent.Main;
            if (mainAgent == null || mainAgent.MissionPeer != null)
                return;

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot = CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (snapshot == null || snapshot.HasAgent)
                return;

            string lifecycle = snapshot.LifecycleState ?? string.Empty;
            bool shouldRelease =
                snapshot.CanRespawn ||
                string.Equals(lifecycle, "Respawnable", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycle, "SpawnQueued", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycle, "Waiting", StringComparison.OrdinalIgnoreCase);
            if (!shouldRelease)
                return;

            try
            {
                if (ReferenceEquals(mission.MainAgent, mainAgent))
                    mission.MainAgent = null;
                if (ReferenceEquals(mission.MainAgentServer, mainAgent))
                    mission.MainAgentServer = null;

                ModLogger.Info(
                    "CoopMissionClientLogic: released stale detached main agent for respawn flow. " +
                    "AgentIndex=" + mainAgent.Index +
                    " Lifecycle=" + lifecycle +
                    " CanRespawn=" + snapshot.CanRespawn);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: failed to release stale detached main agent: " + ex.Message);
            }
        }

        private void TryRestoreClientMainAgentFromMissionPeer(Mission mission)
        {
            if (mission == null || !GameNetwork.IsClient || !GameNetwork.IsSessionActive || Agent.Main != null)
                return;

            NetworkCommunicator myPeer = GameNetwork.MyPeer;
            MissionPeer missionPeer = myPeer?.GetComponent<MissionPeer>();
            Agent controlledAgent = missionPeer?.ControlledAgent;
            if (controlledAgent == null || !controlledAgent.IsActive() || !ReferenceEquals(controlledAgent.MissionPeer, missionPeer))
                return;

            try
            {
                mission.MainAgent = controlledAgent;
                if (ReferenceEquals(mission.MainAgentServer, controlledAgent))
                    mission.MainAgentServer = controlledAgent;

                ModLogger.Info(
                    "CoopMissionClientLogic: restored main agent from mission peer controlled agent. " +
                    "AgentIndex=" + controlledAgent.Index +
                    " Team=" + (controlledAgent.Team?.TeamIndex ?? -1) +
                    " Side=" + (controlledAgent.Team?.Side ?? BattleSideEnum.None));

                TryRepairMainAgentControllerState(mission, controlledAgent);
                LogControlFinalizeDiagnosticsForCurrentAgent(mission, controlledAgent);
                _pendingPostControlDiagnosticAgentIndex = controlledAgent.Index;
                _pendingPostControlDiagnosticDelay = 0.9f;
                _lastControlledAgent = controlledAgent;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: failed to restore main agent from mission peer: " + ex.Message);
            }
        }

        private void TryTriggerClientPreferredTroopRequest(Mission mission)
        {
            if (!EnableClientPreferredTroopRequestExperiment)
                return;

            if (mission == null || !GameNetwork.IsClient || HasClientControlledAgent() || !GameNetwork.IsSessionActive)
                return;

            NetworkCommunicator myPeer = GameNetwork.MyPeer;
            if (myPeer == null || !myPeer.IsConnectionActive || !myPeer.IsSynchronized)
                return;

            MissionPeer missionPeer = myPeer.GetComponent<MissionPeer>();
            if (missionPeer == null || missionPeer.Team == null || ReferenceEquals(missionPeer.Team, mission.SpectatorTeam))
                return;

            int preferredTroopIndex = missionPeer.SelectedTroopIndex;
            if (preferredTroopIndex < 0)
                return;

            string requestKey = (missionPeer.Team.TeamIndex) + "|" + preferredTroopIndex;
            if (string.Equals(_lastRequestedPreferredTroopKey, requestKey, StringComparison.Ordinal))
                return;

            foreach (MissionBehavior behavior in mission.MissionBehaviors)
            {
                if (behavior == null)
                    continue;

                MethodInfo[] methods;
                try
                {
                    methods = behavior.GetType()
                        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Where(method => string.Equals(method.Name, "RequestChangePreferredTroopType", StringComparison.Ordinal))
                        .ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (MethodInfo method in methods)
                {
                    if (!TryInvokePreferredTroopRequestMethod(behavior, method, myPeer, missionPeer, preferredTroopIndex))
                        continue;

                    _lastRequestedPreferredTroopKey = requestKey;
                    ModLogger.Info(
                        "CoopMissionClientLogic: invoked native preferred troop request. " +
                        "Behavior=" + behavior.GetType().FullName +
                        " Method=" + method +
                        " Team=" + missionPeer.Team.TeamIndex +
                        " TroopIndex=" + preferredTroopIndex);
                    return;
                }
            }
        }

        private void TryResetSpawnPreviewStateForPreferredTroopChange(Mission mission)
        {
            if (mission == null || !GameNetwork.IsClient || HasClientControlledAgent() || !GameNetwork.IsSessionActive)
                return;

            NetworkCommunicator myPeer = GameNetwork.MyPeer;
            if (myPeer == null || !myPeer.IsConnectionActive || !myPeer.IsSynchronized)
                return;

            MissionPeer missionPeer = myPeer.GetComponent<MissionPeer>();
            if (missionPeer == null || missionPeer.Team == null || ReferenceEquals(missionPeer.Team, mission.SpectatorTeam))
                return;

            int selectedTroopIndex = missionPeer.SelectedTroopIndex;
            if (selectedTroopIndex < 0)
                return;

            string selectionKey = missionPeer.Team.TeamIndex + "|" + selectedTroopIndex;
            if (string.Equals(_lastObservedSpectatorTroopSelectionKey, selectionKey, StringComparison.Ordinal))
                return;

            _lastObservedSpectatorTroopSelectionKey = selectionKey;
            _lastRequestedPreferredTroopKey = null;
            _visualSpawnAutoConfirmTriggered = false;

            ModLogger.Info(
                "CoopMissionClientLogic: spectator troop selection changed. " +
                "Team=" + missionPeer.Team.TeamIndex +
                " TroopIndex=" + selectedTroopIndex +
                " AutoConfirmReset=True");
        }

        private static bool TryInvokePreferredTroopRequestMethod(
            MissionBehavior behavior,
            MethodInfo method,
            NetworkCommunicator myPeer,
            MissionPeer missionPeer,
            int preferredTroopIndex)
        {
            try
            {
                ParameterInfo[] parameters = method.GetParameters();
                object[] arguments = new object[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    Type parameterType = parameters[i].ParameterType;
                    if (parameterType == typeof(int))
                        arguments[i] = preferredTroopIndex;
                    else if (parameterType.IsAssignableFrom(typeof(NetworkCommunicator)))
                        arguments[i] = myPeer;
                    else if (parameterType.IsAssignableFrom(typeof(MissionPeer)))
                        arguments[i] = missionPeer;
                    else if (parameterType == typeof(bool))
                        arguments[i] = true;
                    else
                        return false;
                }

                method.Invoke(behavior, arguments);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: preferred troop request invoke failed for " + behavior.GetType().FullName + "." + method.Name + ": " + ex.Message);
                return false;
            }
        }

        private void TryHookVisualSpawnAutoConfirm(Mission mission)
        {
            if (!EnableVisualSpawnAutoConfirmExperiment || EnableVanillaSelectionUiExperiment)
                return;

            if (_visualSpawnAutoConfirmHooked || mission == null || !GameNetwork.IsClient)
                return;

            try
            {
                MissionBehavior visualSpawnBehavior = mission.MissionBehaviors
                    .FirstOrDefault(behavior =>
                        behavior != null &&
                        behavior.GetType().Name.IndexOf("MultiplayerMissionAgentVisualSpawnComponent", StringComparison.OrdinalIgnoreCase) >= 0);
                if (visualSpawnBehavior == null)
                    return;

                EventInfo eventInfo = visualSpawnBehavior.GetType().GetEvent(
                    "OnMyAgentSpawnedFromVisual",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                    visualSpawnBehavior.GetType().GetEvent(
                        "OnMyAgentVisualSpawned",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (eventInfo == null)
                    return;

                MethodInfo handler = GetType().GetMethod(
                    nameof(OnMyAgentVisualSpawned),
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (handler == null)
                    return;

                Delegate callback = Delegate.CreateDelegate(eventInfo.EventHandlerType, this, handler, false);
                if (callback == null)
                    return;

                eventInfo.AddEventHandler(visualSpawnBehavior, callback);
                _visualSpawnComponent = visualSpawnBehavior;
                _onMyAgentVisualSpawnedDelegate = callback;
                _visualSpawnLifecycleEventName = eventInfo.Name;
                _visualSpawnAutoConfirmHooked = true;
                ModLogger.Info(
                    "CoopMissionClientLogic: hooked " + _visualSpawnLifecycleEventName + " for coop visual finalize.");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: visual spawn auto-confirm hook failed: " + ex.Message);
            }
        }

        private void UnhookVisualSpawnAutoConfirm()
        {
            if (!EnableVisualSpawnAutoConfirmExperiment)
                return;

            if (!_visualSpawnAutoConfirmHooked || _visualSpawnComponent == null || _onMyAgentVisualSpawnedDelegate == null)
                return;

            try
            {
                EventInfo eventInfo = !string.IsNullOrWhiteSpace(_visualSpawnLifecycleEventName)
                    ? _visualSpawnComponent.GetType().GetEvent(
                        _visualSpawnLifecycleEventName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    : null;
                eventInfo?.RemoveEventHandler(_visualSpawnComponent, _onMyAgentVisualSpawnedDelegate);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: visual spawn auto-confirm unhook failed: " + ex.Message);
            }
            finally
            {
                _visualSpawnAutoConfirmHooked = false;
                _visualSpawnComponent = null;
                _onMyAgentVisualSpawnedDelegate = null;
                _visualSpawnLifecycleEventName = null;
            }
        }

        private void OnMyAgentVisualSpawned()
        {
            if (!EnableVisualSpawnAutoConfirmExperiment)
                return;

            if (_visualSpawnAutoConfirmTriggered || HasClientControlledAgent())
                return;

            Mission mission = Mission;
            if (mission == null)
                return;

            _visualSpawnAutoConfirmTriggered = true;
            LogMissionBehaviorCatalogOnce(mission);

            if (TryRemoveMyPendingAgentVisuals(mission))
                return;

            TryInvokeAutoSpawnConfirm(mission);
        }

        private bool TryRemoveMyPendingAgentVisuals(Mission mission)
        {
            if (!EnableVisualSpawnAutoConfirmExperiment || mission == null || _visualSpawnComponent == null)
                return false;

            NetworkCommunicator myPeer = GameNetwork.MyPeer;
            MissionPeer missionPeer = myPeer?.GetComponent<MissionPeer>();
            if (missionPeer == null)
                return false;

            try
            {
                Type componentType = _visualSpawnComponent.GetType();
                MethodInfo removeMethod = componentType.GetMethod(
                    "RemoveAgentVisuals",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(MissionPeer), typeof(bool) },
                    null);
                if (removeMethod != null)
                {
                    removeMethod.Invoke(_visualSpawnComponent, new object[] { missionPeer, true });
                    ModLogger.Info(
                        "CoopMissionClientLogic: removed pending visuals after " +
                        (_visualSpawnLifecycleEventName ?? "visual-spawn event") +
                        ". Peer=" + (myPeer?.UserName ?? myPeer?.Index.ToString() ?? "null"));
                    return true;
                }

                removeMethod = componentType.GetMethod(
                    "RemoveAgentVisuals",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(MissionPeer) },
                    null);
                if (removeMethod != null)
                {
                    removeMethod.Invoke(_visualSpawnComponent, new object[] { missionPeer });
                    ModLogger.Info(
                        "CoopMissionClientLogic: removed pending visuals after " +
                        (_visualSpawnLifecycleEventName ?? "visual-spawn event") +
                        ". Peer=" + (myPeer?.UserName ?? myPeer?.Index.ToString() ?? "null"));
                    return true;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: remove pending visuals after visual spawn failed: " + ex.Message);
            }

            return false;
        }

        private void TryInvokeAutoSpawnConfirm(Mission mission)
        {
            if (!EnableVisualSpawnAutoConfirmExperiment)
                return;

            foreach (MissionBehavior behavior in mission.MissionBehaviors)
            {
                if (behavior == null)
                    continue;

                string typeName = behavior.GetType().Name;
                if (typeName.IndexOf("MissionLobbyEquipmentNetworkComponent", StringComparison.OrdinalIgnoreCase) < 0 &&
                    typeName.IndexOf("MultiplayerTeamSelectComponent", StringComparison.OrdinalIgnoreCase) < 0 &&
                    typeName.IndexOf("MultiplayerMissionAgentVisualSpawnComponent", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                LogAutoConfirmBehaviorDiagnosticsOnce(behavior);

                MethodInfo[] candidateMethods;
                try
                {
                    candidateMethods = behavior.GetType()
                        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Where(method =>
                            method.ReturnType == typeof(void) &&
                            method.GetParameters().Length == 0 &&
                            method.Name.IndexOf("Spawn", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            (method.Name.IndexOf("Request", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             method.Name.IndexOf("Confirm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             method.Name.IndexOf("Finalize", StringComparison.OrdinalIgnoreCase) >= 0))
                        .OrderBy(method => method.Name)
                        .ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (MethodInfo method in candidateMethods)
                {
                    try
                    {
                        method.Invoke(behavior, Array.Empty<object>());
                        ModLogger.Info(
                            "CoopMissionClientLogic: auto-confirm spawn invoked. " +
                            "Behavior=" + behavior.GetType().FullName +
                            " Method=" + method.Name);
                        return;
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Info(
                            "CoopMissionClientLogic: auto-confirm candidate failed. " +
                            "Behavior=" + behavior.GetType().FullName +
                            " Method=" + method.Name +
                            " Error=" + ex.Message);
                    }
                }
            }
        }

        private void LogMissionBehaviorCatalogOnce(Mission mission)
        {
            if (_hasLoggedMissionBehaviorCatalog || mission == null)
                return;

            _hasLoggedMissionBehaviorCatalog = true;

            try
            {
                MissionBehavior[] behaviors = mission.MissionBehaviors?.Where(behavior => behavior != null).ToArray() ?? Array.Empty<MissionBehavior>();
                ModLogger.Info("CoopMissionClientLogic: mission behavior catalog count = " + behaviors.Length);
                foreach (MissionBehavior behavior in behaviors)
                    ModLogger.Info("CoopMissionClientLogic: mission behavior => " + behavior.GetType().FullName);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: mission behavior catalog logging failed: " + ex.Message);
            }
        }

        private void LogAutoConfirmBehaviorDiagnosticsOnce(MissionBehavior behavior)
        {
            if (behavior == null)
                return;

            string behaviorKey = behavior.GetType().FullName ?? behavior.GetType().Name;
            if (!_loggedAutoConfirmBehaviorDiagnostics.Add(behaviorKey))
                return;

            try
            {
                MethodInfo[] interestingMethods = behavior.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(method =>
                        method.Name.IndexOf("Spawn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        method.Name.IndexOf("Visual", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        method.Name.IndexOf("Agent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        method.Name.IndexOf("Ready", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        method.Name.IndexOf("Invul", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        method.Name.IndexOf("Confirm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        method.Name.IndexOf("Request", StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(method => method.Name)
                    .ToArray();

                ModLogger.Info(
                    "CoopMissionClientLogic: auto-confirm diagnostics for behavior " +
                    behavior.GetType().FullName +
                    " MethodCount=" + interestingMethods.Length);

                foreach (MethodInfo method in interestingMethods)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    string parameterList = string.Join(", ", parameters.Select(parameter => parameter.ParameterType.Name + " " + parameter.Name));
                    ModLogger.Info(
                        "CoopMissionClientLogic: auto-confirm method candidate => " +
                        method.ReturnType.Name + " " +
                        method.Name + "(" + parameterList + ")");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: auto-confirm diagnostics failed: " + ex.Message);
            }
        }

        private void LogClassLoadoutDiagnosticsOnce(Mission mission)
        {
            if (_hasLoggedClassLoadoutDiagnostics || mission == null)
                return;

            try
            {
                object classLoadout = mission.MissionBehaviors
                    .FirstOrDefault(behavior =>
                        behavior != null &&
                        behavior.GetType().FullName != null &&
                        behavior.GetType().FullName.IndexOf("MissionGauntletClassLoadout", StringComparison.OrdinalIgnoreCase) >= 0);
                if (classLoadout == null)
                    return;

                _hasLoggedClassLoadoutDiagnostics = true;

                Type loadoutType = classLoadout.GetType();
                ModLogger.Info("CoopMissionClientLogic: class loadout diagnostics type = " + loadoutType.FullName);

                foreach (FieldInfo field in loadoutType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    object value = null;
                    string valueDescription;
                    try
                    {
                        value = field.GetValue(classLoadout);
                        valueDescription = DescribeDiagnosticValue(value);
                    }
                    catch (Exception ex)
                    {
                        valueDescription = "unavailable (" + ex.Message + ")";
                    }

                    ModLogger.Info(
                        "CoopMissionClientLogic: class loadout field => " +
                        field.FieldType.FullName + " " +
                        field.Name + " = " + valueDescription);

                    LogNestedDiagnosticMembers(field.Name, value);
                }

                foreach (PropertyInfo property in loadoutType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (property.GetIndexParameters().Length != 0)
                        continue;

                    object value = null;
                    string valueDescription;
                    try
                    {
                        if (!property.CanRead)
                            continue;

                        value = property.GetValue(classLoadout, null);
                        valueDescription = DescribeDiagnosticValue(value);
                    }
                    catch (Exception ex)
                    {
                        valueDescription = "unavailable (" + ex.Message + ")";
                    }

                    ModLogger.Info(
                        "CoopMissionClientLogic: class loadout property => " +
                        property.PropertyType.FullName + " " +
                        property.Name + " = " + valueDescription);

                    LogNestedDiagnosticMembers(property.Name, value);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: class loadout diagnostics failed: " + ex.Message);
            }
        }

        private static void LogNestedDiagnosticMembers(string ownerName, object value)
        {
            if (value == null)
                return;

            Type valueType = value.GetType();
            string typeName = valueType.FullName ?? valueType.Name;
            bool interestingType =
                typeName.IndexOf("VM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("ViewModel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("List", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Binding", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Class", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Troop", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!interestingType)
                return;

            foreach (FieldInfo nestedField in valueType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!IsInterestingDiagnosticMember(nestedField.Name, nestedField.FieldType))
                    continue;

                string valueDescription;
                try
                {
                    valueDescription = DescribeDiagnosticValue(nestedField.GetValue(value));
                }
                catch (Exception ex)
                {
                    valueDescription = "unavailable (" + ex.Message + ")";
                }

                ModLogger.Info(
                    "CoopMissionClientLogic: class loadout nested field => " +
                    ownerName + "." + nestedField.Name +
                    " : " + nestedField.FieldType.FullName +
                    " = " + valueDescription);
            }

            foreach (PropertyInfo nestedProperty in valueType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (nestedProperty.GetIndexParameters().Length != 0 ||
                    !nestedProperty.CanRead ||
                    !IsInterestingDiagnosticMember(nestedProperty.Name, nestedProperty.PropertyType))
                {
                    continue;
                }

                string valueDescription;
                try
                {
                    valueDescription = DescribeDiagnosticValue(nestedProperty.GetValue(value, null));
                }
                catch (Exception ex)
                {
                    valueDescription = "unavailable (" + ex.Message + ")";
                }

                ModLogger.Info(
                    "CoopMissionClientLogic: class loadout nested property => " +
                    ownerName + "." + nestedProperty.Name +
                    " : " + nestedProperty.PropertyType.FullName +
                    " = " + valueDescription);
            }

            LogDiagnosticCollectionItems(ownerName, value);
        }

        private static void LogDiagnosticCollectionItems(string ownerName, object value)
        {
            if (value == null || value is string || !(value is IEnumerable enumerable))
                return;

            int itemIndex = 0;
            foreach (object item in enumerable)
            {
                if (itemIndex >= 4)
                    break;

                if (item == null)
                {
                    ModLogger.Info("CoopMissionClientLogic: diagnostic collection item => " + ownerName + "[" + itemIndex + "] = null");
                    itemIndex++;
                    continue;
                }

                Type itemType = item.GetType();
                ModLogger.Info(
                    "CoopMissionClientLogic: diagnostic collection item => " +
                    ownerName + "[" + itemIndex + "] = " +
                    DescribeDiagnosticValue(item));

                foreach (FieldInfo nestedField in itemType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!IsInterestingDiagnosticMember(nestedField.Name, nestedField.FieldType))
                        continue;

                    string valueDescription;
                    try
                    {
                        valueDescription = DescribeDiagnosticValue(nestedField.GetValue(item));
                    }
                    catch (Exception ex)
                    {
                        valueDescription = "unavailable (" + ex.Message + ")";
                    }

                    ModLogger.Info(
                        "CoopMissionClientLogic: diagnostic item field => " +
                        ownerName + "[" + itemIndex + "]." + nestedField.Name +
                        " : " + nestedField.FieldType.FullName +
                        " = " + valueDescription);
                }

                foreach (PropertyInfo nestedProperty in itemType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (nestedProperty.GetIndexParameters().Length != 0 ||
                        !nestedProperty.CanRead ||
                        !IsInterestingDiagnosticMember(nestedProperty.Name, nestedProperty.PropertyType))
                    {
                        continue;
                    }

                    string valueDescription;
                    try
                    {
                        valueDescription = DescribeDiagnosticValue(nestedProperty.GetValue(item, null));
                    }
                    catch (Exception ex)
                    {
                        valueDescription = "unavailable (" + ex.Message + ")";
                    }

                    ModLogger.Info(
                        "CoopMissionClientLogic: diagnostic item property => " +
                        ownerName + "[" + itemIndex + "]." + nestedProperty.Name +
                        " : " + nestedProperty.PropertyType.FullName +
                        " = " + valueDescription);
                }

                itemIndex++;
            }
        }

        private static bool IsInterestingDiagnosticMember(string memberName, Type memberType)
        {
            string typeName = memberType?.FullName ?? memberType?.Name ?? string.Empty;
            return memberName.IndexOf("class", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("team", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("score", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("culture", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("banner", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("color", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("troop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("item", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("list", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("data", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Team", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Scoreboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Culture", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("VM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("ViewModel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("List", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Binding", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string DescribeDiagnosticValue(object value)
        {
            if (value == null)
                return "null";

            if (value is string text)
                return "\"" + text + "\"";

            if (value is System.Collections.ICollection collection)
                return value.GetType().FullName + " Count=" + collection.Count;

            return value.GetType().FullName + " :: " + value;
        }

        private void LogActiveClassLoadoutDiagnosticsOnce(Mission mission)
        {
            if (_hasLoggedActiveClassLoadoutDiagnostics || mission == null)
                return;

            try
            {
                object classLoadout = mission.MissionBehaviors
                    .FirstOrDefault(behavior =>
                        behavior != null &&
                        behavior.GetType().FullName != null &&
                        behavior.GetType().FullName.IndexOf("MissionGauntletClassLoadout", StringComparison.OrdinalIgnoreCase) >= 0);
                if (classLoadout == null)
                    return;

                Type loadoutType = classLoadout.GetType();
                FieldInfo dataSourceField = loadoutType.GetField("_dataSource", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo tryToInitializeField = loadoutType.GetField("_tryToInitialize", BindingFlags.Instance | BindingFlags.NonPublic);
                PropertyInfo isActiveProperty = loadoutType.GetProperty("IsActive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (dataSourceField == null)
                    return;

                object dataSource = dataSourceField.GetValue(classLoadout);
                bool isActive = false;
                bool tryToInitialize = false;
                try
                {
                    if (isActiveProperty != null)
                        isActive = (bool)isActiveProperty.GetValue(classLoadout, null);
                }
                catch
                {
                }

                try
                {
                    if (tryToInitializeField != null)
                        tryToInitialize = (bool)tryToInitializeField.GetValue(classLoadout);
                }
                catch
                {
                }

                if (dataSource == null && !isActive && !tryToInitialize)
                    return;

                if (dataSource == null)
                {
                    if (!_hasLoggedClassLoadoutPendingInitialization)
                    {
                        _hasLoggedClassLoadoutPendingInitialization = true;
                        ModLogger.Info(
                            "CoopMissionClientLogic: class loadout waiting for datasource. " +
                            "IsActive=" + isActive +
                            " TryToInitialize=" + tryToInitialize);
                    }
                    return;
                }

                _hasLoggedActiveClassLoadoutDiagnostics = true;
                ModLogger.Info(
                    "CoopMissionClientLogic: active class loadout state. " +
                    "IsActive=" + isActive +
                    " TryToInitialize=" + tryToInitialize +
                    " DataSource=" + DescribeDiagnosticValue(dataSource));
                Type dataSourceType = dataSource.GetType();
                ModLogger.Info("CoopMissionClientLogic: active class loadout datasource type = " + dataSourceType.FullName);

                foreach (FieldInfo field in dataSourceType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!IsInterestingDiagnosticMember(field.Name, field.FieldType))
                        continue;

                    string valueDescription;
                    try
                    {
                        valueDescription = DescribeDiagnosticValue(field.GetValue(dataSource));
                    }
                    catch (Exception ex)
                    {
                        valueDescription = "unavailable (" + ex.Message + ")";
                    }

                    ModLogger.Info(
                        "CoopMissionClientLogic: active datasource field => " +
                        field.FieldType.FullName + " " +
                        field.Name + " = " + valueDescription);

                    object nestedValue = null;
                    try
                    {
                        nestedValue = field.GetValue(dataSource);
                    }
                    catch
                    {
                    }

                    LogNestedDiagnosticMembers("datasource." + field.Name, nestedValue);
                }

                foreach (PropertyInfo property in dataSourceType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (property.GetIndexParameters().Length != 0 ||
                        !property.CanRead ||
                        !IsInterestingDiagnosticMember(property.Name, property.PropertyType))
                    {
                        continue;
                    }

                    string valueDescription;
                    try
                    {
                        valueDescription = DescribeDiagnosticValue(property.GetValue(dataSource, null));
                    }
                    catch (Exception ex)
                    {
                        valueDescription = "unavailable (" + ex.Message + ")";
                    }

                    ModLogger.Info(
                        "CoopMissionClientLogic: active datasource property => " +
                        property.PropertyType.FullName + " " +
                        property.Name + " = " + valueDescription);

                    object nestedValue = null;
                    try
                    {
                        nestedValue = property.GetValue(dataSource, null);
                    }
                    catch
                    {
                    }

                    LogNestedDiagnosticMembers("datasource." + property.Name, nestedValue);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: active class loadout diagnostics failed: " + ex.Message);
            }
        }

        private void TrySyncFixedTeamSelectCultures(Mission mission)
        {
            if (EnableVanillaSelectionUiExperiment)
                return;

            if (!EnableFixedClientTeamSelectCulturesExperiment || mission == null || !GameNetwork.IsClient)
                return;

            try
            {
                CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge = CoopBattleSelectionBridgeFile.ReadCurrentSelection();
                CoopBattleEntryPolicy.ClientSnapshot entryPolicy = CoopBattleEntryPolicy.BuildClientSnapshot(mission, selectionBridge);
                if (!entryPolicy.AllowLegacyVanillaTeamSelectionInteraction)
                    return;

                object teamSelection = mission.MissionBehaviors
                    .FirstOrDefault(behavior =>
                        behavior != null &&
                        behavior.GetType().FullName != null &&
                        behavior.GetType().FullName.IndexOf("MissionGauntletTeamSelection", StringComparison.OrdinalIgnoreCase) >= 0);
                if (teamSelection == null)
                    return;

                object dataSource = GetMemberValue(teamSelection, "_dataSource") ?? GetMemberValue(teamSelection, "DataSource");
                if (dataSource == null)
                    return;

                LogTeamSelectDiagnosticsOnce(teamSelection, dataSource);

                string syncKey = BuildTeamSelectCultureSyncKey(mission, dataSource);
                if (string.Equals(_lastAppliedTeamSelectCultureSyncKey, syncKey, StringComparison.Ordinal))
                    return;

                int updatedTeamCount = 0;
                HashSet<int> visited = new HashSet<int>();
                updatedTeamCount += TryApplyFixedCultureToKnownTeamVm(
                    GetMemberValue(dataSource, "_attackerTeam") ?? GetMemberValue(dataSource, "AttackerTeam"),
                    FixedClientAttackerCultureId,
                    "datasource.attacker",
                    visited);
                updatedTeamCount += TryApplyFixedCultureToKnownTeamVm(
                    GetMemberValue(dataSource, "_defenderTeam") ?? GetMemberValue(dataSource, "DefenderTeam"),
                    FixedClientDefenderCultureId,
                    "datasource.defender",
                    visited);
                updatedTeamCount += TryApplyFixedCultureToKnownTeamVm(
                    GetMemberValue(dataSource, "_team1") ?? GetMemberValue(dataSource, "Team1"),
                    FixedClientAttackerCultureId,
                    "datasource.team1",
                    visited);
                updatedTeamCount += TryApplyFixedCultureToKnownTeamVm(
                    GetMemberValue(dataSource, "_team2") ?? GetMemberValue(dataSource, "Team2"),
                    FixedClientDefenderCultureId,
                    "datasource.team2",
                    visited);
                updatedTeamCount += TryApplyFixedCultureToTeamVmCollection(
                    GetMemberValue(dataSource, "_teams") ?? GetMemberValue(dataSource, "Teams"),
                    visited);

                if (updatedTeamCount <= 0)
                    return;

                InvokeRefreshValuesIfPresent(dataSource);
                InvokeRefreshValuesIfPresent(teamSelection);
                _lastAppliedTeamSelectCultureSyncKey = syncKey;

                ModLogger.Info(
                    "CoopMissionClientLogic: synced fixed cultures into early team-select UI. " +
                    "SyncKey=" + syncKey +
                    " UpdatedTeams=" + updatedTeamCount +
                    " AttackerCulture=" + FixedClientAttackerCultureId +
                    " DefenderCulture=" + FixedClientDefenderCultureId);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: fixed team-select culture sync failed: " + ex.Message);
            }
        }

        private void TrySuppressVanillaEntryUi(Mission mission)
        {
            if (!EnableVanillaEntryUiSuppressionExperiment || mission == null || !GameNetwork.IsClient)
                return;

            try
            {
                CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge = CoopBattleSelectionBridgeFile.ReadCurrentSelection();
                CoopBattleEntryPolicy.ClientSnapshot entryPolicy = CoopBattleEntryPolicy.BuildClientSnapshot(mission, selectionBridge);
                if (!entryPolicy.UseAuthoritativeEntryPath)
                    return;

                string suppressionKey =
                    (entryPolicy.PlayerHasActiveAgent ? "agent" : "spectator") + "|" +
                    (entryPolicy.HasBridgeSide ? entryPolicy.BridgeSideRaw : "none") + "|" +
                    (entryPolicy.HasBridgeTroop ? entryPolicy.BridgeTroopOrEntryId : "none");

                bool suppressedAny = false;
                object teamSelection = mission.MissionBehaviors
                    .FirstOrDefault(behavior =>
                        behavior != null &&
                        behavior.GetType().FullName != null &&
                        behavior.GetType().FullName.IndexOf("MissionGauntletTeamSelection", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!entryPolicy.AllowLegacyVanillaTeamSelectionInteraction)
                    suppressedAny |= TrySuppressVanillaUiBehavior(teamSelection);

                object classLoadout = mission.MissionBehaviors
                    .FirstOrDefault(behavior =>
                        behavior != null &&
                        behavior.GetType().FullName != null &&
                        behavior.GetType().FullName.IndexOf("MissionGauntletClassLoadout", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!entryPolicy.AllowLegacyVanillaClassSelectionInteraction)
                    suppressedAny |= TrySuppressVanillaUiBehavior(classLoadout);

                if (!suppressedAny)
                    return;

                if (!string.Equals(_lastSuppressedVanillaEntryUiKey, suppressionKey, StringComparison.Ordinal))
                {
                    _lastSuppressedVanillaEntryUiKey = suppressionKey;
                    ModLogger.Info(
                        "CoopMissionClientLogic: suppressed vanilla entry UI. " +
                        entryPolicy.Describe());
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: vanilla entry UI suppression failed: " + ex.Message);
            }
        }

        private void TryAutoRequestTeamChangeFromBridge(Mission mission)
        {
            if (EnableVanillaSelectionUiExperiment)
                return;

            if (mission == null || !GameNetwork.IsClient || !GameNetwork.IsMultiplayer)
                return;

            if (_legacyOverlayAutoRequestCooldownRemaining > 0f)
                return;

            MissionPeer missionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
            if (missionPeer == null)
                return;

            if (missionPeer.Team != null && !ReferenceEquals(missionPeer.Team, mission.SpectatorTeam))
                return;

            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge = CoopBattleSelectionBridgeFile.ReadCurrentSelection();
            CoopBattleEntryPolicy.ClientSnapshot entryPolicy = CoopBattleEntryPolicy.BuildClientSnapshot(mission, selectionBridge);
            if (!entryPolicy.HasBridgeSide)
            {
                return;
            }

            BattleSideEnum bridgeSide = entryPolicy.BridgeSide;
            int teamIndex = bridgeSide == BattleSideEnum.Attacker ? 0 : 1;
            string requestKey = bridgeSide + "|" + teamIndex;
            if (string.Equals(_lastAutoRequestedTeamChangeKey, requestKey, StringComparison.Ordinal))
                return;

            try
            {
                GameNetwork.BeginModuleEventAsClient();
                GameNetwork.WriteMessage(new NetworkMessages.FromClient.TeamChange(autoAssign: false, teamIndex: teamIndex));
                GameNetwork.EndModuleEventAsClient();
                _lastAutoRequestedTeamChangeKey = requestKey;
                ModLogger.Info(
                    "CoopMissionClientLogic: auto-requested vanilla team change from bridge. " +
                    "BridgeSide=" + bridgeSide +
                    " TeamIndex=" + teamIndex);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: auto team change request failed: " + ex.Message);
            }
        }

        private static bool TrySuppressVanillaUiBehavior(object behavior)
        {
            if (behavior == null)
                return false;

            bool changed = false;
            object dataSource = GetMemberValue(behavior, "_dataSource") ?? GetMemberValue(behavior, "DataSource");
            foreach (object target in new[] { behavior, dataSource })
            {
                if (target == null)
                    continue;

                changed |= TrySetBooleanMember(target, "_isActive", false);
                changed |= TrySetBooleanMember(target, "IsActive", false);
                changed |= TrySetBooleanMember(target, "_isForceClosed", true);
                changed |= TrySetBooleanMember(target, "IsForceClosed", true);
                changed |= TrySetBooleanMember(target, "_isEnabled", false);
                changed |= TrySetBooleanMember(target, "IsEnabled", false);
                changed |= TrySetBooleanMember(target, "_isVisible", false);
                changed |= TrySetBooleanMember(target, "IsVisible", false);
                changed |= TrySetBooleanMember(target, "_isShown", false);
                changed |= TrySetBooleanMember(target, "IsShown", false);
                changed |= TrySetBooleanMember(target, "_isViewSuspended", true);
                changed |= TrySetBooleanMember(target, "IsViewSuspended", true);
                changed |= TrySetBooleanMember(target, "_tryToInitialize", false);
                changed |= TrySetBooleanMember(target, "TryToInitialize", false);
                InvokeRefreshValuesIfPresent(target);
                InvokeNoArgMethodIfPresent(target, "HandleFinalize");
                InvokeNoArgMethodIfPresent(target, "OnFinalize");
                InvokeNoArgMethodIfPresent(target, "HandleDeactivate");
                InvokeNoArgMethodIfPresent(target, "OnDeactivate");
            }

            return changed;
        }

        private static bool TrySetBooleanMember(object instance, string memberName, bool value)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
                return false;

            try
            {
                Type type = instance.GetType();
                PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null &&
                    property.CanWrite &&
                    property.PropertyType == typeof(bool) &&
                    property.GetIndexParameters().Length == 0)
                {
                    property.SetValue(instance, value, null);
                    return true;
                }

                FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && field.FieldType == typeof(bool))
                {
                    field.SetValue(instance, value);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private void LogTeamSelectDiagnosticsOnce(object teamSelection, object dataSource)
        {
            if (_hasLoggedTeamSelectDiagnostics)
                return;

            _hasLoggedTeamSelectDiagnostics = true;

            try
            {
                ModLogger.Info("CoopMissionClientLogic: team select diagnostics type = " + teamSelection.GetType().FullName);
                ModLogger.Info("CoopMissionClientLogic: team select datasource type = " + dataSource.GetType().FullName);

                foreach (FieldInfo field in dataSource.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!IsInterestingTeamSelectMember(field.Name, field.FieldType))
                        continue;

                    string valueDescription;
                    try
                    {
                        valueDescription = DescribeDiagnosticValue(field.GetValue(dataSource));
                    }
                    catch (Exception ex)
                    {
                        valueDescription = "unavailable (" + ex.Message + ")";
                    }

                    ModLogger.Info(
                        "CoopMissionClientLogic: team select datasource field => " +
                        field.FieldType.FullName + " " +
                        field.Name + " = " + valueDescription);
                }

                foreach (PropertyInfo property in dataSource.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (property.GetIndexParameters().Length != 0 ||
                        !property.CanRead ||
                        !IsInterestingTeamSelectMember(property.Name, property.PropertyType))
                    {
                        continue;
                    }

                    string valueDescription;
                    try
                    {
                        valueDescription = DescribeDiagnosticValue(property.GetValue(dataSource, null));
                    }
                    catch (Exception ex)
                    {
                        valueDescription = "unavailable (" + ex.Message + ")";
                    }

                    ModLogger.Info(
                        "CoopMissionClientLogic: team select datasource property => " +
                        property.PropertyType.FullName + " " +
                        property.Name + " = " + valueDescription);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: team select diagnostics failed: " + ex.Message);
            }
        }

        private void TrySyncFixedScoreboardCultures(Mission mission)
        {
            if (EnableVanillaSelectionUiExperiment)
                return;

            if (!EnableFixedClientTeamSelectCulturesExperiment || mission == null || !GameNetwork.IsClient)
                return;

            try
            {
                object scoreboardBehavior = mission.MissionBehaviors
                    .FirstOrDefault(behavior =>
                        behavior != null &&
                        behavior.GetType().FullName != null &&
                        behavior.GetType().FullName.IndexOf("MissionGauntletMultiplayerScoreboard", StringComparison.OrdinalIgnoreCase) >= 0);
                if (scoreboardBehavior == null)
                    return;

                object dataSource = ResolveScoreboardDataSource(scoreboardBehavior);
                if (dataSource == null)
                    return;

                LogScoreboardDiagnosticsOnce(scoreboardBehavior, dataSource);

                string syncKey = BuildScoreboardCultureSyncKey(mission, dataSource);
                if (string.Equals(_lastAppliedScoreboardCultureSyncKey, syncKey, StringComparison.Ordinal))
                    return;

                int updatedTeamCount = 0;
                HashSet<int> visited = new HashSet<int>();
                updatedTeamCount += TryApplyFixedCultureToKnownTeamVm(
                    GetMemberValue(dataSource, "_team1") ?? GetMemberValue(dataSource, "Team1") ?? GetMemberValue(dataSource, "_attackerTeam") ?? GetMemberValue(dataSource, "AttackerTeam"),
                    FixedClientAttackerCultureId,
                    "scoreboard.team1",
                    visited);
                updatedTeamCount += TryApplyFixedCultureToKnownTeamVm(
                    GetMemberValue(dataSource, "_team2") ?? GetMemberValue(dataSource, "Team2") ?? GetMemberValue(dataSource, "_defenderTeam") ?? GetMemberValue(dataSource, "DefenderTeam"),
                    FixedClientDefenderCultureId,
                    "scoreboard.team2",
                    visited);
                updatedTeamCount += TryApplyFixedCultureToTeamVmCollection(
                    GetMemberValue(dataSource, "_teams") ?? GetMemberValue(dataSource, "Teams") ?? GetMemberValue(dataSource, "_scoreboardSides") ?? GetMemberValue(dataSource, "ScoreboardSides"),
                    visited);
                updatedTeamCount += TryApplyFixedCultureToScoreboardSideCollection(
                    GetMemberValue(dataSource, "_sides") ?? GetMemberValue(dataSource, "Sides"),
                    visited);
                updatedTeamCount += TryApplyFixedCultureToScoreboardSideDictionary(
                    GetMemberValue(dataSource, "_missionSides") ?? GetMemberValue(dataSource, "MissionSides"),
                    visited);

                if (updatedTeamCount <= 0)
                    return;

                InvokeRefreshValuesIfPresent(dataSource);
                InvokeRefreshValuesIfPresent(scoreboardBehavior);
                _lastAppliedScoreboardCultureSyncKey = syncKey;

                ModLogger.Info(
                    "CoopMissionClientLogic: synced fixed cultures into scoreboard UI. " +
                    "SyncKey=" + syncKey +
                    " UpdatedTeams=" + updatedTeamCount +
                    " AttackerCulture=" + FixedClientAttackerCultureId +
                    " DefenderCulture=" + FixedClientDefenderCultureId);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: fixed scoreboard culture sync failed: " + ex.Message);
            }
        }

        private void LogScoreboardDiagnosticsOnce(object scoreboardBehavior, object dataSource)
        {
            if (_hasLoggedScoreboardDiagnostics)
                return;

            _hasLoggedScoreboardDiagnostics = true;

            try
            {
                ModLogger.Info("CoopMissionClientLogic: scoreboard diagnostics type = " + scoreboardBehavior.GetType().FullName);
                ModLogger.Info("CoopMissionClientLogic: scoreboard datasource type = " + dataSource.GetType().FullName);
                LogInterestingMembers("scoreboard behavior", scoreboardBehavior);
                LogInterestingMembers("scoreboard datasource", dataSource);
                LogNestedDiagnosticMembers("scoreboard.datasource", dataSource);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: scoreboard diagnostics failed: " + ex.Message);
            }
        }

        private static object ResolveScoreboardDataSource(object scoreboardBehavior)
        {
            if (scoreboardBehavior == null)
                return null;

            object directDataSource = GetMemberValue(scoreboardBehavior, "_dataSource") ??
                                      GetMemberValue(scoreboardBehavior, "DataSource") ??
                                      GetMemberValue(scoreboardBehavior, "_scoreboardDataSource") ??
                                      GetMemberValue(scoreboardBehavior, "ScoreboardDataSource") ??
                                      GetMemberValue(scoreboardBehavior, "_multiplayerScoreboardVM") ??
                                      GetMemberValue(scoreboardBehavior, "MultiplayerScoreboardVM");
            if (IsUsefulScoreboardDataSourceCandidate(directDataSource))
                return directDataSource;

            foreach (FieldInfo field in scoreboardBehavior.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!IsInterestingScoreboardMember(field.Name, field.FieldType))
                    continue;

                object value = null;
                try
                {
                    value = field.GetValue(scoreboardBehavior);
                }
                catch
                {
                }

                if (IsUsefulScoreboardDataSourceCandidate(value))
                    return value;
            }

            foreach (PropertyInfo property in scoreboardBehavior.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (property.GetIndexParameters().Length != 0 || !property.CanRead || !IsInterestingScoreboardMember(property.Name, property.PropertyType))
                    continue;

                object value = null;
                try
                {
                    value = property.GetValue(scoreboardBehavior, null);
                }
                catch
                {
                }

                if (IsUsefulScoreboardDataSourceCandidate(value))
                    return value;
            }

            return null;
        }

        private static bool IsUsefulScoreboardDataSourceCandidate(object value)
        {
            if (value == null)
                return false;

            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
                return false;

            string typeName = type.FullName ?? type.Name;
            if (typeName.IndexOf("Scoreboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Team", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("ViewModel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("VM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("DataSource", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        private static bool IsInterestingScoreboardMember(string memberName, Type memberType)
        {
            string typeName = memberType?.FullName ?? memberType?.Name ?? string.Empty;
            return memberName.IndexOf("score", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("team", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("data", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("vm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Scoreboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Team", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("ViewModel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("VM", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void LogControlFinalizeDiagnosticsForCurrentAgent(Mission mission, Agent currentMain)
        {
            if (mission == null || currentMain == null)
                return;

            if (!_loggedControlFinalizeDiagnosticsByAgentIndex.Add(currentMain.Index))
                return;

            try
            {
                NetworkCommunicator myPeer = GameNetwork.MyPeer;
                MissionPeer missionPeer = myPeer?.GetComponent<MissionPeer>();
                ModLogger.Info(
                    "CoopMissionClientLogic: control finalize diagnostics. " +
                    "AgentIndex=" + currentMain.Index +
                    " AgentTeam=" + (currentMain.Team?.TeamIndex ?? -1) +
                    " AgentSide=" + (currentMain.Team?.Side ?? BattleSideEnum.None) +
                    " AgentController=" + currentMain.Controller +
                    " AgentMissionPeer=" + DescribeDiagnosticValue(currentMain.MissionPeer) +
                    " MyPeer=" + (myPeer?.UserName ?? myPeer?.Index.ToString() ?? "null") +
                    " MissionPeerTeam=" + (missionPeer?.Team?.TeamIndex ?? -1) +
                    " MissionPeerSide=" + (missionPeer?.Team?.Side ?? BattleSideEnum.None) +
                    " MissionPeerCulture=" + (missionPeer?.Culture?.StringId ?? "null") +
                    " SelectedTroopIndex=" + (missionPeer?.SelectedTroopIndex ?? -1));

                MissionBehavior[] behaviors = mission.MissionBehaviors?.Where(behavior => behavior != null).ToArray() ?? Array.Empty<MissionBehavior>();
                foreach (MissionBehavior behavior in behaviors)
                {
                    string fullName = behavior.GetType().FullName ?? behavior.GetType().Name;
                    if (fullName.IndexOf("MissionMainAgentController", StringComparison.OrdinalIgnoreCase) < 0 &&
                        fullName.IndexOf("MainAgentControl", StringComparison.OrdinalIgnoreCase) < 0 &&
                        fullName.IndexOf("Order", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    ModLogger.Info("CoopMissionClientLogic: control behavior diagnostics type = " + fullName);
                    LogControlInterestingMembers("control behavior", behavior);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: control finalize diagnostics failed: " + ex.Message);
            }
        }

        private void TryRepairMainAgentControllerState(Mission mission, Agent currentMain)
        {
            if (mission == null || currentMain == null || !GameNetwork.IsClient)
                return;

            try
            {
                MissionBehavior controllerBehavior = mission.MissionBehaviors?.FirstOrDefault(behavior =>
                    behavior != null &&
                    ((behavior.GetType().FullName ?? behavior.GetType().Name).IndexOf("MissionMainAgentController", StringComparison.OrdinalIgnoreCase) >= 0));
                if (controllerBehavior == null)
                    return;

                Type controllerType = controllerBehavior.GetType();
                FieldInfo isPlayerAgentAddedField = controllerType.GetField("_isPlayerAgentAdded", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                bool isPlayerAgentAdded = isPlayerAgentAddedField != null && isPlayerAgentAddedField.FieldType == typeof(bool) &&
                                          (bool)isPlayerAgentAddedField.GetValue(controllerBehavior);
                if (isPlayerAgentAdded)
                    return;

                MethodInfo repairMethod = controllerType.GetMethod(
                    "Mission_OnMainAgentChanged",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(Agent) },
                    null);
                if (repairMethod == null)
                {
                    ModLogger.Info("CoopMissionClientLogic: main agent controller repair skipped: Mission_OnMainAgentChanged(Agent) missing.");
                    return;
                }

                repairMethod.Invoke(controllerBehavior, new object[] { null });

                bool repairedIsPlayerAgentAdded = isPlayerAgentAddedField != null && isPlayerAgentAddedField.FieldType == typeof(bool) &&
                                                  (bool)isPlayerAgentAddedField.GetValue(controllerBehavior);
                ModLogger.Info(
                    "CoopMissionClientLogic: main agent controller repair invoked. " +
                    "AgentIndex=" + currentMain.Index +
                    " BeforeIsPlayerAgentAdded=" + isPlayerAgentAdded +
                    " AfterIsPlayerAgentAdded=" + repairedIsPlayerAgentAdded);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: main agent controller repair failed: " + ex.Message);
            }
        }

        private void TryLogPostControlAgentDiagnostics(Mission mission, float dt)
        {
            if (mission == null || !GameNetwork.IsClient || _pendingPostControlDiagnosticAgentIndex < 0)
                return;

            _pendingPostControlDiagnosticDelay -= dt;
            if (_pendingPostControlDiagnosticDelay > 0f)
                return;

            int agentIndex = _pendingPostControlDiagnosticAgentIndex;
            _pendingPostControlDiagnosticAgentIndex = -1;
            _pendingPostControlDiagnosticDelay = 0f;

            Agent currentMain = Agent.Main;
            if (currentMain == null || currentMain.Index != agentIndex)
                return;

            if (!_loggedPostControlDiagnosticsByAgentIndex.Add(agentIndex))
                return;

            try
            {
                MissionWeapon wieldedWeapon = currentMain.WieldedWeapon;
                string wieldedItemId = wieldedWeapon.Item?.StringId ?? "null";
                string wieldedUsage = wieldedWeapon.CurrentUsageItem?.ItemUsage ?? "null";

                Agent currentMount = currentMain.MountAgent;
                string mountHealth = currentMount != null
                    ? currentMount.Health.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                    : "n/a";
                string mountHealthLimit = currentMount != null
                    ? currentMount.HealthLimit.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                    : "n/a";

                ModLogger.Info(
                    "CoopMissionClientLogic: post-control agent diagnostics. " +
                    "AgentIndex=" + currentMain.Index +
                    " State=" + currentMain.State +
                    " Controller=" + currentMain.Controller +
                    " IsActive=" + currentMain.IsActive() +
                    " IsPlayerControlled=" + currentMain.IsPlayerControlled +
                    " IsAIControlled=" + currentMain.IsAIControlled +
                    " IsMount=" + currentMain.IsMount +
                    " HasMount=" + currentMain.HasMount +
                    " MountAgent=" + DescribeDiagnosticValue(currentMain.MountAgent) +
                    " RiderAgent=" + DescribeDiagnosticValue(currentMain.RiderAgent) +
                    " Health=" + currentMain.Health +
                    " HealthLimit=" + currentMain.HealthLimit +
                    " MountHealth=" + mountHealth +
                    " MountHealthLimit=" + mountHealthLimit +
                    " Team=" + (currentMain.Team?.TeamIndex ?? -1) +
                    " Side=" + (currentMain.Team?.Side ?? BattleSideEnum.None) +
                    " MovementFlags=" + currentMain.MovementFlags +
                    " ActionStage=" + currentMain.GetCurrentActionStage(0) +
                    " WieldedItem=" + wieldedItemId +
                    " WieldedUsage=" + wieldedUsage +
                    " MissionPeer=" + DescribeDiagnosticValue(currentMain.MissionPeer));

                MissionBehavior[] behaviors = mission.MissionBehaviors?.Where(behavior =>
                    behavior != null &&
                    (((behavior.GetType().FullName ?? behavior.GetType().Name).IndexOf("MainAgentControl", StringComparison.OrdinalIgnoreCase) >= 0) ||
                     ((behavior.GetType().FullName ?? behavior.GetType().Name).IndexOf("Order", StringComparison.OrdinalIgnoreCase) >= 0) ||
                     ((behavior.GetType().FullName ?? behavior.GetType().Name).IndexOf("MissionMainAgentController", StringComparison.OrdinalIgnoreCase) >= 0)))
                    .ToArray() ?? Array.Empty<MissionBehavior>();

                foreach (MissionBehavior behavior in behaviors)
                {
                    ModLogger.Info("CoopMissionClientLogic: post-control behavior diagnostics type = " + (behavior.GetType().FullName ?? behavior.GetType().Name));
                    LogControlInterestingMembers("post-control behavior", behavior);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: post-control diagnostics failed: " + ex.Message);
            }
        }

        private static void LogControlInterestingMembers(string ownerName, object instance)
        {
            if (instance == null)
                return;

            foreach (FieldInfo field in instance.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!IsInterestingControlMember(field.Name, field.FieldType))
                    continue;

                string valueDescription;
                try
                {
                    valueDescription = DescribeDiagnosticValue(field.GetValue(instance));
                }
                catch (Exception ex)
                {
                    valueDescription = "unavailable (" + ex.Message + ")";
                }

                ModLogger.Info(
                    "CoopMissionClientLogic: " + ownerName + " field => " +
                    field.FieldType.FullName + " " +
                    field.Name + " = " + valueDescription);
            }

            foreach (PropertyInfo property in instance.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (property.GetIndexParameters().Length != 0 || !property.CanRead || !IsInterestingControlMember(property.Name, property.PropertyType))
                    continue;

                string valueDescription;
                try
                {
                    valueDescription = DescribeDiagnosticValue(property.GetValue(instance, null));
                }
                catch (Exception ex)
                {
                    valueDescription = "unavailable (" + ex.Message + ")";
                }

                ModLogger.Info(
                    "CoopMissionClientLogic: " + ownerName + " property => " +
                    property.PropertyType.FullName + " " +
                    property.Name + " = " + valueDescription);
            }
        }

        private static bool IsInterestingControlMember(string memberName, Type memberType)
        {
            string loweredName = memberName ?? string.Empty;
            string typeName = memberType?.FullName ?? memberType?.Name ?? string.Empty;
            return loweredName.IndexOf("owner", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   loweredName.IndexOf("order", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   loweredName.IndexOf("control", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   loweredName.IndexOf("agent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   loweredName.IndexOf("peer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   loweredName.IndexOf("team", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Agent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Peer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Order", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Team", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void LogInterestingMembers(string ownerName, object instance)
        {
            if (instance == null)
                return;

            foreach (FieldInfo field in instance.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!IsInterestingScoreboardMember(field.Name, field.FieldType) && !IsInterestingTeamSelectMember(field.Name, field.FieldType))
                    continue;

                string valueDescription;
                try
                {
                    valueDescription = DescribeDiagnosticValue(field.GetValue(instance));
                }
                catch (Exception ex)
                {
                    valueDescription = "unavailable (" + ex.Message + ")";
                }

                ModLogger.Info(
                    "CoopMissionClientLogic: " + ownerName + " field => " +
                    field.FieldType.FullName + " " +
                    field.Name + " = " + valueDescription);
            }

            foreach (PropertyInfo property in instance.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (property.GetIndexParameters().Length != 0 ||
                    !property.CanRead ||
                    (!IsInterestingScoreboardMember(property.Name, property.PropertyType) && !IsInterestingTeamSelectMember(property.Name, property.PropertyType)))
                {
                    continue;
                }

                string valueDescription;
                try
                {
                    valueDescription = DescribeDiagnosticValue(property.GetValue(instance, null));
                }
                catch (Exception ex)
                {
                    valueDescription = "unavailable (" + ex.Message + ")";
                }

                ModLogger.Info(
                    "CoopMissionClientLogic: " + ownerName + " property => " +
                    property.PropertyType.FullName + " " +
                    property.Name + " = " + valueDescription);
            }
        }

        private static bool IsInterestingTeamSelectMember(string memberName, Type memberType)
        {
            string typeName = memberType?.FullName ?? memberType?.Name ?? string.Empty;
            return memberName.IndexOf("team", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("culture", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("attacker", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("defender", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Team", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Culture", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int TryApplyFixedCultureToTeamVmCollection(object teamsObject, HashSet<int> visited)
        {
            if (!(teamsObject is IEnumerable teams))
                return 0;

            int updatedTeamCount = 0;
            foreach (object teamVm in teams)
            {
                string cultureId = ResolveFixedCultureIdForClientTeamVm(teamVm);
                if (string.IsNullOrWhiteSpace(cultureId))
                    continue;

                updatedTeamCount += TryApplyFixedCultureToKnownTeamVm(teamVm, cultureId, "datasource.teams", visited);
            }

            return updatedTeamCount;
        }

        private static int TryApplyFixedCultureToScoreboardSideCollection(object sidesObject, HashSet<int> visited)
        {
            if (!(sidesObject is IEnumerable sides))
                return 0;

            int updatedTeamCount = 0;
            foreach (object sideVm in sides)
            {
                string cultureId = ResolveFixedCultureIdForClientTeamVm(sideVm);
                if (string.IsNullOrWhiteSpace(cultureId))
                    continue;

                updatedTeamCount += TryApplyFixedCultureToKnownTeamVm(sideVm, cultureId, "scoreboard.sides", visited);
            }

            return updatedTeamCount;
        }

        private static int TryApplyFixedCultureToScoreboardSideDictionary(object missionSidesObject, HashSet<int> visited)
        {
            if (!(missionSidesObject is IDictionary dictionary))
                return 0;

            int updatedTeamCount = 0;
            foreach (DictionaryEntry entry in dictionary)
            {
                string cultureId = null;
                if (entry.Key is BattleSideEnum keySide)
                {
                    if (keySide == BattleSideEnum.Attacker)
                        cultureId = FixedClientAttackerCultureId;
                    else if (keySide == BattleSideEnum.Defender)
                        cultureId = FixedClientDefenderCultureId;
                }

                if (string.IsNullOrWhiteSpace(cultureId))
                    cultureId = ResolveFixedCultureIdForClientTeamVm(entry.Value);
                if (string.IsNullOrWhiteSpace(cultureId))
                    continue;

                updatedTeamCount += TryApplyFixedCultureToKnownTeamVm(entry.Value, cultureId, "scoreboard.missionSides", visited);
            }

            return updatedTeamCount;
        }

        private static string ResolveFixedCultureIdForClientTeamVm(object teamVm)
        {
            if (teamVm == null)
                return null;

            object sideValue = GetMemberValue(teamVm, "Side") ?? GetMemberValue(teamVm, "_side");
            if (sideValue is BattleSideEnum side)
            {
                if (side == BattleSideEnum.Attacker)
                    return FixedClientAttackerCultureId;
                if (side == BattleSideEnum.Defender)
                    return FixedClientDefenderCultureId;
            }
            else
            {
                string sideText = sideValue?.ToString();
                if (string.Equals(sideText, nameof(BattleSideEnum.Attacker), StringComparison.OrdinalIgnoreCase))
                    return FixedClientAttackerCultureId;
                if (string.Equals(sideText, nameof(BattleSideEnum.Defender), StringComparison.OrdinalIgnoreCase))
                    return FixedClientDefenderCultureId;
            }

            object teamIndexValue = GetMemberValue(teamVm, "TeamIndex") ?? GetMemberValue(teamVm, "_teamIndex");
            if (teamIndexValue is int teamIndex)
            {
                if (teamIndex == 1)
                    return FixedClientAttackerCultureId;
                if (teamIndex == 2)
                    return FixedClientDefenderCultureId;
            }

            return null;
        }

        private static int TryApplyFixedCultureToKnownTeamVm(object teamVm, string cultureId, string path, HashSet<int> visited)
        {
            if (teamVm == null || string.IsNullOrWhiteSpace(cultureId))
                return 0;

            int objectId = RuntimeHelpers.GetHashCode(teamVm);
            if (visited != null && !visited.Add(objectId))
                return 0;

            BasicCultureObject culture = MBObjectManager.Instance?.GetObject<BasicCultureObject>(cultureId);
            string cultureName = culture?.Name?.ToString();
            if (string.IsNullOrWhiteSpace(cultureName))
                cultureName = cultureId;

            bool updated = false;
            updated |= TrySetTeamVmCultureMember(teamVm, "Culture", culture, cultureId);
            updated |= TrySetTeamVmCultureMember(teamVm, "_culture", culture, cultureId);
            updated |= TrySetTeamVmCultureMember(teamVm, "<Culture>k__BackingField", culture, cultureId);
            updated |= TrySetTeamVmTextMember(teamVm, "CultureId", cultureId);
            updated |= TrySetTeamVmTextMember(teamVm, "_cultureId", cultureId);
            updated |= TrySetTeamVmTextMember(teamVm, "CultureCode", cultureId);
            updated |= TrySetTeamVmTextMember(teamVm, "_cultureCode", cultureId);
            updated |= TrySetTeamVmTextMember(teamVm, "FactionId", cultureId);
            updated |= TrySetTeamVmTextMember(teamVm, "_factionId", cultureId);
            updated |= TrySetTeamVmTextMember(teamVm, "CultureName", cultureName);
            updated |= TrySetTeamVmTextMember(teamVm, "_cultureName", cultureName);
            updated |= TrySetTeamVmTextMember(teamVm, "FactionName", cultureName);
            updated |= TrySetTeamVmTextMember(teamVm, "_factionName", cultureName);
            updated |= TrySetTeamVmTextMember(teamVm, "Name", cultureName);
            updated |= TrySetTeamVmTextMember(teamVm, "_name", cultureName);
            updated |= TrySetTeamVmTextMember(teamVm, "TeamName", cultureName);
            updated |= TrySetTeamVmTextMember(teamVm, "_teamName", cultureName);

            if (updated)
            {
                InvokeRefreshValuesIfPresent(teamVm);
                ModLogger.Info(
                    "CoopMissionClientLogic: fixed early team-select culture VM. " +
                    "Path=" + path +
                    " Type=" + teamVm.GetType().FullName +
                    " AppliedCulture=" + cultureId +
                    " AppliedName=" + cultureName);
                return 1;
            }

            return 0;
        }

        private static bool TrySetTeamVmCultureMember(object instance, string memberName, BasicCultureObject culture, string cultureId)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
                return false;

            try
            {
                Type type = instance.GetType();
                PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
                {
                    if (property.PropertyType == typeof(string))
                    {
                        property.SetValue(instance, cultureId, null);
                        return true;
                    }

                    if (culture != null && property.PropertyType.IsAssignableFrom(culture.GetType()))
                    {
                        property.SetValue(instance, culture, null);
                        return true;
                    }
                }

                FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    if (field.FieldType == typeof(string))
                    {
                        field.SetValue(instance, cultureId);
                        return true;
                    }

                    if (culture != null && field.FieldType.IsAssignableFrom(culture.GetType()))
                    {
                        field.SetValue(instance, culture);
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TrySetTeamVmTextMember(object instance, string memberName, string value)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName) || value == null)
                return false;

            try
            {
                Type type = instance.GetType();
                PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0 && property.PropertyType == typeof(string))
                {
                    property.SetValue(instance, value, null);
                    return true;
                }

                FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && field.FieldType == typeof(string))
                {
                    field.SetValue(instance, value);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static void InvokeRefreshValuesIfPresent(object instance)
        {
            if (instance == null)
                return;

            try
            {
                MethodInfo method = instance.GetType().GetMethod("RefreshValues", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                method?.Invoke(instance, Array.Empty<object>());
            }
            catch
            {
            }
        }

        private static void InvokeNoArgMethodIfPresent(object instance, string methodName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(methodName))
                return;

            try
            {
                MethodInfo method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                method?.Invoke(instance, Array.Empty<object>());
            }
            catch
            {
            }
        }

        private void TryFilterClassLoadoutToCoopUnits(Mission mission)
        {
            if (EnableVanillaSelectionUiExperiment)
                return;

            if (mission == null)
                return;

            try
            {
                object classLoadout = mission.MissionBehaviors
                    .FirstOrDefault(behavior =>
                        behavior != null &&
                        behavior.GetType().FullName != null &&
                        behavior.GetType().FullName.IndexOf("MissionGauntletClassLoadout", StringComparison.OrdinalIgnoreCase) >= 0);
                if (classLoadout == null)
                    return;

                FieldInfo dataSourceField = classLoadout.GetType().GetField("_dataSource", BindingFlags.Instance | BindingFlags.NonPublic);
                object dataSource = dataSourceField?.GetValue(classLoadout);
                if (dataSource == null)
                    return;

                CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge = CoopBattleSelectionBridgeFile.ReadCurrentSelection();
                CoopBattleEntryPolicy.ClientSnapshot entryPolicy = CoopBattleEntryPolicy.BuildClientSnapshot(mission, selectionBridge);
                if (!entryPolicy.UseAuthoritativeEntryPath)
                    return;

                HashSet<string> allowedTroopIds = ResolveAllowedCoopTroopIdsForClientPicker();
                string filterKey = BuildClassLoadoutFilterKey(mission, dataSource);
                if (string.Equals(_lastAppliedClassLoadoutFilterKey, filterKey, StringComparison.Ordinal))
                    return;

                object classesObject = GetMemberValue(dataSource, "_classes") ?? GetMemberValue(dataSource, "Classes");
                if (!(classesObject is IList groups) || groups.Count == 0)
                    return;

                if (allowedTroopIds.Count > 0 && !HasAnyAllowedCoopHeroClass(groups, allowedTroopIds))
                {
                    ModLogger.Info(
                        "CoopMissionClientLogic: allowed troop ids did not match any class-loadout subclass; " +
                        "falling back to broad coop filter. " +
                        "AllowedTroops=" + string.Join(",", allowedTroopIds));
                    allowedTroopIds.Clear();
                }

                bool anyRemoved = false;
                int keptSubclassCount = 0;
                for (int groupIndex = groups.Count - 1; groupIndex >= 0; groupIndex--)
                {
                    object groupVm = groups[groupIndex];
                    object subClassesObject = GetMemberValue(groupVm, "_subClasses") ?? GetMemberValue(groupVm, "SubClasses");
                    if (!(subClassesObject is IList subClasses))
                        continue;

                    for (int subClassIndex = subClasses.Count - 1; subClassIndex >= 0; subClassIndex--)
                    {
                        object heroClassVm = subClasses[subClassIndex];
                        if (ShouldKeepHeroClassVmForCoopPicker(heroClassVm, allowedTroopIds))
                        {
                            keptSubclassCount++;
                            continue;
                        }

                        subClasses.RemoveAt(subClassIndex);
                        anyRemoved = true;
                    }

                    if (subClasses.Count == 0)
                    {
                        groups.RemoveAt(groupIndex);
                        anyRemoved = true;
                    }
                }

                if (!anyRemoved)
                    return;

                object currentSelectedClass = GetMemberValue(dataSource, "_currentSelectedClass") ?? GetMemberValue(dataSource, "CurrentSelectedClass");
                if (!ShouldKeepHeroClassVmForCoopPicker(currentSelectedClass, allowedTroopIds))
                {
                    object firstCoopClass = FindPreferredAllowedCoopHeroClass(groups, allowedTroopIds);
                    if (firstCoopClass != null)
                    {
                        SetMemberValue(dataSource, "_currentSelectedClass", firstCoopClass);
                        SetMemberValue(dataSource, "CurrentSelectedClass", firstCoopClass);
                    }
                }

                _lastAppliedClassLoadoutFilterKey = filterKey;
                ModLogger.Info(
                    "CoopMissionClientLogic: filtered class loadout to coop units. " +
                    "FilterKey=" + filterKey + " " +
                    "AllowedTroops=" + string.Join(",", allowedTroopIds) + " " +
                    "RemainingGroups=" + groups.Count +
                    " RemainingCoopSubClasses=" + keptSubclassCount);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: class loadout coop filter failed: " + ex.Message);
            }
        }

        private void LogEntryPolicySnapshot(Mission mission)
        {
            if (mission == null || !GameNetwork.IsClient)
                return;

            try
            {
                CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge = CoopBattleSelectionBridgeFile.ReadCurrentSelection();
                CoopBattleEntryPolicy.ClientSnapshot entryPolicy = CoopBattleEntryPolicy.BuildClientSnapshot(mission, selectionBridge);
                string snapshotText = entryPolicy.Describe();
                if (string.Equals(_lastLoggedEntryPolicySnapshot, snapshotText, StringComparison.Ordinal))
                    return;

                _lastLoggedEntryPolicySnapshot = snapshotText;
                ModLogger.Info("CoopMissionClientLogic: entry policy snapshot. " + snapshotText);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: entry policy snapshot failed: " + ex.Message);
            }
        }

        private static bool HasAnyAllowedCoopHeroClass(IList groups, HashSet<string> allowedTroopIds)
        {
            if (groups == null || allowedTroopIds == null || allowedTroopIds.Count == 0)
                return false;

            foreach (object groupVm in groups)
            {
                object subClassesObject = GetMemberValue(groupVm, "_subClasses") ?? GetMemberValue(groupVm, "SubClasses");
                if (!(subClassesObject is IList subClasses))
                    continue;

                foreach (object heroClassVm in subClasses)
                {
                    if (ShouldKeepHeroClassVmForCoopPicker(heroClassVm, allowedTroopIds))
                        return true;
                }
            }

            return false;
        }

        private static object FindFirstAllowedCoopHeroClass(IList groups, HashSet<string> allowedTroopIds)
        {
            foreach (object groupVm in groups)
            {
                object subClassesObject = GetMemberValue(groupVm, "_subClasses") ?? GetMemberValue(groupVm, "SubClasses");
                if (!(subClassesObject is IList subClasses))
                    continue;

                foreach (object heroClassVm in subClasses)
                {
                    if (ShouldKeepHeroClassVmForCoopPicker(heroClassVm, allowedTroopIds))
                        return heroClassVm;
                }
            }

            return null;
        }

        private static object FindPreferredAllowedCoopHeroClass(IList groups, HashSet<string> allowedTroopIds)
        {
            MissionPeer missionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            string preferredTroopId = NormalizeTroopId(selectionState.TroopId);
            if (!string.IsNullOrWhiteSpace(preferredTroopId))
            {
                foreach (object groupVm in groups)
                {
                    object subClassesObject = GetMemberValue(groupVm, "_subClasses") ?? GetMemberValue(groupVm, "SubClasses");
                    if (!(subClassesObject is IList subClasses))
                        continue;

                    foreach (object heroClassVm in subClasses)
                    {
                        if (!ShouldKeepHeroClassVmForCoopPicker(heroClassVm, allowedTroopIds))
                            continue;

                        string heroClassTroopId = NormalizeTroopId(TryGetHeroClassVmTroopId(heroClassVm));
                        if (string.Equals(heroClassTroopId, preferredTroopId, StringComparison.Ordinal))
                            return heroClassVm;
                    }
                }
            }

            return FindFirstAllowedCoopHeroClass(groups, allowedTroopIds);
        }

        private static bool ShouldKeepHeroClassVmForCoopPicker(object heroClassVm, HashSet<string> allowedTroopIds)
        {
            if (!IsCoopHeroClassVm(heroClassVm))
                return false;

            if (allowedTroopIds == null || allowedTroopIds.Count == 0)
                return true;

            string heroClassTroopId = TryGetHeroClassVmTroopId(heroClassVm);
            if (string.IsNullOrWhiteSpace(heroClassTroopId))
                return true;

            return allowedTroopIds.Contains(heroClassTroopId);
        }

        private static bool IsCoopHeroClassVm(object heroClassVm)
        {
            if (heroClassVm == null)
                return false;

            object heroClass = GetMemberValue(heroClassVm, "HeroClass");
            if (IsCoopHeroClassObject(heroClass))
                return true;

            string troopTypeId = GetMemberValue(heroClassVm, "_troopTypeId") as string ?? GetMemberValue(heroClassVm, "TroopTypeId") as string;
            if (!string.IsNullOrEmpty(troopTypeId) && troopTypeId.IndexOf("coop", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            foreach (string memberName in new[] { "Name", "_name", "ClassName", "_className", "Title", "_title" })
            {
                string text = GetMemberValue(heroClassVm, memberName)?.ToString();
                if (!string.IsNullOrEmpty(text) && text.IndexOf("coop", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static string TryGetHeroClassVmTroopId(object heroClassVm)
        {
            if (heroClassVm == null)
                return null;

            object heroClass = GetMemberValue(heroClassVm, "HeroClass");
            string heroClassTroopId = TryGetHeroClassTroopId(heroClass);
            if (!string.IsNullOrWhiteSpace(heroClassTroopId))
                return heroClassTroopId;

            return NormalizeTroopId(
                GetMemberValue(heroClassVm, "_troopTypeId") as string ??
                GetMemberValue(heroClassVm, "TroopTypeId") as string);
        }

        private static string TryGetHeroClassTroopId(object heroClass)
        {
            if (heroClass == null)
                return null;

            foreach (string memberName in new[] { "Character", "CharacterObject", "TroopCharacter", "HeroCharacter" })
            {
                object character = GetMemberValue(heroClass, memberName);
                if (character == null)
                    continue;

                string stringId = NormalizeTroopId(
                    GetMemberValue(character, "StringId") as string ??
                    GetMemberValue(character, "Id") as string);
                if (!string.IsNullOrWhiteSpace(stringId))
                    return stringId;
            }

            return NormalizeTroopId(GetMemberValue(heroClass, "StringId") as string ?? GetMemberValue(heroClass, "Id") as string);
        }

        private static HashSet<string> ResolveAllowedCoopTroopIdsForClientPicker()
        {
            HashSet<string> allowedTroopIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            MissionPeer missionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
            if (missionPeer == null)
                return allowedTroopIds;

            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge = CoopBattleSelectionBridgeFile.ReadCurrentSelection();
            string requestedTroopOrEntryId = selectionBridge?.TroopOrEntryId;
            if (!string.IsNullOrWhiteSpace(requestedTroopOrEntryId))
            {
                RosterEntryState requestedEntry = BattleSnapshotRuntimeState.GetEntryState(requestedTroopOrEntryId);
                string requestedTroopId = requestedEntry?.CharacterId ?? NormalizeTroopId(requestedTroopOrEntryId);
                if (!string.IsNullOrWhiteSpace(requestedTroopId))
                {
                    string cultureSpecificRequestedTroopId = TryBuildClientCultureSpecificCoopTroopId(requestedTroopId, missionPeer?.Culture);
                    AddAllowedCoopTroopId(allowedTroopIds, cultureSpecificRequestedTroopId);
                    AddAllowedCoopTroopId(allowedTroopIds, requestedTroopId);
                    return allowedTroopIds;
                }
            }

            BattleSideEnum authoritativeSide = ResolveClientPickerSide(missionPeer, selectionBridge);
            IReadOnlyList<string> allowedBaseTroopIds = ResolveAllowedBaseTroopIdsForClientPicker(authoritativeSide);
            foreach (string baseTargetTroopId in allowedBaseTroopIds)
            {
                string cultureSpecificTargetTroopId = TryBuildClientCultureSpecificCoopTroopId(baseTargetTroopId, missionPeer?.Culture);
                AddAllowedCoopTroopId(allowedTroopIds, cultureSpecificTargetTroopId);
                AddAllowedCoopTroopId(allowedTroopIds, baseTargetTroopId);
            }

            List<MultiplayerClassDivisions.MPHeroClass> cultureClasses = MultiplayerClassDivisions
                .GetMPHeroClasses(missionPeer.Culture)
                ?.Where(heroClass => heroClass?.HeroCharacter != null)
                .ToList();

            int selectedTroopIndex = missionPeer.SelectedTroopIndex;
            bool shouldUseVanillaSelectedClassFallback =
                allowedTroopIds.Count == 0 &&
                !CoopMissionSpawnLogic.HasSideScopedRoster();
            if (shouldUseVanillaSelectedClassFallback &&
                cultureClasses != null &&
                selectedTroopIndex >= 0 &&
                selectedTroopIndex < cultureClasses.Count)
            {
                AddAllowedCoopTroopId(allowedTroopIds, cultureClasses[selectedTroopIndex]?.HeroCharacter?.StringId);
            }

            return allowedTroopIds;
        }

        private static BattleSideEnum ResolveClientPickerSide(
            MissionPeer missionPeer,
            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge)
        {
            if (selectionBridge != null &&
                TryParseClientBridgeSide(selectionBridge.Side, out BattleSideEnum bridgedSide) &&
                bridgedSide != BattleSideEnum.None)
            {
                return bridgedSide;
            }

            return CoopMissionSpawnLogic.ResolveAuthoritativeSide(missionPeer, Mission.Current, "client-picker");
        }

        private static bool TryParseClientBridgeSide(string requestedSideRaw, out BattleSideEnum requestedSide)
        {
            requestedSide = BattleSideEnum.None;
            if (string.IsNullOrWhiteSpace(requestedSideRaw))
                return false;

            string normalized = requestedSideRaw.Trim().ToLowerInvariant();
            if (normalized == "attacker" || normalized == "attackers" || normalized == "1")
            {
                requestedSide = BattleSideEnum.Attacker;
                return true;
            }

            if (normalized == "defender" || normalized == "defenders" || normalized == "2")
            {
                requestedSide = BattleSideEnum.Defender;
                return true;
            }

            return false;
        }

        private static IReadOnlyList<string> ResolveAllowedBaseTroopIdsForClientPicker(BattleSideEnum side)
        {
            if (side == BattleSideEnum.None && CoopMissionSpawnLogic.HasSideScopedRoster())
                return Array.Empty<string>();

            IReadOnlyList<string> authorityTroopIds = CoopMissionSpawnLogic.GetAllowedControlTroopIdsSnapshot(side);
            if (authorityTroopIds.Count > 0)
                return authorityTroopIds;

            IReadOnlyList<RosterEntryState> entryStates = CoopMissionSpawnLogic.GetAllowedControlEntryStatesSnapshot(side);
            if (entryStates.Count > 0)
            {
                return entryStates
                    .Select(entry => entry?.CharacterId)
                    .Where(characterId => !string.IsNullOrWhiteSpace(characterId))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            }

            BattleRuntimeState state = BattleSnapshotRuntimeState.GetState();
            if (state?.Sides != null && state.Sides.Count > 0)
            {
                string canonicalSideKey = side == BattleSideEnum.Attacker
                    ? "attacker"
                    : side == BattleSideEnum.Defender
                        ? "defender"
                        : null;
                if (!string.IsNullOrWhiteSpace(canonicalSideKey) &&
                    state.SidesByKey.TryGetValue(canonicalSideKey, out BattleSideState sideState) &&
                    sideState?.TroopIds != null &&
                    sideState.TroopIds.Count > 0)
                {
                    return sideState.TroopIds
                        .Where(characterId => !string.IsNullOrWhiteSpace(characterId))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                }
            }

            IReadOnlyList<string> fixedTestTroopIds = CoopMissionSpawnLogic.GetFixedTestAllowedControlTroopIdsForSidePublic(side);
            if (fixedTestTroopIds.Count > 0)
                return fixedTestTroopIds;

            return Array.Empty<string>();
        }

        private static string TryBuildClientCultureSpecificCoopTroopId(string targetTroopId, BasicCultureObject culture)
        {
            string cultureToken = TryExtractCultureTokenForClientPicker(culture);
            if (string.IsNullOrWhiteSpace(cultureToken))
                return NormalizeTroopId(targetTroopId);

            return TryBuildCultureSpecificCoopTroopIdForClientPicker(targetTroopId, cultureToken);
        }

        private static string TryBuildCultureSpecificCoopTroopIdForClientPicker(string targetTroopId, string cultureToken)
        {
            string normalizedTargetTroopId = NormalizeTroopId(targetTroopId);
            if (string.IsNullOrWhiteSpace(normalizedTargetTroopId) ||
                string.IsNullOrWhiteSpace(cultureToken) ||
                !normalizedTargetTroopId.StartsWith("mp_coop_", StringComparison.Ordinal))
            {
                return null;
            }

            string role = GetTroopRoleForClientPicker(normalizedTargetTroopId);
            if (string.IsNullOrWhiteSpace(role))
                return null;

            string weight = GetTroopWeightForClientPicker(normalizedTargetTroopId);
            string prefix = string.IsNullOrWhiteSpace(weight)
                ? "mp_coop_" + role + "_"
                : "mp_coop_" + weight + "_" + role + "_";

            return prefix + cultureToken + "_troop";
        }

        private static string TryExtractCultureTokenForClientPicker(BasicCultureObject culture)
        {
            string cultureId = culture?.StringId;
            if (string.IsNullOrWhiteSpace(cultureId))
                return null;

            int dotIndex = cultureId.LastIndexOf('.');
            if (dotIndex >= 0 && dotIndex < cultureId.Length - 1)
                return cultureId.Substring(dotIndex + 1).Trim().ToLowerInvariant();

            return cultureId.Trim().ToLowerInvariant();
        }

        private static string GetTroopRoleForClientPicker(string troopId)
        {
            if (string.IsNullOrWhiteSpace(troopId))
                return string.Empty;

            if (troopId.Contains("_cavalry_"))
                return "cavalry";
            if (troopId.Contains("_ranged_") || troopId.Contains("_archer_"))
                return "ranged";
            if (troopId.Contains("_infantry_"))
                return "infantry";
            return string.Empty;
        }

        private static string GetTroopWeightForClientPicker(string troopId)
        {
            if (string.IsNullOrWhiteSpace(troopId))
                return string.Empty;

            if (troopId.Contains("_heavy_"))
                return "heavy";
            if (troopId.Contains("_light_"))
                return "light";
            if (troopId.Contains("_medium_"))
                return "medium";
            return string.Empty;
        }

        private static void AddAllowedCoopTroopId(HashSet<string> allowedTroopIds, string troopId)
        {
            string normalizedTroopId = NormalizeTroopId(troopId);
            if (string.IsNullOrWhiteSpace(normalizedTroopId) || normalizedTroopId.IndexOf("mp_coop_", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            allowedTroopIds.Add(normalizedTroopId);

            string alternateTroopId = GetAlternateCoopTroopIdVariant(normalizedTroopId);
            if (!string.IsNullOrWhiteSpace(alternateTroopId))
                allowedTroopIds.Add(alternateTroopId);
        }

        private static string NormalizeTroopId(string troopId)
        {
            return string.IsNullOrWhiteSpace(troopId)
                ? null
                : troopId.Trim().ToLowerInvariant();
        }

        private static string GetAlternateCoopTroopIdVariant(string troopId)
        {
            string normalizedTroopId = NormalizeTroopId(troopId);
            if (string.IsNullOrWhiteSpace(normalizedTroopId))
                return null;

            if (normalizedTroopId.EndsWith("_troop", StringComparison.Ordinal))
                return normalizedTroopId.Substring(0, normalizedTroopId.Length - "_troop".Length) + "_hero";

            if (normalizedTroopId.EndsWith("_hero", StringComparison.Ordinal))
                return normalizedTroopId.Substring(0, normalizedTroopId.Length - "_hero".Length) + "_troop";

            return null;
        }

        private static bool IsCoopHeroClassObject(object heroClass)
        {
            if (heroClass == null)
                return false;

            foreach (string memberName in new[] { "StringId", "Id" })
            {
                string text = GetMemberValue(heroClass, memberName) as string;
                if (!string.IsNullOrEmpty(text) && text.IndexOf("mp_coop_", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            foreach (string memberName in new[] { "Name", "ClassName" })
            {
                string text = GetMemberValue(heroClass, memberName)?.ToString();
                if (!string.IsNullOrEmpty(text) && text.IndexOf("coop", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            foreach (string memberName in new[] { "Character", "CharacterObject", "TroopCharacter", "HeroCharacter" })
            {
                object character = GetMemberValue(heroClass, memberName);
                if (character == null)
                    continue;

                string stringId = GetMemberValue(character, "StringId") as string ?? GetMemberValue(character, "Id") as string;
                if (!string.IsNullOrEmpty(stringId) && stringId.IndexOf("mp_coop_", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                string name = GetMemberValue(character, "Name")?.ToString();
                if (!string.IsNullOrEmpty(name) && name.IndexOf("coop", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return null;

            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    return property.GetValue(instance, null);
                }
                catch
                {
                }
            }

            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                try
                {
                    return field.GetValue(instance);
                }
                catch
                {
                }
            }

            return null;
        }

        private static void SetMemberValue(object instance, string memberName, object value)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return;

            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    property.SetValue(instance, value, null);
                    return;
                }
                catch
                {
                }
            }

            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                try
                {
                    field.SetValue(instance, value);
                }
                catch
                {
                }
            }
        }

        private static string BuildClassLoadoutFilterKey(Mission mission, object dataSource)
        {
            MissionPeer missionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
            string dataSourceIdentity = dataSource.GetType().FullName + "#" + dataSource.GetHashCode();
            string culture = missionPeer?.Culture?.StringId ?? "none";
            string missionMode = mission?.Mode.ToString() ?? "unknown";
            string selectionStamp = BuildClientCoopSelectionStamp(missionPeer);
            string allowedTroopIds = string.Join(",", ResolveAllowedCoopTroopIdsForClientPicker().OrderBy(id => id, StringComparer.Ordinal));
            return missionMode + "|" + culture + "|" + selectionStamp + "|" + allowedTroopIds + "|" + dataSourceIdentity;
        }

        private static string BuildTeamSelectCultureSyncKey(Mission mission, object dataSource)
        {
            MissionPeer missionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
            string culture = missionPeer?.Culture?.StringId ?? "none";
            string missionMode = mission?.Mode.ToString() ?? "unknown";
            string dataSourceIdentity = dataSource.GetType().FullName + "#" + RuntimeHelpers.GetHashCode(dataSource);
            string selectionStamp = BuildClientCoopSelectionStamp(missionPeer);
            return missionMode + "|" + culture + "|" + selectionStamp + "|" + FixedClientAttackerCultureId + "|" + FixedClientDefenderCultureId + "|" + dataSourceIdentity;
        }

        private static string BuildScoreboardCultureSyncKey(Mission mission, object dataSource)
        {
            MissionPeer missionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
            string culture = missionPeer?.Culture?.StringId ?? "none";
            string missionMode = mission?.Mode.ToString() ?? "unknown";
            string dataSourceIdentity = dataSource.GetType().FullName + "#" + RuntimeHelpers.GetHashCode(dataSource);
            string selectionStamp = BuildClientCoopSelectionStamp(missionPeer);
            return missionMode + "|" + culture + "|" + selectionStamp + "|scoreboard|" + FixedClientAttackerCultureId + "|" + FixedClientDefenderCultureId + "|" + dataSourceIdentity;
        }

        private static string BuildClientCoopSelectionStamp(MissionPeer missionPeer)
        {
            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge = CoopBattleSelectionBridgeFile.ReadCurrentSelection();
            string bridgedSide = selectionBridge?.Side ?? "null";
            string bridgedTroop = selectionBridge?.TroopOrEntryId ?? "null";
            int selectedTroopIndex = CoopMissionSpawnLogic.HasSideScopedRoster()
                ? -1
                : missionPeer?.SelectedTroopIndex ?? -1;
            return
                "requested=" + selectionState.RequestedSide +
                "|assigned=" + selectionState.Side +
                "|entry=" + (selectionState.EntryId ?? "null") +
                "|troop=" + (selectionState.TroopId ?? "null") +
                "|bridgeSide=" + bridgedSide +
                "|bridgeTroop=" + bridgedTroop +
                "|selectedIndex=" + selectedTroopIndex;
        }

    }

    /// <summary>
    /// Серверна логіка: заглушка для майбутнього спавну гравців (етап 3.4), логування peer/spawn для Етапу 3.3. Варіант A: читає battle_roster.json.
    /// </summary>
    public sealed class CoopMissionSpawnLogic : MissionLogic
    {
        // Direct/manual SpawnAgent-based player spawn is intentionally retired.
        // Proven-good path is coop authority + vanilla TDM/SpawningBehaviorBase spawn.
        private const bool EnableDirectCoopPlayerSpawnExperiment = false;
        // Possessing pre-materialized AI agents is currently experimental. It does not yet
        // reproduce the full vanilla player-spawn lifecycle (economy/finalize/control state),
        // so the stable runtime keeps battlefield armies materialized as AI-only context while
        // player spawn/respawn still goes through vanilla TDM/SpawningBehaviorBase.
        // 7b spike: try to hand a peer into an already materialized army body through
        // the vanilla bot-replacement lifecycle, while keeping vanilla player spawn as fallback.
        private const bool EnableMaterializedArmyPossessionExperiment = true;
        private const bool EnableCoopClassRestrictionSyncExperiment = false;
        private bool _hasLoggedStart;
        private const float ServerLogIntervalSeconds = 8f;
        private float _timeUntilNextPeerLog;
        private static Mission _lastDedicatedObservedMission;
        private static Mission _lastServerRuntimeInitializedMission;
        private static string _lastServerRuntimeInitializationSource = string.Empty;
        private static Mission _lastDiagnosticSpawnMission;
        private static Mission _lastDiagnosticOwnershipMission;
        private static bool _hasLoggedObjectManagerDiagnostics;
        private static bool _hasLoggedLoadedCharacterCatalog;
        private static bool _hasSpawnedDiagnosticAllowedAgent;
        private static bool _hasTransferredDiagnosticAllowedAgentToPeer;
        private static readonly HashSet<int> _spawnedCoopPeerIndices = new HashSet<int>();
        private static HashSet<string> _loggedForcedPreferredClassKeys = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<int, int> _lastBridgedSelectedTroopIndexByPeer = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> _lastBridgedPeerTeamIndexByPeer = new Dictionary<int, int>();
        private static readonly Dictionary<int, string> _appliedFixedMissionCultureByPeer = new Dictionary<int, string>();
        private static readonly Dictionary<FormationClass, bool> _appliedCoopClassAvailabilityStates = new Dictionary<FormationClass, bool>();
        private static readonly Dictionary<int, int> _lastAlignedControlledAgentIndexByPeer = new Dictionary<int, int>();
        private static readonly Dictionary<int, string> _materializedArmyEntryIdByAgentIndex = new Dictionary<int, string>();
        private static readonly Dictionary<int, BattleSideEnum> _materializedArmySideByAgentIndex = new Dictionary<int, BattleSideEnum>();
        private static readonly Dictionary<int, Agent> _materializedAgentInstanceByIndex = new Dictionary<int, Agent>();
        private static readonly HashSet<int> _exactNativeSnapshotOverlayAppliedAgentIndices = new HashSet<int>();
        private static readonly HashSet<string> _exactNativeSnapshotOverlayLoggedEntryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<int> _exactNativeClientVisualOverlayAppliedAgentIndices = new HashSet<int>();
        private static readonly HashSet<string> _exactNativeClientVisualOverlayLoggedEntryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, string> _exactNativeClientVisualOverlayEntryIdByAgentIndex = new Dictionary<int, string>();
        private static readonly Dictionary<int, Agent> _exactNativeClientVisualOverlayAgentByIndex = new Dictionary<int, Agent>();
        private static readonly Dictionary<int, PendingClientExactVisualOverlayState> _pendingExactNativeClientVisualOverlaysByAgentIndex =
            new Dictionary<int, PendingClientExactVisualOverlayState>();
        private static readonly Dictionary<string, Queue<string>> _exactNativeClientVisualOverlayEntryQueuesByAssignmentKey =
            new Dictionary<string, Queue<string>>(StringComparer.OrdinalIgnoreCase);
        private static string _exactNativeClientVisualOverlayQueueSnapshotKey = string.Empty;
        private static readonly HashSet<int> _battlePhaseHeldFormationKeys = new HashSet<int>();
        private static readonly HashSet<string> _loggedAutomatedMaterializedEntrySkipIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _loggedSuppressedMaterializedEquipmentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static Mission _lastBattlePhaseAiHoldMission;
        private static Mission _lastMaterializedArmyMission;
        private static Mission _lastClientBattleSnapshotRefreshMission;
        private static string _lastClientBattleSnapshotRefreshKey = string.Empty;
        private static bool? _lastAppliedBattlePhaseAiHold;
        private static CoopBattlePhase? _lastAppliedFormationHoldPhase;
        private static bool _hasMaterializedBattlefieldArmies;
        private static bool _hasLoggedImportedEquipmentAvailabilityDiagnostics;
        private static bool _hasLoggedHeroEquipmentResolutionAudit;
        private static bool _hasLoggedMaterializedEquipmentCoverageSummary;
        private static bool _hasTriggeredAuthoritativeBattleCompletion;
        private static string _authoritativeBattleWinnerSide = string.Empty;
        private static string _authoritativeBattleCompletionReason = string.Empty;
        private static string _lastLoggedBattleCompletionAuditKey = string.Empty;
        private static string _lastLoggedBattleStartReadinessAuditKey = string.Empty;
        private static DateTime _nextIncompleteBattleSnapshotRefreshUtc;
        private static DateTime _nextIncompleteBattleSnapshotLogUtc;
        private static DateTime _nextBattleMapStartupDeferLogUtc;
        private static DateTime _nextExactSceneMaterializationDeferLogUtc;
        private static DateTime _exactScenePostPossessionMaterializationResumeUtc;
        private static DateTime _nextInitialMaterializedArmyPulseUtc;
        private static DateTime _nextMaterializedArmyReinforcementPulseUtc;
        private static DateTime _nextExactSceneNativeBootstrapDeferLogUtc;
        private static DateTime _nextNativeBattleMapWarmupFallbackRefreshUtc;
        private static string _lastNativeBattleMapWarmupFallbackLogKey = string.Empty;
        private static readonly FieldInfo NativeWarmupCurrentStateStartTimeField =
            typeof(MultiplayerWarmupComponent).GetField("_currentStateStartTime", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo NativeWarmupStateField =
            typeof(MultiplayerWarmupComponent).GetField("_warmupState", BindingFlags.Instance | BindingFlags.NonPublic);
        private const float NativeBattleMapWarmupFallbackDurationSeconds = 1800f;
        private const float NativeBattleMapWarmupFutureStartOffsetSeconds = 1770f;
        private static Agent _diagnosticAllowedAgent;
        private const bool EnableFixedMissionCulturesExperiment = true;
        private const string SyntheticAllCampaignTroopsBattleId = "synthetic_all_campaign_troops";
        private const string SyntheticLiveHeroesBattleId = "synthetic_live_heroes";
        private const int MaxMaterializedArmyAgentsPerSide = 24;
        private const int MaxMaterializedAgentsPerEntry = 12;
        private const int FallbackMaterializedAgentsPerTroop = 4;
        private const double ExactScenePostPossessionMaterializationDelaySeconds = 3.0d;
        private const int InitialMaterializedArmySpawnPulseBudgetPerSide = 1;
        private const double InitialMaterializedArmyPulseIntervalSeconds = 0.35d;
        private const double MaterializedArmyReinforcementPulseIntervalSeconds = 0.75d;
        private const string FixedMissionAttackerCultureId = "empire";
        private const string FixedMissionDefenderCultureId = "vlandia";
        private static readonly string[] ImportedEquipmentProbeIds =
        {
            "aserai_chain_plate_armor_d",
            "pointed_skullcap_over_cloth_headwrap",
            "eastern_spear_3_t3",
            "aserai_sword_3_t3",
            "noble_horse_southern",
            "mail_and_plate_barding",
            "ladys_shoe",
            "large_adarga",
            "lordly_padded_mitten",
            "nomad_cap",
            "nordic_sloven",
            "northern_2hsword_t4",
            "northern_spear_4_t5",
            "peasant_hammer_1_t1",
            "peasant_maul_t1_2",
            "pointed_skullcap_over_mail_coif",
            "scale_shoulder_armor",
            "sling_braided",
            "small_heater_shield",
            "southern_spear_4_t3",
            "southern_throwing_axe_1_t4",
            "steel_druzhinnik_kite_shield",
            "storm_charger",
            "studded_adarga",
            "studded_leather_waistcoat",
            "sturgia_infantry_shield_a",
            "peasant_pitchfork_2_t1",
            "seax",
            "torn_bandit_clothes",
            "western_javelin_2_t3",
            "crossbow_c",
            "sumpter_horse",
            "tournament_arrows",
            "tribal_bow"
        };
        private static readonly IReadOnlyDictionary<string, string> ExactEquipmentCompatibilityAliasIds =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["pointed_skullcap_over_mail_coif"] = "cs_exact_pointed_skullcap_over_mail_coif",
                ["sling_braided"] = "cs_exact_sling_braided",
                ["lordly_padded_mitten"] = "cs_exact_lordly_padded_mitten",
                ["ladys_shoe"] = "cs_exact_ladys_shoe",
                ["steel_druzhinnik_kite_shield"] = "cs_exact_steel_druzhinnik_kite_shield",
                ["northern_spear_4_t5"] = "cs_exact_northern_spear_4_t5",
                ["nordic_sloven"] = "cs_exact_nordic_sloven",
                ["sturgia_infantry_shield_a"] = "cs_exact_sturgia_infantry_shield_a",
                ["storm_charger"] = "cs_exact_storm_charger",
                ["nomad_cap"] = "cs_exact_nomad_cap",
                ["studded_leather_waistcoat"] = "cs_exact_studded_leather_waistcoat",
                ["southern_spear_4_t3"] = "cs_exact_southern_spear_4_t3",
                ["large_adarga"] = "cs_exact_large_adarga",
                ["studded_adarga"] = "cs_exact_studded_adarga",
                ["southern_throwing_axe_1_t4"] = "cs_exact_southern_throwing_axe_1_t4",
                ["small_heater_shield"] = "cs_exact_small_heater_shield",
                ["scale_shoulder_armor"] = "cs_exact_scale_shoulder_armor",
                ["peasant_hammer_1_t1"] = "cs_exact_peasant_hammer_1_t1",
                ["peasant_maul_t1_2"] = "cs_exact_peasant_maul_t1_2",
                ["northern_2hsword_t4"] = "cs_exact_northern_2hsword_t4",
                ["peasant_pitchfork_2_t1"] = "mp_western_pitchfork_wood",
                ["seax"] = "mp_default_dagger",
                ["torn_bandit_clothes"] = "mp_vlandia_bandit_c",
                ["peasant_hammer_2_t1"] = "cs_exact_peasant_hammer_2_t1",
                ["western_javelin_2_t3"] = "cs_exact_western_javelin_2_t3",
                ["bolted_leather_strips"] = "cs_exact_bolted_leather_strips",
                ["bolt_c"] = "cs_exact_bolt_c",
                ["bolt_d"] = "cs_exact_bolt_d",
                ["bolt_e"] = "cs_exact_bolt_e",
                ["crossbow_c"] = "cs_exact_crossbow_c",
                ["nordic_shortbow"] = "cs_exact_nordic_shortbow",
                ["southern_spear_3_t3"] = "cs_exact_southern_spear_3_t3",
                ["southern_spear_3_t4"] = "cs_exact_southern_spear_3_t4",
                ["sumpter_horse"] = "cs_exact_sumpter_horse",
                ["tournament_arrows"] = "cs_exact_tournament_arrows",
                ["tribal_bow"] = "cs_exact_tribal_bow"
            };
        private static readonly IReadOnlyDictionary<string, string> ExactEquipmentCompatibilityStandInItemIds =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["pointed_skullcap_over_mail_coif"] = "mp_pointed_skullcap_over_laced_coif",
                ["sling_braided"] = "mp_throwing_stone",
                ["lordly_padded_mitten"] = "mp_guarded_padded_vambrace",
                ["ladys_shoe"] = "mp_strapped_shoes",
                ["steel_druzhinnik_kite_shield"] = "mp_ironrim_riders_kite_shield",
                ["northern_spear_4_t5"] = "sturgia_lance_1_t4",
                ["nordic_sloven"] = "mp_sturgian_lamellar_gambeson_heavy",
                ["sturgia_infantry_shield_a"] = "mp_western_riders_kite_shield",
                ["storm_charger"] = "mp_khuzait_horse",
                ["nomad_cap"] = "mp_fur_hat",
                ["studded_leather_waistcoat"] = "mp_stitched_leather_over_mail",
                ["southern_spear_4_t3"] = "eastern_spear_1_t2",
                ["large_adarga"] = "adarga",
                ["studded_adarga"] = "adarga",
                ["southern_throwing_axe_1_t4"] = "highland_throwing_axe_1_t2",
                ["small_heater_shield"] = "mp_heavy_heater_shield",
                ["scale_shoulder_armor"] = "fur_cloak_a",
                ["peasant_hammer_1_t1"] = "light_mace_t3",
                ["peasant_maul_t1_2"] = "light_mace_t3",
                ["northern_2hsword_t4"] = "kaskara_2hsword_t3",
                ["sturgia_infantry_shield_b"] = "mp_western_riders_kite_shield",
                ["sturgia_mace_1_t3"] = "light_mace_t3",
                ["sturgia_mace_2_t4"] = "khuzait_mace_2_t4",
                ["t3_aserai_horse"] = "t2_aserai_horse",
                ["varangian_bra_mail"] = "mp_mail_shoulders",
                ["vlandia_2haxe_1_t4"] = "battania_axe_2_t4",
                ["vlandia_infantry_shield_a"] = "mp_heavy_heater_shield",
                ["vlandia_lance_1_t3"] = "vlandia_lance_2_t4",
                ["vlandia_pike_1_t5"] = "vlandia_polearm_1_t5",
                ["western_2hsword_t3"] = "kaskara_2hsword_t3",
                ["western_2hsword_t4"] = "battania_2hsword_5_t4",
                ["western_spear_4_t3"] = "western_spear_3_t3",
                ["western_throwing_axe_1_t1"] = "highland_throwing_axe_1_t2",
                ["wooden_2hsword_t1"] = "kaskara_2hsword_t3",
                ["peasant_pitchfork_2_t1"] = "mp_western_pitchfork_wood",
                ["seax"] = "mp_default_dagger",
                ["torn_bandit_clothes"] = "mp_vlandia_bandit_c",
                ["western_javelin_2_t3"] = "mp_javelin",
                ["battania_polearm_1_t5"] = "mp_battania_long_infantry_spear",
                ["bolted_leather_strips"] = "fur_cloak_a",
                ["bolt_c"] = "bolt_a",
                ["bolt_d"] = "bolt_a",
                ["bolt_e"] = "bolt_a",
                ["crossbow_c"] = "crossbow_b",
                ["nordic_shortbow"] = "mp_nordic_short_bow",
                ["southern_spear_3_t3"] = "eastern_spear_1_t2",
                ["southern_spear_3_t4"] = "eastern_spear_1_t2",
                ["sumpter_horse"] = "mp_battania_pony",
                ["tournament_arrows"] = "mp_arrows_barbed",
                ["tribal_bow"] = "mp_hunting_bow"
                ,
                ["southern_moccasins"] = "mp_strapped_shoes"
            };
        private static readonly Dictionary<string, int> MaterializedEquipmentResolutionSourceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> MaterializedEquipmentMissCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> MaterializedEquipmentNormalizedFallbackCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> MaterializedCombatProfileApplyCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, MaterializedCombatProfileRuntimeState> _materializedCombatProfilesByAgentIndex = new Dictionary<int, MaterializedCombatProfileRuntimeState>();
        private static readonly Dictionary<string, MaterializedBattleResultEntryRuntimeState> _materializedBattleResultEntriesByEntryId = new Dictionary<string, MaterializedBattleResultEntryRuntimeState>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<int> _materializedBattleResultRemovedAgentIndices = new HashSet<int>();
        private static readonly Dictionary<int, MaterializedBattleResultLastHitDebugState> _materializedBattleResultLastHitDebugByVictimAgentIndex = new Dictionary<int, MaterializedBattleResultLastHitDebugState>();
        private static readonly List<CoopBattleResultBridgeFile.BattleResultCombatEventSnapshot> _materializedBattleResultCombatEvents = new List<CoopBattleResultBridgeFile.BattleResultCombatEventSnapshot>();
        private static Mission _lastMaterializedCombatProfileMission;
        private static Mission _lastMaterializedBattleResultMission;
        private static Mission _lastCombatProfileDrivenRefreshMission;
        private static float _lastCombatProfileDrivenRefreshMissionTime = -1f;
        private static bool _hasLoggedManualCombatProfileRefreshForMission;
        private static bool _hasWrittenBattleResultSnapshotForMission;
        private static int _droppedMaterializedBattleResultCombatEventCount;
        private static int _materializedBattleResultRawOnScoreHitCount;
        private static int _materializedBattleResultRawOnAgentRemovedCount;
        private static int _materializedBattleResultOnScoreHitCount;
        private static int _materializedBattleResultOnAgentRemovedCount;
        private static int _materializedBattleResultRemovalDebugLogCount;
        private static string _lastLoggedSelectableEntryUniverseKey;
        private static readonly List<string> _materializedBattleResultOnScoreHitSamples = new List<string>();
        private static readonly List<string> _materializedBattleResultOnAgentRemovedSamples = new List<string>();
        private static readonly List<string> _materializedBattleResultReconcileRemovedSamples = new List<string>();
        private static PropertyInfo _agentDrivenPropertiesProperty;
        private static bool _agentDrivenPropertiesPropertyResolved;
        private static bool _loggedMissingAgentDrivenPropertiesAccessor;
        private static MaterializedCombatProfileRuntimeState _drivenPropertyBaselineProfileContext;
        private static Agent _drivenPropertyBaselineAgentContext;
        private static bool _drivenPropertyBaselineMountContext;
        private const int MaxRecordedBattleResultCombatEvents = 16384;
        private const int MaxBattleResultDebugSampleCount = 24;
        private const int AgentFlagHorseMasterBit = 0x1000000;
        private const int AgentFlagMountedCrossbowmanBit = 0x2000000;
        private const int AgentFlagProjectileDeflectionBit = 0x4000000;

        private sealed class MaterializedCombatProfileRuntimeState
        {
            public string EntryId { get; set; }
            public string PartyId { get; set; }
            public string OriginalCharacterId { get; set; }
            public bool IsHero { get; set; }
            public int AttributeVigor { get; set; }
            public int AttributeControl { get; set; }
            public int AttributeEndurance { get; set; }
            public int SkillOneHanded { get; set; }
            public int SkillTwoHanded { get; set; }
            public int SkillPolearm { get; set; }
            public int SkillBow { get; set; }
            public int SkillCrossbow { get; set; }
            public int SkillThrowing { get; set; }
            public int SkillRiding { get; set; }
            public int SkillAthletics { get; set; }
            public int BaseHitPoints { get; set; }
            public List<string> PerkIds { get; set; } = new List<string>();
            public int PerkMeleeCount { get; set; }
            public int PerkRangedCount { get; set; }
            public int PerkAthleticsCount { get; set; }
            public int PerkRidingCount { get; set; }
            public float SideMorale { get; set; }
            public float PartyMorale { get; set; }
            public int LeaderLeadershipSkill { get; set; }
            public int LeaderTacticsSkill { get; set; }
            public int ScoutScoutingSkill { get; set; }
            public int QuartermasterStewardSkill { get; set; }
            public int EngineerEngineeringSkill { get; set; }
            public int SurgeonMedicineSkill { get; set; }
            public int PartyLeaderPerkCount { get; set; }
            public int ArmyCommanderPerkCount { get; set; }
            public int CaptainPerkCount { get; set; }
            public int ScoutRolePerkCount { get; set; }
            public int QuartermasterRolePerkCount { get; set; }
            public int EngineerRolePerkCount { get; set; }
            public int SurgeonRolePerkCount { get; set; }
            public bool CountedPerkRegistration;
            public bool CountedWeaponSkillAdjustment;
            public bool CountedWeaponInaccuracyAdjustment;
            public bool CountedVigorAttributeAdjustment;
            public bool CountedControlAttributeAdjustment;
            public bool CountedEnduranceAttributeAdjustment;
            public bool CountedRidingAttributeAdjustment;
            public bool CountedMountedPenaltyAdjustment;
            public bool CountedMountStatAdjustment;
            public bool CountedPerkMeleeAdjustment;
            public bool CountedPerkRangedAdjustment;
            public bool CountedPerkAthleticsAdjustment;
            public bool CountedPerkRidingAdjustment;
            public bool CountedPerkMountAdjustment;
            public bool CountedPerkExactPersonalAdjustment;
            public bool CountedPartyMoraleAdjustment;
            public bool CountedPartyTacticsAdjustment;
            public bool CountedPartyCaptainAdjustment;
            public bool CountedPartyScoutAdjustment;
            public bool CountedPartyQuartermasterAdjustment;
            public bool CountedPartyEngineerAdjustment;
            public bool CountedPartySurgeonAdjustment;
            public string HumanDrivenPropertyBaselineSignature { get; set; }
            public string MountDrivenPropertyBaselineSignature { get; set; }
            public Dictionary<DrivenProperty, float> HumanDrivenPropertyBaselines { get; } = new Dictionary<DrivenProperty, float>();
            public Dictionary<DrivenProperty, float> MountDrivenPropertyBaselines { get; } = new Dictionary<DrivenProperty, float>();
            public Dictionary<DrivenProperty, float> HumanDrivenPropertyAccumulatedScales { get; } = new Dictionary<DrivenProperty, float>();
            public Dictionary<DrivenProperty, float> MountDrivenPropertyAccumulatedScales { get; } = new Dictionary<DrivenProperty, float>();
        }

        private sealed class MaterializedBattleResultEntryRuntimeState
        {
            public string EntryId { get; set; }
            public string SideId { get; set; }
            public string PartyId { get; set; }
            public string CharacterId { get; set; }
            public string OriginalCharacterId { get; set; }
            public string SpawnTemplateId { get; set; }
            public string TroopName { get; set; }
            public string HeroId { get; set; }
            public string HeroRole { get; set; }
            public bool IsHero { get; set; }
            public int SnapshotCount { get; set; }
            public int SnapshotWoundedCount { get; set; }
            public int MaterializedSpawnCount { get; set; }
            public int ActiveCount { get; set; }
            public int RemovedCount { get; set; }
            public int KilledCount { get; set; }
            public int UnconsciousCount { get; set; }
            public int RoutedCount { get; set; }
            public int OtherRemovedCount { get; set; }
            public int ScoreHitCount { get; set; }
            public int HitsTakenCount { get; set; }
            public int FatalHitCount { get; set; }
            public int KillsInflictedCount { get; set; }
            public int UnconsciousInflictedCount { get; set; }
            public int RoutedInflictedCount { get; set; }
            public float DamageDealt { get; set; }
            public float DamageTaken { get; set; }
        }

        private sealed class MaterializedBattleResultLastHitDebugState
        {
            public int AttackerAgentIndex { get; set; }
            public string DamageType { get; set; }
            public string WeaponClass { get; set; }
            public string WeaponFlags { get; set; }
            public bool CanKillEvenIfBlunt { get; set; }
            public bool IsHorseCharge { get; set; }
            public bool IsAlternativeAttack { get; set; }
            public bool IsFallDamage { get; set; }
            public bool IsMissile { get; set; }
            public bool IsBlocked { get; set; }
            public bool IsSiegeEngineHit { get; set; }
            public string VictimBodyPart { get; set; }
            public bool IsHeadShot { get; set; }
            public float BlowBaseMagnitude { get; set; }
            public float CollisionBaseMagnitude { get; set; }
            public int AbsorbedByArmor { get; set; }
            public float MovementSpeedDamageModifier { get; set; }
            public float MissileVelocity { get; set; }
            public float MissileStartSpeed { get; set; }
            public float MissileTotalDamage { get; set; }
            public float Damage { get; set; }
            public float MissionTime { get; set; }
        }

        private sealed class MaterializedBattleResultReconciledCounts
        {
            public int ActiveCount { get; set; }
            public int KilledCount { get; set; }
            public int UnconsciousCount { get; set; }
            public int RoutedCount { get; set; }
            public int OtherRemovedCount { get; set; }
            public float ObservedDamageTaken { get; set; }
        }

        private sealed class MaterializedArmyReinforcementCandidate
        {
            public RosterEntryState EntryState { get; set; }
            public BasicCharacterObject Troop { get; set; }
            public string ResolvedTroopSource { get; set; }
            public int AvailableCount { get; set; }
            public int ActiveCount { get; set; }
            public int SpawnedCount { get; set; }
            public int TotalRemainingCount { get; set; }
            public int ConcurrentRemainingCapacity { get; set; }
        }

        private sealed class PendingClientExactVisualOverlayState
        {
            public string EntryId { get; set; }
            public DateTime CreatedUtc { get; set; }
            public DateTime NotBeforeUtc { get; set; }
            public int RetryCount { get; set; }
            public string Source { get; set; }
            public bool IncludeWeaponsForRefresh { get; set; }
        }

        private static readonly FormationClass[] RestrictableFormationClasses =
        {
            FormationClass.Infantry,
            FormationClass.Ranged,
            FormationClass.Cavalry,
            FormationClass.HorseArcher
        };

        public static void ResetClientMissionRuntimeState(string source)
        {
            if (GameNetwork.IsServer)
                return;

            _materializedArmyEntryIdByAgentIndex.Clear();
            _materializedArmySideByAgentIndex.Clear();
            _materializedAgentInstanceByIndex.Clear();
            _exactNativeSnapshotOverlayAppliedAgentIndices.Clear();
            _exactNativeSnapshotOverlayLoggedEntryIds.Clear();
            ResetClientExactCampaignVisualOverlayAssignmentState(source);
            _battlePhaseHeldFormationKeys.Clear();
            _loggedAutomatedMaterializedEntrySkipIds.Clear();
            _loggedSuppressedMaterializedEquipmentKeys.Clear();
            _lastBattlePhaseAiHoldMission = null;
            _lastMaterializedArmyMission = null;
            _lastClientBattleSnapshotRefreshMission = null;
            _lastClientBattleSnapshotRefreshKey = string.Empty;
            _lastAppliedBattlePhaseAiHold = null;
            _lastAppliedFormationHoldPhase = null;
            _hasMaterializedBattlefieldArmies = false;
            ModLogger.Info(
                "CoopMissionSpawnLogic: reset client mission runtime state. " +
                "Source=" + (source ?? "unknown"));
        }

        private static void ResetClientExactCampaignVisualOverlayAssignmentState(string source)
        {
            _exactNativeClientVisualOverlayAppliedAgentIndices.Clear();
            _exactNativeClientVisualOverlayLoggedEntryIds.Clear();
            _exactNativeClientVisualOverlayEntryIdByAgentIndex.Clear();
            _exactNativeClientVisualOverlayAgentByIndex.Clear();
            _pendingExactNativeClientVisualOverlaysByAgentIndex.Clear();
            _exactNativeClientVisualOverlayEntryQueuesByAssignmentKey.Clear();
            _exactNativeClientVisualOverlayQueueSnapshotKey = string.Empty;
            ModLogger.Info(
                "CoopMissionSpawnLogic: reset client exact visual overlay assignment state. " +
                "Source=" + (source ?? "unknown"));
        }

        /// <summary>Після читання файлу — список troop ID з кампанії для обмеження вибору юнітів клієнтами (варіант A).</summary>
        public static List<string> CampaignRosterTroopIds { get; private set; } = new List<string>();
        public static List<string> ControlTroopIds { get; private set; } = new List<string>();
        public static List<string> AllowedControlTroopIds { get; private set; } = new List<string>();
        public static List<string> AllowedControlEntryIds { get; private set; } = new List<string>();
        public static List<BasicCharacterObject> AllowedControlCharacters { get; private set; } = new List<BasicCharacterObject>();
        public static Dictionary<BattleSideEnum, List<string>> AllowedControlTroopIdsBySide { get; private set; } = new Dictionary<BattleSideEnum, List<string>>();
        public static Dictionary<BattleSideEnum, List<string>> AllowedControlEntryIdsBySide { get; private set; } = new Dictionary<BattleSideEnum, List<string>>();
        public static Dictionary<BattleSideEnum, List<BattleRosterEntryProjectionState>> AllowedControlEntriesBySide { get; private set; } = new Dictionary<BattleSideEnum, List<BattleRosterEntryProjectionState>>();
        public static Dictionary<BattleSideEnum, List<RosterEntryState>> AllowedControlEntryStatesBySide { get; private set; } = new Dictionary<BattleSideEnum, List<RosterEntryState>>();
        public static Dictionary<BattleSideEnum, List<BasicCharacterObject>> AllowedControlCharactersBySide { get; private set; } = new Dictionary<BattleSideEnum, List<BasicCharacterObject>>();
        /// <summary>Перший дозволений troop ID із нормалізованого roster. Поки що лише для контрольованої серверної фіксації.</summary>
        public static string SelectedAllowedTroopId { get; private set; }
        public static string SelectedAllowedEntryId { get; private set; }
        /// <summary>Резолвлений character object для першого дозволеного troop ID. Поки тільки для діагностики.</summary>
        public static BasicCharacterObject SelectedAllowedCharacter { get; private set; }
        private const string BattleMapStartupBuildMarker = "battle-map-startup-deferral-v1";

        public override void AfterStart()
        {
            ModLogger.Info("CoopMissionSpawnLogic AfterStart ENTER");
            base.AfterStart();
            Mission mission = Mission;
            if (mission == null) return;
            if (_hasLoggedStart) return;
            _hasLoggedStart = true;

            if (MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
            {
                TryInitializeServerMissionRuntimeState(
                    mission,
                    "CoopMissionSpawnLogic.AfterStart battle-map deferred",
                    forceReinitialize: false);
                ExactCampaignRuntimeItemRegistry.EnsureLoadedFromState(
                    BattleSnapshotRuntimeState.GetState(),
                    "CoopMissionSpawnLogic.AfterStart battle-map deferred");
                LogImportedEquipmentAvailabilityDiagnostics();
                LogHeroEquipmentResolutionAudit();
                ModLogger.Info("CoopMissionSpawnLogic: battle-map AfterStart using minimal deferred startup path. BuildMarker=" + BattleMapStartupBuildMarker);
                _timeUntilNextPeerLog = ServerLogIntervalSeconds;
                ModLogger.Info("CoopMissionSpawnLogic AfterStart EXIT (battle-map deferred). BuildMarker=" + BattleMapStartupBuildMarker);
                return;
            }

            TryInitializeServerMissionRuntimeState(mission, "CoopMissionSpawnLogic.AfterStart", forceReinitialize: false);
            ExactCampaignRuntimeItemRegistry.EnsureLoadedFromState(
                BattleSnapshotRuntimeState.GetState(),
                "CoopMissionSpawnLogic.AfterStart");

            ModLogger.Info("CoopMissionSpawnLogic: server mission entered.");
            try
            {
                string mode = mission.Mode.ToString();
                ModLogger.Info("CoopMissionSpawnLogic: current mission mode = " + mode);
            }
            catch (Exception ex) { ModLogger.Info("CoopMissionSpawnLogic: mission mode log failed: " + ex.Message); }

            if (GameNetwork.IsSessionActive)
            {
                int peerCount = GameNetwork.NetworkPeerCount;
                ModLogger.Info("CoopMissionSpawnLogic: peer count (after start) = " + peerCount);
            }

            ModLogger.Info("CoopMissionSpawnLogic: server mission started. Campaign roster: " + CampaignRosterTroopIds.Count + " normalized troop IDs.");
            if (!string.IsNullOrEmpty(SelectedAllowedTroopId))
                ModLogger.Info("CoopMissionSpawnLogic: selected allowed troop id = " + SelectedAllowedTroopId);
            else
                ModLogger.Info("CoopMissionSpawnLogic: no allowed troop id available yet (roster empty).");
            LogAllowedCharacterResolution();
            LogImportedEquipmentAvailabilityDiagnostics();
            LogHeroEquipmentResolutionAudit();
            CoopBattlePhaseRuntimeState.AdvanceToAtLeast(CoopBattlePhase.SideSelection, "CoopMissionSpawnLogic.AfterStart", mission);

            _timeUntilNextPeerLog = ServerLogIntervalSeconds;
            ModLogger.Info("CoopMissionSpawnLogic AfterStart EXIT.");
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (!_hasLoggedStart) return;
            _timeUntilNextPeerLog -= dt;
            if (_timeUntilNextPeerLog <= 0f)
            {
                _timeUntilNextPeerLog = ServerLogIntervalSeconds;
                try
                {
                    if (GameNetwork.IsSessionActive)
                        ModLogger.Info("CoopMissionSpawnLogic: peer count = " + GameNetwork.NetworkPeerCount);
                }
                catch (Exception ex) { ModLogger.Info("CoopMissionSpawnLogic: peer log failed: " + ex.Message); }
            }

            if (Mission.GetMissionBehavior<MissionMultiplayerCoopBattle>() != null)
                return;

            RunCoopBattleSpawnOwnerTick(Mission, "CoopMissionSpawnLogic.OnMissionTick fallback");
            RunCoopBattlePhaseOwnerTick(Mission, "CoopMissionSpawnLogic.OnMissionTick fallback");
        }

        public override void OnScoreHit(
            Agent affectedAgent,
            Agent affectorAgent,
            WeaponComponentData attackerWeapon,
            bool isBlocked,
            bool isSiegeEngineHit,
            in Blow blow,
            in AttackCollisionData collisionData,
            float damagedHp,
            float hitDistance,
            float shotDifficulty)
        {
            float authoritativeDamagedHp = damagedHp;
            TryApplyAuthoritativeExactPersonalRangedDamageBonusOnScoreHit(
                affectedAgent,
                affectorAgent,
                attackerWeapon,
                isBlocked,
                isSiegeEngineHit,
                blow,
                collisionData,
                ref authoritativeDamagedHp);

            _materializedBattleResultRawOnScoreHitCount++;
            TryTrackMaterializedBattleResultScoreHit(
                affectedAgent,
                affectorAgent,
                attackerWeapon,
                isBlocked,
                isSiegeEngineHit,
                blow,
                collisionData,
                authoritativeDamagedHp,
                hitDistance,
                shotDifficulty);
            base.OnScoreHit(affectedAgent, affectorAgent, attackerWeapon, isBlocked, isSiegeEngineHit, blow, collisionData, authoritativeDamagedHp, hitDistance, shotDifficulty);
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow blow)
        {
            _materializedBattleResultRawOnAgentRemovedCount++;
            TrySyncExactCampaignNativeOriginRemoval(Mission, affectedAgent, affectorAgent, agentState);
            TryTrackMaterializedBattleResultRemoval(affectedAgent, affectorAgent, agentState);
            TryLogMaterializedBattleResultRemovalDebug(affectedAgent, affectorAgent, agentState, blow);
            TryCompleteBattleIfResolved(Mission, "server behavior agent-removed");
            base.OnAgentRemoved(affectedAgent, affectorAgent, agentState, blow);
        }

        public override void OnMissionResultReady(MissionResult missionResult)
        {
            CoopBattlePhaseRuntimeState.SetPhase(CoopBattlePhase.BattleEnded, "CoopMissionSpawnLogic.OnMissionResultReady", Mission, allowRegression: true);
            TryWriteBattleResultSnapshot(Mission, "server behavior mission-result-ready");
            ModLogger.Info(
                "CoopMissionSpawnLogic: mission result ready. " +
                "HasResult=" + (missionResult != null) +
                " Mission=" + (Mission?.SceneName ?? "null") + ".");
            base.OnMissionResultReady(missionResult);
        }

        protected override void OnEndMission()
        {
            CoopBattlePhaseRuntimeState.SetPhase(CoopBattlePhase.BattleEnded, "CoopMissionSpawnLogic.OnEndMission", Mission, allowRegression: true);
            TryWriteBattleResultSnapshot(Mission, "server behavior end-mission");
            CoopBattleSelectionIntentState.Reset();
            CoopBattleSelectionRequestState.Reset();
            CoopBattleSpawnIntentState.Reset();
            CoopBattleSpawnRequestState.Reset();
            CoopBattleSpawnRuntimeState.Reset();
            CoopBattlePeerLifecycleRuntimeState.Reset();
            _lastAlignedControlledAgentIndexByPeer.Clear();
            _materializedArmyEntryIdByAgentIndex.Clear();
            _materializedArmySideByAgentIndex.Clear();
            _materializedAgentInstanceByIndex.Clear();
            _exactNativeSnapshotOverlayAppliedAgentIndices.Clear();
            _exactNativeSnapshotOverlayLoggedEntryIds.Clear();
            _exactNativeClientVisualOverlayAppliedAgentIndices.Clear();
            _exactNativeClientVisualOverlayLoggedEntryIds.Clear();
            _exactNativeClientVisualOverlayEntryIdByAgentIndex.Clear();
            _pendingExactNativeClientVisualOverlaysByAgentIndex.Clear();
            _exactNativeClientVisualOverlayEntryQueuesByAssignmentKey.Clear();
            _battlePhaseHeldFormationKeys.Clear();
            _loggedAutomatedMaterializedEntrySkipIds.Clear();
            _loggedSuppressedMaterializedEquipmentKeys.Clear();
            _lastBattlePhaseAiHoldMission = null;
            _lastAppliedBattlePhaseAiHold = null;
            _lastAppliedFormationHoldPhase = null;
            _lastMaterializedArmyMission = null;
            ResetMaterializedCombatProfileRuntimeState();
            ResetMaterializedBattleResultRuntimeState();
            _hasMaterializedBattlefieldArmies = false;
            _lastLoggedBattleCompletionAuditKey = string.Empty;
            _lastLoggedBattleStartReadinessAuditKey = string.Empty;
            _nextIncompleteBattleSnapshotRefreshUtc = DateTime.MinValue;
            _nextIncompleteBattleSnapshotLogUtc = DateTime.MinValue;
            _nextBattleMapStartupDeferLogUtc = DateTime.MinValue;
            _exactScenePostPossessionMaterializationResumeUtc = DateTime.MinValue;
            _nextInitialMaterializedArmyPulseUtc = DateTime.MinValue;
            _nextMaterializedArmyReinforcementPulseUtc = DateTime.MinValue;
            _nextNativeBattleMapWarmupFallbackRefreshUtc = DateTime.MinValue;
            _lastNativeBattleMapWarmupFallbackLogKey = string.Empty;
            if (ReferenceEquals(_lastServerRuntimeInitializedMission, Mission))
            {
                _lastServerRuntimeInitializedMission = null;
                _lastServerRuntimeInitializationSource = string.Empty;
            }
            if (ReferenceEquals(_lastDedicatedObservedMission, Mission))
                _lastDedicatedObservedMission = null;
            TryWriteEntryStatusSnapshot(Mission, "server behavior end-mission");
            base.OnEndMission();
        }

        private static List<string> NormalizeRosterTroopIds(List<string> troopIds)
        {
            if (troopIds == null || troopIds.Count == 0)
                return new List<string>();

            return troopIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static BasicCharacterObject ResolveAllowedCharacter(string troopId)
        {
            if (string.IsNullOrWhiteSpace(troopId))
                return null;

            try
            {
                BasicCharacterObject direct = MBObjectManager.Instance.GetObject<BasicCharacterObject>(troopId);
                if (direct != null)
                    return direct;

                LogClosestLoadedCharacterIds(troopId);

                string guaranteedFallbackId = TryResolveGuaranteedMissionSafeTroopId(troopId);
                if (!string.IsNullOrWhiteSpace(guaranteedFallbackId) && !string.Equals(guaranteedFallbackId, troopId, StringComparison.Ordinal))
                {
                    ModLogger.Info("CoopMissionSpawnLogic: direct lookup failed for '" + troopId + "'. Trying guaranteed mission-safe fallback '" + guaranteedFallbackId + "'.");
                    BasicCharacterObject fallback = MBObjectManager.Instance.GetObject<BasicCharacterObject>(guaranteedFallbackId);
                    if (fallback != null)
                        return fallback;
                }

                return null;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: character lookup failed for troop id '" + troopId + "': " + ex.Message);
                return null;
            }
        }

        private static string TryResolveGuaranteedMissionSafeTroopId(string troopId)
        {
            if (string.IsNullOrWhiteSpace(troopId))
                return null;

            string normalized = troopId.Trim().ToLowerInvariant();
            if (string.Equals(normalized, "mp_coop_light_cavalry_sturgia_troop", StringComparison.Ordinal))
                return "mp_light_cavalry_sturgia_troop";
            if (string.Equals(normalized, "mp_coop_light_cavalry_battania_troop", StringComparison.Ordinal))
                return "mp_light_cavalry_battania_troop";
            if (string.Equals(normalized, "mp_coop_light_cavalry_vlandia_troop", StringComparison.Ordinal))
                return "mp_light_cavalry_vlandia_troop";
            if (string.Equals(normalized, "mp_coop_light_cavalry_empire_troop", StringComparison.Ordinal))
                return "mp_light_cavalry_empire_troop";
            if (string.Equals(normalized, "mp_coop_light_cavalry_aserai_troop", StringComparison.Ordinal))
                return "mp_light_cavalry_aserai_troop";
            if (string.Equals(normalized, "mp_coop_light_cavalry_khuzait_troop", StringComparison.Ordinal))
                return "mp_light_cavalry_khuzait_troop";
            if (string.Equals(normalized, "mp_light_infantry_empire_troop", StringComparison.Ordinal))
                return "mp_coop_light_infantry_empire_troop";
            if (string.Equals(normalized, "mp_light_infantry_empire_hero", StringComparison.Ordinal))
                return "mp_coop_light_infantry_empire_hero";
            if (string.Equals(normalized, "mp_coop_light_infantry_empire_troop", StringComparison.Ordinal))
                return "mp_light_infantry_empire_troop";
            if (string.Equals(normalized, "mp_coop_heavy_infantry_empire_troop", StringComparison.Ordinal))
                return "mp_heavy_infantry_empire_troop";
            if (string.Equals(normalized, "mp_coop_heavy_infantry_vlandia_troop", StringComparison.Ordinal))
                return "mp_heavy_infantry_vlandia_troop";
            if (normalized.StartsWith("imperial_"))
                return "mp_coop_light_infantry_empire_troop";
            if (normalized.StartsWith("sturgian_"))
                return "imperial_infantryman";
            if (normalized.StartsWith("battanian_"))
                return "imperial_infantryman";
            if (normalized.StartsWith("aserai_"))
                return "imperial_infantryman";
            if (normalized.StartsWith("khuzait_"))
                return "imperial_infantryman";
            if (normalized.StartsWith("vlandian_"))
                return "imperial_infantryman";

            return "imperial_infantryman";
        }

        private static void LogClosestLoadedCharacterIds(string troopId)
        {
            try
            {
                IEnumerable<BasicCharacterObject> loadedCharacters = TryGetLoadedCharacters();
                if (loadedCharacters == null)
                {
                    LogObjectManagerDiagnostics("loaded character list is null for troop id '" + troopId + "'");
                    return;
                }

                List<BasicCharacterObject> loadedCharacterList = loadedCharacters
                    .Where(character => character != null)
                    .ToList();

                ModLogger.Info("CoopMissionSpawnLogic: loaded BasicCharacterObject count = " + loadedCharacterList.Count + ".");
                LogLoadedCharacterCatalog(loadedCharacterList);

                string prefix = troopId;
                int underscoreIndex = troopId.IndexOf('_');
                if (underscoreIndex > 0)
                    prefix = troopId.Substring(0, underscoreIndex + 1);

                List<string> closestIds = loadedCharacterList
                    .Where(character => character != null && !string.IsNullOrWhiteSpace(character.StringId))
                    .Select(character => character.StringId)
                    .Where(id =>
                        id.IndexOf(troopId, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        troopId.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.Ordinal)
                    .Take(8)
                    .ToList();

                if (closestIds.Count > 0)
                    ModLogger.Info("CoopMissionSpawnLogic: closest loaded BasicCharacterObject ids for '" + troopId + "' = [" + string.Join(", ", closestIds) + "].");
                else
                    ModLogger.Info("CoopMissionSpawnLogic: no close loaded BasicCharacterObject ids found for '" + troopId + "'.");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: closest character id dump failed for '" + troopId + "': " + ex.Message);
            }
        }

        private static IEnumerable<BasicCharacterObject> TryGetLoadedCharacters()
        {
            try
            {
                var methods = typeof(MBObjectManager).GetMethods();
                var genericMethod = methods.FirstOrDefault(method =>
                    method.Name == "GetObjectTypeList" &&
                    method.IsGenericMethodDefinition &&
                    method.GetParameters().Length == 0);

                if (genericMethod == null)
                {
                    LogObjectManagerDiagnostics("GetObjectTypeList<T>() method was not found");
                    return null;
                }

                object result = genericMethod.MakeGenericMethod(typeof(BasicCharacterObject)).Invoke(MBObjectManager.Instance, null);
                if (result is IEnumerable<BasicCharacterObject> typedEnumerable)
                    return typedEnumerable;

                if (result is System.Collections.IEnumerable nonGenericEnumerable)
                    return nonGenericEnumerable.Cast<object>().OfType<BasicCharacterObject>().ToList();

                LogObjectManagerDiagnostics("GetObjectTypeList<T>() returned non-enumerable result of type '" + (result?.GetType().FullName ?? "null") + "'");
                return null;
            }
            catch (Exception ex)
            {
                LogObjectManagerDiagnostics("TryGetLoadedCharacters exception: " + ex.Message);
                return null;
            }
        }

        private static void LogObjectManagerDiagnostics(string reason)
        {
            if (_hasLoggedObjectManagerDiagnostics)
                return;

            _hasLoggedObjectManagerDiagnostics = true;

            try
            {
                var interestingMethods = typeof(MBObjectManager).GetMethods()
                    .Where(method => method.Name.IndexOf("Object", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                     method.Name.IndexOf("List", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(method =>
                    {
                        string parameters = string.Join(", ", method.GetParameters().Select(parameter => parameter.ParameterType.Name));
                        return method.Name + "(" + parameters + ")";
                    })
                    .Distinct()
                    .ToList();

                ModLogger.Info("CoopMissionSpawnLogic: MBObjectManager diagnostics reason = " + reason + ".");
                ModLogger.Info("CoopMissionSpawnLogic: MBObjectManager list-like methods = [" + string.Join(", ", interestingMethods) + "].");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: MBObjectManager diagnostics failed: " + ex.Message);
            }
        }

        private static void LogLoadedCharacterCatalog(List<BasicCharacterObject> loadedCharacterList)
        {
            if (_hasLoggedLoadedCharacterCatalog || loadedCharacterList == null)
                return;

            _hasLoggedLoadedCharacterCatalog = true;

            try
            {
                List<string> catalog = loadedCharacterList
                    .Where(character => !string.IsNullOrWhiteSpace(character.StringId))
                    .Select(character => character.StringId)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .Take(120)
                    .ToList();

                ModLogger.Info("CoopMissionSpawnLogic: loaded BasicCharacterObject catalog sample = [" + string.Join(", ", catalog) + "].");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: loaded character catalog dump failed: " + ex.Message);
            }
        }

        private static void LogAllowedCharacterResolution()
        {
            if (string.IsNullOrWhiteSpace(SelectedAllowedTroopId))
                return;

            if (SelectedAllowedCharacter == null)
            {
                ModLogger.Info("CoopMissionSpawnLogic: selected allowed troop id could not be resolved to BasicCharacterObject.");
                return;
            }

            try
            {
                string name = SelectedAllowedCharacter.Name?.ToString() ?? SelectedAllowedCharacter.StringId;
                ModLogger.Info(
                    "CoopMissionSpawnLogic: selected allowed character resolved. " +
                    "Id=" + SelectedAllowedCharacter.StringId +
                    " Name=" + name +
                    " Level=" + SelectedAllowedCharacter.Level +
                    " Mounted=" + SelectedAllowedCharacter.IsMounted);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: allowed character log failed: " + ex.Message);
            }
        }

        public static void TryRunDedicatedMissionObserver(Mission mission)
        {
            if (mission == null || !GameNetwork.IsServer)
                return;

            bool isNewMission = !ReferenceEquals(_lastDedicatedObservedMission, mission);
            if (isNewMission)
            {
                _lastDedicatedObservedMission = mission;
                ModLogger.Info("CoopMissionSpawnLogic: dedicated observer detected active mission.");

                try
                {
                    string mode = mission.Mode.ToString();
                    ModLogger.Info("CoopMissionSpawnLogic: dedicated observer mission mode = " + mode);
                }
                catch (Exception ex)
                {
                    ModLogger.Info("CoopMissionSpawnLogic: dedicated observer mission mode log failed: " + ex.Message);
                }

                if (GameNetwork.IsSessionActive)
                    ModLogger.Info("CoopMissionSpawnLogic: dedicated observer peer count = " + GameNetwork.NetworkPeerCount);
            }

            if (TryEnsureDedicatedObserverMissionBehavior(mission, isNewMission))
                return;

            bool initializedByObserver = TryInitializeServerMissionRuntimeState(
                mission,
                "CoopMissionSpawnLogic.TryRunDedicatedMissionObserver fallback",
                forceReinitialize: false);
            if (initializedByObserver)
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: dedicated observer applied fallback runtime init. " +
                    "Mission=" + (mission.SceneName ?? "null") +
                    " Source=" + (_lastServerRuntimeInitializationSource ?? "unknown"));
                LogImportedEquipmentAvailabilityDiagnostics();
                LogHeroEquipmentResolutionAudit();
                CoopBattlePhaseRuntimeState.AdvanceToAtLeast(CoopBattlePhase.SideSelection, "dedicated observer mission detected", mission);
            }

            if (mission.GetMissionBehavior<MissionMultiplayerCoopBattle>() != null)
                return;

            RunSharedServerBattleLifecycleTick(
                mission,
                "dedicated observer",
                applyPreMaterializationPeerNudges: true);
            RunVanillaSpawnBridgeTick(
                mission,
                "dedicated observer");
            RunCoopBattlePhaseOwnerTick(mission, "dedicated observer");
        }

        private static bool TryEnsureDedicatedObserverMissionBehavior(Mission mission, bool isNewMission)
        {
            if (mission == null || !GameNetwork.IsServer)
                return false;

            CoopMissionSpawnLogic existingBehavior = mission.GetMissionBehavior<CoopMissionSpawnLogic>();
            if (existingBehavior != null)
                return true;

            try
            {
                var attachedBehavior = new CoopMissionSpawnLogic();
                mission.AddMissionBehavior(attachedBehavior);
                attachedBehavior.OnBehaviorInitialize();
                attachedBehavior.AfterStart();
                ModLogger.Info(
                    "CoopMissionSpawnLogic: dedicated observer attached CoopMissionSpawnLogic mission behavior to active mission. " +
                    "Mission=" + (mission.SceneName ?? "null") +
                    " IsNewMission=" + isNewMission + ".");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: dedicated observer failed to attach mission behavior: " + ex.Message);
                return false;
            }
        }

        private static bool TryInitializeServerMissionRuntimeState(Mission mission, string source, bool forceReinitialize)
        {
            if (mission == null || !GameNetwork.IsServer)
                return false;

            if (!forceReinitialize && ReferenceEquals(_lastServerRuntimeInitializedMission, mission))
                return false;

            _lastServerRuntimeInitializedMission = mission;
            _lastServerRuntimeInitializationSource = source ?? "unknown";
            _lastDiagnosticSpawnMission = null;
            _lastDiagnosticOwnershipMission = null;
            _hasSpawnedDiagnosticAllowedAgent = false;
            _hasTransferredDiagnosticAllowedAgentToPeer = false;
            _spawnedCoopPeerIndices.Clear();
            _loggedForcedPreferredClassKeys.Clear();
            _lastBridgedSelectedTroopIndexByPeer.Clear();
            _lastBridgedPeerTeamIndexByPeer.Clear();
            _appliedFixedMissionCultureByPeer.Clear();
            _appliedCoopClassAvailabilityStates.Clear();
            _lastAlignedControlledAgentIndexByPeer.Clear();
            _materializedArmyEntryIdByAgentIndex.Clear();
            _materializedArmySideByAgentIndex.Clear();
            _materializedAgentInstanceByIndex.Clear();
            _exactNativeSnapshotOverlayAppliedAgentIndices.Clear();
            _exactNativeSnapshotOverlayLoggedEntryIds.Clear();
            _exactNativeClientVisualOverlayAppliedAgentIndices.Clear();
            _exactNativeClientVisualOverlayLoggedEntryIds.Clear();
            _exactNativeClientVisualOverlayEntryIdByAgentIndex.Clear();
            _pendingExactNativeClientVisualOverlaysByAgentIndex.Clear();
            _exactNativeClientVisualOverlayEntryQueuesByAssignmentKey.Clear();
            _exactNativeClientVisualOverlayQueueSnapshotKey = string.Empty;
            _loggedAutomatedMaterializedEntrySkipIds.Clear();
            _loggedSuppressedMaterializedEquipmentKeys.Clear();
            _battlePhaseHeldFormationKeys.Clear();
            _lastBattlePhaseAiHoldMission = null;
            _lastAppliedBattlePhaseAiHold = null;
            _lastAppliedFormationHoldPhase = null;
            _lastMaterializedArmyMission = null;
            ResetMaterializedCombatProfileRuntimeState();
            ResetMaterializedBattleResultRuntimeState();
            _hasMaterializedBattlefieldArmies = false;
            _hasLoggedImportedEquipmentAvailabilityDiagnostics = false;
            _hasLoggedHeroEquipmentResolutionAudit = false;
            _hasLoggedMaterializedEquipmentCoverageSummary = false;
            _hasTriggeredAuthoritativeBattleCompletion = false;
            _authoritativeBattleWinnerSide = string.Empty;
            _authoritativeBattleCompletionReason = string.Empty;
            _lastLoggedBattleCompletionAuditKey = string.Empty;
            _lastLoggedBattleStartReadinessAuditKey = string.Empty;
            _nextIncompleteBattleSnapshotRefreshUtc = DateTime.MinValue;
            _nextIncompleteBattleSnapshotLogUtc = DateTime.MinValue;
            _nextInitialMaterializedArmyPulseUtc = DateTime.MinValue;
            _nextMaterializedArmyReinforcementPulseUtc = DateTime.MinValue;
            _nextBattleMapStartupDeferLogUtc = DateTime.MinValue;
            _nextExactSceneMaterializationDeferLogUtc = DateTime.MinValue;
            _exactScenePostPossessionMaterializationResumeUtc = DateTime.MinValue;
            _nextNativeBattleMapWarmupFallbackRefreshUtc = DateTime.MinValue;
            _lastNativeBattleMapWarmupFallbackLogKey = string.Empty;
            CoopBattleSelectionIntentState.Reset();
            CoopBattleSelectionRequestState.Reset();
            CoopBattleSpawnIntentState.Reset();
            CoopBattleSpawnRequestState.Reset();
            CoopBattleSpawnRuntimeState.Reset();
            CoopBattlePeerLifecycleRuntimeState.Reset();
            CoopBattlePhaseRuntimeState.Clear(source + " init");
            CoopBattleResultBridgeFile.ClearResult(source + " init");
            CoopBattleSelectionBridgeFile.ClearAll(source + " init");
            CoopBattleSpawnBridgeFile.ClearPendingRequests(source + " init");
            _diagnosticAllowedAgent = null;

            RefreshAllowedTroopsFromRoster(source);
            CampaignMapPatchMissionInit.TryRepairLiveMissionContract(
                mission,
                (_lastServerRuntimeInitializationSource ?? source ?? "unknown") + " live-contract-repair");
            ModLogger.Info(
                "CoopMissionSpawnLogic: server runtime initialized. " +
                "Mission=" + (mission.SceneName ?? "null") +
                " Source=" + (_lastServerRuntimeInitializationSource ?? "unknown") +
                " CampaignRosterTroops=" + CampaignRosterTroopIds.Count);
            BattleMapContractDiagnostics.LogMissionRuntimeContract(mission, source + " runtime-init");
            if (!string.IsNullOrEmpty(SelectedAllowedTroopId))
                ModLogger.Info("CoopMissionSpawnLogic: runtime selected allowed troop id = " + SelectedAllowedTroopId);
            else
                ModLogger.Info("CoopMissionSpawnLogic: runtime found no allowed troop id (roster empty).");
            LogAllowedCharacterResolution();
            return true;
        }

        // Keep mission behavior and dedicated observer on the same battle-lifecycle pipeline.
        // This shared tick owns authoritative coop runtime state and battlefield materialization.
        // The legacy vanilla player-spawn bridge is kept in a separate explicit tick below.
        private static void RunSharedServerBattleLifecycleTick(
            Mission mission,
            string source,
            bool applyPreMaterializationPeerNudges)
        {
            if (mission == null || !GameNetwork.IsServer)
                return;

            if (ShouldDeferBattleMapStartupRuntime(mission, out string deferReason))
            {
                TryConsumeSelectionRequests(mission);
                TryApplySelectionIntentToPrimaryPeer(mission, source + " deferred-startup");
                if (applyPreMaterializationPeerNudges)
                    TryForceAuthoritativePeerTeams(mission, source + " deferred-startup");

                TryWriteEntryStatusSnapshot(mission, source + " deferred-startup");
                LogBattleMapStartupDeferralIfNeeded(mission, source, deferReason);
                return;
            }

            if (applyPreMaterializationPeerNudges)
            {
                TryConsumeSelectionRequests(mission);
                TryApplySelectionIntentToPrimaryPeer(mission, source);
                TryForceAuthoritativePeerTeams(mission, source);
                TryForceFixedMissionCultures(mission, source);
            }
            else
            {
                TryConsumeSelectionRequests(mission);
                TryApplySelectionIntentToPrimaryPeer(mission, source);
            }

            TryEnsureExactCampaignNativeArmyBootstrap(mission, source);
            TryEnsureBattlefieldArmiesMaterialized(mission, source);
            TryConsumeSpawnRequests(mission);
            TryApplySpawnIntentToPrimaryPeer(mission, source);
            TrySyncExactCampaignBattlefieldRuntimeState(mission, source);
            TryForceAuthoritativePeerTeams(mission, source);
            TryForceFixedMissionCultures(mission, source);
            TryForcePreferredHeroClassForPeer(mission, source);
            if (EnableMaterializedArmyPossessionExperiment)
                TryTakeControlOfMaterializedArmyAgents(mission, source);
            TryRefreshPendingSpawnRequests(mission, source);
            TryAlignControlledAgentsWithMaterializedArmy(mission, source);
            TryRefreshMaterializedCombatProfileDrivenStats(mission, source + " tick");
            LogMaterializedEquipmentCoverageSummaryIfNeeded();
            TryWriteEntryStatusSnapshot(mission, source + " tick");
        }

        private static bool ShouldDeferBattleMapStartupRuntime(Mission mission, out string reason)
        {
            reason = string.Empty;
            if (mission == null || !IsSceneAwareBattleMapRuntime(mission))
                return false;

            string missionMode = "unknown";
            float missionTime = -1f;
            int synchronizedPeerCount = 0;
            bool isExactCampaignBattleScene = false;

            try { missionMode = mission.Mode.ToString(); } catch { }
            try { missionTime = mission.CurrentTime; } catch { }
            try { isExactCampaignBattleScene = SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty); } catch { }

            if (GameNetwork.NetworkPeers != null)
            {
                foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
                {
                    if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
                        continue;

                    synchronizedPeerCount++;
                }
            }

            if (string.Equals(missionMode, "StartUp", StringComparison.OrdinalIgnoreCase))
            {
                reason =
                    "MissionMode=" + missionMode +
                    " MissionTime=" + missionTime.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                    " SynchronizedPeers=" + synchronizedPeerCount;
                return true;
            }

            if (missionTime >= 0f && missionTime < 1.5f)
            {
                reason =
                    "MissionMode=" + missionMode +
                    " MissionTime=" + missionTime.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                    " SynchronizedPeers=" + synchronizedPeerCount;
                return true;
            }

            if (isExactCampaignBattleScene && synchronizedPeerCount <= 0)
            {
                reason =
                    "MissionMode=" + missionMode +
                    " MissionTime=" + missionTime.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                    " SynchronizedPeers=" + synchronizedPeerCount +
                    " ExactScenePeerGate=pending";
                return true;
            }

            return false;
        }

        private static bool IsSceneAwareBattleMapRuntime(Mission mission)
        {
            string sceneName = mission?.SceneName ?? string.Empty;
            return SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(sceneName);
        }

        private static void LogBattleMapStartupDeferralIfNeeded(Mission mission, string source, string reason)
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (nowUtc < _nextBattleMapStartupDeferLogUtc)
                return;

            _nextBattleMapStartupDeferLogUtc = nowUtc.AddSeconds(2);
            ModLogger.Info(
                "CoopMissionSpawnLogic: deferred risky battle-map startup runtime. " +
                "Mission=" + (mission?.SceneName ?? "null") +
                " Source=" + (source ?? "unknown") +
                " Reason=" + (reason ?? "unknown"));
        }

        public static void RunCoopBattleSpawnOwnerTick(Mission mission, string source)
        {
            string spawnSource = string.IsNullOrWhiteSpace(source) ? "CoopBattle spawn owner" : source;
            RunSharedServerBattleLifecycleTick(
                mission,
                spawnSource,
                applyPreMaterializationPeerNudges: true);
            RunVanillaSpawnBridgeTick(
                mission,
                spawnSource);
        }

        public static void RunCoopBattlePhaseOwnerTick(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer)
                return;

            string phaseSource = string.IsNullOrWhiteSpace(source) ? "CoopBattle phase owner" : source;
            TryUpdateBattlePhaseState(mission, phaseSource);
            TryConsumeBattlePhaseRequests(mission);
            TryApplyNativeBattleMapWarmupFallback(mission, phaseSource);
            TryApplyBattlePhaseAiHold(mission, phaseSource);
            TryApplyBattlePhaseFormationHold(mission, phaseSource);
            TrySyncExactCampaignNativeArmyBootstrapReinforcements(mission, phaseSource);
            TrySpawnMaterializedBattlefieldReinforcements(mission, phaseSource);
            TryCompleteBattleIfResolved(mission, phaseSource);
        }

        public static void TryRunClientExactCampaignVisualObserver(Mission mission)
        {
            if (mission == null || GameNetwork.IsServer || !IsClientExactCampaignVisualOverlayRuntime(mission))
                return;

            EnsureClientBattleSnapshotFreshForMission(mission, "client-exact-visual-observer");
            ExactCampaignObjectCatalogBootstrap.EnsureLoaded("client-exact-visual-observer:" + (mission.SceneName ?? "null"));
            EnsureClientExactCampaignVisualOverlayQueuesBuilt();
            TryProcessPendingClientExactCampaignVisualOverlays(mission);

            int newlyQueuedAgents = 0;
            if (mission.AllAgents != null)
            {
                for (int i = 0; i < mission.AllAgents.Count; i++)
                {
                    Agent agent = mission.AllAgents[i];
                    if (agent == null || !agent.IsActive() || agent.IsMount || agent.Team == null || agent.Team.Side == BattleSideEnum.None)
                        continue;

                    string entryId = ResolveClientExactCampaignVisualOverlayEntryId(agent);
                    if (string.IsNullOrWhiteSpace(entryId))
                        continue;

                    if (TryQueueClientExactCampaignVisualOverlay(
                            mission,
                            agent,
                            entryId,
                            "client exact-visual observer",
                            delaySeconds: 0.35))
                    {
                        newlyQueuedAgents++;
                    }
                }
            }

            if (newlyQueuedAgents > 0)
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: queued delayed client-local exact campaign visual overlays for native exact agents. " +
                    "Queued=" + newlyQueuedAgents +
                    " Mission=" + (mission.SceneName ?? "null") +
                    " Source=client observer");
            }
        }

        private static bool TryQueueClientExactCampaignVisualOverlay(
            Mission mission,
            Agent agent,
            string preferredEntryId,
            string source,
            double delaySeconds,
            bool force = false,
            bool includeWeaponsForRefresh = false)
        {
            if (mission == null || agent == null || agent.IsMount || !agent.IsActive() || GameNetwork.IsServer)
                return false;

            if (!IsClientExactCampaignVisualOverlayRuntime(mission))
                return false;

            EnsureClientBattleSnapshotFreshForMission(mission, source ?? "client exact visual queue");
            EnsureClientExactCampaignVisualOverlayQueuesBuilt();

            string entryId = preferredEntryId;
            if (string.IsNullOrWhiteSpace(entryId) || BattleSnapshotRuntimeState.GetEntryState(entryId) == null)
                entryId = ResolveClientExactCampaignVisualOverlayEntryId(agent);

            if (string.IsNullOrWhiteSpace(entryId))
                return false;

            RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(entryId);
            if (entryState == null)
                return false;

            RefreshClientExactCampaignVisualOverlayAgentIndexState(
                agent,
                source ?? "client exact visual queue",
                preferredEntryId: entryId,
                logRefresh: true);

            _exactNativeClientVisualOverlayAgentByIndex[agent.Index] = agent;
            _exactNativeClientVisualOverlayEntryIdByAgentIndex[agent.Index] = entryId;
            TryApplyEntryIdentityToAgent(agent, entryState);

            if (!force &&
                (_exactNativeClientVisualOverlayAppliedAgentIndices.Contains(agent.Index) ||
                 _pendingExactNativeClientVisualOverlaysByAgentIndex.ContainsKey(agent.Index)))
            {
                return false;
            }

            if (force)
                _exactNativeClientVisualOverlayAppliedAgentIndices.Remove(agent.Index);

            _pendingExactNativeClientVisualOverlaysByAgentIndex[agent.Index] = new PendingClientExactVisualOverlayState
            {
                EntryId = entryId,
                CreatedUtc = DateTime.UtcNow,
                NotBeforeUtc = DateTime.UtcNow.AddSeconds(Math.Max(0.01, delaySeconds)),
                RetryCount = 0,
                Source = source ?? "client exact visual queue",
                IncludeWeaponsForRefresh = includeWeaponsForRefresh
            };
            return true;
        }

        private static void RefreshClientExactCampaignVisualOverlayAgentIndexState(
            Agent agent,
            string source,
            string preferredEntryId = null,
            bool logRefresh = false)
        {
            if (agent == null)
                return;

            bool rebound =
                _exactNativeClientVisualOverlayAgentByIndex.TryGetValue(agent.Index, out Agent cachedAgent) &&
                !ReferenceEquals(cachedAgent, agent);

            string currentTroopId = agent.Character?.StringId;
            bool troopMismatch = false;
            string previousEntryId = null;
            bool exactEntryChanged = false;
            if (_exactNativeClientVisualOverlayEntryIdByAgentIndex.TryGetValue(agent.Index, out string cachedEntryId) &&
                !string.IsNullOrWhiteSpace(cachedEntryId))
            {
                previousEntryId = cachedEntryId;
                exactEntryChanged =
                    !string.IsNullOrWhiteSpace(preferredEntryId) &&
                    !string.Equals(cachedEntryId, preferredEntryId, StringComparison.OrdinalIgnoreCase);
                RosterEntryState cachedEntryState = BattleSnapshotRuntimeState.GetEntryState(cachedEntryId);
                troopMismatch = cachedEntryState == null ||
                                !DoesClientVisualOverlayEntryMatchAgentTroop(cachedEntryState, currentTroopId);
            }

            if (!rebound && !troopMismatch && !exactEntryChanged)
            {
                _exactNativeClientVisualOverlayAgentByIndex[agent.Index] = agent;
                return;
            }

            _exactNativeClientVisualOverlayAppliedAgentIndices.Remove(agent.Index);
            _exactNativeClientVisualOverlayEntryIdByAgentIndex.Remove(agent.Index);
            _pendingExactNativeClientVisualOverlaysByAgentIndex.Remove(agent.Index);
            _exactNativeClientVisualOverlayAgentByIndex[agent.Index] = agent;

            if (logRefresh)
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: refreshed client exact visual overlay assignment cache. " +
                    "AgentIndex=" + agent.Index +
                    " PreviousEntryId=" + (previousEntryId ?? "null") +
                    " PreferredEntryId=" + (preferredEntryId ?? "null") +
                    " CurrentTroopId=" + (currentTroopId ?? "null") +
                    " Rebound=" + rebound +
                    " TroopMismatch=" + troopMismatch +
                    " ExactEntryChanged=" + exactEntryChanged +
                    " Source=" + (source ?? "unknown"));
            }
        }

        private static bool DoesClientVisualOverlayEntryMatchAgentTroop(RosterEntryState entryState, string currentTroopId)
        {
            if (entryState == null)
                return false;

            if (string.IsNullOrWhiteSpace(currentTroopId))
                return true;

            return string.Equals(entryState.CharacterId, currentTroopId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(entryState.OriginalCharacterId, currentTroopId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(entryState.SpawnTemplateId, currentTroopId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ResolveEntryMissionSafeFallbackTroopId(entryState), currentTroopId, StringComparison.OrdinalIgnoreCase);
        }

        private static void TryProcessPendingClientExactCampaignVisualOverlays(Mission mission)
        {
            if (mission == null || GameNetwork.IsServer || _pendingExactNativeClientVisualOverlaysByAgentIndex.Count == 0)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            var completedAgentIndices = new List<int>();
            foreach (KeyValuePair<int, PendingClientExactVisualOverlayState> pendingPair in _pendingExactNativeClientVisualOverlaysByAgentIndex)
            {
                if (pendingPair.Value == null || nowUtc < pendingPair.Value.NotBeforeUtc)
                    continue;

                int agentIndex = pendingPair.Key;
                PendingClientExactVisualOverlayState pendingState = pendingPair.Value;
                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(agentIndex, canBeNull: true);
                bool expired = nowUtc - pendingState.CreatedUtc >= TimeSpan.FromSeconds(8.0);
                if (agent == null)
                {
                    if (expired)
                    {
                        completedAgentIndices.Add(agentIndex);
                        ModLogger.Info(
                            "CoopMissionSpawnLogic: abandoned pending client exact visual overlay because agent never became available. " +
                            "AgentIndex=" + agentIndex +
                            " EntryId=" + (pendingState.EntryId ?? "null") +
                            " Retries=" + pendingState.RetryCount +
                            " Source=" + (pendingState.Source ?? "unknown"));
                    }
                    else
                    {
                        pendingState.RetryCount++;
                        pendingState.NotBeforeUtc = nowUtc.AddMilliseconds(150);
                    }
                    continue;
                }

                if (agent.IsMount)
                {
                    completedAgentIndices.Add(agentIndex);
                    continue;
                }

                if (!agent.IsActive() || agent.Team == null || agent.Team.Side == BattleSideEnum.None)
                {
                    if (expired)
                    {
                        completedAgentIndices.Add(agentIndex);
                        ModLogger.Info(
                            "CoopMissionSpawnLogic: abandoned pending client exact visual overlay because agent never became ready. " +
                            "AgentIndex=" + agentIndex +
                            " EntryId=" + (pendingState.EntryId ?? "null") +
                            " Active=" + agent.IsActive() +
                            " TeamNull=" + (agent.Team == null) +
                            " TeamSide=" + (agent.Team?.Side.ToString() ?? "null") +
                            " Retries=" + pendingState.RetryCount +
                            " Source=" + (pendingState.Source ?? "unknown"));
                    }
                    else
                    {
                        pendingState.RetryCount++;
                        pendingState.NotBeforeUtc = nowUtc.AddMilliseconds(150);
                    }
                    continue;
                }

                if (TryApplyExactCampaignSnapshotOverlayToNativeAgent(
                        agent,
                        pendingState.EntryId,
                        pendingState.Source ?? "client delayed exact visual refresh",
                        ExactCampaignSnapshotOverlayMode.ClientVisualOnly,
                        includeWeaponsForClientRefresh: pendingState.IncludeWeaponsForRefresh))
                {
                    ModLogger.Info(
                        "CoopMissionSpawnLogic: completed pending client exact visual refresh. " +
                        "AgentIndex=" + agent.Index +
                        " EntryId=" + (pendingState.EntryId ?? "null") +
                        " IncludeWeapons=" + pendingState.IncludeWeaponsForRefresh +
                        " Retries=" + pendingState.RetryCount +
                        " Source=" + (pendingState.Source ?? "unknown"));
                    completedAgentIndices.Add(agentIndex);
                }
                else if (expired)
                {
                    completedAgentIndices.Add(agentIndex);
                    ModLogger.Info(
                        "CoopMissionSpawnLogic: abandoned pending client exact visual overlay because apply never succeeded. " +
                        "AgentIndex=" + agentIndex +
                        " EntryId=" + (pendingState.EntryId ?? "null") +
                        " Retries=" + pendingState.RetryCount +
                        " Source=" + (pendingState.Source ?? "unknown"));
                }
                else
                {
                    pendingState.RetryCount++;
                    pendingState.NotBeforeUtc = nowUtc.AddMilliseconds(150);
                }
            }

            for (int i = 0; i < completedAgentIndices.Count; i++)
                _pendingExactNativeClientVisualOverlaysByAgentIndex.Remove(completedAgentIndices[i]);
        }

        public static bool TryFinalizeClientExactCampaignVisualForAgent(
            Mission mission,
            Agent agent,
            string preferredEntryId,
            string source,
            bool includeWeaponsForClientRefresh = false,
            bool allowImmediateApply = true)
        {
            if (mission == null || agent == null || agent.IsMount || !agent.IsActive() || GameNetwork.IsServer)
                return false;

            if (!IsClientExactCampaignVisualOverlayRuntime(mission))
                return false;

            EnsureClientExactCampaignVisualOverlayQueuesBuilt();

            string entryId = preferredEntryId;
            if (string.IsNullOrWhiteSpace(entryId) || BattleSnapshotRuntimeState.GetEntryState(entryId) == null)
                entryId = ResolveClientExactCampaignVisualOverlayEntryId(agent);

            if (string.IsNullOrWhiteSpace(entryId))
                return false;

            RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(entryId);
            if (!IsHeroEntryEligibleForExactPersonalPerks(entryState))
                return false;

            _exactNativeClientVisualOverlayEntryIdByAgentIndex[agent.Index] = entryId;
            if (!allowImmediateApply)
            {
                return TryQueueClientExactCampaignVisualOverlay(
                    mission,
                    agent,
                    entryId,
                    source ?? "client exact-visual finalize",
                    delaySeconds: 0.35,
                    force: true,
                    includeWeaponsForRefresh: includeWeaponsForClientRefresh);
            }

            if (agent.IsActive() && agent.Team != null && agent.Team.Side != BattleSideEnum.None)
            {
                _pendingExactNativeClientVisualOverlaysByAgentIndex.Remove(agent.Index);
                _exactNativeClientVisualOverlayAppliedAgentIndices.Remove(agent.Index);
                if (TryApplyExactCampaignSnapshotOverlayToNativeAgent(
                        agent,
                        entryId,
                        source ?? "client exact-visual finalize",
                        ExactCampaignSnapshotOverlayMode.ClientVisualOnly,
                        includeWeaponsForClientRefresh: includeWeaponsForClientRefresh))
                {
                    return true;
                }
            }

            return TryQueueClientExactCampaignVisualOverlay(
                mission,
                agent,
                entryId,
                source ?? "client exact-visual finalize",
                delaySeconds: 0.35,
                includeWeaponsForRefresh: includeWeaponsForClientRefresh);
        }

        public static bool TryHandleClientExactCampaignSpawnEquipmentSync(
            Mission mission,
            Agent agent,
            string source)
        {
            if (mission == null || agent == null || agent.IsMount || !agent.IsActive() || GameNetwork.IsServer)
                return false;

            if (!IsClientExactCampaignVisualOverlayRuntime(mission))
                return false;

            ExactCampaignObjectCatalogBootstrap.EnsureLoaded("client-exact-spawn-equipment-sync:" + (mission.SceneName ?? "null"));
            string entryId = ResolveClientExactCampaignVisualOverlayEntryId(agent);
            if (string.IsNullOrWhiteSpace(entryId))
                return false;

            RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(entryId);
            if (!IsHeroEntryEligibleForExactPersonalPerks(entryState))
                return false;

            bool queued = TryQueueClientExactCampaignVisualOverlay(
                mission,
                agent,
                entryId,
                source ?? "client exact equipment sync",
                delaySeconds: 0.08,
                force: true,
                includeWeaponsForRefresh: true);
            if (!queued)
                return false;

            ModLogger.Info(
                "CoopMissionSpawnLogic: queued delayed client exact visual refresh after raw spawn equipment sync. " +
                "AgentIndex=" + agent.Index +
                " EntryId=" + entryId +
                " TroopId=" + (agent.Character?.StringId ?? "null") +
                " Source=" + (source ?? "unknown"));
            return false;
        }

        private static void TryEnsureExactCampaignNativeArmyBootstrap(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer)
                return;

            ExactCampaignArmyBootstrap.ResetForMission(mission);
            if (!ExperimentalFeatures.EnableExactCampaignNativeArmyBootstrap ||
                !IsSceneAwareBattleMapRuntime(mission) ||
                !SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty))
            {
                return;
            }

            MissionPeer missionPeer = ResolvePrimaryControllablePeer(mission);
            if (missionPeer == null)
            {
                LogExactSceneNativeBootstrapDeferred(mission, "primary-peer-missing", source);
                return;
            }

            BattleSideEnum authoritativeSide = ResolveAuthoritativeSide(missionPeer, mission, source + " exact-native-bootstrap");
            if (authoritativeSide == BattleSideEnum.None)
            {
                LogExactSceneNativeBootstrapDeferred(mission, "authoritative-side-none", source);
                return;
            }

            Team authoritativeTeam = ResolveAuthoritativeMissionTeam(mission, missionPeer, authoritativeSide);
            if (authoritativeTeam == null || ReferenceEquals(authoritativeTeam, mission.SpectatorTeam))
            {
                LogExactSceneNativeBootstrapDeferred(mission, "authoritative-team-not-ready", source);
                return;
            }

            TryBridgeMissionPlayerTeamForExactCampaignBootstrap(
                mission,
                authoritativeTeam,
                authoritativeSide,
                source);

            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            bool hasCommittedSelection =
                !string.IsNullOrWhiteSpace(selectionState.EntryId) ||
                !string.IsNullOrWhiteSpace(selectionState.TroopId) ||
                CoopBattleSpawnRequestState.HasPendingRequest(missionPeer);
            if (!hasCommittedSelection)
            {
                LogExactSceneNativeBootstrapDeferred(mission, "selection-not-committed", source);
                return;
            }

            if (mission.PlayerTeam == null || mission.PlayerEnemyTeam == null)
            {
                LogExactSceneNativeBootstrapDeferred(mission, "mission-player-teams-not-ready", source);
                return;
            }

            if (mission.PlayerTeam.Side != authoritativeSide)
            {
                LogExactSceneNativeBootstrapDeferred(
                    mission,
                    "mission-player-team-side-mismatch PlayerTeam=" + mission.PlayerTeam.Side + " AuthoritativeSide=" + authoritativeSide,
                    source);
                return;
            }

            if (!ExactCampaignArmyBootstrap.TryInitialize(
                mission,
                authoritativeSide,
                source + " exact-native-bootstrap",
                out string initializeReason))
            {
                LogExactSceneNativeBootstrapDeferred(mission, initializeReason, source);
            }
        }

        private static void TrySyncExactCampaignNativeArmyBootstrapReinforcements(Mission mission, string source)
        {
            if (!IsUsingNativeExactCampaignArmyBootstrap(mission))
                return;

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            bool shouldEnableReinforcements =
                currentPhase >= CoopBattlePhase.BattleActive &&
                currentPhase < CoopBattlePhase.BattleEnded;
            ExactCampaignArmyBootstrap.TrySyncReinforcementState(
                mission,
                shouldEnableReinforcements,
                source + " exact-native-bootstrap");
            ExactCampaignArmyBootstrap.TryLogRuntimeDiagnostics(
                mission,
                source + " exact-native-bootstrap");
        }

        private static void TrySyncExactCampaignNativeOriginRemoval(Mission mission, Agent affectedAgent, Agent affectorAgent, AgentState agentState)
        {
            ExactCampaignArmyBootstrap.TrySyncAgentOriginRemoval(
                mission,
                affectedAgent,
                affectorAgent,
                agentState,
                "mission-behavior-onagentremoved");
        }

        private static void TrySyncExactCampaignBattlefieldRuntimeState(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer || !IsUsingNativeExactCampaignArmyBootstrap(mission))
                return;

            EnsureMaterializedBattleResultMission(mission);
            int seededEntries = 0;
            seededEntries += EnsureExactCampaignBattleResultEntryStates(BattleSideEnum.Attacker);
            seededEntries += EnsureExactCampaignBattleResultEntryStates(BattleSideEnum.Defender);

            int newlyTrackedAgents = 0;
            int newlyOverlaidAgents = 0;
            if (mission.AllAgents != null)
            {
                for (int i = 0; i < mission.AllAgents.Count; i++)
                {
                    Agent agent = mission.AllAgents[i];
                    if (agent == null || !agent.IsActive() || agent.IsMount)
                        continue;

                    if (!TryRefreshMaterializedAgentIdentityCache(
                            agent,
                            out string entryId,
                            out BattleSideEnum side,
                            out bool requiresRegistration,
                            source,
                            logRefresh: true) ||
                        string.IsNullOrWhiteSpace(entryId) ||
                        side == BattleSideEnum.None)
                    {
                        continue;
                    }

                    if (requiresRegistration)
                    {
                        RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(entryId);
                        if (entryState != null)
                            RegisterMaterializedBattleResultEntry(agent, entryState, side);
                        else if (_materializedBattleResultEntriesByEntryId.TryGetValue(entryId, out MaterializedBattleResultEntryRuntimeState runtimeState) &&
                                 runtimeState != null)
                        {
                            runtimeState.MaterializedSpawnCount++;
                            runtimeState.ActiveCount++;
                        }

                        newlyTrackedAgents++;
                    }

                    if (TryApplyExactCampaignSnapshotOverlayToNativeAgent(agent, entryId, source))
                        newlyOverlaidAgents++;
                }

                for (int i = 0; i < mission.AllAgents.Count; i++)
                {
                    Agent agent = mission.AllAgents[i];
                    if (agent == null || !agent.IsActive() || agent.IsMount)
                        continue;

                    if (_exactNativeSnapshotOverlayAppliedAgentIndices.Contains(agent.Index))
                        continue;

                    if (!ExactCampaignArmyBootstrap.TryGetEntryId(agent, out string entryId) ||
                        string.IsNullOrWhiteSpace(entryId))
                    {
                        continue;
                    }

                    if (TryApplyExactCampaignSnapshotOverlayToNativeAgent(agent, entryId, source))
                        newlyOverlaidAgents++;
                }
            }

            int commanderAssignments = TryAssignExactCampaignCommanders(mission, source);
            if (seededEntries <= 0 && newlyTrackedAgents <= 0 && newlyOverlaidAgents <= 0 && commanderAssignments <= 0)
                return;

            int trackedAttackerAgents = _materializedArmySideByAgentIndex.Values.Count(candidate => candidate == BattleSideEnum.Attacker);
            int trackedDefenderAgents = _materializedArmySideByAgentIndex.Values.Count(candidate => candidate == BattleSideEnum.Defender);
            ModLogger.Info(
                "CoopMissionSpawnLogic: synced exact campaign bootstrap runtime state. " +
                "SeededEntries=" + seededEntries +
                " NewlyTrackedAgents=" + newlyTrackedAgents +
                " SnapshotOverlays=" + newlyOverlaidAgents +
                " CommanderAssignments=" + commanderAssignments +
                " TrackedAttackerAgents=" + trackedAttackerAgents +
                " TrackedDefenderAgents=" + trackedDefenderAgents +
                " Source=" + (source ?? "unknown"));
        }

        private static bool IsClientExactCampaignVisualOverlayRuntime(Mission mission)
        {
            return mission != null &&
                   SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(mission.SceneName) &&
                   BattleSnapshotRuntimeState.GetState()?.Sides != null &&
                   BattleSnapshotRuntimeState.GetState().Sides.Count > 0;
        }

        private static void EnsureClientExactCampaignVisualOverlayQueuesBuilt()
        {
            BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
            if (runtimeState?.Sides == null || runtimeState.Sides.Count == 0)
                return;

            string snapshotKey = BuildClientExactCampaignVisualOverlayQueueSnapshotKey(runtimeState);
            if (_exactNativeClientVisualOverlayEntryQueuesByAssignmentKey.Count > 0)
            {
                if (string.Equals(_exactNativeClientVisualOverlayQueueSnapshotKey, snapshotKey, StringComparison.Ordinal))
                    return;

                ResetClientExactCampaignVisualOverlayAssignmentState(
                    "EnsureClientExactCampaignVisualOverlayQueuesBuilt snapshot-changed: " +
                    (_exactNativeClientVisualOverlayQueueSnapshotKey ?? "null") +
                    " -> " +
                    (snapshotKey ?? "null"));
            }

            foreach (BattleSideState sideState in runtimeState.Sides)
            {
                BattleSideEnum side = ResolveBattleSideFromState(sideState);
                if (side == BattleSideEnum.None || sideState?.Entries == null)
                    continue;

                RosterEntryState commanderEntryState = BattleCommanderResolver.ResolveCommanderEntry(runtimeState, side, sideState.Entries);
                string commanderEntryId = commanderEntryState?.EntryId;
                IEnumerable<RosterEntryState> orderedEntries = sideState.Entries;
                if (!string.IsNullOrWhiteSpace(commanderEntryId))
                {
                    orderedEntries = sideState.Entries
                        .OrderByDescending(entry => string.Equals(entry?.EntryId, commanderEntryId, StringComparison.Ordinal));
                }

                foreach (RosterEntryState entryState in orderedEntries)
                {
                    if (entryState == null)
                        continue;

                    int availableCount = Math.Max(0, entryState.Count - entryState.WoundedCount);
                    if (availableCount <= 0)
                        continue;

                    string spawnTemplateId = ResolveEntrySpawnTemplateId(entryState);
                    if (string.IsNullOrWhiteSpace(spawnTemplateId) || string.IsNullOrWhiteSpace(entryState.EntryId))
                        continue;

                    EnqueueClientExactCampaignVisualOverlayAssignmentEntries(
                        side,
                        spawnTemplateId,
                        entryState.EntryId,
                        availableCount);

                    string fallbackTroopId = ResolveEntryMissionSafeFallbackTroopId(entryState);
                    if (!string.IsNullOrWhiteSpace(fallbackTroopId) &&
                        !string.Equals(fallbackTroopId, spawnTemplateId, StringComparison.OrdinalIgnoreCase))
                    {
                        EnqueueClientExactCampaignVisualOverlayAssignmentEntries(
                            side,
                            fallbackTroopId,
                            entryState.EntryId,
                            availableCount);
                    }
                }
            }

            _exactNativeClientVisualOverlayQueueSnapshotKey = snapshotKey;
            ModLogger.Info(
                "CoopMissionSpawnLogic: built client exact visual overlay assignment queues. " +
                "SnapshotKey=" + (_exactNativeClientVisualOverlayQueueSnapshotKey ?? "null") +
                " QueueCount=" + _exactNativeClientVisualOverlayEntryQueuesByAssignmentKey.Count +
                " EntryCount=" + (runtimeState.EntriesById?.Count ?? 0));
        }

        private static string BuildClientExactCampaignVisualOverlayQueueSnapshotKey(BattleRuntimeState runtimeState)
        {
            BattleSnapshotMessage currentSnapshot = BattleSnapshotRuntimeState.GetCurrent();
            string battleId = currentSnapshot?.BattleId ?? "null";
            string sceneName = currentSnapshot?.MapScene ?? "null";
            string sideSignature = runtimeState?.Sides == null
                ? "none"
                : string.Join(",",
                    runtimeState.Sides.Select(sideState =>
                        (sideState?.CanonicalSideKey ?? sideState?.SideId ?? "null") + ":" +
                        (sideState?.TotalManCount ?? 0) + ":" +
                        (sideState?.Entries?.Count ?? 0)));
            return battleId + "|" + sceneName + "|" + sideSignature;
        }

        private static string ResolveClientExactCampaignVisualOverlayEntryId(Agent agent)
        {
            if (agent == null || agent.Team == null || agent.Team.Side == BattleSideEnum.None)
                return null;

            EnsureClientBattleSnapshotFreshForMission(agent.Mission ?? Mission.Current, "ResolveClientExactCampaignVisualOverlayEntryId");
            RefreshClientExactCampaignVisualOverlayAgentIndexState(agent, "ResolveClientExactCampaignVisualOverlayEntryId", logRefresh: true);

            if (_exactNativeClientVisualOverlayEntryIdByAgentIndex.TryGetValue(agent.Index, out string cachedEntryId) &&
                !string.IsNullOrWhiteSpace(cachedEntryId))
            {
                if (BattleSnapshotRuntimeState.GetEntryState(cachedEntryId) != null)
                    return cachedEntryId;

                _exactNativeClientVisualOverlayEntryIdByAgentIndex.Remove(agent.Index);
            }

            if (ExactCampaignArmyBootstrap.TryGetEntryId(agent, out string originEntryId) &&
                !string.IsNullOrWhiteSpace(originEntryId))
            {
                _exactNativeClientVisualOverlayEntryIdByAgentIndex[agent.Index] = originEntryId;
                return originEntryId;
            }

            string troopId = agent.Character?.StringId;
            if (string.IsNullOrWhiteSpace(troopId))
                return null;

            string assignmentKey = BuildClientExactCampaignVisualOverlayAssignmentKey(agent.Team.Side, troopId);
            if (_exactNativeClientVisualOverlayEntryQueuesByAssignmentKey.TryGetValue(assignmentKey, out Queue<string> entryQueue) &&
                entryQueue != null &&
                entryQueue.Count > 0)
            {
                string assignedEntryId = entryQueue.Dequeue();
                if (!string.IsNullOrWhiteSpace(assignedEntryId))
                {
                    _exactNativeClientVisualOverlayEntryIdByAgentIndex[agent.Index] = assignedEntryId;
                    return assignedEntryId;
                }
            }

            return null;
        }

        private static string BuildClientExactCampaignVisualOverlayAssignmentKey(BattleSideEnum side, string troopId)
        {
            return side + "|" + (troopId ?? string.Empty);
        }

        private static void EnqueueClientExactCampaignVisualOverlayAssignmentEntries(
            BattleSideEnum side,
            string troopId,
            string entryId,
            int availableCount)
        {
            if (side == BattleSideEnum.None ||
                string.IsNullOrWhiteSpace(troopId) ||
                string.IsNullOrWhiteSpace(entryId) ||
                availableCount <= 0)
            {
                return;
            }

            string assignmentKey = BuildClientExactCampaignVisualOverlayAssignmentKey(side, troopId);
            if (!_exactNativeClientVisualOverlayEntryQueuesByAssignmentKey.TryGetValue(assignmentKey, out Queue<string> entryQueue))
            {
                entryQueue = new Queue<string>();
                _exactNativeClientVisualOverlayEntryQueuesByAssignmentKey[assignmentKey] = entryQueue;
            }

            for (int i = 0; i < availableCount; i++)
                entryQueue.Enqueue(entryId);
        }

        private static string ResolveEntryMissionSafeFallbackTroopId(RosterEntryState entryState)
        {
            return BattleSnapshotRuntimeState.TryResolveMissionSafeFallbackCharacterId(
                entryState,
                ResolveEntrySpawnTemplateId(entryState));
        }

        private static void EnsureClientBattleSnapshotFreshForMission(Mission mission, string source)
        {
            if (mission == null || GameNetwork.IsServer || !IsSceneAwareBattleMapRuntime(mission))
                return;

            BattleSnapshotMessage currentSnapshot = BattleSnapshotRuntimeState.GetCurrent();
            bool missionChanged = !ReferenceEquals(_lastClientBattleSnapshotRefreshMission, mission);
            bool sceneMismatch = !string.Equals(
                currentSnapshot?.MapScene ?? string.Empty,
                mission.SceneName ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
            if (!missionChanged && !sceneMismatch)
                return;

            string previousKey = _lastClientBattleSnapshotRefreshKey;
            BattleSnapshotMessage refreshedSnapshot = BattleRosterFileHelper.ReadSnapshot() ?? BattleSnapshotRuntimeState.GetCurrent();
            string refreshedKey = BuildClientBattleSnapshotRefreshKey(refreshedSnapshot);

            ResetClientMissionRuntimeState(
                "EnsureClientBattleSnapshotFreshForMission: " + (source ?? "unknown"));
            CoopSpectator.Patches.BattleMapSpawnHandoffPatch.ResetRuntimeState(
                "EnsureClientBattleSnapshotFreshForMission: " + (source ?? "unknown"));

            _lastClientBattleSnapshotRefreshMission = mission;
            _lastClientBattleSnapshotRefreshKey = refreshedKey;

            ModLogger.Info(
                "CoopMissionSpawnLogic: refreshed client battle snapshot for mission. " +
                "Mission=" + (mission.SceneName ?? "null") +
                " Source=" + (source ?? "unknown") +
                " MissionChanged=" + missionChanged +
                " SceneMismatch=" + sceneMismatch +
                " PreviousSnapshotKey=" + (previousKey ?? "null") +
                " RefreshedSnapshotKey=" + (_lastClientBattleSnapshotRefreshKey ?? "null"));
        }

        private static string BuildClientBattleSnapshotRefreshKey(BattleSnapshotMessage snapshot)
        {
            if (snapshot == null)
                return "null";

            string sidesSignature = snapshot.Sides == null
                ? "none"
                : string.Join(",",
                    snapshot.Sides.Select(side =>
                        (side?.SideId ?? "null") + ":" +
                        (side?.TotalManCount ?? 0) + ":" +
                        (side?.Troops?.Count ?? 0)));
            return (snapshot.BattleId ?? "null") +
                   "|" +
                   (snapshot.MapScene ?? "null") +
                   "|" +
                   sidesSignature;
        }

        private static bool HasExactCampaignPreSpawnLoadoutInjected(Agent agent)
        {
            return ExperimentalFeatures.EnableExactCampaignPreSpawnLoadoutInjection &&
                   GameNetwork.IsServer &&
                   CoopSpectator.Patches.ExactCampaignPreSpawnLoadoutPatch.IsOperationalOnCurrentProcess &&
                   agent?.Origin is ExactCampaignSnapshotAgentOrigin;
        }

        private static bool TryApplyExactCampaignSnapshotOverlayToNativeAgent(
            Agent agent,
            string entryId,
            string source,
            ExactCampaignSnapshotOverlayMode overlayMode = ExactCampaignSnapshotOverlayMode.ServerAuthoritative,
            bool includeWeaponsForClientRefresh = false)
        {
            if (agent == null || !agent.IsActive() || agent.IsMount || string.IsNullOrWhiteSpace(entryId))
                return false;

            HashSet<int> appliedAgentIndices =
                overlayMode == ExactCampaignSnapshotOverlayMode.ClientVisualOnly
                    ? _exactNativeClientVisualOverlayAppliedAgentIndices
                    : _exactNativeSnapshotOverlayAppliedAgentIndices;
            HashSet<string> loggedEntryIds =
                overlayMode == ExactCampaignSnapshotOverlayMode.ClientVisualOnly
                    ? _exactNativeClientVisualOverlayLoggedEntryIds
                    : _exactNativeSnapshotOverlayLoggedEntryIds;
            if (appliedAgentIndices.Contains(agent.Index))
                return false;

            RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(entryId);
            if (entryState == null)
                return false;

            string appliedEquipment = "(none)";
            string equipmentMisses = "(none)";
            bool runtimeExactCharacter = ExactCampaignRuntimeObjectRegistry.IsRuntimeCharacter(agent.Character as BasicCharacterObject);
            bool preSpawnExactLoadoutInjected = HasExactCampaignPreSpawnLoadoutInjected(agent);
            bool includeWeaponsForOverlayRefresh =
                overlayMode == ExactCampaignSnapshotOverlayMode.ClientVisualOnly
                    ? includeWeaponsForClientRefresh
                    : !preSpawnExactLoadoutInjected;
            if (overlayMode == ExactCampaignSnapshotOverlayMode.ClientVisualOnly)
                TryApplyEntryIdentityToAgent(agent, entryState);

            Equipment seedEquipment = agent.SpawnEquipment?.Clone(false);
            Equipment spawnEquipment = runtimeExactCharacter || preSpawnExactLoadoutInjected
                ? null
                : BuildSnapshotEquipmentForReplaceBot(
                    entryState,
                    seedEquipment,
                    includeWeapons: includeWeaponsForOverlayRefresh);
            if (runtimeExactCharacter)
            {
                appliedEquipment = "runtime-exact-character";
                equipmentMisses = "post-spawn-refresh-skipped";
            }
            else if (preSpawnExactLoadoutInjected)
            {
                if (overlayMode == ExactCampaignSnapshotOverlayMode.ClientVisualOnly)
                {
                    Equipment safeClientEquipment = BuildSnapshotEquipmentForReplaceBot(
                        entryState,
                        seedEquipment,
                        includeWeapons: includeWeaponsForClientRefresh);
                    if (safeClientEquipment != null)
                    {
                        var missedSlots = new List<string>();
                        appliedEquipment = TryApplyMaterializedEquipmentOverrides(
                            safeClientEquipment,
                            entryState,
                            missedSlots,
                            trackCoverage: false,
                            includeWeapons: includeWeaponsForClientRefresh);
                        equipmentMisses = missedSlots.Count > 0 ? string.Join(", ", missedSlots) : "(none)";
                        try
                        {
                            agent.UpdateSpawnEquipmentAndRefreshVisuals(safeClientEquipment);
                            agent.MountAgent?.UpdateAgentProperties();
                        }
                        catch (Exception ex)
                        {
                            equipmentMisses =
                                equipmentMisses == "(none)"
                                    ? "visual-refresh-failed:" + ex.GetType().Name
                                    : equipmentMisses + ", visual-refresh-failed:" + ex.GetType().Name;
                        }
                    }
                    else
                    {
                        appliedEquipment = "pre-spawn-exact-loadout-client-refresh-fallback";
                        equipmentMisses = "safe-client-equipment-build-failed";
                    }
                }
                else
                {
                    appliedEquipment = "pre-spawn-exact-loadout";
                    equipmentMisses = "overlay-skipped";
                }
            }
            else if (spawnEquipment != null)
            {
                var missedSlots = new List<string>();
                appliedEquipment = TryApplyMaterializedEquipmentOverrides(
                    spawnEquipment,
                    entryState,
                    missedSlots,
                    trackCoverage: false,
                    includeWeapons: includeWeaponsForOverlayRefresh);
                equipmentMisses = missedSlots.Count > 0 ? string.Join(", ", missedSlots) : "(none)";

                try
                {
                    agent.UpdateSpawnEquipmentAndRefreshVisuals(spawnEquipment);
                    agent.MountAgent?.UpdateAgentProperties();
                }
                catch (Exception ex)
                {
                    equipmentMisses =
                        equipmentMisses == "(none)"
                            ? "visual-refresh-failed:" + ex.GetType().Name
                            : equipmentMisses + ", visual-refresh-failed:" + ex.GetType().Name;
                }
            }

            if (overlayMode != ExactCampaignSnapshotOverlayMode.ClientVisualOnly)
                TryApplyEntryIdentityToAgent(agent, entryState);
            string appliedCombatProfile =
                overlayMode == ExactCampaignSnapshotOverlayMode.ServerAuthoritative
                    ? TryApplyMaterializedCombatProfile(agent, entryState)
                    : TryApplyClientVisualOnlyCombatProfile(agent, entryState);
            appliedAgentIndices.Add(agent.Index);

            if (loggedEntryIds.Add(entryId))
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: applied " +
                    (overlayMode == ExactCampaignSnapshotOverlayMode.ClientVisualOnly
                        ? "client-local exact campaign visual overlay"
                        : "exact campaign snapshot overlay") +
                    " to native exact agent. " +
                    "AgentIndex=" + agent.Index +
                    " EntryId=" + entryId +
                    " TroopId=" + (agent.Character?.StringId ?? "null") +
                    " TroopName=" + (entryState.TroopName ?? "null") +
                    " Hero=" + entryState.IsHero +
                    " AppliedEquipment=" + appliedEquipment +
                    " EquipmentMisses=" + equipmentMisses +
                    " " + appliedCombatProfile +
                    " Source=" + (source ?? "unknown"));
            }

            return true;
        }

        private static string TryApplyClientVisualOnlyCombatProfile(Agent agent, RosterEntryState entryState)
        {
            if (agent == null || entryState == null || !IsHeroEntryEligibleForExactPersonalPerks(entryState))
                return "AppliedCombatProfile=client-visual-only";

            try
            {
                const string prefix = "AppliedCombatProfile=";
                string summary = TryApplyMaterializedCombatProfile(agent, entryState);
                if (string.IsNullOrWhiteSpace(summary))
                    return "AppliedCombatProfile=client-visual-only";

                if (summary.StartsWith(prefix, StringComparison.Ordinal))
                    return prefix + "client-local/" + summary.Substring(prefix.Length);

                return summary;
            }
            catch (Exception ex)
            {
                return "AppliedCombatProfile=client-visual-only-failed:" + ex.GetType().Name;
            }
        }

        private static int TryAssignExactCampaignCommanders(Mission mission, string source)
        {
            BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
            if (mission?.Teams == null || runtimeState == null)
                return 0;

            int assignments = 0;
            assignments += TryAssignExactCampaignCommanderForSide(mission, runtimeState, BattleSideEnum.Attacker, source);
            assignments += TryAssignExactCampaignCommanderForSide(mission, runtimeState, BattleSideEnum.Defender, source);
            return assignments;
        }

        private static int TryAssignExactCampaignCommanderForSide(
            Mission mission,
            BattleRuntimeState runtimeState,
            BattleSideEnum side,
            string source)
        {
            if (mission == null || runtimeState == null || side == BattleSideEnum.None)
                return 0;

            Team team = mission.Teams?.FirstOrDefault(candidate => candidate != null && candidate.Side == side);
            if (team == null)
                return 0;

            RosterEntryState commanderEntry = BattleCommanderResolver.ResolveCommanderEntry(runtimeState, side);
            if (commanderEntry == null || string.IsNullOrWhiteSpace(commanderEntry.EntryId))
                return 0;

            Agent commanderAgent = ResolveLiveExactCampaignCommanderAgent(mission, team, commanderEntry.EntryId);
            if (commanderAgent == null)
                return 0;

            bool changed = false;
            if (!ReferenceEquals(team.GeneralAgent, commanderAgent))
            {
                commanderAgent.SetCanLeadFormationsRemotely(value: true);
                team.GeneralAgent = commanderAgent;
                changed = true;
            }

            Formation commanderFormation = commanderAgent.Formation;
            if (commanderFormation != null &&
                ReferenceEquals(commanderFormation.Team, team) &&
                !ReferenceEquals(commanderFormation.Captain, commanderAgent))
            {
                commanderFormation.Captain = commanderAgent;
                changed = true;
            }

            if (!changed)
                return 0;

            ModLogger.Info(
                "CoopMissionSpawnLogic: assigned exact campaign commander. " +
                "Side=" + side +
                " TeamIndex=" + team.TeamIndex +
                " EntryId=" + commanderEntry.EntryId +
                " TroopId=" + (commanderEntry.CharacterId ?? "null") +
                " HeroRole=" + (commanderEntry.HeroRole ?? "none") +
                " Tier=" + commanderEntry.Tier +
                " AgentIndex=" + commanderAgent.Index +
                " PlayerControlled=" + (commanderAgent.MissionPeer != null || commanderAgent.Controller == AgentControllerType.Player) +
                " Formation=" + (commanderFormation?.FormationIndex.ToString() ?? "null") +
                " Source=" + (source ?? "unknown"));
            return 1;
        }

        private static Agent ResolveLiveExactCampaignCommanderAgent(Mission mission, Team team, string commanderEntryId)
        {
            if (mission?.AllAgents == null || team == null || string.IsNullOrWhiteSpace(commanderEntryId))
                return null;

            Agent firstMatchingAgent = null;
            Agent existingGeneralAgent = null;
            Agent controlledMatchingAgent = null;

            for (int i = 0; i < mission.AllAgents.Count; i++)
            {
                Agent candidate = mission.AllAgents[i];
                if (candidate == null ||
                    !candidate.IsActive() ||
                    candidate.IsMount ||
                    !ReferenceEquals(candidate.Team, team) ||
                    !DoesAgentMatchAuthoritativeEntryId(candidate, commanderEntryId))
                {
                    continue;
                }

                if (firstMatchingAgent == null)
                    firstMatchingAgent = candidate;

                if (ReferenceEquals(candidate, team.GeneralAgent))
                    existingGeneralAgent = candidate;

                if (controlledMatchingAgent == null &&
                    (candidate.MissionPeer != null || candidate.Controller == AgentControllerType.Player))
                {
                    controlledMatchingAgent = candidate;
                }
            }

            return controlledMatchingAgent ?? existingGeneralAgent ?? firstMatchingAgent;
        }

        private static int EnsureExactCampaignBattleResultEntryStates(BattleSideEnum side)
        {
            if (side == BattleSideEnum.None)
                return 0;

            int seededCount = 0;
            foreach (RosterEntryState entryState in GetAllowedControlEntryStatesSnapshot(side))
            {
                if (entryState == null || string.IsNullOrWhiteSpace(entryState.EntryId) || _materializedBattleResultEntriesByEntryId.ContainsKey(entryState.EntryId))
                    continue;

                string sideId = !string.IsNullOrWhiteSpace(entryState.SideId)
                    ? entryState.SideId
                    : side.ToString();
                _materializedBattleResultEntriesByEntryId[entryState.EntryId] = new MaterializedBattleResultEntryRuntimeState
                {
                    EntryId = entryState.EntryId,
                    SideId = sideId,
                    PartyId = entryState.PartyId,
                    CharacterId = entryState.CharacterId,
                    OriginalCharacterId = entryState.OriginalCharacterId,
                    SpawnTemplateId = entryState.SpawnTemplateId,
                    TroopName = entryState.TroopName,
                    HeroId = entryState.HeroId,
                    HeroRole = entryState.HeroRole,
                    IsHero = entryState.IsHero,
                    SnapshotCount = entryState.Count,
                    SnapshotWoundedCount = entryState.WoundedCount
                };
                seededCount++;
            }

            return seededCount;
        }

        private static bool IsUsingNativeExactCampaignArmyBootstrap(Mission mission)
        {
            return ExactCampaignArmyBootstrap.IsActive(mission);
        }

        private static void LogExactSceneNativeBootstrapDeferred(Mission mission, string reason, string source)
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (nowUtc < _nextExactSceneNativeBootstrapDeferLogUtc)
                return;

            _nextExactSceneNativeBootstrapDeferLogUtc = nowUtc.AddSeconds(2);
            ExactCampaignArmyBootstrap.LogInitializationDeferred(
                mission,
                reason,
                source + " exact-native-bootstrap");
        }

        private static void TryBridgeMissionPlayerTeamForExactCampaignBootstrap(
            Mission mission,
            Team authoritativeTeam,
            BattleSideEnum authoritativeSide,
            string source)
        {
            if (mission == null || authoritativeTeam == null || authoritativeSide == BattleSideEnum.None)
                return;

            Team previousPlayerTeam = mission.PlayerTeam;
            Team previousPlayerEnemyTeam = mission.PlayerEnemyTeam;
            bool needsBridge =
                previousPlayerTeam == null ||
                previousPlayerTeam.Side != authoritativeSide ||
                !ReferenceEquals(previousPlayerTeam, authoritativeTeam);
            if (!needsBridge)
                return;

            mission.PlayerTeam = authoritativeTeam;

            string previousPlayerTeamText =
                previousPlayerTeam == null
                    ? "null"
                    : previousPlayerTeam.Side + "#" + previousPlayerTeam.TeamIndex;
            string previousPlayerEnemyTeamText =
                previousPlayerEnemyTeam == null
                    ? "null"
                    : previousPlayerEnemyTeam.Side + "#" + previousPlayerEnemyTeam.TeamIndex;
            string appliedPlayerTeamText =
                mission.PlayerTeam == null
                    ? "null"
                    : mission.PlayerTeam.Side + "#" + mission.PlayerTeam.TeamIndex;
            string appliedPlayerEnemyTeamText =
                mission.PlayerEnemyTeam == null
                    ? "null"
                    : mission.PlayerEnemyTeam.Side + "#" + mission.PlayerEnemyTeam.TeamIndex;

            ModLogger.Info(
                "CoopMissionSpawnLogic: bridged mission player teams for exact campaign bootstrap. " +
                "Scene=" + (mission.SceneName ?? "null") +
                " AuthoritativeSide=" + authoritativeSide +
                " PreviousPlayerTeam=" + previousPlayerTeamText +
                " PreviousPlayerEnemyTeam=" + previousPlayerEnemyTeamText +
                " AppliedPlayerTeam=" + appliedPlayerTeamText +
                " AppliedPlayerEnemyTeam=" + appliedPlayerEnemyTeamText +
                " Source=" + (source ?? "unknown"));
        }

        // This bridge keeps the stable vanilla MP preview/spawn lifecycle while CoopBattle owns
        // the higher-level battle flow. It is isolated so we can later replace or shrink it safely.
        private static void RunVanillaSpawnBridgeTick(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer)
                return;

            TryFinalizePendingVanillaSpawnVisuals(mission, source);
        }

        private static void TryUpdateBattlePhaseState(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer || GameNetwork.NetworkPeers == null)
                return;

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            if (currentPhase >= CoopBattlePhase.BattleActive)
                return;

            int assignedPeerCount = 0;
            int previewReadyPeerCount = 0;
            int controlledPeerCount = 0;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
                    continue;

                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                if (missionPeer == null || missionPeer.Team == null || ReferenceEquals(missionPeer.Team, mission.SpectatorTeam))
                    continue;

                BattleSideEnum authoritativeSide = ResolveAuthoritativeSide(missionPeer, mission, source + " phase-update");
                if (authoritativeSide == BattleSideEnum.None)
                    continue;

                assignedPeerCount++;
                if (missionPeer.HasSpawnedAgentVisuals)
                    previewReadyPeerCount++;
                if (missionPeer.ControlledAgent != null)
                    controlledPeerCount++;
            }

            if (controlledPeerCount > 0)
            {
                CoopBattlePhaseRuntimeState.AdvanceToAtLeast(CoopBattlePhase.Deployment, source + " controlled-agent", mission);
                if (assignedPeerCount > 0 && controlledPeerCount >= assignedPeerCount)
                    CoopBattlePhaseRuntimeState.AdvanceToAtLeast(CoopBattlePhase.PreBattleHold, source + " all-peers-controlled", mission);
                return;
            }

            if (previewReadyPeerCount > 0)
            {
                CoopBattlePhaseRuntimeState.AdvanceToAtLeast(CoopBattlePhase.Deployment, source + " visuals-ready", mission);
                return;
            }

            if (assignedPeerCount > 0)
            {
                CoopBattlePhaseRuntimeState.AdvanceToAtLeast(CoopBattlePhase.UnitSelection, source + " side-assigned", mission);
                return;
            }

            CoopBattlePhaseRuntimeState.AdvanceToAtLeast(CoopBattlePhase.SideSelection, source + " waiting-for-side", mission);
        }

        private static bool IsBattleStartReady(Mission mission, out int assignedPeerCount, out int controlledPeerCount)
        {
            assignedPeerCount = 0;
            controlledPeerCount = 0;

            if (mission == null || GameNetwork.NetworkPeers == null)
                return false;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
                    continue;

                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                if (missionPeer == null || missionPeer.Team == null || ReferenceEquals(missionPeer.Team, mission.SpectatorTeam))
                    continue;

                BattleSideEnum authoritativeSide = ResolveAuthoritativeSide(missionPeer, mission, "battle-start-ready");
                if (authoritativeSide == BattleSideEnum.None)
                    continue;

                assignedPeerCount++;
                if (missionPeer.ControlledAgent != null && missionPeer.ControlledAgent.IsActive())
                    controlledPeerCount++;
            }

            return AreBattlefieldArmiesReadyForStart(mission, out _, out _, out _) &&
                assignedPeerCount > 0 &&
                controlledPeerCount >= assignedPeerCount;
        }

        private static bool AreBattlefieldArmiesReadyForStart(
            Mission mission,
            out string readinessSource,
            out int attackerActive,
            out int defenderActive)
        {
            attackerActive = 0;
            defenderActive = 0;

            if (_hasMaterializedBattlefieldArmies)
            {
                readinessSource = "legacy-materialized";
                return true;
            }

            if (!IsUsingNativeExactCampaignArmyBootstrap(mission))
            {
                readinessSource = "not-ready";
                return false;
            }

            attackerActive = CountActiveTeamAgents(mission, BattleSideEnum.Attacker);
            defenderActive = CountActiveTeamAgents(mission, BattleSideEnum.Defender);
            readinessSource =
                "exact-native-bootstrap" +
                " MissionTeamAttacker=" + attackerActive +
                " MissionTeamDefender=" + defenderActive;
            return attackerActive > 0 && defenderActive > 0;
        }

        private static void TryConsumeBattlePhaseRequests(Mission mission)
        {
            if (mission == null || !GameNetwork.IsServer)
                return;

            if (!CoopBattlePhaseBridgeFile.ConsumeStartBattleRequest(out string requestSource))
                return;

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            bool battleStartReady = IsBattleStartReady(mission, out int assignedPeerCount, out int controlledPeerCount);
            if (currentPhase < CoopBattlePhase.PreBattleHold || !battleStartReady)
            {
                bool armiesReady = AreBattlefieldArmiesReadyForStart(mission, out string readinessSource, out int attackerActive, out int defenderActive);
                ModLogger.Info(
                    "CoopMissionSpawnLogic: ignored start battle request because phase is not ready. " +
                    "CurrentPhase=" + currentPhase +
                    " BattleStartReady=" + battleStartReady +
                    " AssignedPeers=" + assignedPeerCount +
                    " ControlledPeers=" + controlledPeerCount +
                    " MaterializedArmies=" + _hasMaterializedBattlefieldArmies +
                    " ArmiesReady=" + armiesReady +
                    " ArmiesReadySource=" + readinessSource +
                    " AttackerActive=" + attackerActive +
                    " DefenderActive=" + defenderActive +
                    " Source=" + (requestSource ?? "unknown"));
                return;
            }

            CoopBattlePhaseRuntimeState.SetPhase(
                CoopBattlePhase.BattleActive,
                "bridge-file start battle request from " + (requestSource ?? "unknown"),
                mission,
                allowRegression: false);
        }

        private static void TryApplyBattlePhaseAiHold(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer)
                return;

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            // Freeze native AI before the first controlled spawn exists.
            // Once a peer controls an agent, global AI pause would also block local locomotion,
            // so Deployment/PreBattleHold rely on formation hold instead.
            bool shouldPauseAi = currentPhase < CoopBattlePhase.Deployment;

            if (!ReferenceEquals(_lastBattlePhaseAiHoldMission, mission))
            {
                _lastBattlePhaseAiHoldMission = mission;
                _lastAppliedBattlePhaseAiHold = null;
            }

            bool currentPauseAi = mission.PauseAITick;
            if (_lastAppliedBattlePhaseAiHold.HasValue &&
                _lastAppliedBattlePhaseAiHold.Value == shouldPauseAi &&
                currentPauseAi == shouldPauseAi)
            {
                return;
            }

            mission.PauseAITick = shouldPauseAi;
            _lastAppliedBattlePhaseAiHold = shouldPauseAi;

            ModLogger.Info(
                "CoopMissionSpawnLogic: battle phase AI hold state applied. " +
                "Phase=" + currentPhase +
                " PauseAITick=" + shouldPauseAi +
                " Source=" + (source ?? "unknown"));
        }

        private static void TryApplyNativeBattleMapWarmupFallback(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer || !IsSceneAwareBattleMapRuntime(mission))
                return;

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            if (currentPhase >= CoopBattlePhase.BattleEnded)
                return;

            if (currentPhase >= CoopBattlePhase.PreBattleHold)
                return;

            MultiplayerWarmupComponent warmupComponent = mission.GetMissionBehavior<MultiplayerWarmupComponent>();
            MultiplayerTimerComponent timerComponent = mission.GetMissionBehavior<MultiplayerTimerComponent>();
            if (warmupComponent == null || timerComponent == null)
                return;

            float remainingTimeSeconds;
            try
            {
                remainingTimeSeconds = timerComponent.GetRemainingTime(false);
            }
            catch
            {
                remainingTimeSeconds = -1f;
            }

            string warmupState = NativeWarmupStateField?.GetValue(warmupComponent)?.ToString() ?? "unknown";
            bool stateNeedsRecovery =
                string.Equals(warmupState, "WaitingForPlayers", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(warmupState, "Ending", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(warmupState, "Ended", StringComparison.OrdinalIgnoreCase);
            bool timerNeedsRefresh =
                remainingTimeSeconds < 0f ||
                remainingTimeSeconds < (NativeBattleMapWarmupFallbackDurationSeconds - 60f);
            DateTime nowUtc = DateTime.UtcNow;

            if (!stateNeedsRecovery &&
                !timerNeedsRefresh &&
                nowUtc < _nextNativeBattleMapWarmupFallbackRefreshUtc)
            {
                return;
            }

            bool refreshedTimer = false;
            try
            {
                timerComponent.StartTimerAsServer(NativeBattleMapWarmupFallbackDurationSeconds);
                refreshedTimer = true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: native battle-map warmup fallback failed to refresh timer: " + ex.Message);
            }

            bool forcedWarmupState = false;
            try
            {
                if (NativeWarmupStateField != null)
                {
                    object inProgressState = Enum.Parse(NativeWarmupStateField.FieldType, "InProgress");
                    NativeWarmupStateField.SetValue(warmupComponent, inProgressState);
                    forcedWarmupState = true;
                }

                if (NativeWarmupCurrentStateStartTimeField != null)
                {
                    MissionTime futureStartTime = MissionTime.SecondsFromNow(NativeBattleMapWarmupFutureStartOffsetSeconds);
                    NativeWarmupCurrentStateStartTimeField.SetValue(warmupComponent, futureStartTime);
                    forcedWarmupState = true;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: native battle-map warmup fallback failed to extend warmup state: " + ex.Message);
            }

            _nextNativeBattleMapWarmupFallbackRefreshUtc = nowUtc.AddSeconds(5);

            string logKey =
                (source ?? "unknown") + "|" +
                currentPhase + "|" +
                warmupState + "|" +
                refreshedTimer + "|" +
                forcedWarmupState;
            if (string.Equals(_lastNativeBattleMapWarmupFallbackLogKey, logKey, StringComparison.Ordinal))
                return;

            _lastNativeBattleMapWarmupFallbackLogKey = logKey;
            ModLogger.Info(
                "CoopMissionSpawnLogic: native battle-map warmup fallback applied. " +
                "Phase=" + currentPhase +
                " WarmupStateBefore=" + warmupState +
                " RemainingSecondsBefore=" + remainingTimeSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                " ForcedWarmupState=" + forcedWarmupState +
                " RefreshedTimer=" + refreshedTimer +
                " TargetDurationSeconds=" + NativeBattleMapWarmupFallbackDurationSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                " Source=" + (source ?? "unknown"));
        }

        private static void TryCompleteBattleIfResolved(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer)
                return;

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            if (currentPhase < CoopBattlePhase.BattleActive || currentPhase >= CoopBattlePhase.BattleEnded)
                return;

            if (_hasTriggeredAuthoritativeBattleCompletion)
                return;

            ResolveActiveBattleSideCounts(mission, out int attackerActive, out int defenderActive, out string countSource);
            TryLogBattleCompletionAudit(attackerActive, defenderActive, countSource, source);
            if (attackerActive > 0 && defenderActive > 0)
                return;

            int attackerReserve;
            int defenderReserve;
            string reserveSource;
            if (IsUsingNativeExactCampaignArmyBootstrap(mission) &&
                ExactCampaignArmyBootstrap.TryGetRemainingTroopCounts(
                    mission,
                    out attackerReserve,
                    out defenderReserve))
            {
                reserveSource = "exact-native-bootstrap";
            }
            else
            {
                attackerReserve = CountMaterializedUnspawnedReserveForSide(BattleSideEnum.Attacker);
                defenderReserve = CountMaterializedUnspawnedReserveForSide(BattleSideEnum.Defender);
                reserveSource = "materialized-runtime";
            }

            if ((attackerActive <= 0 && attackerReserve > 0) ||
                (defenderActive <= 0 && defenderReserve > 0))
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: authoritative battle completion deferred because reserves remain. " +
                    "AttackerActive=" + attackerActive +
                    " DefenderActive=" + defenderActive +
                    " AttackerReserve=" + attackerReserve +
                    " DefenderReserve=" + defenderReserve +
                    " ReserveSource=" + reserveSource +
                    " CountSource=" + (countSource ?? "unknown") +
                    " Source=" + (source ?? "unknown"));
                return;
            }

            _hasTriggeredAuthoritativeBattleCompletion = true;
            _authoritativeBattleCompletionReason =
                attackerActive <= 0 && defenderActive <= 0 ? "mutual-elimination" :
                attackerActive <= 0 ? "attacker-eliminated" :
                "defender-eliminated";
            _authoritativeBattleWinnerSide =
                attackerActive > 0 && defenderActive <= 0 ? BattleSideEnum.Attacker.ToString() :
                defenderActive > 0 && attackerActive <= 0 ? BattleSideEnum.Defender.ToString() :
                string.Empty;

            CoopBattlePhaseRuntimeState.SetPhase(
                CoopBattlePhase.BattleEnded,
                "authoritative battle completion from " + (source ?? "unknown"),
                mission,
                allowRegression: false);

            TryWriteBattleResultSnapshot(
                mission,
                "authoritative battle completion | " +
                (_authoritativeBattleCompletionReason ?? "unknown") +
                " | " + (source ?? "unknown"));

            ModLogger.Info(
                "CoopMissionSpawnLogic: authoritative battle completion detected. " +
                "WinnerSide=" + (string.IsNullOrWhiteSpace(_authoritativeBattleWinnerSide) ? "none" : _authoritativeBattleWinnerSide) +
                " Reason=" + (_authoritativeBattleCompletionReason ?? "unknown") +
                " AttackerActive=" + attackerActive +
                " DefenderActive=" + defenderActive +
                " AttackerReserve=" + attackerReserve +
                " DefenderReserve=" + defenderReserve +
                " ReserveSource=" + reserveSource +
                " CountSource=" + (countSource ?? "unknown") +
                " Source=" + (source ?? "unknown") +
                " AwaitingHostEndMission=True.");
        }

        private static void TryApplyBattlePhaseFormationHold(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer)
                return;

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            bool shouldHoldFormations = currentPhase >= CoopBattlePhase.SideSelection && currentPhase < CoopBattlePhase.BattleActive;
            bool shouldReleaseAndPulse = currentPhase >= CoopBattlePhase.BattleActive;

            if (!ReferenceEquals(_lastBattlePhaseAiHoldMission, mission))
            {
                _battlePhaseHeldFormationKeys.Clear();
                _lastAppliedFormationHoldPhase = null;
            }

            bool phaseChanged = !_lastAppliedFormationHoldPhase.HasValue || _lastAppliedFormationHoldPhase.Value != currentPhase;
            bool shouldRunReleasePass = shouldReleaseAndPulse && phaseChanged;

            // BattleActive release is a one-shot transition step. Re-running it every tick
            // would immediately overwrite commander-issued orders with charge/fire-at-will/AI delegation.
            if (!shouldHoldFormations && !shouldRunReleasePass)
                return;

            int affectedFormationCount = 0;
            int delegatedTeamCount = 0;
            int pulsedAgentCount = 0;
            HashSet<int> currentFormationKeys = shouldHoldFormations ? new HashSet<int>() : null;
            foreach (Team team in mission.Teams)
            {
                if (team == null || team.Side == BattleSideEnum.None || ReferenceEquals(team, mission.SpectatorTeam))
                    continue;

                foreach (Formation formation in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (formation == null || formation.CountOfUnits <= 0)
                        continue;

                    int formationKey = GetBattlePhaseFormationKey(team, formation);
                    if (shouldHoldFormations)
                    {
                        currentFormationKeys?.Add(formationKey);

                        if (ShouldSkipBattlePhaseFormationHold(mission, team, formation))
                        {
                            _battlePhaseHeldFormationKeys.Remove(formationKey);
                            continue;
                        }

                        if (_battlePhaseHeldFormationKeys.Contains(formationKey))
                            continue;

                        formation.SetMovementOrder(MovementOrder.MovementOrderStop);
                        formation.SetFiringOrder(FiringOrder.FiringOrderHoldYourFire);
                        formation.SetControlledByAI(true, true);
                        _battlePhaseHeldFormationKeys.Add(formationKey);
                        affectedFormationCount++;
                    }
                    else if (shouldRunReleasePass)
                    {
                        // Release/pulse must also cover formations that contain the player.
                        // They are intentionally skipped during PreBattleHold, so BattleActive
                        // cannot rely on the held-key set alone.
                        bool wasHeld = _battlePhaseHeldFormationKeys.Remove(formationKey);
                        formation.SetMovementOrder(MovementOrder.MovementOrderCharge);
                        formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
                        formation.SetControlledByAI(true, true);
                        int pulsedFormationAgents = TryPulseFormationAiEngage(formation, team);
                        pulsedAgentCount += pulsedFormationAgents;
                        affectedFormationCount++;

                        bool hasPlayerControlledTroop = (GetServerMemberValue(formation, "HasPlayerControlledTroop") as bool?) ?? false;
                        bool isPlayerTroopInFormation = (GetServerMemberValue(formation, "IsPlayerTroopInFormation") as bool?) ?? false;
                        object playerOwner = GetServerMemberValue(formation, "PlayerOwner");
                        ModLogger.Info(
                            "CoopMissionSpawnLogic: battle phase formation release detail. " +
                            "Team=" + team.Side +
                            " Formation=" + formation.FormationIndex +
                            " UnitCount=" + formation.CountOfUnits +
                            " WasHeld=" + wasHeld +
                            " HasPlayerControlledTroop=" + hasPlayerControlledTroop +
                            " IsPlayerTroopInFormation=" + isPlayerTroopInFormation +
                            " HasPlayerOwner=" + (playerOwner != null) +
                            " PulsedAgents=" + pulsedFormationAgents +
                            " Source=" + (source ?? "unknown"));
                    }
                }

                if (shouldReleaseAndPulse &&
                    team.HasTeamAi &&
                    team.HasAnyEnemyTeamsWithAgents(false))
                {
                    team.ResetTactic();
                    team.DelegateCommandToAI();
                    delegatedTeamCount++;
                }
            }

            if (shouldHoldFormations && currentFormationKeys != null)
                _battlePhaseHeldFormationKeys.RemoveWhere(key => !currentFormationKeys.Contains(key));

            if (!phaseChanged &&
                !shouldHoldFormations)
            {
                return;
            }

            if (!phaseChanged &&
                affectedFormationCount == 0)
            {
                return;
            }

            _lastAppliedFormationHoldPhase = currentPhase;
            ModLogger.Info(
                "CoopMissionSpawnLogic: battle phase formation hold state applied. " +
                "Phase=" + currentPhase +
                " HoldApplied=" + shouldHoldFormations +
                " AffectedFormations=" + affectedFormationCount +
                " DelegatedTeams=" + delegatedTeamCount +
                " PulsedAgents=" + pulsedAgentCount +
                " Source=" + (source ?? "unknown"));
        }

        private static bool ShouldSkipBattlePhaseFormationHold(Mission mission, Team team, Formation formation)
        {
            if (formation == null)
                return true;

            return IsFormationOccupiedByControlledPeer(mission, team, formation);
        }

        private static bool IsFormationOccupiedByControlledPeer(Mission mission, Team team, Formation formation)
        {
            if (mission == null || team == null || formation == null || GameNetwork.NetworkPeers == null)
                return false;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
                    continue;

                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                Agent controlledAgent = missionPeer?.ControlledAgent;
                if (controlledAgent == null || !controlledAgent.IsActive())
                    continue;

                if (!ReferenceEquals(controlledAgent.Team, team))
                    continue;

                Formation controlledFormation = controlledAgent.Formation;
                if (ReferenceEquals(controlledFormation, formation))
                    return true;
            }

            return false;
        }

        private static int GetBattlePhaseFormationKey(Team team, Formation formation)
        {
            int teamKey = ((int)team.Side + 1) * 100;
            int formationKey = formation != null ? (int)formation.FormationIndex : -1;
            return teamKey + formationKey;
        }

        private static int TryPulseFormationAiEngage(Formation formation, Team team)
        {
            if (formation == null || team == null || formation.CountOfUnits <= 0)
                return 0;

            Team enemyTeam = null;
            foreach (Team candidate in team.Mission.Teams)
            {
                if (candidate == null || ReferenceEquals(candidate, team) || ReferenceEquals(candidate, team.Mission.SpectatorTeam))
                    continue;

                if (!team.IsEnemyOf(candidate))
                    continue;

                if (!candidate.FormationsIncludingSpecialAndEmpty.Any(x => x != null && x.CountOfUnits > 0))
                    continue;

                enemyTeam = candidate;
                break;
            }

            if (enemyTeam == null)
                return 0;

            Formation enemyFormation = enemyTeam.FormationsIncludingSpecialAndEmpty.FirstOrDefault(x => x != null && x.CountOfUnits > 0);
            Agent enemyAgent = enemyFormation?.GetFirstUnit();
            int pulsedAgentCount = 0;

            formation.ApplyActionOnEachUnit(agent =>
            {
                if (agent == null || !agent.IsActive() || agent.Controller != AgentControllerType.AI)
                    return;

                agent.SetAutomaticTargetSelection(true);
                agent.SetWatchState(Agent.WatchState.Alarmed);
                agent.SetAlarmState(Agent.AIStateFlag.Alarmed);
                if (enemyFormation != null)
                    agent.SetTargetFormationIndex((int)enemyFormation.FormationIndex);
                if (enemyAgent != null && enemyAgent.IsActive())
                    agent.SetTargetAgent(enemyAgent);
                agent.ForceAiBehaviorSelection();
                pulsedAgentCount++;
            }, null);

            return pulsedAgentCount;
        }

        private static void TryEnsureBattlefieldArmiesMaterialized(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer)
                return;

            if (!ReferenceEquals(_lastMaterializedArmyMission, mission))
            {
                _lastMaterializedArmyMission = mission;
                _hasMaterializedBattlefieldArmies = false;
                _hasLoggedMaterializedEquipmentCoverageSummary = false;
                _lastAlignedControlledAgentIndexByPeer.Clear();
                _materializedArmyEntryIdByAgentIndex.Clear();
                _materializedArmySideByAgentIndex.Clear();
                _materializedAgentInstanceByIndex.Clear();
                _loggedAutomatedMaterializedEntrySkipIds.Clear();
                _loggedSuppressedMaterializedEquipmentKeys.Clear();
                MaterializedEquipmentResolutionSourceCounts.Clear();
                MaterializedEquipmentMissCounts.Clear();
                MaterializedEquipmentNormalizedFallbackCounts.Clear();
                ResetMaterializedCombatProfileRuntimeState();
                _nextIncompleteBattleSnapshotRefreshUtc = DateTime.MinValue;
                _nextIncompleteBattleSnapshotLogUtc = DateTime.MinValue;
                _nextExactSceneMaterializationDeferLogUtc = DateTime.MinValue;
                _exactScenePostPossessionMaterializationResumeUtc = DateTime.MinValue;
                _nextInitialMaterializedArmyPulseUtc = DateTime.MinValue;
                _nextMaterializedArmyReinforcementPulseUtc = DateTime.MinValue;
                _nextExactSceneNativeBootstrapDeferLogUtc = DateTime.MinValue;
            }

            if (_hasMaterializedBattlefieldArmies)
                return;

            if (IsUsingNativeExactCampaignArmyBootstrap(mission))
                return;

            Team attackerTeam = mission.Teams?.Attacker;
            Team defenderTeam = mission.Teams?.Defender;
            if (attackerTeam == null || defenderTeam == null)
                return;

            if (ShouldDeferExactSceneInitialBattlefieldMaterialization(mission, out string exactSceneMaterializationDeferReason))
            {
                DateTime nowUtc = DateTime.UtcNow;
                if (nowUtc >= _nextExactSceneMaterializationDeferLogUtc)
                {
                    _nextExactSceneMaterializationDeferLogUtc = nowUtc.AddSeconds(2);
                    ModLogger.Info(
                        "CoopMissionSpawnLogic: deferring initial battlefield materialization on exact campaign scene until a synchronized peer has a controlled agent. " +
                        exactSceneMaterializationDeferReason +
                        " Source=" + (source ?? "unknown"));
                }

                return;
            }

            if (ShouldDeferExactScenePostPossessionMaterialization(mission, out string exactScenePostPossessionDeferReason))
            {
                DateTime nowUtc = DateTime.UtcNow;
                if (nowUtc >= _nextExactSceneMaterializationDeferLogUtc)
                {
                    _nextExactSceneMaterializationDeferLogUtc = nowUtc.AddSeconds(2);
                    ModLogger.Info(
                        "CoopMissionSpawnLogic: deferring automated battlefield materialization on exact campaign scene after player possession to let spawn handoff settle. " +
                        exactScenePostPossessionDeferReason +
                        " Source=" + (source ?? "unknown"));
                }

                return;
            }

            if (!IsBattleSnapshotReadyForMaterialization(out string readinessSummary))
            {
                DateTime nowUtc = DateTime.UtcNow;
                if (nowUtc >= _nextIncompleteBattleSnapshotRefreshUtc)
                {
                    _nextIncompleteBattleSnapshotRefreshUtc = nowUtc.AddMilliseconds(750);
                    RefreshAllowedTroopsFromRoster(source + " awaiting snapshot");
                }

                if (!IsBattleSnapshotReadyForMaterialization(out readinessSummary))
                {
                    if (nowUtc >= _nextIncompleteBattleSnapshotLogUtc)
                    {
                        _nextIncompleteBattleSnapshotLogUtc = nowUtc.AddSeconds(2);
                        ModLogger.Info(
                            "CoopMissionSpawnLogic: delaying battlefield materialization because battle snapshot is incomplete. " +
                            readinessSummary +
                            " Source=" + (source ?? "unknown"));
                    }
                    return;
                }

                _nextIncompleteBattleSnapshotLogUtc = DateTime.MinValue;
            }

            DateTime pulseNowUtc = DateTime.UtcNow;
            if (pulseNowUtc < _nextInitialMaterializedArmyPulseUtc)
                return;

            _nextInitialMaterializedArmyPulseUtc = pulseNowUtc.AddSeconds(InitialMaterializedArmyPulseIntervalSeconds);

            int attackerTargetCount = GetInitialMaterializedArmyTargetCount(BattleSideEnum.Attacker);
            int defenderTargetCount = GetInitialMaterializedArmyTargetCount(BattleSideEnum.Defender);
            int attackerCurrentCount = GetCurrentMaterializedSpawnCountForSide(BattleSideEnum.Attacker);
            int defenderCurrentCount = GetCurrentMaterializedSpawnCountForSide(BattleSideEnum.Defender);

            bool exactCampaignSceneHasControlledPeer = false;
            if (SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty) &&
                GameNetwork.NetworkPeers != null)
            {
                foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
                {
                    if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
                        continue;

                    MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                    if (HasActiveControlledAgent(missionPeer))
                    {
                        exactCampaignSceneHasControlledPeer = true;
                        break;
                    }
                }
            }

            int attackerSpawnBudget = Math.Min(
                InitialMaterializedArmySpawnPulseBudgetPerSide,
                Math.Max(0, attackerTargetCount - attackerCurrentCount));
            int defenderSpawnBudget = Math.Min(
                InitialMaterializedArmySpawnPulseBudgetPerSide,
                Math.Max(0, defenderTargetCount - defenderCurrentCount));

            if (exactCampaignSceneHasControlledPeer &&
                defenderSpawnBudget > 0 &&
                defenderCurrentCount <= 0)
            {
                attackerSpawnBudget = 0;
                if (pulseNowUtc >= _nextExactSceneMaterializationDeferLogUtc)
                {
                    _nextExactSceneMaterializationDeferLogUtc = pulseNowUtc.AddSeconds(2);
                    ModLogger.Info(
                        "CoopMissionSpawnLogic: prioritizing defender-side initial battlefield materialization on exact campaign scene after player possession. " +
                        "Scene=" + (mission.SceneName ?? "null") +
                        " AttackerCurrent=" + attackerCurrentCount +
                        " DefenderCurrent=" + defenderCurrentCount +
                        " AttackerTarget=" + attackerTargetCount +
                        " DefenderTarget=" + defenderTargetCount +
                        " Source=" + (source ?? "unknown"));
                }
            }

            int attackerCount = attackerSpawnBudget > 0
                ? MaterializeArmyForSide(mission, attackerTeam, BattleSideEnum.Attacker, source, attackerSpawnBudget)
                : 0;
            int defenderCount = defenderSpawnBudget > 0
                ? MaterializeArmyForSide(mission, defenderTeam, BattleSideEnum.Defender, source, defenderSpawnBudget)
                : 0;

            attackerCurrentCount = GetCurrentMaterializedSpawnCountForSide(BattleSideEnum.Attacker);
            defenderCurrentCount = GetCurrentMaterializedSpawnCountForSide(BattleSideEnum.Defender);

            bool attackerReady = attackerTargetCount <= 0 || attackerCurrentCount >= attackerTargetCount;
            bool defenderReady = defenderTargetCount <= 0 || defenderCurrentCount >= defenderTargetCount;
            if (!attackerReady || !defenderReady)
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: initial battlefield materialization pulse. " +
                    "AttackerSpawnedThisPulse=" + attackerCount +
                    " AttackerCurrent=" + attackerCurrentCount + "/" + attackerTargetCount +
                    " DefenderSpawnedThisPulse=" + defenderCount +
                    " DefenderCurrent=" + defenderCurrentCount + "/" + defenderTargetCount +
                    " Source=" + (source ?? "unknown"));
                return;
            }

            if (attackerCurrentCount <= 0 && defenderCurrentCount <= 0)
                return;

            _hasMaterializedBattlefieldArmies = true;
            ModLogger.Info(
                "CoopMissionSpawnLogic: battlefield armies materialized. " +
                "AttackerAgents=" + attackerCurrentCount +
                " DefenderAgents=" + defenderCurrentCount +
                " BattleSizeBudget=" + GetConfiguredBattleSizeBudget() +
                " AttackerSideCap=" + GetMaterializedArmyAgentsPerSideCap(BattleSideEnum.Attacker, GetAllowedControlEntryStatesSnapshot(BattleSideEnum.Attacker).Count) +
                " DefenderSideCap=" + GetMaterializedArmyAgentsPerSideCap(BattleSideEnum.Defender, GetAllowedControlEntryStatesSnapshot(BattleSideEnum.Defender).Count) +
                " Source=" + (source ?? "unknown"));
        }

        private static void TrySpawnMaterializedBattlefieldReinforcements(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer || !_hasMaterializedBattlefieldArmies)
                return;

            if (IsUsingNativeExactCampaignArmyBootstrap(mission))
                return;

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            if (currentPhase < CoopBattlePhase.BattleActive || currentPhase >= CoopBattlePhase.BattleEnded)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            if (nowUtc < _nextMaterializedArmyReinforcementPulseUtc)
                return;

            _nextMaterializedArmyReinforcementPulseUtc = nowUtc.AddSeconds(MaterializedArmyReinforcementPulseIntervalSeconds);

            Team attackerTeam = mission.Teams?.Attacker;
            Team defenderTeam = mission.Teams?.Defender;
            if (attackerTeam == null || defenderTeam == null)
                return;

            int attackerSpawned = SpawnMaterializedReinforcementWaveForSide(mission, attackerTeam, BattleSideEnum.Attacker, source);
            int defenderSpawned = SpawnMaterializedReinforcementWaveForSide(mission, defenderTeam, BattleSideEnum.Defender, source);
            if (attackerSpawned <= 0 && defenderSpawned <= 0)
                return;

            int pulsedAgents = 0;
            if (attackerSpawned > 0)
                pulsedAgents += TryActivateBattleActiveReinforcementAi(attackerTeam);
            if (defenderSpawned > 0)
                pulsedAgents += TryActivateBattleActiveReinforcementAi(defenderTeam);

            ModLogger.Info(
                "CoopMissionSpawnLogic: materialized reinforcement wave spawned. " +
                "AttackerSpawned=" + attackerSpawned +
                " DefenderSpawned=" + defenderSpawned +
                " PulsedAgents=" + pulsedAgents +
                " BattleSizeBudget=" + GetConfiguredBattleSizeBudget() +
                " Source=" + (source ?? "unknown"));
        }

        private static int SpawnMaterializedReinforcementWaveForSide(Mission mission, Team team, BattleSideEnum side, string source)
        {
            if (mission == null || team == null || side == BattleSideEnum.None)
                return 0;

            IReadOnlyList<RosterEntryState> entryStates = GetAutomatedMaterializableEntryStatesSnapshot(side);
            if (entryStates == null || entryStates.Count == 0)
                return 0;

            int sideCap = GetMaterializedArmyAgentsPerSideCap(side, entryStates.Count);
            int totalAvailableForSide = 0;
            int activeForSide = 0;
            var candidates = new List<MaterializedArmyReinforcementCandidate>();

            foreach (RosterEntryState entryState in entryStates)
            {
                string spawnTemplateId = ResolveEntrySpawnTemplateId(entryState);
                if (entryState == null || string.IsNullOrWhiteSpace(spawnTemplateId))
                    continue;

                int availableCount = Math.Max(0, entryState.Count - entryState.WoundedCount);
                if (availableCount <= 0)
                    continue;

                totalAvailableForSide += availableCount;

                _materializedBattleResultEntriesByEntryId.TryGetValue(entryState.EntryId ?? string.Empty, out MaterializedBattleResultEntryRuntimeState runtimeState);
                int spawnedCount = Math.Max(0, runtimeState?.MaterializedSpawnCount ?? 0);
                int activeCount = Math.Max(0, runtimeState?.ActiveCount ?? 0);
                activeForSide += activeCount;

                int totalRemainingCount = Math.Max(0, availableCount - spawnedCount);
                int concurrentRemainingCapacity = Math.Max(0, Math.Min(availableCount, GetMaterializedAgentsPerEntryCap(side, entryStates.Count)) - activeCount);
                if (totalRemainingCount <= 0 || concurrentRemainingCapacity <= 0)
                    continue;

                string resolvedTroopSource;
                BasicCharacterObject troop = ResolveMaterializedArmyCharacter(entryState, spawnTemplateId, out resolvedTroopSource);
                if (troop == null)
                    continue;

                candidates.Add(new MaterializedArmyReinforcementCandidate
                {
                    EntryState = entryState,
                    Troop = troop,
                    ResolvedTroopSource = resolvedTroopSource,
                    AvailableCount = availableCount,
                    ActiveCount = activeCount,
                    SpawnedCount = spawnedCount,
                    TotalRemainingCount = totalRemainingCount,
                    ConcurrentRemainingCapacity = concurrentRemainingCapacity,
                });
            }

            if (candidates.Count == 0)
                return 0;

            int targetActiveForSide = Math.Min(sideCap, totalAvailableForSide);
            int remainingSideNeed = Math.Max(0, targetActiveForSide - activeForSide);
            if (remainingSideNeed <= 0)
                return 0;

            int spawnedCountForSide = 0;
            var queuedByEntryId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (MaterializedArmyReinforcementCandidate candidate in candidates.Where(item => item.ActiveCount <= 0))
            {
                if (remainingSideNeed <= 0)
                    break;

                int seedCount = GetReinforcementSpawnCount(candidate, queuedByEntryId, remainingSideNeed, seedOneBody: true);
                if (seedCount <= 0)
                    continue;

                int spawned = SpawnMaterializedAgentsForEntry(
                    mission,
                    team,
                    side,
                    candidate.Troop,
                    candidate.EntryState,
                    seedCount,
                    activeForSide + spawnedCountForSide,
                    source + " reinforcement character=" + candidate.ResolvedTroopSource + " pass=seed",
                    isReinforcement: true);
                if (spawned <= 0)
                    continue;

                queuedByEntryId[candidate.EntryState.EntryId ?? string.Empty] = GetQueuedReinforcementCount(queuedByEntryId, candidate.EntryState.EntryId) + spawned;
                spawnedCountForSide += spawned;
                remainingSideNeed = Math.Max(0, remainingSideNeed - spawned);
            }

            foreach (MaterializedArmyReinforcementCandidate candidate in candidates)
            {
                if (remainingSideNeed <= 0)
                    break;

                int fillCount = GetReinforcementSpawnCount(candidate, queuedByEntryId, remainingSideNeed, seedOneBody: false);
                if (fillCount <= 0)
                    continue;

                int spawned = SpawnMaterializedAgentsForEntry(
                    mission,
                    team,
                    side,
                    candidate.Troop,
                    candidate.EntryState,
                    fillCount,
                    activeForSide + spawnedCountForSide,
                    source + " reinforcement character=" + candidate.ResolvedTroopSource + " pass=fill",
                    isReinforcement: true);
                if (spawned <= 0)
                    continue;

                queuedByEntryId[candidate.EntryState.EntryId ?? string.Empty] = GetQueuedReinforcementCount(queuedByEntryId, candidate.EntryState.EntryId) + spawned;
                spawnedCountForSide += spawned;
                remainingSideNeed = Math.Max(0, remainingSideNeed - spawned);
            }

            return spawnedCountForSide;
        }

        private static int GetReinforcementSpawnCount(
            MaterializedArmyReinforcementCandidate candidate,
            IReadOnlyDictionary<string, int> queuedByEntryId,
            int remainingSideNeed,
            bool seedOneBody)
        {
            if (candidate == null || remainingSideNeed <= 0)
                return 0;

            int alreadyQueued = GetQueuedReinforcementCount(queuedByEntryId, candidate.EntryState?.EntryId);
            int remainingTotalCount = Math.Max(0, candidate.TotalRemainingCount - alreadyQueued);
            int remainingConcurrentCapacity = Math.Max(0, candidate.ConcurrentRemainingCapacity - alreadyQueued);
            int allowed = Math.Min(remainingSideNeed, Math.Min(remainingTotalCount, remainingConcurrentCapacity));
            if (allowed <= 0)
                return 0;

            return seedOneBody ? Math.Min(1, allowed) : allowed;
        }

        private static int GetQueuedReinforcementCount(IReadOnlyDictionary<string, int> queuedByEntryId, string entryId)
        {
            if (queuedByEntryId == null || string.IsNullOrWhiteSpace(entryId))
                return 0;

            return queuedByEntryId.TryGetValue(entryId, out int queuedCount)
                ? Math.Max(0, queuedCount)
                : 0;
        }

        private static int TryActivateBattleActiveReinforcementAi(Team team)
        {
            if (team == null || team.Side == BattleSideEnum.None || ReferenceEquals(team, team.Mission?.SpectatorTeam))
                return 0;

            int pulsedAgentCount = 0;
            foreach (Formation formation in team.FormationsIncludingSpecialAndEmpty)
            {
                if (formation == null || formation.CountOfUnits <= 0)
                    continue;

                formation.SetMovementOrder(MovementOrder.MovementOrderCharge);
                formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
                formation.SetControlledByAI(true, true);
                pulsedAgentCount += TryPulseFormationAiEngage(formation, team);
            }

            if (team.HasTeamAi && team.HasAnyEnemyTeamsWithAgents(false))
            {
                team.ResetTactic();
                team.DelegateCommandToAI();
            }

            return pulsedAgentCount;
        }

        private static int CountMaterializedUnspawnedReserveForSide(BattleSideEnum side)
        {
            if (side == BattleSideEnum.None)
                return 0;

            int reserveCount = 0;
            IReadOnlyList<RosterEntryState> entryStates = GetAutomatedMaterializableEntryStatesSnapshot(side);
            foreach (RosterEntryState entryState in entryStates)
            {
                if (entryState == null)
                    continue;

                int availableCount = Math.Max(0, entryState.Count - entryState.WoundedCount);
                if (availableCount <= 0)
                    continue;

                _materializedBattleResultEntriesByEntryId.TryGetValue(entryState.EntryId ?? string.Empty, out MaterializedBattleResultEntryRuntimeState runtimeState);
                int spawnedCount = Math.Max(0, runtimeState?.MaterializedSpawnCount ?? 0);
                reserveCount += Math.Max(0, availableCount - spawnedCount);
            }

            return reserveCount;
        }

        private static int GetInitialMaterializedArmyTargetCount(BattleSideEnum side)
        {
            if (side == BattleSideEnum.None)
                return 0;

            IReadOnlyList<RosterEntryState> entryStates = GetAutomatedMaterializableEntryStatesSnapshot(side);
            if (entryStates != null && entryStates.Count > 0)
            {
                int sideCap = GetMaterializedArmyAgentsPerSideCap(side, entryStates.Count);
                int totalAvailable = entryStates.Sum(entryState => Math.Max(0, (entryState?.Count ?? 0) - (entryState?.WoundedCount ?? 0)));
                return Math.Max(0, Math.Min(sideCap, totalAvailable));
            }

            IReadOnlyList<string> troopIds = GetGenericMaterializedTroopFallbackIds(side);
            if (troopIds == null || troopIds.Count <= 0)
                return 0;

            int fallbackSideCap = GetMaterializedArmyAgentsPerSideCap(side, troopIds.Count);
            int fallbackAvailable = troopIds.Count * GetFallbackMaterializedAgentsPerTroopCap(side, troopIds.Count);
            return Math.Max(0, Math.Min(fallbackSideCap, fallbackAvailable));
        }

        private static int GetCurrentMaterializedSpawnCountForSide(BattleSideEnum side)
        {
            if (side == BattleSideEnum.None || _materializedBattleResultEntriesByEntryId.Count == 0)
                return 0;

            string sideId = side.ToString();
            int spawnedCount = 0;
            foreach (MaterializedBattleResultEntryRuntimeState entryState in _materializedBattleResultEntriesByEntryId.Values)
            {
                if (entryState == null || !string.Equals(entryState.SideId, sideId, StringComparison.OrdinalIgnoreCase))
                    continue;

                spawnedCount += Math.Max(0, entryState.MaterializedSpawnCount);
            }

            return spawnedCount;
        }

        private static IReadOnlyList<RosterEntryState> GetAutomatedMaterializableEntryStatesSnapshot(BattleSideEnum side)
        {
            IReadOnlyList<RosterEntryState> entryStates = GetAllowedControlEntryStatesSnapshot(side);
            if (entryStates == null || entryStates.Count == 0)
                return Array.Empty<RosterEntryState>();

            var filteredEntries = new List<RosterEntryState>(entryStates.Count);
            var exactPlayerPartyFallbackEntries = new List<RosterEntryState>(entryStates.Count);
            foreach (RosterEntryState entryState in entryStates)
            {
                string skipReason = GetAutomatedMaterializedSpawnEntrySkipReason(entryState);
                if (string.IsNullOrEmpty(skipReason))
                {
                    filteredEntries.Add(entryState);
                    continue;
                }

                if (string.Equals(skipReason, "player-party-entry", StringComparison.OrdinalIgnoreCase) &&
                    IsAutomatedMaterializedExactPlayerPartyFallbackCandidate(entryState))
                {
                    exactPlayerPartyFallbackEntries.Add(entryState);
                    continue;
                }

                LogAutomatedMaterializedSpawnEntrySkip(entryState, skipReason);
            }

            if (filteredEntries.Count > 0)
                return filteredEntries;

            if (exactPlayerPartyFallbackEntries.Count > 0)
            {
                string logKey = "exact-player-party-fallback:" + side;
                if (_loggedAutomatedMaterializedEntrySkipIds.Add(logKey))
                {
                    ModLogger.Info(
                        "CoopMissionSpawnLogic: preserving non-hero player-party exact roster entries for automated materialization because filtering would otherwise leave side empty. " +
                        "Side=" + side +
                        " RawEntryCount=" + entryStates.Count +
                        " PreservedEntryCount=" + exactPlayerPartyFallbackEntries.Count);
                }

                return exactPlayerPartyFallbackEntries;
            }

            return Array.Empty<RosterEntryState>();
        }

        private static string GetAutomatedMaterializedSpawnEntrySkipReason(RosterEntryState entryState)
        {
            if (entryState == null)
                return "null-entry";

            bool isHeroEntry =
                entryState.IsHero ||
                !string.IsNullOrWhiteSpace(entryState.HeroRole) ||
                string.Equals(entryState.OriginalCharacterId, "main_hero", StringComparison.OrdinalIgnoreCase);
            if (isHeroEntry)
                return "hero-entry";

            bool isPlayerPartyEntry =
                string.Equals(entryState.PartyId, "player_party", StringComparison.OrdinalIgnoreCase);
            return isPlayerPartyEntry
                ? "player-party-entry"
                : string.Empty;
        }

        private static bool IsAutomatedMaterializedExactPlayerPartyFallbackCandidate(RosterEntryState entryState)
        {
            if (entryState == null)
                return false;

            return string.Equals(entryState.PartyId, "player_party", StringComparison.OrdinalIgnoreCase) &&
                   !entryState.IsHero &&
                   string.IsNullOrWhiteSpace(entryState.HeroRole) &&
                   !string.Equals(entryState.OriginalCharacterId, "main_hero", StringComparison.OrdinalIgnoreCase) &&
                   !string.IsNullOrWhiteSpace(ResolveEntrySpawnTemplateId(entryState));
        }

        private static void LogAutomatedMaterializedSpawnEntrySkip(RosterEntryState entryState, string reason)
        {
            string spawnTemplateId = ResolveEntrySpawnTemplateId(entryState);
            string entryKey = entryState?.EntryId ?? entryState?.OriginalCharacterId ?? spawnTemplateId ?? "(unknown)";
            if (!_loggedAutomatedMaterializedEntrySkipIds.Add(entryKey))
                return;

            ModLogger.Info(
                "CoopMissionSpawnLogic: skipping automated materialized spawn entry. " +
                "Reason=" + (reason ?? "unknown") +
                " " +
                "EntryId=" + (entryState?.EntryId ?? "null") +
                " PartyId=" + (entryState?.PartyId ?? "null") +
                " OriginalCharacterId=" + (entryState?.OriginalCharacterId ?? "null") +
                " HeroRole=" + (entryState?.HeroRole ?? "null") +
                " IsHero=" + (entryState?.IsHero ?? false) +
                " SpawnTemplateId=" + (spawnTemplateId ?? "null"));
        }

        private static IReadOnlyList<string> GetGenericMaterializedTroopFallbackIds(BattleSideEnum side)
        {
            if (side == BattleSideEnum.None)
                return Array.Empty<string>();

            IReadOnlyList<RosterEntryState> rawEntryStates = GetAllowedControlEntryStatesSnapshot(side);
            IReadOnlyList<string> allowedTroopIds = GetAllowedControlTroopIdsSnapshot(side);
            if (allowedTroopIds == null || allowedTroopIds.Count <= 0)
                return Array.Empty<string>();

            if (rawEntryStates == null || rawEntryStates.Count <= 0)
                return allowedTroopIds;

            IReadOnlyList<RosterEntryState> automatedEntryStates = GetAutomatedMaterializableEntryStatesSnapshot(side);
            if (automatedEntryStates != null && automatedEntryStates.Count > 0)
            {
                string logKey = "generic-fallback-suppressed:" + side;
                if (_loggedAutomatedMaterializedEntrySkipIds.Add(logKey))
                {
                    ModLogger.Info(
                        "CoopMissionSpawnLogic: suppressed generic allowed-troop fallback for automated materialization because materializable roster entries exist. " +
                        "Side=" + side +
                        " RawEntryCount=" + rawEntryStates.Count +
                        " MaterializableEntryCount=" + automatedEntryStates.Count);
                }

                return Array.Empty<string>();
            }

            var safeTroopIds = new List<string>(allowedTroopIds.Count);
            foreach (string troopId in allowedTroopIds)
            {
                if (string.IsNullOrWhiteSpace(troopId))
                    continue;

                BasicCharacterObject troop = ResolveAllowedCharacter(troopId);
                bool isHeroTroop =
                    troop?.IsHero == true ||
                    troopId.IndexOf("_hero", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isHeroTroop)
                    continue;

                if (safeTroopIds.Contains(troopId, StringComparer.OrdinalIgnoreCase))
                    continue;

                safeTroopIds.Add(troopId);
            }

            if (safeTroopIds.Count > 0)
            {
                string logKey = "generic-fallback-sanitized:" + side;
                if (_loggedAutomatedMaterializedEntrySkipIds.Add(logKey))
                {
                    ModLogger.Info(
                        "CoopMissionSpawnLogic: using sanitized generic allowed-troop fallback for automated materialization because filtered roster entries left side empty. " +
                        "Side=" + side +
                        " RawEntryCount=" + rawEntryStates.Count +
                        " SafeTroopIds=[" + string.Join(", ", safeTroopIds) + "]");
                }

                return safeTroopIds;
            }

            string emptyLogKey = "generic-fallback-empty:" + side;
            if (_loggedAutomatedMaterializedEntrySkipIds.Add(emptyLogKey))
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: suppressed generic allowed-troop fallback for automated materialization because only hero-class fallback ids remained after filtering. " +
                    "Side=" + side +
                    " RawEntryCount=" + rawEntryStates.Count +
                    " AllowedTroopIds=[" + string.Join(", ", allowedTroopIds) + "]");
            }

            return Array.Empty<string>();
        }

        private static bool IsBattleSnapshotReadyForMaterialization(out string readinessSummary)
        {
            BattleRuntimeState rosterState = BattleSnapshotRuntimeState.GetState();
            BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
            int sideCount = 0;
            int populatedSideCount = 0;
            bool hasAttacker = false;
            bool hasDefender = false;

            if (rosterState?.Sides != null && rosterState.Sides.Count > 0)
            {
                sideCount = rosterState.Sides.Count;
                foreach (BattleSideState sideState in rosterState.Sides)
                {
                    BattleSideEnum side = ResolveBattleSideFromState(sideState);
                    int entryCount = sideState?.Entries?.Count ?? 0;
                    if (entryCount <= 0)
                        continue;

                    populatedSideCount++;
                    if (side == BattleSideEnum.Attacker)
                        hasAttacker = true;
                    else if (side == BattleSideEnum.Defender)
                        hasDefender = true;
                }
            }
            else if (snapshot?.Sides != null && snapshot.Sides.Count > 0)
            {
                sideCount = snapshot.Sides.Count;
                foreach (BattleSideSnapshotMessage sideSnapshot in snapshot.Sides)
                {
                    BattleSideEnum side = ResolveBattleSideFromSnapshot(sideSnapshot);
                    int troopCount = sideSnapshot?.Troops?.Count ?? 0;
                    if (troopCount <= 0)
                        continue;

                    populatedSideCount++;
                    if (side == BattleSideEnum.Attacker)
                        hasAttacker = true;
                    else if (side == BattleSideEnum.Defender)
                        hasDefender = true;
                }
            }

            readinessSummary =
                "BattleId=" + (snapshot?.BattleId ?? "null") +
                " SnapshotSource=" + (BattleSnapshotRuntimeState.GetSource() ?? "unknown") +
                " Sides=" + sideCount +
                " PopulatedSides=" + populatedSideCount +
                " HasAttacker=" + hasAttacker +
                " HasDefender=" + hasDefender;

            return sideCount >= 2 && populatedSideCount >= 2 && hasAttacker && hasDefender;
        }

        private static bool ShouldDeferExactSceneInitialBattlefieldMaterialization(Mission mission, out string reason)
        {
            reason = string.Empty;
            if (mission == null || !IsSceneAwareBattleMapRuntime(mission))
                return false;

            if (IsUsingNativeExactCampaignArmyBootstrap(mission))
            {
                reason = "native exact-scene bootstrap active";
                return true;
            }

            if (!SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName))
                return false;

            if (_hasMaterializedBattlefieldArmies || GameNetwork.NetworkPeers == null)
                return false;

            int synchronizedPeerCount = 0;
            int controlledPeerCount = 0;
            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
                    continue;

                synchronizedPeerCount++;
                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                Agent controlledAgent = missionPeer?.ControlledAgent;
                if (controlledAgent != null && controlledAgent.IsActive())
                    controlledPeerCount++;
            }

            if (synchronizedPeerCount <= 0 || controlledPeerCount > 0)
                return false;

            reason =
                "Scene=" + (mission.SceneName ?? "null") +
                " SynchronizedPeers=" + synchronizedPeerCount +
                " ControlledPeers=" + controlledPeerCount;
            return true;
        }

        private static bool ShouldDeferExactScenePostPossessionMaterialization(Mission mission, out string reason)
        {
            reason = string.Empty;
            if (mission == null || _hasMaterializedBattlefieldArmies || !IsSceneAwareBattleMapRuntime(mission))
                return false;

            if (IsUsingNativeExactCampaignArmyBootstrap(mission))
            {
                reason = "native exact-scene bootstrap active";
                return true;
            }

            if (!SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty))
                return false;

            DateTime nowUtc = DateTime.UtcNow;
            if (_exactScenePostPossessionMaterializationResumeUtc <= nowUtc)
                return false;

            int controlledPeerCount = 0;
            if (GameNetwork.NetworkPeers != null)
            {
                foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
                {
                    if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
                        continue;

                    MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                    if (HasActiveControlledAgent(missionPeer))
                        controlledPeerCount++;
                }
            }

            if (controlledPeerCount <= 0)
                return false;

            reason =
                "Scene=" + (mission.SceneName ?? "null") +
                " ControlledPeers=" + controlledPeerCount +
                " RemainingSeconds=" + (_exactScenePostPossessionMaterializationResumeUtc - nowUtc)
                    .TotalSeconds
                    .ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        private static int MaterializeArmyForSide(
            Mission mission,
            Team team,
            BattleSideEnum side,
            string source,
            int maxSpawnCount = int.MaxValue)
        {
            if (mission == null || team == null || side == BattleSideEnum.None || maxSpawnCount <= 0)
                return 0;

            int spawnedCount = 0;
            int currentSpawnedForSide = GetCurrentMaterializedSpawnCountForSide(side);
            IReadOnlyList<RosterEntryState> entryStates = GetAutomatedMaterializableEntryStatesSnapshot(side);
            int sideCap = GetMaterializedArmyAgentsPerSideCap(side, entryStates.Count);
            if (entryStates.Count > 0)
            {
                var materializableEntries = new List<(RosterEntryState EntryState, BasicCharacterObject Troop, int AvailableCount, string ResolvedTroopSource)>();
                foreach (RosterEntryState entryState in entryStates)
                {
                    string spawnTemplateId = ResolveEntrySpawnTemplateId(entryState);
                    if (entryState == null || string.IsNullOrWhiteSpace(spawnTemplateId))
                        continue;

                    int availableCount = Math.Max(0, entryState.Count - entryState.WoundedCount);
                    if (availableCount <= 0)
                        continue;

                    string resolvedTroopSource;
                    BasicCharacterObject troop = ResolveMaterializedArmyCharacter(entryState, spawnTemplateId, out resolvedTroopSource);
                    if (troop == null)
                    {
                        ModLogger.Info(
                            "CoopMissionSpawnLogic: materialized entry character resolution failed. " +
                            "Side=" + side +
                            " EntryId=" + (entryState.EntryId ?? "null") +
                            " OriginalCharacterId=" + (entryState.OriginalCharacterId ?? "null") +
                            " SpawnTemplateId=" + (spawnTemplateId ?? "null") +
                            " Source=" + (source ?? "unknown"));
                        continue;
                    }

                    materializableEntries.Add((entryState, troop, availableCount, resolvedTroopSource));
                }

                // First pass: ensure each distinct roster entry gets at least one spawned body
                // before large stacks consume the side cap. This preserves representation for
                // hero/ranged/specialized entries such as crossbowmen under tight limits.
                foreach ((RosterEntryState entryState, BasicCharacterObject troop, int availableCount, string resolvedTroopSource) in materializableEntries)
                {
                    int remainingCapacity = sideCap - (currentSpawnedForSide + spawnedCount);
                    int remainingPulseBudget = maxSpawnCount - spawnedCount;
                    if (remainingCapacity <= 0 || remainingPulseBudget <= 0)
                        break;

                    _materializedBattleResultEntriesByEntryId.TryGetValue(entryState.EntryId ?? string.Empty, out MaterializedBattleResultEntryRuntimeState runtimeState);
                    int alreadySpawnedCount = Math.Max(0, runtimeState?.MaterializedSpawnCount ?? 0);
                    if (alreadySpawnedCount > 0)
                        continue;

                    int remainingAvailableCount = Math.Max(0, availableCount - alreadySpawnedCount);
                    int seedCount = Math.Min(1, Math.Min(remainingAvailableCount, Math.Min(remainingCapacity, remainingPulseBudget)));
                    if (seedCount <= 0)
                        continue;

                    spawnedCount += SpawnMaterializedAgentsForEntry(
                        mission,
                        team,
                        side,
                        troop,
                        entryState,
                        seedCount,
                        currentSpawnedForSide + spawnedCount,
                        source + " character=" + resolvedTroopSource + " pass=seed");
                }

                // Second pass: fill remaining side capacity up to per-entry caps.
                foreach ((RosterEntryState entryState, BasicCharacterObject troop, int availableCount, string resolvedTroopSource) in materializableEntries)
                {
                    int remainingCapacity = sideCap - (currentSpawnedForSide + spawnedCount);
                    int remainingPulseBudget = maxSpawnCount - spawnedCount;
                    if (remainingCapacity <= 0 || remainingPulseBudget <= 0)
                        break;

                    _materializedBattleResultEntriesByEntryId.TryGetValue(entryState.EntryId ?? string.Empty, out MaterializedBattleResultEntryRuntimeState runtimeState);
                    int alreadySpawnedCount = Math.Max(0, runtimeState?.MaterializedSpawnCount ?? 0);
                    int remainingEntryCapacity = Math.Min(availableCount, GetMaterializedAgentsPerEntryCap(side, entryStates.Count)) - alreadySpawnedCount;
                    if (remainingEntryCapacity <= 0)
                        continue;

                    int extraCount = Math.Min(remainingEntryCapacity, Math.Min(remainingCapacity, remainingPulseBudget));
                    if (extraCount <= 0)
                        continue;

                    spawnedCount += SpawnMaterializedAgentsForEntry(
                        mission,
                        team,
                        side,
                        troop,
                        entryState,
                        extraCount,
                        currentSpawnedForSide + spawnedCount,
                        source + " character=" + resolvedTroopSource + " pass=fill");
                }

                return spawnedCount;
            }

            IReadOnlyList<string> troopIds = GetGenericMaterializedTroopFallbackIds(side);
            sideCap = GetMaterializedArmyAgentsPerSideCap(side, troopIds.Count);
            foreach (string troopId in troopIds)
            {
                BasicCharacterObject troop = ResolveAllowedCharacter(troopId);
                if (troop == null)
                    continue;

                int remainingCapacity = sideCap - (currentSpawnedForSide + spawnedCount);
                int remainingPulseBudget = maxSpawnCount - spawnedCount;
                if (remainingCapacity <= 0 || remainingPulseBudget <= 0)
                    break;

                int spawnCount = Math.Min(
                    GetFallbackMaterializedAgentsPerTroopCap(side, troopIds.Count),
                    Math.Min(remainingCapacity, remainingPulseBudget));
                spawnedCount += SpawnMaterializedAgentsForEntry(
                    mission,
                    team,
                    side,
                    troop,
                    null,
                    spawnCount,
                    currentSpawnedForSide + spawnedCount,
                    source);
            }

            return spawnedCount;
        }

        private static bool IsSyntheticAllCampaignTroopsRuntime()
        {
            BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
            return string.Equals(snapshot?.BattleId, SyntheticAllCampaignTroopsBattleId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSyntheticLiveHeroesRuntime()
        {
            BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
            return string.Equals(snapshot?.BattleId, SyntheticLiveHeroesBattleId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSyntheticRuntime()
        {
            return IsSyntheticAllCampaignTroopsRuntime() || IsSyntheticLiveHeroesRuntime();
        }

        private static int GetMaterializedArmyAgentsPerSideCap(BattleSideEnum side, int requestedEntryCount)
        {
            if (side == BattleSideEnum.None)
                return MaxMaterializedArmyAgentsPerSide;

            if (IsSyntheticRuntime())
                return Math.Max(MaxMaterializedArmyAgentsPerSide, Math.Max(0, requestedEntryCount));

            int totalBattleBudget = GetConfiguredBattleSizeBudget();
            int attackerAvailable = GetBattleSideAvailableCount(BattleSideEnum.Attacker);
            int defenderAvailable = GetBattleSideAvailableCount(BattleSideEnum.Defender);
            int totalAvailable = attackerAvailable + defenderAvailable;
            int sideAvailable = side == BattleSideEnum.Attacker ? attackerAvailable : defenderAvailable;

            if (totalBattleBudget <= 0 || totalAvailable <= 0 || sideAvailable <= 0)
                return MaxMaterializedArmyAgentsPerSide;

            int sideCap = (int)Math.Round((double)totalBattleBudget * sideAvailable / totalAvailable, MidpointRounding.AwayFromZero);
            sideCap = Math.Max(1, sideCap);
            sideCap = Math.Min(sideAvailable, sideCap);
            return sideCap;
        }

        private static int GetMaterializedAgentsPerEntryCap(BattleSideEnum side, int requestedEntryCount)
        {
            if (IsSyntheticRuntime())
                return 1;

            if (requestedEntryCount <= 0)
                return MaxMaterializedAgentsPerEntry;

            int sideCap = GetMaterializedArmyAgentsPerSideCap(side, requestedEntryCount);
            if (sideCap <= 0)
                return MaxMaterializedAgentsPerEntry;

            return Math.Max(1, (int)Math.Ceiling((double)sideCap / requestedEntryCount));
        }

        private static int GetFallbackMaterializedAgentsPerTroopCap(BattleSideEnum side, int requestedTroopCount)
        {
            if (IsSyntheticRuntime())
                return 1;

            if (requestedTroopCount <= 0)
                return FallbackMaterializedAgentsPerTroop;

            int sideCap = GetMaterializedArmyAgentsPerSideCap(side, requestedTroopCount);
            if (sideCap <= 0)
                return FallbackMaterializedAgentsPerTroop;

            return Math.Max(1, (int)Math.Ceiling((double)sideCap / requestedTroopCount));
        }

        private static int GetConfiguredBattleSizeBudget()
        {
            BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
            int snapshotBudget = runtimeState?.BattleSizeBudget ?? BattleSnapshotRuntimeState.GetCurrent()?.BattleSizeBudget ?? 0;
            if (snapshotBudget > 0)
                return snapshotBudget;

            return MaxMaterializedArmyAgentsPerSide * 2;
        }

        private static int GetBattleSideAvailableCount(BattleSideEnum side)
        {
            if (side == BattleSideEnum.None)
                return 0;

            IReadOnlyList<RosterEntryState> entryStates = GetAllowedControlEntryStatesSnapshot(side);
            if (entryStates != null && entryStates.Count > 0)
            {
                int entryTotal = entryStates.Sum(entryState => Math.Max(0, (entryState?.Count ?? 0) - (entryState?.WoundedCount ?? 0)));
                if (entryTotal > 0)
                    return entryTotal;
            }

            BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
            if (runtimeState?.Sides != null)
            {
                BattleSideState sideState = runtimeState.Sides.FirstOrDefault(candidate =>
                    ResolveBattleSideFromState(candidate) == side);
                int fallbackTotal = Math.Max(0, sideState?.TotalManCount ?? 0);
                if (fallbackTotal > 0)
                    return fallbackTotal;
            }

            return 0;
        }

        private static bool TryRepairMaterializedDeploymentPlan(
            Mission mission,
            Team team,
            BattleSideEnum side,
            string source)
        {
            if (mission == null || team == null || side == BattleSideEnum.None)
                return false;

            BattleMapContractDiagnostics.LogDeploymentPlanContract(
                mission,
                team,
                focusFormation: null,
                isReinforcement: false,
                source: (source ?? "unknown") + " pre-repair");

            if (!mission.GetDeploymentPlan<DefaultMissionDeploymentPlan>(out DefaultMissionDeploymentPlan deploymentPlan) || deploymentPlan == null)
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: materialized deployment plan repair skipped because DefaultMissionDeploymentPlan is unavailable. " +
                    "TeamSide=" + side +
                    " TeamIndex=" + team.TeamIndex +
                    " Source=" + (source ?? "unknown"));
                return false;
            }

            if (!EnsureMaterializedDeploymentPlanHasTeamPlan(mission, deploymentPlan, team, side, source))
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: materialized deployment plan repair skipped because no team plan is registered for the current mission team. " +
                    "TeamSide=" + side +
                    " TeamIndex=" + team.TeamIndex +
                    " Source=" + (source ?? "unknown"));
                return false;
            }

            Dictionary<FormationClass, (int Foot, int Mounted)> formationCounts =
                BuildMaterializedDeploymentCountsForSide(side, out int plannedAgents, out bool hasMountedUnits);
            if (formationCounts.Count == 0 || plannedAgents <= 0)
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: materialized deployment plan repair skipped because no planned troop counts were resolved. " +
                    "TeamSide=" + side +
                    " TeamIndex=" + team.TeamIndex +
                    " Source=" + (source ?? "unknown"));
                return false;
            }

            try
            {
                if (deploymentPlan.IsPlanMade(team))
                {
                    deploymentPlan.ClearAddedTroops(team);
                    deploymentPlan.ClearDeploymentPlan(team);
                }

                if (deploymentPlan.IsReinforcementPlanMade(team))
                {
                    deploymentPlan.ClearAddedTroops(team, isReinforcement: true);
                    deploymentPlan.ClearReinforcementPlan(team);
                }

                deploymentPlan.SetSpawnWithHorses(team, hasMountedUnits);
                foreach (KeyValuePair<FormationClass, (int Foot, int Mounted)> formationCount in formationCounts.OrderBy(pair => (int)pair.Key))
                {
                    if (formationCount.Value.Foot <= 0 && formationCount.Value.Mounted <= 0)
                        continue;

                    deploymentPlan.AddTroops(team, formationCount.Key, formationCount.Value.Foot, formationCount.Value.Mounted);
                    deploymentPlan.AddTroops(team, formationCount.Key, formationCount.Value.Foot, formationCount.Value.Mounted, isReinforcement: true);
                }

                deploymentPlan.MakeDeploymentPlan(team);
                deploymentPlan.MakeReinforcementDeploymentPlan(team);

                bool hasSceneMapPatch = false;
                try
                {
                    hasSceneMapPatch = mission.HasSceneMapPatch();
                }
                catch
                {
                }

                bool initialPlanMade = deploymentPlan.IsPlanMade(team);
                bool reinforcementPlanMade = deploymentPlan.IsReinforcementPlanMade(team);
                BattleMapContractDiagnostics.LogDeploymentPlanContract(
                    mission,
                    team,
                    focusFormation: null,
                    isReinforcement: false,
                    source: (source ?? "unknown") + " post-repair");
                ModLogger.Info(
                    "CoopMissionSpawnLogic: repaired materialized deployment plan. " +
                    "TeamSide=" + side +
                    " TeamIndex=" + team.TeamIndex +
                    " PlannedAgents=" + plannedAgents +
                    " HasMountedUnits=" + hasMountedUnits +
                    " HasSceneMapPatch=" + hasSceneMapPatch +
                    " InitialPlanMade=" + initialPlanMade +
                    " ReinforcementPlanMade=" + reinforcementPlanMade +
                    " FormationCounts=" + BuildMaterializedDeploymentCountSummary(formationCounts) +
                    " Source=" + (source ?? "unknown"));
                return initialPlanMade;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: materialized deployment plan repair failed. " +
                    "TeamSide=" + side +
                    " TeamIndex=" + team.TeamIndex +
                    " Source=" + (source ?? "unknown") +
                    " Message=" + ex.Message);
                return false;
            }
        }

        private static readonly FieldInfo DefaultMissionDeploymentPlanTeamDeploymentPlansField =
            typeof(DefaultMissionDeploymentPlan).GetField("_teamDeploymentPlans", BindingFlags.Instance | BindingFlags.NonPublic);

        private static bool EnsureMaterializedDeploymentPlanHasTeamPlan(
            Mission mission,
            DefaultMissionDeploymentPlan deploymentPlan,
            Team team,
            BattleSideEnum side,
            string source)
        {
            if (mission == null || deploymentPlan == null || team == null)
                return false;

            bool hasTeamPlan = TryHasMaterializedDeploymentTeamPlan(deploymentPlan, team, out int registeredPlanCountBefore);
            if (hasTeamPlan)
                return true;

            int missionTeamCount = 0;
            try
            {
                missionTeamCount = mission.Teams?.Count ?? 0;
            }
            catch
            {
            }

            ModLogger.Info(
                "CoopMissionSpawnLogic: refreshing deployment plan team snapshot for materialized spawn repair. " +
                "TeamSide=" + side +
                " TeamIndex=" + team.TeamIndex +
                " MissionTeamCount=" + missionTeamCount +
                " RegisteredPlanCountBefore=" + registeredPlanCountBefore +
                " Source=" + (source ?? "unknown"));

            try
            {
                deploymentPlan.Initialize();
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: deployment plan team snapshot refresh failed. " +
                    "TeamSide=" + side +
                    " TeamIndex=" + team.TeamIndex +
                    " Source=" + (source ?? "unknown") +
                    " Message=" + ex.Message);
                return false;
            }

            bool readyAfterRefresh = TryHasMaterializedDeploymentTeamPlan(deploymentPlan, team, out int registeredPlanCountAfter);
            ModLogger.Info(
                "CoopMissionSpawnLogic: deployment plan team snapshot refresh completed. " +
                "TeamSide=" + side +
                " TeamIndex=" + team.TeamIndex +
                " RegisteredPlanCountAfter=" + registeredPlanCountAfter +
                " Ready=" + readyAfterRefresh +
                " Source=" + (source ?? "unknown"));
            return readyAfterRefresh;
        }

        private static bool TryHasMaterializedDeploymentTeamPlan(
            DefaultMissionDeploymentPlan deploymentPlan,
            Team team,
            out int registeredPlanCount)
        {
            registeredPlanCount = 0;
            if (deploymentPlan == null || team == null || DefaultMissionDeploymentPlanTeamDeploymentPlansField == null)
                return false;

            if (!(DefaultMissionDeploymentPlanTeamDeploymentPlansField.GetValue(deploymentPlan) is IEnumerable entries))
                return false;

            foreach (object entry in entries)
            {
                if (entry == null)
                    continue;

                registeredPlanCount++;
                FieldInfo item1Field = entry.GetType().GetField("Item1", BindingFlags.Instance | BindingFlags.Public);
                if (item1Field == null)
                    continue;

                if (ReferenceEquals(item1Field.GetValue(entry), team))
                    return true;
            }

            return false;
        }

        private static Dictionary<FormationClass, (int Foot, int Mounted)> BuildMaterializedDeploymentCountsForSide(
            BattleSideEnum side,
            out int plannedAgents,
            out bool hasMountedUnits)
        {
            var formationCounts = new Dictionary<FormationClass, (int Foot, int Mounted)>();
            plannedAgents = 0;
            hasMountedUnits = false;

            IReadOnlyList<RosterEntryState> entryStates = GetAutomatedMaterializableEntryStatesSnapshot(side);
            int requestedEntryCount = entryStates?.Count ?? 0;
            int sideCap = GetMaterializedArmyAgentsPerSideCap(side, requestedEntryCount);
            if (entryStates != null && entryStates.Count > 0)
            {
                var materializableEntries = new List<(RosterEntryState EntryState, BasicCharacterObject Troop, int AvailableCount)>();
                foreach (RosterEntryState entryState in entryStates)
                {
                    string spawnTemplateId = ResolveEntrySpawnTemplateId(entryState);
                    if (entryState == null || string.IsNullOrWhiteSpace(spawnTemplateId))
                        continue;

                    int availableCount = Math.Max(0, entryState.Count - entryState.WoundedCount);
                    if (availableCount <= 0)
                        continue;

                    BasicCharacterObject troop = ResolveMaterializedArmyCharacter(entryState, spawnTemplateId, out _);
                    if (troop == null)
                        continue;

                    materializableEntries.Add((entryState, troop, availableCount));
                }

                foreach ((RosterEntryState entryState, BasicCharacterObject troop, int availableCount) in materializableEntries)
                {
                    int remainingCapacity = sideCap - plannedAgents;
                    if (remainingCapacity <= 0)
                        break;

                    int seedCount = Math.Min(1, Math.Min(availableCount, remainingCapacity));
                    if (seedCount <= 0)
                        continue;

                    AccumulateMaterializedDeploymentCount(
                        formationCounts,
                        troop,
                        entryState,
                        seedCount,
                        ref plannedAgents,
                        ref hasMountedUnits);
                }

                foreach ((RosterEntryState entryState, BasicCharacterObject troop, int availableCount) in materializableEntries)
                {
                    int remainingCapacity = sideCap - plannedAgents;
                    if (remainingCapacity <= 0)
                        break;

                    int remainingEntryCapacity = Math.Min(availableCount, GetMaterializedAgentsPerEntryCap(side, entryStates.Count)) - 1;
                    if (remainingEntryCapacity <= 0)
                        continue;

                    int extraCount = Math.Min(remainingEntryCapacity, remainingCapacity);
                    if (extraCount <= 0)
                        continue;

                    AccumulateMaterializedDeploymentCount(
                        formationCounts,
                        troop,
                        entryState,
                        extraCount,
                        ref plannedAgents,
                        ref hasMountedUnits);
                }

                return formationCounts;
            }

            IReadOnlyList<string> troopIds = GetGenericMaterializedTroopFallbackIds(side);
            sideCap = GetMaterializedArmyAgentsPerSideCap(side, troopIds.Count);
            foreach (string troopId in troopIds)
            {
                BasicCharacterObject troop = ResolveAllowedCharacter(troopId);
                if (troop == null)
                    continue;

                int remainingCapacity = sideCap - plannedAgents;
                if (remainingCapacity <= 0)
                    break;

                int spawnCount = Math.Min(GetFallbackMaterializedAgentsPerTroopCap(side, troopIds.Count), remainingCapacity);
                if (spawnCount <= 0)
                    continue;

                AccumulateMaterializedDeploymentCount(
                    formationCounts,
                    troop,
                    entryState: null,
                    spawnCount,
                    ref plannedAgents,
                    ref hasMountedUnits);
            }

            return formationCounts;
        }

        private static void AccumulateMaterializedDeploymentCount(
            Dictionary<FormationClass, (int Foot, int Mounted)> formationCounts,
            BasicCharacterObject troop,
            RosterEntryState entryState,
            int count,
            ref int plannedAgents,
            ref bool hasMountedUnits)
        {
            if (formationCounts == null || troop == null || count <= 0)
                return;

            FormationClass formationClass = troop.DefaultFormationClass;
            if ((int)formationClass < 0 || (int)formationClass >= (int)FormationClass.NumberOfRegularFormations)
                formationClass = FormationClass.Infantry;

            bool isMounted = entryState?.IsMounted ?? (formationClass == FormationClass.Cavalry || formationClass == FormationClass.HorseArcher);
            if (!formationCounts.TryGetValue(formationClass, out (int Foot, int Mounted) existingCounts))
                existingCounts = (0, 0);

            existingCounts = isMounted
                ? (existingCounts.Foot, existingCounts.Mounted + count)
                : (existingCounts.Foot + count, existingCounts.Mounted);
            formationCounts[formationClass] = existingCounts;

            plannedAgents += count;
            if (isMounted)
                hasMountedUnits = true;
        }

        private static string BuildMaterializedDeploymentCountSummary(
            Dictionary<FormationClass, (int Foot, int Mounted)> formationCounts)
        {
            if (formationCounts == null || formationCounts.Count == 0)
                return "(none)";

            return string.Join(
                ",",
                formationCounts
                    .OrderBy(pair => (int)pair.Key)
                    .Select(pair => pair.Key + "(F=" + pair.Value.Foot + ",M=" + pair.Value.Mounted + ")"));
        }

        private static BasicCharacterObject ResolveMaterializedArmyCharacter(
            RosterEntryState entryState,
            string spawnTemplateId,
            out string resolvedTroopSource)
        {
            resolvedTroopSource = "spawn-template";
            if (entryState == null)
                return null;

            if (!string.IsNullOrWhiteSpace(entryState.OriginalCharacterId))
            {
                try
                {
                    BasicCharacterObject originalCharacter = MBObjectManager.Instance?.GetObject<BasicCharacterObject>(entryState.OriginalCharacterId);
                    if (originalCharacter != null)
                    {
                        resolvedTroopSource = "original-character";
                        return originalCharacter;
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Info(
                        "CoopMissionSpawnLogic: original character lookup failed for materialized entry. " +
                        "EntryId=" + (entryState.EntryId ?? "null") +
                        " OriginalCharacterId=" + entryState.OriginalCharacterId +
                        " Message=" + ex.Message);
                }
            }

            BasicCharacterObject templateCharacter = BattleSnapshotRuntimeState.TryResolveCharacterObject(entryState.EntryId) ?? ResolveAllowedCharacter(spawnTemplateId);
            if (templateCharacter == null)
                return null;

            resolvedTroopSource = "spawn-template";
            return templateCharacter;
        }

        private static int SpawnMaterializedAgentsForEntry(
            Mission mission,
            Team team,
            BattleSideEnum side,
            BasicCharacterObject troop,
            RosterEntryState entryState,
            int spawnCount,
            int sideOffset,
            string source,
            bool isReinforcement = false)
        {
            if (mission == null || team == null || troop == null || spawnCount <= 0)
                return 0;

            string entryId = entryState?.EntryId;

            FormationClass formationClass = troop.DefaultFormationClass;
            ResolveMaterializedArmySpawnFrame(
                mission,
                team,
                side,
                troop,
                formationClass,
                isReinforcement,
                out WorldPosition formationSpawnPosition,
                out Vec2 formationDirection,
                out string spawnFrameSource);

            if (formationDirection.LengthSquared < 0.001f)
                formationDirection = side == BattleSideEnum.Attacker ? new Vec2(1f, 0f) : new Vec2(-1f, 0f);

            formationDirection.Normalize();
            Vec3 forward = new Vec3(formationDirection.x, formationDirection.y, 0f);
            Vec3 right = new Vec3(-formationDirection.y, formationDirection.x, 0f);
            Vec3 basePosition = formationSpawnPosition.IsValid ? formationSpawnPosition.GetGroundVec3() : new Vec3(0f, 0f, 0f);

            string equipmentOverrideDiagnostics = BuildMaterializedEquipmentOverrideDiagnostics(entryState, troop);
            string combatProfileDiagnostics = "AppliedCombatProfile=(none)";
            string identityDiagnostics = "AppliedIdentity=(none)";
            int spawnedCount = 0;
            ModLogger.Info(
                "CoopMissionSpawnLogic: materialized battlefield entry spawn begin. " +
                "Side=" + side +
                " TroopId=" + troop.StringId +
                " EntryId=" + (entryId ?? "null") +
                " RequestedCount=" + spawnCount +
                " Reinforcement=" + isReinforcement +
                " SpawnFrameSource=" + spawnFrameSource +
                " Source=" + (source ?? "unknown"));
            for (int i = 0; i < spawnCount; i++)
            {
                int absoluteIndex = sideOffset + spawnedCount;
                int column = absoluteIndex % 4;
                int row = absoluteIndex / 4;
                float lateralOffset = (column - 1.5f) * 1.8f;
                float depthOffset = row * 2.2f * (side == BattleSideEnum.Attacker ? -1f : 1f);
                Vec3 spawnPosition = basePosition + right * lateralOffset + forward * depthOffset;

                string appliedArmorOverrides;
                string appliedCombatProfile;
                string appliedIdentity;
                Agent agent = SpawnBattlefieldArmyAgent(
                    mission,
                    team,
                    troop,
                    entryState,
                    formationClass,
                    spawnPosition,
                    formationDirection,
                    out appliedArmorOverrides,
                    out appliedCombatProfile,
                    out appliedIdentity);
                if (agent == null)
                    continue;

                _materializedArmyEntryIdByAgentIndex[agent.Index] = entryId ?? string.Empty;
                _materializedArmySideByAgentIndex[agent.Index] = side;
                RegisterMaterializedBattleResultEntry(agent, entryState, side);
                combatProfileDiagnostics = appliedCombatProfile;
                identityDiagnostics = appliedIdentity;
                spawnedCount++;
            }

            if (spawnedCount > 0)
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: materialized battlefield entry. " +
                    "Side=" + side +
                    " TroopId=" + troop.StringId +
                    " EntryId=" + (entryId ?? "null") +
                    " Spawned=" + spawnedCount +
                    " Reinforcement=" + isReinforcement +
                    " SpawnFrameSource=" + spawnFrameSource +
                    " " + equipmentOverrideDiagnostics +
                    " " + identityDiagnostics +
                    " " + combatProfileDiagnostics +
                    " Source=" + (source ?? "unknown"));
            }

            return spawnedCount;
        }

        private static void ResolveMaterializedArmySpawnFrame(
            Mission mission,
            Team team,
            BattleSideEnum side,
            BasicCharacterObject troop,
            FormationClass formationClass,
            bool isReinforcement,
            out WorldPosition spawnPosition,
            out Vec2 spawnDirection,
            out string spawnFrameSource)
        {
            spawnPosition = default;
            spawnDirection = side == BattleSideEnum.Attacker ? new Vec2(1f, 0f) : new Vec2(-1f, 0f);
            spawnFrameSource = "direct-fallback";

            try
            {
                mission.GetFormationSpawnFrame(team, formationClass, isReinforcement, out spawnPosition, out spawnDirection);
                spawnFrameSource = isReinforcement ? "formation-reinforcement" : "formation";
                return;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: failed to resolve formation spawn frame for " + troop.StringId + ": " + ex.Message);

                string repairSource = (isReinforcement ? "reinforcement" : "initial") + "-formation-frame-repair:" + troop.StringId;
                if (TryRepairMaterializedDeploymentPlan(mission, team, side, repairSource))
                {
                    if (TryResolveRepairedMaterializedSpawnFrame(
                        mission,
                        team,
                        formationClass,
                        isReinforcement,
                        out spawnPosition,
                        out spawnDirection,
                        out string repairedSpawnDiagnostics))
                    {
                        spawnFrameSource = isReinforcement ? "formation-reinforcement-repaired" : "formation-repaired";
                        return;
                    }

                    ModLogger.Info(
                        "CoopMissionSpawnLogic: repaired deployment plan did not yield a safe formation frame for " +
                        troop.StringId +
                        ": " + (repairedSpawnDiagnostics ?? "unknown"));
                }
            }

            try
            {
                var spawnFrameBehavior = new FFASpawnFrameBehavior();
                spawnFrameBehavior.Initialize();
                bool hasMount = formationClass == FormationClass.Cavalry || formationClass == FormationClass.HorseArcher;
                MatrixFrame spawnFrame = spawnFrameBehavior.GetSpawnFrame(team, hasMount: hasMount, isInitialSpawn: !isReinforcement);
                if (spawnFrame.origin != Vec3.Zero)
                {
                    spawnPosition = new WorldPosition(mission.Scene, spawnFrame.origin);
                    Vec2 frameDirection = spawnFrame.rotation.f.AsVec2;
                    if (frameDirection.LengthSquared > 0.001f)
                    {
                        spawnDirection = frameDirection.Normalized();
                    }

                    spawnFrameSource = isReinforcement ? "ffa-scene-reinforcement" : "ffa-scene";
                    return;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: FFASpawnFrameBehavior materialized spawn fallback failed for " + troop.StringId + ": " + ex.Message);
            }

            GetDirectSpawnFrame(mission, team, out Vec3 fallbackSpawnPosition, out Vec2 fallbackSpawnDirection);
            spawnPosition = new WorldPosition(mission.Scene, fallbackSpawnPosition);
            if (fallbackSpawnDirection.LengthSquared > 0.001f)
                spawnDirection = fallbackSpawnDirection;
        }

        private static bool TryResolveRepairedMaterializedSpawnFrame(
            Mission mission,
            Team team,
            FormationClass formationClass,
            bool isReinforcement,
            out WorldPosition spawnPosition,
            out Vec2 spawnDirection,
            out string diagnostics)
        {
            spawnPosition = default;
            spawnDirection = Vec2.Invalid;
            diagnostics = null;

            if (mission == null || team == null)
            {
                diagnostics = "mission-or-team-missing";
                return false;
            }

            if (!mission.GetDeploymentPlan<DefaultMissionDeploymentPlan>(out DefaultMissionDeploymentPlan deploymentPlan) || deploymentPlan == null)
            {
                diagnostics = "deployment-plan-missing";
                return false;
            }

            IFormationDeploymentPlan formationPlan;
            try
            {
                formationPlan = deploymentPlan.GetFormationPlan(team, formationClass, isReinforcement);
            }
            catch (Exception ex)
            {
                diagnostics = "get-formation-plan-failed:" + ex.Message;
                return false;
            }

            if (formationPlan == null)
            {
                diagnostics = "formation-plan-null";
                return false;
            }

            bool hasFrame;
            try
            {
                hasFrame = formationPlan.HasFrame();
            }
            catch (Exception ex)
            {
                diagnostics = "has-frame-failed:" + ex.Message;
                return false;
            }

            if (!hasFrame)
            {
                BattleMapContractDiagnostics.LogDeploymentPlanContract(
                    mission,
                    team,
                    formationClass,
                    isReinforcement,
                    "repaired-frame-missing");
                diagnostics =
                    "formation-plan-has-no-frame" +
                    " PlannedTroopCount=" + formationPlan.PlannedTroopCount +
                    " HasDimensions=" + formationPlan.HasDimensions;
                return false;
            }

            try
            {
                MatrixFrame frame = formationPlan.GetFrame();
                BattleMapContractDiagnostics.LogDeploymentPlanContract(
                    mission,
                    team,
                    formationClass,
                    isReinforcement,
                    "repaired-frame-success");
                spawnPosition = new WorldPosition(mission.Scene, UIntPtr.Zero, frame.origin, hasValidZ: false);

                Vec2 direction = formationPlan.GetDirection();
                if (direction.LengthSquared <= 0.001f)
                    direction = frame.rotation.f.AsVec2;

                spawnDirection = direction.LengthSquared > 0.001f
                    ? direction.Normalized()
                    : (team.Side == BattleSideEnum.Attacker ? new Vec2(1f, 0f) : new Vec2(-1f, 0f));

                diagnostics =
                    "frame-ok" +
                    " PlannedTroopCount=" + formationPlan.PlannedTroopCount +
                    " HasDimensions=" + formationPlan.HasDimensions;
                return true;
            }
            catch (Exception ex)
            {
                diagnostics = "extract-frame-failed:" + ex.Message;
                return false;
            }
        }

        private static Agent SpawnBattlefieldArmyAgent(
            Mission mission,
            Team team,
            BasicCharacterObject troop,
            RosterEntryState entryState,
            FormationClass formationClass,
            Vec3 spawnPosition,
            Vec2 direction,
            out string appliedArmorOverrides,
            out string appliedCombatProfile,
            out string appliedIdentity)
        {
            appliedArmorOverrides = "(none)";
            appliedCombatProfile = "AppliedCombatProfile=(none)";
            appliedIdentity = "AppliedIdentity=(none)";
            if (mission == null || team == null || troop == null)
                return null;

            try
            {
                var origin = new BasicBattleAgentOrigin(troop);
                AgentBuildData buildData = new AgentBuildData(troop);
                Equipment snapshotEquipment = BuildSnapshotEquipmentForExactRuntime(entryState);
                Equipment spawnEquipment = snapshotEquipment ?? troop.Equipment?.Clone(false);
                appliedArmorOverrides = TryApplyMaterializedEquipmentOverrides(spawnEquipment, entryState, null, trackCoverage: true);
                TextObject originalTroopName = null;
                bool hasTemporaryNameOverride =
                    TryApplyEntryNameToSpawnCharacter(troop, entryState, out originalTroopName);
                try
                {
                    buildData.Team(team);
                    buildData.Controller(AgentControllerType.AI);
                    buildData.TroopOrigin(origin);
                    buildData.InitialPosition(in spawnPosition);
                    buildData.InitialDirection(in direction);
                    buildData.SpawnsIntoOwnFormation(true);
                    buildData.SpawnsUsingOwnTroopClass(true);
                    if (spawnEquipment != null)
                        buildData.Equipment(spawnEquipment);

                    appliedIdentity = TryApplyEntryIdentityToBuildData(buildData, entryState);
                    Agent agent = mission.SpawnAgent(buildData, spawnFromAgentVisuals: false);
                    if (agent != null)
                    {
                        TryApplyEntryIdentityToAgent(agent, entryState);

                        FormationClass resolvedFormationClass = formationClass;
                        if ((int)resolvedFormationClass < 0 || (int)resolvedFormationClass >= team.FormationsIncludingSpecialAndEmpty.Count())
                            resolvedFormationClass = FormationClass.Infantry;

                        Formation targetFormation = team.GetFormation(resolvedFormationClass);
                        if (targetFormation != null && !ReferenceEquals(agent.Formation, targetFormation))
                        {
                            agent.Formation = targetFormation;
                            agent.ForceUpdateCachedAndFormationValues(updateOnlyMovement: false, arrangementChangeAllowed: false);
                        }

                        agent.SetAutomaticTargetSelection(true);
                        agent.SetFiringOrder(FiringOrder.RangedWeaponUsageOrderEnum.FireAtWill);
                        appliedCombatProfile = TryApplyMaterializedCombatProfile(agent, entryState);
                    }
                    else
                    {
                        ModLogger.Info(
                            "CoopMissionSpawnLogic: SpawnBattlefieldArmyAgent returned null. " +
                            "TroopId=" + troop.StringId +
                            " TeamSide=" + team.Side +
                            " TeamIndex=" + team.TeamIndex);
                    }

                    return agent;
                }
                finally
                {
                    RestoreSpawnCharacterName(troop, hasTemporaryNameOverride, originalTroopName);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: SpawnBattlefieldArmyAgent failed. " +
                    "TroopId=" + (troop?.StringId ?? "null") +
                    " EntryId=" + (entryState?.EntryId ?? "null") +
                    " TeamSide=" + team?.Side +
                    " TeamIndex=" + (team != null ? team.TeamIndex.ToString() : "null") +
                    " FormationClass=" + formationClass +
                    " SpawnPosition=" + spawnPosition +
                    " Direction=" + direction +
                    " Message=" + ex.Message);
                return null;
            }
        }

        private static string TryApplyEntryIdentityToBuildData(AgentBuildData buildData, RosterEntryState entryState)
        {
            if (buildData == null || !HasEntryHeroIdentity(entryState))
                return "AppliedIdentity=(none)";

            var parts = new List<string>();
            try
            {
                buildData.IsFemale(entryState.HeroIsFemale);
                parts.Add("Female=" + entryState.HeroIsFemale);
            }
            catch (Exception ex)
            {
                parts.Add("FemaleApplyFailed=" + ex.GetType().Name);
            }

            if (entryState.HeroAge > 0.01f)
            {
                try
                {
                    int roundedAge = Math.Max(1, Math.Min(120, (int)Math.Round(entryState.HeroAge)));
                    buildData.Age(roundedAge);
                    parts.Add("Age=" + entryState.HeroAge.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                }
                catch (Exception ex)
                {
                    parts.Add("AgeApplyFailed=" + ex.GetType().Name);
                }
            }

            if (TryResolveEntryBodyProperties(entryState, out BodyProperties bodyProperties))
            {
                try
                {
                    buildData.BodyProperties(bodyProperties);
                    parts.Add("Body=applied");
                }
                catch (Exception ex)
                {
                    parts.Add("BodyApplyFailed=" + ex.GetType().Name);
                }
            }

            if (!string.IsNullOrWhiteSpace(entryState.TroopName))
                parts.Add("Name=" + entryState.TroopName.Replace('\r', ' ').Replace('\n', ' '));

            return parts.Count == 0
                ? "AppliedIdentity=(none)"
                : "AppliedIdentity=" + string.Join(", ", parts);
        }

        private static void TryApplyEntryIdentityToAgent(Agent agent, RosterEntryState entryState)
        {
            if (agent == null || entryState == null)
                return;

            if (HasEntryHeroIdentity(entryState))
            {
                try
                {
                    agent.IsFemale = entryState.HeroIsFemale;
                }
                catch
                {
                }

                if (entryState.HeroAge > 0.01f)
                {
                    try
                    {
                        agent.Age = entryState.HeroAge;
                    }
                    catch
                    {
                    }
                }

                if (TryResolveEntryBodyProperties(entryState, out BodyProperties bodyProperties))
                {
                    try
                    {
                        agent.UpdateBodyProperties(bodyProperties);
                    }
                    catch
                    {
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(entryState.TroopName))
                return;

            try
            {
                FieldInfo agentNameField = typeof(Agent).GetField("_name", BindingFlags.Instance | BindingFlags.NonPublic);
                if (agentNameField != null)
                    agentNameField.SetValue(agent, new TextObject(entryState.TroopName));
            }
            catch
            {
            }
        }

        private static bool TryApplyEntryNameToSpawnCharacter(BasicCharacterObject troop, RosterEntryState entryState, out TextObject originalName)
        {
            originalName = null;
            if (troop == null || !HasEntryHeroIdentity(entryState) || string.IsNullOrWhiteSpace(entryState.TroopName))
                return false;

            string currentName = troop.Name?.ToString() ?? string.Empty;
            if (string.Equals(currentName, entryState.TroopName, StringComparison.Ordinal))
                return false;

            try
            {
                originalName = new TextObject(string.IsNullOrWhiteSpace(currentName) ? troop.StringId ?? string.Empty : currentName);
                if (!TrySetCharacterName(troop, new TextObject(entryState.TroopName)))
                    return false;
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: failed to apply temporary spawn-character name override. " +
                    "TroopId=" + troop.StringId +
                    " EntryId=" + (entryState?.EntryId ?? "null") +
                    " Name=" + (entryState?.TroopName ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ') +
                    " Message=" + ex.Message);
                return false;
            }
        }

        private static void RestoreSpawnCharacterName(BasicCharacterObject troop, bool restore, TextObject originalName)
        {
            if (!restore || troop == null || originalName == null)
                return;

            try
            {
                TrySetCharacterName(troop, originalName);
            }
            catch
            {
            }
        }

        private static bool TrySetCharacterName(BasicCharacterObject troop, TextObject name)
        {
            if (troop == null || name == null)
                return false;

            try
            {
                MethodInfo setNameMethod = troop.GetType().GetMethod(
                    "SetName",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(TextObject) },
                    null);
                if (setNameMethod == null)
                    return false;

                setNameMethod.Invoke(troop, new object[] { name });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasEntryHeroIdentity(RosterEntryState entryState)
        {
            return entryState != null &&
                   (entryState.IsHero ||
                    !string.IsNullOrWhiteSpace(entryState.HeroId) ||
                    !string.IsNullOrWhiteSpace(entryState.HeroRole) ||
                    !string.IsNullOrWhiteSpace(entryState.HeroTemplateId) ||
                    !string.IsNullOrWhiteSpace(entryState.HeroBodyProperties));
        }

        private static bool TryResolveEntryBodyProperties(RosterEntryState entryState, out BodyProperties bodyProperties)
        {
            bodyProperties = default;
            if (string.IsNullOrWhiteSpace(entryState?.HeroBodyProperties))
                return false;

            try
            {
                return BodyProperties.FromString(entryState.HeroBodyProperties, out bodyProperties);
            }
            catch
            {
                return false;
            }
        }

        private static string TryApplyMaterializedCombatProfile(Agent agent, RosterEntryState entryState)
        {
            if (agent == null || entryState == null)
                return "AppliedCombatProfile=(none)";

            RegisterMaterializedCombatProfile(agent, entryState);
            BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
            BattlePartyState partyState = null;
            if (runtimeState?.PartiesById != null && !string.IsNullOrWhiteSpace(entryState.PartyId))
                runtimeState.PartiesById.TryGetValue(entryState.PartyId, out partyState);

            var parts = new List<string>();
            if (entryState.BaseHitPoints > 0)
            {
                float targetHitPoints = Math.Max(1f, entryState.BaseHitPoints);
                try
                {
                    agent.HealthLimit = targetHitPoints;
                    agent.Health = targetHitPoints;
                    parts.Add("Hp=" + targetHitPoints.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                }
                catch (Exception ex)
                {
                    parts.Add("HpApplyFailed=" + ex.GetType().Name);
                }
            }

            Agent mountAgent = agent.MountAgent;
            if (mountAgent != null && mountAgent.IsActive())
            {
                try
                {
                    float targetMountHitPoints = Math.Max(1f, mountAgent.HealthLimit);
                    mountAgent.Health = targetMountHitPoints;
                    parts.Add("MountHp=" + targetMountHitPoints.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                }
                catch (Exception ex)
                {
                    parts.Add("MountHpApplyFailed=" + ex.GetType().Name);
                }
            }

            if (IsHeroEntryEligibleForExactPersonalPerks(entryState))
            {
                if (TryApplyExactCampaignMissionEquipmentPerks(agent, entryState, partyState, out string equipmentSummary))
                    parts.Add(equipmentSummary);
                if (TryApplyExactCampaignHeroFlags(agent, entryState, out string flagSummary))
                    parts.Add(flagSummary);
            }

            return parts.Count == 0
                ? "AppliedCombatProfile=(none)"
                : "AppliedCombatProfile=" + string.Join(", ", parts);
        }

        private static bool TryApplyExactCampaignMissionEquipmentPerks(
            Agent agent,
            RosterEntryState entryState,
            BattlePartyState partyState,
            out string summary)
        {
            summary = null;
            if (agent == null || entryState == null || !agent.IsHuman || !IsHeroEntryEligibleForExactPersonalPerks(entryState))
                return false;

            MissionEquipment equipment = agent.Equipment;
            if (equipment == null)
                return false;

            IEnumerable<string> personalPerkIds = entryState.PerkIds ?? Enumerable.Empty<string>();
            IEnumerable<string> partyLeaderPerkIds = partyState?.Modifiers?.PartyLeaderPerkIds ?? Enumerable.Empty<string>();
            bool isMounted = agent.HasMount || agent.MountAgent != null;
            var changes = new List<string>();

            for (EquipmentIndex index = EquipmentIndex.Weapon0; index <= EquipmentIndex.Weapon3; index++)
            {
                MissionWeapon missionWeapon = equipment[index];
                ItemObject item = missionWeapon.Item;
                WeaponComponentData currentUsageItem = missionWeapon.CurrentUsageItem;
                if (item == null || currentUsageItem == null)
                    continue;

                if (currentUsageItem.IsConsumable && currentUsageItem.RelevantSkill != null)
                {
                    float ammoBonus = 0f;
                    if (currentUsageItem.RelevantSkill == DefaultSkills.Bow)
                    {
                        if (HasPerkId(personalPerkIds, "BowDeepQuivers"))
                            ammoBonus += 3f;
                        if (HasPerkId(partyLeaderPerkIds, "BowDeepQuivers"))
                            ammoBonus += 1f;
                    }
                    else if (currentUsageItem.RelevantSkill == DefaultSkills.Crossbow)
                    {
                        if (HasPerkId(personalPerkIds, "CrossbowFletcher"))
                            ammoBonus += 4f;
                        if (HasPerkId(partyLeaderPerkIds, "CrossbowFletcher"))
                            ammoBonus += 2f;
                    }
                    else if (currentUsageItem.RelevantSkill == DefaultSkills.Throwing)
                    {
                        if (HasPerkId(personalPerkIds, "ThrowingWellPrepared"))
                            ammoBonus += 1f;
                        if (HasPerkId(personalPerkIds, "ThrowingResourceful"))
                            ammoBonus += 2f;
                        if (isMounted && HasPerkId(personalPerkIds, "ThrowingSaddlebags"))
                            ammoBonus += 2f;
                        if (HasPerkId(partyLeaderPerkIds, "ThrowingWellPrepared"))
                            ammoBonus += 1f;
                    }

                    int currentAmount = missionWeapon.Amount;
                    int roundedAmmoBonus = Math.Max(0, MathF.Round(ammoBonus));
                    int? snapshotBaseAmount = GetSnapshotCombatItemBaseAmount(entryState, index);
                    int targetAmount = snapshotBaseAmount.HasValue && snapshotBaseAmount.Value > 0
                        ? Math.Max(0, snapshotBaseAmount.Value + roundedAmmoBonus)
                        : Math.Max(0, currentAmount + roundedAmmoBonus);
                    if (targetAmount != currentAmount)
                    {
                        if (GameNetwork.IsServerOrRecorder)
                        {
                            agent.SetWeaponAmountInSlot(index, (short)targetAmount, enforcePrimaryItem: true);
                        }
                        else
                        {
                            equipment.SetAmountOfSlot(index, (short)targetAmount, true);
                        }
                        changes.Add(
                            snapshotBaseAmount.HasValue && snapshotBaseAmount.Value > 0
                                ? index + "Ammo=" + currentAmount + "->" + targetAmount + "(base:" + snapshotBaseAmount.Value + ",+" + roundedAmmoBonus + ")"
                                : index + "Ammo=" + currentAmount + "->" + targetAmount + "(" + (roundedAmmoBonus >= 0 ? "+" : string.Empty) + roundedAmmoBonus + ")");
                    }
                }
                else if (currentUsageItem.IsShield)
                {
                    int currentHitPoints = missionWeapon.HitPoints;
                    float targetHitPointsValue = currentHitPoints;
                    if (HasPerkId(personalPerkIds, "EngineeringScaffolds"))
                        targetHitPointsValue *= 1.3f;
                    int targetHitPoints = Math.Max(1, MathF.Round(targetHitPointsValue));
                    if (targetHitPoints != currentHitPoints)
                    {
                        equipment.SetHitPointsOfSlot(index, (short)targetHitPoints, true);
                        int delta = targetHitPoints - currentHitPoints;
                        changes.Add(index + "Shield=" + (delta >= 0 ? "+" : string.Empty) + delta);
                    }
                }
            }

            if (changes.Count == 0)
                return false;

            try
            {
                agent.UpdateWeapons();
            }
            catch (Exception ex)
            {
                changes.Add("WeaponRefreshFailed=" + ex.GetType().Name);
            }

            summary = "ExactPerks=" + string.Join("/", changes);
            return true;
        }

        private static int? GetSnapshotCombatItemBaseAmount(RosterEntryState entryState, EquipmentIndex index)
        {
            if (entryState == null)
                return null;

            switch (index)
            {
                case EquipmentIndex.Weapon0:
                    return entryState.CombatItem0Amount;
                case EquipmentIndex.Weapon1:
                    return entryState.CombatItem1Amount;
                case EquipmentIndex.Weapon2:
                    return entryState.CombatItem2Amount;
                case EquipmentIndex.Weapon3:
                    return entryState.CombatItem3Amount;
                default:
                    return null;
            }
        }

        private static bool TryApplyExactCampaignHeroFlags(Agent agent, RosterEntryState entryState, out string summary)
        {
            summary = null;
            if (agent == null || entryState?.PerkIds == null || entryState.PerkIds.Count == 0 || !agent.IsHuman || !IsHeroEntryEligibleForExactPersonalPerks(entryState))
                return false;

            int desiredFlagBits = 0;
            if (HasPerkId(entryState.PerkIds, "BowHorseMaster"))
                desiredFlagBits |= AgentFlagHorseMasterBit;
            if (HasPerkId(entryState.PerkIds, "CrossbowMountedCrossbowman"))
                desiredFlagBits |= AgentFlagMountedCrossbowmanBit;
            if (HasPerkId(entryState.PerkIds, "TwoHandedProjectileDeflection"))
                desiredFlagBits |= AgentFlagProjectileDeflectionBit;

            if (desiredFlagBits == 0)
                return false;

            AgentFlag currentFlags = agent.GetAgentFlags();
            int currentFlagBits = (int)currentFlags;
            int updatedFlagBits = currentFlagBits | desiredFlagBits;
            if (updatedFlagBits == currentFlagBits)
                return false;

            agent.SetAgentFlags((AgentFlag)updatedFlagBits);

            var appliedFlags = new List<string>();
            if ((desiredFlagBits & AgentFlagHorseMasterBit) != 0 && (currentFlagBits & AgentFlagHorseMasterBit) == 0)
                appliedFlags.Add("HorseMaster");
            if ((desiredFlagBits & AgentFlagMountedCrossbowmanBit) != 0 && (currentFlagBits & AgentFlagMountedCrossbowmanBit) == 0)
                appliedFlags.Add("MountedCrossbowman");
            if ((desiredFlagBits & AgentFlagProjectileDeflectionBit) != 0 && (currentFlagBits & AgentFlagProjectileDeflectionBit) == 0)
                appliedFlags.Add("ProjectileDeflection");

            if (appliedFlags.Count == 0)
                return false;

            summary = "HeroFlags=" + string.Join("/", appliedFlags);
            return true;
        }

        private static bool IsHeroEntryEligibleForExactPersonalPerks(RosterEntryState entryState)
        {
            return HasEntryExactPersonalPerkHeroIdentity(entryState);
        }

        private static bool IsHeroProfileEligibleForExactPersonalPerks(MaterializedCombatProfileRuntimeState profile)
        {
            return profile != null &&
                   (profile.IsHero ||
                   string.Equals(profile.OriginalCharacterId, "main_hero", StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasEntryExactPersonalPerkHeroIdentity(RosterEntryState entryState)
        {
            return entryState != null &&
                   (entryState.IsHero ||
                    !string.IsNullOrWhiteSpace(entryState.HeroId) ||
                    string.Equals(entryState.OriginalCharacterId, "main_hero", StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasPerkId(IEnumerable<string> perkIds, string perkId)
        {
            if (perkIds == null || string.IsNullOrWhiteSpace(perkId))
                return false;

            foreach (string candidate in perkIds)
            {
                if (string.Equals(candidate, perkId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static void ResetMaterializedCombatProfileRuntimeState()
        {
            _materializedCombatProfilesByAgentIndex.Clear();
            MaterializedCombatProfileApplyCounts.Clear();
            _lastMaterializedCombatProfileMission = null;
            _lastCombatProfileDrivenRefreshMission = null;
            _lastCombatProfileDrivenRefreshMissionTime = -1f;
            _hasLoggedManualCombatProfileRefreshForMission = false;
        }

        private static void ResetMaterializedBattleResultRuntimeState()
        {
            _materializedBattleResultEntriesByEntryId.Clear();
            _materializedBattleResultRemovedAgentIndices.Clear();
            _materializedBattleResultLastHitDebugByVictimAgentIndex.Clear();
            _materializedBattleResultCombatEvents.Clear();
            _materializedBattleResultRawOnScoreHitCount = 0;
            _materializedBattleResultRawOnAgentRemovedCount = 0;
            _materializedBattleResultOnScoreHitCount = 0;
            _materializedBattleResultOnAgentRemovedCount = 0;
            _materializedBattleResultRemovalDebugLogCount = 0;
            _materializedBattleResultOnScoreHitSamples.Clear();
            _materializedBattleResultOnAgentRemovedSamples.Clear();
            _materializedBattleResultReconcileRemovedSamples.Clear();
            _lastMaterializedBattleResultMission = null;
            _hasWrittenBattleResultSnapshotForMission = false;
            _droppedMaterializedBattleResultCombatEventCount = 0;
        }

        private static void EnsureMaterializedCombatProfileMission(Mission mission)
        {
            if (ReferenceEquals(_lastMaterializedCombatProfileMission, mission))
                return;

            _materializedCombatProfilesByAgentIndex.Clear();
            MaterializedCombatProfileApplyCounts.Clear();
            _lastMaterializedCombatProfileMission = mission;
            _lastCombatProfileDrivenRefreshMission = null;
            _lastCombatProfileDrivenRefreshMissionTime = -1f;
            _hasLoggedManualCombatProfileRefreshForMission = false;
        }

        private static void EnsureMaterializedBattleResultMission(Mission mission)
        {
            if (ReferenceEquals(_lastMaterializedBattleResultMission, mission))
                return;

            _materializedBattleResultEntriesByEntryId.Clear();
            _materializedBattleResultRemovedAgentIndices.Clear();
            _materializedBattleResultLastHitDebugByVictimAgentIndex.Clear();
            _materializedBattleResultCombatEvents.Clear();
            _materializedBattleResultRawOnScoreHitCount = 0;
            _materializedBattleResultRawOnAgentRemovedCount = 0;
            _materializedBattleResultOnScoreHitCount = 0;
            _materializedBattleResultOnAgentRemovedCount = 0;
            _materializedBattleResultRemovalDebugLogCount = 0;
            _materializedBattleResultOnScoreHitSamples.Clear();
            _materializedBattleResultOnAgentRemovedSamples.Clear();
            _materializedBattleResultReconcileRemovedSamples.Clear();
            _lastMaterializedBattleResultMission = mission;
            _hasWrittenBattleResultSnapshotForMission = false;
            _droppedMaterializedBattleResultCombatEventCount = 0;
        }

        private static void RegisterMaterializedCombatProfile(Agent agent, RosterEntryState entryState)
        {
            if (agent?.Mission == null || entryState == null)
                return;

            EnsureMaterializedCombatProfileMission(agent.Mission);
            BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
            BattlePartyState partyState = null;
            BattleSideState sideState = null;
            if (runtimeState?.PartiesById != null && !string.IsNullOrWhiteSpace(entryState.PartyId))
            {
                runtimeState.PartiesById.TryGetValue(entryState.PartyId, out partyState);
                if (partyState != null && runtimeState.SidesByKey != null && !string.IsNullOrWhiteSpace(partyState.SideId))
                    runtimeState.SidesByKey.TryGetValue(partyState.SideId, out sideState);
            }

            _materializedCombatProfilesByAgentIndex[agent.Index] = new MaterializedCombatProfileRuntimeState
            {
                EntryId = entryState.EntryId,
                PartyId = entryState.PartyId,
                OriginalCharacterId = entryState.OriginalCharacterId,
                IsHero = HasEntryExactPersonalPerkHeroIdentity(entryState),
                AttributeVigor = entryState.AttributeVigor,
                AttributeControl = entryState.AttributeControl,
                AttributeEndurance = entryState.AttributeEndurance,
                SkillOneHanded = entryState.SkillOneHanded,
                SkillTwoHanded = entryState.SkillTwoHanded,
                SkillPolearm = entryState.SkillPolearm,
                SkillBow = entryState.SkillBow,
                SkillCrossbow = entryState.SkillCrossbow,
                SkillThrowing = entryState.SkillThrowing,
                SkillRiding = entryState.SkillRiding,
                SkillAthletics = entryState.SkillAthletics,
                BaseHitPoints = entryState.BaseHitPoints,
                SideMorale = sideState?.SideMorale ?? 0f,
                PartyMorale = partyState?.Modifiers?.Morale ?? 0f,
                LeaderLeadershipSkill = partyState?.Modifiers?.LeaderLeadershipSkill ?? 0,
                LeaderTacticsSkill = partyState?.Modifiers?.LeaderTacticsSkill ?? 0,
                ScoutScoutingSkill = partyState?.Modifiers?.ScoutScoutingSkill ?? 0,
                QuartermasterStewardSkill = partyState?.Modifiers?.QuartermasterStewardSkill ?? 0,
                EngineerEngineeringSkill = partyState?.Modifiers?.EngineerEngineeringSkill ?? 0,
                SurgeonMedicineSkill = partyState?.Modifiers?.SurgeonMedicineSkill ?? 0,
                PartyLeaderPerkCount = partyState?.Modifiers?.PartyLeaderPerkIds?.Count ?? 0,
                ArmyCommanderPerkCount = partyState?.Modifiers?.ArmyCommanderPerkIds?.Count ?? 0,
                CaptainPerkCount = partyState?.Modifiers?.CaptainPerkIds?.Count ?? 0,
                ScoutRolePerkCount = partyState?.Modifiers?.ScoutPerkIds?.Count ?? 0,
                QuartermasterRolePerkCount = partyState?.Modifiers?.QuartermasterPerkIds?.Count ?? 0,
                EngineerRolePerkCount = partyState?.Modifiers?.EngineerPerkIds?.Count ?? 0,
                SurgeonRolePerkCount = partyState?.Modifiers?.SurgeonPerkIds?.Count ?? 0,
                PerkIds = entryState.PerkIds != null
                    ? entryState.PerkIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                    : new List<string>()
            };
            MaterializedCombatProfileRuntimeState profile = _materializedCombatProfilesByAgentIndex[agent.Index];
            profile.PerkMeleeCount = CountCombatProfilePerksByPrefix(profile.PerkIds, "OneHanded", "TwoHanded", "Polearm");
            profile.PerkRangedCount = CountCombatProfilePerksByPrefix(profile.PerkIds, "Bow", "Crossbow", "Throwing");
            profile.PerkAthleticsCount = CountCombatProfilePerksByPrefix(profile.PerkIds, "Athletics");
            profile.PerkRidingCount = CountCombatProfilePerksByPrefix(profile.PerkIds, "Riding");
            IncrementMaterializedEquipmentCounter(MaterializedCombatProfileApplyCounts, "registered");
            if (profile.PerkIds.Count > 0)
                CountMaterializedCombatProfileApply(profile, "perks", ref profile.CountedPerkRegistration);
        }

        private static void RegisterMaterializedBattleResultEntry(Agent agent, RosterEntryState entryState, BattleSideEnum side)
        {
            if (agent?.Mission == null || entryState == null || string.IsNullOrWhiteSpace(entryState.EntryId))
                return;

            EnsureMaterializedBattleResultMission(agent.Mission);
            _materializedAgentInstanceByIndex[agent.Index] = agent;
            _materializedArmyEntryIdByAgentIndex[agent.Index] = entryState.EntryId;
            if (side != BattleSideEnum.None)
                _materializedArmySideByAgentIndex[agent.Index] = side;
            _materializedBattleResultRemovedAgentIndices.Remove(agent.Index);

            if (!_materializedBattleResultEntriesByEntryId.TryGetValue(entryState.EntryId, out MaterializedBattleResultEntryRuntimeState state))
            {
                state = new MaterializedBattleResultEntryRuntimeState
                {
                    EntryId = entryState.EntryId,
                    SideId = entryState.SideId,
                    PartyId = entryState.PartyId,
                    CharacterId = entryState.CharacterId,
                    OriginalCharacterId = entryState.OriginalCharacterId,
                    SpawnTemplateId = ResolveEntrySpawnTemplateId(entryState),
                    TroopName = entryState.TroopName,
                    HeroId = entryState.HeroId,
                    HeroRole = entryState.HeroRole,
                    IsHero = entryState.IsHero,
                    SnapshotCount = entryState.Count,
                    SnapshotWoundedCount = entryState.WoundedCount
                };
                if (string.IsNullOrWhiteSpace(state.SideId))
                    state.SideId = side == BattleSideEnum.None ? string.Empty : side.ToString();
                _materializedBattleResultEntriesByEntryId[entryState.EntryId] = state;
            }

            state.MaterializedSpawnCount++;
            state.ActiveCount++;
        }

        private static void AddBattleResultDebugSample(ICollection<string> samples, string value)
        {
            if (samples == null || string.IsNullOrWhiteSpace(value) || samples.Count >= MaxBattleResultDebugSampleCount)
                return;

            samples.Add(value);
        }

        private static void TryTrackMaterializedBattleResultScoreHit(
            Agent affectedAgent,
            Agent affectorAgent,
            WeaponComponentData attackerWeapon,
            bool isBlocked,
            bool isSiegeEngineHit,
            in Blow blow,
            in AttackCollisionData collisionData,
            float damagedHp,
            float hitDistance,
            float shotDifficulty)
        {
            if (affectorAgent == null || affectedAgent == null || affectorAgent.Mission == null || damagedHp <= 0.01f)
                return;

            Agent normalizedAffectorAgent = NormalizeBattleResultCombatAffectorAgent(affectorAgent);
            if (normalizedAffectorAgent == null || normalizedAffectorAgent.Mission == null)
                return;

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            if (currentPhase < CoopBattlePhase.BattleActive || currentPhase >= CoopBattlePhase.BattleEnded)
                return;

            _materializedBattleResultOnScoreHitCount++;

            MaterializedBattleResultEntryRuntimeState attackerEntry = TryGetMaterializedBattleResultEntryByAgent(normalizedAffectorAgent, out string attackerEntryId);
            MaterializedBattleResultEntryRuntimeState victimEntry = TryGetMaterializedBattleResultEntryByAgent(affectedAgent, out string victimEntryId);
            if (attackerEntry == null && victimEntry == null)
                return;

            if (attackerEntry != null)
            {
                attackerEntry.ScoreHitCount++;
                attackerEntry.DamageDealt += Math.Max(0f, damagedHp);
            }

            if (victimEntry != null)
            {
                victimEntry.HitsTakenCount++;
                victimEntry.DamageTaken += Math.Max(0f, damagedHp);
            }

            WeaponFlags weaponFlags = attackerWeapon?.WeaponFlags ?? (WeaponFlags)0;
            if (weaponFlags == (WeaponFlags)0)
                weaponFlags = blow.WeaponRecord.WeaponFlags;
            _materializedBattleResultLastHitDebugByVictimAgentIndex[affectedAgent.Index] = new MaterializedBattleResultLastHitDebugState
            {
                AttackerAgentIndex = normalizedAffectorAgent.Index,
                DamageType = ((DamageTypes)collisionData.DamageType).ToString(),
                WeaponClass = ResolveCombatEventWeaponClassHint(attackerWeapon),
                WeaponFlags = weaponFlags.ToString(),
                CanKillEvenIfBlunt = (weaponFlags & WeaponFlags.CanKillEvenIfBlunt) != 0,
                IsHorseCharge = collisionData.IsHorseCharge,
                IsAlternativeAttack = collisionData.IsAlternativeAttack,
                IsFallDamage = collisionData.IsFallDamage,
                IsMissile = collisionData.IsMissile,
                IsBlocked = isBlocked,
                IsSiegeEngineHit = isSiegeEngineHit,
                VictimBodyPart = collisionData.VictimHitBodyPart.ToString(),
                IsHeadShot = collisionData.VictimHitBodyPart == BoneBodyPartType.Head,
                BlowBaseMagnitude = blow.BaseMagnitude,
                CollisionBaseMagnitude = collisionData.BaseMagnitude,
                AbsorbedByArmor = collisionData.AbsorbedByArmor,
                MovementSpeedDamageModifier = collisionData.MovementSpeedDamageModifier,
                MissileVelocity = collisionData.MissileVelocity.Length,
                MissileStartSpeed = collisionData.MissileStartingBaseSpeed,
                MissileTotalDamage = collisionData.MissileTotalDamage,
                Damage = Math.Max(0f, damagedHp),
                MissionTime = normalizedAffectorAgent.Mission.CurrentTime
            };
            AddBattleResultDebugSample(
                _materializedBattleResultOnScoreHitSamples,
                "V=" + affectedAgent.Index +
                " A=" + normalizedAffectorAgent.Index +
                " DmgType=" + ((DamageTypes)collisionData.DamageType) +
                " Horse=" + collisionData.IsHorseCharge +
                " Alt=" + collisionData.IsAlternativeAttack +
                " Fall=" + collisionData.IsFallDamage +
                " Blocked=" + isBlocked +
                " BodyPart=" + collisionData.VictimHitBodyPart +
                " Siege=" + isSiegeEngineHit +
                " Dmg=" + Math.Max(0f, damagedHp).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) +
                " Flags=" + weaponFlags);

            if (_materializedBattleResultCombatEvents.Count >= MaxRecordedBattleResultCombatEvents)
            {
                _droppedMaterializedBattleResultCombatEventCount++;
                return;
            }

            string weaponItemId = ResolveCombatEventWeaponItemId(normalizedAffectorAgent, attackerWeapon);
            ResolveCombatEventCaptainIdentity(
                normalizedAffectorAgent,
                attackerEntry,
                out string captainHeroId,
                out string captainCharacterId,
                out string captainOriginalCharacterId);
            string commanderHeroId = ResolveCombatEventCommanderHeroId(normalizedAffectorAgent, attackerEntry);
            string attackerCharacterId;
            string attackerOriginalCharacterId;
            ResolveBattleResultCharacterIds(normalizedAffectorAgent, attackerEntry, out attackerCharacterId, out attackerOriginalCharacterId);
            string victimCharacterId;
            string victimOriginalCharacterId;
            ResolveBattleResultCharacterIds(affectedAgent, victimEntry, out victimCharacterId, out victimOriginalCharacterId);
            float hitPointRatio = affectedAgent.HealthLimit > 0f
                ? Math.Max(0f, Math.Min(Math.Max(0f, damagedHp), affectedAgent.HealthLimit) / Math.Max(1f, affectedAgent.HealthLimit))
                : 0f;

            _materializedBattleResultCombatEvents.Add(new CoopBattleResultBridgeFile.BattleResultCombatEventSnapshot
            {
                AttackerEntryId = attackerEntryId,
                AttackerSideId = attackerEntry?.SideId,
                AttackerPartyId = attackerEntry?.PartyId,
                AttackerCharacterId = attackerCharacterId,
                AttackerOriginalCharacterId = attackerOriginalCharacterId,
                WeaponItemId = weaponItemId,
                CaptainHeroId = captainHeroId,
                CaptainCharacterId = captainCharacterId,
                CaptainOriginalCharacterId = captainOriginalCharacterId,
                CommanderHeroId = commanderHeroId,
                VictimEntryId = victimEntryId,
                VictimSideId = victimEntry?.SideId,
                VictimPartyId = victimEntry?.PartyId,
                VictimCharacterId = victimCharacterId,
                VictimOriginalCharacterId = victimOriginalCharacterId,
                WeaponSkillHint = ResolveCombatEventSkillHint(attackerWeapon, affectorAgent, isSiegeEngineHit),
                WeaponClassHint = ResolveCombatEventWeaponClassHint(attackerWeapon),
                IsBlocked = isBlocked,
                IsSiegeEngineHit = isSiegeEngineHit,
                IsTeamKill = normalizedAffectorAgent.Team != null && normalizedAffectorAgent.Team == affectedAgent.Team,
                IsFatal = false,
                IsAttackerMounted = normalizedAffectorAgent.MountAgent != null,
                IsAttackerUnderCommand = ResolveCombatEventIsAttackerUnderCommand(normalizedAffectorAgent, attackerEntry, commanderHeroId),
                IsHorseCharge = collisionData.IsHorseCharge,
                IsSneakAttack = collisionData.IsSneakAttack,
                Damage = Math.Max(0f, damagedHp),
                HitDistance = Math.Max(0f, hitDistance),
                MovementSpeedBonus = Math.Max(0f, collisionData.MovementSpeedDamageModifier),
                HitPointRatio = hitPointRatio,
                ShotDifficulty = Math.Max(0f, shotDifficulty),
                MissionTime = normalizedAffectorAgent.Mission.CurrentTime
            });
        }

        private static void TryTrackMaterializedBattleResultRemoval(Agent affectedAgent, Agent affectorAgent, AgentState agentState)
        {
            if (affectedAgent == null || affectedAgent.IsMount || affectedAgent.Mission == null)
                return;

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            if (currentPhase < CoopBattlePhase.BattleActive || currentPhase >= CoopBattlePhase.BattleEnded)
                return;

            _materializedBattleResultOnAgentRemovedCount++;
            AddBattleResultDebugSample(
                _materializedBattleResultOnAgentRemovedSamples,
                "V=" + affectedAgent.Index +
                " A=" + (affectorAgent?.Index.ToString() ?? "none") +
                " State=" + agentState +
                " Health=" + affectedAgent.Health.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));

            if (!TryRefreshMaterializedAgentIdentityCache(
                    affectedAgent,
                    out string entryId,
                    out BattleSideEnum side,
                    out bool requiresRegistration,
                    "battle-result-removal-victim",
                    logRefresh: true))
            {
                return;
            }

            if (requiresRegistration)
            {
                RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(entryId);
                if (entryState != null)
                    RegisterMaterializedBattleResultEntry(affectedAgent, entryState, side);
            }

            if (!_materializedBattleResultRemovedAgentIndices.Add(affectedAgent.Index))
                return;

            if (!_materializedBattleResultEntriesByEntryId.TryGetValue(entryId, out MaterializedBattleResultEntryRuntimeState state) || state == null)
                return;

            state.ActiveCount = Math.Max(0, state.ActiveCount - 1);
            state.RemovedCount++;

            string removedState = agentState.ToString();
            if (string.Equals(removedState, "Killed", StringComparison.OrdinalIgnoreCase))
                state.KilledCount++;
            else if (string.Equals(removedState, "Unconscious", StringComparison.OrdinalIgnoreCase))
                state.UnconsciousCount++;
            else if (string.Equals(removedState, "Routed", StringComparison.OrdinalIgnoreCase))
                state.RoutedCount++;
            else
                state.OtherRemovedCount++;

            MaterializedBattleResultEntryRuntimeState attackerState = TryGetMaterializedBattleResultEntryByAgent(affectorAgent, out _);
            if (attackerState != null)
            {
                if (string.Equals(removedState, "Killed", StringComparison.OrdinalIgnoreCase))
                {
                    attackerState.KillsInflictedCount++;
                    attackerState.FatalHitCount++;
                }
                else if (string.Equals(removedState, "Unconscious", StringComparison.OrdinalIgnoreCase))
                    attackerState.UnconsciousInflictedCount++;
                else if (string.Equals(removedState, "Routed", StringComparison.OrdinalIgnoreCase))
                    attackerState.RoutedInflictedCount++;
            }

            bool matchedExistingCombatEvent = false;
            if (_materializedBattleResultCombatEvents.Count > 0)
            {
                for (int i = _materializedBattleResultCombatEvents.Count - 1; i >= 0; i--)
                {
                    CoopBattleResultBridgeFile.BattleResultCombatEventSnapshot combatEvent = _materializedBattleResultCombatEvents[i];
                    if (combatEvent == null)
                        continue;

                    if (!string.Equals(combatEvent.VictimEntryId, entryId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    matchedExistingCombatEvent = true;
                    if (combatEvent.IsFatal)
                        break;

                    combatEvent.IsFatal = string.Equals(removedState, "Killed", StringComparison.OrdinalIgnoreCase);
                    break;
                }
            }

            TryTrackSyntheticRemovalCombatEvent(
                affectedAgent,
                affectorAgent,
                removedState,
                entryId,
                state,
                attackerState,
                matchedExistingCombatEvent);
        }

        private static void TryLogMaterializedBattleResultRemovalDebug(
            Agent affectedAgent,
            Agent affectorAgent,
            AgentState agentState,
            KillingBlow killingBlow)
        {
            if (affectedAgent == null || affectedAgent.IsMount || affectedAgent.Mission == null)
                return;

            _materializedBattleResultRemovalDebugLogCount++;

            TryRefreshMaterializedAgentIdentityCache(
                affectedAgent,
                out string victimEntryId,
                out _,
                out _,
                "battle-result-removal-debug-victim",
                logRefresh: true);
            TryRefreshMaterializedAgentIdentityCache(
                affectorAgent,
                out string attackerEntryId,
                out _,
                out _,
                "battle-result-removal-debug-attacker",
                logRefresh: true);
            string removedState = agentState.ToString();
            string victimCharacterId = (affectedAgent.Character as BasicCharacterObject)?.StringId ?? string.Empty;
            string attackerCharacterId = (affectorAgent?.Character as BasicCharacterObject)?.StringId ?? string.Empty;
            WeaponFlags killingBlowFlags = killingBlow.WeaponRecordWeaponFlags;
            bool canKillEvenIfBlunt = (killingBlowFlags & WeaponFlags.CanKillEvenIfBlunt) != 0;

            _materializedBattleResultLastHitDebugByVictimAgentIndex.TryGetValue(affectedAgent.Index, out MaterializedBattleResultLastHitDebugState lastHit);

            ModLogger.Info(
                "CoopMissionSpawnLogic: removal debug. " +
                "VictimAgent=" + affectedAgent.Index +
                " VictimEntry=" + (victimEntryId ?? string.Empty) +
                " VictimCharacter=" + (victimCharacterId ?? string.Empty) +
                " AttackerAgent=" + (affectorAgent?.Index.ToString() ?? "none") +
                " AttackerEntry=" + (attackerEntryId ?? string.Empty) +
                " AttackerCharacter=" + (attackerCharacterId ?? string.Empty) +
                " RemovedState=" + removedState +
                " KillingBlow[Valid=" + killingBlow.IsValid +
                " DamageType=" + killingBlow.DamageType +
                " AttackType=" + killingBlow.AttackType +
                " WeaponClass=" + ((WeaponClass)killingBlow.WeaponClass) +
                " WeaponFlags=" + killingBlowFlags +
                " CanKillEvenIfBlunt=" + canKillEvenIfBlunt +
                " IsMissile=" + killingBlow.IsMissile +
                " InflictedDamage=" + killingBlow.InflictedDamage + "]" +
                " LastHit[" + BuildMaterializedBattleResultLastHitDebugSummary(lastHit) + "].");

            _materializedBattleResultLastHitDebugByVictimAgentIndex.Remove(affectedAgent.Index);
        }

        private static string BuildMaterializedBattleResultLastHitDebugSummary(MaterializedBattleResultLastHitDebugState state)
        {
            if (state == null)
                return "none";

            return
                "AttackerAgent=" + state.AttackerAgentIndex +
                " DamageType=" + (state.DamageType ?? string.Empty) +
                " WeaponClass=" + (state.WeaponClass ?? string.Empty) +
                " WeaponFlags=" + (state.WeaponFlags ?? string.Empty) +
                " CanKillEvenIfBlunt=" + state.CanKillEvenIfBlunt +
                " HorseCharge=" + state.IsHorseCharge +
                " AltAttack=" + state.IsAlternativeAttack +
                " FallDamage=" + state.IsFallDamage +
                " Missile=" + state.IsMissile +
                " Blocked=" + state.IsBlocked +
                " Siege=" + state.IsSiegeEngineHit +
                " BodyPart=" + (state.VictimBodyPart ?? string.Empty) +
                " HeadShot=" + state.IsHeadShot +
                " BlowBaseMagnitude=" + state.BlowBaseMagnitude.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " CollisionBaseMagnitude=" + state.CollisionBaseMagnitude +
                " AbsorbedByArmor=" + state.AbsorbedByArmor +
                " MoveSpeedMod=" + state.MovementSpeedDamageModifier.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " MissileVelocity=" + state.MissileVelocity.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " MissileStartSpeed=" + state.MissileStartSpeed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " MissileTotalDamage=" + state.MissileTotalDamage.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " Damage=" + state.Damage.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) +
                " MissionTime=" + state.MissionTime.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void ClearMaterializedAgentIndexScopedRuntimeCaches(int agentIndex, bool clearRemovedGuard)
        {
            if (agentIndex < 0)
                return;

            _materializedArmyEntryIdByAgentIndex.Remove(agentIndex);
            _materializedArmySideByAgentIndex.Remove(agentIndex);
            _materializedAgentInstanceByIndex.Remove(agentIndex);
            _materializedCombatProfilesByAgentIndex.Remove(agentIndex);
            _materializedBattleResultLastHitDebugByVictimAgentIndex.Remove(agentIndex);
            if (clearRemovedGuard)
                _materializedBattleResultRemovedAgentIndices.Remove(agentIndex);
            _exactNativeSnapshotOverlayAppliedAgentIndices.Remove(agentIndex);
            _exactNativeClientVisualOverlayAppliedAgentIndices.Remove(agentIndex);
            _exactNativeClientVisualOverlayEntryIdByAgentIndex.Remove(agentIndex);
            _pendingExactNativeClientVisualOverlaysByAgentIndex.Remove(agentIndex);
        }

        private static bool TryRefreshMaterializedAgentIdentityCache(
            Agent agent,
            out string entryId,
            out BattleSideEnum side,
            out bool requiresRegistration,
            string source = null,
            bool logRefresh = false)
        {
            entryId = null;
            side = BattleSideEnum.None;
            requiresRegistration = false;
            if (agent == null || agent.IsMount)
                return false;

            bool hadCachedEntry = _materializedArmyEntryIdByAgentIndex.TryGetValue(agent.Index, out string cachedEntryId) &&
                                  !string.IsNullOrWhiteSpace(cachedEntryId);
            bool hadCachedSide = _materializedArmySideByAgentIndex.TryGetValue(agent.Index, out BattleSideEnum cachedSide) &&
                                 cachedSide != BattleSideEnum.None;
            bool hadCachedAgentInstance = _materializedAgentInstanceByIndex.TryGetValue(agent.Index, out Agent cachedAgentInstance);
            bool agentIndexRebound = hadCachedAgentInstance && !ReferenceEquals(cachedAgentInstance, agent);

            string exactEntryId = null;
            ExactCampaignArmyBootstrap.TryGetEntryId(agent, out exactEntryId);
            BattleSideEnum exactSide = BattleSideEnum.None;
            ExactCampaignArmyBootstrap.TryGetSide(agent, out exactSide);

            string resolvedEntryId = !string.IsNullOrWhiteSpace(exactEntryId)
                ? exactEntryId
                : cachedEntryId;
            BattleSideEnum resolvedSide = exactSide != BattleSideEnum.None
                ? exactSide
                : cachedSide;

            if (string.IsNullOrWhiteSpace(resolvedEntryId))
                return false;

            bool entryChanged = !hadCachedEntry || !string.Equals(cachedEntryId, resolvedEntryId, StringComparison.OrdinalIgnoreCase);
            bool sideChanged = resolvedSide != BattleSideEnum.None &&
                               (!hadCachedSide || cachedSide != resolvedSide);
            bool shouldResetIndexState = agentIndexRebound || (hadCachedEntry && entryChanged) || (hadCachedSide && sideChanged);
            bool clearedRemovedGuard = false;

            if (shouldResetIndexState)
            {
                clearedRemovedGuard = _materializedBattleResultRemovedAgentIndices.Contains(agent.Index);
                ClearMaterializedAgentIndexScopedRuntimeCaches(agent.Index, clearRemovedGuard: true);
            }
            else if (agent.IsActive())
            {
                clearedRemovedGuard = _materializedBattleResultRemovedAgentIndices.Remove(agent.Index);
            }

            _materializedAgentInstanceByIndex[agent.Index] = agent;
            _materializedArmyEntryIdByAgentIndex[agent.Index] = resolvedEntryId;
            if (resolvedSide != BattleSideEnum.None)
                _materializedArmySideByAgentIndex[agent.Index] = resolvedSide;

            requiresRegistration =
                shouldResetIndexState ||
                clearedRemovedGuard ||
                !hadCachedEntry;
            entryId = resolvedEntryId;
            side = resolvedSide;

            if (logRefresh && (shouldResetIndexState || clearedRemovedGuard))
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: refreshed materialized agent identity cache from exact origin. " +
                    "AgentIndex=" + agent.Index +
                    " PreviousEntryId=" + (cachedEntryId ?? "null") +
                    " EntryId=" + resolvedEntryId +
                    " PreviousSide=" + (hadCachedSide ? cachedSide.ToString() : "none") +
                    " Side=" + (resolvedSide == BattleSideEnum.None ? "none" : resolvedSide.ToString()) +
                    " Character=" + ((agent.Character as BasicCharacterObject)?.StringId ?? "null") +
                    " Rebound=" + agentIndexRebound +
                    " ClearedRemovedGuard=" + clearedRemovedGuard +
                    " Source=" + (source ?? "unknown"));
            }

            return true;
        }

        private static MaterializedBattleResultEntryRuntimeState TryGetMaterializedBattleResultEntryByAgent(Agent agent, out string entryId)
        {
            entryId = null;
            if (!TryRefreshMaterializedAgentIdentityCache(
                    agent,
                    out entryId,
                    out BattleSideEnum side,
                    out bool requiresRegistration,
                    "battle-result-agent-resolve"))
            {
                return null;
            }

            if (requiresRegistration)
            {
                RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(entryId);
                if (entryState != null)
                    RegisterMaterializedBattleResultEntry(agent, entryState, side);
            }

            if (!_materializedBattleResultEntriesByEntryId.TryGetValue(entryId, out MaterializedBattleResultEntryRuntimeState state))
                return null;

            return state;
        }

        private static Agent NormalizeBattleResultCombatAffectorAgent(Agent affectorAgent)
        {
            if (affectorAgent == null)
                return null;

            try
            {
                if (affectorAgent.IsMount && affectorAgent.RiderAgent != null)
                    return affectorAgent.RiderAgent;
            }
            catch
            {
            }

            return affectorAgent;
        }

        private static void ResolveBattleResultCharacterIds(
            Agent agent,
            MaterializedBattleResultEntryRuntimeState entryState,
            out string characterId,
            out string originalCharacterId)
        {
            characterId = entryState?.CharacterId;
            originalCharacterId = entryState?.OriginalCharacterId;

            BasicCharacterObject character = agent?.Character as BasicCharacterObject;
            if (string.IsNullOrWhiteSpace(characterId))
                characterId = character?.StringId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(originalCharacterId))
                originalCharacterId = character?.StringId ?? string.Empty;
        }

        private static string ResolveCombatEventWeaponItemId(Agent affectorAgent, WeaponComponentData attackerWeapon)
        {
            try
            {
                MissionWeapon wieldedWeapon = affectorAgent?.WieldedWeapon ?? MissionWeapon.Invalid;
                string wieldedItemId = wieldedWeapon.Item?.StringId;
                if (!string.IsNullOrWhiteSpace(wieldedItemId))
                    return wieldedItemId;
            }
            catch
            {
            }

            MissionEquipment affectorEquipment = affectorAgent?.Equipment;
            ItemObject primaryItem = TryResolveAgentPrimaryCombatItem(affectorAgent, affectorEquipment, out _, out _);
            if (!string.IsNullOrWhiteSpace(primaryItem?.StringId))
                return primaryItem.StringId;

            return string.Empty;
        }

        private static void ResolveCombatEventCaptainIdentity(
            Agent affectorAgent,
            MaterializedBattleResultEntryRuntimeState attackerEntry,
            out string captainHeroId,
            out string captainCharacterId,
            out string captainOriginalCharacterId)
        {
            captainHeroId = string.Empty;
            captainCharacterId = string.Empty;
            captainOriginalCharacterId = string.Empty;

            Agent captainAgent = null;
            try
            {
                captainAgent = affectorAgent?.Formation?.Captain;
            }
            catch
            {
                captainAgent = null;
            }

            captainAgent = NormalizeBattleResultCombatAffectorAgent(captainAgent);
            if (captainAgent != null && !ReferenceEquals(captainAgent, affectorAgent))
            {
                MaterializedBattleResultEntryRuntimeState captainEntry = TryGetMaterializedBattleResultEntryByAgent(captainAgent, out _);
                captainHeroId = captainEntry?.HeroId ?? string.Empty;
                ResolveBattleResultCharacterIds(captainAgent, captainEntry, out captainCharacterId, out captainOriginalCharacterId);
                if (!string.IsNullOrWhiteSpace(captainHeroId) || !string.IsNullOrWhiteSpace(captainCharacterId))
                    return;
            }

            if (string.IsNullOrWhiteSpace(attackerEntry?.PartyId))
                return;

            captainHeroId = TryResolvePartyLeaderHeroIdForCombatEvent(attackerEntry.PartyId);
            if (string.IsNullOrWhiteSpace(captainHeroId))
                return;

            RosterEntryState leaderEntry = TryResolveRuntimeEntryByHeroIdForCombatEvent(attackerEntry.PartyId, captainHeroId);
            if (leaderEntry == null)
                return;

            captainCharacterId = leaderEntry.CharacterId ?? string.Empty;
            captainOriginalCharacterId = leaderEntry.OriginalCharacterId ?? leaderEntry.CharacterId ?? string.Empty;
        }

        private static string ResolveCombatEventCommanderHeroId(
            Agent affectorAgent,
            MaterializedBattleResultEntryRuntimeState attackerEntry)
        {
            Agent commanderAgent = null;
            try
            {
                commanderAgent = affectorAgent?.Team?.Leader;
            }
            catch
            {
                commanderAgent = null;
            }

            commanderAgent = NormalizeBattleResultCombatAffectorAgent(commanderAgent);
            if (commanderAgent != null)
            {
                MaterializedBattleResultEntryRuntimeState commanderEntry = TryGetMaterializedBattleResultEntryByAgent(commanderAgent, out _);
                if (!string.IsNullOrWhiteSpace(commanderEntry?.HeroId))
                    return commanderEntry.HeroId;
            }

            string sideLeaderHeroId = TryResolveSideLeaderHeroIdForCombatEvent(attackerEntry?.SideId);
            if (!string.IsNullOrWhiteSpace(sideLeaderHeroId))
                return sideLeaderHeroId;

            return TryResolvePartyLeaderHeroIdForCombatEvent(attackerEntry?.PartyId);
        }

        private static bool ResolveCombatEventIsAttackerUnderCommand(
            Agent affectorAgent,
            MaterializedBattleResultEntryRuntimeState attackerEntry,
            string commanderHeroId)
        {
            if (affectorAgent == null || string.IsNullOrWhiteSpace(commanderHeroId))
                return false;

            if (!string.IsNullOrWhiteSpace(attackerEntry?.HeroId) &&
                string.Equals(attackerEntry.HeroId, commanderHeroId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static string TryResolvePartyLeaderHeroIdForCombatEvent(string partyId)
        {
            if (string.IsNullOrWhiteSpace(partyId))
                return string.Empty;

            BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
            if (runtimeState?.PartiesById == null ||
                !runtimeState.PartiesById.TryGetValue(partyId, out BattlePartyState partyState))
            {
                return string.Empty;
            }

            return partyState?.Modifiers?.LeaderHeroId ?? string.Empty;
        }

        private static string TryResolveSideLeaderHeroIdForCombatEvent(string sideId)
        {
            if (string.IsNullOrWhiteSpace(sideId))
                return string.Empty;

            BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
            if (runtimeState?.SidesByKey == null ||
                !runtimeState.SidesByKey.TryGetValue(sideId, out BattleSideState sideState))
            {
                return string.Empty;
            }

            return TryResolvePartyLeaderHeroIdForCombatEvent(sideState?.LeaderPartyId);
        }

        private static RosterEntryState TryResolveRuntimeEntryByHeroIdForCombatEvent(string partyId, string heroId)
        {
            if (string.IsNullOrWhiteSpace(partyId) || string.IsNullOrWhiteSpace(heroId))
                return null;

            BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
            if (runtimeState?.PartiesById == null ||
                !runtimeState.PartiesById.TryGetValue(partyId, out BattlePartyState partyState) ||
                partyState?.Entries == null)
            {
                return null;
            }

            return partyState.Entries.FirstOrDefault(entry =>
                entry != null &&
                string.Equals(entry.HeroId, heroId, StringComparison.OrdinalIgnoreCase));
        }

        private static string ResolveCombatEventSkillHint(WeaponComponentData attackerWeapon, Agent affectorAgent, bool isSiegeEngineHit)
        {
            try
            {
                SkillObject relevantSkill = attackerWeapon?.RelevantSkill;
                if (!string.IsNullOrWhiteSpace(relevantSkill?.StringId))
                    return relevantSkill.StringId;
            }
            catch
            {
            }

            if (attackerWeapon != null)
            {
                string byWeaponClass = MapCombatEventWeaponClassToSkillHint(attackerWeapon.WeaponClass.ToString(), isSiegeEngineHit);
                if (!string.IsNullOrWhiteSpace(byWeaponClass))
                    return byWeaponClass;
            }

            MissionEquipment affectorEquipment = affectorAgent?.Equipment;
            ItemObject primaryItem = TryResolveAgentPrimaryCombatItem(affectorAgent, affectorEquipment, out WeaponComponentData primaryWeapon, out SkillObject relevantSkillObject);
            if (primaryItem != null)
            {
                if (!string.IsNullOrWhiteSpace(relevantSkillObject?.StringId))
                    return relevantSkillObject.StringId;

                if (primaryWeapon != null)
                {
                    string byPrimaryWeaponClass = MapCombatEventWeaponClassToSkillHint(primaryWeapon.WeaponClass.ToString(), isSiegeEngineHit);
                    if (!string.IsNullOrWhiteSpace(byPrimaryWeaponClass))
                        return byPrimaryWeaponClass;
                }
            }

            return string.Empty;
        }

        private static string ResolveCombatEventWeaponClassHint(WeaponComponentData attackerWeapon)
        {
            try
            {
                return attackerWeapon?.WeaponClass.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void TryTrackSyntheticRemovalCombatEvent(
            Agent affectedAgent,
            Agent affectorAgent,
            string removedState,
            string victimEntryId,
            MaterializedBattleResultEntryRuntimeState victimEntry,
            MaterializedBattleResultEntryRuntimeState attackerEntry,
            bool matchedExistingCombatEvent)
        {
            Agent normalizedAffectorAgent = NormalizeBattleResultCombatAffectorAgent(affectorAgent);
            if (matchedExistingCombatEvent ||
                affectedAgent == null ||
                victimEntry == null ||
                normalizedAffectorAgent == null ||
                attackerEntry == null ||
                _materializedBattleResultCombatEvents.Count >= MaxRecordedBattleResultCombatEvents)
            {
                return;
            }

            bool isFatal = string.Equals(removedState, "Killed", StringComparison.OrdinalIgnoreCase);
            bool isUnconscious = string.Equals(removedState, "Unconscious", StringComparison.OrdinalIgnoreCase);
            if (!isFatal && !isUnconscious)
                return;

            WeaponComponentData attackerWeapon = TryResolveCurrentWeaponComponent(normalizedAffectorAgent);
            float syntheticDamage = Math.Max(1f, affectedAgent.HealthLimit);
            attackerEntry.ScoreHitCount++;
            attackerEntry.DamageDealt += syntheticDamage;
            victimEntry.HitsTakenCount++;
            victimEntry.DamageTaken += syntheticDamage;

            string attackerEntryId;
            TryGetMaterializedBattleResultEntryByAgent(normalizedAffectorAgent, out attackerEntryId);
            ResolveBattleResultCharacterIds(normalizedAffectorAgent, attackerEntry, out string attackerCharacterId, out string attackerOriginalCharacterId);
            ResolveBattleResultCharacterIds(affectedAgent, victimEntry, out string victimCharacterId, out string victimOriginalCharacterId);
            ResolveCombatEventCaptainIdentity(
                normalizedAffectorAgent,
                attackerEntry,
                out string captainHeroId,
                out string captainCharacterId,
                out string captainOriginalCharacterId);
            string commanderHeroId = ResolveCombatEventCommanderHeroId(normalizedAffectorAgent, attackerEntry);

            _materializedBattleResultCombatEvents.Add(new CoopBattleResultBridgeFile.BattleResultCombatEventSnapshot
            {
                AttackerEntryId = attackerEntryId,
                AttackerSideId = attackerEntry.SideId,
                AttackerPartyId = attackerEntry.PartyId,
                AttackerCharacterId = attackerCharacterId,
                AttackerOriginalCharacterId = attackerOriginalCharacterId,
                WeaponItemId = ResolveCombatEventWeaponItemId(normalizedAffectorAgent, attackerWeapon),
                CaptainHeroId = captainHeroId,
                CaptainCharacterId = captainCharacterId,
                CaptainOriginalCharacterId = captainOriginalCharacterId,
                CommanderHeroId = commanderHeroId,
                VictimEntryId = victimEntryId,
                VictimSideId = victimEntry.SideId,
                VictimPartyId = victimEntry.PartyId,
                VictimCharacterId = victimCharacterId,
                VictimOriginalCharacterId = victimOriginalCharacterId,
                WeaponSkillHint = ResolveCombatEventSkillHint(attackerWeapon, affectorAgent, false),
                WeaponClassHint = ResolveCombatEventWeaponClassHint(attackerWeapon),
                IsBlocked = false,
                IsSiegeEngineHit = false,
                IsTeamKill = normalizedAffectorAgent.Team != null && normalizedAffectorAgent.Team == affectedAgent.Team,
                IsFatal = isFatal,
                IsAttackerMounted = normalizedAffectorAgent.MountAgent != null,
                IsAttackerUnderCommand = ResolveCombatEventIsAttackerUnderCommand(normalizedAffectorAgent, attackerEntry, commanderHeroId),
                IsHorseCharge = false,
                IsSneakAttack = false,
                Damage = syntheticDamage,
                HitDistance = 0f,
                MovementSpeedBonus = 0f,
                HitPointRatio = 1f,
                ShotDifficulty = 0f,
                MissionTime = affectedAgent.Mission?.CurrentTime ?? 0f
            });
        }

        private static WeaponComponentData TryResolveCurrentWeaponComponent(Agent agent)
        {
            try
            {
                MissionWeapon wieldedWeapon = agent?.WieldedWeapon ?? MissionWeapon.Invalid;
                return wieldedWeapon.CurrentUsageItem;
            }
            catch
            {
                return null;
            }
        }

        private static string MapCombatEventWeaponClassToSkillHint(string weaponClass, bool isSiegeEngineHit)
        {
            if (isSiegeEngineHit)
                return "SiegeEngine";

            string normalized = weaponClass ?? string.Empty;
            if (normalized.IndexOf("Crossbow", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Crossbow";
            if (normalized.IndexOf("Bow", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Bow";
            if (normalized.IndexOf("Throw", StringComparison.OrdinalIgnoreCase) >= 0 || normalized.IndexOf("Javelin", StringComparison.OrdinalIgnoreCase) >= 0 || normalized.IndexOf("Stone", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Throwing";
            if (normalized.IndexOf("Polearm", StringComparison.OrdinalIgnoreCase) >= 0 || normalized.IndexOf("Spear", StringComparison.OrdinalIgnoreCase) >= 0 || normalized.IndexOf("Lance", StringComparison.OrdinalIgnoreCase) >= 0 || normalized.IndexOf("Pike", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Polearm";
            if (normalized.IndexOf("TwoHanded", StringComparison.OrdinalIgnoreCase) >= 0 || normalized.IndexOf("TwoHand", StringComparison.OrdinalIgnoreCase) >= 0)
                return "TwoHanded";
            if (normalized.IndexOf("OneHanded", StringComparison.OrdinalIgnoreCase) >= 0 || normalized.IndexOf("OneHand", StringComparison.OrdinalIgnoreCase) >= 0 || normalized.IndexOf("Mace", StringComparison.OrdinalIgnoreCase) >= 0 || normalized.IndexOf("Axe", StringComparison.OrdinalIgnoreCase) >= 0 || normalized.IndexOf("Sword", StringComparison.OrdinalIgnoreCase) >= 0)
                return "OneHanded";
            return string.Empty;
        }

        private static void TryWriteBattleResultSnapshot(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer)
                return;

            EnsureMaterializedBattleResultMission(mission);
            if (_hasWrittenBattleResultSnapshotForMission)
                return;

            CoopBattleResultBridgeFile.BattleResultSnapshot snapshot = BuildBattleResultSnapshot(mission, source);
            if (snapshot == null)
                return;

            if (!CoopBattleResultBridgeFile.WriteResult(snapshot))
                return;

            _hasWrittenBattleResultSnapshotForMission = true;
            ModLogger.Info(
                "CoopMissionSpawnLogic: battle result snapshot written. " +
                "BattleId=" + (snapshot.BattleId ?? "null") +
                " WinnerSide=" + (snapshot.WinnerSide ?? "none") +
                " Entries=" + snapshot.Entries.Count +
                " Summary=[" + BuildBattleResultEntryAuditSummary(snapshot) + "].");
        }

        private static void ReconcileMaterializedBattleResultStateFromMission(Mission mission, string source)
        {
            if (mission == null || _materializedBattleResultEntriesByEntryId.Count == 0)
                return;

            var countsByEntryId = new Dictionary<string, MaterializedBattleResultReconciledCounts>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, MaterializedBattleResultEntryRuntimeState> pair in _materializedBattleResultEntriesByEntryId)
            {
                if (pair.Value == null || string.IsNullOrWhiteSpace(pair.Key))
                    continue;

                countsByEntryId[pair.Key] = new MaterializedBattleResultReconciledCounts();
            }

            int trackedAgents = 0;
            int activeTotal = 0;
            int removedTotal = 0;
            int killedTotal = 0;
            int unconsciousTotal = 0;
            int routedTotal = 0;
            int otherRemovedTotal = 0;
            int exactOriginFallbackAgents = 0;
            _materializedBattleResultReconcileRemovedSamples.Clear();

            for (int i = 0; i < mission.AllAgents.Count; i++)
            {
                Agent agent = mission.AllAgents[i];
                if (agent == null || agent.IsMount)
                    continue;

                bool usedExactOriginFallback = false;
                if (!TryRefreshMaterializedAgentIdentityCache(
                        agent,
                        out string entryId,
                        out BattleSideEnum originSide,
                        out bool requiresRegistration,
                        source,
                        logRefresh: true))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entryId) ||
                    !countsByEntryId.TryGetValue(entryId, out MaterializedBattleResultReconciledCounts counts))
                {
                    continue;
                }

                if (requiresRegistration)
                {
                    usedExactOriginFallback = true;
                    RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(entryId);
                    if (entryState != null)
                        RegisterMaterializedBattleResultEntry(agent, entryState, originSide);
                }

                if (usedExactOriginFallback)
                    exactOriginFallbackAgents++;

                trackedAgents++;
                counts.ObservedDamageTaken += Math.Max(0f, agent.HealthLimit - agent.Health);
                if (agent.IsActive())
                {
                    counts.ActiveCount++;
                    activeTotal++;
                    continue;
                }

                string stateText = agent.State.ToString();
                if (string.Equals(stateText, "Killed", StringComparison.OrdinalIgnoreCase))
                {
                    counts.KilledCount++;
                    killedTotal++;
                    AddBattleResultDebugSample(
                        _materializedBattleResultReconcileRemovedSamples,
                        "V=" + agent.Index +
                        " Entry=" + entryId +
                        " State=Killed" +
                        " Health=" + agent.Health.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
                }
                else if (string.Equals(stateText, "Unconscious", StringComparison.OrdinalIgnoreCase))
                {
                    counts.UnconsciousCount++;
                    unconsciousTotal++;
                    AddBattleResultDebugSample(
                        _materializedBattleResultReconcileRemovedSamples,
                        "V=" + agent.Index +
                        " Entry=" + entryId +
                        " State=Unconscious" +
                        " Health=" + agent.Health.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
                }
                else if (string.Equals(stateText, "Routed", StringComparison.OrdinalIgnoreCase))
                {
                    counts.RoutedCount++;
                    routedTotal++;
                    AddBattleResultDebugSample(
                        _materializedBattleResultReconcileRemovedSamples,
                        "V=" + agent.Index +
                        " Entry=" + entryId +
                        " State=Routed" +
                        " Health=" + agent.Health.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    counts.OtherRemovedCount++;
                    otherRemovedTotal++;
                    AddBattleResultDebugSample(
                        _materializedBattleResultReconcileRemovedSamples,
                        "V=" + agent.Index +
                        " Entry=" + entryId +
                        " State=" + stateText +
                        " Health=" + agent.Health.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
                }

                removedTotal++;
            }

            foreach (KeyValuePair<string, MaterializedBattleResultEntryRuntimeState> pair in _materializedBattleResultEntriesByEntryId)
            {
                string entryId = pair.Key;
                MaterializedBattleResultEntryRuntimeState runtimeState = pair.Value;
                if (runtimeState == null || string.IsNullOrWhiteSpace(entryId))
                    continue;

                if (!countsByEntryId.TryGetValue(entryId, out MaterializedBattleResultReconciledCounts counts))
                    counts = new MaterializedBattleResultReconciledCounts();

                int accountedCount =
                    counts.ActiveCount +
                    counts.KilledCount +
                    counts.UnconsciousCount +
                    counts.RoutedCount +
                    counts.OtherRemovedCount;
                int missingCount = Math.Max(0, runtimeState.MaterializedSpawnCount - accountedCount);
                if (missingCount > 0)
                {
                    counts.OtherRemovedCount += missingCount;
                    removedTotal += missingCount;
                    otherRemovedTotal += missingCount;
                }

                runtimeState.ActiveCount = counts.ActiveCount;
                runtimeState.KilledCount = counts.KilledCount;
                runtimeState.UnconsciousCount = counts.UnconsciousCount;
                runtimeState.RoutedCount = counts.RoutedCount;
                runtimeState.OtherRemovedCount = counts.OtherRemovedCount;
                runtimeState.RemovedCount =
                    counts.KilledCount +
                    counts.UnconsciousCount +
                    counts.RoutedCount +
                    counts.OtherRemovedCount;
                runtimeState.DamageTaken = Math.Max(runtimeState.DamageTaken, Math.Max(0f, counts.ObservedDamageTaken));
            }

            ModLogger.Info(
                "CoopMissionSpawnLogic: reconciled battle result state from mission. " +
                "Entries=" + _materializedBattleResultEntriesByEntryId.Count +
                " TrackedAgents=" + trackedAgents +
                " Active=" + activeTotal +
                " Removed=" + removedTotal +
                " Killed=" + killedTotal +
                " Unconscious=" + unconsciousTotal +
                " Routed=" + routedTotal +
                " OtherRemoved=" + otherRemovedTotal +
                " ExactOriginFallbackAgents=" + exactOriginFallbackAgents +
                " RawScoreHitCalls=" + _materializedBattleResultRawOnScoreHitCount +
                " RawAgentRemovedCalls=" + _materializedBattleResultRawOnAgentRemovedCount +
                " ScoreHits=" + _materializedBattleResultOnScoreHitCount +
                " AgentRemovedCalls=" + _materializedBattleResultOnAgentRemovedCount +
                " RemovalDebugLogs=" + _materializedBattleResultRemovalDebugLogCount +
                " ScoreHitSamples=[" + string.Join("; ", _materializedBattleResultOnScoreHitSamples) + "]" +
                " AgentRemovedSamples=[" + string.Join("; ", _materializedBattleResultOnAgentRemovedSamples) + "]" +
                " ReconcileRemovedSamples=[" + string.Join("; ", _materializedBattleResultReconcileRemovedSamples) + "]" +
                " Source=" + (source ?? "unknown") + ".");
        }

        private static CoopBattleResultBridgeFile.BattleResultSnapshot BuildBattleResultSnapshot(Mission mission, string source)
        {
            BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
            BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
            if (runtimeState?.EntriesById == null || runtimeState.EntriesById.Count == 0)
                return null;

            ReconcileMaterializedBattleResultStateFromMission(mission, source);

            var result = new CoopBattleResultBridgeFile.BattleResultSnapshot
            {
                BattleId = snapshot?.BattleId,
                BattleType = snapshot?.BattleType,
                MapScene = mission.SceneName ?? snapshot?.MapScene ?? string.Empty,
                Source = source ?? "unknown",
                WinnerSide = ResolveBattleResultWinnerSide(mission),
                PlayerSide = snapshot?.PlayerSide,
                IsSynthetic = IsSyntheticRuntime(),
                UpdatedUtc = DateTime.UtcNow,
                DroppedCombatEventCount = _droppedMaterializedBattleResultCombatEventCount
            };

            foreach (RosterEntryState entryState in runtimeState.EntriesById.Values
                .Where(entry => entry != null)
                .OrderBy(entry => entry.SideId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.PartyId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.EntryId, StringComparer.OrdinalIgnoreCase))
            {
                _materializedBattleResultEntriesByEntryId.TryGetValue(entryState.EntryId ?? string.Empty, out MaterializedBattleResultEntryRuntimeState runtimeEntry);

                result.Entries.Add(new CoopBattleResultBridgeFile.BattleResultEntrySnapshot
                {
                    EntryId = entryState.EntryId,
                    SideId = entryState.SideId,
                    PartyId = entryState.PartyId,
                    CharacterId = entryState.CharacterId,
                    OriginalCharacterId = entryState.OriginalCharacterId,
                    SpawnTemplateId = ResolveEntrySpawnTemplateId(entryState),
                    TroopName = entryState.TroopName,
                    HeroId = entryState.HeroId,
                    HeroRole = entryState.HeroRole,
                    IsHero = entryState.IsHero,
                    SnapshotCount = entryState.Count,
                    SnapshotWoundedCount = entryState.WoundedCount,
                    MaterializedSpawnCount = runtimeEntry?.MaterializedSpawnCount ?? 0,
                    ActiveCount = runtimeEntry?.ActiveCount ?? 0,
                    RemovedCount = runtimeEntry?.RemovedCount ?? 0,
                    KilledCount = runtimeEntry?.KilledCount ?? 0,
                    UnconsciousCount = runtimeEntry?.UnconsciousCount ?? 0,
                    RoutedCount = runtimeEntry?.RoutedCount ?? 0,
                    OtherRemovedCount = runtimeEntry?.OtherRemovedCount ?? 0,
                    ScoreHitCount = runtimeEntry?.ScoreHitCount ?? 0,
                    HitsTakenCount = runtimeEntry?.HitsTakenCount ?? 0,
                    FatalHitCount = runtimeEntry?.FatalHitCount ?? 0,
                    KillsInflictedCount = runtimeEntry?.KillsInflictedCount ?? 0,
                    UnconsciousInflictedCount = runtimeEntry?.UnconsciousInflictedCount ?? 0,
                    RoutedInflictedCount = runtimeEntry?.RoutedInflictedCount ?? 0,
                    DamageDealt = runtimeEntry?.DamageDealt ?? 0f,
                    DamageTaken = runtimeEntry?.DamageTaken ?? 0f
                });
            }

            if (_materializedBattleResultCombatEvents.Count > 0)
                result.CombatEvents.AddRange(_materializedBattleResultCombatEvents);

            return result;
        }

        private static string ResolveBattleResultWinnerSide(Mission mission)
        {
            if (!string.IsNullOrWhiteSpace(_authoritativeBattleWinnerSide))
                return _authoritativeBattleWinnerSide;

            if (mission == null)
                return string.Empty;

            ResolveActiveBattleSideCounts(mission, out int attackerActive, out int defenderActive, out _);
            if (attackerActive > 0 && defenderActive <= 0)
                return BattleSideEnum.Attacker.ToString();
            if (defenderActive > 0 && attackerActive <= 0)
                return BattleSideEnum.Defender.ToString();
            if (attackerActive > defenderActive)
                return BattleSideEnum.Attacker.ToString();
            if (defenderActive > attackerActive)
                return BattleSideEnum.Defender.ToString();
            return string.Empty;
        }

        private static void ResolveActiveBattleSideCounts(Mission mission, out int attackerActive, out int defenderActive, out string countSource)
        {
            int missionTeamAttackerActive = CountActiveTeamAgents(mission, BattleSideEnum.Attacker);
            int missionTeamDefenderActive = CountActiveTeamAgents(mission, BattleSideEnum.Defender);

            if (TryResolveLiveMaterializedActiveBattleSideCounts(
                mission,
                out int materializedLiveAttackerActive,
                out int materializedLiveDefenderActive,
                out int trackedAttackerAgents,
                out int trackedDefenderAgents))
            {
                attackerActive = materializedLiveAttackerActive;
                defenderActive = materializedLiveDefenderActive;
                countSource =
                    "materialized-live-agents" +
                    " AttackerTracked=" + trackedAttackerAgents +
                    " DefenderTracked=" + trackedDefenderAgents +
                    " | mission-teams Attacker=" + missionTeamAttackerActive +
                    " Defender=" + missionTeamDefenderActive;

                if (TryResolveMaterializedActiveBattleSideCounts(out int materializedRuntimeAttackerActive, out int materializedRuntimeDefenderActive))
                {
                    countSource +=
                        " | materialized-runtime Attacker=" + materializedRuntimeAttackerActive +
                        " Defender=" + materializedRuntimeDefenderActive;
                }

                return;
            }

            attackerActive = missionTeamAttackerActive;
            defenderActive = missionTeamDefenderActive;

            if (TryResolveMaterializedActiveBattleSideCounts(out int materializedAttackerActive, out int materializedDefenderActive))
            {
                countSource =
                    "mission-teams" +
                    " | materialized-runtime Attacker=" + materializedAttackerActive +
                    " Defender=" + materializedDefenderActive;
                return;
            }

            countSource = "mission-teams";
        }

        private static void TryLogBattleCompletionAudit(int attackerActive, int defenderActive, string countSource, string source)
        {
            if (attackerActive > 2 && defenderActive > 2)
                return;

            string auditKey =
                attackerActive + "|" +
                defenderActive + "|" +
                (countSource ?? string.Empty);
            if (string.Equals(_lastLoggedBattleCompletionAuditKey, auditKey, StringComparison.Ordinal))
                return;

            _lastLoggedBattleCompletionAuditKey = auditKey;
            ModLogger.Info(
                "CoopMissionSpawnLogic: battle completion audit. " +
                "AttackerActive=" + attackerActive +
                " DefenderActive=" + defenderActive +
                " CountSource=" + (countSource ?? "unknown") +
                " Source=" + (source ?? "unknown") + ".");
        }

        private static bool TryResolveMaterializedActiveBattleSideCounts(out int attackerActive, out int defenderActive)
        {
            attackerActive = 0;
            defenderActive = 0;

            int attackerSpawned = 0;
            int defenderSpawned = 0;

            foreach (MaterializedBattleResultEntryRuntimeState state in _materializedBattleResultEntriesByEntryId.Values)
            {
                if (state == null || state.MaterializedSpawnCount <= 0 || string.IsNullOrWhiteSpace(state.SideId))
                    continue;

                if (string.Equals(state.SideId, BattleSideEnum.Attacker.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    attackerSpawned += state.MaterializedSpawnCount;
                    attackerActive += Math.Max(0, state.ActiveCount);
                }
                else if (string.Equals(state.SideId, BattleSideEnum.Defender.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    defenderSpawned += state.MaterializedSpawnCount;
                    defenderActive += Math.Max(0, state.ActiveCount);
                }
            }

            return attackerSpawned > 0 && defenderSpawned > 0;
        }

        private static bool TryResolveLiveMaterializedActiveBattleSideCounts(
            Mission mission,
            out int attackerActive,
            out int defenderActive,
            out int trackedAttackerAgents,
            out int trackedDefenderAgents)
        {
            attackerActive = 0;
            defenderActive = 0;
            trackedAttackerAgents = 0;
            trackedDefenderAgents = 0;

            if (mission?.AllAgents == null || !_hasMaterializedBattlefieldArmies || _materializedArmySideByAgentIndex.Count == 0)
                return false;

            foreach (BattleSideEnum trackedSide in _materializedArmySideByAgentIndex.Values)
            {
                if (trackedSide == BattleSideEnum.Attacker)
                    trackedAttackerAgents++;
                else if (trackedSide == BattleSideEnum.Defender)
                    trackedDefenderAgents++;
            }

            if (trackedAttackerAgents <= 0 || trackedDefenderAgents <= 0)
                return false;

            for (int i = 0; i < mission.AllAgents.Count; i++)
            {
                Agent agent = mission.AllAgents[i];
                if (agent == null || !agent.IsActive() || agent.IsMount)
                    continue;

                if (!_materializedArmySideByAgentIndex.TryGetValue(agent.Index, out BattleSideEnum trackedSide))
                    continue;

                if (trackedSide == BattleSideEnum.Attacker)
                    attackerActive++;
                else if (trackedSide == BattleSideEnum.Defender)
                    defenderActive++;
            }

            return true;
        }

        private static int CountActiveTeamAgents(Mission mission, BattleSideEnum side)
        {
            if (mission?.Teams == null)
                return 0;

            int count = 0;
            foreach (Team team in mission.Teams)
            {
                if (team == null || team.Side != side || ReferenceEquals(team, mission.SpectatorTeam))
                    continue;

                foreach (Agent agent in team.ActiveAgents)
                {
                    if (agent != null && agent.IsActive() && !agent.IsMount)
                        count++;
                }
            }

            return count;
        }

        private static string BuildBattleResultEntryAuditSummary(CoopBattleResultBridgeFile.BattleResultSnapshot snapshot)
        {
            if (snapshot?.Entries == null || snapshot.Entries.Count == 0)
                return "(none)";

            IEnumerable<string> entries = snapshot.Entries
                .Where(entry => entry != null && (entry.MaterializedSpawnCount > 0 || entry.RemovedCount > 0 || entry.ActiveCount > 0))
                .Take(24)
                .Select(entry =>
                    (entry.SideId ?? "side") + "/" +
                    (entry.EntryId ?? "entry") +
                    " Spawned=" + entry.MaterializedSpawnCount +
                    " Active=" + entry.ActiveCount +
                    " Removed=" + entry.RemovedCount +
                    " Killed=" + entry.KilledCount +
                    " Unconscious=" + entry.UnconsciousCount +
                    " Routed=" + entry.RoutedCount +
                    " Hits=" + entry.ScoreHitCount +
                    " Dmg=" + entry.DamageDealt.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) +
                    " KillsBy=" + entry.KillsInflictedCount);

            string summary = string.Join("; ", entries);
            return string.IsNullOrWhiteSpace(summary) ? "(none)" : summary;
        }

        private static int CountCombatProfilePerksByPrefix(IEnumerable<string> perkIds, params string[] prefixes)
        {
            if (perkIds == null || prefixes == null || prefixes.Length == 0)
                return 0;

            int count = 0;
            foreach (string perkId in perkIds)
            {
                if (string.IsNullOrWhiteSpace(perkId))
                    continue;

                for (int i = 0; i < prefixes.Length; i++)
                {
                    string prefix = prefixes[i];
                    if (string.IsNullOrWhiteSpace(prefix))
                        continue;

                    if (!perkId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    count++;
                    break;
                }
            }

            return count;
        }

        private static void TryRefreshMaterializedCombatProfileDrivenStats(Mission mission, string source)
        {
            if (mission == null || _materializedCombatProfilesByAgentIndex.Count == 0)
                return;

            EnsureMaterializedCombatProfileMission(mission);

            float currentMissionTime = mission.CurrentTime;
            if (ReferenceEquals(_lastCombatProfileDrivenRefreshMission, mission) &&
                Math.Abs(_lastCombatProfileDrivenRefreshMissionTime - currentMissionTime) <= 0.0001f)
            {
                return;
            }

            _lastCombatProfileDrivenRefreshMission = mission;
            _lastCombatProfileDrivenRefreshMissionTime = currentMissionTime;

            bool appliedAny = false;
            if (mission.AllAgents != null)
            {
                for (int i = 0; i < mission.AllAgents.Count; i++)
                {
                    Agent agent = mission.AllAgents[i];
                    if (agent == null || !agent.IsActive())
                        continue;

                    AgentDrivenProperties agentDrivenProperties = TryGetAgentDrivenProperties(agent);
                    if (agentDrivenProperties == null)
                        continue;

                    if (TryApplyDrivenSkillCombatProfile(agent, agentDrivenProperties))
                        appliedAny = true;
                }
            }

            if (appliedAny)
            {
                if (!_hasLoggedManualCombatProfileRefreshForMission)
                {
                    _hasLoggedManualCombatProfileRefreshForMission = true;
                    ModLogger.Info(
                        "CoopMissionSpawnLogic: applied manual combat-profile driven-stat refresh. " +
                        "MissionTime=" + currentMissionTime.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                        " Source=" + (source ?? "unknown"));
                }
            }
        }

        private static AgentDrivenProperties TryGetAgentDrivenProperties(Agent agent)
        {
            if (agent == null)
                return null;

            if (!_agentDrivenPropertiesPropertyResolved)
            {
                _agentDrivenPropertiesProperty =
                    typeof(Agent).GetProperty("AgentDrivenProperties", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) ??
                    typeof(Agent).GetProperty("DrivenProperties", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                _agentDrivenPropertiesPropertyResolved = true;

                if (_agentDrivenPropertiesProperty == null && !_loggedMissingAgentDrivenPropertiesAccessor)
                {
                    _loggedMissingAgentDrivenPropertiesAccessor = true;
                    ModLogger.Info("CoopMissionSpawnLogic: AgentDrivenProperties accessor was not found; manual combat-profile refresh is unavailable.");
                }
            }

            if (_agentDrivenPropertiesProperty == null)
                return null;

            try
            {
                return _agentDrivenPropertiesProperty.GetValue(agent, null) as AgentDrivenProperties;
            }
            catch
            {
                return null;
            }
        }

        public static bool TryApplyDrivenSkillCombatProfile(Agent agent, AgentDrivenProperties agentDrivenProperties)
        {
            if (agent == null || agentDrivenProperties == null)
                return false;

            Mission mission = agent.Mission;
            if (mission == null)
                return false;

            EnsureMaterializedCombatProfileMission(mission);

            if (agent.IsMount)
                return TryApplyMountDrivenSkillCombatProfile(agent, agentDrivenProperties);

            if (!agent.IsHuman || !_materializedCombatProfilesByAgentIndex.TryGetValue(agent.Index, out MaterializedCombatProfileRuntimeState profile))
                return false;

            bool applied = false;

            var equipment = agent.Equipment;
            WeaponComponentData primaryWeapon;
            SkillObject relevantSkill;
            ItemObject primaryItem = TryResolveAgentPrimaryCombatItem(agent, equipment, out primaryWeapon, out relevantSkill);
            if (primaryItem == null)
                return false;

            relevantSkill = relevantSkill ?? primaryItem?.RelevantSkill ?? DefaultSkills.Athletics;
            int templateRelevantSkill = TryGetCharacterSkillValue(agent.Character, relevantSkill);
            int desiredRelevantSkill = TryGetCombatProfileSkillValue(profile, relevantSkill, templateRelevantSkill);

            bool suppressApproximateHeroRangedBallistics = ShouldSuppressApproximateHeroRangedBallistics(profile, agent, primaryWeapon);

            SetDrivenPropertyBaselineContext(profile, agent, isMountContext: false);
            try
            {
                if (TryApplyPrimaryWeaponSkillDrivenStats(agentDrivenProperties, primaryWeapon, templateRelevantSkill, desiredRelevantSkill))
                {
                    applied = true;
                    CountMaterializedCombatProfileApply(profile, "weapon-skill", ref profile.CountedWeaponSkillAdjustment);
                }

                if ((suppressApproximateHeroRangedBallistics &&
                        TryApplyExactHeroPrimaryRangedAccuracyDrivenStats(
                            agent,
                            profile,
                            agentDrivenProperties,
                            primaryWeapon,
                            relevantSkill,
                            templateRelevantSkill,
                            desiredRelevantSkill)) ||
                    (!suppressApproximateHeroRangedBallistics &&
                        TryApplyPrimaryWeaponInaccuracyDrivenStats(agentDrivenProperties, primaryWeapon, templateRelevantSkill, desiredRelevantSkill)))
                {
                    applied = true;
                    CountMaterializedCombatProfileApply(profile, "weapon-inaccuracy", ref profile.CountedWeaponInaccuracyAdjustment);
                }

                if (TryApplyLoadoutAttributeDrivenStats(agent, profile, agentDrivenProperties))
                    applied = true;

                if (TryApplyEnduranceDrivenStats(agent, profile, agentDrivenProperties))
                    applied = true;

                if (TryApplyPerkDrivenStats(agent, profile, agentDrivenProperties, primaryWeapon, relevantSkill))
                    applied = true;

                if (TryApplyPartyModifierDrivenStats(agent, profile, agentDrivenProperties))
                    applied = true;

                if (agent.HasMount || agent.MountAgent != null)
                {
                    int templateRiding = TryGetCharacterSkillValue(agent.Character, DefaultSkills.Riding);
                    int desiredRiding = profile.SkillRiding > 0 ? profile.SkillRiding : templateRiding;
                    if (TryApplyMountedHumanRidingDrivenStats(
                            agent,
                            agentDrivenProperties,
                            templateRiding,
                            desiredRiding,
                            includeAccuracyAdjustments: !suppressApproximateHeroRangedBallistics))
                    {
                        applied = true;
                        CountMaterializedCombatProfileApply(profile, "riding", ref profile.CountedRidingAttributeAdjustment);
                        if (!suppressApproximateHeroRangedBallistics)
                            CountMaterializedCombatProfileApply(profile, "mounted-penalty", ref profile.CountedMountedPenaltyAdjustment);
                    }
                }

                return applied;
            }
            finally
            {
                ClearDrivenPropertyBaselineContext();
            }
        }

        internal static bool TryApplyWeaponDamageMultiplierCombatProfile(
            Agent agent,
            WeaponComponentData weapon,
            ref float damageMultiplier)
        {
            if (agent == null || weapon == null || !agent.IsHuman)
                return false;

            Mission mission = agent.Mission;
            if (mission == null)
                return false;

            EnsureMaterializedCombatProfileMission(mission);

            if (!_materializedCombatProfilesByAgentIndex.TryGetValue(agent.Index, out MaterializedCombatProfileRuntimeState profile) ||
                !IsHeroProfileEligibleForExactPersonalPerks(profile))
            {
                return false;
            }

            SkillObject relevantSkill = ResolveWeaponDamageMultiplierRelevantSkill(weapon);
            if (relevantSkill == null)
                return false;

            int templateRelevantSkill = TryGetCharacterSkillValue(agent.Character, relevantSkill);
            int desiredRelevantSkill = TryGetCombatProfileSkillValue(profile, relevantSkill, templateRelevantSkill);
            float desiredSkillMultiplier = ComputeExactPersonalWeaponDamageMultiplier(relevantSkill, desiredRelevantSkill);
            if (desiredSkillMultiplier <= 0f || Math.Abs(desiredSkillMultiplier - 1f) < 0.0001f)
                return false;

            float currentMultiplier = Math.Max(0f, damageMultiplier);
            float updatedMultiplier = Math.Max(0f, currentMultiplier * desiredSkillMultiplier);
            if (Math.Abs(updatedMultiplier - currentMultiplier) < 0.0001f)
                return false;

            damageMultiplier = updatedMultiplier;
            return true;
        }

        private static SkillObject ResolveWeaponDamageMultiplierRelevantSkill(WeaponComponentData weapon)
        {
            if (weapon == null)
                return null;

            SkillObject relevantSkill = weapon.RelevantSkill;
            if (relevantSkill != null)
            {
                if (string.Equals(relevantSkill.StringId, "OneHanded", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(relevantSkill.StringId, "TwoHanded", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(relevantSkill.StringId, "Polearm", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(relevantSkill.StringId, "Bow", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(relevantSkill.StringId, "Crossbow", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(relevantSkill.StringId, "Throwing", StringComparison.OrdinalIgnoreCase))
                {
                    return relevantSkill;
                }
            }

            string skillHint = MapCombatEventWeaponClassToSkillHint(weapon.WeaponClass.ToString(), isSiegeEngineHit: false);
            switch (skillHint)
            {
                case "OneHanded":
                    return DefaultSkills.OneHanded;
                case "TwoHanded":
                    return DefaultSkills.TwoHanded;
                case "Polearm":
                    return DefaultSkills.Polearm;
                case "Bow":
                    return DefaultSkills.Bow;
                case "Crossbow":
                    return DefaultSkills.Crossbow;
                case "Throwing":
                    return DefaultSkills.Throwing;
                default:
                    return relevantSkill;
            }
        }

        private static bool TryApplyAuthoritativeExactPersonalRangedDamageBonusOnScoreHit(
            Agent affectedAgent,
            Agent affectorAgent,
            WeaponComponentData attackerWeapon,
            bool isBlocked,
            bool isSiegeEngineHit,
            in Blow blow,
            in AttackCollisionData collisionData,
            ref float damagedHp)
        {
            if (!GameNetwork.IsServer || GameNetwork.IsClientOrReplay)
                return false;

            if (affectedAgent == null || affectorAgent == null || attackerWeapon == null || !affectorAgent.IsHuman || !affectedAgent.IsActive())
                return false;

            if (CoopCampaignDerivedAgentStatCalculateModel.IsActiveForMission(affectorAgent.Mission))
                return false;

            if (isBlocked || isSiegeEngineHit || damagedHp <= 0.01f || affectedAgent.Health < 1f)
                return false;

            float desiredMultiplier = 1f;
            if (!TryApplyWeaponDamageMultiplierCombatProfile(affectorAgent, attackerWeapon, ref desiredMultiplier) ||
                desiredMultiplier <= 1.0001f)
            {
                return false;
            }

            float currentHealth = affectedAgent.Health;
            if (currentHealth <= 0f)
                return false;

            float desiredDamage = Math.Max(0f, damagedHp) * desiredMultiplier;
            int extraDamage = TaleWorlds.Library.MathF.Ceiling(desiredDamage - damagedHp);
            if (extraDamage <= 0)
                return false;

            float updatedHealth = Math.Max(0f, currentHealth - extraDamage);
            float appliedExtraDamage = currentHealth - updatedHealth;
            if (appliedExtraDamage <= 0.01f)
                return false;

            affectedAgent.Health = updatedHealth;
            if (updatedHealth < 1f && currentHealth >= 1f)
            {
                Agent.KillInfo overrideKillInfo = blow.IsFallDamage ? Agent.KillInfo.Gravity : Agent.KillInfo.Invalid;
                affectedAgent.Die(blow, overrideKillInfo);
            }

            damagedHp += appliedExtraDamage;
            ModLogger.Info(
                "CoopMissionSpawnLogic: applied authoritative exact personal ranged damage bonus. " +
                "Attacker=" + affectorAgent.Index +
                " Victim=" + affectedAgent.Index +
                " WeaponSkill=" + (attackerWeapon.RelevantSkill?.StringId ?? "null") +
                " Multiplier=" + desiredMultiplier.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " BaseDamage=" + Math.Max(0f, damagedHp - appliedExtraDamage).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) +
                " ExtraDamage=" + appliedExtraDamage.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) +
                " FinalDamage=" + damagedHp.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) +
                " TargetHealth=" + updatedHealth.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + ".");
            return true;
        }

        private static ItemObject TryResolveAgentPrimaryCombatItem(
            Agent agent,
            MissionEquipment equipment,
            out WeaponComponentData primaryWeapon,
            out SkillObject relevantSkill)
        {
            primaryWeapon = null;
            relevantSkill = null;

            if (agent == null || equipment == null)
                return null;

            EquipmentIndex primaryWieldedItemIndex = agent.GetPrimaryWieldedItemIndex();
            if (primaryWieldedItemIndex != EquipmentIndex.None)
            {
                MissionWeapon wieldedMissionWeapon = equipment[primaryWieldedItemIndex];
                ItemObject wieldedItem = wieldedMissionWeapon.Item;
                if (wieldedItem != null)
                {
                    primaryWeapon = wieldedMissionWeapon.CurrentUsageItem ?? wieldedItem.PrimaryWeapon;
                    relevantSkill = wieldedItem.RelevantSkill;
                    return wieldedItem;
                }
            }

            for (EquipmentIndex index = EquipmentIndex.Weapon0; index <= EquipmentIndex.Weapon3; index++)
            {
                MissionWeapon missionWeapon = equipment[index];
                ItemObject item = missionWeapon.Item;
                if (item == null)
                    continue;

                WeaponComponentData usageItem = missionWeapon.CurrentUsageItem ?? item.PrimaryWeapon;
                if (usageItem == null || usageItem.IsAmmo || usageItem.IsShield)
                    continue;

                primaryWeapon = usageItem;
                relevantSkill = item.RelevantSkill;
                return item;
            }

            for (EquipmentIndex index = EquipmentIndex.Weapon0; index <= EquipmentIndex.Weapon3; index++)
            {
                MissionWeapon missionWeapon = equipment[index];
                ItemObject item = missionWeapon.Item;
                if (item == null)
                    continue;

                primaryWeapon = missionWeapon.CurrentUsageItem ?? item.PrimaryWeapon;
                relevantSkill = item.RelevantSkill;
                return item;
            }

            return null;
        }

        private static bool TryApplyLoadoutAttributeDrivenStats(
            Agent agent,
            MaterializedCombatProfileRuntimeState profile,
            AgentDrivenProperties agentDrivenProperties)
        {
            if (agent == null || profile == null || agentDrivenProperties == null)
                return false;

            int templateVigor = DeriveCharacterVigorAttribute(agent.Character);
            int templateControl = DeriveCharacterControlAttribute(agent.Character);

            int desiredVigor = profile.AttributeVigor > 0 ? profile.AttributeVigor : DeriveCombatAttributeFromSkills(profile.SkillOneHanded, profile.SkillTwoHanded, profile.SkillPolearm);
            int desiredControl = profile.AttributeControl > 0 ? profile.AttributeControl : DeriveCombatAttributeFromSkills(profile.SkillBow, profile.SkillCrossbow, profile.SkillThrowing);

            bool applied = false;
            if (AgentLoadoutContainsRelevantSkill(agent, "OneHanded", "TwoHanded", "Polearm"))
            {
                if (TryApplyVigorDrivenStats(agentDrivenProperties, templateVigor, desiredVigor))
                {
                    applied = true;
                    CountMaterializedCombatProfileApply(profile, "attribute-vigor", ref profile.CountedVigorAttributeAdjustment);
                }
            }

            if (AgentLoadoutContainsRelevantSkill(agent, "Bow", "Crossbow", "Throwing") &&
                !ShouldSuppressApproximateHeroRangedBallistics(profile, agent))
            {
                if (TryApplyControlDrivenStats(agentDrivenProperties, templateControl, desiredControl))
                {
                    applied = true;
                    CountMaterializedCombatProfileApply(profile, "attribute-control", ref profile.CountedControlAttributeAdjustment);
                }
            }

            return applied;
        }

        private static bool TryApplyPerkDrivenStats(
            Agent agent,
            MaterializedCombatProfileRuntimeState profile,
            AgentDrivenProperties agentDrivenProperties,
            WeaponComponentData primaryWeapon,
            SkillObject relevantSkill)
        {
            if (agent == null || profile == null || agentDrivenProperties == null || profile.PerkIds == null || profile.PerkIds.Count == 0)
                return false;

            bool applied = false;
            bool isMounted = agent.HasMount || agent.MountAgent != null;
            int adjustedRangedPerkCount = profile.PerkRangedCount;

            if (TryApplyExactPersonalPerkDrivenStats(agent, profile, agentDrivenProperties, primaryWeapon, relevantSkill))
            {
                applied = true;
                CountMaterializedCombatProfileApply(profile, "perk-personal-exact", ref profile.CountedPerkExactPersonalAdjustment);

                if (IsHeroProfileEligibleForExactPersonalPerks(profile))
                {
                    adjustedRangedPerkCount = Math.Max(
                        0,
                        adjustedRangedPerkCount - CountExactPersonalRangedPerkCoverage(profile, relevantSkill, primaryWeapon, isMounted));
                }
            }

            if (profile.PerkMeleeCount > 0 && AgentLoadoutContainsRelevantSkill(agent, "OneHanded", "TwoHanded", "Polearm"))
            {
                if (TryApplyMeleePerkDrivenStats(agentDrivenProperties, profile.PerkMeleeCount))
                {
                    applied = true;
                    CountMaterializedCombatProfileApply(profile, "perk-melee", ref profile.CountedPerkMeleeAdjustment);
                }
            }

            if (adjustedRangedPerkCount > 0 &&
                ShouldApplyApproximateRangedPerkDrivenStats(profile) &&
                AgentLoadoutContainsRelevantSkill(agent, "Bow", "Crossbow", "Throwing"))
            {
                if (TryApplyRangedPerkDrivenStats(agentDrivenProperties, adjustedRangedPerkCount))
                {
                    applied = true;
                    CountMaterializedCombatProfileApply(profile, "perk-ranged", ref profile.CountedPerkRangedAdjustment);
                }
            }

            if (profile.PerkAthleticsCount > 0 && !agent.IsMount && !(agent.HasMount || agent.MountAgent != null))
            {
                if (TryApplyAthleticsPerkDrivenStats(agentDrivenProperties, profile.PerkAthleticsCount))
                {
                    applied = true;
                    CountMaterializedCombatProfileApply(profile, "perk-athletics", ref profile.CountedPerkAthleticsAdjustment);
                }
            }

            if (profile.PerkRidingCount > 0 && (agent.HasMount || agent.MountAgent != null))
            {
                if (TryApplyRidingPerkDrivenStats(agentDrivenProperties, profile.PerkRidingCount))
                {
                    applied = true;
                    CountMaterializedCombatProfileApply(profile, "perk-riding", ref profile.CountedPerkRidingAdjustment);
                }
            }

            return applied;
        }

        private static bool TryApplyExactPersonalPerkDrivenStats(
            Agent agent,
            MaterializedCombatProfileRuntimeState profile,
            AgentDrivenProperties agentDrivenProperties,
            WeaponComponentData primaryWeapon,
            SkillObject relevantSkill)
        {
            if (agent == null ||
                profile == null ||
                agentDrivenProperties == null ||
                profile.PerkIds == null ||
                profile.PerkIds.Count == 0 ||
                !IsHeroProfileEligibleForExactPersonalPerks(profile) ||
                !agent.IsHuman)
            {
                return false;
            }

            bool applied = false;
            bool lowLevelExactAgentStatsActive = CoopCampaignDerivedAgentStatCalculateModel.IsActiveForMission(agent.Mission);

            if (!lowLevelExactAgentStatsActive && HasPerkId(profile.PerkIds, "AthleticsFormFittingArmor"))
                applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.ArmorEncumbrance, 1f, 0.85f);

            // Keep personal ranged driven perks disabled until they are bridged
            // against the native runtime weapon-state path. The first direct port
            // caused projectile drift and ammo/runtime divergence despite correct
            // snapshot transfer.
            if (IsExactPersonalRangedDrivenPerkSubsetEnabled())
            {
                applied |= TryApplyExactPersonalRangedPerkDrivenStats(
                    agent,
                    profile,
                    agentDrivenProperties,
                    relevantSkill,
                    primaryWeapon);
            }

            return applied;
        }

        private static bool TryApplyExactPersonalRangedPerkDrivenStats(
            Agent agent,
            MaterializedCombatProfileRuntimeState profile,
            AgentDrivenProperties agentDrivenProperties,
            SkillObject relevantSkill,
            WeaponComponentData primaryWeapon)
        {
            if (agent == null || profile == null || agentDrivenProperties == null)
                return false;

            string relevantSkillId = GetPrimaryCombatRelevantSkillId(relevantSkill, primaryWeapon);
            if (!IsExactPersonalRangedRelevantSkillId(relevantSkillId))
                return false;

            bool isMounted = agent.HasMount || agent.MountAgent != null;
            bool applied = false;

            if (HasPerkId(profile.PerkIds, "BowNockingPoint"))
                applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.ReloadMovementPenaltyFactor, 1f, 0.5f);

            if (isMounted && HasPerkId(profile.PerkIds, "RidingSagittarius"))
            {
                applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.WeaponWorstMobileAccuracyPenalty, 1f, 0.85f);
                applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.WeaponWorstUnsteadyAccuracyPenalty, 1f, 0.85f);
            }

            if (string.Equals(relevantSkillId, "Bow", StringComparison.OrdinalIgnoreCase))
            {
                if (HasPerkId(profile.PerkIds, "BowBowControl"))
                    applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.WeaponWorstMobileAccuracyPenalty, 1f, 0.7f);
                if (HasPerkId(profile.PerkIds, "BowRapidFire"))
                    applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.ReloadSpeed, 1f, 1.25f);
                if (HasPerkId(profile.PerkIds, "BowQuickAdjustments"))
                    applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.WeaponRotationalAccuracyPenaltyInRadians, 1f, 0.5f);
                if (HasPerkId(profile.PerkIds, "BowDiscipline"))
                {
                    applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.WeaponUnsteadyBeginTime, 1f, 1.5f);
                    applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.WeaponUnsteadyEndTime, 1f, 1.5f);
                }
                if (HasPerkId(profile.PerkIds, "BowQuickDraw"))
                    applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.ThrustOrRangedReadySpeedMultiplier, 1f, 1.25f);
                if (isMounted && HasPerkId(profile.PerkIds, "BowMountedArchery"))
                {
                    applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.WeaponWorstMobileAccuracyPenalty, 1f, 0.7f);
                    applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.WeaponWorstUnsteadyAccuracyPenalty, 1f, 0.7f);
                }
            }
            else if (string.Equals(relevantSkillId, "Crossbow", StringComparison.OrdinalIgnoreCase))
            {
                if (isMounted && HasPerkId(profile.PerkIds, "CrossbowSteady"))
                {
                    applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.WeaponWorstMobileAccuracyPenalty, 1f, 0.5f);
                    applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.WeaponRotationalAccuracyPenaltyInRadians, 1f, 0.5f);
                }
                if (HasPerkId(profile.PerkIds, "CrossbowWindWinder"))
                    applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.ReloadSpeed, 1f, 1.25f);
                if (HasPerkId(profile.PerkIds, "CrossbowDonkeysSwiftness"))
                    applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.WeaponWorstMobileAccuracyPenalty, 1f, 0.7f);
                if (HasPerkId(profile.PerkIds, "CrossbowMarksmen"))
                    applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.ThrustOrRangedReadySpeedMultiplier, 1f, 1.25f);
            }
            else if (string.Equals(relevantSkillId, "Throwing", StringComparison.OrdinalIgnoreCase))
            {
                if (HasPerkId(profile.PerkIds, "ThrowingQuickDraw"))
                    applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.ReloadSpeed, 1f, 1.2f);
                if (HasPerkId(profile.PerkIds, "ThrowingPerfectTechnique"))
                    applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.MissileSpeedMultiplier, 1f, 1.25f);
                if (isMounted && HasPerkId(profile.PerkIds, "ThrowingMountedSkirmisher"))
                {
                    applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.WeaponWorstMobileAccuracyPenalty, 1f, 0.8f);
                    applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.WeaponWorstUnsteadyAccuracyPenalty, 1f, 0.8f);
                }
            }

            return applied;
        }

        private static int CountExactPersonalRangedPerkCoverage(
            MaterializedCombatProfileRuntimeState profile,
            SkillObject relevantSkill,
            WeaponComponentData primaryWeapon,
            bool isMounted)
        {
            if (!IsExactPersonalRangedDrivenPerkSubsetEnabled())
                return 0;

            if (profile?.PerkIds == null || profile.PerkIds.Count == 0)
                return 0;

            string relevantSkillId = GetPrimaryCombatRelevantSkillId(relevantSkill, primaryWeapon);
            if (!IsExactPersonalRangedRelevantSkillId(relevantSkillId))
                return 0;

            int count = 0;

            if (HasPerkId(profile.PerkIds, "BowNockingPoint"))
                count++;

            if (string.Equals(relevantSkillId, "Bow", StringComparison.OrdinalIgnoreCase))
            {
                if (HasPerkId(profile.PerkIds, "BowBowControl"))
                    count++;
                if (HasPerkId(profile.PerkIds, "BowRapidFire"))
                    count++;
                if (HasPerkId(profile.PerkIds, "BowQuickAdjustments"))
                    count++;
                if (HasPerkId(profile.PerkIds, "BowDiscipline"))
                    count++;
                if (HasPerkId(profile.PerkIds, "BowQuickDraw"))
                    count++;
                if (isMounted && HasPerkId(profile.PerkIds, "BowMountedArchery"))
                    count++;
            }
            else if (string.Equals(relevantSkillId, "Crossbow", StringComparison.OrdinalIgnoreCase))
            {
                if (isMounted && HasPerkId(profile.PerkIds, "CrossbowSteady"))
                    count++;
                if (HasPerkId(profile.PerkIds, "CrossbowWindWinder"))
                    count++;
                if (HasPerkId(profile.PerkIds, "CrossbowDonkeysSwiftness"))
                    count++;
                if (HasPerkId(profile.PerkIds, "CrossbowMarksmen"))
                    count++;
            }
            else if (string.Equals(relevantSkillId, "Throwing", StringComparison.OrdinalIgnoreCase))
            {
                if (HasPerkId(profile.PerkIds, "ThrowingQuickDraw"))
                    count++;
                if (HasPerkId(profile.PerkIds, "ThrowingPerfectTechnique"))
                    count++;
                if (isMounted && HasPerkId(profile.PerkIds, "ThrowingMountedSkirmisher"))
                    count++;
            }

            return count;
        }

        private static bool IsExactPersonalRangedDrivenPerkSubsetEnabled()
        {
            return false;
        }

        private static bool ShouldApplyApproximateRangedPerkDrivenStats(MaterializedCombatProfileRuntimeState profile)
        {
            // The old count-based ranged approximation mutates generic inaccuracy and
            // missile properties without the native skill/weapon conditions. For hero
            // entries this produced visibly wrong bow ballistics. Keep it only for the
            // non-hero fallback path until ranged perks are ported exact from native.
            return !IsHeroProfileEligibleForExactPersonalPerks(profile);
        }

        private static bool ShouldSuppressApproximateHeroRangedBallistics(
            MaterializedCombatProfileRuntimeState profile,
            Agent agent,
            WeaponComponentData primaryWeapon = null)
        {
            if (!IsHeroProfileEligibleForExactPersonalPerks(profile) || agent == null)
                return false;

            SkillObject relevantSkill = primaryWeapon?.RelevantSkill;
            if (relevantSkill == DefaultSkills.Bow || relevantSkill == DefaultSkills.Crossbow || relevantSkill == DefaultSkills.Throwing)
                return true;

            return AgentLoadoutContainsRelevantSkill(agent, "Bow", "Crossbow", "Throwing");
        }

        private static string GetPrimaryCombatRelevantSkillId(SkillObject relevantSkill, WeaponComponentData primaryWeapon)
        {
            return relevantSkill?.StringId
                ?? primaryWeapon?.RelevantSkill?.StringId
                ?? string.Empty;
        }

        private static bool IsExactPersonalRangedRelevantSkillId(string relevantSkillId)
        {
            return string.Equals(relevantSkillId, "Bow", StringComparison.OrdinalIgnoreCase)
                || string.Equals(relevantSkillId, "Crossbow", StringComparison.OrdinalIgnoreCase)
                || string.Equals(relevantSkillId, "Throwing", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryApplyPartyModifierDrivenStats(
            Agent agent,
            MaterializedCombatProfileRuntimeState profile,
            AgentDrivenProperties agentDrivenProperties)
        {
            if (agent == null || profile == null || agentDrivenProperties == null)
                return false;

            bool applied = false;

            if (TryApplyPartyMoraleDrivenStats(agentDrivenProperties, profile))
            {
                applied = true;
                CountMaterializedCombatProfileApply(profile, "party-morale", ref profile.CountedPartyMoraleAdjustment);
            }

            if (TryApplyPartyTacticsDrivenStats(agent, agentDrivenProperties, profile))
            {
                applied = true;
                CountMaterializedCombatProfileApply(profile, "party-tactics", ref profile.CountedPartyTacticsAdjustment);
            }

            if (TryApplyPartyCaptainDrivenStats(agent, agentDrivenProperties, profile))
            {
                applied = true;
                CountMaterializedCombatProfileApply(profile, "party-captain", ref profile.CountedPartyCaptainAdjustment);
            }

            if (AgentLoadoutContainsRelevantSkill(agent, "Bow", "Crossbow", "Throwing"))
            {
                if (TryApplyPartyScoutDrivenStats(agentDrivenProperties, profile))
                {
                    applied = true;
                    CountMaterializedCombatProfileApply(profile, "party-scout", ref profile.CountedPartyScoutAdjustment);
                }
            }

            if (!agent.IsMount)
            {
                if (TryApplyPartyQuartermasterDrivenStats(agentDrivenProperties, profile))
                {
                    applied = true;
                    CountMaterializedCombatProfileApply(profile, "party-quartermaster", ref profile.CountedPartyQuartermasterAdjustment);
                }

                if (TryApplyPartyEngineerDrivenStats(agent, agentDrivenProperties, profile))
                {
                    applied = true;
                    CountMaterializedCombatProfileApply(profile, "party-engineer", ref profile.CountedPartyEngineerAdjustment);
                }

                if (TryApplyPartySurgeonDrivenStats(agentDrivenProperties, profile))
                {
                    applied = true;
                    CountMaterializedCombatProfileApply(profile, "party-surgeon", ref profile.CountedPartySurgeonAdjustment);
                }
            }

            return applied;
        }

        private static bool AgentLoadoutContainsRelevantSkill(Agent agent, params string[] relevantSkillIds)
        {
            if (agent == null || relevantSkillIds == null || relevantSkillIds.Length == 0)
                return false;

            MissionEquipment equipment = agent.Equipment;
            if (equipment == null)
                return false;

            for (EquipmentIndex index = EquipmentIndex.Weapon0; index <= EquipmentIndex.Weapon3; index++)
            {
                MissionWeapon missionWeapon = equipment[index];
                ItemObject item = missionWeapon.Item;
                if (item == null)
                    continue;

                WeaponComponentData usageItem = missionWeapon.CurrentUsageItem ?? item.PrimaryWeapon;
                if (MatchesRelevantSkillIdsByWeaponUsage(item, usageItem, relevantSkillIds))
                    return true;

                string skillId = item.RelevantSkill?.StringId;
                if (string.IsNullOrWhiteSpace(skillId))
                    continue;

                for (int i = 0; i < relevantSkillIds.Length; i++)
                {
                    if (string.Equals(skillId, relevantSkillIds[i], StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        private static bool MatchesRelevantSkillIdsByWeaponUsage(ItemObject item, WeaponComponentData usageItem, params string[] relevantSkillIds)
        {
            if (relevantSkillIds == null || relevantSkillIds.Length == 0)
                return false;

            bool wantsMelee = ContainsRelevantSkillId(relevantSkillIds, "OneHanded")
                || ContainsRelevantSkillId(relevantSkillIds, "TwoHanded")
                || ContainsRelevantSkillId(relevantSkillIds, "Polearm");
            bool wantsRanged = ContainsRelevantSkillId(relevantSkillIds, "Bow")
                || ContainsRelevantSkillId(relevantSkillIds, "Crossbow")
                || ContainsRelevantSkillId(relevantSkillIds, "Throwing");

            if (usageItem != null)
            {
                if (wantsMelee && (usageItem.IsOneHanded || usageItem.IsTwoHanded || usageItem.IsPolearm || usageItem.IsMeleeWeapon))
                    return true;

                if (wantsRanged && (usageItem.IsRangedWeapon
                    || usageItem.WeaponClass == WeaponClass.Javelin
                    || usageItem.WeaponClass == WeaponClass.ThrowingAxe
                    || usageItem.WeaponClass == WeaponClass.ThrowingKnife
                    || usageItem.WeaponClass == WeaponClass.Stone
                    || usageItem.WeaponClass == WeaponClass.SlingStone))
                    return true;
            }

            if (item == null)
                return false;

            switch (item.ItemType)
            {
                case ItemObject.ItemTypeEnum.OneHandedWeapon:
                case ItemObject.ItemTypeEnum.TwoHandedWeapon:
                case ItemObject.ItemTypeEnum.Polearm:
                    return wantsMelee;
                case ItemObject.ItemTypeEnum.Bow:
                case ItemObject.ItemTypeEnum.Crossbow:
                case ItemObject.ItemTypeEnum.Sling:
                case ItemObject.ItemTypeEnum.Thrown:
                    return wantsRanged;
                default:
                    return false;
            }
        }

        private static bool ContainsRelevantSkillId(string[] relevantSkillIds, string skillId)
        {
            if (relevantSkillIds == null || string.IsNullOrWhiteSpace(skillId))
                return false;

            for (int i = 0; i < relevantSkillIds.Length; i++)
            {
                if (string.Equals(relevantSkillIds[i], skillId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool TryApplyEnduranceDrivenStats(
            Agent agent,
            MaterializedCombatProfileRuntimeState profile,
            AgentDrivenProperties agentDrivenProperties)
        {
            if (agent == null || profile == null || agentDrivenProperties == null || agent.IsMount)
                return false;

            int templateEndurance = DeriveCharacterEnduranceAttribute(agent.Character);
            int desiredEndurance = profile.AttributeEndurance > 0 ? profile.AttributeEndurance : DeriveCombatAttributeFromSkills(profile.SkillRiding, profile.SkillAthletics);
            if (!TryApplyEnduranceDrivenProperties(agentDrivenProperties, templateEndurance, desiredEndurance))
                return false;

            CountMaterializedCombatProfileApply(profile, "attribute-endurance", ref profile.CountedEnduranceAttributeAdjustment);
            return true;
        }

        private static bool TryApplyMountDrivenSkillCombatProfile(Agent mountAgent, AgentDrivenProperties agentDrivenProperties)
        {
            Agent riderAgent = mountAgent?.RiderAgent;
            if (mountAgent == null || riderAgent == null || !_materializedCombatProfilesByAgentIndex.TryGetValue(riderAgent.Index, out MaterializedCombatProfileRuntimeState profile))
                return false;

            int templateRiding = TryGetCharacterSkillValue(riderAgent.Character, DefaultSkills.Riding);
            int desiredRiding = profile.SkillRiding > 0 ? profile.SkillRiding : templateRiding;
            if (templateRiding <= 0 || desiredRiding <= 0)
                return false;

            bool applied = false;
            float mountSpeedBaseFactor = 1f + templateRiding * 0.0032f;
            float mountSpeedDesiredFactor = 1f + desiredRiding * 0.0032f;
            float mountManeuverBaseFactor = 1f + templateRiding * 0.0035f;
            float mountManeuverDesiredFactor = 1f + desiredRiding * 0.0035f;

            SetDrivenPropertyBaselineContext(profile, mountAgent, isMountContext: true);
            try
            {
                applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.MountSpeed, mountSpeedBaseFactor, mountSpeedDesiredFactor);
                applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.MountManeuver, mountManeuverBaseFactor, mountManeuverDesiredFactor);
                if (applied)
                    CountMaterializedCombatProfileApply(profile, "mount", ref profile.CountedMountStatAdjustment);

                if (TryApplyMountPerkDrivenStats(profile, agentDrivenProperties))
                {
                    applied = true;
                    CountMaterializedCombatProfileApply(profile, "perk-mount", ref profile.CountedPerkMountAdjustment);
                }

                return applied;
            }
            finally
            {
                ClearDrivenPropertyBaselineContext();
            }
        }

        private static bool TryApplyMountPerkDrivenStats(
            MaterializedCombatProfileRuntimeState profile,
            AgentDrivenProperties agentDrivenProperties)
        {
            if (profile == null || agentDrivenProperties == null || profile.PerkRidingCount <= 0)
                return false;

            float factor = ComputePerkPositiveFactor(profile.PerkRidingCount, 0.005f, 0.08f);
            bool applied = false;
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.MountSpeed, 1f, factor);
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.MountManeuver, 1f, factor);
            return applied;
        }

        private static bool TryApplyPrimaryWeaponSkillDrivenStats(
            AgentDrivenProperties agentDrivenProperties,
            WeaponComponentData primaryWeapon,
            int templateRelevantSkill,
            int desiredRelevantSkill)
        {
            if (desiredRelevantSkill <= 0)
                return false;

            float baseFactor = 0.93f + 0.0007f * templateRelevantSkill;
            float desiredFactor = 0.93f + 0.0007f * desiredRelevantSkill;
            bool applied = false;
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.SwingSpeedMultiplier, baseFactor, desiredFactor);
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.ThrustOrRangedReadySpeedMultiplier, baseFactor, desiredFactor);
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.ReloadSpeed, baseFactor, desiredFactor);
            return applied;
        }

        private static bool TryApplyMeleePerkDrivenStats(
            AgentDrivenProperties agentDrivenProperties,
            int perkCount)
        {
            if (agentDrivenProperties == null || perkCount <= 0)
                return false;

            float factor = ComputePerkPositiveFactor(perkCount, 0.004f, 0.08f);
            bool applied = false;
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.SwingSpeedMultiplier, 1f, factor);
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.ThrustOrRangedReadySpeedMultiplier, 1f, factor);
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.HandlingMultiplier, 1f, factor);
            return applied;
        }

        private static bool TryApplyVigorDrivenStats(
            AgentDrivenProperties agentDrivenProperties,
            int templateVigor,
            int desiredVigor)
        {
            if (agentDrivenProperties == null || desiredVigor <= 0)
                return false;

            float baseFactor = ComputeAttributeOffenseFactor(templateVigor);
            float desiredFactor = ComputeAttributeOffenseFactor(desiredVigor);
            bool applied = false;
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.HandlingMultiplier, baseFactor, desiredFactor);
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.MeleeWeaponDamageMultiplierBonus, baseFactor, desiredFactor);
            return applied;
        }

        private static bool TryApplyControlDrivenStats(
            AgentDrivenProperties agentDrivenProperties,
            int templateControl,
            int desiredControl)
        {
            if (agentDrivenProperties == null || desiredControl <= 0)
                return false;

            bool applied = false;
            applied |= TryScaleDrivenProperty(
                agentDrivenProperties,
                DrivenProperty.MissileSpeedMultiplier,
                ComputeAttributeOffenseFactor(templateControl),
                ComputeAttributeOffenseFactor(desiredControl));
            applied |= TryScaleDrivenProperty(
                agentDrivenProperties,
                DrivenProperty.WeaponInaccuracy,
                ComputeAccuracyPenaltyFactor(templateControl),
                ComputeAccuracyPenaltyFactor(desiredControl));
            applied |= TryScaleDrivenProperty(
                agentDrivenProperties,
                DrivenProperty.WeaponBestAccuracyWaitTime,
                ComputeAccuracyPenaltyFactor(templateControl),
                ComputeAccuracyPenaltyFactor(desiredControl));
            return applied;
        }

        private static bool TryApplyRangedPerkDrivenStats(
            AgentDrivenProperties agentDrivenProperties,
            int perkCount)
        {
            if (agentDrivenProperties == null || perkCount <= 0)
                return false;

            float speedFactor = ComputePerkPositiveFactor(perkCount, 0.0045f, 0.08f);
            float accuracyFactor = ComputePerkPenaltyReductionFactor(perkCount, 0.0075f, 0.15f, 0.8f);
            bool applied = false;
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.ThrustOrRangedReadySpeedMultiplier, 1f, speedFactor);
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.ReloadSpeed, 1f, speedFactor);
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.MissileSpeedMultiplier, 1f, speedFactor);
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.WeaponInaccuracy, 1f, accuracyFactor);
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.WeaponBestAccuracyWaitTime, 1f, accuracyFactor);
            return applied;
        }

        private static bool TryApplyEnduranceDrivenProperties(
            AgentDrivenProperties agentDrivenProperties,
            int templateEndurance,
            int desiredEndurance)
        {
            if (agentDrivenProperties == null || desiredEndurance <= 0)
                return false;

            bool applied = false;
            applied |= TryScaleDrivenProperty(
                agentDrivenProperties,
                DrivenProperty.CombatMaxSpeedMultiplier,
                ComputeAttributeOffenseFactor(templateEndurance),
                ComputeAttributeOffenseFactor(desiredEndurance));
            applied |= TryScaleDrivenProperty(
                agentDrivenProperties,
                DrivenProperty.TopSpeedReachDuration,
                ComputeSpeedReachDurationFactor(templateEndurance),
                ComputeSpeedReachDurationFactor(desiredEndurance));
            return applied;
        }

        private static bool TryApplyAthleticsPerkDrivenStats(
            AgentDrivenProperties agentDrivenProperties,
            int perkCount)
        {
            if (agentDrivenProperties == null || perkCount <= 0)
                return false;

            float speedFactor = ComputePerkPositiveFactor(perkCount, 0.0035f, 0.05f);
            float reachFactor = ComputePerkPenaltyReductionFactor(perkCount, 0.004f, 0.07f, 0.85f);
            bool applied = false;
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.CombatMaxSpeedMultiplier, 1f, speedFactor);
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.TopSpeedReachDuration, 1f, reachFactor);
            return applied;
        }

        private static bool TryApplyPartyMoraleDrivenStats(
            AgentDrivenProperties agentDrivenProperties,
            MaterializedCombatProfileRuntimeState profile)
        {
            if (agentDrivenProperties == null || profile == null)
                return false;

            float morale = profile.PartyMorale > 0.01f ? profile.PartyMorale : profile.SideMorale;
            int commandPerkCount = profile.PartyLeaderPerkCount + profile.ArmyCommanderPerkCount;
            if (morale <= 50.01f && profile.LeaderLeadershipSkill <= 0 && commandPerkCount <= 0)
                return false;

            float moraleBonus = Math.Max(0f, morale - 50f) * 0.0012f;
            float leadershipBonus = Math.Min(0.08f, Math.Max(0, profile.LeaderLeadershipSkill) * 0.00025f);
            float perkBonus = Math.Min(0.06f, commandPerkCount * 0.002f);
            float desiredFactor = 1f + moraleBonus + leadershipBonus + perkBonus;

            bool applied = false;
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.AttributeCourage, 1f, desiredFactor);
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.AIAttackOnDecideChance, 1f, Math.Min(1.08f, desiredFactor));
            return applied;
        }

        private static bool TryApplyPartyTacticsDrivenStats(
            Agent agent,
            AgentDrivenProperties agentDrivenProperties,
            MaterializedCombatProfileRuntimeState profile)
        {
            if (agent == null || agentDrivenProperties == null || profile == null)
                return false;

            int commandPerkCount = profile.ArmyCommanderPerkCount;
            if (profile.LeaderTacticsSkill <= 0 && commandPerkCount <= 0)
                return false;

            float decisionFactor = ComputePartySkillPositiveFactor(profile.LeaderTacticsSkill, 0.00016f, 0.05f);
            decisionFactor *= ComputePerkPositiveFactor(commandPerkCount, 0.0025f, 0.04f);
            decisionFactor = Math.Min(1.09f, decisionFactor);

            bool applied = false;
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.AIAttackOnDecideChance, 1f, decisionFactor);

            if (AgentLoadoutContainsRelevantSkill(agent, "Bow", "Crossbow", "Throwing") &&
                !ShouldSuppressApproximateHeroRangedBallistics(profile, agent))
            {
                float rangedFactor = ComputePartySkillPositiveFactor(profile.LeaderTacticsSkill, 0.00012f, 0.035f);
                rangedFactor *= ComputePerkPositiveFactor(commandPerkCount, 0.002f, 0.03f);
                rangedFactor = Math.Min(1.07f, rangedFactor);
                applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.MissileSpeedMultiplier, 1f, rangedFactor);
            }
            else if (AgentLoadoutContainsRelevantSkill(agent, "OneHanded", "TwoHanded", "Polearm"))
            {
                float handlingFactor = ComputePartySkillPositiveFactor(profile.LeaderTacticsSkill, 0.0001f, 0.03f);
                handlingFactor *= ComputePerkPositiveFactor(commandPerkCount, 0.0018f, 0.025f);
                handlingFactor = Math.Min(1.06f, handlingFactor);
                applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.HandlingMultiplier, 1f, handlingFactor);
            }

            return applied;
        }

        private static bool TryApplyPartyScoutDrivenStats(
            AgentDrivenProperties agentDrivenProperties,
            MaterializedCombatProfileRuntimeState profile)
        {
            if (agentDrivenProperties == null || profile == null)
                return false;

            Agent baselineAgent = _drivenPropertyBaselineAgentContext;
            if (ShouldSuppressApproximateHeroRangedBallistics(profile, baselineAgent))
                return false;

            int scoutPerkCount = profile.ScoutRolePerkCount;
            if (profile.ScoutScoutingSkill <= 0 && scoutPerkCount <= 0)
                return false;

            float skillAccuracyFactor = ComputePartySkillPenaltyReductionFactor(profile.ScoutScoutingSkill, 0.00022f, 0.08f, 0.9f);
            float perkAccuracyFactor = ComputePerkPenaltyReductionFactor(scoutPerkCount, 0.006f, 0.06f, 0.9f);
            float desiredAccuracyFactor = Math.Max(0.82f, skillAccuracyFactor * perkAccuracyFactor);

            bool applied = false;
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.WeaponInaccuracy, 1f, desiredAccuracyFactor);
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.WeaponBestAccuracyWaitTime, 1f, desiredAccuracyFactor);
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.AiShooterError, 1f, desiredAccuracyFactor);
            return applied;
        }

        private static bool TryApplyPartyCaptainDrivenStats(
            Agent agent,
            AgentDrivenProperties agentDrivenProperties,
            MaterializedCombatProfileRuntimeState profile)
        {
            if (agent == null || agentDrivenProperties == null || profile == null || profile.CaptainPerkCount <= 0)
                return false;

            bool applied = false;

            if (AgentLoadoutContainsRelevantSkill(agent, "OneHanded", "TwoHanded", "Polearm"))
            {
                float damageFactor = ComputePerkPositiveFactor(profile.CaptainPerkCount, 0.0035f, 0.05f);
                float handlingFactor = ComputePerkPositiveFactor(profile.CaptainPerkCount, 0.0025f, 0.04f);
                applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.MeleeWeaponDamageMultiplierBonus, 1f, damageFactor);
                applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.HandlingMultiplier, 1f, handlingFactor);
            }

            if (AgentLoadoutContainsRelevantSkill(agent, "Bow", "Crossbow", "Throwing") &&
                !ShouldSuppressApproximateHeroRangedBallistics(profile, agent))
            {
                float speedFactor = ComputePerkPositiveFactor(profile.CaptainPerkCount, 0.003f, 0.045f);
                float accuracyFactor = ComputePerkPenaltyReductionFactor(profile.CaptainPerkCount, 0.005f, 0.08f, 0.88f);
                applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.MissileSpeedMultiplier, 1f, speedFactor);
                applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.WeaponInaccuracy, 1f, accuracyFactor);
            }

            return applied;
        }

        private static bool TryApplyPartyQuartermasterDrivenStats(
            AgentDrivenProperties agentDrivenProperties,
            MaterializedCombatProfileRuntimeState profile)
        {
            if (agentDrivenProperties == null || profile == null)
                return false;

            int quartermasterPerkCount = profile.QuartermasterRolePerkCount;
            if (profile.QuartermasterStewardSkill <= 0 && quartermasterPerkCount <= 0)
                return false;

            float encumbranceFactor = ComputePartySkillPenaltyReductionFactor(profile.QuartermasterStewardSkill, 0.00018f, 0.07f, 0.9f);
            encumbranceFactor *= ComputePerkPenaltyReductionFactor(quartermasterPerkCount, 0.005f, 0.05f, 0.9f);
            encumbranceFactor = Math.Max(0.82f, encumbranceFactor);

            float speedFactor = ComputePartySkillPositiveFactor(profile.QuartermasterStewardSkill, 0.00012f, 0.04f);
            speedFactor *= ComputePerkPositiveFactor(quartermasterPerkCount, 0.003f, 0.04f);
            speedFactor = Math.Min(1.08f, speedFactor);

            bool applied = false;
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.ArmorEncumbrance, 1f, encumbranceFactor);
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.WeaponsEncumbrance, 1f, encumbranceFactor);
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.CombatMaxSpeedMultiplier, 1f, speedFactor);
            return applied;
        }

        private static bool TryApplyPartyEngineerDrivenStats(
            Agent agent,
            AgentDrivenProperties agentDrivenProperties,
            MaterializedCombatProfileRuntimeState profile)
        {
            if (agent == null || agentDrivenProperties == null || profile == null)
                return false;

            int engineerPerkCount = profile.EngineerRolePerkCount;
            if ((profile.EngineerEngineeringSkill <= 0 && engineerPerkCount <= 0) ||
                !AgentLoadoutContainsRelevantSkill(agent, "Bow", "Crossbow", "Throwing"))
            {
                return false;
            }

            float reloadFactor = ComputePartySkillPositiveFactor(profile.EngineerEngineeringSkill, 0.00018f, 0.05f);
            reloadFactor *= ComputePerkPositiveFactor(engineerPerkCount, 0.0035f, 0.05f);
            reloadFactor = Math.Min(1.1f, reloadFactor);

            float readinessFactor = ComputePartySkillPositiveFactor(profile.EngineerEngineeringSkill, 0.00012f, 0.035f);
            readinessFactor *= ComputePerkPositiveFactor(engineerPerkCount, 0.0025f, 0.03f);
            readinessFactor = Math.Min(1.07f, readinessFactor);

            bool applied = false;
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.ReloadSpeed, 1f, reloadFactor);
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.ThrustOrRangedReadySpeedMultiplier, 1f, readinessFactor);
            return applied;
        }

        private static bool TryApplyPartySurgeonDrivenStats(
            AgentDrivenProperties agentDrivenProperties,
            MaterializedCombatProfileRuntimeState profile)
        {
            if (agentDrivenProperties == null || profile == null)
                return false;

            int surgeonPerkCount = profile.SurgeonRolePerkCount;
            if (profile.SurgeonMedicineSkill <= 0 && surgeonPerkCount <= 0)
                return false;

            float courageFactor = ComputePartySkillPositiveFactor(profile.SurgeonMedicineSkill, 0.0001f, 0.025f);
            courageFactor *= ComputePerkPositiveFactor(surgeonPerkCount, 0.002f, 0.03f);
            courageFactor = Math.Min(1.06f, courageFactor);

            return TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.AttributeCourage, 1f, courageFactor);
        }

        private static bool TryApplyPrimaryWeaponInaccuracyDrivenStats(
            AgentDrivenProperties agentDrivenProperties,
            WeaponComponentData primaryWeapon,
            int templateRelevantSkill,
            int desiredRelevantSkill)
        {
            if (primaryWeapon == null || desiredRelevantSkill <= 0)
                return false;

            float baseInaccuracy = ComputeWeaponInaccuracy(primaryWeapon, templateRelevantSkill);
            float desiredInaccuracy = ComputeWeaponInaccuracy(primaryWeapon, desiredRelevantSkill);
            return TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.WeaponInaccuracy, baseInaccuracy, desiredInaccuracy);
        }

        private static bool TryApplyExactHeroPrimaryRangedAccuracyDrivenStats(
            Agent agent,
            MaterializedCombatProfileRuntimeState profile,
            AgentDrivenProperties agentDrivenProperties,
            WeaponComponentData primaryWeapon,
            SkillObject relevantSkill,
            int templateRelevantSkill,
            int desiredRelevantSkill)
        {
            if (agent == null ||
                profile == null ||
                agentDrivenProperties == null ||
                primaryWeapon == null ||
                !IsHeroProfileEligibleForExactPersonalPerks(profile))
            {
                return false;
            }

            string relevantSkillId = GetPrimaryCombatRelevantSkillId(relevantSkill, primaryWeapon);
            if (!IsExactPersonalRangedRelevantSkillId(relevantSkillId))
                return false;

            bool applied = false;
            float baseInaccuracy = ComputeWeaponInaccuracy(primaryWeapon, templateRelevantSkill);
            float desiredInaccuracy = ComputeWeaponInaccuracy(primaryWeapon, desiredRelevantSkill);

            if (agent.HasMount || agent.MountAgent != null)
            {
                int templateRiding = TryGetCharacterSkillValue(agent.Character, DefaultSkills.Riding);
                int desiredRiding = profile.SkillRiding > 0 ? profile.SkillRiding : templateRiding;

                baseInaccuracy *= ComputeMountedWeaponInaccuracyFactor(templateRiding);
                desiredInaccuracy *= ComputeSandboxMountedWeaponInaccuracyFactor(desiredRiding);
            }

            applied |= TryScaleDrivenProperty(
                agentDrivenProperties,
                DrivenProperty.WeaponInaccuracy,
                Math.Max(0.0001f, baseInaccuracy),
                Math.Max(0.0001f, desiredInaccuracy));

            return applied;
        }

        private static bool TryApplyMountedHumanRidingDrivenStats(
            Agent agent,
            AgentDrivenProperties agentDrivenProperties,
            int templateRiding,
            int desiredRiding,
            bool includeAccuracyAdjustments = true)
        {
            if (desiredRiding <= 0)
                return false;

            bool applied = false;

            float mountRidingFactor = agent.MountAgent?.GetAgentDrivenPropertyValue(DrivenProperty.AttributeRiding) ?? 1f;
            float desiredAttributeRiding = desiredRiding * mountRidingFactor;
            applied |= TrySetDrivenProperty(agentDrivenProperties, DrivenProperty.AttributeRiding, desiredAttributeRiding, 0.01f);

            float baseMountedSpeedFactor = ComputeMountedWeaponSpeedFactor(templateRiding);
            float desiredMountedSpeedFactor = ComputeMountedWeaponSpeedFactor(desiredRiding);
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.SwingSpeedMultiplier, baseMountedSpeedFactor, desiredMountedSpeedFactor);
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.ThrustOrRangedReadySpeedMultiplier, baseMountedSpeedFactor, desiredMountedSpeedFactor);
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.ReloadSpeed, baseMountedSpeedFactor, desiredMountedSpeedFactor);

            if (includeAccuracyAdjustments)
            {
                float baseWaitFactor = ComputeMountedWeaponAccuracyWaitFactor(templateRiding);
                float desiredWaitFactor = ComputeMountedWeaponAccuracyWaitFactor(desiredRiding);
                applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.WeaponBestAccuracyWaitTime, baseWaitFactor, desiredWaitFactor);

                float baseInaccuracyFactor = ComputeMountedWeaponInaccuracyFactor(templateRiding);
                float desiredInaccuracyFactor = ComputeMountedWeaponInaccuracyFactor(desiredRiding);
                applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.WeaponInaccuracy, baseInaccuracyFactor, desiredInaccuracyFactor);
            }

            return applied;
        }

        private static bool TryApplyRidingPerkDrivenStats(
            AgentDrivenProperties agentDrivenProperties,
            int perkCount)
        {
            if (agentDrivenProperties == null || perkCount <= 0)
                return false;

            float speedFactor = ComputePerkPositiveFactor(perkCount, 0.004f, 0.06f);
            float accuracyFactor = ComputePerkPenaltyReductionFactor(perkCount, 0.006f, 0.12f, 0.82f);
            bool applied = false;
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.SwingSpeedMultiplier, 1f, speedFactor);
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.ThrustOrRangedReadySpeedMultiplier, 1f, speedFactor);
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.ReloadSpeed, 1f, speedFactor);
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.WeaponBestAccuracyWaitTime, 1f, accuracyFactor);
            applied |= TryScaleDrivenProperty(agentDrivenProperties, DrivenProperty.WeaponInaccuracy, 1f, accuracyFactor);
            return applied;
        }

        private static int TryGetCombatProfileSkillValue(MaterializedCombatProfileRuntimeState profile, SkillObject skillObject, int fallback)
        {
            if (profile == null || skillObject == null)
                return fallback;

            string skillId = skillObject.StringId;
            if (string.IsNullOrWhiteSpace(skillId))
                return fallback;

            switch (skillId)
            {
                case "OneHanded":
                    return profile.SkillOneHanded > 0 ? profile.SkillOneHanded : fallback;
                case "TwoHanded":
                    return profile.SkillTwoHanded > 0 ? profile.SkillTwoHanded : fallback;
                case "Polearm":
                    return profile.SkillPolearm > 0 ? profile.SkillPolearm : fallback;
                case "Bow":
                    return profile.SkillBow > 0 ? profile.SkillBow : fallback;
                case "Crossbow":
                    return profile.SkillCrossbow > 0 ? profile.SkillCrossbow : fallback;
                case "Throwing":
                    return profile.SkillThrowing > 0 ? profile.SkillThrowing : fallback;
                case "Riding":
                    return profile.SkillRiding > 0 ? profile.SkillRiding : fallback;
                case "Athletics":
                    return profile.SkillAthletics > 0 ? profile.SkillAthletics : fallback;
                default:
                    return fallback;
            }
        }

        internal static bool TryGetExactHeroCombatProfileSkillValue(
            Agent agent,
            SkillObject skillObject,
            out int skillValue,
            out string entryId)
        {
            skillValue = 0;
            entryId = string.Empty;

            if (agent == null || skillObject == null || agent.Mission == null)
                return false;

            EnsureMaterializedCombatProfileMission(agent.Mission);
            if (!_materializedCombatProfilesByAgentIndex.TryGetValue(agent.Index, out MaterializedCombatProfileRuntimeState profile) ||
                !IsHeroProfileEligibleForExactPersonalPerks(profile))
            {
                return false;
            }

            int exactSkillValue = TryGetCombatProfileSkillValue(profile, skillObject, 0);
            if (exactSkillValue <= 0)
                return false;

            skillValue = exactSkillValue;
            entryId = profile.EntryId ?? string.Empty;
            return true;
        }

        internal static bool TryGetExactHeroCombatProfileBaseHitPoints(
            Agent agent,
            out int baseHitPoints,
            out string entryId)
        {
            baseHitPoints = 0;
            entryId = string.Empty;

            if (agent == null || agent.Mission == null)
                return false;

            EnsureMaterializedCombatProfileMission(agent.Mission);
            if (!_materializedCombatProfilesByAgentIndex.TryGetValue(agent.Index, out MaterializedCombatProfileRuntimeState profile) ||
                !IsHeroProfileEligibleForExactPersonalPerks(profile) ||
                profile.BaseHitPoints <= 0)
            {
                return false;
            }

            baseHitPoints = profile.BaseHitPoints;
            entryId = profile.EntryId ?? string.Empty;
            return true;
        }

        internal static bool HasExactHeroCombatProfilePerk(Agent agent, string perkId, out string entryId)
        {
            entryId = string.Empty;

            if (agent == null || string.IsNullOrWhiteSpace(perkId) || agent.Mission == null)
                return false;

            EnsureMaterializedCombatProfileMission(agent.Mission);
            if (!_materializedCombatProfilesByAgentIndex.TryGetValue(agent.Index, out MaterializedCombatProfileRuntimeState profile) ||
                !IsHeroProfileEligibleForExactPersonalPerks(profile))
            {
                return false;
            }

            entryId = profile.EntryId ?? string.Empty;
            return HasPerkId(profile.PerkIds, perkId);
        }

        private static int TryGetCharacterSkillValue(BasicCharacterObject character, SkillObject skillObject)
        {
            if (character == null || skillObject == null)
                return 0;

            try
            {
                return character.GetSkillValue(skillObject);
            }
            catch
            {
                return 0;
            }
        }

        private static float ComputeWeaponInaccuracy(WeaponComponentData weapon, int skillValue)
        {
            if (weapon == null)
                return 0f;

            float inaccuracy = (100f - weapon.Accuracy) * (1f - 0.002f * skillValue) * 0.001f;
            if (weapon.WeaponClass == WeaponClass.ThrowingAxe)
                inaccuracy *= 2f;

            return Math.Max(0.0001f, inaccuracy);
        }

        private static float ComputeMountedWeaponSpeedFactor(int ridingSkill)
        {
            float penalty = 0.3f - ridingSkill * 0.003f;
            return penalty > 0f ? Math.Max(0.0001f, 1f - penalty) : 1f;
        }

        private static float ComputeMountedWeaponAccuracyWaitFactor(int ridingSkill)
        {
            float penalty = 0.3f - ridingSkill * 0.003f;
            return penalty > 0f ? 1f + penalty : 1f;
        }

        private static float ComputeMountedWeaponInaccuracyFactor(int ridingSkill)
        {
            float penalty = 15f - ridingSkill * 0.15f;
            return penalty > 0f ? 1f + penalty : 1f;
        }

        private static float ComputeExactPersonalWeaponDamageMultiplier(SkillObject relevantSkill, int desiredRelevantSkill)
        {
            if (relevantSkill == null || desiredRelevantSkill <= 0)
                return 1f;

            string skillId = relevantSkill.StringId;
            if (string.Equals(skillId, "OneHanded", StringComparison.OrdinalIgnoreCase))
            {
                // Native SP DefaultSkillEffects.OneHandedDamage = +0.15% per skill level.
                return 1f + desiredRelevantSkill * 0.0015f;
            }

            if (string.Equals(skillId, "TwoHanded", StringComparison.OrdinalIgnoreCase))
            {
                // Native SP DefaultSkillEffects.TwoHandedDamage = +0.16% per skill level.
                return 1f + desiredRelevantSkill * 0.0016f;
            }

            if (string.Equals(skillId, "Polearm", StringComparison.OrdinalIgnoreCase))
            {
                // Native SP DefaultSkillEffects.PolearmDamage = +0.07% per skill level.
                return 1f + desiredRelevantSkill * 0.0007f;
            }

            if (string.Equals(skillId, "Bow", StringComparison.OrdinalIgnoreCase))
            {
                // Native SP DefaultSkillEffects.BowDamage = +0.11% per skill level.
                return 1f + desiredRelevantSkill * 0.0011f;
            }

            if (string.Equals(skillId, "Throwing", StringComparison.OrdinalIgnoreCase))
            {
                // Native SP DefaultSkillEffects.ThrowingDamage = +0.06% per skill level.
                return 1f + desiredRelevantSkill * 0.0006f;
            }

            return 1f;
        }

        private static float ComputeSandboxMountedWeaponInaccuracyFactor(int ridingSkill)
        {
            float penalty = 5f - ridingSkill * 0.05f;
            return penalty > 0f ? 1f + penalty : 1f;
        }

        private static float ComputeAttributeOffenseFactor(int attributeValue)
        {
            int normalized = Math.Max(0, Math.Min(10, attributeValue));
            return Math.Max(0.75f, 0.92f + normalized * 0.02f);
        }

        private static float ComputePartySkillPositiveFactor(int skillValue, float perSkillBonus, float maxBonus)
        {
            if (skillValue <= 0)
                return 1f;

            float bonus = Math.Min(maxBonus, skillValue * perSkillBonus);
            return 1f + Math.Max(0f, bonus);
        }

        private static float ComputePartySkillPenaltyReductionFactor(int skillValue, float perSkillReduction, float maxReduction, float floor)
        {
            if (skillValue <= 0)
                return 1f;

            float reduction = Math.Min(maxReduction, skillValue * perSkillReduction);
            return Math.Max(floor, 1f - Math.Max(0f, reduction));
        }

        private static float ComputeAccuracyPenaltyFactor(int attributeValue)
        {
            int normalized = Math.Max(0, Math.Min(10, attributeValue));
            return Math.Max(0.6f, 1.12f - normalized * 0.03f);
        }

        private static float ComputeSpeedReachDurationFactor(int attributeValue)
        {
            int normalized = Math.Max(0, Math.Min(10, attributeValue));
            return Math.Max(0.65f, 1.1f - normalized * 0.025f);
        }

        private static float ComputePerkPositiveFactor(int perkCount, float perPerkBonus, float maxBonus)
        {
            if (perkCount <= 0)
                return 1f;

            float bonus = Math.Min(maxBonus, perkCount * perPerkBonus);
            return 1f + Math.Max(0f, bonus);
        }

        private static float ComputePerkPenaltyReductionFactor(int perkCount, float perPerkReduction, float maxReduction, float floor)
        {
            if (perkCount <= 0)
                return 1f;

            float reduction = Math.Min(maxReduction, perkCount * perPerkReduction);
            return Math.Max(floor, 1f - Math.Max(0f, reduction));
        }

        private static int DeriveCharacterVigorAttribute(BasicCharacterObject character)
        {
            return DeriveCombatAttributeFromSkills(
                TryGetCharacterSkillValue(character, DefaultSkills.OneHanded),
                TryGetCharacterSkillValue(character, DefaultSkills.TwoHanded),
                TryGetCharacterSkillValue(character, DefaultSkills.Polearm));
        }

        private static int DeriveCharacterControlAttribute(BasicCharacterObject character)
        {
            return DeriveCombatAttributeFromSkills(
                TryGetCharacterSkillValue(character, DefaultSkills.Bow),
                TryGetCharacterSkillValue(character, DefaultSkills.Crossbow),
                TryGetCharacterSkillValue(character, DefaultSkills.Throwing));
        }

        private static int DeriveCharacterEnduranceAttribute(BasicCharacterObject character)
        {
            return DeriveCombatAttributeFromSkills(
                TryGetCharacterSkillValue(character, DefaultSkills.Riding),
                TryGetCharacterSkillValue(character, DefaultSkills.Athletics));
        }

        private static int DeriveCombatAttributeFromSkills(params int[] skillValues)
        {
            if (skillValues == null || skillValues.Length == 0)
                return 0;

            int maxSkill = 0;
            for (int i = 0; i < skillValues.Length; i++)
                maxSkill = Math.Max(maxSkill, skillValues[i]);

            if (maxSkill <= 0)
                return 0;

            return Math.Max(1, Math.Min(10, 1 + (int)Math.Round(maxSkill / 40f, MidpointRounding.AwayFromZero)));
        }

        private static void SetDrivenPropertyBaselineContext(MaterializedCombatProfileRuntimeState profile, Agent agent, bool isMountContext)
        {
            _drivenPropertyBaselineProfileContext = profile;
            _drivenPropertyBaselineAgentContext = agent;
            _drivenPropertyBaselineMountContext = isMountContext;

            if (profile == null)
                return;

            Dictionary<DrivenProperty, float> accumulatedScales = isMountContext
                ? profile.MountDrivenPropertyAccumulatedScales
                : profile.HumanDrivenPropertyAccumulatedScales;
            accumulatedScales.Clear();
        }

        private static void ClearDrivenPropertyBaselineContext()
        {
            _drivenPropertyBaselineProfileContext = null;
            _drivenPropertyBaselineAgentContext = null;
            _drivenPropertyBaselineMountContext = false;
        }

        private static string GetDrivenPropertyBaselineSignature(Agent agent, bool isMountContext)
        {
            if (agent == null)
                return "null";

            if (isMountContext)
            {
                string mountCharacterId = agent.Character?.StringId ?? "null";
                int riderIndex = agent.RiderAgent?.Index ?? -1;
                return "mount|" + mountCharacterId + "|rider=" + riderIndex;
            }

            EquipmentIndex primaryWieldedItemIndex = agent.GetPrimaryWieldedItemIndex();
            ItemObject primaryItem = primaryWieldedItemIndex != EquipmentIndex.None ? agent.Equipment[primaryWieldedItemIndex].Item : null;
            string primaryItemId = primaryItem?.StringId ?? "none";
            bool isMounted = agent.HasMount || agent.MountAgent != null;
            return "human|" + primaryItemId + "|mounted=" + isMounted;
        }

        private static bool TryScaleDrivenPropertyFromBaselineContext(
            AgentDrivenProperties agentDrivenProperties,
            DrivenProperty drivenProperty,
            float baseFactor,
            float desiredFactor)
        {
            MaterializedCombatProfileRuntimeState profile = _drivenPropertyBaselineProfileContext;
            Agent agent = _drivenPropertyBaselineAgentContext;
            if (profile == null || agent == null || agentDrivenProperties == null || baseFactor <= 0f || desiredFactor <= 0f)
                return false;

            float scale = desiredFactor / baseFactor;
            if (float.IsNaN(scale) || float.IsInfinity(scale) || Math.Abs(scale - 1f) < 0.001f)
                return false;

            bool isMountContext = _drivenPropertyBaselineMountContext;
            Dictionary<DrivenProperty, float> baselines = isMountContext
                ? profile.MountDrivenPropertyBaselines
                : profile.HumanDrivenPropertyBaselines;
            Dictionary<DrivenProperty, float> accumulatedScales = isMountContext
                ? profile.MountDrivenPropertyAccumulatedScales
                : profile.HumanDrivenPropertyAccumulatedScales;
            string signature = GetDrivenPropertyBaselineSignature(agent, isMountContext);

            if (isMountContext)
            {
                if (!string.Equals(profile.MountDrivenPropertyBaselineSignature, signature, StringComparison.Ordinal))
                {
                    profile.MountDrivenPropertyBaselineSignature = signature;
                    baselines.Clear();
                    accumulatedScales.Clear();
                }
            }
            else
            {
                if (!string.Equals(profile.HumanDrivenPropertyBaselineSignature, signature, StringComparison.Ordinal))
                {
                    profile.HumanDrivenPropertyBaselineSignature = signature;
                    baselines.Clear();
                    accumulatedScales.Clear();
                }
            }

            if (!baselines.TryGetValue(drivenProperty, out float baselineValue))
            {
                baselineValue = agentDrivenProperties.GetStat(drivenProperty);
                baselines[drivenProperty] = baselineValue;
            }

            float accumulatedScale = 1f;
            accumulatedScales.TryGetValue(drivenProperty, out accumulatedScale);
            if (accumulatedScale <= 0f)
                accumulatedScale = 1f;

            accumulatedScale *= scale;
            accumulatedScales[drivenProperty] = accumulatedScale;

            float desiredValue = Math.Max(0f, baselineValue * accumulatedScale);
            return TrySetDrivenProperty(agentDrivenProperties, drivenProperty, desiredValue);
        }

        private static bool TryScaleDrivenProperty(AgentDrivenProperties agentDrivenProperties, DrivenProperty drivenProperty, float baseFactor, float desiredFactor)
        {
            if (agentDrivenProperties == null)
                return false;

            if (TryScaleDrivenPropertyFromBaselineContext(agentDrivenProperties, drivenProperty, baseFactor, desiredFactor))
                return true;

            float currentValue = agentDrivenProperties.GetStat(drivenProperty);
            if (!TryScaleStat(ref currentValue, baseFactor, desiredFactor))
                return false;

            agentDrivenProperties.SetStat(drivenProperty, currentValue);
            return true;
        }

        private static bool TrySetDrivenProperty(AgentDrivenProperties agentDrivenProperties, DrivenProperty drivenProperty, float desiredValue, float epsilon = 0.001f)
        {
            if (agentDrivenProperties == null)
                return false;

            float currentValue = agentDrivenProperties.GetStat(drivenProperty);
            if (Math.Abs(currentValue - desiredValue) <= epsilon)
                return false;

            agentDrivenProperties.SetStat(drivenProperty, desiredValue);
            return true;
        }

        private static bool TryScaleStat(ref float currentValue, float baseFactor, float desiredFactor)
        {
            if (baseFactor <= 0f || desiredFactor <= 0f)
                return false;

            float scale = desiredFactor / baseFactor;
            if (float.IsNaN(scale) || float.IsInfinity(scale) || Math.Abs(scale - 1f) < 0.001f)
                return false;

            currentValue = Math.Max(0f, currentValue * scale);
            return true;
        }

        private static void CountMaterializedCombatProfileApply(MaterializedCombatProfileRuntimeState profile, string counterKey, ref bool countedFlag)
        {
            if (profile == null || countedFlag)
                return;

            countedFlag = true;
            IncrementMaterializedEquipmentCounter(MaterializedCombatProfileApplyCounts, counterKey);
        }

        private static string TryApplyMaterializedEquipmentOverrides(
            Equipment spawnEquipment,
            RosterEntryState entryState,
            List<string> missedSlots = null,
            bool trackCoverage = false,
            bool includeWeapons = true)
        {
            if (spawnEquipment == null || entryState == null)
                return "(none)";

            ClearTemplateEquipmentForUnspecifiedSnapshotSlots(spawnEquipment, entryState, includeWeapons);

            var appliedSlots = new List<string>();
            if (includeWeapons && entryState.IsMounted)
            {
                TryApplyMountedMaterializedWeaponOverrides(spawnEquipment, entryState, appliedSlots, missedSlots, trackCoverage);
            }
            else if (includeWeapons)
            {
                TryApplyMaterializedArmorOverride(spawnEquipment, EquipmentIndex.Weapon0, entryState.CombatItem0Id, "Item0", entryState, appliedSlots, missedSlots, trackCoverage);
                TryApplyMaterializedArmorOverride(spawnEquipment, EquipmentIndex.Weapon1, entryState.CombatItem1Id, "Item1", entryState, appliedSlots, missedSlots, trackCoverage);
                TryApplyMaterializedArmorOverride(spawnEquipment, EquipmentIndex.Weapon2, entryState.CombatItem2Id, "Item2", entryState, appliedSlots, missedSlots, trackCoverage);
                TryApplyMaterializedArmorOverride(spawnEquipment, EquipmentIndex.Weapon3, entryState.CombatItem3Id, "Item3", entryState, appliedSlots, missedSlots, trackCoverage);
            }
            TryApplyMaterializedArmorOverride(spawnEquipment, EquipmentIndex.Head, entryState.CombatHeadId, "Head", entryState, appliedSlots, missedSlots, trackCoverage);
            TryApplyMaterializedArmorOverride(spawnEquipment, EquipmentIndex.Body, entryState.CombatBodyId, "Body", entryState, appliedSlots, missedSlots, trackCoverage);
            TryApplyMaterializedArmorOverride(spawnEquipment, EquipmentIndex.Leg, entryState.CombatLegId, "Leg", entryState, appliedSlots, missedSlots, trackCoverage);
            TryApplyMaterializedArmorOverride(spawnEquipment, EquipmentIndex.Gloves, entryState.CombatGlovesId, "Gloves", entryState, appliedSlots, missedSlots, trackCoverage);
            TryApplyMaterializedArmorOverride(spawnEquipment, EquipmentIndex.Cape, entryState.CombatCapeId, "Cape", entryState, appliedSlots, missedSlots, trackCoverage);
            TryApplyMaterializedArmorOverride(spawnEquipment, EquipmentIndex.Horse, entryState.CombatHorseId, "Horse", entryState, appliedSlots, missedSlots, trackCoverage);
            TryApplyMaterializedArmorOverride(spawnEquipment, EquipmentIndex.HorseHarness, entryState.CombatHorseHarnessId, "HorseHarness", entryState, appliedSlots, missedSlots, trackCoverage);
            return appliedSlots.Count > 0 ? string.Join(", ", appliedSlots) : "(none)";
        }

        private static void ClearTemplateEquipmentForUnspecifiedSnapshotSlots(Equipment spawnEquipment, RosterEntryState entryState, bool includeWeapons)
        {
            if (spawnEquipment == null || entryState == null)
                return;

            if (includeWeapons)
            {
                ClearTemplateEquipmentSlotIfMissing(spawnEquipment, EquipmentIndex.Weapon0, entryState.CombatItem0Id);
                ClearTemplateEquipmentSlotIfMissing(spawnEquipment, EquipmentIndex.Weapon1, entryState.CombatItem1Id);
                ClearTemplateEquipmentSlotIfMissing(spawnEquipment, EquipmentIndex.Weapon2, entryState.CombatItem2Id);
                ClearTemplateEquipmentSlotIfMissing(spawnEquipment, EquipmentIndex.Weapon3, entryState.CombatItem3Id);
            }
            ClearTemplateEquipmentSlotIfMissing(spawnEquipment, EquipmentIndex.Head, entryState.CombatHeadId);
            ClearTemplateEquipmentSlotIfMissing(spawnEquipment, EquipmentIndex.Body, entryState.CombatBodyId);
            ClearTemplateEquipmentSlotIfMissing(spawnEquipment, EquipmentIndex.Leg, entryState.CombatLegId);
            ClearTemplateEquipmentSlotIfMissing(spawnEquipment, EquipmentIndex.Gloves, entryState.CombatGlovesId);
            ClearTemplateEquipmentSlotIfMissing(spawnEquipment, EquipmentIndex.Cape, entryState.CombatCapeId);
            ClearTemplateEquipmentSlotIfMissing(spawnEquipment, EquipmentIndex.Horse, entryState.CombatHorseId);
            ClearTemplateEquipmentSlotIfMissing(spawnEquipment, EquipmentIndex.HorseHarness, entryState.CombatHorseHarnessId);
        }

        private static void ClearTemplateEquipmentSlotIfMissing(Equipment spawnEquipment, EquipmentIndex slot, string snapshotItemId)
        {
            if (spawnEquipment == null || !string.IsNullOrWhiteSpace(snapshotItemId))
                return;

            spawnEquipment[slot] = default(EquipmentElement);
        }

        private sealed class ResolvedMaterializedEquipmentOverride
        {
            public string SourceItemId { get; set; }
            public string SourceSlotLabel { get; set; }
            public string ResolvedItemId { get; set; }
            public string ResolutionSource { get; set; }
            public ItemObject Item { get; set; }
        }

        private enum MaterializedMountedWeaponRole
        {
            Other = 0,
            Melee = 1,
            Polearm = 2,
            Shield = 3,
            Ranged = 4,
            Ammo = 5
        }

        private enum ExactCampaignSnapshotOverlayMode
        {
            ServerAuthoritative = 0,
            ClientVisualOnly = 1
        }

        private static void TryApplyMountedMaterializedWeaponOverrides(
            Equipment spawnEquipment,
            RosterEntryState entryState,
            List<string> appliedSlots,
            List<string> missedSlots,
            bool trackCoverage)
        {
            var resolvedItems = new List<ResolvedMaterializedEquipmentOverride>();
            TryAddResolvedMaterializedEquipmentOverride(entryState.CombatItem0Id, "Item0", entryState, resolvedItems, missedSlots, trackCoverage);
            TryAddResolvedMaterializedEquipmentOverride(entryState.CombatItem1Id, "Item1", entryState, resolvedItems, missedSlots, trackCoverage);
            TryAddResolvedMaterializedEquipmentOverride(entryState.CombatItem2Id, "Item2", entryState, resolvedItems, missedSlots, trackCoverage);
            TryAddResolvedMaterializedEquipmentOverride(entryState.CombatItem3Id, "Item3", entryState, resolvedItems, missedSlots, trackCoverage);
            if (resolvedItems.Count == 0)
                return;

            bool hasRanged = resolvedItems.Any(item => GetMaterializedMountedWeaponRole(item.Item) == MaterializedMountedWeaponRole.Ranged);
            bool hasAmmo = resolvedItems.Any(item => GetMaterializedMountedWeaponRole(item.Item) == MaterializedMountedWeaponRole.Ammo);
            if (hasRanged && hasAmmo)
            {
                EquipmentIndex[] sequentialSlots =
                {
                    EquipmentIndex.Weapon0,
                    EquipmentIndex.Weapon1,
                    EquipmentIndex.Weapon2,
                    EquipmentIndex.Weapon3
                };

                for (int i = 0; i < resolvedItems.Count && i < sequentialSlots.Length; i++)
                    ApplyResolvedMaterializedEquipmentOverride(spawnEquipment, sequentialSlots[i], resolvedItems[i], appliedSlots);

                return;
            }

            ResolvedMaterializedEquipmentOverride shieldItem = resolvedItems.FirstOrDefault(item =>
                GetMaterializedMountedWeaponRole(item.Item) == MaterializedMountedWeaponRole.Shield);
            ResolvedMaterializedEquipmentOverride primaryItem = resolvedItems.FirstOrDefault(item =>
                    GetMaterializedMountedWeaponRole(item.Item) == MaterializedMountedWeaponRole.Polearm) ??
                resolvedItems.FirstOrDefault(item =>
                    GetMaterializedMountedWeaponRole(item.Item) == MaterializedMountedWeaponRole.Melee) ??
                resolvedItems.FirstOrDefault(item => item != shieldItem);

            var remainingItems = resolvedItems
                .Where(item => item != null && !ReferenceEquals(item, primaryItem) && !ReferenceEquals(item, shieldItem))
                .ToList();

            if (primaryItem != null)
                ApplyResolvedMaterializedEquipmentOverride(spawnEquipment, EquipmentIndex.Weapon0, primaryItem, appliedSlots);

            if (shieldItem != null)
                ApplyResolvedMaterializedEquipmentOverride(spawnEquipment, EquipmentIndex.Weapon1, shieldItem, appliedSlots);

            EquipmentIndex[] fallbackSlots = shieldItem != null
                ? new[] { EquipmentIndex.Weapon2, EquipmentIndex.Weapon3 }
                : new[] { EquipmentIndex.Weapon1, EquipmentIndex.Weapon2, EquipmentIndex.Weapon3 };
            for (int i = 0; i < remainingItems.Count && i < fallbackSlots.Length; i++)
                ApplyResolvedMaterializedEquipmentOverride(spawnEquipment, fallbackSlots[i], remainingItems[i], appliedSlots);
        }

        private static void TryAddResolvedMaterializedEquipmentOverride(
            string itemId,
            string sourceSlotLabel,
            RosterEntryState entryState,
            List<ResolvedMaterializedEquipmentOverride> resolvedItems,
            List<string> missedSlots,
            bool trackCoverage)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return;

            string resolvedItemId;
            string resolutionSource;
            ItemObject item = ResolveMaterializedEquipmentItem(itemId, entryState, sourceSlotLabel, out resolvedItemId, out resolutionSource, trackCoverage);
            if (item == null)
            {
                missedSlots?.Add(sourceSlotLabel + "=" + itemId);
                return;
            }

            resolvedItems.Add(new ResolvedMaterializedEquipmentOverride
            {
                SourceItemId = itemId,
                SourceSlotLabel = sourceSlotLabel,
                ResolvedItemId = resolvedItemId,
                ResolutionSource = resolutionSource,
                Item = item
            });
        }

        private static void AssignMountedMaterializedWeaponByRole(
            Equipment spawnEquipment,
            EquipmentIndex targetSlot,
            List<ResolvedMaterializedEquipmentOverride> resolvedItems,
            MaterializedMountedWeaponRole role,
            HashSet<string> assignedSourceIds,
            List<string> appliedSlots)
        {
            if (spawnEquipment == null || resolvedItems == null || resolvedItems.Count == 0)
                return;

            ResolvedMaterializedEquipmentOverride match = resolvedItems.FirstOrDefault(item =>
                item != null &&
                !assignedSourceIds.Contains(item.SourceItemId) &&
                GetMaterializedMountedWeaponRole(item.Item) == role);
            if (match == null)
                return;

            ApplyResolvedMaterializedEquipmentOverride(spawnEquipment, targetSlot, match, appliedSlots);
            assignedSourceIds.Add(match.SourceItemId);
        }

        private static void FillMountedMaterializedWeaponFallback(
            Equipment spawnEquipment,
            EquipmentIndex targetSlot,
            List<ResolvedMaterializedEquipmentOverride> resolvedItems,
            HashSet<string> assignedSourceIds,
            List<string> appliedSlots)
        {
            if (spawnEquipment == null || resolvedItems == null || resolvedItems.Count == 0)
                return;

            ResolvedMaterializedEquipmentOverride fallback = resolvedItems.FirstOrDefault(item =>
                item != null &&
                !assignedSourceIds.Contains(item.SourceItemId));
            if (fallback == null)
                return;

            ApplyResolvedMaterializedEquipmentOverride(spawnEquipment, targetSlot, fallback, appliedSlots);
            assignedSourceIds.Add(fallback.SourceItemId);
        }

        private static void ApplyResolvedMaterializedEquipmentOverride(
            Equipment spawnEquipment,
            EquipmentIndex slot,
            ResolvedMaterializedEquipmentOverride resolvedItem,
            List<string> appliedSlots)
        {
            if (spawnEquipment == null || resolvedItem?.Item == null)
                return;

            spawnEquipment[slot] = new EquipmentElement(resolvedItem.Item, null, null, false);
            if (string.Equals(resolvedItem.SourceItemId, resolvedItem.ResolvedItemId, StringComparison.Ordinal))
            {
                appliedSlots.Add(GetEquipmentSlotLabel(slot) + "=" + resolvedItem.Item.StringId);
                return;
            }

            appliedSlots.Add(
                GetEquipmentSlotLabel(slot) + "=" + resolvedItem.Item.StringId +
                "(from:" + resolvedItem.SourceItemId +
                ",via:" + resolvedItem.ResolutionSource + ")");
        }

        private static MaterializedMountedWeaponRole GetMaterializedMountedWeaponRole(ItemObject item)
        {
            if (item == null)
                return MaterializedMountedWeaponRole.Other;

            WeaponComponentData primaryWeapon = item.PrimaryWeapon;
            if (primaryWeapon != null)
            {
                if (primaryWeapon.IsShield)
                    return MaterializedMountedWeaponRole.Shield;
                if (primaryWeapon.IsPolearm)
                    return MaterializedMountedWeaponRole.Polearm;
                if (primaryWeapon.IsAmmo)
                    return MaterializedMountedWeaponRole.Ammo;
                if (primaryWeapon.IsRangedWeapon || primaryWeapon.WeaponClass == WeaponClass.Javelin || primaryWeapon.WeaponClass == WeaponClass.ThrowingAxe || primaryWeapon.WeaponClass == WeaponClass.ThrowingKnife || primaryWeapon.WeaponClass == WeaponClass.Stone || primaryWeapon.WeaponClass == WeaponClass.SlingStone)
                    return MaterializedMountedWeaponRole.Ranged;
                if (primaryWeapon.IsOneHanded || primaryWeapon.IsTwoHanded || primaryWeapon.IsMeleeWeapon)
                    return MaterializedMountedWeaponRole.Melee;
            }

            switch (item.ItemType)
            {
                case ItemObject.ItemTypeEnum.Shield:
                    return MaterializedMountedWeaponRole.Shield;
                case ItemObject.ItemTypeEnum.Polearm:
                    return MaterializedMountedWeaponRole.Polearm;
                case ItemObject.ItemTypeEnum.Bow:
                case ItemObject.ItemTypeEnum.Crossbow:
                case ItemObject.ItemTypeEnum.Sling:
                case ItemObject.ItemTypeEnum.Thrown:
                    return MaterializedMountedWeaponRole.Ranged;
                case ItemObject.ItemTypeEnum.Arrows:
                case ItemObject.ItemTypeEnum.Bolts:
                case ItemObject.ItemTypeEnum.SlingStones:
                    return MaterializedMountedWeaponRole.Ammo;
                case ItemObject.ItemTypeEnum.OneHandedWeapon:
                case ItemObject.ItemTypeEnum.TwoHandedWeapon:
                    return MaterializedMountedWeaponRole.Melee;
                default:
                    return MaterializedMountedWeaponRole.Other;
            }
        }

        private static string GetEquipmentSlotLabel(EquipmentIndex slot)
        {
            switch (slot)
            {
                case EquipmentIndex.Weapon0:
                    return "Item0";
                case EquipmentIndex.Weapon1:
                    return "Item1";
                case EquipmentIndex.Weapon2:
                    return "Item2";
                case EquipmentIndex.Weapon3:
                    return "Item3";
                case EquipmentIndex.Head:
                    return "Head";
                case EquipmentIndex.Body:
                    return "Body";
                case EquipmentIndex.Leg:
                    return "Leg";
                case EquipmentIndex.Gloves:
                    return "Gloves";
                case EquipmentIndex.Cape:
                    return "Cape";
                case EquipmentIndex.Horse:
                    return "Horse";
                case EquipmentIndex.HorseHarness:
                    return "HorseHarness";
                default:
                    return slot.ToString();
            }
        }

        private static void TryApplyMaterializedArmorOverride(
            Equipment spawnEquipment,
            EquipmentIndex slot,
            string itemId,
            string slotLabel,
            RosterEntryState entryState,
            List<string> appliedSlots,
            List<string> missedSlots,
            bool trackCoverage)
        {
            if (spawnEquipment == null || string.IsNullOrWhiteSpace(itemId))
                return;

            string resolvedItemId;
            string resolutionSource;
            ItemObject item = ResolveMaterializedEquipmentItem(itemId, entryState, slotLabel, out resolvedItemId, out resolutionSource, trackCoverage);
            if (item == null)
            {
                missedSlots?.Add(slotLabel + "=" + itemId);
                return;
            }

            spawnEquipment[slot] = new EquipmentElement(item, null, null, false);
            if (string.Equals(itemId, resolvedItemId, StringComparison.Ordinal))
            {
                appliedSlots.Add(slotLabel + "=" + item.StringId);
                return;
            }

            appliedSlots.Add(
                slotLabel + "=" + item.StringId +
                "(from:" + itemId +
                ",via:" + resolutionSource + ")");
        }

        private static ItemObject ResolveMaterializedEquipmentItem(
            string itemId,
            RosterEntryState entryState,
            string slotLabel,
            out string resolvedItemId,
            out string resolutionSource,
            bool trackCoverage)
        {
            resolvedItemId = itemId;
            resolutionSource = "direct";
            if (string.IsNullOrWhiteSpace(itemId))
                return null;

            if (ShouldSuppressMaterializedEquipmentItem(itemId, entryState, slotLabel, out string suppressionReason))
            {
                resolvedItemId = itemId;
                resolutionSource = suppressionReason ?? "suppressed";
                RecordMaterializedEquipmentResolution(itemId, resolutionSource, trackCoverage);
                LogSuppressedMaterializedEquipmentItem(itemId, entryState, slotLabel, resolutionSource);
                return null;
            }

            if (ShouldPreferNormalizedMaterializedEquipmentItem(itemId, entryState, slotLabel) &&
                TryNormalizeCampaignEquipmentToMpItemId(itemId, entryState, slotLabel, out string preferredNormalizedItemId) &&
                !string.IsNullOrWhiteSpace(preferredNormalizedItemId))
            {
                ItemObject preferredNormalizedItem = TryGetMaterializedEquipmentItem(preferredNormalizedItemId);
                if (preferredNormalizedItem != null)
                {
                    resolvedItemId = preferredNormalizedItem.StringId;
                    resolutionSource = "normalized-preferred";
                    RecordMaterializedEquipmentResolution(itemId, resolutionSource, trackCoverage);
                    return preferredNormalizedItem;
                }
            }

            ItemObject directItem = TryGetMaterializedEquipmentItem(itemId);
            if (directItem != null)
            {
                resolvedItemId = directItem.StringId;
                resolutionSource = "direct";
                RecordMaterializedEquipmentResolution(itemId, resolutionSource, trackCoverage);
                return directItem;
            }

            string compatibilityAliasItemId = null;
            if (ExactEquipmentCompatibilityAliasIds.TryGetValue(itemId, out compatibilityAliasItemId) &&
                !string.IsNullOrWhiteSpace(compatibilityAliasItemId))
            {
                ItemObject compatibilityAliasItem = TryGetMaterializedEquipmentItem(compatibilityAliasItemId);
                if (compatibilityAliasItem != null)
                {
                    resolvedItemId = compatibilityAliasItem.StringId;
                    resolutionSource = "compat-alias";
                    RecordMaterializedEquipmentResolution(itemId, resolutionSource, trackCoverage);
                    return compatibilityAliasItem;
                }
            }

            if (ExactEquipmentCompatibilityStandInItemIds.TryGetValue(itemId, out string compatibilityStandInItemId) &&
                !string.IsNullOrWhiteSpace(compatibilityStandInItemId))
            {
                ItemObject compatibilityStandInItem = TryGetMaterializedEquipmentItem(compatibilityStandInItemId);
                if (compatibilityStandInItem != null)
                {
                    resolvedItemId = compatibilityStandInItem.StringId;
                    resolutionSource = "compat-standin";
                    RecordMaterializedEquipmentResolution(itemId, resolutionSource, trackCoverage);
                    return compatibilityStandInItem;
                }
            }

            if (!itemId.StartsWith("mp_", StringComparison.Ordinal))
            {
                string mpPrefixedItemId = "mp_" + itemId;
                ItemObject prefixedItem = TryGetMaterializedEquipmentItem(mpPrefixedItemId);
                if (prefixedItem != null)
                {
                    resolvedItemId = prefixedItem.StringId;
                    resolutionSource = "mp-prefix";
                    RecordMaterializedEquipmentResolution(itemId, resolutionSource, trackCoverage);
                    return prefixedItem;
                }
            }

            if (TryNormalizeCampaignEquipmentToMpItemId(itemId, entryState, slotLabel, out string normalizedItemId))
            {
                ItemObject normalizedItem = TryGetMaterializedEquipmentItem(normalizedItemId);
                if (normalizedItem != null)
                {
                    resolvedItemId = normalizedItem.StringId;
                    resolutionSource = "normalized";
                    RecordMaterializedEquipmentResolution(itemId, resolutionSource, trackCoverage);
                    return normalizedItem;
                }
            }

            if (ImportedEquipmentProbeIds.Contains(itemId, StringComparer.Ordinal))
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: exact imported item lookup still unresolved. " +
                    "ItemId=" + itemId +
                    " AliasId=" + (compatibilityAliasItemId ?? "(none)") +
                    " Slot=" + (slotLabel ?? "(null)") +
                    " SpawnTemplate=" + (entryState?.SpawnTemplateId ?? "(null)") +
                    " Culture=" + (entryState?.CultureId ?? "(null)"));
            }

            RecordMaterializedEquipmentMiss(itemId, trackCoverage);
            return null;
        }

        private static bool ShouldPreferNormalizedMaterializedEquipmentItem(
            string itemId,
            RosterEntryState entryState,
            string slotLabel)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            switch (itemId.Trim().ToLowerInvariant())
            {
                case "sling_wool":
                    return true;
                default:
                    return false;
            }
        }

        private static bool ShouldSuppressMaterializedEquipmentItem(
            string itemId,
            RosterEntryState entryState,
            string slotLabel,
            out string suppressionReason)
        {
            suppressionReason = null;
            if (string.IsNullOrWhiteSpace(itemId) || entryState == null)
                return false;

            if (entryState.IsHero ||
                !string.Equals(entryState.CultureId, "looters", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            switch (itemId.Trim().ToLowerInvariant())
            {
                case "throwing_stone":
                    suppressionReason = "suppressed-looter-thrown-weapon1";
                    return true;
                case "sling_wool":
                case "sling_braided":
                    suppressionReason = "suppressed-looter-sling-weapon1";
                    return true;
                case "sling_stoneammo":
                    suppressionReason = "suppressed-looter-sling-ammo";
                    return true;
                default:
                    return false;
            }
        }

        private static void LogSuppressedMaterializedEquipmentItem(
            string itemId,
            RosterEntryState entryState,
            string slotLabel,
            string suppressionReason)
        {
            string logKey =
                (entryState?.EntryId ?? "(unknown)") + "|" +
                (slotLabel ?? "(slot)") + "|" +
                (itemId ?? "(item)") + "|" +
                (suppressionReason ?? "(reason)");
            if (!_loggedSuppressedMaterializedEquipmentKeys.Add(logKey))
                return;

            ModLogger.Info(
                "CoopMissionSpawnLogic: suppressed risky materialized equipment item for mission-safe non-hero runtime. " +
                "EntryId=" + (entryState?.EntryId ?? "(null)") +
                " TroopId=" + (entryState?.CharacterId ?? "(null)") +
                " Slot=" + (slotLabel ?? "(null)") +
                " ItemId=" + (itemId ?? "(null)") +
                " Reason=" + (suppressionReason ?? "(none)") +
                " MissionSafeCulture=" + (entryState?.CultureId ?? "(null)"));
        }

        private static void LogImportedEquipmentAvailabilityDiagnostics()
        {
            if (_hasLoggedImportedEquipmentAvailabilityDiagnostics)
                return;

            _hasLoggedImportedEquipmentAvailabilityDiagnostics = true;
            var diagnostics = new List<string>();
            var missing = new List<string>();
            foreach (string itemId in ImportedEquipmentProbeIds)
            {
                ItemObject item = TryGetMaterializedEquipmentItem(itemId);
                if (item == null)
                {
                    diagnostics.Add(itemId + "=missing");
                    missing.Add(itemId);
                    continue;
                }

                diagnostics.Add(itemId + "=" + item.StringId + ":" + item.Type);
            }

            ModLogger.Info(
                "CoopMissionSpawnLogic: imported equipment availability diagnostics. " +
                "ProbeCount=" + ImportedEquipmentProbeIds.Length +
                " Missing=" + missing.Count +
                " Loaded=" + (ImportedEquipmentProbeIds.Length - missing.Count));

            if (missing.Count > 0)
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: imported equipment probe missing direct ItemObject ids = [" +
                    string.Join(", ", missing) +
                    "].");
            }

            string[] aliasIds = ExactEquipmentCompatibilityAliasIds.Values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var missingAliases = new List<string>();
            foreach (string aliasId in aliasIds)
            {
                if (TryGetMaterializedEquipmentItem(aliasId) == null)
                    missingAliases.Add(aliasId);
            }

            ModLogger.Info(
                "CoopMissionSpawnLogic: compatibility alias availability diagnostics. " +
                "ProbeCount=" + aliasIds.Length +
                " Missing=" + missingAliases.Count +
                " Loaded=" + (aliasIds.Length - missingAliases.Count));

            if (missingAliases.Count > 0)
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: compatibility alias probe missing ItemObject ids = [" +
                    string.Join(", ", missingAliases) +
                    "].");
            }
        }

        private static void LogHeroEquipmentResolutionAudit()
        {
            if (_hasLoggedHeroEquipmentResolutionAudit)
                return;

            _hasLoggedHeroEquipmentResolutionAudit = true;
            BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
            if (runtimeState?.EntriesById == null || runtimeState.EntriesById.Count == 0)
                return;

            var heroEntries = runtimeState.EntriesById.Values
                .Where(entry => entry != null && entry.IsHero)
                .OrderBy(entry => entry.EntryId, StringComparer.Ordinal)
                .ToList();
            if (heroEntries.Count == 0)
                return;

            foreach (RosterEntryState heroEntry in heroEntries)
            {
                string audit =
                    BuildHeroEquipmentResolutionAuditForSlot(heroEntry, "Item0", heroEntry.CombatItem0Id) + "; " +
                    BuildHeroEquipmentResolutionAuditForSlot(heroEntry, "Item1", heroEntry.CombatItem1Id) + "; " +
                    BuildHeroEquipmentResolutionAuditForSlot(heroEntry, "Item2", heroEntry.CombatItem2Id) + "; " +
                    BuildHeroEquipmentResolutionAuditForSlot(heroEntry, "Item3", heroEntry.CombatItem3Id) + "; " +
                    BuildHeroEquipmentResolutionAuditForSlot(heroEntry, "Head", heroEntry.CombatHeadId) + "; " +
                    BuildHeroEquipmentResolutionAuditForSlot(heroEntry, "Body", heroEntry.CombatBodyId) + "; " +
                    BuildHeroEquipmentResolutionAuditForSlot(heroEntry, "Leg", heroEntry.CombatLegId) + "; " +
                    BuildHeroEquipmentResolutionAuditForSlot(heroEntry, "Gloves", heroEntry.CombatGlovesId) + "; " +
                    BuildHeroEquipmentResolutionAuditForSlot(heroEntry, "Cape", heroEntry.CombatCapeId) + "; " +
                    BuildHeroEquipmentResolutionAuditForSlot(heroEntry, "Horse", heroEntry.CombatHorseId) + "; " +
                    BuildHeroEquipmentResolutionAuditForSlot(heroEntry, "HorseHarness", heroEntry.CombatHorseHarnessId);

                ModLogger.Info(
                    "CoopMissionSpawnLogic: hero equipment resolution audit. " +
                    "EntryId=" + (heroEntry.EntryId ?? "null") +
                    " HeroId=" + (heroEntry.HeroId ?? "null") +
                    " HeroRole=" + (heroEntry.HeroRole ?? "null") +
                    " SpawnTemplate=" + (heroEntry.SpawnTemplateId ?? "null") +
                    " Audit=[" + audit + "].");
            }
        }

        private static string BuildHeroEquipmentResolutionAuditForSlot(
            RosterEntryState entryState,
            string slotLabel,
            string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return (slotLabel ?? "slot") + "=(none)";

            string normalizedItemId = null;
            bool normalizedCandidate =
                TryNormalizeCampaignEquipmentToMpItemId(itemId, entryState, slotLabel, out normalizedItemId) &&
                !string.IsNullOrWhiteSpace(normalizedItemId);
            string aliasItemId =
                ExactEquipmentCompatibilityAliasIds.TryGetValue(itemId, out string aliasCandidate) &&
                !string.IsNullOrWhiteSpace(aliasCandidate)
                    ? aliasCandidate
                    : null;
            string standInItemId =
                ExactEquipmentCompatibilityStandInItemIds.TryGetValue(itemId, out string standInCandidate) &&
                !string.IsNullOrWhiteSpace(standInCandidate)
                    ? standInCandidate
                    : null;
            string mpPrefixedItemId = itemId.StartsWith("mp_", StringComparison.Ordinal)
                ? null
                : "mp_" + itemId;

            bool direct = TryGetMaterializedEquipmentItem(itemId) != null;
            bool alias = !string.IsNullOrWhiteSpace(aliasItemId) && TryGetMaterializedEquipmentItem(aliasItemId) != null;
            bool standIn = !string.IsNullOrWhiteSpace(standInItemId) && TryGetMaterializedEquipmentItem(standInItemId) != null;
            bool mpPrefix = !string.IsNullOrWhiteSpace(mpPrefixedItemId) && TryGetMaterializedEquipmentItem(mpPrefixedItemId) != null;
            bool normalized = normalizedCandidate && TryGetMaterializedEquipmentItem(normalizedItemId) != null;

            string resolvedItemId;
            string resolutionSource;
            ItemObject resolvedItem = ResolveMaterializedEquipmentItem(
                itemId,
                entryState,
                slotLabel,
                out resolvedItemId,
                out resolutionSource,
                trackCoverage: false);

            return
                (slotLabel ?? "slot") + "=" + itemId +
                " -> " + (resolvedItem?.StringId ?? "unresolved") +
                "(via:" + (resolutionSource ?? "null") +
                ", direct=" + direct +
                ", alias=" + alias +
                ", standin=" + standIn +
                ", mpPrefix=" + mpPrefix +
                ", normalized=" + normalized +
                ", aliasId=" + (aliasItemId ?? "(none)") +
                ", standinId=" + (standInItemId ?? "(none)") +
                ", normalizedId=" + (normalizedItemId ?? "(none)") +
                ")";
        }

        private static void RecordMaterializedEquipmentResolution(string sourceItemId, string resolutionSource, bool trackCoverage)
        {
            if (!trackCoverage || string.IsNullOrWhiteSpace(resolutionSource))
                return;

            IncrementMaterializedEquipmentCounter(MaterializedEquipmentResolutionSourceCounts, resolutionSource);
            if (string.Equals(resolutionSource, "normalized", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(sourceItemId))
            {
                IncrementMaterializedEquipmentCounter(MaterializedEquipmentNormalizedFallbackCounts, sourceItemId);
            }
        }

        private static void RecordMaterializedEquipmentMiss(string sourceItemId, bool trackCoverage)
        {
            if (!trackCoverage)
                return;

            IncrementMaterializedEquipmentCounter(MaterializedEquipmentResolutionSourceCounts, "miss");
            if (!string.IsNullOrWhiteSpace(sourceItemId))
                IncrementMaterializedEquipmentCounter(MaterializedEquipmentMissCounts, sourceItemId);
        }

        private static void IncrementMaterializedEquipmentCounter(Dictionary<string, int> counters, string key)
        {
            if (counters == null || string.IsNullOrWhiteSpace(key))
                return;

            if (counters.TryGetValue(key, out int currentCount))
                counters[key] = currentCount + 1;
            else
                counters[key] = 1;
        }

        private static void LogMaterializedEquipmentCoverageSummaryIfNeeded()
        {
            if (_hasLoggedMaterializedEquipmentCoverageSummary)
                return;

            if (!IsSyntheticRuntime() && !HasLiveCombatProfileCoverageSignal())
                return;

            bool hasCoverageData =
                (MaterializedEquipmentResolutionSourceCounts != null && MaterializedEquipmentResolutionSourceCounts.Count > 0) ||
                (MaterializedEquipmentMissCounts != null && MaterializedEquipmentMissCounts.Count > 0) ||
                (MaterializedEquipmentNormalizedFallbackCounts != null && MaterializedEquipmentNormalizedFallbackCounts.Count > 0) ||
                (MaterializedCombatProfileApplyCounts != null && MaterializedCombatProfileApplyCounts.Count > 0);
            if (!hasCoverageData)
                return;

            _hasLoggedMaterializedEquipmentCoverageSummary = true;

            string resolutionCounts = FormatMaterializedEquipmentCounterSummary(MaterializedEquipmentResolutionSourceCounts, 8);
            string topMisses = FormatMaterializedEquipmentCounterSummary(MaterializedEquipmentMissCounts, 20);
            string topNormalizedFallbacks = FormatMaterializedEquipmentCounterSummary(MaterializedEquipmentNormalizedFallbackCounts, 20);
            string combatProfileCounts = FormatMaterializedEquipmentCounterSummary(MaterializedCombatProfileApplyCounts, 20);
            string modeLabel = IsSyntheticRuntime() ? "synthetic" : "live";

            ModLogger.Info(
                "CoopMissionSpawnLogic: " + modeLabel + " equipment coverage summary. " +
                "ResolutionCounts=[" + resolutionCounts + "] " +
                "CombatProfileCounts=[" + combatProfileCounts + "] " +
                "TopMisses=[" + topMisses + "] " +
                "TopNormalizedFallbacks=[" + topNormalizedFallbacks + "]");
        }

        private static bool HasLiveCombatProfileCoverageSignal()
        {
            if (MaterializedCombatProfileApplyCounts == null || MaterializedCombatProfileApplyCounts.Count == 0)
                return false;

            foreach (KeyValuePair<string, int> entry in MaterializedCombatProfileApplyCounts)
            {
                if (entry.Value <= 0 || string.IsNullOrWhiteSpace(entry.Key))
                    continue;

                if (!string.Equals(entry.Key, "registered", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(entry.Key, "perks", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(entry.Key, "mount", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(entry.Key, "perk-mount", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string FormatMaterializedEquipmentCounterSummary(Dictionary<string, int> counters, int take)
        {
            if (counters == null || counters.Count == 0)
                return "(none)";

            return string.Join(
                ", ",
                counters
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Take(Math.Max(1, take))
                    .Select(pair => pair.Key + "=" + pair.Value));
        }

        private static ItemObject TryGetMaterializedEquipmentItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return null;

            try
            {
                return MBObjectManager.Instance?.GetObject<ItemObject>(itemId);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryNormalizeCampaignEquipmentToMpItemId(
            string itemId,
            RosterEntryState entryState,
            string slotLabel,
            out string normalizedItemId)
        {
            normalizedItemId = null;
            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            switch (itemId)
            {
                case "leather_cap":
                    normalizedItemId = "mp_leather_cap";
                    return true;
                case "empire_horseman_boots":
                    normalizedItemId = "mp_empire_horseman_boots";
                    return true;
                case "highland_boots":
                    normalizedItemId = "mp_highland_boots";
                    return true;
                case "scarf":
                    normalizedItemId = "mp_scarf";
                    return true;
                case "wrapped_shoes":
                    normalizedItemId = "mp_woven_leather_boots";
                    return true;
                case "tunic_with_shoulder_pads":
                    normalizedItemId = "mp_empire_short_dress";
                    return true;
                case "bandit_envelope_dress_v1":
                    normalizedItemId = "mp_empire_short_dress";
                    return true;
                case "northern_leather_vest":
                    normalizedItemId = "mp_basic_imperial_leather_armor";
                    return true;
                case "sumpter_horse":
                    normalizedItemId = "mp_battania_pony";
                    return true;
                case "worn_kite_shield":
                    normalizedItemId = "mp_worn_kite_shield";
                    return true;
                case "peasant_pitchfork_2_t1":
                    normalizedItemId = "mp_western_pitchfork_wood";
                    return true;
                case "seax":
                    normalizedItemId = "mp_default_dagger";
                    return true;
                case "torn_bandit_clothes":
                    normalizedItemId = "mp_vlandia_bandit_c";
                    return true;
                case "peasant_2haxe_1_t1":
                    normalizedItemId = "mp_hatchet_axe";
                    return true;
                case "sling_wool":
                    normalizedItemId = "mp_sling_stone";
                    return true;
                case "sling_stoneammo":
                    normalizedItemId = "mp_sling_stone";
                    return true;
                case "sturgia_axe_2_t2":
                    normalizedItemId = "mp_sturgia_axe";
                    return true;
                case "northern_spear_1_t2":
                    normalizedItemId = "mp_sturgia_spear";
                    return true;
                case "battania_sword_1_t2":
                    normalizedItemId = "mp_battania_long_sword";
                    return true;
                case "empire_noble_sword_3_t5":
                    normalizedItemId = "mp_empire_paramerion";
                    return true;
                case "empire_sword_3_t3":
                    normalizedItemId = "mp_empire_paramerion";
                    return true;
                case "aserai_sword_1_t2":
                case "aserai_sword_2_t2":
                case "aserai_sword_3_t3":
                case "aserai_sword_4_t4":
                case "desert_long_sword_t4":
                case "aserai_noble_sword_2_t5":
                    normalizedItemId = "mp_aserai_sword";
                    return true;
                case "peasant_hammer_2_t1":
                    normalizedItemId = "mp_aserai_heavy_mace";
                    return true;
                case "noble_horse_southern":
                case "war_camel":
                    normalizedItemId = "mp_aserai_horse_war";
                    return true;
                case "noble_horse_imperial":
                    normalizedItemId = "mp_empire_horse_war";
                    return true;
                case "mail_and_plate_barding":
                    normalizedItemId = "mp_mail_and_plate_barding";
                    return true;
                case "aserai_chain_plate_armor_d":
                    normalizedItemId = "mp_aserai_robe_c_chain";
                    return true;
                case "leatherlame_roundkettle_over_imperial_leather":
                    normalizedItemId = "mp_roundkettle_over_imperial_leather";
                    return true;
                default:
                    return TryNormalizeCampaignWeaponPatternToMpItemId(itemId, entryState, slotLabel, out normalizedItemId);
            }
        }

        private static bool TryNormalizeCampaignWeaponPatternToMpItemId(
            string itemId,
            RosterEntryState entryState,
            string slotLabel,
            out string normalizedItemId)
        {
            normalizedItemId = null;
            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            string normalized = itemId.Trim().ToLowerInvariant();
            string cultureId = entryState?.CultureId?.Trim().ToLowerInvariant();
            string spawnTemplateId = entryState?.SpawnTemplateId?.Trim().ToLowerInvariant();
            bool isMounted = entryState?.IsMounted == true;
            bool isHeavyRanged = !string.IsNullOrWhiteSpace(spawnTemplateId) &&
                (spawnTemplateId.Contains("heavy_ranged") || spawnTemplateId.Contains("crossbow"));
            bool isHorseArcher = !string.IsNullOrWhiteSpace(spawnTemplateId) &&
                spawnTemplateId.Contains("horse_archer");
            bool isLightRanged = !string.IsNullOrWhiteSpace(spawnTemplateId) &&
                spawnTemplateId.Contains("light_ranged");

            if (normalized.Contains("crossbow"))
            {
                normalizedItemId = normalized.Contains("light") || isLightRanged
                    ? "mp_light_crossbow"
                    : "mp_crossbow";
                return true;
            }

            if (normalized.Contains("bolt"))
            {
                if (string.Equals(cultureId, "empire", StringComparison.Ordinal))
                {
                    normalizedItemId = "mp_bolts_imperial";
                    return true;
                }

                if (string.Equals(cultureId, "vlandia", StringComparison.Ordinal))
                {
                    normalizedItemId = "mp_bolts_western";
                    return true;
                }

                normalizedItemId = normalized.Contains("empire") || normalized.Contains("imperial")
                    ? "mp_bolts_imperial"
                    : "mp_bolts_western";
                return true;
            }

            if (normalized.Contains("bow"))
            {
                if (string.Equals(cultureId, "khuzait", StringComparison.Ordinal))
                {
                    normalizedItemId = "mp_nomad_bow";
                    return true;
                }

                if (string.Equals(cultureId, "battania", StringComparison.Ordinal))
                {
                    normalizedItemId = isHeavyRanged ? "mp_long_bow" : "mp_mountain_hunting_bow";
                    return true;
                }

                if (string.Equals(cultureId, "sturgia", StringComparison.Ordinal))
                {
                    normalizedItemId = "mp_nordic_short_bow";
                    return true;
                }

                if (string.Equals(cultureId, "aserai", StringComparison.Ordinal))
                {
                    normalizedItemId = "mp_imperial_recurve_bow";
                    return true;
                }

                if (string.Equals(cultureId, "empire", StringComparison.Ordinal))
                {
                    normalizedItemId = "mp_imperial_recurve_bow";
                    return true;
                }

                if (normalized.Contains("nomad") || normalized.Contains("steppe") || normalized.Contains("composite"))
                {
                    normalizedItemId = "mp_nomad_bow";
                    return true;
                }

                if (normalized.Contains("empire") || normalized.Contains("imperial") || normalized.Contains("recurve") || normalized.Contains("desert"))
                {
                    normalizedItemId = "mp_imperial_recurve_bow";
                    return true;
                }

                if (normalized.Contains("mountain"))
                {
                    normalizedItemId = "mp_mountain_hunting_bow";
                    return true;
                }

                if (normalized.Contains("nordic") || normalized.Contains("sturgia"))
                {
                    normalizedItemId = "mp_nordic_short_bow";
                    return true;
                }

                if (normalized.Contains("longbow") ||
                    normalized.Contains("long_bow") ||
                    normalized.Contains("woodland") ||
                    normalized.Contains("lowland") ||
                    normalized.Contains("highland") ||
                    normalized.Contains("yew") ||
                    normalized.Contains("ranger") ||
                    normalized.Contains("tribal") ||
                    normalized.Contains("battania"))
                {
                    normalizedItemId = "mp_long_bow";
                    return true;
                }

                normalizedItemId = "mp_hunting_bow";
                return true;
            }

            if (normalized.Contains("arrow"))
            {
                if (string.Equals(cultureId, "khuzait", StringComparison.Ordinal))
                {
                    normalizedItemId = isMounted || isHorseArcher
                        ? "mp_arrows_steppe_mounted"
                        : "mp_arrows_steppe";
                    return true;
                }

                if (string.Equals(cultureId, "battania", StringComparison.Ordinal) ||
                    string.Equals(cultureId, "sturgia", StringComparison.Ordinal))
                {
                    normalizedItemId = "mp_arrows_bodkin";
                    return true;
                }

                if (string.Equals(cultureId, "empire", StringComparison.Ordinal) ||
                    string.Equals(cultureId, "aserai", StringComparison.Ordinal))
                {
                    normalizedItemId = "mp_arrows_barbed";
                    return true;
                }

                if (normalized.Contains("steppe"))
                {
                    normalizedItemId = isMounted || isHorseArcher
                        ? "mp_arrows_steppe_mounted"
                        : "mp_arrows_steppe";
                    return true;
                }

                if (normalized.Contains("barbed"))
                {
                    normalizedItemId = "mp_arrows_barbed";
                    return true;
                }

                if (normalized.Contains("bodkin") || normalized.Contains("piercing"))
                {
                    normalizedItemId = "mp_arrows_bodkin";
                    return true;
                }

                normalizedItemId = "mp_arrows_barbed";
                return true;
            }

            if (normalized.Contains("javelin"))
            {
                if (string.Equals(cultureId, "aserai", StringComparison.Ordinal))
                {
                    normalizedItemId = "mp_javelin";
                    return true;
                }

                if (normalized.Contains("empire") || normalized.Contains("imperial"))
                {
                    normalizedItemId = "mp_empire_javelin";
                    return true;
                }

                if (normalized.Contains("battania"))
                {
                    normalizedItemId = "mp_battania_javelin";
                    return true;
                }

                if (normalized.Contains("light"))
                {
                    normalizedItemId = "mp_light_javelin";
                    return true;
                }

                normalizedItemId = "mp_javelin";
                return true;
            }

            if (normalized.Contains("spear") || normalized.Contains("lance"))
            {
                if (string.Equals(cultureId, "aserai", StringComparison.Ordinal))
                {
                    normalizedItemId = entryState?.IsMounted == true
                        ? "mp_aserai_long_spear"
                        : "mp_southern_spear";
                    return true;
                }

                if (normalized.Contains("southern"))
                {
                    normalizedItemId = "mp_southern_spear";
                    return true;
                }
            }

            if (normalized.Contains("stone"))
            {
                normalizedItemId = normalized.Contains("sling")
                    ? "mp_sling_stone"
                    : "mp_throwing_stone";
                return true;
            }

            return false;
        }

        private static string BuildMaterializedEquipmentOverrideDiagnostics(RosterEntryState entryState, BasicCharacterObject troop)
        {
            Equipment spawnEquipment = troop?.Equipment?.Clone(false);
            var missedSlots = new List<string>();
            string applied = TryApplyMaterializedEquipmentOverrides(spawnEquipment, entryState, missedSlots, trackCoverage: false);
            string misses = missedSlots.Count > 0 ? string.Join(", ", missedSlots) : "(none)";
            return "AppliedEquipmentOverrides=" + applied + " EquipmentOverrideMisses=" + misses;
        }

        private static void TryTakeControlOfMaterializedArmyAgents(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer || GameNetwork.NetworkPeers == null || GameNetwork.NetworkPeers.Count == 0)
                return;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
                    continue;

                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                if (missionPeer == null || missionPeer.ControlledAgent != null)
                    continue;

                if (!CoopBattleSpawnRequestState.TryGetPendingRequest(missionPeer, out CoopBattleSpawnRequestState.PeerSpawnRequestState pendingRequest))
                    continue;

                BattleSideEnum authoritativeSide = ResolveAuthoritativeSide(missionPeer, mission, source + " army-possession");
                if (authoritativeSide == BattleSideEnum.None)
                    continue;

                Agent candidateAgent = FindEligibleMaterializedArmyAgent(mission, authoritativeSide, pendingRequest.EntryId, pendingRequest.TroopId);
                if (candidateAgent == null)
                {
                    candidateAgent = TrySeedExactScenePossessionCandidate(
                        mission,
                        missionPeer,
                        peer,
                        authoritativeSide,
                        pendingRequest,
                        source + " army-possession");
                }

                if (candidateAgent == null)
                {
                    LogMaterializedArmyPossessionCandidateMiss(mission, peer, authoritativeSide, pendingRequest, source);
                    continue;
                }

                if (!TryReplaceMaterializedBotWithPlayer(mission, missionPeer, peer, candidateAgent, pendingRequest, source + " army-possession"))
                    continue;

                _spawnedCoopPeerIndices.Add(peer.Index);
                ExpireMissionPeerVanillaSpawnVisuals(missionPeer);
                missionPeer.WantsToSpawnAsBot = false;
                CoopBattleSpawnRequestState.Clear(missionPeer, source + " army-possession");
                CoopBattleSpawnRuntimeState.MarkSpawned(missionPeer, pendingRequest.TroopId, pendingRequest.EntryId, source + " army-possession");
                CoopBattlePeerLifecycleRuntimeState.MarkAlive(missionPeer, pendingRequest.TroopId, pendingRequest.EntryId, source + " army-possession");

                ModLogger.Info(
                    "CoopMissionSpawnLogic: possessed materialized army agent via vanilla replace-bot flow. " +
                    "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                    " Side=" + authoritativeSide +
                    " TroopId=" + (pendingRequest.TroopId ?? "null") +
                    " EntryId=" + (pendingRequest.EntryId ?? "null") +
                    " AgentIndex=" + candidateAgent.Index +
                    " Source=" + (source ?? "unknown"));
            }
        }

        private static Agent FindEligibleMaterializedArmyAgent(Mission mission, BattleSideEnum side, string entryId, string troopId)
        {
            if (mission?.AllAgents == null)
                return null;

            Agent troopCandidate = null;
            Agent nativeTroopCandidate = null;
            for (int i = 0; i < mission.AllAgents.Count; i++)
            {
                Agent candidate = mission.AllAgents[i];
                if (candidate == null ||
                    !candidate.IsActive() ||
                    candidate.IsMount ||
                    candidate.MissionPeer != null ||
                    candidate.Controller == AgentControllerType.Player)
                {
                    continue;
                }

                bool trackedMaterialized =
                    _materializedArmySideByAgentIndex.TryGetValue(candidate.Index, out BattleSideEnum candidateSide) &&
                    candidateSide == side;
                bool nativeBootstrapMaterialized =
                    ExactCampaignArmyBootstrap.TryGetSide(candidate, out BattleSideEnum originSide) &&
                    originSide == side;
                if (!trackedMaterialized && !nativeBootstrapMaterialized && candidate.Team?.Side != side)
                    continue;

                if (!string.IsNullOrWhiteSpace(entryId) &&
                    ((_materializedArmyEntryIdByAgentIndex.TryGetValue(candidate.Index, out string candidateEntryId) &&
                      string.Equals(candidateEntryId, entryId, StringComparison.Ordinal)) ||
                     (ExactCampaignArmyBootstrap.TryGetEntryId(candidate, out string originEntryId) &&
                      string.Equals(originEntryId, entryId, StringComparison.Ordinal))))
                {
                    return candidate;
                }

                string candidateTroopId = (candidate.Character as BasicCharacterObject)?.StringId;
                if (troopCandidate == null &&
                    trackedMaterialized &&
                    !string.IsNullOrWhiteSpace(troopId) &&
                    string.Equals(candidateTroopId, troopId, StringComparison.OrdinalIgnoreCase))
                {
                    troopCandidate = candidate;
                }

                if (nativeTroopCandidate == null &&
                    nativeBootstrapMaterialized &&
                    !string.IsNullOrWhiteSpace(troopId) &&
                    string.Equals(candidateTroopId, troopId, StringComparison.OrdinalIgnoreCase))
                {
                    nativeTroopCandidate = candidate;
                }
            }

            return troopCandidate ?? nativeTroopCandidate;
        }

        private static void LogMaterializedArmyPossessionCandidateMiss(
            Mission mission,
            NetworkCommunicator peer,
            BattleSideEnum side,
            CoopBattleSpawnRequestState.PeerSpawnRequestState pendingRequest,
            string source)
        {
            if (mission?.AllAgents == null)
                return;

            int totalSideCandidates = 0;
            int entryMatches = 0;
            int troopMatches = 0;
            for (int i = 0; i < mission.AllAgents.Count; i++)
            {
                Agent candidate = mission.AllAgents[i];
                if (candidate == null || !candidate.IsActive() || candidate.IsMount)
                    continue;

                bool trackedMaterialized =
                    _materializedArmySideByAgentIndex.TryGetValue(candidate.Index, out BattleSideEnum candidateSide) &&
                    candidateSide == side;
                bool nativeBootstrapMaterialized =
                    ExactCampaignArmyBootstrap.TryGetSide(candidate, out BattleSideEnum originSide) &&
                    originSide == side;
                if (!trackedMaterialized && !nativeBootstrapMaterialized && candidate.Team?.Side != side)
                    continue;

                totalSideCandidates++;
                if (!string.IsNullOrWhiteSpace(pendingRequest.EntryId) &&
                    ((_materializedArmyEntryIdByAgentIndex.TryGetValue(candidate.Index, out string candidateEntryId) &&
                      string.Equals(candidateEntryId, pendingRequest.EntryId, StringComparison.Ordinal)) ||
                     (ExactCampaignArmyBootstrap.TryGetEntryId(candidate, out string originEntryId) &&
                      string.Equals(originEntryId, pendingRequest.EntryId, StringComparison.Ordinal))))
                {
                    entryMatches++;
                }

                string candidateTroopId = (candidate.Character as BasicCharacterObject)?.StringId;
                if (!string.IsNullOrWhiteSpace(pendingRequest.TroopId) &&
                    string.Equals(candidateTroopId, pendingRequest.TroopId, StringComparison.OrdinalIgnoreCase))
                {
                    troopMatches++;
                }
            }

            ModLogger.Info(
                "CoopMissionSpawnLogic: materialized army possession found no eligible candidate. " +
                "Peer=" + (peer?.UserName ?? peer?.Index.ToString() ?? "none") +
                " Side=" + side +
                " EntryId=" + (pendingRequest.EntryId ?? "null") +
                " TroopId=" + (pendingRequest.TroopId ?? "null") +
                " SideCandidates=" + totalSideCandidates +
                " EntryMatches=" + entryMatches +
                " TroopMatches=" + troopMatches +
                " Source=" + (source ?? "unknown"));
        }

        private static bool ShouldSeedExactScenePossessionCandidate(Mission mission, BattleSideEnum side)
        {
            if (mission == null || _hasMaterializedBattlefieldArmies || !IsSceneAwareBattleMapRuntime(mission))
                return false;

            if (IsUsingNativeExactCampaignArmyBootstrap(mission))
                return false;

            if (!SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty))
                return false;

            return GetCurrentMaterializedSpawnCountForSide(side) <= 0;
        }

        private static Agent TrySeedExactScenePossessionCandidate(
            Mission mission,
            MissionPeer missionPeer,
            NetworkCommunicator peer,
            BattleSideEnum side,
            CoopBattleSpawnRequestState.PeerSpawnRequestState pendingRequest,
            string source)
        {
            if (!ShouldSeedExactScenePossessionCandidate(mission, side) ||
                missionPeer == null)
            {
                return null;
            }

            if (!TryResolveAuthoritativeCharacterForPeer(
                missionPeer,
                out BasicCharacterObject selectedCharacter,
                out string selectedTroopId,
                out string selectedEntryId,
                out string resolveReason))
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: exact-scene on-demand possession seed skipped because authoritative character resolution failed. " +
                    "Peer=" + (peer?.UserName ?? peer?.Index.ToString() ?? "none") +
                    " Side=" + side +
                    " PendingTroopId=" + (pendingRequest.TroopId ?? "null") +
                    " PendingEntryId=" + (pendingRequest.EntryId ?? "null") +
                    " Reason=" + resolveReason +
                    " Source=" + (source ?? "unknown"));
                return null;
            }

            Team team = ResolveAuthoritativeMissionTeam(mission, missionPeer, side);
            if (team == null)
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: exact-scene on-demand possession seed skipped because authoritative team was unavailable. " +
                    "Peer=" + (peer?.UserName ?? peer?.Index.ToString() ?? "none") +
                    " Side=" + side +
                    " PendingTroopId=" + (pendingRequest.TroopId ?? "null") +
                    " PendingEntryId=" + (pendingRequest.EntryId ?? "null") +
                    " Source=" + (source ?? "unknown"));
                return null;
            }

            RosterEntryState selectedEntryState = !string.IsNullOrWhiteSpace(selectedEntryId)
                ? BattleSnapshotRuntimeState.GetEntryState(selectedEntryId)
                : null;
            int sideOffset = GetCurrentMaterializedSpawnCountForSide(side);
            int spawned = SpawnMaterializedAgentsForEntry(
                mission,
                team,
                side,
                selectedCharacter,
                selectedEntryState,
                1,
                sideOffset,
                source + " exact-scene-on-demand-seed");
            if (spawned <= 0)
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: exact-scene on-demand possession seed produced no candidate. " +
                    "Peer=" + (peer?.UserName ?? peer?.Index.ToString() ?? "none") +
                    " Side=" + side +
                    " SelectedTroopId=" + (selectedTroopId ?? pendingRequest.TroopId ?? "null") +
                    " SelectedEntryId=" + (selectedEntryId ?? pendingRequest.EntryId ?? "null") +
                    " Source=" + (source ?? "unknown"));
                return null;
            }

            Agent seededCandidate = FindEligibleMaterializedArmyAgent(
                mission,
                side,
                selectedEntryId ?? pendingRequest.EntryId,
                selectedTroopId ?? pendingRequest.TroopId);
            ModLogger.Info(
                "CoopMissionSpawnLogic: exact-scene on-demand possession seed completed. " +
                "Peer=" + (peer?.UserName ?? peer?.Index.ToString() ?? "none") +
                " Side=" + side +
                " SelectedTroopId=" + (selectedTroopId ?? pendingRequest.TroopId ?? "null") +
                " SelectedEntryId=" + (selectedEntryId ?? pendingRequest.EntryId ?? "null") +
                " SeededCandidate=" + (seededCandidate?.Index.ToString() ?? "null") +
                " Source=" + (source ?? "unknown"));
            return seededCandidate;
        }

        private static void TryAlignControlledAgentsWithMaterializedArmy(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer || GameNetwork.NetworkPeers == null || !_hasMaterializedBattlefieldArmies)
                return;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
                    continue;

                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                Agent controlledAgent = missionPeer?.ControlledAgent;
                if (missionPeer == null || controlledAgent == null || !controlledAgent.IsActive())
                    continue;

                if (IsMaterializedArmyAgent(controlledAgent))
                    continue;

                if (_lastAlignedControlledAgentIndexByPeer.TryGetValue(peer.Index, out int lastAlignedAgentIndex) &&
                    lastAlignedAgentIndex == controlledAgent.Index)
                {
                    continue;
                }

                if (CoopBattleSpawnRequestState.HasPendingRequest(missionPeer))
                    continue;

                BattleSideEnum authoritativeSide = ResolveAuthoritativeSide(missionPeer, mission, source + " align");
                if (authoritativeSide == BattleSideEnum.None)
                    continue;

                string selectedEntryId = CoopBattleAuthorityState.GetSelectedEntryId(missionPeer);
                string selectedTroopId = CoopBattleAuthorityState.GetSelectedTroopId(missionPeer);
                Agent anchorAgent = FindEligibleMaterializedArmyAgent(mission, authoritativeSide, selectedEntryId, selectedTroopId);
                if (anchorAgent == null || !anchorAgent.IsActive())
                    continue;

                Vec2 facing = anchorAgent.LookDirection.AsVec2;
                if (facing.LengthSquared < 0.001f)
                    facing = authoritativeSide == BattleSideEnum.Attacker ? new Vec2(1f, 0f) : new Vec2(-1f, 0f);
                facing.Normalize();

                Vec3 forward = new Vec3(facing.x, facing.y, 0f);
                Vec3 right = new Vec3(-facing.y, facing.x, 0f);
                Vec3 alignedPosition = anchorAgent.Position + right * 2.2f - forward * 1.5f;

                try
                {
                    controlledAgent.MountAgent?.TeleportToPosition(alignedPosition);
                    controlledAgent.TeleportToPosition(alignedPosition);
                    _lastAlignedControlledAgentIndexByPeer[peer.Index] = controlledAgent.Index;
                    ModLogger.Info(
                        "CoopMissionSpawnLogic: aligned controlled agent with materialized army. " +
                        "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                        " AgentIndex=" + controlledAgent.Index +
                        " Side=" + authoritativeSide +
                        " EntryId=" + (selectedEntryId ?? "null") +
                        " TroopId=" + (selectedTroopId ?? "null") +
                        " AnchorAgentIndex=" + anchorAgent.Index +
                        " Source=" + (source ?? "unknown"));
                }
                catch (Exception ex)
                {
                    ModLogger.Info("CoopMissionSpawnLogic: failed to align controlled agent with materialized army: " + ex.Message);
                }
            }
        }

        private static bool TryReplaceMaterializedBotWithPlayer(
            Mission mission,
            MissionPeer missionPeer,
            NetworkCommunicator peer,
            Agent targetAgent,
            CoopBattleSpawnRequestState.PeerSpawnRequestState pendingRequest,
            string source)
        {
            if (mission == null || missionPeer == null || peer == null || targetAgent == null || !targetAgent.IsActive())
                return false;

            MissionMultiplayerGameModeBase gameMode = mission.GetMissionBehavior<MissionMultiplayerGameModeBase>();
            Formation targetFormation = targetAgent.Formation;
            if (targetFormation == null)
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: skipped materialized army possession because target agent has no formation. " +
                    "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                    " AgentIndex=" + targetAgent.Index +
                    " Source=" + (source ?? "unknown"));
                return false;
            }

            try
            {
                RosterEntryState targetEntryState = ResolveMaterializedEntryStateForPossessedAgent(targetAgent, pendingRequest);
                Team peerTeam = missionPeer.Team ?? targetAgent.Team;
                if (peerTeam != null && !ReferenceEquals(targetAgent.Team, peerTeam))
                {
                    targetAgent.SetTeam(peerTeam, false);
                    targetAgent.ForceUpdateCachedAndFormationValues(true, false);
                }

                int activeAiUnitsInFormation = 0;
                targetFormation.ApplyActionOnEachUnit(agent =>
                {
                    if (agent != null && agent.IsActive() && agent.IsAIControlled)
                    {
                        activeAiUnitsInFormation++;
                    }
                });
                activeAiUnitsInFormation = Math.Max(activeAiUnitsInFormation, 1);

                missionPeer.ControlledFormation = targetFormation;
                TrySetBotsUnderControlTotal(missionPeer, Math.Max(missionPeer.BotsUnderControlTotal, activeAiUnitsInFormation));
                missionPeer.BotsUnderControlAlive = Math.Max(missionPeer.BotsUnderControlAlive, activeAiUnitsInFormation);
                missionPeer.FollowedAgent = targetAgent;
                missionPeer.SpawnCountThisRound = Math.Max(missionPeer.SpawnCountThisRound, 1);

                TryEnsureVanillaSpawnGoldFloor(gameMode, peer, missionPeer, source + " replace-bot");
                Agent replacedAgent = mission.ReplaceBotWithPlayer(targetAgent, missionPeer);
                if (replacedAgent == null)
                {
                    ModLogger.Info(
                        "CoopMissionSpawnLogic: materialized army possession replace-bot returned null. " +
                        "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                        " AgentIndex=" + targetAgent.Index +
                        " Formation=" + targetFormation.FormationIndex +
                        " Source=" + (source ?? "unknown"));
                    return false;
                }

                if (!ReferenceEquals(missionPeer.ControlledAgent, replacedAgent))
                    missionPeer.ControlledAgent = replacedAgent;
                if (peer.VirtualPlayer != null)
                {
                    GameNetwork.BeginModuleEventAsServer(peer.VirtualPlayer);
                    GameNetwork.WriteMessage(new NetworkMessages.FromServer.SetAgentOwningMissionPeer(replacedAgent.Index, peer.VirtualPlayer));
                    GameNetwork.EndModuleEventAsServer();
                }
                missionPeer.FollowedAgent = replacedAgent;
                TransferMaterializedAgentRuntimeState(targetAgent, replacedAgent);
                string reappliedAgentState = ReapplyMaterializedAgentStateAfterReplaceBot(replacedAgent, targetEntryState);

                string commanderControlState = TryPromoteExactCampaignCommanderPeerToGeneralControl(
                    mission,
                    missionPeer,
                    peer,
                    replacedAgent,
                    pendingRequest,
                    source + " replace-bot");

                TryApplyVanillaSpawnGoldDeduction(gameMode, peer, missionPeer, source + " replace-bot");
                bool removedPendingVisuals = TryRemovePendingAgentVisuals(mission, missionPeer);
                ExpireMissionPeerVanillaSpawnVisuals(missionPeer);
                if (IsSceneAwareBattleMapRuntime(mission) &&
                    SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty))
                {
                    _exactScenePostPossessionMaterializationResumeUtc =
                        DateTime.UtcNow.AddSeconds(ExactScenePostPossessionMaterializationDelaySeconds);
                }

                ModLogger.Info(
                    "CoopMissionSpawnLogic: materialized army replace-bot succeeded. " +
                    "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                    " AgentIndex=" + replacedAgent.Index +
                    " Formation=" + targetFormation.FormationIndex +
                    " ActiveAiUnitsInFormation=" + activeAiUnitsInFormation +
                    " PendingTroopId=" + (pendingRequest.TroopId ?? "null") +
                    " PendingEntryId=" + (pendingRequest.EntryId ?? "null") +
                    " " + commanderControlState +
                    " " + reappliedAgentState +
                    " RemovedPendingVisuals=" + removedPendingVisuals +
                    " Source=" + (source ?? "unknown"));
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: materialized army replace-bot failed (" + source + "): " + ex.Message);
                return false;
            }
        }

        private static RosterEntryState ResolveMaterializedEntryStateForPossessedAgent(
            Agent targetAgent,
            CoopBattleSpawnRequestState.PeerSpawnRequestState pendingRequest)
        {
            string entryId = pendingRequest.EntryId;
            if (string.IsNullOrWhiteSpace(entryId) &&
                targetAgent != null &&
                _materializedArmyEntryIdByAgentIndex.TryGetValue(targetAgent.Index, out string mappedEntryId))
            {
                entryId = mappedEntryId;
            }

            if (string.IsNullOrWhiteSpace(entryId))
                return null;

            return BattleSnapshotRuntimeState.GetEntryState(entryId);
        }

        private static string TryPromoteExactCampaignCommanderPeerToGeneralControl(
            Mission mission,
            MissionPeer missionPeer,
            NetworkCommunicator peer,
            Agent controlledAgent,
            CoopBattleSpawnRequestState.PeerSpawnRequestState pendingRequest,
            string source)
        {
            if (mission == null ||
                missionPeer == null ||
                controlledAgent == null ||
                !controlledAgent.IsActive() ||
                !IsSceneAwareBattleMapRuntime(mission) ||
                !SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty))
            {
                return "CommanderControl=(captain)";
            }

            try
            {
                BattleSideEnum authoritativeSide = ResolveAuthoritativeSide(missionPeer, mission, source + " general-bridge");
                if (authoritativeSide == BattleSideEnum.None)
                    return "CommanderControl=(captain)";

                BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
                RosterEntryState commanderEntry = BattleCommanderResolver.ResolveCommanderEntry(runtimeState, authoritativeSide);
                if (commanderEntry == null || string.IsNullOrWhiteSpace(commanderEntry.EntryId))
                    return "CommanderControl=(captain)";

                string possessedEntryId = pendingRequest.EntryId;
                if (string.IsNullOrWhiteSpace(possessedEntryId) &&
                    !ExactCampaignArmyBootstrap.TryGetEntryId(controlledAgent, out possessedEntryId))
                {
                    possessedEntryId = null;
                }

                if (!DoesPossessedEntryMatchCommanderEntry(commanderEntry.EntryId, possessedEntryId))
                    return "CommanderControl=(captain)";

                Team team = missionPeer.Team ?? controlledAgent.Team;
                if (team == null)
                    return "CommanderControl=(captain)";

                team.SetPlayerRole(isPlayerGeneral: true, isPlayerSergeant: false);

                OrderController commanderOrderController = team.GetOrderControllerOf(controlledAgent);
                if (commanderOrderController != null)
                    commanderOrderController.Owner = controlledAgent;

                if (team.PlayerOrderController != null)
                    team.PlayerOrderController.Owner = controlledAgent;

                controlledAgent.SetCanLeadFormationsRemotely(value: true);
                if (!ReferenceEquals(team.GeneralAgent, controlledAgent))
                    team.GeneralAgent = controlledAgent;

                Formation controlledFormation = controlledAgent.Formation;
                foreach (Formation formation in team.FormationsIncludingEmpty)
                {
                    if (formation == null || !ReferenceEquals(formation.Team, team))
                        continue;

                    SetServerMemberValue(formation, "PlayerOwner", controlledAgent);
                    bool isControlledFormation = ReferenceEquals(formation, controlledFormation);
                    SetServerMemberValue(formation, "HasPlayerControlledTroop", isControlledFormation);
                    SetServerMemberValue(formation, "IsPlayerTroopInFormation", isControlledFormation);
                }

                int controlledBotTotal = 0;
                int controlledBotAlive = 0;
                if (mission.AllAgents != null)
                {
                    for (int i = 0; i < mission.AllAgents.Count; i++)
                    {
                        Agent candidate = mission.AllAgents[i];
                        if (candidate == null ||
                            candidate.IsMount ||
                            !ReferenceEquals(candidate.Team, team) ||
                            ReferenceEquals(candidate, controlledAgent) ||
                            candidate.MissionPeer != null ||
                            candidate.Controller == AgentControllerType.Player)
                        {
                            continue;
                        }

                        controlledBotTotal++;
                        if (candidate.IsActive())
                            controlledBotAlive++;
                    }
                }

                TrySetBotsUnderControlTotal(missionPeer, controlledBotTotal);
                missionPeer.BotsUnderControlAlive = controlledBotAlive;

                if (peer != null && peer.IsConnectionActive)
                {
                    GameNetwork.BeginModuleEventAsServer(peer);
                    GameNetwork.WriteMessage(new NetworkMessages.FromServer.BotsControlledChange(peer, controlledBotAlive, controlledBotTotal));
                    GameNetwork.EndModuleEventAsServer();
                }

                ModLogger.Info(
                    "CoopMissionSpawnLogic: promoted exact campaign commander peer to general control. " +
                    "Peer=" + (peer?.UserName ?? peer?.Index.ToString() ?? "none") +
                    " Side=" + authoritativeSide +
                    " TeamIndex=" + team.TeamIndex +
                    " EntryId=" + commanderEntry.EntryId +
                    " AgentIndex=" + controlledAgent.Index +
                    " ControlledFormation=" + (controlledFormation?.FormationIndex.ToString() ?? "null") +
                    " ControlledBotsAlive=" + controlledBotAlive +
                    " ControlledBotsTotal=" + controlledBotTotal +
                    " Source=" + (source ?? "unknown"));

                return "CommanderControl=general ControlledBotsAlive=" + controlledBotAlive + " ControlledBotsTotal=" + controlledBotTotal;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: failed to promote exact campaign commander peer to general control. " +
                    "Peer=" + (peer?.UserName ?? peer?.Index.ToString() ?? "none") +
                    " AgentIndex=" + controlledAgent.Index +
                    " Source=" + (source ?? "unknown") +
                    " Error=" + ex.Message);
                return "CommanderControl=(promotion-failed:" + ex.GetType().Name + ")";
            }
        }

        private static bool DoesPossessedEntryMatchCommanderEntry(string commanderEntryId, string possessedEntryId)
        {
            if (string.IsNullOrWhiteSpace(commanderEntryId) || string.IsNullOrWhiteSpace(possessedEntryId))
                return false;

            return string.Equals(
                StripEntryVariantSuffix(commanderEntryId),
                StripEntryVariantSuffix(possessedEntryId),
                StringComparison.Ordinal);
        }

        private static string StripEntryVariantSuffix(string entryId)
        {
            if (string.IsNullOrWhiteSpace(entryId))
                return entryId;

            const string variantMarker = "|variant-";
            int variantIndex = entryId.IndexOf(variantMarker, StringComparison.Ordinal);
            return variantIndex >= 0
                ? entryId.Substring(0, variantIndex)
                : entryId;
        }

        private static string ReapplyMaterializedAgentStateAfterReplaceBot(
            Agent replacedAgent,
            RosterEntryState entryState)
        {
            if (replacedAgent == null)
                return "ReappliedState=(none)";

            string appliedEquipment = "(none)";
            string equipmentMisses = "(none)";
            string reapplyWield = "skipped";
            bool runtimeExactCharacter = ExactCampaignRuntimeObjectRegistry.IsRuntimeCharacter(replacedAgent.Character as BasicCharacterObject);
            bool preSpawnExactLoadoutInjected = HasExactCampaignPreSpawnLoadoutInjected(replacedAgent);
            Equipment seedEquipment = replacedAgent.SpawnEquipment?.Clone(false);
            bool includeWeaponsForReplaceBotRefresh = !preSpawnExactLoadoutInjected;
            bool forceInitialWieldAfterReplaceBotRefresh = !preSpawnExactLoadoutInjected;
            Equipment spawnEquipment = runtimeExactCharacter
                ? null
                : BuildSnapshotEquipmentForReplaceBot(
                    entryState,
                    seedEquipment,
                    includeWeapons: includeWeaponsForReplaceBotRefresh);
            if (runtimeExactCharacter)
            {
                appliedEquipment = "runtime-exact-character";
                equipmentMisses = "post-replace-refresh-skipped";
            }
            else if (spawnEquipment != null)
            {
                var missedSlots = new List<string>();
                appliedEquipment = TryApplyMaterializedEquipmentOverrides(
                    spawnEquipment,
                    entryState,
                    missedSlots,
                    trackCoverage: false,
                    includeWeapons: includeWeaponsForReplaceBotRefresh);
                equipmentMisses = missedSlots.Count > 0 ? string.Join(", ", missedSlots) : "(none)";

                try
                {
                    replacedAgent.UpdateSpawnEquipmentAndRefreshVisuals(spawnEquipment);
                    if (forceInitialWieldAfterReplaceBotRefresh)
                    {
                        replacedAgent.WieldInitialWeapons(
                            Agent.WeaponWieldActionType.Instant,
                            Equipment.InitialWeaponEquipPreference.Any);
                        reapplyWield = "forced";
                    }
                    else
                    {
                        reapplyWield = "skipped-for-pre-spawn-exact-loadout";
                    }
                    replacedAgent.MountAgent?.UpdateAgentProperties();
                }
                catch (Exception ex)
                {
                    equipmentMisses =
                        equipmentMisses == "(none)"
                            ? "visual-refresh-failed:" + ex.GetType().Name
                            : equipmentMisses + ", visual-refresh-failed:" + ex.GetType().Name;
                }
            }

            TryApplyEntryIdentityToAgent(replacedAgent, entryState);
            string appliedCombatProfile = TryApplyMaterializedCombatProfile(replacedAgent, entryState);
            return
                "ReappliedEquipment=" + appliedEquipment +
                " ReapplyMisses=" + equipmentMisses +
                " ReapplyWield=" + reapplyWield +
                " " + appliedCombatProfile;
        }

        private static Equipment BuildSnapshotEquipmentForReplaceBot(
            RosterEntryState entryState,
            Equipment seedEquipment = null,
            bool includeWeapons = true)
        {
            if (entryState == null)
                return null;

            try
            {
                Equipment equipment = seedEquipment?.Clone(false) ?? new Equipment();
                TryApplyMaterializedEquipmentOverrides(
                    equipment,
                    entryState,
                    null,
                    trackCoverage: false,
                    includeWeapons: includeWeapons);
                return equipment;
            }
            catch
            {
                return null;
            }
        }

        internal static Equipment BuildSnapshotEquipmentForExactRuntime(RosterEntryState entryState)
        {
            return BuildSnapshotEquipmentForReplaceBot(entryState);
        }

        private static void TransferMaterializedAgentRuntimeState(Agent sourceAgent, Agent targetAgent)
        {
            if (sourceAgent == null || targetAgent == null || sourceAgent.Index == targetAgent.Index)
                return;

            if (_materializedArmyEntryIdByAgentIndex.TryGetValue(sourceAgent.Index, out string entryId))
            {
                _materializedArmyEntryIdByAgentIndex[targetAgent.Index] = entryId;
                _materializedArmyEntryIdByAgentIndex.Remove(sourceAgent.Index);
            }

            if (_materializedArmySideByAgentIndex.TryGetValue(sourceAgent.Index, out BattleSideEnum side))
            {
                _materializedArmySideByAgentIndex[targetAgent.Index] = side;
                _materializedArmySideByAgentIndex.Remove(sourceAgent.Index);
            }

            if (_materializedAgentInstanceByIndex.ContainsKey(sourceAgent.Index))
            {
                _materializedAgentInstanceByIndex[targetAgent.Index] = targetAgent;
                _materializedAgentInstanceByIndex.Remove(sourceAgent.Index);
            }

            if (_materializedCombatProfilesByAgentIndex.TryGetValue(sourceAgent.Index, out MaterializedCombatProfileRuntimeState combatProfile))
            {
                _materializedCombatProfilesByAgentIndex[targetAgent.Index] = combatProfile;
                _materializedCombatProfilesByAgentIndex.Remove(sourceAgent.Index);
            }

            if (_materializedBattleResultLastHitDebugByVictimAgentIndex.TryGetValue(sourceAgent.Index, out MaterializedBattleResultLastHitDebugState lastHitDebug))
            {
                _materializedBattleResultLastHitDebugByVictimAgentIndex[targetAgent.Index] = lastHitDebug;
                _materializedBattleResultLastHitDebugByVictimAgentIndex.Remove(sourceAgent.Index);
            }

            if (_materializedBattleResultRemovedAgentIndices.Remove(sourceAgent.Index))
                _materializedBattleResultRemovedAgentIndices.Add(targetAgent.Index);
        }

        private static void TrySetBotsUnderControlTotal(MissionPeer missionPeer, int value)
        {
            if (missionPeer == null)
                return;

            int appliedValue = Math.Max(0, value);
            try
            {
                PropertyInfo totalProperty = typeof(MissionPeer).GetProperty(
                    "BotsUnderControlTotal",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo setMethod = totalProperty?.GetSetMethod(nonPublic: true);
                if (setMethod != null)
                {
                    setMethod.Invoke(missionPeer, new object[] { appliedValue });
                    return;
                }

                FieldInfo backingField = typeof(MissionPeer).GetField(
                    "<BotsUnderControlTotal>k__BackingField",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                backingField?.SetValue(missionPeer, appliedValue);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: failed to set BotsUnderControlTotal for materialized replace-bot: " + ex.Message);
            }
        }

        private static void TryForceFixedMissionCultures(Mission mission, string source)
        {
            if (!EnableFixedMissionCulturesExperiment || mission == null || !GameNetwork.IsServer || GameNetwork.NetworkPeers == null)
                return;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
                    continue;

                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                if (missionPeer == null || missionPeer.Team == null || ReferenceEquals(missionPeer.Team, mission.SpectatorTeam))
                    continue;

                // Fixed mission culture is only needed right before a real coop spawn.
                // Applying it on every side/unit selection while the peer is merely browsing
                // the overlay proved unstable during post-death reselection.
                if (!CoopBattleSpawnRequestState.HasPendingRequest(missionPeer))
                    continue;

                Agent controlledAgent = missionPeer.ControlledAgent;
                if (controlledAgent != null && controlledAgent.IsActive())
                    continue;

                BattleSideEnum authoritativeSide = ResolveAuthoritativeSide(missionPeer, mission, source + " team-sync");
                string targetCultureId = ResolveRuntimeMissionCultureIdForPeer(missionPeer, authoritativeSide);
                if (string.IsNullOrWhiteSpace(targetCultureId))
                    continue;

                BasicCultureObject targetCulture = MBObjectManager.Instance?.GetObject<BasicCultureObject>(targetCultureId);
                if (targetCulture == null)
                    continue;

                string currentCultureId = missionPeer.Culture?.StringId;
                if (string.Equals(currentCultureId, targetCulture.StringId, StringComparison.Ordinal))
                    continue;

                SetServerMemberValue(missionPeer, "Culture", targetCulture);
                if (!ReferenceEquals(GetServerMemberValue(missionPeer, "Culture"), targetCulture))
                    SetServerMemberValue(missionPeer, "_culture", targetCulture);
                if (!ReferenceEquals(GetServerMemberValue(missionPeer, "Culture"), targetCulture))
                    SetServerMemberValue(missionPeer, "<Culture>k__BackingField", targetCulture);

                string appliedCultureId = (GetServerMemberValue(missionPeer, "Culture") as BasicCultureObject)?.StringId;
                if (!string.Equals(appliedCultureId, targetCulture.StringId, StringComparison.Ordinal))
                    continue;

                if (_appliedFixedMissionCultureByPeer.TryGetValue(peer.Index, out string lastAppliedCultureId) &&
                    string.Equals(lastAppliedCultureId, appliedCultureId, StringComparison.Ordinal))
                {
                    continue;
                }

                _appliedFixedMissionCultureByPeer[peer.Index] = appliedCultureId;

                TryBroadcastFixedMissionCulture(peer, missionPeer, targetCulture);

                ModLogger.Info(
                    "CoopMissionSpawnLogic: forced fixed mission culture (" + source + "). " +
                    "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                    " TeamIndex=" + missionPeer.Team.TeamIndex +
                    " Side=" + missionPeer.Team.Side +
                    " PreviousCulture=" + (currentCultureId ?? "null") +
                    " AppliedCulture=" + appliedCultureId +
                    " PreferredHeroRole=" + (ResolvePreferredAllowedEntryStateForPeer(missionPeer, CoopBattleAuthorityState.GetSelectionState(missionPeer))?.HeroRole ?? "none"));
            }
        }

        private static void TryForceAuthoritativePeerTeams(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer || GameNetwork.NetworkPeers == null)
                return;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
                    continue;

                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                if (missionPeer == null)
                    continue;

                bool hasPendingSpawnRequest = CoopBattleSpawnRequestState.HasPendingRequest(missionPeer);
                if (!hasPendingSpawnRequest)
                    continue;

                BattleSideEnum authoritativeSide = ResolveAuthoritativeSide(missionPeer, mission, source + " peer-team-sync");
                Team authoritativeTeam = ResolveAuthoritativeMissionTeam(mission, missionPeer, authoritativeSide);
                if (authoritativeTeam == null || ReferenceEquals(authoritativeTeam, mission.SpectatorTeam))
                    continue;

                Team currentTeam = missionPeer.Team;
                int authoritativeTeamIndex = authoritativeTeam.TeamIndex;
                if (ReferenceEquals(currentTeam, authoritativeTeam) &&
                    _lastBridgedPeerTeamIndexByPeer.TryGetValue(peer.Index, out int lastBridgedTeamIndex) &&
                    lastBridgedTeamIndex == authoritativeTeamIndex)
                {
                    continue;
                }

                SetServerMemberValue(missionPeer, "Team", authoritativeTeam);
                if (!ReferenceEquals(GetServerMemberValue(missionPeer, "Team"), authoritativeTeam))
                    SetServerMemberValue(missionPeer, "_team", authoritativeTeam);
                if (!ReferenceEquals(GetServerMemberValue(missionPeer, "Team"), authoritativeTeam))
                    SetServerMemberValue(missionPeer, "<Team>k__BackingField", authoritativeTeam);

                Team appliedTeam = GetServerMemberValue(missionPeer, "Team") as Team;
                if (!ReferenceEquals(appliedTeam, authoritativeTeam))
                    appliedTeam = GetServerMemberValue(missionPeer, "_team") as Team;
                if (!ReferenceEquals(appliedTeam, authoritativeTeam))
                    continue;

                _lastBridgedPeerTeamIndexByPeer[peer.Index] = authoritativeTeamIndex;

                try
                {
                    GameNetwork.BeginBroadcastModuleEvent();
                    GameNetwork.WriteMessage(new NetworkMessages.FromServer.SetPeerTeam(peer, authoritativeTeamIndex));
                    GameNetwork.EndBroadcastModuleEvent(GameNetwork.EventBroadcastFlags.None);

                    ModLogger.Info(
                        "CoopMissionSpawnLogic: forced authoritative peer team (" + source + "). " +
                        "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                        " PreviousTeamIndex=" + (currentTeam?.TeamIndex ?? -1) +
                        " AppliedTeamIndex=" + authoritativeTeamIndex +
                        " AppliedSide=" + authoritativeTeam.Side);
                }
                catch (Exception ex)
                {
                    ModLogger.Info("CoopMissionSpawnLogic: rebroadcast authoritative peer team failed: " + ex.Message);
                }
            }
        }

        private static string ResolveFixedMissionCultureIdForTeam(Team team)
        {
            return ResolveFixedMissionCultureIdForSide(team?.Side ?? BattleSideEnum.None);
        }

        private static string ResolveFixedMissionCultureIdForSide(BattleSideEnum side)
        {
            if (side == BattleSideEnum.None)
                return null;

            if (side == BattleSideEnum.Attacker)
                return FixedMissionAttackerCultureId;

            if (side == BattleSideEnum.Defender)
                return FixedMissionDefenderCultureId;

            return null;
        }

        private static string ResolveRuntimeMissionCultureIdForPeer(MissionPeer missionPeer, BattleSideEnum authoritativeSide)
        {
            string heroCultureId = TryResolveHeroRuntimeCultureIdForPeer(missionPeer);
            return !string.IsNullOrWhiteSpace(heroCultureId)
                ? heroCultureId
                : ResolveFixedMissionCultureIdForSide(authoritativeSide);
        }

        private static string TryResolveHeroRuntimeCultureIdForPeer(MissionPeer missionPeer)
        {
            if (missionPeer == null)
                return null;

            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            RosterEntryState preferredEntry = ResolvePreferredAllowedEntryStateForPeer(missionPeer, selectionState);
            if (!IsHeroRoleEntry(preferredEntry, "player", "companion", "wanderer", "lord"))
                return null;

            return string.IsNullOrWhiteSpace(preferredEntry.CultureId)
                ? null
                : preferredEntry.CultureId;
        }

        private static void TryBroadcastFixedMissionCulture(NetworkCommunicator peer, MissionPeer missionPeer, BasicCultureObject targetCulture)
        {
            if (peer == null || missionPeer == null || targetCulture == null || !GameNetwork.IsServer)
                return;

            try
            {
                GameNetwork.BeginBroadcastModuleEvent();
                GameNetwork.WriteMessage(new NetworkMessages.FromServer.ChangeCulture(missionPeer, targetCulture));
                GameNetwork.EndBroadcastModuleEvent(GameNetwork.EventBroadcastFlags.None);

                ModLogger.Info(
                    "CoopMissionSpawnLogic: rebroadcast fixed culture after server override. " +
                    "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                    " TeamIndex=" + (missionPeer.Team?.TeamIndex ?? -1) +
                    " Side=" + (missionPeer.Team?.Side.ToString() ?? "None") +
                    " Culture=" + targetCulture.StringId);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: rebroadcast fixed culture failed: " + ex.Message);
            }
        }

        private static object GetServerMemberValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return null;

            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    return property.GetValue(instance, null);
                }
                catch
                {
                }
            }

            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                try
                {
                    return field.GetValue(instance);
                }
                catch
                {
                }
            }

            return null;
        }

        private static void SetServerMemberValue(object instance, string memberName, object value)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return;

            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    property.SetValue(instance, value, null);
                    return;
                }
                catch
                {
                }
            }

            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                try
                {
                    field.SetValue(instance, value);
                }
                catch
                {
                }
            }
        }

        private static void RefreshAllowedTroopsFromRoster(string source)
        {
            BattleSnapshotMessage rosterSnapshot = BattleRosterFileHelper.ReadSnapshot();
            if (rosterSnapshot == null)
                rosterSnapshot = BattleSnapshotRuntimeState.GetCurrent();

            BattleRuntimeState rosterState = BattleSnapshotRuntimeState.GetState();
            BattleSnapshotProjectionState rosterProjection = BattleSnapshotRuntimeState.GetProjection();
            if ((rosterState == null || rosterProjection == null) && rosterSnapshot != null)
            {
                rosterState = BattleSnapshotRuntimeState.GetState();
                rosterProjection = BattleSnapshotRuntimeState.GetProjection();
            }

            List<string> roster = BattleRosterFileHelper.ReadRoster();
            CampaignRosterTroopIds = NormalizeRosterTroopIds(roster);
            AllowedControlTroopIds = new List<string>();
            AllowedControlEntryIds = new List<string>();
            AllowedControlCharacters = new List<BasicCharacterObject>();
            AllowedControlTroopIdsBySide = new Dictionary<BattleSideEnum, List<string>>();
            AllowedControlEntryIdsBySide = new Dictionary<BattleSideEnum, List<string>>();
            AllowedControlEntriesBySide = new Dictionary<BattleSideEnum, List<BattleRosterEntryProjectionState>>();
            AllowedControlEntryStatesBySide = new Dictionary<BattleSideEnum, List<RosterEntryState>>();
            AllowedControlCharactersBySide = new Dictionary<BattleSideEnum, List<BasicCharacterObject>>();

            SelectedAllowedTroopId = null;
            SelectedAllowedEntryId = null;
            SelectedAllowedCharacter = null;

            if (rosterState?.Sides != null && rosterState.Sides.Count > 0 && rosterProjection != null)
            {
                CampaignRosterTroopIds = NormalizeRosterTroopIds(rosterState.FlatTroopIds);
                ControlTroopIds = CampaignRosterTroopIds.ToList();

                List<string> attackerRoster = new List<string>();
                List<string> defenderRoster = new List<string>();
                List<RosterEntryState> playerSideEntries = new List<RosterEntryState>();

                foreach (BattleSideState sideState in rosterState.Sides)
                {
                    BattleSideEnum side = ResolveBattleSideFromState(sideState);
                    List<string> sideTroopIds = NormalizeRosterTroopIds(sideState.TroopIds);
                    if (sideState?.Entries == null || sideState.Entries.Count == 0)
                        continue;

                    if (side == BattleSideEnum.Attacker)
                        attackerRoster = sideTroopIds;
                    else if (side == BattleSideEnum.Defender)
                        defenderRoster = sideTroopIds;

                    if (sideState.IsPlayerSide && playerSideEntries.Count == 0)
                        playerSideEntries = sideState.Entries.Where(entry => entry != null).ToList();

                    foreach (RosterEntryState entryState in sideState.Entries.Where(entry => entry != null))
                        TryAddAllowedControlEntryState(side, entryState, rosterProjection, preferAsSelected: SelectedAllowedCharacter == null && sideState.IsPlayerSide);
                }

                List<RosterEntryState> preferredEntries = playerSideEntries.Count > 0
                    ? playerSideEntries
                    : rosterState.EntriesById.Values.Where(entry => entry != null).ToList();
                if (SelectedAllowedCharacter == null && preferredEntries.Count > 0)
                {
                    SelectedAllowedEntryId = preferredEntries[0].EntryId;
                    SelectedAllowedTroopId = ResolveEntrySpawnTemplateId(preferredEntries[0]);
                    SelectedAllowedCharacter = TryResolveEntryPreferredCharacter(SelectedAllowedEntryId, SelectedAllowedTroopId);
                    if (SelectedAllowedCharacter != null)
                        TryAddAllowedControlEntryState(BattleSideEnum.None, preferredEntries[0], rosterProjection, preferAsSelected: true);
                }

                RefreshAuthorityStateFromAllowedTroops(source, "battle-snapshot");
                ModLogger.Info(
                    "CoopMissionSpawnLogic: using battle snapshot runtime source = " + (BattleSnapshotRuntimeState.GetSource() ?? "unknown") +
                    " entries = " + rosterState.EntriesById.Count +
                    " parties = " + rosterState.PartiesById.Count +
                    " sides = " + rosterState.Sides.Count);
                LogAllowedEntryStateMappings(source, rosterState);
                LogPartyModifierStateMappings(source, rosterState);
                LogAllowedControlTroops(source, "battle-snapshot", attackerRoster, defenderRoster);
                return;
            }

            if (EnableFixedMissionCulturesExperiment)
            {
                List<string> attackerRoster = new List<string>
                {
                    "mp_coop_light_cavalry_empire_troop",
                    "mp_coop_heavy_infantry_empire_troop",
                };
                List<string> defenderRoster = new List<string>
                {
                    "mp_coop_light_cavalry_vlandia_troop",
                    "mp_coop_heavy_infantry_vlandia_troop",
                };

                CampaignRosterTroopIds = attackerRoster.Concat(defenderRoster).ToList();
                ControlTroopIds = CampaignRosterTroopIds.ToList();

                foreach (string troopId in attackerRoster)
                    TryAddAllowedControlTroop(BattleSideEnum.Attacker, troopId, preferAsSelected: SelectedAllowedCharacter == null);

                foreach (string troopId in defenderRoster)
                    TryAddAllowedControlTroop(BattleSideEnum.Defender, troopId, preferAsSelected: false);

                RefreshAuthorityStateFromAllowedTroops(source, "fixed-test");
                LogAllowedControlTroops(source, "fixed-test", attackerRoster, defenderRoster);
                return;
            }

            ControlTroopIds = CampaignRosterTroopIds.Take(2).ToList();
            foreach (string troopId in BuildAllowedControlTroopCandidates(ControlTroopIds))
            {
                TryAddAllowedControlTroop(BattleSideEnum.None, troopId, preferAsSelected: SelectedAllowedCharacter == null);
            }

            if (SelectedAllowedCharacter == null && CampaignRosterTroopIds.Count > 0)
            {
                SelectedAllowedTroopId = CampaignRosterTroopIds[0];
                SelectedAllowedCharacter = ResolveAllowedCharacter(SelectedAllowedTroopId);
                if (SelectedAllowedCharacter != null)
                {
                    TryAddAllowedControlTroop(BattleSideEnum.None, SelectedAllowedTroopId, preferAsSelected: true);
                }
            }

            RefreshAuthorityStateFromAllowedTroops(source, "campaign-roster");
            LogAllowedControlTroops(source, "campaign-roster", ControlTroopIds, Array.Empty<string>());
        }

        private static void RefreshAuthorityStateFromAllowedTroops(string source, string mode)
        {
            CoopBattleAuthorityState.Reset(
                AllowedControlEntryIdsBySide,
                AllowedControlTroopIdsBySide,
                AllowedControlEntryIds,
                AllowedControlTroopIds,
                SelectedAllowedEntryId,
                SelectedAllowedTroopId);
            ModLogger.Info(
                "CoopMissionSpawnLogic: coop authority state refreshed. " +
                "Source=" + source +
                " Mode=" + mode +
                " Allowed=" + AllowedControlTroopIds.Count +
                " Entries=" + AllowedControlEntryIds.Count +
                " SideBuckets=" + AllowedControlTroopIdsBySide.Count +
                " FallbackSelected=" + (SelectedAllowedTroopId ?? "null"));
        }

        private static BattleSideEnum ResolveBattleSideFromSnapshot(BattleSideSnapshotMessage sideSnapshot)
        {
            if (sideSnapshot == null)
                return BattleSideEnum.None;

            string sideText = sideSnapshot.SideText ?? sideSnapshot.SideId;
            if (string.Equals(sideText, nameof(BattleSideEnum.Attacker), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sideText, "attacker", StringComparison.OrdinalIgnoreCase))
            {
                return BattleSideEnum.Attacker;
            }

            if (string.Equals(sideText, nameof(BattleSideEnum.Defender), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sideText, "defender", StringComparison.OrdinalIgnoreCase))
            {
                return BattleSideEnum.Defender;
            }

            return BattleSideEnum.None;
        }

        private static BattleSideEnum ResolveBattleSideFromState(BattleSideState sideState)
        {
            if (sideState == null)
                return BattleSideEnum.None;

            if (string.Equals(sideState.CanonicalSideKey, "attacker", StringComparison.OrdinalIgnoreCase))
                return BattleSideEnum.Attacker;

            if (string.Equals(sideState.CanonicalSideKey, "defender", StringComparison.OrdinalIgnoreCase))
                return BattleSideEnum.Defender;

            return BattleSideEnum.None;
        }

        private static void LogAllowedControlTroops(string source, string mode, IReadOnlyCollection<string> attackerRoster, IReadOnlyCollection<string> defenderRoster)
        {
            string controlUnits = ControlTroopIds.Count > 0
                ? string.Join(", ", ControlTroopIds)
                : "(none)";
            ModLogger.Info("CoopMissionSpawnLogic: control troop candidates (" + source + ", " + mode + ") = [" + controlUnits + "].");

            string allowedUnits = AllowedControlTroopIds.Count > 0
                ? string.Join(", ", AllowedControlTroopIds)
                : "(none)";
            ModLogger.Info("CoopMissionSpawnLogic: allowed control troop ids (" + source + ", " + mode + ") = [" + allowedUnits + "].");

            string attackerUnits = AllowedControlTroopIdsBySide.TryGetValue(BattleSideEnum.Attacker, out List<string> attackerAllowed) && attackerAllowed.Count > 0
                ? string.Join(", ", attackerAllowed)
                : (attackerRoster != null && attackerRoster.Count > 0 ? string.Join(", ", attackerRoster) : "(none)");
            string defenderUnits = AllowedControlTroopIdsBySide.TryGetValue(BattleSideEnum.Defender, out List<string> defenderAllowed) && defenderAllowed.Count > 0
                ? string.Join(", ", defenderAllowed)
                : (defenderRoster != null && defenderRoster.Count > 0 ? string.Join(", ", defenderRoster) : "(none)");

            ModLogger.Info("CoopMissionSpawnLogic: attacker allowed troop ids (" + source + ", " + mode + ") = [" + attackerUnits + "].");
            ModLogger.Info("CoopMissionSpawnLogic: defender allowed troop ids (" + source + ", " + mode + ") = [" + defenderUnits + "].");
        }

        private static void LogAllowedEntryStateMappings(string source, BattleRuntimeState rosterState)
        {
            if (rosterState?.EntriesById == null || rosterState.EntriesById.Count == 0)
                return;

            IEnumerable<string> mappings = rosterState.EntriesById.Values
                .Where(entry => entry != null)
                .Take(32)
                .Select(entry =>
                    (entry.SideId ?? "side") + "/" +
                    (entry.PartyId ?? "party") + "/" +
                    (entry.EntryId ?? "entry") + ": " +
                    (entry.OriginalCharacterId ?? "null") +
                    " -> " +
                    (ResolveEntrySpawnTemplateId(entry) ?? "null") +
                    " Culture=" + (entry.CultureId ?? "null") +
                    " Ranged=" + entry.IsRanged +
                    " Shield=" + entry.HasShield +
                    " Thrown=" + entry.HasThrown +
                    FormatEntryHeroIdentitySummary(entry) +
                    FormatEntryCombatProfileSummary(entry) +
                    FormatEntryCombatEquipmentSummary(entry) +
                    " Count=" + entry.Count +
                    " Wounded=" + entry.WoundedCount +
                    " Hero=" + entry.IsHero);

            ModLogger.Info(
                "CoopMissionSpawnLogic: snapshot entry mapping summary (" + (source ?? "unknown") + ") = [" +
                string.Join("; ", mappings) +
                "].");
        }

        private static void LogPartyModifierStateMappings(string source, BattleRuntimeState rosterState)
        {
            if (rosterState?.PartiesById == null || rosterState.PartiesById.Count == 0)
                return;

            IEnumerable<string> summaries = rosterState.PartiesById.Values
                .Where(party => party != null)
                .Select(party => FormatPartyModifierStateSummary(rosterState, party))
                .Where(summary => !string.IsNullOrWhiteSpace(summary))
                .Take(24);
            if (!summaries.Any())
                return;

            ModLogger.Info(
                "CoopMissionSpawnLogic: snapshot party modifier summary (" + (source ?? "unknown") + ") = [" +
                string.Join("; ", summaries) +
                "].");
        }

        private static string FormatPartyModifierStateSummary(BattleRuntimeState rosterState, BattlePartyState party)
        {
            if (rosterState == null || party == null)
                return null;

            BattlePartyModifierState modifiers = party.Modifiers;
            BattleSideState sideState = null;
            if (!string.IsNullOrWhiteSpace(party.SideId))
                rosterState.SidesByKey?.TryGetValue(party.SideId, out sideState);

            bool hasAnyData =
                (!string.IsNullOrWhiteSpace(sideState?.LeaderPartyId) || Math.Abs(sideState?.SideMorale ?? 0f) > 0.01f) ||
                HasPartyModifierData(modifiers);
            if (!hasAnyData)
                return null;

            var parts = new List<string>
            {
                (party.SideId ?? "side") + "/" + (party.PartyId ?? "party")
            };

            if (!string.IsNullOrWhiteSpace(sideState?.LeaderPartyId) &&
                string.Equals(sideState.LeaderPartyId, party.PartyId, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add("LeaderParty=True");
            }

            if (Math.Abs(sideState?.SideMorale ?? 0f) > 0.01f)
                parts.Add("SideMorale=" + sideState.SideMorale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));

            if (modifiers != null)
            {
                AddPartyModifierSummaryPart(parts, "Leader", modifiers.LeaderHeroId);
                AddPartyModifierSummaryPart(parts, "Owner", modifiers.OwnerHeroId);
                AddPartyModifierSummaryPart(parts, "Scout", modifiers.ScoutHeroId);
                AddPartyModifierSummaryPart(parts, "QM", modifiers.QuartermasterHeroId);
                AddPartyModifierSummaryPart(parts, "Eng", modifiers.EngineerHeroId);
                AddPartyModifierSummaryPart(parts, "Surg", modifiers.SurgeonHeroId);

                if (Math.Abs(modifiers.Morale) > 0.01f)
                    parts.Add("Morale=" + modifiers.Morale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                if (Math.Abs(modifiers.RecentEventsMorale) > 0.01f)
                    parts.Add("Recent=" + modifiers.RecentEventsMorale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                if (Math.Abs(modifiers.MoraleChange) > 0.01f)
                    parts.Add("MoraleChange=" + modifiers.MoraleChange.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                if (modifiers.ContributionToBattle > 0)
                    parts.Add("Contribution=" + modifiers.ContributionToBattle);

                if (modifiers.LeaderLeadershipSkill > 0 || modifiers.LeaderTacticsSkill > 0)
                    parts.Add("LeaderSkills=" + modifiers.LeaderLeadershipSkill + "/" + modifiers.LeaderTacticsSkill);
                if (modifiers.ScoutScoutingSkill > 0)
                    parts.Add("ScoutSkill=" + modifiers.ScoutScoutingSkill);
                if (modifiers.QuartermasterStewardSkill > 0)
                    parts.Add("QMSkill=" + modifiers.QuartermasterStewardSkill);
                if (modifiers.EngineerEngineeringSkill > 0)
                    parts.Add("EngSkill=" + modifiers.EngineerEngineeringSkill);
                if (modifiers.SurgeonMedicineSkill > 0)
                    parts.Add("SurgSkill=" + modifiers.SurgeonMedicineSkill);

                string rolePerks = FormatRolePerkCountSummary(modifiers);
                if (!string.IsNullOrWhiteSpace(rolePerks))
                    parts.Add("RolePerks[" + rolePerks + "]");
            }

            return string.Join(" ", parts);
        }

        private static string FormatRolePerkCountSummary(BattlePartyModifierState modifiers)
        {
            if (modifiers == null)
                return string.Empty;

            var parts = new List<string>();
            AddRolePerkCountSummaryPart(parts, "PL", modifiers.PartyLeaderPerkIds);
            AddRolePerkCountSummaryPart(parts, "AC", modifiers.ArmyCommanderPerkIds);
            AddRolePerkCountSummaryPart(parts, "Cap", modifiers.CaptainPerkIds);
            AddRolePerkCountSummaryPart(parts, "Sc", modifiers.ScoutPerkIds);
            AddRolePerkCountSummaryPart(parts, "QM", modifiers.QuartermasterPerkIds);
            AddRolePerkCountSummaryPart(parts, "Eng", modifiers.EngineerPerkIds);
            AddRolePerkCountSummaryPart(parts, "Surg", modifiers.SurgeonPerkIds);
            return string.Join(",", parts);
        }

        private static void AddRolePerkCountSummaryPart(List<string> parts, string label, List<string> perkIds)
        {
            if (parts == null || string.IsNullOrWhiteSpace(label) || perkIds == null || perkIds.Count <= 0)
                return;

            parts.Add(label + "=" + perkIds.Count);
        }

        private static void AddPartyModifierSummaryPart(List<string> parts, string label, string value)
        {
            if (parts == null || string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(value))
                return;

            parts.Add(label + "=" + value);
        }

        private static bool HasPartyModifierData(BattlePartyModifierState modifiers)
        {
            if (modifiers == null)
                return false;

            return
                !string.IsNullOrWhiteSpace(modifiers.LeaderHeroId) ||
                !string.IsNullOrWhiteSpace(modifiers.OwnerHeroId) ||
                !string.IsNullOrWhiteSpace(modifiers.ScoutHeroId) ||
                !string.IsNullOrWhiteSpace(modifiers.QuartermasterHeroId) ||
                !string.IsNullOrWhiteSpace(modifiers.EngineerHeroId) ||
                !string.IsNullOrWhiteSpace(modifiers.SurgeonHeroId) ||
                Math.Abs(modifiers.Morale) > 0.01f ||
                Math.Abs(modifiers.RecentEventsMorale) > 0.01f ||
                Math.Abs(modifiers.MoraleChange) > 0.01f ||
                modifiers.ContributionToBattle > 0 ||
                modifiers.LeaderLeadershipSkill > 0 ||
                modifiers.LeaderTacticsSkill > 0 ||
                modifiers.ScoutScoutingSkill > 0 ||
                modifiers.QuartermasterStewardSkill > 0 ||
                modifiers.EngineerEngineeringSkill > 0 ||
                modifiers.SurgeonMedicineSkill > 0 ||
                (modifiers.PartyLeaderPerkIds?.Count ?? 0) > 0 ||
                (modifiers.ArmyCommanderPerkIds?.Count ?? 0) > 0 ||
                (modifiers.CaptainPerkIds?.Count ?? 0) > 0 ||
                (modifiers.ScoutPerkIds?.Count ?? 0) > 0 ||
                (modifiers.QuartermasterPerkIds?.Count ?? 0) > 0 ||
                (modifiers.EngineerPerkIds?.Count ?? 0) > 0 ||
                (modifiers.SurgeonPerkIds?.Count ?? 0) > 0;
        }

        private static string FormatEntryCombatEquipmentSummary(RosterEntryState entry)
        {
            if (entry == null)
                return string.Empty;

            var parts = new List<string>();
            AddEntryCombatEquipmentSummaryPart(parts, "Item0", entry.CombatItem0Id, entry.CombatItem0Amount);
            AddEntryCombatEquipmentSummaryPart(parts, "Item1", entry.CombatItem1Id, entry.CombatItem1Amount);
            AddEntryCombatEquipmentSummaryPart(parts, "Item2", entry.CombatItem2Id, entry.CombatItem2Amount);
            AddEntryCombatEquipmentSummaryPart(parts, "Item3", entry.CombatItem3Id, entry.CombatItem3Amount);
            AddEntryCombatEquipmentSummaryPart(parts, "Head", entry.CombatHeadId);
            AddEntryCombatEquipmentSummaryPart(parts, "Body", entry.CombatBodyId);
            AddEntryCombatEquipmentSummaryPart(parts, "Leg", entry.CombatLegId);
            AddEntryCombatEquipmentSummaryPart(parts, "Gloves", entry.CombatGlovesId);
            AddEntryCombatEquipmentSummaryPart(parts, "Cape", entry.CombatCapeId);
            AddEntryCombatEquipmentSummaryPart(parts, "Horse", entry.CombatHorseId);
            AddEntryCombatEquipmentSummaryPart(parts, "HorseHarness", entry.CombatHorseHarnessId);

            return parts.Count == 0
                ? " Equip=[]"
                : " Equip=[" + string.Join(", ", parts) + "]";
        }

        private static string FormatEntryCombatProfileSummary(RosterEntryState entry)
        {
            if (entry == null)
                return string.Empty;

            bool hasCombatProfile =
                entry.AttributeVigor > 0 ||
                entry.AttributeControl > 0 ||
                entry.AttributeEndurance > 0 ||
                entry.SkillOneHanded > 0 ||
                entry.SkillTwoHanded > 0 ||
                entry.SkillPolearm > 0 ||
                entry.SkillBow > 0 ||
                entry.SkillCrossbow > 0 ||
                entry.SkillThrowing > 0 ||
                entry.SkillRiding > 0 ||
                entry.SkillAthletics > 0 ||
                entry.BaseHitPoints > 0 ||
                (entry.PerkIds != null && entry.PerkIds.Count > 0);

            if (!hasCombatProfile)
                return string.Empty;

            var parts = new List<string>
            {
                "Attr=" + entry.AttributeVigor + "/" + entry.AttributeControl + "/" + entry.AttributeEndurance,
                "Skills=" + string.Join("/",
                    entry.SkillOneHanded,
                    entry.SkillTwoHanded,
                    entry.SkillPolearm,
                    entry.SkillBow,
                    entry.SkillCrossbow,
                    entry.SkillThrowing,
                    entry.SkillRiding,
                    entry.SkillAthletics),
                "Hp=" + entry.BaseHitPoints
            };

            if (entry.PerkIds != null && entry.PerkIds.Count > 0)
            {
                IEnumerable<string> samplePerks = entry.PerkIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Take(3);
                string perkSample = string.Join(",", samplePerks);
                if (entry.PerkIds.Count > 3)
                    perkSample += ",...";

                parts.Add("Perks=" + entry.PerkIds.Count + (string.IsNullOrWhiteSpace(perkSample) ? string.Empty : "[" + perkSample + "]"));
            }

            return " Profile=[" + string.Join(" ", parts) + "]";
        }

        private static string FormatEntryHeroIdentitySummary(RosterEntryState entry)
        {
            if (entry == null)
                return string.Empty;

            bool hasHeroIdentity =
                !string.IsNullOrWhiteSpace(entry.HeroId) ||
                !string.IsNullOrWhiteSpace(entry.HeroRole) ||
                !string.IsNullOrWhiteSpace(entry.HeroOccupationId) ||
                !string.IsNullOrWhiteSpace(entry.HeroClanId);

            if (!hasHeroIdentity)
                return entry.IsHero ? " HeroRole=hero" : string.Empty;

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(entry.HeroId))
                parts.Add("HeroId=" + entry.HeroId);
            if (!string.IsNullOrWhiteSpace(entry.HeroRole))
                parts.Add("HeroRole=" + entry.HeroRole);
            if (!string.IsNullOrWhiteSpace(entry.HeroOccupationId))
                parts.Add("Occupation=" + entry.HeroOccupationId);
            if (!string.IsNullOrWhiteSpace(entry.HeroClanId))
                parts.Add("Clan=" + entry.HeroClanId);
            if (!string.IsNullOrWhiteSpace(entry.HeroTemplateId))
                parts.Add("Template=" + entry.HeroTemplateId);
            if (!string.IsNullOrWhiteSpace(entry.HeroBodyProperties))
                parts.Add("Body=present");
            if (entry.HeroLevel > 0)
                parts.Add("Level=" + entry.HeroLevel);
            if (entry.HeroAge > 0.01f)
                parts.Add("Age=" + entry.HeroAge.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            parts.Add("Female=" + entry.HeroIsFemale);

            return parts.Count == 0
                ? string.Empty
                : " " + string.Join(" ", parts);
        }

        private static void AddEntryCombatEquipmentSummaryPart(List<string> parts, string label, string itemId, int? itemAmount = null)
        {
            if (parts == null || string.IsNullOrWhiteSpace(itemId))
                return;

            parts.Add(label + "=" + itemId + (itemAmount.HasValue ? "x" + itemAmount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty));
        }

        private static IEnumerable<string> BuildAllowedControlTroopCandidates(IEnumerable<string> baseControlTroopIds)
        {
            HashSet<string> yieldedTroopIds = new HashSet<string>(StringComparer.Ordinal);
            if (baseControlTroopIds == null)
                yield break;

            foreach (string troopId in baseControlTroopIds)
            {
                if (string.IsNullOrWhiteSpace(troopId) || !yieldedTroopIds.Add(troopId))
                    continue;

                yield return troopId;

                foreach (string companionTroopId in GetCuratedCompanionControlTroopIds(troopId))
                {
                    if (string.IsNullOrWhiteSpace(companionTroopId) || !yieldedTroopIds.Add(companionTroopId))
                        continue;

                    yield return companionTroopId;
                }
            }
        }

        private static IEnumerable<string> GetCuratedCompanionControlTroopIds(string troopId)
        {
            if (string.IsNullOrWhiteSpace(troopId))
                yield break;

            string normalizedTroopId = troopId.Trim().ToLowerInvariant();
            if (normalizedTroopId.IndexOf("_empire_", StringComparison.Ordinal) >= 0)
            {
                yield return "mp_coop_light_cavalry_empire_troop";
                yield return "mp_coop_heavy_infantry_empire_troop";
            }

            if (normalizedTroopId.IndexOf("_vlandia_", StringComparison.Ordinal) >= 0)
            {
                yield return "mp_coop_light_cavalry_vlandia_troop";
                yield return "mp_coop_heavy_infantry_vlandia_troop";
            }
        }

        private static bool TryAddAllowedControlTroop(BattleSideEnum side, string troopId, bool preferAsSelected)
        {
            if (string.IsNullOrWhiteSpace(troopId) || AllowedControlTroopIds.Contains(troopId))
                return false;

            BasicCharacterObject resolvedCharacter = ResolveAllowedCharacter(troopId);
            if (resolvedCharacter == null)
                return false;

            if (!AllowedControlTroopIdsBySide.TryGetValue(side, out List<string> sideTroopIds))
            {
                sideTroopIds = new List<string>();
                AllowedControlTroopIdsBySide[side] = sideTroopIds;
            }

            if (!AllowedControlCharactersBySide.TryGetValue(side, out List<BasicCharacterObject> sideCharacters))
            {
                sideCharacters = new List<BasicCharacterObject>();
                AllowedControlCharactersBySide[side] = sideCharacters;
            }

            AllowedControlTroopIds.Add(troopId);
            AllowedControlCharacters.Add(resolvedCharacter);
            sideTroopIds.Add(troopId);
            sideCharacters.Add(resolvedCharacter);

            if (preferAsSelected)
            {
                SelectedAllowedTroopId = troopId;
                SelectedAllowedCharacter = resolvedCharacter;
            }

            return true;
        }

        private static bool TryAddAllowedControlEntry(BattleSideEnum side, BattleRosterEntryProjectionState entryProjection, bool preferAsSelected)
        {
            string spawnTemplateId = ResolveEntrySpawnTemplateId(entryProjection);
            if (entryProjection == null || string.IsNullOrWhiteSpace(entryProjection.EntryId) || string.IsNullOrWhiteSpace(spawnTemplateId))
                return false;

            BasicCharacterObject resolvedCharacter = TryResolveEntryPreferredCharacter(entryProjection.EntryId, spawnTemplateId);
            if (resolvedCharacter == null)
                return false;

            if (!AllowedControlEntryIdsBySide.TryGetValue(side, out List<string> sideEntryIds))
            {
                sideEntryIds = new List<string>();
                AllowedControlEntryIdsBySide[side] = sideEntryIds;
            }

            if (!AllowedControlEntriesBySide.TryGetValue(side, out List<BattleRosterEntryProjectionState> sideEntries))
            {
                sideEntries = new List<BattleRosterEntryProjectionState>();
                AllowedControlEntriesBySide[side] = sideEntries;
            }

            if (!AllowedControlCharactersBySide.TryGetValue(side, out List<BasicCharacterObject> sideCharacters))
            {
                sideCharacters = new List<BasicCharacterObject>();
                AllowedControlCharactersBySide[side] = sideCharacters;
            }

            if (!AllowedControlEntryIds.Contains(entryProjection.EntryId))
                AllowedControlEntryIds.Add(entryProjection.EntryId);
            if (!sideEntryIds.Contains(entryProjection.EntryId))
                sideEntryIds.Add(entryProjection.EntryId);
            if (!sideEntries.Any(entry => string.Equals(entry?.EntryId, entryProjection.EntryId, StringComparison.Ordinal)))
                sideEntries.Add(entryProjection);

            if (!AllowedControlTroopIds.Contains(spawnTemplateId))
                AllowedControlTroopIds.Add(spawnTemplateId);
            if (!AllowedControlCharacters.Contains(resolvedCharacter))
                AllowedControlCharacters.Add(resolvedCharacter);

            if (!AllowedControlTroopIdsBySide.TryGetValue(side, out List<string> sideTroopIds))
            {
                sideTroopIds = new List<string>();
                AllowedControlTroopIdsBySide[side] = sideTroopIds;
            }

            if (!sideTroopIds.Contains(spawnTemplateId))
                sideTroopIds.Add(spawnTemplateId);
            if (!sideCharacters.Contains(resolvedCharacter))
                sideCharacters.Add(resolvedCharacter);

            if (preferAsSelected)
            {
                SelectedAllowedEntryId = entryProjection.EntryId;
                SelectedAllowedTroopId = spawnTemplateId;
                SelectedAllowedCharacter = resolvedCharacter;
            }

            return true;
        }

        private static bool TryAddAllowedControlEntryState(
            BattleSideEnum side,
            RosterEntryState entryState,
            BattleSnapshotProjectionState rosterProjection,
            bool preferAsSelected)
        {
            if (entryState == null || rosterProjection?.EntriesById == null)
                return false;

            if (!AllowedControlEntryStatesBySide.TryGetValue(side, out List<RosterEntryState> sideEntryStates))
            {
                sideEntryStates = new List<RosterEntryState>();
                AllowedControlEntryStatesBySide[side] = sideEntryStates;
            }

            if (!sideEntryStates.Any(entry => string.Equals(entry?.EntryId, entryState.EntryId, StringComparison.Ordinal)))
                sideEntryStates.Add(entryState);

            if (!rosterProjection.EntriesById.TryGetValue(entryState.EntryId, out BattleRosterEntryProjectionState entryProjection) || entryProjection == null)
                return false;

            return TryAddAllowedControlEntry(side, entryProjection, preferAsSelected);
        }

        private static string ResolveEntrySpawnTemplateId(RosterEntryState entryState)
        {
            if (entryState == null)
                return null;

            return !string.IsNullOrWhiteSpace(entryState.SpawnTemplateId)
                ? entryState.SpawnTemplateId
                : entryState.CharacterId;
        }

        private static string ResolveEntrySpawnTemplateId(BattleRosterEntryProjectionState entryProjection)
        {
            if (entryProjection == null)
                return null;

            return !string.IsNullOrWhiteSpace(entryProjection.SpawnTemplateId)
                ? entryProjection.SpawnTemplateId
                : entryProjection.CharacterId;
        }

        private static BasicCharacterObject TryResolveEntryPreferredCharacter(string entryId, string spawnTemplateId)
        {
            BasicCharacterObject entryCharacter = BattleSnapshotRuntimeState.TryResolveCharacterObject(entryId);
            return entryCharacter ?? ResolveAllowedCharacter(spawnTemplateId);
        }

        public static IReadOnlyList<string> GetAllowedControlTroopIdsSnapshot()
        {
            return CoopBattleAuthorityState.GetAllowedTroopIds(BattleSideEnum.None);
        }

        public static IReadOnlyList<string> GetAllowedControlTroopIdsSnapshot(BattleSideEnum side)
        {
            return CoopBattleAuthorityState.GetAllowedTroopIds(side);
        }

        public static bool HasSideScopedRoster()
        {
            return HasAllowedRosterForSide(BattleSideEnum.Attacker) || HasAllowedRosterForSide(BattleSideEnum.Defender);
        }

        public static IReadOnlyList<string> GetAllowedControlEntryIdsSnapshot()
        {
            return AllowedControlEntryIds.ToArray();
        }

        public static IReadOnlyList<string> GetAllowedControlEntryIdsSnapshot(BattleSideEnum side)
        {
            if (AllowedControlEntryIdsBySide.TryGetValue(side, out List<string> sideEntryIds) && sideEntryIds.Count > 0)
                return sideEntryIds.ToArray();

            return Array.Empty<string>();
        }

        public static IReadOnlyList<RosterEntryState> GetAllowedControlEntryStatesSnapshot(BattleSideEnum side)
        {
            if (AllowedControlEntryStatesBySide.TryGetValue(side, out List<RosterEntryState> sideEntries) && sideEntries.Count > 0)
                return sideEntries.ToArray();

            return Array.Empty<RosterEntryState>();
        }

        public static BattleSideEnum ResolveAuthoritativeSide(MissionPeer missionPeer, Mission mission, string source)
        {
            if (missionPeer == null)
                return BattleSideEnum.None;

            BattleSideEnum requestedSide = CoopBattleAuthorityState.GetRequestedSide(missionPeer);
            BattleSideEnum assignedSide = CoopBattleAuthorityState.GetAssignedSide(missionPeer);
            if (HasAllowedRosterForSide(assignedSide))
                return assignedSide;

            BattleSideEnum runtimeSide =
                missionPeer.Team != null && !ReferenceEquals(missionPeer.Team, mission?.SpectatorTeam)
                    ? missionPeer.Team.Side
                    : BattleSideEnum.None;
            if (runtimeSide != BattleSideEnum.None)
                CoopBattleAuthorityState.TryRequestSide(missionPeer, runtimeSide, source + " runtime-team");

            BattleSideEnum requestedOrRuntimeSide = requestedSide != BattleSideEnum.None ? requestedSide : runtimeSide;
            if (!HasAllowedRosterForSide(requestedOrRuntimeSide))
                return BattleSideEnum.None;

            CoopBattleAuthorityState.TryAssignSide(missionPeer, requestedOrRuntimeSide, source);
            return requestedOrRuntimeSide;
        }

        private static bool HasAllowedRosterForSide(BattleSideEnum side)
        {
            if (side == BattleSideEnum.None)
                return false;

            if (AllowedControlEntryStatesBySide.TryGetValue(side, out List<RosterEntryState> sideEntryStates) && sideEntryStates.Count > 0)
                return true;

            if (AllowedControlTroopIdsBySide.TryGetValue(side, out List<string> sideTroopIds) && sideTroopIds.Count > 0)
                return true;

            string canonicalSideKey = side == BattleSideEnum.Attacker
                ? "attacker"
                : side == BattleSideEnum.Defender
                    ? "defender"
                    : null;
            if (string.IsNullOrWhiteSpace(canonicalSideKey))
                return false;

            BattleSideState sideState = BattleSnapshotRuntimeState.GetSideState(canonicalSideKey);
            return sideState?.Entries != null && sideState.Entries.Count > 0;
        }

        private static IReadOnlyList<string> GetFixedTestAllowedControlTroopIdsForSide(BattleSideEnum side)
        {
            if (!EnableFixedMissionCulturesExperiment)
                return Array.Empty<string>();

            if (side == BattleSideEnum.Attacker)
            {
                return new[]
                {
                    "mp_coop_light_cavalry_empire_troop",
                    "mp_coop_heavy_infantry_empire_troop",
                };
            }

            if (side == BattleSideEnum.Defender)
            {
                return new[]
                {
                    "mp_coop_light_cavalry_vlandia_troop",
                    "mp_coop_heavy_infantry_vlandia_troop",
                };
            }

            return Array.Empty<string>();
        }

        public static IReadOnlyList<string> GetFixedTestAllowedControlTroopIdsForSidePublic(BattleSideEnum side)
        {
            return GetFixedTestAllowedControlTroopIdsForSide(side);
        }

        private static void TryRefreshPendingSpawnRequests(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer || GameNetwork.NetworkPeers == null)
                return;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
                    continue;

                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                if (missionPeer == null)
                    continue;

                Agent controlledAgent = missionPeer.ControlledAgent;
                bool hasActiveControlledAgent = HasActiveControlledAgent(missionPeer);
                bool hadActiveCoopLifeRuntime = IsPeerOccupyingActiveCoopLife(missionPeer);
                bool lostControlledAgent =
                    hadActiveCoopLifeRuntime &&
                    (!hasActiveControlledAgent ||
                     missionPeer.Team == null ||
                     ReferenceEquals(missionPeer.Team, mission.SpectatorTeam));
                if (lostControlledAgent)
                {
                    if (controlledAgent != null && !controlledAgent.IsActive())
                    {
                        missionPeer.ControlledAgent = null;
                        missionPeer.FollowedAgent = null;
                        if (ReferenceEquals(peer.ControlledAgent, controlledAgent))
                            peer.ControlledAgent = null;
                    }

                    string lastTroopId = (controlledAgent?.Character as BasicCharacterObject)?.StringId ?? CoopBattleAuthorityState.GetSelectedTroopId(missionPeer);
                    string lastEntryId = CoopBattleAuthorityState.GetSelectedEntryId(missionPeer);
                    _spawnedCoopPeerIndices.Remove(peer.Index);
                    CoopBattleSelectionRequestState.Clear(missionPeer, source + " lost-controlled-agent");
                    CoopBattleSpawnRequestState.Clear(missionPeer, source + " lost-controlled-agent");
                    ExpireMissionPeerVanillaSpawnVisuals(missionPeer);
                    CoopBattleSpawnRuntimeState.Clear(missionPeer, source + " lost-controlled-agent");
                    MovePeerToSpectatorHoldingState(mission, missionPeer, peer, source + " lost-controlled-agent");
                    CoopBattlePeerLifecycleRuntimeState.MarkDeadAwaitingRespawn(missionPeer, lastTroopId, lastEntryId, source + " lost-controlled-agent");
                    ModLogger.Info(
                        "CoopMissionSpawnLogic: peer returned to respawnable state (" + source + "). " +
                        "Peer=" + (peer.UserName ?? peer.Index.ToString()));
                }

                if (HasActiveControlledAgent(missionPeer))
                {
                    CoopBattleSelectionRequestState.Clear(missionPeer, source + " controlled-agent");
                    CoopBattleSpawnRequestState.Clear(missionPeer, source + " controlled-agent");
                    CoopBattleSpawnRuntimeState.MarkSpawned(
                        missionPeer,
                        (missionPeer.ControlledAgent.Character as BasicCharacterObject)?.StringId,
                        CoopBattleAuthorityState.GetSelectedEntryId(missionPeer),
                        source + " controlled-agent");
                    CoopBattlePeerLifecycleRuntimeState.MarkAlive(
                        missionPeer,
                        (missionPeer.ControlledAgent.Character as BasicCharacterObject)?.StringId,
                        CoopBattleAuthorityState.GetSelectedEntryId(missionPeer),
                        source + " controlled-agent");
                    continue;
                }

                if (missionPeer.Team == null || ReferenceEquals(missionPeer.Team, mission.SpectatorTeam))
                {
                    BattleSideEnum spectatorAuthoritativeSide = ResolveAuthoritativeSide(missionPeer, mission, source + " spectator-pending-request");
                    if (spectatorAuthoritativeSide == BattleSideEnum.None)
                    {
                        CoopBattleSelectionRequestState.Clear(missionPeer, source + " spectator");
                        CoopBattleSpawnRequestState.Clear(missionPeer, source + " spectator");
                        CoopBattleSpawnRuntimeState.Clear(missionPeer, source + " spectator");
                        CoopBattlePeerLifecycleRuntimeState.MarkNoSide(missionPeer, BattleSideEnum.None, source + " spectator");
                        continue;
                    }
                }

                BattleSideEnum authoritativeSide = ResolveAuthoritativeSide(missionPeer, mission, source + " pending-request");
                if (authoritativeSide == BattleSideEnum.None)
                {
                    CoopBattleSelectionRequestState.Clear(missionPeer, source + " no-side");
                    CoopBattleSpawnRequestState.Clear(missionPeer, source + " no-side");
                    CoopBattleSpawnRuntimeState.Clear(missionPeer, source + " no-side");
                    CoopBattlePeerLifecycleRuntimeState.MarkNoSide(missionPeer, BattleSideEnum.None, source + " no-side");
                    continue;
                }

                CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
                bool hasMeaningfulSelection =
                    !string.IsNullOrWhiteSpace(selectionState.EntryId) ||
                    !string.IsNullOrWhiteSpace(selectionState.TroopId);
                if (!hasMeaningfulSelection)
                {
                    CoopBattleSelectionRequestState.Clear(missionPeer, source + " no-selection");
                    CoopBattleSpawnRequestState.Clear(missionPeer, source + " no-selection");
                    CoopBattleSpawnRuntimeState.Clear(missionPeer, source + " no-selection");
                    CoopBattlePeerLifecycleRuntimeState.MarkAwaitingSelection(missionPeer, authoritativeSide, source + " no-selection");
                    continue;
                }

                if (CoopBattleSpawnRequestState.TryGetPendingRequest(missionPeer, out CoopBattleSpawnRequestState.PeerSpawnRequestState existingPendingRequest))
                {
                    CoopBattleSpawnRuntimeState.MarkPending(existingPendingRequest);
                    CoopBattlePeerLifecycleRuntimeState.MarkSpawnQueued(
                        missionPeer,
                        existingPendingRequest.TroopId,
                        existingPendingRequest.EntryId,
                        source + " existing-pending-request");
                    continue;
                }

                if (!ShouldAutoQueueSelectionFromAuthority(missionPeer))
                {
                    CoopBattleSelectionRequestState.Clear(missionPeer, source + " explicit-spawn-required");
                    CoopBattleSpawnRequestState.Clear(missionPeer, source + " explicit-spawn-required");
                    if (!CoopBattlePeerLifecycleRuntimeState.TryGetState(missionPeer, out PeerLifecycleRuntimeState lifecycleState) ||
                        (lifecycleState.Status != CoopBattlePeerLifecycleStatus.Respawnable &&
                         lifecycleState.Status != CoopBattlePeerLifecycleStatus.DeadAwaitingRespawn))
                    {
                        CoopBattlePeerLifecycleRuntimeState.MarkWaiting(
                            missionPeer,
                            authoritativeSide,
                            selectionState.TroopId,
                            selectionState.EntryId,
                            source + " waiting-explicit-spawn");
                    }

                    continue;
                }

                CoopBattleSelectionRequestState.TryQueueFromAuthoritySelection(missionPeer, source);
                if (CoopBattleSelectionRequestState.TryGetRequest(missionPeer, out CoopBattleSelectionRequestState.PeerSelectionRequestState selectionRequest) &&
                    CoopBattleSpawnRequestState.TryQueueFromSelectionRequest(selectionRequest, source) &&
                    CoopBattleSpawnRequestState.TryGetPendingRequest(missionPeer, out CoopBattleSpawnRequestState.PeerSpawnRequestState pendingRequest))
                {
                    CoopBattleSpawnRuntimeState.MarkPending(pendingRequest);
                    CoopBattlePeerLifecycleRuntimeState.MarkSpawnQueued(missionPeer, pendingRequest.TroopId, pendingRequest.EntryId, source + " pending-request");
                }
                else
                {
                    CoopBattlePeerLifecycleRuntimeState.MarkWaiting(missionPeer, authoritativeSide, selectionState.TroopId, selectionState.EntryId, source + " waiting");
                }
            }
        }

        private static bool HasActiveControlledAgent(MissionPeer missionPeer)
        {
            Agent controlledAgent = missionPeer?.ControlledAgent;
            return controlledAgent != null && controlledAgent.IsActive();
        }

        private static bool IsPeerOccupyingActiveCoopLife(MissionPeer missionPeer)
        {
            if (missionPeer == null)
                return false;

            if (HasActiveControlledAgent(missionPeer))
                return true;

            if (CoopBattlePeerLifecycleRuntimeState.TryGetState(missionPeer, out PeerLifecycleRuntimeState lifecycleState) &&
                lifecycleState.Status == CoopBattlePeerLifecycleStatus.Alive)
            {
                return true;
            }

            if (CoopBattleSpawnRuntimeState.TryGetState(missionPeer, out PeerSpawnRuntimeState spawnState) &&
                spawnState.Status == CoopBattleSpawnStatus.Spawned)
            {
                return true;
            }

            return false;
        }

        private static bool IsPeerHoldingQueuedSpawnInCoopRuntime(MissionPeer missionPeer)
        {
            if (missionPeer == null)
                return false;

            if (CoopBattleSpawnRequestState.HasPendingRequest(missionPeer))
                return true;

            if (CoopBattlePeerLifecycleRuntimeState.TryGetState(missionPeer, out PeerLifecycleRuntimeState lifecycleState) &&
                lifecycleState.Status == CoopBattlePeerLifecycleStatus.SpawnQueued)
            {
                return true;
            }

            if (CoopBattleSpawnRuntimeState.TryGetState(missionPeer, out PeerSpawnRuntimeState spawnState))
            {
                return spawnState.Status == CoopBattleSpawnStatus.Pending ||
                       spawnState.Status == CoopBattleSpawnStatus.Validating ||
                       spawnState.Status == CoopBattleSpawnStatus.Validated;
            }

            return false;
        }

        private static bool ShouldAutoQueueSelectionFromAuthority(MissionPeer missionPeer)
        {
            return false;
        }

        private static void TryConsumeSelectionRequests(Mission mission)
        {
            if (mission == null || !GameNetwork.IsServer)
                return;

            if (CoopBattleSelectionBridgeFile.ConsumeSelectSideRequest(out string requestedSideRaw, out string sideSource))
                StoreSelectionSideIntent(requestedSideRaw, sideSource ?? "bridge side request");

            if (CoopBattleSelectionBridgeFile.ConsumeSelectTroopRequest(out string troopOrEntryId, out string troopSource))
                StoreSelectionTroopIntent(troopOrEntryId, troopSource ?? "bridge troop request");

            if (CoopBattleSelectionBridgeFile.ConsumeSpectatorRequest(out string spectatorSource))
                TryApplySpectatorSelectionToPrimaryPeer(mission, spectatorSource ?? "bridge spectator request");
        }

        private static void TryConsumeSpawnRequests(Mission mission)
        {
            if (mission == null || !GameNetwork.IsServer)
                return;

            if (CoopBattleSpawnBridgeFile.ConsumeSpawnNowRequest(out string source))
                CoopBattleSpawnIntentState.RequestSpawn(source ?? "bridge spawn request");

            if (CoopBattleSpawnBridgeFile.ConsumeForceRespawnableRequest(out string respawnableSource))
                TryForcePrimaryPeerRespawnable(mission, respawnableSource ?? "bridge force-respawnable request");
        }

        private static void TryForcePrimaryPeerRespawnable(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer)
                return;

            MissionPeer missionPeer = ResolvePrimaryControllablePeer(mission);
            if (missionPeer == null)
            {
                ModLogger.Info("CoopMissionSpawnLogic: force-respawnable ignored (" + source + "). Peer=none");
                return;
            }

            NetworkCommunicator peer = missionPeer.GetNetworkPeer();
            Agent controlledAgent = missionPeer.ControlledAgent;
            bool triggeredVanillaRemoval = false;
            bool returnedMaterializedAgentToAi = false;
            if (controlledAgent != null && controlledAgent.IsActive())
            {
                if (IsMaterializedArmyAgent(controlledAgent))
                {
                    returnedMaterializedAgentToAi = TryReturnMaterializedAgentControlToAi(missionPeer, peer, controlledAgent, source);
                }
                else
                {
                    try
                    {
                        mission.KillAgentCheat(controlledAgent);
                        triggeredVanillaRemoval = true;
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Info("CoopMissionSpawnLogic: failed to kill controlled agent during force-respawnable: " + ex.Message);
                    }
                }
            }

            bool resetSucceeded =
                controlledAgent == null ||
                !controlledAgent.IsActive() ||
                triggeredVanillaRemoval ||
                returnedMaterializedAgentToAi;
            if (!resetSucceeded)
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: force-respawnable ignored because controlled agent is still active. " +
                    "Peer=" + (peer?.UserName ?? peer?.Index.ToString() ?? "none") +
                    " AgentIndex=" + controlledAgent.Index +
                    " Source=" + source);
                return;
            }

            missionPeer.ControlledAgent = null;
            missionPeer.FollowedAgent = null;
            if (peer != null && ReferenceEquals(peer.ControlledAgent, controlledAgent))
                peer.ControlledAgent = null;
            if (peer != null)
                _spawnedCoopPeerIndices.Remove(peer.Index);

            ExpireMissionPeerVanillaSpawnVisuals(missionPeer);
            ResetMissionPeerSpawnTimerForImmediateRespawn(mission, missionPeer);
            missionPeer.WantsToSpawnAsBot = false;
            MovePeerToSpectatorHoldingState(mission, missionPeer, peer, source + " forced-respawnable");
            CoopBattleSelectionRequestState.Clear(missionPeer, source + " forced-respawnable");
            CoopBattleSpawnRequestState.Clear(missionPeer, source + " forced-respawnable");
            CoopBattleSpawnRuntimeState.Clear(missionPeer, source + " forced-respawnable");
            CoopBattlePeerLifecycleRuntimeState.MarkRespawnable(
                missionPeer,
                CoopBattleAuthorityState.GetSelectedTroopId(missionPeer),
                CoopBattleAuthorityState.GetSelectedEntryId(missionPeer),
                source + " forced-respawnable");

            ModLogger.Info(
                "CoopMissionSpawnLogic: forced peer into respawnable state. " +
                "Peer=" + (peer?.UserName ?? peer?.Index.ToString() ?? "none") +
                " TriggeredVanillaRemoval=" + triggeredVanillaRemoval +
                " ReturnedMaterializedAgentToAi=" + returnedMaterializedAgentToAi +
                " Source=" + source);
        }

        private static bool IsMaterializedArmyAgent(Agent agent)
        {
            return agent != null &&
                (_materializedArmySideByAgentIndex.ContainsKey(agent.Index) ||
                 ExactCampaignArmyBootstrap.TryGetEntryId(agent, out _));
        }

        private static void ExpireMissionPeerVanillaSpawnVisuals(MissionPeer missionPeer)
        {
            if (missionPeer == null)
                return;

            missionPeer.HasSpawnedAgentVisuals = false;
            missionPeer.EquipmentUpdatingExpired = true;
        }

        private static void ResetMissionPeerSpawnTimerForImmediateRespawn(Mission mission, MissionPeer missionPeer)
        {
            if (mission == null || missionPeer == null)
                return;

            if (missionPeer.SpawnTimer != null)
                missionPeer.SpawnTimer.Reset(mission.CurrentTime, 0f);
            missionPeer.HasSpawnTimerExpired = true;
        }

        private static void MovePeerToSpectatorHoldingState(
            Mission mission,
            MissionPeer missionPeer,
            NetworkCommunicator peer,
            string source)
        {
            if (mission == null || missionPeer == null)
                return;

            Team spectatorTeam = mission.SpectatorTeam;
            if (spectatorTeam == null)
                return;

            Team previousTeam = missionPeer.Team;
            bool alreadySpectator = ReferenceEquals(previousTeam, spectatorTeam);

            missionPeer.ControlledFormation = null;
            TrySetBotsUnderControlTotal(missionPeer, 0);
            missionPeer.BotsUnderControlAlive = 0;
            missionPeer.WantsToSpawnAsBot = false;
            ExpireMissionPeerVanillaSpawnVisuals(missionPeer);

            if (!alreadySpectator)
            {
                SetServerMemberValue(missionPeer, "Team", spectatorTeam);
                if (!ReferenceEquals(GetServerMemberValue(missionPeer, "Team"), spectatorTeam))
                    SetServerMemberValue(missionPeer, "_team", spectatorTeam);
                if (!ReferenceEquals(GetServerMemberValue(missionPeer, "Team"), spectatorTeam))
                    SetServerMemberValue(missionPeer, "<Team>k__BackingField", spectatorTeam);

                Team appliedTeam = GetServerMemberValue(missionPeer, "Team") as Team;
                if (!ReferenceEquals(appliedTeam, spectatorTeam))
                    appliedTeam = GetServerMemberValue(missionPeer, "_team") as Team;

                if (ReferenceEquals(appliedTeam, spectatorTeam))
                {
                    if (peer != null)
                        _lastBridgedPeerTeamIndexByPeer.Remove(peer.Index);

                    try
                    {
                        if (peer != null)
                        {
                            GameNetwork.BeginBroadcastModuleEvent();
                            GameNetwork.WriteMessage(new NetworkMessages.FromServer.SetPeerTeam(peer, spectatorTeam.TeamIndex));
                            GameNetwork.EndBroadcastModuleEvent(GameNetwork.EventBroadcastFlags.None);
                        }
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Info(
                            "CoopMissionSpawnLogic: rebroadcast spectator holding team failed. " +
                            "Peer=" + (peer?.UserName ?? peer?.Index.ToString() ?? "none") +
                            " Source=" + (source ?? "unknown") +
                            " Error=" + ex.Message);
                    }
                }
            }

            ModLogger.Info(
                "CoopMissionSpawnLogic: moved peer to spectator holding state. " +
                "Peer=" + (peer?.UserName ?? peer?.Index.ToString() ?? "none") +
                " PreviousTeamIndex=" + (previousTeam?.TeamIndex ?? -1) +
                " AppliedTeamIndex=" + (missionPeer.Team?.TeamIndex ?? -1) +
                " AppliedIsSpectator=" + ReferenceEquals(missionPeer.Team, spectatorTeam) +
                " Source=" + (source ?? "unknown"));
        }

        private static bool TryReturnMaterializedAgentControlToAi(
            MissionPeer missionPeer,
            NetworkCommunicator peer,
            Agent controlledAgent,
            string source)
        {
            if (missionPeer == null || controlledAgent == null || !controlledAgent.IsActive())
                return false;

            try
            {
                Formation controlledFormation = controlledAgent.Formation;
                controlledAgent.SetOwningAgentMissionPeer(null);
                controlledAgent.MissionPeer = null;
                controlledAgent.Controller = AgentControllerType.AI;
                controlledAgent.SetAutomaticTargetSelection(true);
                controlledAgent.SetFiringOrder(FiringOrder.RangedWeaponUsageOrderEnum.FireAtWill);
                controlledAgent.WieldInitialWeapons(
                    Agent.WeaponWieldActionType.Instant,
                    Equipment.InitialWeaponEquipPreference.Any);

                if (peer != null)
                {
                    GameNetwork.BeginModuleEventAsServer(peer.VirtualPlayer);
                    GameNetwork.WriteMessage(new NetworkMessages.FromServer.SetAgentIsPlayer(controlledAgent.Index, false));
                    GameNetwork.WriteMessage(new NetworkMessages.FromServer.SetAgentOwningMissionPeer(controlledAgent.Index, null));
                    GameNetwork.EndModuleEventAsServer();
                }

                bool resetFormationPlayerState = TryResetMaterializedFormationPlayerState(
                    missionPeer,
                    controlledFormation,
                    controlledAgent,
                    source);

                missionPeer.ControlledAgent = null;
                missionPeer.FollowedAgent = null;
                missionPeer.ControlledFormation = null;
                TrySetBotsUnderControlTotal(missionPeer, 0);
                missionPeer.BotsUnderControlAlive = 0;
                if (peer != null && ReferenceEquals(peer.ControlledAgent, controlledAgent))
                    peer.ControlledAgent = null;

                ModLogger.Info(
                    "CoopMissionSpawnLogic: returned materialized agent control to AI for respawn. " +
                    "Peer=" + (peer?.UserName ?? peer?.Index.ToString() ?? "none") +
                    " AgentIndex=" + controlledAgent.Index +
                    " Formation=" + (controlledFormation?.FormationIndex.ToString() ?? "none") +
                    " ResetFormationPlayerState=" + resetFormationPlayerState +
                    " Source=" + (source ?? "unknown"));
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: failed to return materialized agent control to AI during force-respawnable: " + ex.Message);
                return false;
            }
        }

        private static bool TryResetMaterializedFormationPlayerState(
            MissionPeer missionPeer,
            Formation formation,
            Agent controlledAgent,
            string source)
        {
            if (formation == null)
                return false;

            bool changed = false;
            try
            {
                object playerOwner = GetServerMemberValue(formation, "PlayerOwner");
                if (ReferenceEquals(playerOwner, controlledAgent))
                {
                    SetServerMemberValue(formation, "PlayerOwner", null);
                    changed = true;
                }

                bool hadPlayerControlledTroop = (GetServerMemberValue(formation, "HasPlayerControlledTroop") as bool?) ?? false;
                SetServerMemberValue(formation, "HasPlayerControlledTroop", false);
                if (hadPlayerControlledTroop && !((GetServerMemberValue(formation, "HasPlayerControlledTroop") as bool?) ?? true))
                    changed = true;

                bool hadPlayerTroopInFormation = (GetServerMemberValue(formation, "IsPlayerTroopInFormation") as bool?) ?? false;
                SetServerMemberValue(formation, "IsPlayerTroopInFormation", false);
                if (hadPlayerTroopInFormation && !((GetServerMemberValue(formation, "IsPlayerTroopInFormation") as bool?) ?? true))
                    changed = true;

                bool wasAiControlled = (GetServerMemberValue(formation, "IsAIControlled") as bool?) ?? false;
                SetServerMemberValue(formation, "IsAIControlled", true);
                if (!wasAiControlled && ((GetServerMemberValue(formation, "IsAIControlled") as bool?) ?? false))
                    changed = true;

                MethodInfo setControlledByAiMethod = formation.GetType().GetMethod(
                    "SetControlledByAI",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(bool), typeof(bool) },
                    null);
                if (setControlledByAiMethod != null)
                {
                    setControlledByAiMethod.Invoke(formation, new object[] { true, true });
                    changed = true;
                }

                MethodInfo onAgentControllerChangedMethod = formation.GetType().GetMethod(
                    "OnAgentControllerChanged",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(Agent), typeof(AgentControllerType) },
                    null);
                if (onAgentControllerChangedMethod != null)
                {
                    onAgentControllerChangedMethod.Invoke(formation, new object[] { controlledAgent, AgentControllerType.AI });
                    changed = true;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: failed to reset materialized formation player state. " +
                    "Formation=" + formation.FormationIndex +
                    " Source=" + (source ?? "unknown") +
                    " Error=" + ex.Message);
            }

            if (changed)
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: reset materialized formation player state. " +
                    "Formation=" + formation.FormationIndex +
                    " MissionPeer=" + (missionPeer?.GetNetworkPeer()?.UserName ?? missionPeer?.GetNetworkPeer()?.Index.ToString() ?? "none") +
                    " AgentIndex=" + controlledAgent.Index +
                    " Source=" + (source ?? "unknown"));
            }

            return changed;
        }

        private static void StoreSelectionSideIntent(string requestedSideRaw, string source)
        {
            if (!TryParseBattleSide(requestedSideRaw, out BattleSideEnum requestedSide))
            {
                ModLogger.Info("CoopMissionSpawnLogic: ignored invalid side selection request. Value=" + (requestedSideRaw ?? "null"));
                return;
            }

            CoopBattleSelectionIntentState.UpdateSide(requestedSide, source + " side-intent");
        }

        private static void StoreSelectionTroopIntent(string troopOrEntryId, string source)
        {
            if (string.IsNullOrWhiteSpace(troopOrEntryId))
                return;

            CoopBattleSelectionIntentState.UpdateTroopOrEntry(troopOrEntryId, source + " troop-intent");
        }

        private static void TryApplySpectatorSelectionToPrimaryPeer(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer)
                return;

            MissionPeer missionPeer = ResolvePrimaryControllablePeer(mission);
            if (missionPeer == null)
            {
                ModLogger.Info("CoopMissionSpawnLogic: spectator selection ignored (" + source + "). Peer=none");
                return;
            }

            NetworkCommunicator peer = missionPeer.GetNetworkPeer();
            if (HasActiveControlledAgent(missionPeer))
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: spectator selection ignored because peer still controls an active agent. " +
                    "Peer=" + (peer?.UserName ?? peer?.Index.ToString() ?? "none") +
                    " Source=" + source);
                return;
            }

            CoopBattleSelectionIntentState.Reset();
            CoopBattleAuthorityState.ClearPeerSelectionAndSide(missionPeer, source + " spectator-request");
            CoopBattleSelectionRequestState.Clear(missionPeer, source + " spectator-request");
            CoopBattleSpawnRequestState.Clear(missionPeer, source + " spectator-request");
            CoopBattleSpawnRuntimeState.Clear(missionPeer, source + " spectator-request");
            missionPeer.WantsToSpawnAsBot = false;
            MovePeerToSpectatorHoldingState(mission, missionPeer, peer, source + " spectator-request");
            CoopBattlePeerLifecycleRuntimeState.MarkNoSide(missionPeer, BattleSideEnum.None, source + " spectator-request");

            ModLogger.Info(
                "CoopMissionSpawnLogic: applied spectator selection request. " +
                "Peer=" + (peer?.UserName ?? peer?.Index.ToString() ?? "none") +
                " TeamIndex=" + (missionPeer.Team?.TeamIndex ?? -1) +
                " Source=" + source);
        }

        private static void TryApplySelectionIntentToPrimaryPeer(Mission mission, string source)
        {
            MissionPeer missionPeer = ResolvePrimaryControllablePeer(mission);
            if (missionPeer == null)
                return;

            bool peerOccupiesActiveCoopLife = IsPeerOccupyingActiveCoopLife(missionPeer);
            BattleSideEnum runtimeSide =
                missionPeer.Team != null && !ReferenceEquals(missionPeer.Team, mission?.SpectatorTeam)
                    ? missionPeer.Team.Side
                    : BattleSideEnum.None;

            CoopBattleSelectionIntentSnapshot intent = CoopBattleSelectionIntentState.GetCurrent();
            bool deferCrossSideSelectionWhileActive =
                peerOccupiesActiveCoopLife &&
                intent.Side != BattleSideEnum.None &&
                runtimeSide != BattleSideEnum.None &&
                runtimeSide != intent.Side;
            if (intent.Side != BattleSideEnum.None)
            {
                CoopBattleAuthorityState.TryRequestSide(missionPeer, intent.Side, source + " side-intent");
                if (deferCrossSideSelectionWhileActive)
                {
                    CoopBattleSelectionRequestState.Clear(missionPeer, source + " deferred-side-change");
                    CoopBattleSpawnRequestState.Clear(missionPeer, source + " deferred-side-change");
                }
                else if (HasAllowedRosterForSide(intent.Side))
                    CoopBattleAuthorityState.TryAssignSide(missionPeer, intent.Side, source + " side-intent");
            }

            if (string.IsNullOrWhiteSpace(intent.TroopOrEntryId) || deferCrossSideSelectionWhileActive)
                return;

            if (CoopBattleAuthorityState.TrySetSelectedEntryId(missionPeer, intent.TroopOrEntryId, source + " intent entry"))
            {
                if (ShouldAutoQueueSelectionFromAuthority(missionPeer))
                    CoopBattleSelectionRequestState.TryQueueFromAuthoritySelection(missionPeer, source + " selection-request");
                return;
            }

            if (CoopBattleAuthorityState.TrySetSelectedTroopId(missionPeer, intent.TroopOrEntryId, source + " intent troop"))
            {
                if (ShouldAutoQueueSelectionFromAuthority(missionPeer))
                    CoopBattleSelectionRequestState.TryQueueFromAuthoritySelection(missionPeer, source + " selection-request");
            }
        }

        private static void TryApplySpawnIntentToPrimaryPeer(Mission mission, string source)
        {
            CoopBattleSpawnIntentSnapshot intent = CoopBattleSpawnIntentState.GetCurrent();
            if (intent == null || !intent.IsRequested)
                return;

            MissionPeer missionPeer = ResolvePrimaryControllablePeer(mission);
            if (missionPeer == null)
                return;

            if (missionPeer.ControlledAgent != null)
            {
                CoopBattleSpawnIntentState.Clear(source + " already-controlled");
                return;
            }

            CoopBattleSelectionRequestState.TryQueueFromAuthoritySelection(missionPeer, source + " spawn-intent selection-request");
            if (!CoopBattleSelectionRequestState.TryGetRequest(missionPeer, out CoopBattleSelectionRequestState.PeerSelectionRequestState selectionRequest))
                return;

            if (!CoopBattleSpawnRequestState.TryQueueFromSelectionRequest(selectionRequest, source + " spawn-intent"))
                return;

            if (CoopBattleSpawnRequestState.TryGetPendingRequest(missionPeer, out CoopBattleSpawnRequestState.PeerSpawnRequestState pendingRequest))
            {
                CoopBattleSpawnRuntimeState.MarkPending(pendingRequest);
                CoopBattleSpawnIntentState.Clear(source + " queued");
            }
        }

        private static MissionPeer ResolvePrimaryControllablePeer(Mission mission)
        {
            if (mission == null || GameNetwork.NetworkPeers == null)
                return null;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
                    continue;

                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                if (missionPeer == null)
                    continue;

                return missionPeer;
            }

            return null;
        }

        private static IReadOnlyList<string> ResolveSelectableEntryIdsForStatus(
            Mission mission,
            BattleSideEnum side,
            CoopBattlePhase currentPhase,
            out string source)
        {
            source = "none";
            if (side == BattleSideEnum.None)
                return Array.Empty<string>();

            if (currentPhase < CoopBattlePhase.BattleActive)
            {
                source = "allowed-prebattle";
                return CoopBattleAuthorityState.GetAllowedEntryIds(side)?
                           .Where(entryId => !string.IsNullOrWhiteSpace(entryId))
                           .Distinct(StringComparer.Ordinal)
                           .ToArray()
                       ?? Array.Empty<string>();
            }

            List<string> liveSelectableEntryIds = GetLiveSelectableEntryIdsSnapshot(mission, side);
            if (liveSelectableEntryIds.Count > 0)
            {
                source = "live-eligible";
                return liveSelectableEntryIds;
            }

            if (currentPhase >= CoopBattlePhase.BattleActive || HasTrackedBattlefieldEntryStateForSide(mission, side))
            {
                source = "live-empty";
                return Array.Empty<string>();
            }

            source = "allowed-fallback";
            return CoopBattleAuthorityState.GetAllowedEntryIds(side)?
                       .Where(entryId => !string.IsNullOrWhiteSpace(entryId))
                       .Distinct(StringComparer.Ordinal)
                       .ToArray()
                   ?? Array.Empty<string>();
        }

        private static List<string> GetLiveSelectableEntryIdsSnapshot(Mission mission, BattleSideEnum side)
        {
            if (mission?.AllAgents == null || side == BattleSideEnum.None)
                return new List<string>();

            var liveEntryIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < mission.AllAgents.Count; i++)
            {
                Agent candidate = mission.AllAgents[i];
                if (!IsSelectableMaterializedArmyAgent(candidate, side, out string entryId))
                    continue;

                liveEntryIds.Add(entryId);
            }

            return OrderSelectableEntryIds(side, liveEntryIds);
        }

        private static bool IsSelectableMaterializedArmyAgent(Agent candidate, BattleSideEnum side, out string entryId)
        {
            entryId = null;
            if (candidate == null ||
                !candidate.IsActive() ||
                candidate.IsMount ||
                candidate.MissionPeer != null ||
                candidate.Controller == AgentControllerType.Player)
            {
                return false;
            }

            return DoesAgentMatchSelectableSide(candidate, side) &&
                   TryResolveSelectableEntryId(candidate, out entryId);
        }

        private static bool DoesAgentMatchSelectableSide(Agent agent, BattleSideEnum side)
        {
            if (agent == null || side == BattleSideEnum.None)
                return false;

            if (_materializedArmySideByAgentIndex.TryGetValue(agent.Index, out BattleSideEnum trackedSide) &&
                trackedSide == side)
            {
                return true;
            }

            if (ExactCampaignArmyBootstrap.TryGetSide(agent, out BattleSideEnum originSide) &&
                originSide == side)
            {
                return true;
            }

            return agent.Team?.Side == side;
        }

        private static bool TryResolveSelectableEntryId(Agent agent, out string entryId)
        {
            entryId = null;
            if (agent == null)
                return false;

            if (_materializedArmyEntryIdByAgentIndex.TryGetValue(agent.Index, out string trackedEntryId) &&
                !string.IsNullOrWhiteSpace(trackedEntryId))
            {
                entryId = trackedEntryId;
                return true;
            }

            if (ExactCampaignArmyBootstrap.TryGetEntryId(agent, out string originEntryId) &&
                !string.IsNullOrWhiteSpace(originEntryId))
            {
                entryId = originEntryId;
                return true;
            }

            if (_exactNativeClientVisualOverlayEntryIdByAgentIndex.TryGetValue(agent.Index, out string clientOverlayEntryId) &&
                !string.IsNullOrWhiteSpace(clientOverlayEntryId) &&
                BattleSnapshotRuntimeState.GetEntryState(clientOverlayEntryId) != null)
            {
                entryId = clientOverlayEntryId;
                return true;
            }

            return false;
        }

        public static bool TryResolveExactDisplayNameForAgent(Agent agent, out string entryId, out TextObject exactName)
        {
            entryId = null;
            exactName = null;
            if (agent == null)
                return false;

            if (!TryResolveSelectableEntryId(agent, out entryId) || string.IsNullOrWhiteSpace(entryId))
                return false;

            RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(entryId);
            if (!HasEntryHeroIdentity(entryState) || string.IsNullOrWhiteSpace(entryState.TroopName))
                return false;

            exactName = new TextObject(entryState.TroopName);
            return true;
        }

        private static bool HasTrackedBattlefieldEntryStateForSide(Mission mission, BattleSideEnum side)
        {
            if (side == BattleSideEnum.None)
                return false;

            string sideId = side.ToString();
            foreach (MaterializedBattleResultEntryRuntimeState runtimeState in _materializedBattleResultEntriesByEntryId.Values)
            {
                if (runtimeState == null ||
                    runtimeState.MaterializedSpawnCount <= 0 ||
                    !string.Equals(runtimeState.SideId, sideId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return true;
            }

            if (mission?.AllAgents == null)
                return false;

            for (int i = 0; i < mission.AllAgents.Count; i++)
            {
                Agent candidate = mission.AllAgents[i];
                if (candidate == null || candidate.IsMount || !DoesAgentMatchSelectableSide(candidate, side))
                    continue;

                if (TryResolveSelectableEntryId(candidate, out _))
                    return true;
            }

            return false;
        }

        private static List<string> OrderSelectableEntryIds(BattleSideEnum side, IEnumerable<string> entryIds)
        {
            var pendingEntryIds = new HashSet<string>(
                (entryIds ?? Enumerable.Empty<string>())
                .Where(entryId => !string.IsNullOrWhiteSpace(entryId)),
                StringComparer.Ordinal);
            var orderedEntryIds = new List<string>(pendingEntryIds.Count);
            if (pendingEntryIds.Count <= 0)
                return orderedEntryIds;

            BattleSideState sideState = BattleSnapshotRuntimeState.GetSideState(side.ToString());
            if (sideState?.MissionReadyEntryOrder != null)
            {
                foreach (string missionReadyEntryId in sideState.MissionReadyEntryOrder)
                {
                    if (!pendingEntryIds.Remove(missionReadyEntryId))
                        continue;

                    orderedEntryIds.Add(missionReadyEntryId);
                }
            }

            foreach (RosterEntryState entryState in GetAllowedControlEntryStatesSnapshot(side))
            {
                string entryId = entryState?.EntryId;
                if (string.IsNullOrWhiteSpace(entryId) || !pendingEntryIds.Remove(entryId))
                    continue;

                orderedEntryIds.Add(entryId);
            }

            orderedEntryIds.AddRange(pendingEntryIds.OrderBy(entryId => entryId, StringComparer.Ordinal));
            return orderedEntryIds;
        }

        private static void TryLogSelectableEntryUniverse(
            Mission mission,
            CoopBattlePhase currentPhase,
            string attackerSource,
            IReadOnlyList<string> attackerEntryIds,
            string defenderSource,
            IReadOnlyList<string> defenderEntryIds,
            BattleSideEnum currentSide,
            string currentSource,
            IReadOnlyList<string> currentEntryIds)
        {
            string logKey =
                (mission?.SceneName ?? "null") + "|" +
                currentPhase + "|" +
                (attackerSource ?? "unknown") + "|" +
                string.Join(",", attackerEntryIds ?? Array.Empty<string>()) + "|" +
                (defenderSource ?? "unknown") + "|" +
                string.Join(",", defenderEntryIds ?? Array.Empty<string>()) + "|" +
                currentSide + "|" +
                (currentSource ?? "unknown") + "|" +
                string.Join(",", currentEntryIds ?? Array.Empty<string>());
            if (string.Equals(_lastLoggedSelectableEntryUniverseKey, logKey, StringComparison.Ordinal))
                return;

            _lastLoggedSelectableEntryUniverseKey = logKey;
            ModLogger.Info(
                "CoopMissionSpawnLogic: selectable entry universe updated. " +
                "Mission=" + (mission?.SceneName ?? "null") +
                " Phase=" + currentPhase +
                " AttackerSource=" + (attackerSource ?? "unknown") +
                " AttackerCount=" + (attackerEntryIds?.Count ?? 0) +
                " AttackerSample=[" + BuildSelectableEntrySample(attackerEntryIds) + "]" +
                " DefenderSource=" + (defenderSource ?? "unknown") +
                " DefenderCount=" + (defenderEntryIds?.Count ?? 0) +
                " DefenderSample=[" + BuildSelectableEntrySample(defenderEntryIds) + "]" +
                " CurrentSide=" + currentSide +
                " CurrentSource=" + (currentSource ?? "unknown") +
                " CurrentCount=" + (currentEntryIds?.Count ?? 0) +
                " CurrentSample=[" + BuildSelectableEntrySample(currentEntryIds) + "]");
        }

        private static string BuildSelectableEntrySample(IReadOnlyList<string> entryIds)
        {
            if (entryIds == null || entryIds.Count <= 0)
                return string.Empty;

            return string.Join(", ", entryIds.Where(entryId => !string.IsNullOrWhiteSpace(entryId)).Take(6));
        }

        private static void TryWriteEntryStatusSnapshot(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer)
                return;

            MissionPeer missionPeer = ResolvePrimaryControllablePeer(mission);
            CoopBattleSelectionIntentSnapshot selectionIntent = CoopBattleSelectionIntentState.GetCurrent();
            BattleSideEnum intentSide = selectionIntent.Side;
            CoopBattlePhaseStateSnapshot phaseSnapshot = CoopBattlePhaseRuntimeState.GetCurrent();
            CoopBattlePhase currentPhase = phaseSnapshot?.Phase ?? CoopBattlePhase.None;
            bool canStartBattle = IsBattleStartReady(mission, out int assignedPeerCount, out int controlledPeerCount) &&
                currentPhase >= CoopBattlePhase.PreBattleHold &&
                currentPhase < CoopBattlePhase.BattleActive;
            IReadOnlyList<string> attackerSelectableEntryIds = ResolveSelectableEntryIdsForStatus(
                mission,
                BattleSideEnum.Attacker,
                currentPhase,
                out string attackerSelectableSource);
            IReadOnlyList<string> defenderSelectableEntryIds = ResolveSelectableEntryIdsForStatus(
                mission,
                BattleSideEnum.Defender,
                currentPhase,
                out string defenderSelectableSource);

            var snapshot = new CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot
            {
                MissionName = mission.SceneName ?? string.Empty,
                Source = source,
                BattlePhase = currentPhase.ToString(),
                BattlePhaseSource = phaseSnapshot?.Source ?? string.Empty,
                WinnerSide = ResolveBattleResultWinnerSide(mission),
                BattleCompletionReason = _authoritativeBattleCompletionReason ?? string.Empty,
                PeerName = missionPeer?.Name?.ToString() ?? missionPeer?.GetNetworkPeer()?.UserName ?? string.Empty,
                PeerIndex = missionPeer?.GetNetworkPeer()?.Index ?? -1,
                HasPeer = missionPeer != null,
                HasAgent = missionPeer?.ControlledAgent != null && missionPeer.ControlledAgent.IsActive(),
                CanRespawn = false,
                CanStartBattle = canStartBattle,
                LifecycleState = "NoPeer",
                LifecycleSource = source,
                DeathCount = 0,
                IntentSide = intentSide == BattleSideEnum.None ? string.Empty : intentSide.ToString(),
                IntentTroopOrEntryId = selectionIntent.TroopOrEntryId,
                AttackerAllowedTroopIds = CoopBattleEntryStatusBridgeFile.SerializeIdList(CoopBattleAuthorityState.GetAllowedTroopIds(BattleSideEnum.Attacker) ?? Array.Empty<string>()),
                AttackerAllowedEntryIds = CoopBattleEntryStatusBridgeFile.SerializeIdList(CoopBattleAuthorityState.GetAllowedEntryIds(BattleSideEnum.Attacker) ?? Array.Empty<string>()),
                AttackerSelectableEntryIds = CoopBattleEntryStatusBridgeFile.SerializeIdList(attackerSelectableEntryIds),
                AttackerSelectableEntrySource = attackerSelectableSource,
                DefenderAllowedTroopIds = CoopBattleEntryStatusBridgeFile.SerializeIdList(CoopBattleAuthorityState.GetAllowedTroopIds(BattleSideEnum.Defender) ?? Array.Empty<string>()),
                DefenderAllowedEntryIds = CoopBattleEntryStatusBridgeFile.SerializeIdList(CoopBattleAuthorityState.GetAllowedEntryIds(BattleSideEnum.Defender) ?? Array.Empty<string>()),
                DefenderSelectableEntryIds = CoopBattleEntryStatusBridgeFile.SerializeIdList(defenderSelectableEntryIds),
                DefenderSelectableEntrySource = defenderSelectableSource,
                UpdatedUtc = DateTime.UtcNow
            };

            if (missionPeer != null)
            {
                CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
                snapshot.RequestedSide = selectionState.RequestedSide == BattleSideEnum.None ? string.Empty : selectionState.RequestedSide.ToString();
                snapshot.AssignedSide = selectionState.Side == BattleSideEnum.None ? string.Empty : selectionState.Side.ToString();
                snapshot.SelectedTroopId = selectionState.TroopId;
                snapshot.SelectedEntryId = selectionState.EntryId;
                snapshot.AllowedTroopIds = CoopBattleEntryStatusBridgeFile.SerializeIdList(CoopBattleAuthorityState.GetAllowedTroopIds(selectionState.Side) ?? Array.Empty<string>());
                snapshot.AllowedEntryIds = CoopBattleEntryStatusBridgeFile.SerializeIdList(CoopBattleAuthorityState.GetAllowedEntryIds(selectionState.Side) ?? Array.Empty<string>());

                if (CoopBattleSelectionRequestState.TryGetRequest(missionPeer, out CoopBattleSelectionRequestState.PeerSelectionRequestState selectionRequest))
                {
                    snapshot.SelectionRequestSide = selectionRequest.Side == BattleSideEnum.None ? string.Empty : selectionRequest.Side.ToString();
                    snapshot.SelectionRequestTroopId = selectionRequest.TroopId;
                    snapshot.SelectionRequestEntryId = selectionRequest.EntryId;
                }

                if (CoopBattleSpawnRequestState.TryGetPendingRequest(missionPeer, out CoopBattleSpawnRequestState.PeerSpawnRequestState spawnRequest))
                {
                    snapshot.SpawnRequestSide = spawnRequest.Side == BattleSideEnum.None ? string.Empty : spawnRequest.Side.ToString();
                    snapshot.SpawnRequestTroopId = spawnRequest.TroopId;
                    snapshot.SpawnRequestEntryId = spawnRequest.EntryId;
                }

                if (CoopBattleSpawnRuntimeState.TryGetState(missionPeer, out PeerSpawnRuntimeState spawnState))
                {
                    snapshot.SpawnStatus = spawnState.Status.ToString();
                    snapshot.SpawnReason = spawnState.Reason;
                }

                if (CoopBattlePeerLifecycleRuntimeState.TryGetState(missionPeer, out PeerLifecycleRuntimeState lifecycleState))
                {
                    snapshot.LifecycleState = lifecycleState.Status.ToString();
                    snapshot.LifecycleSource = lifecycleState.Source;
                    snapshot.DeathCount = lifecycleState.DeathCount;
                }

                snapshot.CanRespawn = CanPeerRespawn(mission, missionPeer);
                if (string.IsNullOrWhiteSpace(snapshot.LifecycleState))
                    snapshot.LifecycleState = ResolveEntryLifecycleState(mission, missionPeer, snapshot);
            }

            BattleSideEnum statusSide = BattleSideEnum.None;
            if (missionPeer != null)
            {
                if (Enum.TryParse(snapshot.AssignedSide, out BattleSideEnum assignedSide) && assignedSide != BattleSideEnum.None)
                    statusSide = assignedSide;
                else if (Enum.TryParse(snapshot.RequestedSide, out BattleSideEnum requestedSide) && requestedSide != BattleSideEnum.None)
                    statusSide = requestedSide;
            }

            if (statusSide == BattleSideEnum.None && intentSide != BattleSideEnum.None)
                statusSide = intentSide;

            if (statusSide != BattleSideEnum.None)
            {
                snapshot.AllowedTroopIds = CoopBattleEntryStatusBridgeFile.SerializeIdList(
                    CoopBattleAuthorityState.GetAllowedTroopIds(statusSide) ?? Array.Empty<string>());
                snapshot.AllowedEntryIds = CoopBattleEntryStatusBridgeFile.SerializeIdList(
                    CoopBattleAuthorityState.GetAllowedEntryIds(statusSide) ?? Array.Empty<string>());
                IReadOnlyList<string> currentSelectableEntryIds = ResolveSelectableEntryIdsForStatus(
                    mission,
                    statusSide,
                    currentPhase,
                    out string currentSelectableSource);
                snapshot.SelectableEntryIds = CoopBattleEntryStatusBridgeFile.SerializeIdList(currentSelectableEntryIds);
                snapshot.SelectableEntrySource = currentSelectableSource;
                TryLogSelectableEntryUniverse(
                    mission,
                    currentPhase,
                    attackerSelectableSource,
                    attackerSelectableEntryIds,
                    defenderSelectableSource,
                    defenderSelectableEntryIds,
                    statusSide,
                    currentSelectableSource,
                    currentSelectableEntryIds);
            }
            else
            {
                snapshot.SelectableEntryIds = string.Empty;
                snapshot.SelectableEntrySource = string.Empty;
                TryLogSelectableEntryUniverse(
                    mission,
                    currentPhase,
                    attackerSelectableSource,
                    attackerSelectableEntryIds,
                    defenderSelectableSource,
                    defenderSelectableEntryIds,
                    BattleSideEnum.None,
                    "none",
                    Array.Empty<string>());
            }

            TryLogBattleStartReadinessAudit(
                mission,
                currentPhase,
                canStartBattle,
                assignedPeerCount,
                controlledPeerCount,
                source);
            CoopBattleEntryStatusBridgeFile.WriteStatus(snapshot);
        }

        private static void TryLogBattleStartReadinessAudit(
            Mission mission,
            CoopBattlePhase currentPhase,
            bool canStartBattle,
            int assignedPeerCount,
            int controlledPeerCount,
            string source)
        {
            if (mission == null || currentPhase < CoopBattlePhase.PreBattleHold || currentPhase >= CoopBattlePhase.BattleActive)
                return;

            bool armiesReady = AreBattlefieldArmiesReadyForStart(
                mission,
                out string readinessSource,
                out int attackerActive,
                out int defenderActive);
            string logKey =
                currentPhase + "|" +
                canStartBattle + "|" +
                assignedPeerCount + "|" +
                controlledPeerCount + "|" +
                armiesReady + "|" +
                attackerActive + "|" +
                defenderActive + "|" +
                readinessSource;
            if (string.Equals(_lastLoggedBattleStartReadinessAuditKey, logKey, StringComparison.Ordinal))
                return;

            _lastLoggedBattleStartReadinessAuditKey = logKey;
            ModLogger.Info(
                "CoopMissionSpawnLogic: battle start readiness audit. " +
                "Phase=" + currentPhase +
                " CanStartBattle=" + canStartBattle +
                " AssignedPeers=" + assignedPeerCount +
                " ControlledPeers=" + controlledPeerCount +
                " ArmiesReady=" + armiesReady +
                " ArmiesReadySource=" + readinessSource +
                " AttackerActive=" + attackerActive +
                " DefenderActive=" + defenderActive +
                " MaterializedArmiesFlag=" + _hasMaterializedBattlefieldArmies +
                " ExactBootstrapActive=" + IsUsingNativeExactCampaignArmyBootstrap(mission) +
                " Source=" + (source ?? "unknown"));
        }

        private static string ResolveEntryLifecycleState(
            Mission mission,
            MissionPeer missionPeer,
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot)
        {
            if (missionPeer == null)
                return "NoPeer";

            if (snapshot != null && snapshot.HasAgent)
                return "Alive";

            BattleSideEnum authoritativeSide = ResolveAuthoritativeSide(missionPeer, mission, "entry-status lifecycle");
            if (authoritativeSide == BattleSideEnum.None)
                return "NoSide";

            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            bool hasSelection =
                !string.IsNullOrWhiteSpace(selectionState.EntryId) ||
                !string.IsNullOrWhiteSpace(selectionState.TroopId);
            if (!hasSelection)
                return "AwaitingSelection";

            if (CoopBattleSpawnRequestState.HasPendingRequest(missionPeer))
                return "SpawnQueued";

            if (snapshot != null && string.Equals(snapshot.SpawnStatus, CoopBattleSpawnStatus.Spawned.ToString(), StringComparison.OrdinalIgnoreCase))
                return "DeadAwaitingRespawn";

            return CanPeerRespawn(mission, missionPeer) ? "Respawnable" : "Waiting";
        }

        private static bool CanPeerRespawn(Mission mission, MissionPeer missionPeer)
        {
            return CanPeerRespawnFromCoopRuntime(mission, missionPeer, out _);
        }

        private static bool CanPeerRespawnFromCoopRuntime(Mission mission, MissionPeer missionPeer, out string reason)
        {
            reason = string.Empty;
            if (mission == null || missionPeer == null)
            {
                reason = "mission or peer missing";
                return false;
            }

            if (HasActiveControlledAgent(missionPeer))
            {
                reason = "peer still controls active agent";
                return false;
            }

            if (IsPeerOccupyingActiveCoopLife(missionPeer))
            {
                reason = "peer still marked alive in coop runtime";
                return false;
            }

            BattleSideEnum authoritativeSide = ResolveAuthoritativeSide(missionPeer, mission, "entry-status can-respawn");
            if (authoritativeSide == BattleSideEnum.None)
            {
                reason = "peer side not assigned";
                return false;
            }

            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            bool hasSelection =
                !string.IsNullOrWhiteSpace(selectionState.EntryId) ||
                !string.IsNullOrWhiteSpace(selectionState.TroopId);
            if (!hasSelection)
            {
                reason = "peer selection missing";
                return false;
            }

            if (IsPeerHoldingQueuedSpawnInCoopRuntime(missionPeer))
            {
                reason = "peer already has queued spawn runtime";
                return false;
            }

            if (CoopBattlePeerLifecycleRuntimeState.TryGetState(missionPeer, out PeerLifecycleRuntimeState lifecycleState) &&
                lifecycleState.Status == CoopBattlePeerLifecycleStatus.NoSide)
            {
                reason = "peer lifecycle still no-side";
                return false;
            }

            return true;
        }

        private static bool TryParseBattleSide(string requestedSideRaw, out BattleSideEnum requestedSide)
        {
            requestedSide = BattleSideEnum.None;
            if (string.IsNullOrWhiteSpace(requestedSideRaw))
                return false;

            string normalized = requestedSideRaw.Trim().ToLowerInvariant();
            if (normalized == "attacker" || normalized == "attackers" || normalized == "1")
            {
                requestedSide = BattleSideEnum.Attacker;
                return true;
            }

            if (normalized == "defender" || normalized == "defenders" || normalized == "2")
            {
                requestedSide = BattleSideEnum.Defender;
                return true;
            }

            return false;
        }

        private static void TrySpawnPeersIntoCoopControl(Mission mission, string source)
        {
            // Legacy direct-spawn experiment. Left in source for reference while respawn/reset
            // is being redesigned, but no longer participates in the runtime tick path.
            if (!EnableDirectCoopPlayerSpawnExperiment || mission == null || !GameNetwork.IsServer)
                return;

            if (GameNetwork.NetworkPeers == null || GameNetwork.NetworkPeers.Count == 0)
                return;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (!TryGetSpawnablePeerState(mission, peer, out MissionPeer missionPeer, out string reason))
                {
                    if (missionPeer != null && CoopBattleSpawnRequestState.HasPendingRequest(missionPeer))
                    {
                        CoopBattleSpawnRuntimeState.MarkRejected(missionPeer, source + " spawnable-check", reason);
                        AdvanceLifecycleAfterSpawnWaitOrReject(mission, missionPeer, reason, source + " spawnable-check");
                    }
                    LogSkippedSpawn(peer, reason, source);
                    continue;
                }

                if (!TryResolveAuthoritativeCharacterForPeer(missionPeer, out BasicCharacterObject selectedCharacter, out string selectedTroopId, out string selectedEntryId, out string resolveReason))
                {
                    CoopBattleSpawnRuntimeState.MarkRejected(missionPeer, source + " resolve-character", resolveReason);
                    AdvanceLifecycleAfterSpawnWaitOrReject(mission, missionPeer, resolveReason, source + " resolve-character");
                    LogSkippedSpawn(peer, "authoritative troop unresolved: " + resolveReason, source);
                    continue;
                }

                if (!missionPeer.HasSpawnedAgentVisuals)
                {
                    BattleSideEnum authoritativeSide = ResolveAuthoritativeSide(missionPeer, mission, source + " visuals");
                    Team authoritativeTeam = ResolveAuthoritativeMissionTeam(mission, missionPeer, authoritativeSide);
                    if (TryEnsurePendingSpawnVisuals(mission, missionPeer, peer, selectedCharacter, authoritativeTeam, source))
                    {
                        CoopBattlePeerLifecycleRuntimeState.MarkSpawnQueued(
                            missionPeer,
                            selectedTroopId,
                            selectedEntryId,
                            source + " visual-preview");
                        LogSkippedSpawn(peer, "awaiting agent visuals", source);
                        continue;
                    }
                }

                RosterEntryState selectedEntryState = !string.IsNullOrWhiteSpace(selectedEntryId)
                    ? BattleSnapshotRuntimeState.GetEntryState(selectedEntryId)
                    : null;
                Agent spawnedAgent = SpawnCoopControlledAgent(mission, missionPeer, peer, selectedCharacter, selectedEntryState, source);
                if (spawnedAgent == null)
                    continue;

                _spawnedCoopPeerIndices.Add(peer.Index);
                CoopBattleSpawnRequestState.Clear(missionPeer, source + " spawn-succeeded");
                CoopBattleSpawnRuntimeState.MarkSpawned(missionPeer, selectedTroopId, selectedEntryId, source + " spawn-succeeded");
                CoopBattlePeerLifecycleRuntimeState.MarkAlive(missionPeer, selectedTroopId, selectedEntryId, source + " spawn-succeeded");

                string peerName = peer.UserName ?? peer.Index.ToString();
                string agentName = spawnedAgent.Name?.ToString() ?? spawnedAgent.Index.ToString();
                ModLogger.Info(
                    "CoopMissionSpawnLogic: coop direct spawn succeeded (" + source + "). " +
                    "Peer=" + peerName +
                    " TroopId=" + selectedTroopId +
                    " EntryId=" + (selectedEntryId ?? "null") +
                    " Agent=" + agentName +
                    " TeamIndex=" + (spawnedAgent.Team?.TeamIndex ?? -1) +
                    " Side=" + (spawnedAgent.Team?.Side ?? BattleSideEnum.None) +
                    " Position=" + spawnedAgent.Position);
            }
        }

        private static bool TryResolveAuthoritativeCharacterForPeer(
            MissionPeer missionPeer,
            out BasicCharacterObject selectedCharacter,
            out string selectedTroopId,
            out string selectedEntryId,
            out string reason)
        {
            selectedCharacter = null;
            selectedTroopId = null;
            selectedEntryId = null;
            reason = string.Empty;

            if (missionPeer == null)
            {
                reason = "mission peer missing";
                return false;
            }

            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            RosterEntryState preferredEntry = ResolvePreferredAllowedEntryStateForPeer(missionPeer, selectionState);
            if (!CoopBattleSpawnRequestState.TryGetPendingRequest(missionPeer, out CoopBattleSpawnRequestState.PeerSpawnRequestState pendingRequest))
            {
                reason = "pending spawn request missing";
                return false;
            }

            CoopBattleSpawnRuntimeState.MarkValidating(missionPeer, "TryResolveAuthoritativeCharacterForPeer pending-request");
            selectedEntryId = pendingRequest.EntryId;
            string requestedTroopId = pendingRequest.TroopId;
            if (string.IsNullOrWhiteSpace(requestedTroopId))
            {
                reason = "pending spawn request troop missing";
                return false;
            }

            bool requiresEntryMatch = !string.IsNullOrWhiteSpace(selectedEntryId) || !string.IsNullOrWhiteSpace(selectionState.EntryId);
            if (preferredEntry == null && requiresEntryMatch)
            {
                reason = "pending spawn request entry not allowed";
                return false;
            }

            if (preferredEntry != null &&
                !string.IsNullOrWhiteSpace(selectedEntryId) &&
                !string.Equals(preferredEntry.EntryId, selectedEntryId, StringComparison.Ordinal))
            {
                reason = "pending spawn request entry mismatched current allowed entry";
                return false;
            }

            bool hasExplicitEntrySelection =
                !string.IsNullOrWhiteSpace(pendingRequest.EntryId) ||
                !string.IsNullOrWhiteSpace(selectionState.EntryId);

            selectedEntryId = hasExplicitEntrySelection
                ? preferredEntry?.EntryId
                : null;
            selectedTroopId = requestedTroopId;
            selectedCharacter = hasExplicitEntrySelection
                ? BattleSnapshotRuntimeState.TryResolveCharacterObject(selectedEntryId) ?? ResolveAllowedCharacter(selectedTroopId)
                : ResolveAllowedCharacter(selectedTroopId);
            if (selectedCharacter == null)
            {
                string cultureFallbackTroopId = ResolveCultureSpecificTargetTroopId(requestedTroopId, missionPeer.Culture);
                if (!string.IsNullOrWhiteSpace(cultureFallbackTroopId) &&
                    !string.Equals(cultureFallbackTroopId, requestedTroopId, StringComparison.Ordinal))
                {
                    BasicCharacterObject cultureFallbackCharacter = hasExplicitEntrySelection
                        ? BattleSnapshotRuntimeState.TryResolveCharacterObject(selectedEntryId) ?? ResolveAllowedCharacter(cultureFallbackTroopId)
                        : ResolveAllowedCharacter(cultureFallbackTroopId);
                    if (cultureFallbackCharacter != null)
                    {
                        ModLogger.Info(
                            "CoopMissionSpawnLogic: authoritative troop direct lookup failed, using peer-culture fallback. " +
                            "RequestedTroopId=" + requestedTroopId +
                            " FallbackTroopId=" + cultureFallbackTroopId +
                            " PeerCulture=" + (missionPeer.Culture?.StringId ?? "null"));
                        selectedTroopId = cultureFallbackTroopId;
                        selectedCharacter = cultureFallbackCharacter;
                    }
                }
            }

            if (!hasExplicitEntrySelection && !string.IsNullOrWhiteSpace(preferredEntry?.EntryId))
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: skipped snapshot entry character resolution for troop-only spawn request. " +
                    "RequestedTroopId=" + requestedTroopId +
                    " PreferredEntryId=" + preferredEntry.EntryId);
            }
            if (selectedCharacter == null)
            {
                reason = "pending troop/entry not resolved (troop='" + selectedTroopId + "', entry='" + (selectedEntryId ?? "null") + "')";
                return false;
            }

            CoopBattleSpawnRuntimeState.MarkValidated(missionPeer, selectedTroopId, selectedEntryId, "TryResolveAuthoritativeCharacterForPeer pending-request");
            return true;
        }

        private static bool TryGetSpawnablePeerState(
            Mission mission,
            NetworkCommunicator peer,
            out MissionPeer missionPeer,
            out string reason)
        {
            missionPeer = null;
            reason = string.Empty;

            if (peer == null)
            {
                reason = "peer is null";
                return false;
            }

            if (peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
            {
                reason = "peer not ready";
                return false;
            }

            missionPeer = peer.GetComponent<MissionPeer>();
            if (missionPeer == null)
            {
                reason = "mission peer missing";
                return false;
            }

            if (HasActiveControlledAgent(missionPeer))
            {
                reason = "peer already controls agent";
                return false;
            }

            if (IsPeerOccupyingActiveCoopLife(missionPeer))
            {
                reason = "peer still marked alive in coop runtime";
                return false;
            }

            BattleSideEnum authoritativeSide = ResolveAuthoritativeSide(missionPeer, mission, "spawnable-peer-state");
            if (authoritativeSide == BattleSideEnum.None)
            {
                reason = "peer side not assigned";
                return false;
            }

            Team authoritativeTeam = ResolveAuthoritativeMissionTeam(mission, missionPeer, authoritativeSide);
            if (authoritativeTeam == null)
            {
                reason = "authoritative mission team not ready";
                return false;
            }

            if (!CoopBattleSpawnRequestState.HasPendingRequest(missionPeer))
            {
                reason = "no pending spawn request";
                return false;
            }

            if (missionPeer.Team != null &&
                !ReferenceEquals(missionPeer.Team, mission.SpectatorTeam) &&
                !missionPeer.TeamInitialPerkInfoReady)
            {
                reason = "team perk info not ready";
                return false;
            }

            if (!IsSpawnTimerReady(missionPeer))
            {
                reason = "spawn timer not ready";
                return false;
            }

            return true;
        }

        private static void LogSkippedSpawn(NetworkCommunicator peer, string reason, string source)
        {
            if (peer == null || string.IsNullOrWhiteSpace(reason))
                return;

            string logKey = peer.Index + "|spawn-skipped|" + reason;
            if (!_loggedForcedPreferredClassKeys.Add(logKey))
                return;

            ModLogger.Info(
                "CoopMissionSpawnLogic: coop direct spawn waiting (" + source + "). " +
                "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                " Reason=" + reason);
        }

        private static void AdvanceLifecycleAfterSpawnWaitOrReject(Mission mission, MissionPeer missionPeer, string reason, string source)
        {
            if (missionPeer == null)
                return;

            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            string troopId = selectionState.TroopId;
            string entryId = selectionState.EntryId;

            if (selectionState.Side == BattleSideEnum.None)
            {
                CoopBattlePeerLifecycleRuntimeState.MarkNoSide(missionPeer, BattleSideEnum.None, source + " no-side");
                return;
            }

            if (string.IsNullOrWhiteSpace(selectionState.TroopId))
            {
                CoopBattlePeerLifecycleRuntimeState.MarkAwaitingSelection(missionPeer, selectionState.Side, source + " awaiting-selection");
                return;
            }

            if (string.Equals(reason, "spawn timer not ready", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(reason, "team perk info not ready", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(reason, "authoritative mission team not ready", StringComparison.OrdinalIgnoreCase))
            {
                CoopBattlePeerLifecycleRuntimeState.MarkSpawnQueued(missionPeer, troopId, entryId, source + " queued");
                return;
            }

            if (!CoopBattleSpawnRequestState.HasPendingRequest(missionPeer) && CanPeerRespawn(mission, missionPeer))
            {
                CoopBattlePeerLifecycleRuntimeState.MarkRespawnable(missionPeer, troopId, entryId, source + " respawnable");
                return;
            }

            CoopBattlePeerLifecycleRuntimeState.MarkWaiting(missionPeer, selectionState.Side, troopId, entryId, source + " waiting");
        }

        private static Team ResolveAuthoritativeMissionTeam(
            Mission mission,
            MissionPeer missionPeer,
            BattleSideEnum authoritativeSide)
        {
            if (mission == null || authoritativeSide == BattleSideEnum.None)
                return null;

            Team currentTeam = missionPeer?.Team;
            if (currentTeam != null &&
                !ReferenceEquals(currentTeam, mission.SpectatorTeam) &&
                currentTeam.Side == authoritativeSide)
            {
                return currentTeam;
            }

            if (authoritativeSide == BattleSideEnum.Attacker)
                return mission.AttackerTeam ?? mission.Teams?.Attacker;

            if (authoritativeSide == BattleSideEnum.Defender)
                return mission.DefenderTeam ?? mission.Teams?.Defender;

            return null;
        }

        private static Agent SpawnCoopControlledAgent(
            Mission mission,
            MissionPeer missionPeer,
            NetworkCommunicator peer,
            BasicCharacterObject troop,
            RosterEntryState entryState,
            string source)
        {
            if (mission == null || missionPeer == null || peer == null || troop == null)
                return null;

            try
            {
                BattleSideEnum authoritativeSide = ResolveAuthoritativeSide(missionPeer, mission, source + " spawn");
                Team team = ResolveAuthoritativeMissionTeam(mission, missionPeer, authoritativeSide);
                if (team == null)
                    return null;

                GetDirectSpawnFrame(mission, team, out Vec3 spawnPosition, out Vec2 spawnDirection);

                Equipment snapshotSpawnEquipment = BuildSnapshotEquipmentForExactRuntime(entryState);
                Equipment spawnEquipment = snapshotSpawnEquipment ?? troop.Equipment?.Clone(false);
                bool usingExactSnapshotSpawnEquipment = snapshotSpawnEquipment != null;
                var origin = new BasicBattleAgentOrigin(troop);

                bool hasTemporaryNameOverride = TryApplyEntryNameToSpawnCharacter(troop, entryState, out TextObject originalTroopName);
                AgentBuildData buildData = new AgentBuildData(troop);
                try
                {
                    buildData.MissionPeer(missionPeer);
                    buildData.Team(team);
                    buildData.TroopOrigin(origin);
                    buildData.Controller(AgentControllerType.Player);
                    buildData.InitialPosition(in spawnPosition);
                    buildData.InitialDirection(in spawnDirection);
                    buildData.SpawnsIntoOwnFormation(false);
                    buildData.SpawnsUsingOwnTroopClass(false);
                    if (spawnEquipment != null)
                        buildData.Equipment(spawnEquipment);

                    string appliedIdentity = TryApplyEntryIdentityToBuildData(buildData, entryState);
                    bool spawnFromAgentVisuals = missionPeer.HasSpawnedAgentVisuals;
                    Agent spawnedAgent = mission.SpawnAgent(buildData, spawnFromAgentVisuals: spawnFromAgentVisuals);
                    if (spawnedAgent == null)
                        return null;

                    TryApplyEntryIdentityToAgent(spawnedAgent, entryState);
                    spawnedAgent.AddComponent(new MPPerksAgentComponent(spawnedAgent));
                    spawnedAgent.MountAgent?.UpdateAgentProperties();

                    MPPerkObject.MPOnSpawnPerkHandler onSpawnPerkHandler = MPPerkObject.GetOnSpawnPerkHandler(missionPeer);
                    float extraHitpoints = onSpawnPerkHandler?.GetHitpoints(true) ?? 0f;
                    if (extraHitpoints > 0f)
                    {
                        spawnedAgent.HealthLimit += extraHitpoints;
                        spawnedAgent.Health = spawnedAgent.HealthLimit;
                    }

                    mission.TakeControlOfAgent(spawnedAgent);
                    missionPeer.ControlledAgent = spawnedAgent;
                    missionPeer.FollowedAgent = spawnedAgent;
                    peer.ControlledAgent = spawnedAgent;
                    spawnedAgent.MissionPeer = missionPeer;
                    bool runtimeExactCharacter = ExactCampaignRuntimeObjectRegistry.IsRuntimeCharacter(spawnedAgent.Character as BasicCharacterObject);
                    bool forceInitialWieldAfterSpawn = !usingExactSnapshotSpawnEquipment;
                    if (!runtimeExactCharacter && !usingExactSnapshotSpawnEquipment && spawnedAgent.SpawnEquipment != null)
                        spawnedAgent.UpdateSpawnEquipmentAndRefreshVisuals(spawnedAgent.SpawnEquipment);
                    if (forceInitialWieldAfterSpawn)
                    {
                        spawnedAgent.WieldInitialWeapons(
                            Agent.WeaponWieldActionType.Instant,
                            Equipment.InitialWeaponEquipPreference.Any);
                    }

                    GameNetwork.BeginModuleEventAsServer(peer.VirtualPlayer);
                    GameNetwork.WriteMessage(new NetworkMessages.FromServer.SetAgentOwningMissionPeer(spawnedAgent.Index, peer.VirtualPlayer));
                    GameNetwork.EndModuleEventAsServer();

                    missionPeer.SpawnCountThisRound++;

                    bool removedPendingVisuals = TryRemovePendingAgentVisuals(mission, missionPeer);

                    missionPeer.HasSpawnedAgentVisuals = false;
                    MPPerkObject.GetPerkHandler(missionPeer)?.OnEvent(MPPerkCondition.PerkEventFlags.SpawnEnd);

                    ModLogger.Info(
                        "CoopMissionSpawnLogic: spawn agent ownership finalized. " +
                        "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                        " Agent=" + spawnedAgent.Index +
                        " SpawnFromVisuals=" + spawnFromAgentVisuals +
                        " HadVisuals=" + missionPeer.HasSpawnedAgentVisuals +
                        " RefreshedSpawnEquipment=" + (!runtimeExactCharacter && spawnedAgent.SpawnEquipment != null) +
                        " RuntimeExactCharacter=" + runtimeExactCharacter +
                        " AddedPerksComponent=True" +
                        " SpawnCountThisRound=" + missionPeer.SpawnCountThisRound +
                        " ExtraHitpoints=" + extraHitpoints +
                        " VisualsRemoved=" + removedPendingVisuals +
                        " " + appliedIdentity);

                    return spawnedAgent;
                }
                finally
                {
                    RestoreSpawnCharacterName(troop, hasTemporaryNameOverride, originalTroopName);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: coop direct spawn failed (" + source + "): " + ex.Message);
                return null;
            }
        }

        private static bool TryRemovePendingAgentVisuals(Mission mission, MissionPeer missionPeer)
        {
            if (mission == null || missionPeer == null || mission.MissionBehaviors == null)
                return false;

            foreach (MissionBehavior behavior in mission.MissionBehaviors)
            {
                if (behavior == null)
                    continue;

                Type behaviorType = behavior.GetType();
                if (behaviorType.Name.IndexOf("MissionAgentVisualSpawnComponent", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                try
                {
                    MethodInfo removeMethod = behaviorType.GetMethod(
                        "RemoveAgentVisuals",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(MissionPeer), typeof(bool) },
                        null);
                    if (removeMethod != null)
                    {
                        removeMethod.Invoke(behavior, new object[] { missionPeer, true });
                        return true;
                    }

                    removeMethod = behaviorType.GetMethod(
                        "RemoveAgentVisuals",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(MissionPeer) },
                        null);
                    if (removeMethod != null)
                    {
                        removeMethod.Invoke(behavior, new object[] { missionPeer });
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Info("CoopMissionSpawnLogic: removing pending agent visuals failed: " + ex.Message);
                    return false;
                }
            }

            return false;
        }

        private static bool TryEnsurePendingSpawnVisuals(
            Mission mission,
            MissionPeer missionPeer,
            NetworkCommunicator peer,
            BasicCharacterObject troop,
            Team authoritativeTeam,
            string source)
        {
            if (mission == null || missionPeer == null || peer == null || troop == null)
                return false;

            if (missionPeer.HasSpawnedAgentVisuals)
                return true;

            try
            {
                MissionMultiplayerGameModeBase gameMode = mission.GetMissionBehavior<MissionMultiplayerGameModeBase>();
                if (gameMode == null)
                {
                    ModLogger.Info("CoopMissionSpawnLogic: pending spawn visuals unavailable (" + source + "): multiplayer game mode missing.");
                    return false;
                }

                Team team = authoritativeTeam ?? missionPeer.Team;
                if (team == null || ReferenceEquals(team, mission.SpectatorTeam))
                {
                    ModLogger.Info(
                        "CoopMissionSpawnLogic: pending spawn visuals unavailable (" + source + "): authoritative team missing or spectator. " +
                        "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                        " MissionPeerTeam=" + (missionPeer.Team?.TeamIndex ?? -1) +
                        " AuthoritativeTeam=" + (authoritativeTeam?.TeamIndex ?? -1));
                    return false;
                }

                Equipment previewEquipment = troop.Equipment?.Clone(false);
                if (previewEquipment == null)
                {
                    ModLogger.Info(
                        "CoopMissionSpawnLogic: pending spawn visuals unavailable (" + source + "): preview equipment missing. " +
                        "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                        " TroopId=" + troop.StringId);
                    return false;
                }

                AgentBuildData previewBuildData = new AgentBuildData(troop);
                previewBuildData.MissionPeer(missionPeer);
                previewBuildData.Team(team);
                previewBuildData.TroopOrigin(new BasicBattleAgentOrigin(troop));
                previewBuildData.Equipment(previewEquipment);
                previewBuildData.IsFemale(missionPeer.Peer?.IsFemale ?? false);
                previewBuildData.BodyProperties((missionPeer.Peer?.BodyProperties).GetValueOrDefault());
                previewBuildData.VisualsIndex(0);
                previewBuildData.ClothingColor1(team.Color);
                previewBuildData.ClothingColor2(team.Color2);

                gameMode.HandleAgentVisualSpawning(peer, previewBuildData);

                ModLogger.Info(
                    "CoopMissionSpawnLogic: requested vanilla agent visuals before direct spawn. " +
                    "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                    " TroopId=" + troop.StringId +
                    " TeamIndex=" + (team.TeamIndex) +
                    " Side=" + team.Side +
                    " Source=" + source);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: failed to request pending spawn visuals (" + source + "): " + ex.Message);
                return false;
            }
        }

        private static void GetDirectSpawnFrame(Mission mission, Team team, out Vec3 spawnPosition, out Vec2 spawnDirection)
        {
            spawnPosition = team.Side == BattleSideEnum.Attacker
                ? new Vec3(-4f, 0f, 0f)
                : new Vec3(4f, 0f, 0f);
            spawnDirection = team.Side == BattleSideEnum.Attacker
                ? new Vec2(1f, 0f)
                : new Vec2(-1f, 0f);

            try
            {
                FFASpawnFrameBehavior spawnFrameBehavior = new FFASpawnFrameBehavior();
                spawnFrameBehavior.Initialize();
                MatrixFrame spawnFrame = spawnFrameBehavior.GetSpawnFrame(team, hasMount: false, isInitialSpawn: true);
                if (spawnFrame.origin != Vec3.Zero)
                {
                    spawnPosition = spawnFrame.origin;
                    spawnDirection = spawnFrame.rotation.f.AsVec2.Normalized();
                    if (spawnDirection.LengthSquared > 0.001f)
                        return;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: FFASpawnFrameBehavior failed: " + ex.Message);
            }

            if (team.ActiveAgents == null || team.ActiveAgents.Count == 0)
            {
                if (string.Equals(mission?.SceneName, "mp_tdm_map_001", StringComparison.OrdinalIgnoreCase))
                {
                    // Temporary safe fallback for the fixed bootstrap scene until we switch to proper
                    // scene-derived spawn points. The origin-based fallback was outside the arena.
                    spawnPosition = team.Side == BattleSideEnum.Attacker
                        ? new Vec3(312f, 320f, 140f)
                        : new Vec3(336f, 320f, 140f);
                }
                return;
            }

            Agent anchorAgent = team.ActiveAgents.FirstOrDefault(agent => agent != null && agent.IsActive());
            if (anchorAgent == null)
                return;

            float lateralOffset = team.Side == BattleSideEnum.Attacker ? -1.5f : 1.5f;
            spawnPosition = anchorAgent.Position + new Vec3(lateralOffset, 0f, 0f);

            Agent targetEnemy = mission.GetClosestEnemyAgent(team, spawnPosition, 200f);
            Vec3 lookAt = targetEnemy != null
                ? targetEnemy.Position
                : anchorAgent.Position + new Vec3(team.Side == BattleSideEnum.Attacker ? 6f : -6f, 0f, 0f);

            Vec3 toEnemy3 = lookAt - spawnPosition;
            spawnDirection = new Vec2(toEnemy3.x, toEnemy3.y);
            if (spawnDirection.LengthSquared < 0.001f)
                spawnDirection = team.Side == BattleSideEnum.Attacker ? new Vec2(1f, 0f) : new Vec2(-1f, 0f);
            spawnDirection.Normalize();
        }

        public static bool TryResolvePreferredHeroClassForPeer(
            MissionPeer missionPeer,
            MultiplayerClassDivisions.MPHeroClass vanillaClass,
            out MultiplayerClassDivisions.MPHeroClass preferredClass,
            out int preferredTroopIndex,
            out string debugReason)
        {
            return TryResolvePreferredHeroClassForPeer(
                missionPeer,
                vanillaClass,
                true,
                out preferredClass,
                out preferredTroopIndex,
                out debugReason);
        }

        private static bool TryResolvePreferredHeroClassForPeer(
            MissionPeer missionPeer,
            MultiplayerClassDivisions.MPHeroClass vanillaClass,
            bool requireSpawnTimerReady,
            out MultiplayerClassDivisions.MPHeroClass preferredClass,
            out int preferredTroopIndex,
            out string debugReason)
        {
            preferredClass = null;
            preferredTroopIndex = -1;
            debugReason = string.Empty;

            if (!GameNetwork.IsServer)
            {
                debugReason = "not server";
                return false;
            }

            if (missionPeer == null)
            {
                debugReason = "missionPeer is null";
                return false;
            }

            if (missionPeer.Team == null || missionPeer.Culture == null)
            {
                debugReason = "peer team/culture not ready";
                return false;
            }

            if (missionPeer.ControlledAgent != null)
            {
                debugReason = "peer already has controlled agent";
                return false;
            }

            if (!CoopBattleSpawnRequestState.HasPendingRequest(missionPeer))
            {
                debugReason = "no pending spawn request";
                return false;
            }

            if (requireSpawnTimerReady && !IsSpawnTimerReady(missionPeer))
            {
                debugReason = "spawn timer not ready";
                return false;
            }

            string targetTroopId = GetPreferredTargetTroopIdForPeerCulture(missionPeer);
            if (string.IsNullOrWhiteSpace(targetTroopId))
            {
                debugReason = "no selected allowed troop";
                return false;
            }

            List<MultiplayerClassDivisions.MPHeroClass> cultureClasses = MultiplayerClassDivisions
                .GetMPHeroClasses(missionPeer.Culture)
                ?.Where(heroClass => heroClass?.HeroCharacter != null)
                .ToList();

            if (cultureClasses == null || cultureClasses.Count == 0)
            {
                debugReason = "peer culture has no MP classes";
                return false;
            }

            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            bool hasExplicitSelection = CoopBattleAuthorityState.HasExplicitSelection(missionPeer);
            RosterEntryState preferredAllowedEntry = ResolvePreferredAllowedEntryStateForPeer(missionPeer, selectionState);
            int currentSelectedTroopIndex = missionPeer.SelectedTroopIndex;
            if (!hasExplicitSelection &&
                currentSelectedTroopIndex >= 0 &&
                currentSelectedTroopIndex < cultureClasses.Count)
            {
                MultiplayerClassDivisions.MPHeroClass currentlySelectedClass = cultureClasses[currentSelectedTroopIndex];
                string matchedAllowedTroopId = ResolveMatchedAllowedTroopId(currentlySelectedClass, missionPeer);
                if (!string.IsNullOrWhiteSpace(matchedAllowedTroopId))
                {
                    RosterEntryState matchedAllowedEntry = ResolveMatchedAllowedEntryState(currentlySelectedClass, missionPeer, selectionState, preferredAllowedEntry);
                    ApplyAuthoritativePreferredSelection(
                        missionPeer,
                        matchedAllowedEntry,
                        matchedAllowedTroopId,
                        "preserve player-selected allowed class");
                    preferredClass = currentlySelectedClass;
                    preferredTroopIndex = currentSelectedTroopIndex;
                    debugReason = "preserve player-selected allowed class '" + matchedAllowedTroopId + "' entry='" + (matchedAllowedEntry?.EntryId ?? "null") + "'";
                    return !ReferenceEquals(preferredClass, vanillaClass) || missionPeer.SelectedTroopIndex != preferredTroopIndex;
                }
            }

            int exactIndex = cultureClasses.FindIndex(heroClass =>
                MatchesPreferredHeroClass(heroClass, targetTroopId));
            if (exactIndex >= 0)
            {
                RosterEntryState exactMatchEntry = ResolveMatchedAllowedEntryState(cultureClasses[exactIndex], missionPeer, selectionState, preferredAllowedEntry);
                ApplyAuthoritativePreferredSelection(missionPeer, exactMatchEntry, targetTroopId, "exact troop id match");
                preferredClass = cultureClasses[exactIndex];
                preferredTroopIndex = exactIndex;
                debugReason = "exact troop id match for '" + targetTroopId + "' entry='" + (exactMatchEntry?.EntryId ?? selectionState.EntryId ?? "null") + "'";
                return !ReferenceEquals(preferredClass, vanillaClass) || missionPeer.SelectedTroopIndex != preferredTroopIndex;
            }

            int bestIndex = FindBestPeerCultureHeroClassIndex(cultureClasses, targetTroopId);
            if (bestIndex < 0 || bestIndex >= cultureClasses.Count)
            {
                debugReason = "no suitable class in peer culture";
                return false;
            }

            RosterEntryState surrogateEntry = ResolveMatchedAllowedEntryState(cultureClasses[bestIndex], missionPeer, selectionState, preferredAllowedEntry);
            ApplyAuthoritativePreferredSelection(missionPeer, surrogateEntry, targetTroopId, "peer-culture surrogate");
            preferredClass = cultureClasses[bestIndex];
            preferredTroopIndex = bestIndex;
            debugReason = "peer-culture surrogate for '" + targetTroopId + "' entry='" + (surrogateEntry?.EntryId ?? selectionState.EntryId ?? "null") + "'";
            return !ReferenceEquals(preferredClass, vanillaClass) || missionPeer.SelectedTroopIndex != preferredTroopIndex;
        }

        private static string ResolveMatchedAllowedTroopId(MultiplayerClassDivisions.MPHeroClass heroClass, MissionPeer missionPeer)
        {
            if (heroClass?.HeroCharacter == null || missionPeer == null)
                return null;

            foreach (string allowedTroopId in ResolveAllowedTargetTroopIdsForPeerCulture(missionPeer))
            {
                if (MatchesPreferredHeroClass(heroClass, allowedTroopId))
                    return allowedTroopId;
            }

            return null;
        }

        private static RosterEntryState ResolvePreferredAllowedEntryStateForPeer(
            MissionPeer missionPeer,
            CoopBattleAuthorityState.PeerSelectionState selectionState)
        {
            if (missionPeer == null)
                return null;

            IReadOnlyList<RosterEntryState> allowedEntries = CoopMissionSpawnLogic.GetAllowedControlEntryStatesSnapshot(selectionState.Side);
            if (allowedEntries.Count == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(selectionState.EntryId))
            {
                RosterEntryState selectedEntry = allowedEntries.FirstOrDefault(entry =>
                    string.Equals(entry?.EntryId, selectionState.EntryId, StringComparison.Ordinal));
                if (selectedEntry != null)
                    return selectedEntry;
            }

            if (!string.IsNullOrWhiteSpace(selectionState.TroopId))
            {
                RosterEntryState selectedByTroop = ResolveHighestPriorityAllowedEntry(
                    missionPeer,
                    selectionState.Side,
                    allowedEntries.Where(entry =>
                        string.Equals(entry?.CharacterId, selectionState.TroopId, StringComparison.OrdinalIgnoreCase)));
                if (selectedByTroop != null)
                    return selectedByTroop;
            }

            return ResolveHighestPriorityAllowedEntry(missionPeer, selectionState.Side, allowedEntries);
        }

        private static RosterEntryState ResolveMatchedAllowedEntryState(
            MultiplayerClassDivisions.MPHeroClass heroClass,
            MissionPeer missionPeer,
            CoopBattleAuthorityState.PeerSelectionState selectionState,
            RosterEntryState preferredAllowedEntry)
        {
            if (heroClass?.HeroCharacter == null || missionPeer == null)
                return null;

            if (!string.IsNullOrWhiteSpace(selectionState.EntryId) &&
                MatchesPreferredHeroClass(heroClass, selectionState.TroopId))
            {
                RosterEntryState selectedEntry = CoopMissionSpawnLogic.GetAllowedControlEntryStatesSnapshot(selectionState.Side)
                    .FirstOrDefault(entry => string.Equals(entry?.EntryId, selectionState.EntryId, StringComparison.Ordinal));
                if (selectedEntry != null)
                    return selectedEntry;
            }

            if (preferredAllowedEntry != null && MatchesPreferredHeroClass(heroClass, preferredAllowedEntry.CharacterId))
                return preferredAllowedEntry;

            return ResolveHighestPriorityAllowedEntry(
                missionPeer,
                selectionState.Side,
                CoopMissionSpawnLogic.GetAllowedControlEntryStatesSnapshot(selectionState.Side)
                    .Where(entry =>
                        entry != null &&
                        MatchesPreferredHeroClass(heroClass, entry.CharacterId)));
        }

        private static RosterEntryState ResolveHighestPriorityAllowedEntry(
            MissionPeer missionPeer,
            BattleSideEnum side,
            IEnumerable<RosterEntryState> entries)
        {
            if (entries == null)
                return null;

            List<RosterEntryState> candidateEntries = entries
                .Where(entry => entry != null)
                .ToList();
            if (candidateEntries.Count <= 0)
                return null;

            RosterEntryState commanderEntry = ResolveCommanderPreferredAllowedEntry(missionPeer, side, candidateEntries);
            if (commanderEntry != null)
                return commanderEntry;

            return candidateEntries
                .OrderBy(GetAllowedEntrySelectionPriority)
                .ThenByDescending(entry => entry.IsHero)
                .ThenByDescending(entry => entry.HeroLevel)
                .ThenByDescending(entry => entry.Tier)
                .ThenBy(entry => entry.EntryId, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        private static RosterEntryState ResolveCommanderPreferredAllowedEntry(
            MissionPeer missionPeer,
            BattleSideEnum side,
            IReadOnlyCollection<RosterEntryState> candidateEntries)
        {
            if (candidateEntries == null || candidateEntries.Count <= 0 || side == BattleSideEnum.None)
                return null;

            RosterEntryState commanderEntry = BattleCommanderResolver.ResolveCommanderEntry(
                BattleSnapshotRuntimeState.GetState(),
                side,
                candidateEntries);
            if (commanderEntry == null || string.IsNullOrWhiteSpace(commanderEntry.EntryId))
                return null;

            return IsCommanderEntryClaimedByAnotherPeer(commanderEntry.EntryId, missionPeer)
                ? null
                : commanderEntry;
        }

        private static bool IsCommanderEntryClaimedByAnotherPeer(string entryId, MissionPeer currentPeer)
        {
            if (string.IsNullOrWhiteSpace(entryId) || GameNetwork.NetworkPeers == null)
                return false;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive)
                    continue;

                MissionPeer otherPeer = peer.GetComponent<MissionPeer>();
                if (otherPeer == null || ReferenceEquals(otherPeer, currentPeer))
                    continue;

                if (DoesPeerClaimEntryId(otherPeer, entryId))
                    return true;
            }

            return false;
        }

        private static bool DoesPeerClaimEntryId(MissionPeer missionPeer, string entryId)
        {
            if (missionPeer == null || string.IsNullOrWhiteSpace(entryId))
                return false;

            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            if (string.Equals(selectionState.EntryId, entryId, StringComparison.Ordinal))
                return true;

            if (CoopBattleSpawnRequestState.TryGetPendingRequest(missionPeer, out CoopBattleSpawnRequestState.PeerSpawnRequestState pendingRequest) &&
                string.Equals(pendingRequest.EntryId, entryId, StringComparison.Ordinal))
            {
                return true;
            }

            return DoesAgentMatchAuthoritativeEntryId(missionPeer.ControlledAgent, entryId);
        }

        private static bool DoesAgentMatchAuthoritativeEntryId(Agent agent, string entryId)
        {
            if (agent == null || string.IsNullOrWhiteSpace(entryId))
                return false;

            return (_materializedArmyEntryIdByAgentIndex.TryGetValue(agent.Index, out string candidateEntryId) &&
                    string.Equals(candidateEntryId, entryId, StringComparison.Ordinal)) ||
                   (ExactCampaignArmyBootstrap.TryGetEntryId(agent, out string originEntryId) &&
                    string.Equals(originEntryId, entryId, StringComparison.Ordinal));
        }

        private static int GetAllowedEntrySelectionPriority(RosterEntryState entry)
        {
            if (entry == null)
                return int.MaxValue;

            if (IsHeroRoleEntry(entry, "player"))
                return 0;

            if (IsHeroRoleEntry(entry, "companion", "wanderer"))
                return 1;

            if (IsHeroRoleEntry(entry, "lord"))
                return 2;

            if (entry.IsHero)
                return 3;

            return 10;
        }

        private static bool IsHeroRoleEntry(RosterEntryState entry, params string[] roles)
        {
            if (entry == null)
                return false;

            if (roles == null || roles.Length == 0)
                return entry.IsHero && !string.IsNullOrWhiteSpace(entry.HeroRole);

            string heroRole = entry.HeroRole ?? string.Empty;
            foreach (string role in roles)
            {
                if (string.Equals(heroRole, role, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string ResolveMatchedAllowedEntryId(MultiplayerClassDivisions.MPHeroClass heroClass, MissionPeer missionPeer)
        {
            if (heroClass?.HeroCharacter == null || missionPeer == null)
                return null;

            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            RosterEntryState preferredAllowedEntry = ResolvePreferredAllowedEntryStateForPeer(missionPeer, selectionState);
            return ResolveMatchedAllowedEntryState(heroClass, missionPeer, selectionState, preferredAllowedEntry)?.EntryId;
        }

        private static void ApplyAuthoritativePreferredSelection(
            MissionPeer missionPeer,
            RosterEntryState preferredEntry,
            string preferredTroopId,
            string source)
        {
            if (missionPeer == null)
                return;

            bool hadPendingSpawnRequest = CoopBattleSpawnRequestState.HasPendingRequest(missionPeer);

            if (preferredEntry != null && !string.IsNullOrWhiteSpace(preferredEntry.EntryId))
            {
                if (CoopBattleAuthorityState.TrySetSelectedEntryId(missionPeer, preferredEntry.EntryId, source + " entry"))
                {
                    CoopBattleSelectionRequestState.TryQueueFromAuthoritySelection(missionPeer, source + " selection-request");
                    if (hadPendingSpawnRequest)
                        CoopBattleSpawnRequestState.TryQueueFromSelection(missionPeer, source + " pending-request-refresh");
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(preferredTroopId))
            {
                CoopBattleAuthorityState.TrySetSelectedTroopId(missionPeer, preferredTroopId, source + " troop");
                CoopBattleSelectionRequestState.TryQueueFromAuthoritySelection(missionPeer, source + " selection-request");
                if (hadPendingSpawnRequest)
                    CoopBattleSpawnRequestState.TryQueueFromSelection(missionPeer, source + " pending-request-refresh");
            }
        }

        private static void TryForcePreferredHeroClassForPeer(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer || GameNetwork.NetworkPeers == null || GameNetwork.NetworkPeers.Count == 0)
                return;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
                    continue;

                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                if (missionPeer == null || missionPeer.ControlledAgent != null)
                    continue;

                if (missionPeer.Team == null || ReferenceEquals(missionPeer.Team, mission.SpectatorTeam) || missionPeer.Culture == null)
                    continue;

                if (!CoopBattleSpawnRequestState.HasPendingRequest(missionPeer))
                    continue;

                bool hasExplicitSelection = CoopBattleAuthorityState.HasExplicitSelection(missionPeer);

                int currentTroopIndex = missionPeer.SelectedTroopIndex;
                MultiplayerClassDivisions.MPHeroClass currentClass = null;
                List<MultiplayerClassDivisions.MPHeroClass> cultureClasses = MultiplayerClassDivisions
                    .GetMPHeroClasses(missionPeer.Culture)
                    ?.Where(heroClass => heroClass?.HeroCharacter != null)
                    .ToList();
                if (cultureClasses != null && currentTroopIndex >= 0 && currentTroopIndex < cultureClasses.Count)
                    currentClass = cultureClasses[currentTroopIndex];

                if (!hasExplicitSelection && currentClass != null)
                    continue;

                if (!TryResolvePreferredHeroClassForPeer(
                        missionPeer,
                        currentClass,
                        false,
                        out MultiplayerClassDivisions.MPHeroClass preferredClass,
                        out int preferredTroopIndex,
                        out string debugReason))
                {
                    continue;
                }

                if (preferredTroopIndex < 0)
                    continue;

                if (hasExplicitSelection && currentClass != null && missionPeer.SelectedTroopIndex == preferredTroopIndex)
                    continue;

                ApplySelectedTroopIndexBridge(missionPeer, peer, preferredTroopIndex);

                string classId = preferredClass?.HeroCharacter?.StringId ?? "null";
                string peerName = peer.UserName ?? peer.Index.ToString();
                string logKey = peer.Index + "|forced-selected-index|" + preferredTroopIndex + "|" + classId;
                if (_loggedForcedPreferredClassKeys.Add(logKey))
                {
                    ModLogger.Info(
                        "CoopMissionSpawnLogic: forced preferred troop index before vanilla spawn (" + source + "). " +
                        "Peer=" + peerName +
                        " Culture=" + (missionPeer.Culture?.StringId ?? "null") +
                        " TroopIndex=" + preferredTroopIndex +
                        " HeroClass=" + classId +
                        " Reason=" + debugReason);
                }
            }

            TrySyncCoopClassRestrictions(mission, source);
        }

        private static void TryFinalizePendingVanillaSpawnVisuals(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer || GameNetwork.NetworkPeers == null || GameNetwork.NetworkPeers.Count == 0)
                return;

            SpawnComponent spawnComponent = mission.GetMissionBehavior<SpawnComponent>();
            MissionMultiplayerGameModeBase gameMode = mission.GetMissionBehavior<MissionMultiplayerGameModeBase>();
            if (spawnComponent == null || gameMode == null)
                return;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
                    continue;

                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                if (missionPeer == null || missionPeer.ControlledAgent != null)
                    continue;

                if (!CoopBattleSpawnRequestState.HasPendingRequest(missionPeer))
                    continue;

                if (!missionPeer.HasSpawnedAgentVisuals || missionPeer.EquipmentUpdatingExpired)
                    continue;

                try
                {
                    TryEnsureVanillaSpawnGoldFloor(gameMode, peer, missionPeer, source);
                    spawnComponent.SetEarlyAgentVisualsDespawning(missionPeer, canDespawnEarly: true);
                    ModLogger.Info(
                        "CoopMissionSpawnLogic: finalized pending vanilla spawn visuals for agent creation. " +
                        "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                        " TeamIndex=" + (missionPeer.Team?.TeamIndex ?? -1) +
                        " Side=" + missionPeer.Team?.Side +
                        " SelectedTroopIndex=" + missionPeer.SelectedTroopIndex +
                        " Source=" + source);
                }
                catch (Exception ex)
                {
                    ModLogger.Info("CoopMissionSpawnLogic: failed to finalize pending vanilla spawn visuals (" + source + "): " + ex.Message);
                }
            }
        }

        private static void TryEnsureVanillaSpawnGoldFloor(
            MissionMultiplayerGameModeBase gameMode,
            NetworkCommunicator peer,
            MissionPeer missionPeer,
            string source)
        {
            if (gameMode == null || peer == null || missionPeer == null || missionPeer.Culture == null)
                return;

            int selectedTroopIndex = missionPeer.SelectedTroopIndex;
            if (selectedTroopIndex < 0)
                return;

            List<MultiplayerClassDivisions.MPHeroClass> cultureClasses = MultiplayerClassDivisions
                .GetMPHeroClasses(missionPeer.Culture)
                ?.Where(heroClass => heroClass?.HeroCharacter != null)
                .ToList();
            if (cultureClasses == null || selectedTroopIndex >= cultureClasses.Count)
                return;

            MultiplayerClassDivisions.MPHeroClass selectedClass = cultureClasses[selectedTroopIndex];
            int troopCasualCost = selectedClass?.TroopCasualCost ?? 0;
            if (troopCasualCost <= 0)
                return;

            int currentGold = gameMode.GetCurrentGoldForPeer(missionPeer);
            if (currentGold >= troopCasualCost)
                return;

            gameMode.ChangeCurrentGoldForPeer(missionPeer, troopCasualCost);
            ModLogger.Info(
                "CoopMissionSpawnLogic: raised vanilla spawn gold floor before visuals finalize. " +
                "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                " SelectedTroopIndex=" + selectedTroopIndex +
                " HeroClass=" + (selectedClass?.HeroCharacter?.StringId ?? "null") +
                " PreviousGold=" + currentGold +
                " AppliedGold=" + troopCasualCost +
                " Source=" + source);
        }

        private static void TryApplyVanillaSpawnGoldDeduction(
            MissionMultiplayerGameModeBase gameMode,
            NetworkCommunicator peer,
            MissionPeer missionPeer,
            string source)
        {
            if (gameMode == null || peer == null || missionPeer == null || missionPeer.Culture == null)
                return;

            int selectedTroopIndex = missionPeer.SelectedTroopIndex;
            if (selectedTroopIndex < 0)
                return;

            List<MultiplayerClassDivisions.MPHeroClass> cultureClasses = MultiplayerClassDivisions
                .GetMPHeroClasses(missionPeer.Culture)
                ?.Where(heroClass => heroClass?.HeroCharacter != null)
                .ToList();
            if (cultureClasses == null || selectedTroopIndex >= cultureClasses.Count)
                return;

            MultiplayerClassDivisions.MPHeroClass selectedClass = cultureClasses[selectedTroopIndex];
            int troopCasualCost = selectedClass?.TroopCasualCost ?? 0;
            if (troopCasualCost <= 0)
                return;

            int currentGold = gameMode.GetCurrentGoldForPeer(missionPeer);
            int appliedGold = Math.Max(0, currentGold - troopCasualCost);
            gameMode.ChangeCurrentGoldForPeer(missionPeer, appliedGold);
            ModLogger.Info(
                "CoopMissionSpawnLogic: applied vanilla spawn gold deduction after materialized replace-bot. " +
                "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                " SelectedTroopIndex=" + selectedTroopIndex +
                " HeroClass=" + (selectedClass?.HeroCharacter?.StringId ?? "null") +
                " PreviousGold=" + currentGold +
                " TroopCasualCost=" + troopCasualCost +
                " AppliedGold=" + appliedGold +
                " Source=" + source);
        }

        private static void ApplySelectedTroopIndexBridge(MissionPeer missionPeer, NetworkCommunicator peer, int preferredTroopIndex)
        {
            if (missionPeer == null || peer == null || preferredTroopIndex < 0)
                return;

            if (missionPeer.SelectedTroopIndex == preferredTroopIndex &&
                _lastBridgedSelectedTroopIndexByPeer.TryGetValue(peer.Index, out int lastBridgedTroopIndex) &&
                lastBridgedTroopIndex == preferredTroopIndex)
            {
                return;
            }

            missionPeer.SelectedTroopIndex = preferredTroopIndex;
            _lastBridgedSelectedTroopIndexByPeer[peer.Index] = preferredTroopIndex;

            GameNetwork.BeginBroadcastModuleEvent();
            GameNetwork.WriteMessage(new NetworkMessages.FromServer.UpdateSelectedTroopIndex(peer, preferredTroopIndex));
            GameNetwork.EndBroadcastModuleEvent(GameNetwork.EventBroadcastFlags.None);
        }

        private static void TrySyncCoopClassRestrictions(Mission mission, string source)
        {
            if (!EnableCoopClassRestrictionSyncExperiment)
                return;

            if (mission == null || !GameNetwork.IsServer)
                return;

            MissionLobbyComponent lobbyComponent = mission.GetMissionBehavior<MissionLobbyComponent>();
            if (lobbyComponent == null)
                return;

            HashSet<FormationClass> allowedClasses = ResolveAllowedFormationClassesForMission(mission);
            if (allowedClasses.Count == 0)
                return;

            foreach (FormationClass formationClass in RestrictableFormationClasses)
            {
                bool desiredAvailability = allowedClasses.Contains(formationClass);
                if (_appliedCoopClassAvailabilityStates.TryGetValue(formationClass, out bool appliedAvailability) &&
                    appliedAvailability == desiredAvailability)
                {
                    continue;
                }

                bool finalAvailability = ApplyCoopClassRestriction(lobbyComponent, formationClass, desiredAvailability);
                _appliedCoopClassAvailabilityStates[formationClass] = finalAvailability;

                ModLogger.Info(
                    "CoopMissionSpawnLogic: synced class restriction (" + source + "). " +
                    "FormationClass=" + formationClass +
                    " DesiredAvailability=" + desiredAvailability +
                    " FinalAvailability=" + finalAvailability);
            }
        }

        private static HashSet<FormationClass> ResolveAllowedFormationClassesForMission(Mission mission)
        {
            HashSet<FormationClass> allowedClasses = new HashSet<FormationClass>();
            if (mission == null || GameNetwork.NetworkPeers == null)
                return allowedClasses;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
                    continue;

                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                if (missionPeer == null || missionPeer.Team == null || ReferenceEquals(missionPeer.Team, mission.SpectatorTeam))
                    continue;

                BattleSideEnum authoritativeSide = ResolveAuthoritativeSide(missionPeer, mission, "class-restriction-sync");
                if (authoritativeSide == BattleSideEnum.None)
                    continue;

                BasicCharacterObject controlledCharacter = missionPeer.ControlledAgent?.Character as BasicCharacterObject;
                if (controlledCharacter != null)
                {
                    allowedClasses.Add(controlledCharacter.DefaultFormationClass);
                    continue;
                }

                foreach (BasicCharacterObject allowedCharacter in ResolveAllowedCharactersForPeerCulture(missionPeer))
                {
                    if (allowedCharacter != null)
                        allowedClasses.Add(allowedCharacter.DefaultFormationClass);
                }
            }

            if (allowedClasses.Count == 0)
            {
                foreach (string allowedTroopId in GetAllowedControlTroopIdsSnapshot())
                {
                    BasicCharacterObject fallbackCharacter = ResolveAllowedCharacter(allowedTroopId);
                    if (fallbackCharacter != null)
                        allowedClasses.Add(fallbackCharacter.DefaultFormationClass);
                }
            }

            return allowedClasses;
        }

        private static bool ApplyCoopClassRestriction(
            MissionLobbyComponent lobbyComponent,
            FormationClass formationClass,
            bool desiredAvailability)
        {
            bool finalAvailability = desiredAvailability;
            bool broadcastValue = !desiredAvailability;

            try
            {
                lobbyComponent.ChangeClassRestriction(formationClass, broadcastValue);
                finalAvailability = lobbyComponent.IsClassAvailable(formationClass);
                if (finalAvailability != desiredAvailability)
                {
                    broadcastValue = desiredAvailability;
                    lobbyComponent.ChangeClassRestriction(formationClass, broadcastValue);
                    finalAvailability = lobbyComponent.IsClassAvailable(formationClass);
                }

                GameNetwork.BeginBroadcastModuleEvent();
                GameNetwork.WriteMessage(new NetworkMessages.FromServer.ChangeClassRestrictions(formationClass, broadcastValue));
                GameNetwork.EndBroadcastModuleEvent(GameNetwork.EventBroadcastFlags.None);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: class restriction sync failed. " +
                    "FormationClass=" + formationClass +
                    " DesiredAvailability=" + desiredAvailability +
                    " Error=" + ex.Message);
            }

            return finalAvailability;
        }

        private static string GetPreferredTargetTroopIdForPeerCulture(MissionPeer missionPeer)
        {
            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            RosterEntryState preferredEntry = ResolvePreferredAllowedEntryStateForPeer(missionPeer, selectionState);
            bool hasExplicitSelection = CoopBattleAuthorityState.HasExplicitSelection(missionPeer);
            string authoritativeBaseTroopId = hasExplicitSelection
                ? (!string.IsNullOrWhiteSpace(selectionState.TroopId)
                    ? selectionState.TroopId
                    : preferredEntry?.CharacterId)
                : (preferredEntry?.CharacterId ?? selectionState.TroopId);
            if (string.IsNullOrWhiteSpace(authoritativeBaseTroopId))
                return ResolveAllowedTargetTroopIdsForPeerCulture(missionPeer).FirstOrDefault();

            if (ShouldPreserveExactHeroRuntimeTarget(preferredEntry, authoritativeBaseTroopId))
                return authoritativeBaseTroopId;

            string cultureSpecificTargetTroopId = ResolveCultureSpecificTargetTroopId(authoritativeBaseTroopId, missionPeer?.Culture);
            return string.IsNullOrWhiteSpace(cultureSpecificTargetTroopId)
                ? authoritativeBaseTroopId
                : cultureSpecificTargetTroopId;
        }

        private static List<RosterEntryState> ResolveOrderedAllowedEntriesForPeer(MissionPeer missionPeer)
        {
            List<RosterEntryState> orderedEntries = new List<RosterEntryState>();
            if (missionPeer == null)
                return orderedEntries;

            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            IReadOnlyList<RosterEntryState> allowedEntries = GetAllowedControlEntryStatesSnapshot(selectionState.Side);
            if (allowedEntries.Count == 0)
                return orderedEntries;

            RosterEntryState preferredEntry = ResolvePreferredAllowedEntryStateForPeer(missionPeer, selectionState);
            if (preferredEntry != null)
                orderedEntries.Add(preferredEntry);

            foreach (RosterEntryState entry in allowedEntries)
            {
                if (entry == null)
                    continue;

                if (orderedEntries.Any(candidate => string.Equals(candidate.EntryId, entry.EntryId, StringComparison.Ordinal)))
                    continue;

                orderedEntries.Add(entry);
            }

            return orderedEntries;
        }

        private static List<string> ResolveAllowedTargetTroopIdsForPeerCulture(MissionPeer missionPeer)
        {
            List<string> targetTroopIds = new List<string>();
            List<RosterEntryState> orderedEntries = ResolveOrderedAllowedEntriesForPeer(missionPeer);
            IReadOnlyList<string> allowedBaseTroopIds = orderedEntries.Count > 0
                ? orderedEntries
                    .Select(entry => entry?.CharacterId)
                    .Where(characterId => !string.IsNullOrWhiteSpace(characterId))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
                : GetAllowedControlTroopIdsSnapshot(CoopBattleAuthorityState.GetSelectionState(missionPeer).Side);
            if (allowedBaseTroopIds.Count == 0)
                return targetTroopIds;

            string cultureToken = ExtractCultureToken(missionPeer?.Culture);
            HashSet<string> seenTroopIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (string baseTargetTroopId in allowedBaseTroopIds)
            {
                string preferredTroopId = baseTargetTroopId;
                RosterEntryState matchingEntry = orderedEntries.FirstOrDefault(entry =>
                    entry != null &&
                    string.Equals(entry.CharacterId, baseTargetTroopId, StringComparison.Ordinal));
                if (!ShouldPreserveExactHeroRuntimeTarget(matchingEntry, baseTargetTroopId) &&
                    !string.IsNullOrWhiteSpace(cultureToken))
                {
                    string cultureSpecificCoopTroopId = TryBuildCultureSpecificCoopTroopId(baseTargetTroopId, cultureToken);
                    if (!string.IsNullOrWhiteSpace(cultureSpecificCoopTroopId))
                    {
                        BasicCharacterObject cultureSpecificCharacter = ResolveAllowedCharacter(cultureSpecificCoopTroopId);
                        if (cultureSpecificCharacter != null)
                            preferredTroopId = cultureSpecificCoopTroopId;
                    }
                }

                if (string.IsNullOrWhiteSpace(preferredTroopId) || !seenTroopIds.Add(preferredTroopId))
                    continue;

                targetTroopIds.Add(preferredTroopId);
            }

            return targetTroopIds;
        }

        private static string ResolveCultureSpecificTargetTroopId(string baseTargetTroopId, BasicCultureObject culture)
        {
            if (string.IsNullOrWhiteSpace(baseTargetTroopId))
                return null;

            string cultureToken = ExtractCultureToken(culture);
            if (string.IsNullOrWhiteSpace(cultureToken))
                return baseTargetTroopId;

            string cultureSpecificCoopTroopId = TryBuildCultureSpecificCoopTroopId(baseTargetTroopId, cultureToken);
            if (string.IsNullOrWhiteSpace(cultureSpecificCoopTroopId))
                return baseTargetTroopId;

            return ResolveAllowedCharacter(cultureSpecificCoopTroopId) != null
                ? cultureSpecificCoopTroopId
                : baseTargetTroopId;
        }

        private static IEnumerable<BasicCharacterObject> ResolveAllowedCharactersForPeerCulture(MissionPeer missionPeer)
        {
            List<RosterEntryState> orderedEntries = ResolveOrderedAllowedEntriesForPeer(missionPeer);
            if (orderedEntries.Count > 0)
            {
                HashSet<string> yieldedTroopIds = new HashSet<string>(StringComparer.Ordinal);
                string cultureToken = ExtractCultureToken(missionPeer?.Culture);
                foreach (RosterEntryState entry in orderedEntries)
                {
                    string baseTroopId = entry?.CharacterId;
                    if (string.IsNullOrWhiteSpace(baseTroopId))
                        continue;

                    string resolvedTroopId = baseTroopId;
                    if (!ShouldPreserveExactHeroRuntimeTarget(entry, baseTroopId) &&
                        !string.IsNullOrWhiteSpace(cultureToken))
                    {
                        string cultureSpecificTroopId = TryBuildCultureSpecificCoopTroopId(baseTroopId, cultureToken);
                        if (!string.IsNullOrWhiteSpace(cultureSpecificTroopId) && ResolveAllowedCharacter(cultureSpecificTroopId) != null)
                            resolvedTroopId = cultureSpecificTroopId;
                    }

                    if (!yieldedTroopIds.Add(resolvedTroopId))
                        continue;

                    BasicCharacterObject allowedCharacter = ResolveAllowedCharacter(resolvedTroopId);
                    if (allowedCharacter != null)
                        yield return allowedCharacter;
                }

                yield break;
            }

            foreach (string targetTroopId in ResolveAllowedTargetTroopIdsForPeerCulture(missionPeer))
            {
                BasicCharacterObject allowedCharacter = ResolveAllowedCharacter(targetTroopId);
                if (allowedCharacter != null)
                    yield return allowedCharacter;
            }
        }

        private static bool ShouldPreserveExactHeroRuntimeTarget(RosterEntryState entry, string troopId)
        {
            if (entry == null || string.IsNullOrWhiteSpace(troopId))
                return false;

            if (!string.Equals(entry.CharacterId, troopId, StringComparison.Ordinal))
                return false;

            return IsHeroRoleEntry(entry, "player", "companion", "wanderer", "lord");
        }

        private static string TryBuildCultureSpecificCoopTroopId(string targetTroopId, string cultureToken)
        {
            if (string.IsNullOrWhiteSpace(targetTroopId) ||
                string.IsNullOrWhiteSpace(cultureToken) ||
                !targetTroopId.StartsWith("mp_coop_", StringComparison.Ordinal))
            {
                return null;
            }

            string normalizedTarget = targetTroopId.Trim().ToLowerInvariant();
            string role = GetTroopRole(normalizedTarget);
            if (string.IsNullOrWhiteSpace(role))
                return null;

            string weight = GetTroopWeight(normalizedTarget);
            string prefix = string.IsNullOrWhiteSpace(weight)
                ? "mp_coop_" + role + "_"
                : "mp_coop_" + weight + "_" + role + "_";

            return prefix + cultureToken + "_troop";
        }

        private static string ExtractCultureToken(BasicCultureObject culture)
        {
            string cultureId = culture?.StringId;
            if (string.IsNullOrWhiteSpace(cultureId))
                return null;

            int dotIndex = cultureId.LastIndexOf('.');
            if (dotIndex >= 0 && dotIndex < cultureId.Length - 1)
                return cultureId.Substring(dotIndex + 1).Trim().ToLowerInvariant();

            return cultureId.Trim().ToLowerInvariant();
        }

        private static bool IsSpawnTimerReady(MissionPeer missionPeer)
        {
            if (missionPeer?.SpawnTimer == null || Mission.Current == null)
                return false;

            try
            {
                float elapsed = Mission.Current.CurrentTime - missionPeer.SpawnTimer.StartTime;
                return elapsed >= missionPeer.SpawnTimer.Duration;
            }
            catch
            {
                return false;
            }
        }

        private static int FindBestPeerCultureHeroClassIndex(List<MultiplayerClassDivisions.MPHeroClass> cultureClasses, string targetTroopId)
        {
            if (cultureClasses == null || cultureClasses.Count == 0 || string.IsNullOrWhiteSpace(targetTroopId))
                return -1;

            string normalizedTarget = targetTroopId.Trim().ToLowerInvariant();
            string targetRole = GetTroopRole(normalizedTarget);
            string targetWeight = GetTroopWeight(normalizedTarget);
            bool targetMounted = normalizedTarget.Contains("_cavalry_");
            bool targetRanged = normalizedTarget.Contains("_ranged_") || normalizedTarget.Contains("_archer_");

            int bestIndex = -1;
            int bestScore = int.MinValue;

            for (int i = 0; i < cultureClasses.Count; i++)
            {
                string candidateId = cultureClasses[i]?.HeroCharacter?.StringId;
                if (string.IsNullOrWhiteSpace(candidateId))
                    continue;

                string normalizedCandidate = candidateId.Trim().ToLowerInvariant();
                int score = 0;

                if (string.Equals(normalizedCandidate, normalizedTarget, StringComparison.Ordinal))
                    score += 1000;

                string candidateRole = GetTroopRole(normalizedCandidate);
                if (!string.IsNullOrEmpty(targetRole) && string.Equals(candidateRole, targetRole, StringComparison.Ordinal))
                    score += 200;

                string candidateWeight = GetTroopWeight(normalizedCandidate);
                if (!string.IsNullOrEmpty(targetWeight) && string.Equals(candidateWeight, targetWeight, StringComparison.Ordinal))
                    score += 50;

                bool candidateMounted = normalizedCandidate.Contains("_cavalry_");
                if (candidateMounted == targetMounted)
                    score += 25;

                bool candidateRanged = normalizedCandidate.Contains("_ranged_") || normalizedCandidate.Contains("_archer_");
                if (candidateRanged == targetRanged)
                    score += 15;

                if (normalizedCandidate.Contains("_troop"))
                    score += 5;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static bool MatchesPreferredHeroClass(MultiplayerClassDivisions.MPHeroClass heroClass, string targetTroopId)
        {
            string heroId = heroClass?.HeroCharacter?.StringId;
            if (string.IsNullOrWhiteSpace(heroId) || string.IsNullOrWhiteSpace(targetTroopId))
                return false;

            if (string.Equals(heroId, targetTroopId, StringComparison.Ordinal))
                return true;

            string normalizedTarget = targetTroopId.Trim();
            string canonicalTarget = NormalizeTroopIdForHeroClassMatch(normalizedTarget);
            string canonicalHeroId = NormalizeTroopIdForHeroClassMatch(heroId);
            if (!string.IsNullOrWhiteSpace(canonicalTarget) &&
                !string.IsNullOrWhiteSpace(canonicalHeroId) &&
                string.Equals(canonicalHeroId, canonicalTarget, StringComparison.Ordinal))
            {
                return true;
            }

            if (normalizedTarget.EndsWith("_troop", StringComparison.Ordinal))
            {
                string expectedHeroId = normalizedTarget.Substring(0, normalizedTarget.Length - "_troop".Length) + "_hero";
                if (string.Equals(heroId, expectedHeroId, StringComparison.Ordinal))
                    return true;

                string canonicalExpectedHeroId = NormalizeTroopIdForHeroClassMatch(expectedHeroId);
                if (!string.IsNullOrWhiteSpace(canonicalExpectedHeroId) &&
                    !string.IsNullOrWhiteSpace(canonicalHeroId) &&
                    string.Equals(canonicalHeroId, canonicalExpectedHeroId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            if (normalizedTarget.EndsWith("_hero", StringComparison.Ordinal))
            {
                string expectedTroopId = normalizedTarget.Substring(0, normalizedTarget.Length - "_hero".Length) + "_troop";
                if (string.Equals(heroId, expectedTroopId, StringComparison.Ordinal))
                    return true;

                string canonicalExpectedTroopId = NormalizeTroopIdForHeroClassMatch(expectedTroopId);
                if (!string.IsNullOrWhiteSpace(canonicalExpectedTroopId) &&
                    !string.IsNullOrWhiteSpace(canonicalHeroId) &&
                    string.Equals(canonicalHeroId, canonicalExpectedTroopId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeTroopIdForHeroClassMatch(string troopId)
        {
            if (string.IsNullOrWhiteSpace(troopId))
                return null;

            string normalized = troopId.Trim();
            if (normalized.StartsWith("mp_coop_", StringComparison.Ordinal))
                normalized = "mp_" + normalized.Substring("mp_coop_".Length);

            return normalized;
        }

        private static string GetTroopRole(string troopId)
        {
            if (string.IsNullOrWhiteSpace(troopId))
                return string.Empty;

            if (troopId.Contains("_cavalry_"))
                return "cavalry";
            if (troopId.Contains("_ranged_") || troopId.Contains("_archer_"))
                return "ranged";
            if (troopId.Contains("_infantry_"))
                return "infantry";
            return string.Empty;
        }

        private static string GetTroopWeight(string troopId)
        {
            if (string.IsNullOrWhiteSpace(troopId))
                return string.Empty;

            if (troopId.Contains("_heavy_"))
                return "heavy";
            if (troopId.Contains("_light_"))
                return "light";
            if (troopId.Contains("_medium_"))
                return "medium";
            return string.Empty;
        }

        private static void TrySpawnSelectedAllowedCharacterForDiagnostics(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer)
                return;

            if (_hasSpawnedDiagnosticAllowedAgent && ReferenceEquals(_lastDiagnosticSpawnMission, mission))
                return;

            if (SelectedAllowedCharacter == null)
                return;

            Team spawnTeam = mission.AttackerTeam ?? mission.DefenderTeam;
            if (spawnTeam == null)
            {
                ModLogger.Info("CoopMissionSpawnLogic: diagnostic spawn skipped (" + source + ") because no vanilla TDM team is available yet.");
                return;
            }

            Agent anchorAgent = null;
            try
            {
                if (spawnTeam.ActiveAgents != null && spawnTeam.ActiveAgents.Count > 0)
                    anchorAgent = spawnTeam.ActiveAgents[0];
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: diagnostic spawn could not inspect team agents (" + source + "): " + ex.Message);
            }

            if (anchorAgent == null)
            {
                ModLogger.Info("CoopMissionSpawnLogic: diagnostic spawn skipped (" + source + ") because team " + spawnTeam.TeamIndex + " has no active anchor agent yet.");
                return;
            }

            Agent spawnedAgent = SpawnDiagnosticAiAgent(mission, spawnTeam, SelectedAllowedCharacter, anchorAgent);
            if (spawnedAgent == null)
            {
                ModLogger.Info("CoopMissionSpawnLogic: diagnostic spawn failed (" + source + ") for troop id '" + SelectedAllowedCharacter.StringId + "'.");
                return;
            }

            _lastDiagnosticSpawnMission = mission;
            _hasSpawnedDiagnosticAllowedAgent = true;
            _diagnosticAllowedAgent = spawnedAgent;

            string spawnedName = spawnedAgent.Name?.ToString() ?? spawnedAgent.Index.ToString();
            ModLogger.Info(
                "CoopMissionSpawnLogic: diagnostic allowed agent spawned (" + source + "). " +
                "TroopId=" + SelectedAllowedCharacter.StringId +
                " TeamIndex=" + (spawnedAgent.Team?.TeamIndex ?? -1) +
                " Side=" + (spawnedAgent.Team?.Side ?? BattleSideEnum.None) +
                " Agent=" + spawnedName +
                " Position=" + spawnedAgent.Position);
        }

        private static Agent SpawnDiagnosticAiAgent(Mission mission, Team team, BasicCharacterObject troop, Agent anchorAgent)
        {
            if (mission == null || team == null || troop == null || anchorAgent == null)
                return null;

            try
            {
                float lateralOffset = team.Side == BattleSideEnum.Attacker ? -1.5f : 1.5f;
                Vec3 spawnPosition = anchorAgent.Position + new Vec3(lateralOffset, 0f, 0f);
                Agent targetEnemy = mission.GetClosestEnemyAgent(team, spawnPosition, 200f);
                Vec3 lookAt = targetEnemy != null
                    ? targetEnemy.Position
                    : anchorAgent.Position + new Vec3(team.Side == BattleSideEnum.Attacker ? 6f : -6f, 0f, 0f);

                var origin = new BasicBattleAgentOrigin(troop);
                Vec3 toEnemy3 = lookAt - spawnPosition;
                Vec2 toEnemy2 = new Vec2(toEnemy3.x, toEnemy3.y);
                if (toEnemy2.LengthSquared < 0.001f)
                    toEnemy2 = new Vec2(1f, 0f);
                toEnemy2.Normalize();

                AgentBuildData buildData = new AgentBuildData(troop);
                buildData.Team(team);
                buildData.Controller(AgentControllerType.AI);
                buildData.TroopOrigin(origin);
                buildData.InitialPosition(in spawnPosition);
                buildData.InitialDirection(in toEnemy2);
                buildData.SpawnsIntoOwnFormation(false);
                buildData.SpawnsUsingOwnTroopClass(false);

                Agent agent = mission.SpawnAgent(buildData, spawnFromAgentVisuals: false);
                if (agent != null)
                {
                    agent.SetAutomaticTargetSelection(true);
                    agent.SetFiringOrder(FiringOrder.RangedWeaponUsageOrderEnum.FireAtWill);
                }

                return agent;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: SpawnDiagnosticAiAgent failed: " + ex.Message);
                return null;
            }
        }

        private static void TryTransferDiagnosticAllowedAgentToPeer(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer || _diagnosticAllowedAgent == null)
                return;

            if (_hasTransferredDiagnosticAllowedAgentToPeer && ReferenceEquals(_lastDiagnosticOwnershipMission, mission))
                return;

            if (GameNetwork.NetworkPeers == null || GameNetwork.NetworkPeers.Count == 0)
                return;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
                    continue;

                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                if (missionPeer == null)
                    continue;

                Agent currentlyControlledAgent = missionPeer.ControlledAgent;
                if (currentlyControlledAgent == null)
                    continue;

                if (ReferenceEquals(currentlyControlledAgent, _diagnosticAllowedAgent))
                {
                    _hasTransferredDiagnosticAllowedAgentToPeer = true;
                    _lastDiagnosticOwnershipMission = mission;
                    return;
                }

                try
                {
                    Team peerTeam = missionPeer.Team ?? currentlyControlledAgent.Team ?? _diagnosticAllowedAgent.Team;
                    if (peerTeam != null && !ReferenceEquals(_diagnosticAllowedAgent.Team, peerTeam))
                    {
                        _diagnosticAllowedAgent.SetTeam(peerTeam, false);
                        _diagnosticAllowedAgent.ForceUpdateCachedAndFormationValues(true, false);
                    }

                    currentlyControlledAgent.MissionPeer = null;
                    currentlyControlledAgent.Controller = AgentControllerType.AI;

                    _diagnosticAllowedAgent.Controller = AgentControllerType.Player;
                    _diagnosticAllowedAgent.MissionPeer = missionPeer;

                    missionPeer.ControlledAgent = _diagnosticAllowedAgent;
                    missionPeer.FollowedAgent = _diagnosticAllowedAgent;
                    peer.ControlledAgent = _diagnosticAllowedAgent;

                    mission.TakeControlOfAgent(_diagnosticAllowedAgent);
                    _diagnosticAllowedAgent.UpdateSpawnEquipmentAndRefreshVisuals(_diagnosticAllowedAgent.SpawnEquipment);
                    _diagnosticAllowedAgent.WieldInitialWeapons(
                        Agent.WeaponWieldActionType.Instant,
                        Equipment.InitialWeaponEquipPreference.Any);

                    GameNetwork.BeginModuleEventAsServer(peer.VirtualPlayer);
                    GameNetwork.WriteMessage(new NetworkMessages.FromServer.SetAgentOwningMissionPeer(_diagnosticAllowedAgent.Index, peer.VirtualPlayer));
                    GameNetwork.EndModuleEventAsServer();

                    _hasTransferredDiagnosticAllowedAgentToPeer = true;
                    _lastDiagnosticOwnershipMission = mission;

                    string previousName = currentlyControlledAgent.Name?.ToString() ?? currentlyControlledAgent.Index.ToString();
                    string newName = _diagnosticAllowedAgent.Name?.ToString() ?? _diagnosticAllowedAgent.Index.ToString();
                    ModLogger.Info(
                        "CoopMissionSpawnLogic: transferred control to diagnostic allowed agent (" + source + "). " +
                        "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                        " PreviousAgent=" + previousName +
                        " NewAgent=" + newName +
                        " TeamIndex=" + (_diagnosticAllowedAgent.Team?.TeamIndex ?? -1) +
                        " Side=" + (_diagnosticAllowedAgent.Team?.Side ?? BattleSideEnum.None) +
                        " Mount=" + _diagnosticAllowedAgent.HasMount);
                    return;
                }
                catch (Exception ex)
                {
                    ModLogger.Info("CoopMissionSpawnLogic: diagnostic control transfer failed (" + source + "): " + ex.Message);
                    return;
                }
            }
        }

    }
}
