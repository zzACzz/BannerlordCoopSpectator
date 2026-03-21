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
using TaleWorlds.MountAndBlade; // Mission, MissionLogic, GameNetwork, Agent, Team, MissionMode
using TaleWorlds.MountAndBlade.Multiplayer;
using TaleWorlds.ObjectSystem; // MBObjectManager
using CoopSpectator.Campaign; // BattleRosterFileHelper (варіант A: roster з кампанії)
using CoopSpectator.Infrastructure; // ModLogger, UiFeedback
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
        private const bool EnableVanillaEntryUiSuppressionExperiment = true;
        private const string FixedClientAttackerCultureId = "empire";
        private const string FixedClientDefenderCultureId = "vlandia";

        public override void AfterStart()
        {
            ModLogger.Info("CoopMissionClientLogic AfterStart ENTER");
            base.AfterStart();
            Mission mission = Mission;
            if (mission == null) return;

            LogMissionEntered(mission);
            LogCurrentState(mission);
            _lastControlledAgent = Agent.Main;
            _timeUntilNextStateLog = LogStateIntervalSeconds;
            _timeUntilNextEntryHint = 0f;
            _timeUntilNextOwnEntryHotkey = 0f;
            _timeUntilNextOwnEntryMenuRefresh = 0f;
            _showOwnEntryMenu = true;
            _lastAnnouncedBattlePhaseKey = null;
            _battleActiveAnnouncementShown = false;
            if (EnableVisualSpawnAutoConfirmExperiment)
                TryHookVisualSpawnAutoConfirm(mission);
            LogClassLoadoutDiagnosticsOnce(mission);
            LogActiveClassLoadoutDiagnosticsOnce(mission);
            LogEntryPolicySnapshot(mission);
            TryMirrorLegacyOverlaySelectionToBridge(mission);
            TryAutoRequestTeamChangeFromBridge(mission);
            TrySuppressVanillaEntryUi(mission);
            TrySyncFixedTeamSelectCultures(mission);
            TrySyncFixedScoreboardCultures(mission);
            TryFilterClassLoadoutToCoopUnits(mission);

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
                    _showOwnEntryMenu = true;
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

            if (EnableVisualSpawnAutoConfirmExperiment)
                TryHookVisualSpawnAutoConfirm(mission);
            LogClassLoadoutDiagnosticsOnce(mission);
            LogActiveClassLoadoutDiagnosticsOnce(mission);
            LogEntryPolicySnapshot(mission);
            TryMirrorAuthoritativeStatusToBridge();
            TryMirrorLegacyOverlaySelectionToBridge(mission);
            TryAutoRequestTeamChangeFromBridge(mission);
            TrySuppressVanillaEntryUi(mission);
            TrySyncFixedTeamSelectCultures(mission);
            TrySyncFixedScoreboardCultures(mission);
            TryFilterClassLoadoutToCoopUnits(mission);
            TryRequestVanillaSpawnFromAuthoritativeStatus(mission);
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

            if (_showOwnEntryMenu)
                return;

            _timeUntilNextEntryHint -= dt;
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot = CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (snapshot == null || !snapshot.HasPeer)
                return;

            string lifecycle = snapshot.LifecycleState ?? string.Empty;
            bool shouldHint =
                !snapshot.HasAgent ||
                snapshot.CanRespawn ||
                string.Equals(lifecycle, "SpawnQueued", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycle, "AwaitingSelection", StringComparison.OrdinalIgnoreCase);
            if (!shouldHint)
                return;

            string side = snapshot.AssignedSide ?? snapshot.RequestedSide ?? snapshot.IntentSide ?? "none";
            string troop = snapshot.SelectedTroopId ?? snapshot.IntentTroopOrEntryId ?? "none";
            string spawn = snapshot.SpawnStatus ?? string.Empty;
            BattleSideEnum resolvedSide = ResolveStatusSide(snapshot);
            string[] sideTroops = GetAllowedTroopIdsForSide(snapshot, resolvedSide);
            int troopIndex = Array.FindIndex(sideTroops, troopId => string.Equals(troopId, troop, StringComparison.OrdinalIgnoreCase));
            string troopOrderText = troopIndex >= 0
                ? " [" + (troopIndex + 1) + "/" + sideTroops.Length + "]"
                : (sideTroops.Length > 0 ? " [0/" + sideTroops.Length + "]" : string.Empty);
            string battlePhase = snapshot.BattlePhase ?? string.Empty;
            string hintKey = side + "|" + troop + "|" + spawn + "|" + battlePhase + "|" + lifecycle + "|" + snapshot.CanRespawn + "|" + snapshot.CanStartBattle + "|" + snapshot.HasAgent;

            if (_timeUntilNextEntryHint > 0f && string.Equals(_lastShownOwnEntryHintKey, hintKey, StringComparison.Ordinal))
                return;

            _lastShownOwnEntryHintKey = hintKey;
            _timeUntilNextEntryHint = EntryHintIntervalSeconds;

            string message =
                "Coop Entry: side=" + side +
                " troop=" + troop +
                troopOrderText +
                " phase=" + battlePhase +
                " state=" + lifecycle +
                " spawn=" + spawn +
                (snapshot.CanRespawn ? " | respawn ready" : string.Empty) +
                (snapshot.CanStartBattle ? " | host can start" : string.Empty) +
                " | keys: Ctrl+1/2 side, Ctrl+Q/E troop, Ctrl+R spawn, Ctrl+B start";
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
            if (!isBattleActive)
                _battleActiveAnnouncementShown = false;

            if (string.Equals(_lastAnnouncedBattlePhaseKey, battlePhase, StringComparison.Ordinal))
                return;

            _lastAnnouncedBattlePhaseKey = battlePhase;
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

            if (!Input.IsKeyDown(InputKey.LeftControl) && !Input.IsKeyDown(InputKey.RightControl))
                return;

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot = CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (snapshot == null)
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

            if (Input.IsKeyPressed(InputKey.B))
            {
                if (!snapshot.CanStartBattle)
                {
                    OnOwnEntryHotkeyHandled("Coop Entry: start battle not ready");
                    return;
                }

                if (CoopBattlePhaseBridgeFile.WriteStartBattleRequest("MP client hotkey"))
                    OnOwnEntryHotkeyHandled("Coop Entry: battle start requested");
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
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return;

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot = CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (snapshot == null || !snapshot.HasPeer)
                return;

            BattleSideEnum side = ResolveStatusSide(snapshot);
            string troopId = ResolveStatusTroopId(snapshot);
            string mirrorKey = side + "|" + (troopId ?? string.Empty);
            if (string.Equals(_lastMirroredAuthoritativeStatusKey, mirrorKey, StringComparison.Ordinal))
                return;

            bool wroteSide = side != BattleSideEnum.None &&
                             CoopBattleSelectionBridgeFile.WriteSelectSideRequest(side.ToString(), "MP client authoritative status");
            bool wroteTroop = !string.IsNullOrWhiteSpace(troopId) &&
                              CoopBattleSelectionBridgeFile.WriteSelectTroopRequest(troopId, "MP client authoritative status");
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
                " TroopId=" + (troopId ?? "null"));
        }

        private void TryRequestVanillaSpawnFromAuthoritativeStatus(Mission mission)
        {
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
            if (!_showOwnEntryMenu || mission == null || !GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return;

            _timeUntilNextOwnEntryMenuRefresh -= dt;

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot = CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (snapshot == null)
                return;

            BattleSideEnum side = ResolveStatusSide(snapshot);
            string sideLabel = side == BattleSideEnum.None ? "none" : side.ToString();
            string troop = ResolveStatusTroopId(snapshot);
            string lifecycle = snapshot.LifecycleState ?? string.Empty;
            string lifecycleSource = snapshot.LifecycleSource ?? string.Empty;
            string battlePhase = string.IsNullOrWhiteSpace(snapshot.BattlePhase) ? "unknown" : snapshot.BattlePhase;
            string spawn = snapshot.SpawnStatus ?? string.Empty;
            string[] troopIds = GetAllowedTroopIdsForSide(snapshot, side);
            int troopIndex = Array.FindIndex(troopIds, troopId => string.Equals(troopId, troop, StringComparison.OrdinalIgnoreCase));
            string troopOrderText = troopIndex >= 0
                ? "[" + (troopIndex + 1) + "/" + troopIds.Length + "]"
                : (troopIds.Length > 0 ? "[0/" + troopIds.Length + "]" : "[0/0]");
            string troopOptionsText = troopIds.Length == 0
                ? "none"
                : string.Join(" | ", troopIds.Select((troopId, index) =>
                    (index + 1) + "=" + troopId + (string.Equals(troopId, troop, StringComparison.OrdinalIgnoreCase) ? "*" : string.Empty)));

            string menuKey =
                sideLabel + "|" +
                troop + "|" +
                troopOrderText + "|" +
                battlePhase + "|" +
                lifecycle + "|" +
                spawn + "|" +
                snapshot.CanRespawn + "|" +
                snapshot.CanStartBattle + "|" +
                snapshot.HasAgent + "|" +
                troopOptionsText;

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
                "Troop: " + troop + " " + troopOrderText + "\n" +
                "State: " + lifecycle + " | Deaths: " + snapshot.DeathCount + "\n" +
                "Source: " + lifecycleSource + "\n" +
                "Spawn: " + spawn + (snapshot.CanRespawn ? " | respawn ready" : string.Empty) + "\n" +
                "Options: " + troopOptionsText + "\n" +
                "Keys: Ctrl+1/2 side | Ctrl+Q/E troop | Ctrl+R spawn | Ctrl+T reset | Ctrl+B start | Ctrl+M menu";
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

            string[] troopIds = GetAllowedTroopIdsForSide(snapshot, side);
            string currentTroopId = ResolveStatusTroopId(snapshot);
            if (troopIds.Length > 0 && !troopIds.Contains(currentTroopId, StringComparer.OrdinalIgnoreCase))
                CoopBattleSelectionBridgeFile.WriteSelectTroopRequest(troopIds[0], "MP client hotkey side default troop");

            string currentTroopLabel = troopIds.Length > 0 ? troopIds[0] + " [1/" + troopIds.Length + "]" : "none";
            OnOwnEntryHotkeyHandled("Coop Entry: side -> " + sideToken + " troop=" + currentTroopLabel);
        }

        private void TryCycleOwnTroopSelection(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot, bool moveNext)
        {
            if (snapshot == null)
                return;

            BattleSideEnum side = ResolveStatusSide(snapshot);
            string[] troopIds = GetAllowedTroopIdsForSide(snapshot, side);
            if (troopIds.Length == 0)
                return;

            string currentTroopId = ResolveStatusTroopId(snapshot);
            int currentIndex = Array.FindIndex(troopIds, troopId => string.Equals(troopId, currentTroopId, StringComparison.OrdinalIgnoreCase));
            int nextIndex;
            if (currentIndex < 0)
            {
                nextIndex = moveNext ? 0 : troopIds.Length - 1;
            }
            else
            {
                int delta = moveNext ? 1 : -1;
                nextIndex = (currentIndex + delta + troopIds.Length) % troopIds.Length;
            }

            string nextTroopId = troopIds[nextIndex];
            if (!CoopBattleSelectionBridgeFile.WriteSelectTroopRequest(nextTroopId, "MP client hotkey troop cycle"))
                return;

            OnOwnEntryHotkeyHandled("Coop Entry: troop -> " + nextTroopId + " [" + (nextIndex + 1) + "/" + troopIds.Length + "] side=" + side);
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

            return snapshot.SelectedTroopId ??
                   snapshot.IntentTroopOrEntryId ??
                   snapshot.SelectionRequestTroopId ??
                   string.Empty;
        }

        private static string[] GetAllowedTroopIdsForSide(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot, BattleSideEnum side)
        {
            if (snapshot == null)
                return Array.Empty<string>();

            string rawTroopIds =
                side == BattleSideEnum.Attacker ? snapshot.AttackerAllowedTroopIds :
                side == BattleSideEnum.Defender ? snapshot.DefenderAllowedTroopIds :
                snapshot.AllowedTroopIds;

            if (string.IsNullOrWhiteSpace(rawTroopIds))
                return Array.Empty<string>();

            return rawTroopIds
                .Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(troopId => troopId.Trim())
                .Where(troopId => !string.IsNullOrWhiteSpace(troopId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        protected override void OnEndMission()
        {
            if (EnableVisualSpawnAutoConfirmExperiment)
                UnhookVisualSpawnAutoConfirm();
            base.OnEndMission();
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
            if (!EnableVisualSpawnAutoConfirmExperiment)
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
        private static readonly HashSet<int> _battlePhaseHeldFormationKeys = new HashSet<int>();
        private static Mission _lastBattlePhaseAiHoldMission;
        private static Mission _lastMaterializedArmyMission;
        private static bool? _lastAppliedBattlePhaseAiHold;
        private static CoopBattlePhase? _lastAppliedFormationHoldPhase;
        private static bool _hasMaterializedBattlefieldArmies;
        private static bool _hasLoggedImportedEquipmentAvailabilityDiagnostics;
        private static bool _hasLoggedMaterializedEquipmentCoverageSummary;
        private static Agent _diagnosticAllowedAgent;
        private const bool EnableFixedMissionCulturesExperiment = true;
        private const string SyntheticAllCampaignTroopsBattleId = "synthetic_all_campaign_troops";
        private const int MaxMaterializedArmyAgentsPerSide = 24;
        private const int MaxMaterializedAgentsPerEntry = 12;
        private const int FallbackMaterializedAgentsPerTroop = 4;
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
                ["peasant_pitchfork_2_t1"] = "cs_exact_peasant_pitchfork_2_t1",
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
                ["peasant_pitchfork_2_t1"] = "western_spear_2_t2",
                ["western_javelin_2_t3"] = "mp_javelin",
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
            };
        private static readonly Dictionary<string, int> MaterializedEquipmentResolutionSourceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> MaterializedEquipmentMissCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> MaterializedEquipmentNormalizedFallbackCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly FormationClass[] RestrictableFormationClasses =
        {
            FormationClass.Infantry,
            FormationClass.Ranged,
            FormationClass.Cavalry,
            FormationClass.HorseArcher
        };

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

        public override void AfterStart()
        {
            ModLogger.Info("CoopMissionSpawnLogic AfterStart ENTER");
            base.AfterStart();
            Mission mission = Mission;
            if (mission == null) return;
            if (_hasLoggedStart) return;
            _hasLoggedStart = true;
            _spawnedCoopPeerIndices.Clear();
            _appliedCoopClassAvailabilityStates.Clear();
            _lastAlignedControlledAgentIndexByPeer.Clear();
            _materializedArmyEntryIdByAgentIndex.Clear();
            _materializedArmySideByAgentIndex.Clear();
            _battlePhaseHeldFormationKeys.Clear();
            _lastBattlePhaseAiHoldMission = null;
            _lastAppliedBattlePhaseAiHold = null;
            _lastAppliedFormationHoldPhase = null;
            _lastMaterializedArmyMission = null;
            _hasMaterializedBattlefieldArmies = false;
            _hasLoggedImportedEquipmentAvailabilityDiagnostics = false;
            CoopBattleSelectionIntentState.Reset();
            CoopBattleSelectionRequestState.Reset();
            CoopBattleSpawnIntentState.Reset();
            CoopBattleSpawnRequestState.Reset();
            CoopBattleSpawnRuntimeState.Reset();
            CoopBattlePeerLifecycleRuntimeState.Reset();

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

            RefreshAllowedTroopsFromRoster("server behavior");
            ModLogger.Info("CoopMissionSpawnLogic: server mission started. Campaign roster: " + CampaignRosterTroopIds.Count + " normalized troop IDs.");
            if (!string.IsNullOrEmpty(SelectedAllowedTroopId))
                ModLogger.Info("CoopMissionSpawnLogic: selected allowed troop id = " + SelectedAllowedTroopId);
            else
                ModLogger.Info("CoopMissionSpawnLogic: no allowed troop id available yet (roster empty).");
            LogAllowedCharacterResolution();
            LogImportedEquipmentAvailabilityDiagnostics();
            CoopBattlePhaseRuntimeState.AdvanceToAtLeast(CoopBattlePhase.SideSelection, "CoopMissionSpawnLogic.AfterStart", mission);

            _timeUntilNextPeerLog = ServerLogIntervalSeconds;
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

            TryConsumeSelectionRequests(Mission);
            TryApplySelectionIntentToPrimaryPeer(Mission, "server behavior");
            TryEnsureBattlefieldArmiesMaterialized(Mission, "server behavior");
            TryConsumeSpawnRequests(Mission);
            TryApplySpawnIntentToPrimaryPeer(Mission, "server behavior");
            TryForceAuthoritativePeerTeams(Mission, "server behavior");
            TryForceFixedMissionCultures(Mission, "server behavior");
            TryForcePreferredHeroClassForPeer(Mission, "server behavior");
            if (EnableMaterializedArmyPossessionExperiment)
                TryTakeControlOfMaterializedArmyAgents(Mission, "server behavior");
            TryRefreshPendingSpawnRequests(Mission, "server behavior");
            TryFinalizePendingVanillaSpawnVisuals(Mission, "server behavior");
            TryAlignControlledAgentsWithMaterializedArmy(Mission, "server behavior");
            TryUpdateBattlePhaseState(Mission, "server behavior tick");
            TryApplyBattlePhaseAiHold(Mission, "server behavior tick");
            TryApplyBattlePhaseFormationHold(Mission, "server behavior tick");
            TryWriteEntryStatusSnapshot(Mission, "server behavior tick");
        }

        protected override void OnEndMission()
        {
            CoopBattleSelectionIntentState.Reset();
            CoopBattleSelectionRequestState.Reset();
            CoopBattleSpawnIntentState.Reset();
            CoopBattleSpawnRequestState.Reset();
            CoopBattleSpawnRuntimeState.Reset();
            CoopBattlePeerLifecycleRuntimeState.Reset();
            _lastAlignedControlledAgentIndexByPeer.Clear();
            _materializedArmyEntryIdByAgentIndex.Clear();
            _materializedArmySideByAgentIndex.Clear();
            _battlePhaseHeldFormationKeys.Clear();
            _lastBattlePhaseAiHoldMission = null;
            _lastAppliedBattlePhaseAiHold = null;
            _lastAppliedFormationHoldPhase = null;
            _lastMaterializedArmyMission = null;
            _hasMaterializedBattlefieldArmies = false;
            CoopBattlePhaseRuntimeState.SetPhase(CoopBattlePhase.BattleEnded, "CoopMissionSpawnLogic.OnEndMission", Mission, allowRegression: true);
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
            if (string.Equals(normalized, "mp_coop_heavy_infantry_empire_troop", StringComparison.Ordinal))
                return "mp_heavy_infantry_empire_troop";
            if (string.Equals(normalized, "mp_coop_heavy_infantry_vlandia_troop", StringComparison.Ordinal))
                return "mp_heavy_infantry_vlandia_troop";
            if (normalized.StartsWith("imperial_"))
                return "imperial_infantryman";
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
                _lastDiagnosticSpawnMission = null;
                _lastDiagnosticOwnershipMission = null;
                _hasSpawnedDiagnosticAllowedAgent = false;
                _hasTransferredDiagnosticAllowedAgentToPeer = false;
                _spawnedCoopPeerIndices.Clear();
                _loggedForcedPreferredClassKeys.Clear();
                _lastBridgedSelectedTroopIndexByPeer.Clear();
                _lastBridgedPeerTeamIndexByPeer.Clear();
                _appliedFixedMissionCultureByPeer.Clear();
                _lastAlignedControlledAgentIndexByPeer.Clear();
                _materializedArmyEntryIdByAgentIndex.Clear();
                _materializedArmySideByAgentIndex.Clear();
                _battlePhaseHeldFormationKeys.Clear();
                _lastBattlePhaseAiHoldMission = null;
                _lastAppliedBattlePhaseAiHold = null;
                _lastAppliedFormationHoldPhase = null;
                _lastMaterializedArmyMission = null;
                _hasMaterializedBattlefieldArmies = false;
                CoopBattleSelectionIntentState.Reset();
                CoopBattleSelectionRequestState.Reset();
                CoopBattleSpawnIntentState.Reset();
                CoopBattleSpawnRequestState.Reset();
                CoopBattleSpawnRuntimeState.Reset();
                CoopBattlePeerLifecycleRuntimeState.Reset();
                _diagnosticAllowedAgent = null;

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

                RefreshAllowedTroopsFromRoster("dedicated observer");
                ModLogger.Info("CoopMissionSpawnLogic: dedicated observer roster loaded. Campaign roster: " + CampaignRosterTroopIds.Count + " normalized troop IDs.");
                if (!string.IsNullOrEmpty(SelectedAllowedTroopId))
                    ModLogger.Info("CoopMissionSpawnLogic: dedicated observer selected allowed troop id = " + SelectedAllowedTroopId);
                else
                    ModLogger.Info("CoopMissionSpawnLogic: dedicated observer found no allowed troop id (roster empty).");
                LogAllowedCharacterResolution();
                CoopBattlePhaseRuntimeState.AdvanceToAtLeast(CoopBattlePhase.SideSelection, "dedicated observer mission detected", mission);
            }

            TryConsumeSelectionRequests(mission);
            TryApplySelectionIntentToPrimaryPeer(mission, "dedicated observer");
            TryForceAuthoritativePeerTeams(mission, "dedicated observer");
            TryForceFixedMissionCultures(mission, "dedicated observer");
            TryEnsureBattlefieldArmiesMaterialized(mission, "dedicated observer");
            TryConsumeSpawnRequests(mission);
            TryApplySpawnIntentToPrimaryPeer(mission, "dedicated observer");
            TryForceAuthoritativePeerTeams(mission, "dedicated observer");
            TryForceFixedMissionCultures(mission, "dedicated observer");
            TryForcePreferredHeroClassForPeer(mission, "dedicated observer");
            if (EnableMaterializedArmyPossessionExperiment)
                TryTakeControlOfMaterializedArmyAgents(mission, "dedicated observer");
            TryFinalizePendingVanillaSpawnVisuals(mission, "dedicated observer");
            TryAlignControlledAgentsWithMaterializedArmy(mission, "dedicated observer");
            TryUpdateBattlePhaseState(mission, "dedicated observer");
            TryConsumeBattlePhaseRequests(mission);
            TryApplyBattlePhaseAiHold(mission, "dedicated observer");
            TryApplyBattlePhaseFormationHold(mission, "dedicated observer");
            TryWriteEntryStatusSnapshot(mission, "dedicated observer");
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

            return _hasMaterializedBattlefieldArmies &&
                assignedPeerCount > 0 &&
                controlledPeerCount >= assignedPeerCount;
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
                ModLogger.Info(
                    "CoopMissionSpawnLogic: ignored start battle request because phase is not ready. " +
                    "CurrentPhase=" + currentPhase +
                    " BattleStartReady=" + battleStartReady +
                    " AssignedPeers=" + assignedPeerCount +
                    " ControlledPeers=" + controlledPeerCount +
                    " MaterializedArmies=" + _hasMaterializedBattlefieldArmies +
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
            bool shouldPauseAi = currentPhase >= CoopBattlePhase.PreBattleHold && currentPhase < CoopBattlePhase.BattleActive;

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

        private static void TryApplyBattlePhaseFormationHold(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer)
                return;

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            bool shouldHoldFormations = currentPhase >= CoopBattlePhase.PreBattleHold && currentPhase < CoopBattlePhase.BattleActive;
            bool shouldReleaseAndPulse = currentPhase >= CoopBattlePhase.BattleActive;

            if (!ReferenceEquals(_lastBattlePhaseAiHoldMission, mission))
            {
                _battlePhaseHeldFormationKeys.Clear();
                _lastAppliedFormationHoldPhase = null;
            }

            if (_lastAppliedFormationHoldPhase.HasValue && _lastAppliedFormationHoldPhase.Value == currentPhase)
                return;

            int affectedFormationCount = 0;
            int delegatedTeamCount = 0;
            int pulsedAgentCount = 0;
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
                        if (ShouldSkipBattlePhaseFormationHold(formation))
                            continue;

                        formation.SetMovementOrder(MovementOrder.MovementOrderStop);
                        formation.SetFiringOrder(FiringOrder.FiringOrderHoldYourFire);
                        formation.SetControlledByAI(true, true);
                        _battlePhaseHeldFormationKeys.Add(formationKey);
                        affectedFormationCount++;
                    }
                    else if (shouldReleaseAndPulse)
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

        private static bool ShouldSkipBattlePhaseFormationHold(Formation formation)
        {
            if (formation == null)
                return true;

            object playerOwner = GetServerMemberValue(formation, "PlayerOwner");
            if (playerOwner != null)
                return true;

            bool hasPlayerControlledTroop = (GetServerMemberValue(formation, "HasPlayerControlledTroop") as bool?) ?? false;
            if (hasPlayerControlledTroop)
                return true;

            bool isPlayerTroopInFormation = (GetServerMemberValue(formation, "IsPlayerTroopInFormation") as bool?) ?? false;
            return isPlayerTroopInFormation;
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
                MaterializedEquipmentResolutionSourceCounts.Clear();
                MaterializedEquipmentMissCounts.Clear();
                MaterializedEquipmentNormalizedFallbackCounts.Clear();
            }

            if (_hasMaterializedBattlefieldArmies)
                return;

            Team attackerTeam = mission.Teams?.Attacker;
            Team defenderTeam = mission.Teams?.Defender;
            if (attackerTeam == null || defenderTeam == null)
                return;

            int attackerCount = MaterializeArmyForSide(mission, attackerTeam, BattleSideEnum.Attacker, source);
            int defenderCount = MaterializeArmyForSide(mission, defenderTeam, BattleSideEnum.Defender, source);
            if (attackerCount <= 0 && defenderCount <= 0)
                return;

            _hasMaterializedBattlefieldArmies = true;
            ModLogger.Info(
                "CoopMissionSpawnLogic: battlefield armies materialized. " +
                "AttackerAgents=" + attackerCount +
                " DefenderAgents=" + defenderCount +
                " Source=" + (source ?? "unknown"));
            LogMaterializedEquipmentCoverageSummaryIfNeeded();
        }

        private static int MaterializeArmyForSide(Mission mission, Team team, BattleSideEnum side, string source)
        {
            if (mission == null || team == null || side == BattleSideEnum.None)
                return 0;

            int spawnedCount = 0;
            IReadOnlyList<RosterEntryState> entryStates = GetAllowedControlEntryStatesSnapshot(side);
            int sideCap = GetMaterializedArmyAgentsPerSideCap(entryStates.Count);
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
                    int remainingCapacity = sideCap - spawnedCount;
                    if (remainingCapacity <= 0)
                        break;

                    int seedCount = Math.Min(1, Math.Min(availableCount, remainingCapacity));
                    if (seedCount <= 0)
                        continue;

                    spawnedCount += SpawnMaterializedAgentsForEntry(
                        mission,
                        team,
                        side,
                        troop,
                        entryState,
                        seedCount,
                        spawnedCount,
                        source + " character=" + resolvedTroopSource + " pass=seed");
                }

                // Second pass: fill remaining side capacity up to per-entry caps.
                foreach ((RosterEntryState entryState, BasicCharacterObject troop, int availableCount, string resolvedTroopSource) in materializableEntries)
                {
                    int remainingCapacity = sideCap - spawnedCount;
                    if (remainingCapacity <= 0)
                        break;

                    int remainingEntryCapacity = Math.Min(availableCount, GetMaterializedAgentsPerEntryCap()) - 1;
                    if (remainingEntryCapacity <= 0)
                        continue;

                    int extraCount = Math.Min(remainingEntryCapacity, remainingCapacity);
                    if (extraCount <= 0)
                        continue;

                    spawnedCount += SpawnMaterializedAgentsForEntry(
                        mission,
                        team,
                        side,
                        troop,
                        entryState,
                        extraCount,
                        spawnedCount,
                        source + " character=" + resolvedTroopSource + " pass=fill");
                }

                return spawnedCount;
            }

            IReadOnlyList<string> troopIds = GetAllowedControlTroopIdsSnapshot(side);
            sideCap = GetMaterializedArmyAgentsPerSideCap(troopIds.Count);
            foreach (string troopId in troopIds)
            {
                BasicCharacterObject troop = ResolveAllowedCharacter(troopId);
                if (troop == null)
                    continue;

                int remainingCapacity = sideCap - spawnedCount;
                if (remainingCapacity <= 0)
                    break;

                int spawnCount = Math.Min(GetFallbackMaterializedAgentsPerTroopCap(), remainingCapacity);
                spawnedCount += SpawnMaterializedAgentsForEntry(mission, team, side, troop, null, spawnCount, spawnedCount, source);
            }

            return spawnedCount;
        }

        private static bool IsSyntheticAllCampaignTroopsRuntime()
        {
            BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
            return string.Equals(snapshot?.BattleId, SyntheticAllCampaignTroopsBattleId, StringComparison.OrdinalIgnoreCase);
        }

        private static int GetMaterializedArmyAgentsPerSideCap(int requestedEntryCount)
        {
            if (!IsSyntheticAllCampaignTroopsRuntime())
                return MaxMaterializedArmyAgentsPerSide;

            return Math.Max(MaxMaterializedArmyAgentsPerSide, Math.Max(0, requestedEntryCount));
        }

        private static int GetMaterializedAgentsPerEntryCap()
        {
            return IsSyntheticAllCampaignTroopsRuntime() ? 1 : MaxMaterializedAgentsPerEntry;
        }

        private static int GetFallbackMaterializedAgentsPerTroopCap()
        {
            return IsSyntheticAllCampaignTroopsRuntime() ? 1 : FallbackMaterializedAgentsPerTroop;
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
            string source)
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
            int spawnedCount = 0;
            for (int i = 0; i < spawnCount; i++)
            {
                int absoluteIndex = sideOffset + spawnedCount;
                int column = absoluteIndex % 4;
                int row = absoluteIndex / 4;
                float lateralOffset = (column - 1.5f) * 1.8f;
                float depthOffset = row * 2.2f * (side == BattleSideEnum.Attacker ? -1f : 1f);
                Vec3 spawnPosition = basePosition + right * lateralOffset + forward * depthOffset;

                string appliedArmorOverrides;
                Agent agent = SpawnBattlefieldArmyAgent(mission, team, troop, entryState, formationClass, spawnPosition, formationDirection, out appliedArmorOverrides);
                if (agent == null)
                    continue;

                _materializedArmyEntryIdByAgentIndex[agent.Index] = entryId ?? string.Empty;
                _materializedArmySideByAgentIndex[agent.Index] = side;
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
                    " SpawnFrameSource=" + spawnFrameSource +
                    " " + equipmentOverrideDiagnostics +
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
            out WorldPosition spawnPosition,
            out Vec2 spawnDirection,
            out string spawnFrameSource)
        {
            spawnPosition = default;
            spawnDirection = side == BattleSideEnum.Attacker ? new Vec2(1f, 0f) : new Vec2(-1f, 0f);
            spawnFrameSource = "direct-fallback";

            try
            {
                mission.GetFormationSpawnFrame(team, formationClass, isReinforcement: false, out spawnPosition, out spawnDirection);
                spawnFrameSource = "formation";
                return;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: failed to resolve formation spawn frame for " + troop.StringId + ": " + ex.Message);
            }

            try
            {
                var spawnFrameBehavior = new FFASpawnFrameBehavior();
                spawnFrameBehavior.Initialize();
                bool hasMount = formationClass == FormationClass.Cavalry || formationClass == FormationClass.HorseArcher;
                MatrixFrame spawnFrame = spawnFrameBehavior.GetSpawnFrame(team, hasMount: hasMount, isInitialSpawn: true);
                if (spawnFrame.origin != Vec3.Zero)
                {
                    spawnPosition = new WorldPosition(mission.Scene, spawnFrame.origin);
                    Vec2 frameDirection = spawnFrame.rotation.f.AsVec2;
                    if (frameDirection.LengthSquared > 0.001f)
                    {
                        spawnDirection = frameDirection.Normalized();
                    }

                    spawnFrameSource = "ffa-scene";
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

        private static Agent SpawnBattlefieldArmyAgent(
            Mission mission,
            Team team,
            BasicCharacterObject troop,
            RosterEntryState entryState,
            FormationClass formationClass,
            Vec3 spawnPosition,
            Vec2 direction,
            out string appliedArmorOverrides)
        {
            appliedArmorOverrides = "(none)";
            if (mission == null || team == null || troop == null)
                return null;

            try
            {
                var origin = new BasicBattleAgentOrigin(troop);
                AgentBuildData buildData = new AgentBuildData(troop);
                Equipment spawnEquipment = troop.Equipment?.Clone(false);
                appliedArmorOverrides = TryApplyMaterializedEquipmentOverrides(spawnEquipment, entryState, null, trackCoverage: true);
                buildData.Team(team);
                buildData.Controller(AgentControllerType.AI);
                buildData.TroopOrigin(origin);
                buildData.InitialPosition(in spawnPosition);
                buildData.InitialDirection(in direction);
                buildData.SpawnsIntoOwnFormation(true);
                buildData.SpawnsUsingOwnTroopClass(true);
                if (spawnEquipment != null)
                    buildData.Equipment(spawnEquipment);

                Agent agent = mission.SpawnAgent(buildData, spawnFromAgentVisuals: false);
                if (agent != null)
                {
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
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: SpawnBattlefieldArmyAgent failed: " + ex.Message);
                return null;
            }
        }

        private static string TryApplyMaterializedEquipmentOverrides(Equipment spawnEquipment, RosterEntryState entryState, List<string> missedSlots = null, bool trackCoverage = false)
        {
            if (spawnEquipment == null || entryState == null)
                return "(none)";

            var appliedSlots = new List<string>();
            if (entryState.IsMounted)
            {
                TryApplyMountedMaterializedWeaponOverrides(spawnEquipment, entryState, appliedSlots, missedSlots, trackCoverage);
            }
            else
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

            var assignedSourceIds = new HashSet<string>(StringComparer.Ordinal);
            AssignMountedMaterializedWeaponByRole(spawnEquipment, EquipmentIndex.Weapon0, resolvedItems, MaterializedMountedWeaponRole.Polearm, assignedSourceIds, appliedSlots);
            AssignMountedMaterializedWeaponByRole(spawnEquipment, EquipmentIndex.Weapon1, resolvedItems, MaterializedMountedWeaponRole.Melee, assignedSourceIds, appliedSlots);
            AssignMountedMaterializedWeaponByRole(spawnEquipment, EquipmentIndex.Weapon2, resolvedItems, MaterializedMountedWeaponRole.Shield, assignedSourceIds, appliedSlots);
            AssignMountedMaterializedWeaponByRole(spawnEquipment, EquipmentIndex.Weapon3, resolvedItems, MaterializedMountedWeaponRole.Ranged, assignedSourceIds, appliedSlots);
            AssignMountedMaterializedWeaponByRole(spawnEquipment, EquipmentIndex.Weapon3, resolvedItems, MaterializedMountedWeaponRole.Ammo, assignedSourceIds, appliedSlots);

            FillMountedMaterializedWeaponFallback(spawnEquipment, EquipmentIndex.Weapon0, resolvedItems, assignedSourceIds, appliedSlots);
            FillMountedMaterializedWeaponFallback(spawnEquipment, EquipmentIndex.Weapon1, resolvedItems, assignedSourceIds, appliedSlots);
            FillMountedMaterializedWeaponFallback(spawnEquipment, EquipmentIndex.Weapon2, resolvedItems, assignedSourceIds, appliedSlots);
            FillMountedMaterializedWeaponFallback(spawnEquipment, EquipmentIndex.Weapon3, resolvedItems, assignedSourceIds, appliedSlots);
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

            _hasLoggedMaterializedEquipmentCoverageSummary = true;
            if (!IsSyntheticAllCampaignTroopsRuntime())
                return;

            string resolutionCounts = FormatMaterializedEquipmentCounterSummary(MaterializedEquipmentResolutionSourceCounts, 8);
            string topMisses = FormatMaterializedEquipmentCounterSummary(MaterializedEquipmentMissCounts, 20);
            string topNormalizedFallbacks = FormatMaterializedEquipmentCounterSummary(MaterializedEquipmentNormalizedFallbackCounts, 20);

            ModLogger.Info(
                "CoopMissionSpawnLogic: synthetic equipment coverage summary. " +
                "ResolutionCounts=[" + resolutionCounts + "] " +
                "TopMisses=[" + topMisses + "] " +
                "TopNormalizedFallbacks=[" + topNormalizedFallbacks + "]");
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
                    normalizedItemId = "mp_western_short_spear";
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
                    LogMaterializedArmyPossessionCandidateMiss(mission, peer, authoritativeSide, pendingRequest, source);
                    continue;
                }

                if (!TryReplaceMaterializedBotWithPlayer(mission, missionPeer, peer, candidateAgent, pendingRequest, source + " army-possession"))
                    continue;

                _spawnedCoopPeerIndices.Add(peer.Index);
                missionPeer.HasSpawnedAgentVisuals = false;
                missionPeer.EquipmentUpdatingExpired = true;
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
            for (int i = 0; i < mission.AllAgents.Count; i++)
            {
                Agent candidate = mission.AllAgents[i];
                if (candidate == null ||
                    !candidate.IsActive() ||
                    candidate.MissionPeer != null ||
                    candidate.Controller == AgentControllerType.Player)
                {
                    continue;
                }

                if (!_materializedArmySideByAgentIndex.TryGetValue(candidate.Index, out BattleSideEnum candidateSide) || candidateSide != side)
                    continue;

                if (!string.IsNullOrWhiteSpace(entryId) &&
                    _materializedArmyEntryIdByAgentIndex.TryGetValue(candidate.Index, out string candidateEntryId) &&
                    string.Equals(candidateEntryId, entryId, StringComparison.Ordinal))
                {
                    return candidate;
                }

                string candidateTroopId = (candidate.Character as BasicCharacterObject)?.StringId;
                if (troopCandidate == null &&
                    !string.IsNullOrWhiteSpace(troopId) &&
                    string.Equals(candidateTroopId, troopId, StringComparison.OrdinalIgnoreCase))
                {
                    troopCandidate = candidate;
                }
            }

            return troopCandidate;
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
                if (candidate == null || !candidate.IsActive())
                    continue;

                if (!_materializedArmySideByAgentIndex.TryGetValue(candidate.Index, out BattleSideEnum candidateSide) || candidateSide != side)
                    continue;

                totalSideCandidates++;
                if (!string.IsNullOrWhiteSpace(pendingRequest.EntryId) &&
                    _materializedArmyEntryIdByAgentIndex.TryGetValue(candidate.Index, out string candidateEntryId) &&
                    string.Equals(candidateEntryId, pendingRequest.EntryId, StringComparison.Ordinal))
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
                missionPeer.FollowedAgent = replacedAgent;

                TryApplyVanillaSpawnGoldDeduction(gameMode, peer, missionPeer, source + " replace-bot");
                bool removedPendingVisuals = TryRemovePendingAgentVisuals(mission, missionPeer);
                missionPeer.HasSpawnedAgentVisuals = false;
                missionPeer.EquipmentUpdatingExpired = true;

                ModLogger.Info(
                    "CoopMissionSpawnLogic: materialized army replace-bot succeeded. " +
                    "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                    " AgentIndex=" + replacedAgent.Index +
                    " Formation=" + targetFormation.FormationIndex +
                    " ActiveAiUnitsInFormation=" + activeAiUnitsInFormation +
                    " PendingTroopId=" + (pendingRequest.TroopId ?? "null") +
                    " PendingEntryId=" + (pendingRequest.EntryId ?? "null") +
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
            BattleRuntimeState rosterState = BattleSnapshotRuntimeState.GetState();
            BattleSnapshotProjectionState rosterProjection = BattleSnapshotRuntimeState.GetProjection();
            if (rosterState == null || rosterProjection == null)
            {
                BattleSnapshotMessage rosterSnapshot = BattleSnapshotRuntimeState.GetCurrent() ?? BattleRosterFileHelper.ReadSnapshot();
                if (rosterSnapshot != null)
                {
                    rosterState = BattleSnapshotRuntimeState.GetState();
                    rosterProjection = BattleSnapshotRuntimeState.GetProjection();
                }
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
                    SelectedAllowedCharacter = ResolveAllowedCharacter(SelectedAllowedTroopId);
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
                    FormatEntryCombatEquipmentSummary(entry) +
                    " Count=" + entry.Count +
                    " Wounded=" + entry.WoundedCount +
                    " Hero=" + entry.IsHero);

            ModLogger.Info(
                "CoopMissionSpawnLogic: snapshot entry mapping summary (" + (source ?? "unknown") + ") = [" +
                string.Join("; ", mappings) +
                "].");
        }

        private static string FormatEntryCombatEquipmentSummary(RosterEntryState entry)
        {
            if (entry == null)
                return string.Empty;

            var parts = new List<string>();
            AddEntryCombatEquipmentSummaryPart(parts, "Item0", entry.CombatItem0Id);
            AddEntryCombatEquipmentSummaryPart(parts, "Item1", entry.CombatItem1Id);
            AddEntryCombatEquipmentSummaryPart(parts, "Item2", entry.CombatItem2Id);
            AddEntryCombatEquipmentSummaryPart(parts, "Item3", entry.CombatItem3Id);
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
            if (entry.HeroLevel > 0)
                parts.Add("Level=" + entry.HeroLevel);
            if (entry.HeroAge > 0.01f)
                parts.Add("Age=" + entry.HeroAge.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            parts.Add("Female=" + entry.HeroIsFemale);

            return parts.Count == 0
                ? string.Empty
                : " " + string.Join(" ", parts);
        }

        private static void AddEntryCombatEquipmentSummaryPart(List<string> parts, string label, string itemId)
        {
            if (parts == null || string.IsNullOrWhiteSpace(itemId))
                return;

            parts.Add(label + "=" + itemId);
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

            BasicCharacterObject resolvedCharacter = ResolveAllowedCharacter(spawnTemplateId);
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
                bool lostControlledAgent =
                    controlledAgent == null ||
                    !controlledAgent.IsActive() ||
                    missionPeer.Team == null ||
                    ReferenceEquals(missionPeer.Team, mission.SpectatorTeam);
                if (_spawnedCoopPeerIndices.Contains(peer.Index) && lostControlledAgent)
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
                    missionPeer.HasSpawnedAgentVisuals = false;
                    missionPeer.EquipmentUpdatingExpired = true;
                    CoopBattleSpawnRuntimeState.Clear(missionPeer, source + " lost-controlled-agent");
                    CoopBattlePeerLifecycleRuntimeState.MarkDeadAwaitingRespawn(missionPeer, lastTroopId, lastEntryId, source + " lost-controlled-agent");
                    ModLogger.Info(
                        "CoopMissionSpawnLogic: peer returned to respawnable state (" + source + "). " +
                        "Peer=" + (peer.UserName ?? peer.Index.ToString()));
                }

                if (missionPeer.ControlledAgent != null)
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

        private static void TryConsumeSelectionRequests(Mission mission)
        {
            if (mission == null || !GameNetwork.IsServer)
                return;

            if (CoopBattleSelectionBridgeFile.ConsumeSelectSideRequest(out string requestedSideRaw, out string sideSource))
                StoreSelectionSideIntent(requestedSideRaw, sideSource ?? "bridge side request");

            if (CoopBattleSelectionBridgeFile.ConsumeSelectTroopRequest(out string troopOrEntryId, out string troopSource))
                StoreSelectionTroopIntent(troopOrEntryId, troopSource ?? "bridge troop request");
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

            missionPeer.HasSpawnedAgentVisuals = false;
            missionPeer.EquipmentUpdatingExpired = true;
            if (missionPeer.SpawnTimer != null)
                missionPeer.SpawnTimer.Reset(mission.CurrentTime, 0f);
            missionPeer.HasSpawnTimerExpired = true;
            missionPeer.WantsToSpawnAsBot = false;
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
            return agent != null && _materializedArmySideByAgentIndex.ContainsKey(agent.Index);
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

        private static void TryApplySelectionIntentToPrimaryPeer(Mission mission, string source)
        {
            MissionPeer missionPeer = ResolvePrimaryControllablePeer(mission);
            if (missionPeer == null)
                return;

            CoopBattleSelectionIntentSnapshot intent = CoopBattleSelectionIntentState.GetCurrent();
            if (intent.Side != BattleSideEnum.None)
            {
                CoopBattleAuthorityState.TryRequestSide(missionPeer, intent.Side, source + " side-intent");
                if (HasAllowedRosterForSide(intent.Side))
                    CoopBattleAuthorityState.TryAssignSide(missionPeer, intent.Side, source + " side-intent");
            }

            if (string.IsNullOrWhiteSpace(intent.TroopOrEntryId))
                return;

            if (CoopBattleAuthorityState.TrySetSelectedEntryId(missionPeer, intent.TroopOrEntryId, source + " intent entry"))
            {
                CoopBattleSelectionRequestState.TryQueueFromAuthoritySelection(missionPeer, source + " selection-request");
                return;
            }

            if (CoopBattleAuthorityState.TrySetSelectedTroopId(missionPeer, intent.TroopOrEntryId, source + " intent troop"))
                CoopBattleSelectionRequestState.TryQueueFromAuthoritySelection(missionPeer, source + " selection-request");
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

            var snapshot = new CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot
            {
                MissionName = mission.SceneName ?? string.Empty,
                Source = source,
                BattlePhase = currentPhase.ToString(),
                BattlePhaseSource = phaseSnapshot?.Source ?? string.Empty,
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
                AttackerAllowedTroopIds = string.Join("|", CoopBattleAuthorityState.GetAllowedTroopIds(BattleSideEnum.Attacker) ?? Array.Empty<string>()),
                AttackerAllowedEntryIds = string.Join("|", CoopBattleAuthorityState.GetAllowedEntryIds(BattleSideEnum.Attacker) ?? Array.Empty<string>()),
                DefenderAllowedTroopIds = string.Join("|", CoopBattleAuthorityState.GetAllowedTroopIds(BattleSideEnum.Defender) ?? Array.Empty<string>()),
                DefenderAllowedEntryIds = string.Join("|", CoopBattleAuthorityState.GetAllowedEntryIds(BattleSideEnum.Defender) ?? Array.Empty<string>()),
                UpdatedUtc = DateTime.UtcNow
            };

            if (missionPeer != null)
            {
                CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
                snapshot.RequestedSide = selectionState.RequestedSide == BattleSideEnum.None ? string.Empty : selectionState.RequestedSide.ToString();
                snapshot.AssignedSide = selectionState.Side == BattleSideEnum.None ? string.Empty : selectionState.Side.ToString();
                snapshot.SelectedTroopId = selectionState.TroopId;
                snapshot.SelectedEntryId = selectionState.EntryId;
                snapshot.AllowedTroopIds = string.Join("|", CoopBattleAuthorityState.GetAllowedTroopIds(selectionState.Side) ?? Array.Empty<string>());
                snapshot.AllowedEntryIds = string.Join("|", CoopBattleAuthorityState.GetAllowedEntryIds(selectionState.Side) ?? Array.Empty<string>());

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
                snapshot.AllowedTroopIds = string.Join("|", CoopBattleAuthorityState.GetAllowedTroopIds(statusSide) ?? Array.Empty<string>());
                snapshot.AllowedEntryIds = string.Join("|", CoopBattleAuthorityState.GetAllowedEntryIds(statusSide) ?? Array.Empty<string>());
            }

            CoopBattleEntryStatusBridgeFile.WriteStatus(snapshot);
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
            if (mission == null || missionPeer == null)
                return false;

            NetworkCommunicator peer = missionPeer.GetNetworkPeer();
            if (peer == null)
                return false;

            if (_spawnedCoopPeerIndices.Contains(peer.Index))
                return false;

            if (missionPeer.ControlledAgent != null && missionPeer.ControlledAgent.IsActive())
                return false;

            BattleSideEnum authoritativeSide = ResolveAuthoritativeSide(missionPeer, mission, "entry-status can-respawn");
            if (authoritativeSide == BattleSideEnum.None)
                return false;

            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            return !string.IsNullOrWhiteSpace(selectionState.EntryId) || !string.IsNullOrWhiteSpace(selectionState.TroopId);
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

                Agent spawnedAgent = SpawnCoopControlledAgent(mission, missionPeer, peer, selectedCharacter, source);
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

            if (_spawnedCoopPeerIndices.Contains(peer.Index))
            {
                reason = "peer already had coop life";
                return false;
            }

            missionPeer = peer.GetComponent<MissionPeer>();
            if (missionPeer == null)
            {
                reason = "mission peer missing";
                return false;
            }

            if (missionPeer.ControlledAgent != null)
            {
                reason = "peer already controls agent";
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

                Equipment spawnEquipment = troop.Equipment?.Clone(false);
                var origin = new BasicBattleAgentOrigin(troop);

                AgentBuildData buildData = new AgentBuildData(troop);
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

                bool spawnFromAgentVisuals = missionPeer.HasSpawnedAgentVisuals;
                Agent spawnedAgent = mission.SpawnAgent(buildData, spawnFromAgentVisuals: spawnFromAgentVisuals);
                if (spawnedAgent == null)
                    return null;

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
                if (spawnedAgent.SpawnEquipment != null)
                    spawnedAgent.UpdateSpawnEquipmentAndRefreshVisuals(spawnedAgent.SpawnEquipment);
                spawnedAgent.WieldInitialWeapons(
                    Agent.WeaponWieldActionType.Instant,
                    Equipment.InitialWeaponEquipPreference.Any);

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
                    " RefreshedSpawnEquipment=" + (spawnedAgent.SpawnEquipment != null) +
                    " AddedPerksComponent=True" +
                    " SpawnCountThisRound=" + missionPeer.SpawnCountThisRound +
                    " ExtraHitpoints=" + extraHitpoints +
                    " VisualsRemoved=" + removedPendingVisuals);

                return spawnedAgent;
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
                RosterEntryState selectedByTroop = ResolveHighestPriorityAllowedEntry(allowedEntries.Where(entry =>
                    string.Equals(entry?.CharacterId, selectionState.TroopId, StringComparison.OrdinalIgnoreCase)));
                if (selectedByTroop != null)
                    return selectedByTroop;
            }

            return ResolveHighestPriorityAllowedEntry(allowedEntries);
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
                CoopMissionSpawnLogic.GetAllowedControlEntryStatesSnapshot(selectionState.Side)
                .Where(entry =>
                    entry != null &&
                    MatchesPreferredHeroClass(heroClass, entry.CharacterId)));
        }

        private static RosterEntryState ResolveHighestPriorityAllowedEntry(IEnumerable<RosterEntryState> entries)
        {
            if (entries == null)
                return null;

            return entries
                .Where(entry => entry != null)
                .OrderBy(GetAllowedEntrySelectionPriority)
                .ThenByDescending(entry => entry.IsHero)
                .ThenByDescending(entry => entry.HeroLevel)
                .ThenByDescending(entry => entry.Tier)
                .ThenBy(entry => entry.EntryId, StringComparer.Ordinal)
                .FirstOrDefault();
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
