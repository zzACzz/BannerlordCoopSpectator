using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.Hideout;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.MissionBehaviors
{
    /// <summary>
    /// Dedicated-safe nighttime hideout runtime. It shares the proven snapshot-backed
    /// agent materializer with the day controller, but owns a separate stealth/call-troops
    /// state machine so the stable daytime flow is not changed by night-specific rules.
    /// </summary>
    internal sealed class CoopExactCampaignHideoutAmbushMissionController
        : CoopExactCampaignHideoutMissionController
    {
        private sealed class SyntheticHideoutAgentOrigin : IAgentOriginBase
        {
            private readonly IAgentOriginBase _source;
            private readonly int _seed;
            private Banner _banner;

            internal SyntheticHideoutAgentOrigin(
                IAgentOriginBase source,
                int ordinal)
            {
                _source = source ?? throw new ArgumentNullException(nameof(source));
                _banner = source.Banner;
                int rawSeed = source.Seed + 1000 + Math.Max(0, ordinal) * 31;
                _seed = Math.Abs(rawSeed % 2001);
            }

            public bool IsUnderPlayersCommand => _source.IsUnderPlayersCommand;

            public bool IsInSameArmyAsPlayer => _source.IsInSameArmyAsPlayer;

            public uint FactionColor => _source.FactionColor;

            public uint FactionColor2 => _source.FactionColor2;

            public IBattleCombatant BattleCombatant => null;

            public int UniqueSeed => _seed;

            public int Seed => _seed;

            public Banner Banner => _banner;

            public BasicCharacterObject Troop => _source.Troop;

            public bool HasThrownWeapon => _source.HasThrownWeapon;

            public bool HasHeavyArmor => _source.HasHeavyArmor;

            public bool HasShield => _source.HasShield;

            public bool HasSpear => _source.HasSpear;

            public void SetWounded()
            {
            }

            public void SetKilled()
            {
            }

            public void SetRouted(bool isOrderRetreat)
            {
            }

            public void OnAgentRemoved(float agentHealth)
            {
            }

            public void OnScoreHit(
                BasicCharacterObject victim,
                BasicCharacterObject formationCaptain,
                int damage,
                bool isFatal,
                bool isTeamKill,
                WeaponComponentData attackerWeapon)
            {
            }

            public void SetBanner(Banner banner)
            {
                if (banner != null)
                    _banner = banner;
            }

            public TroopTraitsMask GetTraitsMask()
            {
                return _source.GetTraitsMask();
            }
        }

        private readonly List<IAgentOriginBase> _reservedPlayerOrigins =
            new List<IAgentOriginBase>();
        private readonly List<IAgentOriginBase> _nightBossBodyguardOrigins =
            new List<IAgentOriginBase>();
        private readonly List<IAgentOriginBase> _nightBodyguardTemplates =
            new List<IAgentOriginBase>();
        private readonly HashSet<int> _sentryAgentIndices = new HashSet<int>();
        private IAgentOriginBase _nightBossOrigin;
        private Agent _nightBossAgent;
        private int _nightBossBodyguardTargetCount;
        private int _syntheticOriginOrdinal;
        private CoopHideoutAmbushPhase _phase =
            CoopHideoutAmbushPhase.WaitingForMaterialization;
        private CoopHideoutSceneManifest _sceneManifest;
        private bool _stealthActivated;
        private bool _callTroopsReadyLogged;
        private bool _reinforcementsSpawned;
        private float _callTroopsTransitionEndsAt;
        private float _alarmStartedAt = -1f;
        private int _stateRevision = 1;

        public CoopExactCampaignHideoutAmbushMissionController(
            IMissionTroopSupplier[] suppliers,
            BattleSideEnum playerSide,
            int firstPhaseEnemyTroopCount)
            : base(suppliers, playerSide, firstPhaseEnemyTroopCount)
        {
        }

        public CoopHideoutAmbushPhase Phase => _phase;

        internal int StateRevision => _stateRevision;

        internal bool IsUsePointAvailable =>
            _phase == CoopHideoutAmbushPhase.Stealth &&
            _sceneManifest?.StealthAreaUsePointFrame != null;

        internal CoopHideoutSceneManifest SceneManifest => _sceneManifest;

        internal bool CanHostUseCallTroopsPoint(
            Agent agent,
            UsableMissionObject usedObject)
        {
            return _phase == CoopHideoutAmbushPhase.Stealth &&
                   IsSupportedUsePoint(usedObject) &&
                   IsHostAgent(agent);
        }

        public override bool IsBossPhaseEligible =>
            _phase == CoopHideoutAmbushPhase.MainCampBattle ||
            _phase == CoopHideoutAmbushPhase.BossBattle;

        internal bool HasNightReservedBossGroup =>
            !_reservedBossGroupSpawned && NightReservedBossGroupCount > 0;

        internal int NightReservedBossGroupCount =>
            _reservedBossGroupSpawned
                ? 0
                : (_nightBossOrigin != null ? 1 : 0) +
                  _nightBossBodyguardTargetCount;

        public override int GetRemainingTroopCount(BattleSideEnum side)
        {
            if (side == _playerSide && !_reinforcementsSpawned)
                return _reservedPlayerOrigins.Count;
            if (side == OpposingSide(_playerSide) && HasNightReservedBossGroup)
                return NightReservedBossGroupCount;
            return base.GetRemainingTroopCount(side);
        }

        public override bool IsSideDepleted(BattleSideEnum side)
        {
            if (side == _playerSide)
            {
                return CountActive(_spawnedPlayerAgents) == 0 &&
                       (_reinforcementsSpawned || _reservedPlayerOrigins.Count == 0);
            }

            if (side == OpposingSide(_playerSide) && HasNightReservedBossGroup)
                return false;

            return base.IsSideDepleted(side);
        }

        internal bool TrySpawnNightReservedBossGroup(
            out Agent bossAgent,
            out int spawnedCount)
        {
            bossAgent = _nightBossAgent;
            spawnedCount = 0;
            if (!GameNetwork.IsServer ||
                !_initialAssaultMaterialized ||
                _reservedBossGroupSpawned ||
                !HasNightReservedBossGroup)
            {
                return bossAgent?.IsActive() == true;
            }

            int requestedCount = NightReservedBossGroupCount;
            List<MatrixFrame> frames = BuildBossSpawnFrames(requestedCount);
            if (frames.Count == 0 || _nightBossOrigin == null)
                return false;

            var bossGroupOrigins = new List<IAgentOriginBase>
            {
                _nightBossOrigin
            };
            bossGroupOrigins.AddRange(_nightBossBodyguardOrigins);
            while (bossGroupOrigins.Count - 1 < _nightBossBodyguardTargetCount)
            {
                IAgentOriginBase template = ResolveRandomNightBodyguardTemplate();
                if (template == null)
                    return false;
                bossGroupOrigins.Add(CreateSyntheticOrigin(template));
            }

            for (int index = 0; index < bossGroupOrigins.Count; index++)
            {
                IAgentOriginBase origin = bossGroupOrigins[index];
                Agent agent = SpawnEnemy(
                    origin,
                    frames[index % frames.Count],
                    isAlarmed: true,
                    wieldInitialWeapons: true);
                if (agent == null)
                    continue;

                _spawnedEnemyAgents.Add(agent);
                spawnedCount++;
                if (index == 0)
                    bossAgent = agent;
            }

            _nightBossAgent = bossAgent;
            _reservedBossGroupSpawned = true;
            ModLogger.Info(
                "CoopExactCampaignHideoutAmbushMissionController: materialized native-shaped night boss group. " +
                "Spawned=" + spawnedCount +
                " Requested=" + requestedCount +
                " OriginalBodyguards=" + _nightBossBodyguardOrigins.Count +
                " SyntheticBodyguards=" +
                Math.Max(0, _nightBossBodyguardTargetCount - _nightBossBodyguardOrigins.Count) +
                " BossAgent=" + (bossAgent?.Index.ToString() ?? "null") + ".");
            return spawnedCount == requestedCount && bossAgent?.IsActive() == true;
        }

        public override void OnMissionTick(float dt)
        {
            if (!GameNetwork.IsServer || !_started || _materializationFaulted)
                return;

            if (!_initialAssaultMaterialized)
            {
                if (CoopBattlePhaseRuntimeState.GetPhase() < CoopBattlePhase.SideSelection)
                    return;

                TryMaterializeNightAmbush();
                return;
            }

            CoopBattlePhase battlePhase = CoopBattlePhaseRuntimeState.GetPhase();
            if (battlePhase < CoopBattlePhase.BattleActive ||
                battlePhase >= CoopBattlePhase.BattleEnded)
            {
                return;
            }

            if (!_stealthActivated)
                ActivateStealthPhase();

            TickSentryGate();
            TickAlarmCounter();

            if (_phase == CoopHideoutAmbushPhase.CallTroops &&
                Mission.CurrentTime >= _callTroopsTransitionEndsAt)
            {
                ActivateMainCampBattle();
            }
        }

        public override void OnObjectUsed(Agent userAgent, UsableMissionObject usedObject)
        {
            if (GameNetwork.IsServer &&
                _phase == CoopHideoutAmbushPhase.Stealth &&
                IsSupportedUsePoint(usedObject) &&
                IsHostAgent(userAgent))
            {
                BeginCallTroops("host-used-stealth-area-use-point");
            }

            base.OnObjectUsed(userAgent, usedObject);
        }

        public override void OnAgentRemoved(
            Agent affectedAgent,
            Agent affectorAgent,
            AgentState agentState,
            KillingBlow blow)
        {
            if (affectedAgent != null)
                _sentryAgentIndices.Remove(affectedAgent.Index);
            base.OnAgentRemoved(affectedAgent, affectorAgent, agentState, blow);
        }

        private void TryMaterializeNightAmbush()
        {
            try
            {
                if (_playerSide != BattleSideEnum.Attacker)
                    throw new InvalidOperationException("night-hideout-player-side-must-be-attacker");
                if (!Mission.MissionBehaviors.Any(behavior => behavior is AgentHumanAILogic))
                    throw new InvalidOperationException("AgentHumanAILogic-missing-before-night-hideout-materialization");

                Team playerTeam = ResolveTeam(_playerSide);
                Team enemyTeam = ResolveTeam(OpposingSide(_playerSide));
                IMissionTroopSupplier playerSupplier = GetSupplier(_playerSide);
                IMissionTroopSupplier enemySupplier = GetSupplier(OpposingSide(_playerSide));
                if (playerTeam == null || enemyTeam == null ||
                    playerSupplier == null || enemySupplier == null)
                {
                    throw new InvalidOperationException("night-hideout-team-or-supplier-missing");
                }

                int enemyTotalCount = Math.Max(0, enemySupplier.NumTroopsNotSupplied);
                if (!CoopHideoutAmbushContract.IsValidNightFirstPhaseParticipantCount(
                        enemyTotalCount,
                        _firstPhaseEnemyTroopCount))
                {
                    throw new InvalidOperationException(
                        "night-hideout-first-phase-contract-invalid Total=" + enemyTotalCount +
                        " FirstPhase=" + _firstPhaseEnemyTroopCount);
                }

                if (!CoopHideoutSceneManifestRuntime.TryResolve(
                        Mission.SceneName,
                        out _sceneManifest,
                        out string manifestDiagnostics) ||
                    _sceneManifest?.HasNightAmbushContract != true)
                {
                    throw new InvalidOperationException(
                        "night-hideout-scene-contract-invalid " + manifestDiagnostics);
                }

                List<IAgentOriginBase> selectedPlayerOrigins = SupplyAll(playerSupplier);
                List<IAgentOriginBase> infiltrationOrigins = selectedPlayerOrigins
                    .Where(origin => origin?.Troop?.IsHero == true)
                    .ToList();
                if (infiltrationOrigins.Count == 0 && selectedPlayerOrigins.Count > 0)
                    infiltrationOrigins.Add(selectedPlayerOrigins[0]);
                _reservedPlayerOrigins.AddRange(selectedPlayerOrigins.Except(infiltrationOrigins));

                List<IAgentOriginBase> selectedEnemyOrigins = enemySupplier
                    .SupplyTroops(enemyTotalCount)
                    .Where(origin => origin != null)
                    .ToList();
                if (infiltrationOrigins.Count == 0 ||
                    selectedEnemyOrigins.Count != enemyTotalCount)
                {
                    throw new InvalidOperationException(
                        "night-hideout-initial-roster-incomplete Infiltration=" + infiltrationOrigins.Count +
                        " EnemyExpected=" + enemyTotalCount +
                        " EnemyActual=" + selectedEnemyOrigins.Count);
                }

                _nightBossOrigin = SelectNightBossOrigin(selectedEnemyOrigins);
                if (_nightBossOrigin == null)
                    throw new InvalidOperationException("night-hideout-boss-origin-missing");

                ModLogger.Info(
                    "CoopExactCampaignHideoutAmbushMissionController: selected reserved boss origin. " +
                    "EntryId=" +
                    ((_nightBossOrigin as ExactCampaignSnapshotAgentOrigin)?.EntryId ?? "null") +
                    " TroopId=" + (_nightBossOrigin.Troop?.StringId ?? "null") +
                    " Priority=" + ResolveNightBossOriginPriority(_nightBossOrigin) + ".");

                List<IAgentOriginBase> nonBossEnemyOrigins = selectedEnemyOrigins
                    .Where(origin => !ReferenceEquals(origin, _nightBossOrigin))
                    .ToList();
                if (nonBossEnemyOrigins.Count == 0)
                    throw new InvalidOperationException("night-hideout-sentry-template-origin-missing");

                _nightBodyguardTemplates.AddRange(nonBossEnemyOrigins);
                var initialEnemyOrigins = nonBossEnemyOrigins
                    .Take(_firstPhaseEnemyTroopCount)
                    .ToList();
                _nightBossBodyguardOrigins.AddRange(
                    nonBossEnemyOrigins.Skip(initialEnemyOrigins.Count));
                while (initialEnemyOrigins.Count < _firstPhaseEnemyTroopCount)
                {
                    IAgentOriginBase template = ResolveRandomNightBodyguardTemplate();
                    if (template == null)
                        throw new InvalidOperationException("night-hideout-synthetic-sentry-template-missing");
                    initialEnemyOrigins.Add(CreateSyntheticOrigin(template));
                }

                int syntheticInitialEnemyCount = Math.Max(
                    0,
                    _firstPhaseEnemyTroopCount -
                    Math.Min(nonBossEnemyOrigins.Count, _firstPhaseEnemyTroopCount));
                int expectedSyntheticInitialEnemyCount =
                    CoopHideoutAmbushContract.ResolveSyntheticInitialEnemyCount(
                        enemyTotalCount,
                        _firstPhaseEnemyTroopCount,
                        hasSeparateBossOrigin: true);
                _nightBossBodyguardTargetCount = Math.Max(
                    _nightBossBodyguardOrigins.Count,
                    CoopHideoutAmbushContract.ResolveBossBodyguardCount(enemyTotalCount));

                List<DefenderPlacementSlot> defenderSlots = CollectHideoutDefenderSlots(
                    allowPartialManifestMetadata: true);
                if (defenderSlots.Count == 0)
                    throw new InvalidOperationException("night-hideout-defender-scene-frames-empty");

                List<DefenderPlacementSlot> forcedSentrySlots = defenderSlots
                    .Where(slot => CoopHideoutAmbushContract.IsForcedSentrySpawnGroup(
                        ResolveSpawnGroupTag(slot)))
                    .ToList();
                List<DefenderPlacementSlot> optionalSentrySlots = defenderSlots
                    .Where(slot => CoopHideoutAmbushContract.IsOptionalSentrySpawnGroup(
                        ResolveSpawnGroupTag(slot)))
                    .ToList();
                var nightSentrySlots = new List<DefenderPlacementSlot>();
                nightSentrySlots.AddRange(forcedSentrySlots.Take(initialEnemyOrigins.Count));
                if (nightSentrySlots.Count < initialEnemyOrigins.Count)
                {
                    int optionalCount = CoopHideoutAmbushContract.ResolveOptionalSentryRouteCount(
                        initialEnemyOrigins.Count,
                        optionalSentrySlots.Count);
                    nightSentrySlots.AddRange(optionalSentrySlots.Take(optionalCount));
                }
                if (nightSentrySlots.Count < initialEnemyOrigins.Count)
                {
                    nightSentrySlots.AddRange(defenderSlots
                        .Where(slot => !nightSentrySlots.Contains(slot))
                        .Take(initialEnemyOrigins.Count - nightSentrySlots.Count));
                }
                if (nightSentrySlots.Count == 0)
                    throw new InvalidOperationException("night-hideout-authored-stealth-routes-empty");

                Mission.DeploymentPlan.MakeDefaultDeploymentPlans();
                SetTeamsAsEnemies(playerTeam, enemyTeam, false);
                SpawnPlayerGroup(infiltrationOrigins);

                CoopHideoutStealthPatrolController stealthController =
                    Mission.GetMissionBehavior<CoopHideoutStealthPatrolController>();
                for (int index = 0; index < initialEnemyOrigins.Count; index++)
                {
                    DefenderPlacementSlot slot =
                        nightSentrySlots[index % nightSentrySlots.Count];
                    Agent agent = SpawnEnemy(
                        initialEnemyOrigins[index],
                        slot.SpawnFrame,
                        isAlarmed: false,
                        wieldInitialWeapons: false);
                    if (agent == null)
                        continue;

                    _spawnedEnemyAgents.Add(agent);
                    stealthController?.RegisterDefender(
                        agent,
                        slot.PatrolPoints,
                        carryNightTorch: slot.PatrolPoints.Any(
                            point => point?.HasTorchTag == true),
                        forceWalkPatrol: true);
                    if (IsSentrySlot(slot))
                        _sentryAgentIndices.Add(agent.Index);
                }

                _initialAssaultEnemyCount = _spawnedEnemyAgents.Count;
                if (_spawnedPlayerAgents.Count == 0 || _initialAssaultEnemyCount == 0)
                    throw new InvalidOperationException("night-hideout-initial-agent-materialization-empty");
                if (_sentryAgentIndices.Count == 0)
                    throw new InvalidOperationException("night-hideout-stealth-area-sentry-classification-empty");

                HoldPlayerFormations();
                _initialAssaultMaterialized = true;
                SetPhase(CoopHideoutAmbushPhase.Stealth);
                ModLogger.Info(
                    "CoopExactCampaignHideoutAmbushMissionController: initial nighttime infiltration materialized. " +
                    "InfiltrationHeroes=" + _spawnedPlayerAgents.Count +
                    " ReservedAllies=" + _reservedPlayerOrigins.Count +
                    " InitialEnemies=" + _initialAssaultEnemyCount +
                    " Sentries=" + _sentryAgentIndices.Count +
                    " SyntheticInitialEnemies=" + syntheticInitialEnemyCount +
                    " ExpectedSyntheticInitialEnemies=" + expectedSyntheticInitialEnemyCount +
                    " ReservedBossGroup=" + NightReservedBossGroupCount +
                    " DefenderSlots=" + defenderSlots.Count +
                    " ForcedSentrySlots=" + forcedSentrySlots.Count +
                    " OptionalSentrySlots=" + optionalSentrySlots.Count +
                    " SelectedNightSlots=" + nightSentrySlots.Count +
                    " Manifest={" + manifestDiagnostics + "}.");
            }
            catch (Exception ex)
            {
                _materializationFaulted = true;
                SetPhase(CoopHideoutAmbushPhase.Faulted);
                ModLogger.Error(
                    "CoopExactCampaignHideoutAmbushMissionController: nighttime infiltration materialization failed.",
                    ex);
            }
        }

        private void ActivateStealthPhase()
        {
            Team playerTeam = ResolveTeam(_playerSide);
            Team enemyTeam = ResolveTeam(OpposingSide(_playerSide));
            SetTeamsAsEnemies(playerTeam, enemyTeam, true);
            Mission.SetMissionMode((MissionMode)4, true);
            Mission.GetMissionBehavior<CoopHideoutStealthPatrolController>()?.Activate(
                useNightAmbushAlarmSemantics: true);
            HoldPlayerFormations();
            _stealthActivated = true;
            ModLogger.Info(
                "CoopExactCampaignHideoutAmbushMissionController: authoritative stealth phase activated. " +
                "Sentries=" + _sentryAgentIndices.Count +
                " ActiveEnemies=" + CountActive(_spawnedEnemyAgents) + ".");
        }

        private void TickSentryGate()
        {
            _sentryAgentIndices.RemoveWhere(index =>
                !_spawnedEnemyAgents.Any(agent =>
                    agent?.Index == index && agent.IsActive()));
            if (!AreSentriesCleared() || _callTroopsReadyLogged)
                return;

            _callTroopsReadyLogged = true;
            ModLogger.Info(
                "CoopExactCampaignHideoutAmbushMissionController: all sentries cleared; host call-troops interaction is ready. " +
                "ActiveCampEnemies=" + CountActive(_spawnedEnemyAgents) + ".");
        }

        private void TickAlarmCounter()
        {
            CoopHideoutStealthPatrolController stealthController =
                Mission.GetMissionBehavior<CoopHideoutStealthPatrolController>();
            if (stealthController?.HasGlobalAlarm != true)
            {
                _alarmStartedAt = -1f;
                return;
            }

            if (_alarmStartedAt < 0f)
            {
                _alarmStartedAt = Mission.CurrentTime;
                ModLogger.Info(
                    "CoopExactCampaignHideoutAmbushMissionController: stealth alarm counter started. " +
                    "FailureWindowSeconds=" + CoopHideoutAmbushContract.AlarmFailureSeconds + ".");
                return;
            }

            if (_phase == CoopHideoutAmbushPhase.Stealth &&
                Mission.CurrentTime - _alarmStartedAt >= CoopHideoutAmbushContract.AlarmFailureSeconds)
            {
                ModLogger.Verbose(
                    "CoopExactCampaignHideoutAmbushMissionController: native-compatible alarm failure boundary reached; " +
                    "mission failure writeback remains disabled in the first nighttime vertical slice.");
                _alarmStartedAt = float.MaxValue;
            }
        }

        internal bool TryBeginCallTroopsFromPeer(
            NetworkCommunicator peer,
            out string rejection)
        {
            rejection = string.Empty;
            Agent hostAgent = ResolveControlledAgent(peer);
            if (!IsHostAgent(hostAgent))
            {
                rejection = "call-troops-sender-not-host";
                return false;
            }

            if (_phase >= CoopHideoutAmbushPhase.CallTroops &&
                _phase < CoopHideoutAmbushPhase.Faulted)
            {
                return true;
            }

            if (_phase != CoopHideoutAmbushPhase.Stealth)
            {
                rejection = "call-troops-phase-invalid:" + _phase;
                return false;
            }

            BeginCallTroops("validated-host-network-use");
            return _phase == CoopHideoutAmbushPhase.CallTroops;
        }

        private void BeginCallTroops(string reason)
        {
            if (_phase != CoopHideoutAmbushPhase.Stealth)
                return;

            FadeRemainingStealthAreaSentries();
            SpawnReservedPlayerGroup();
            SetPhase(CoopHideoutAmbushPhase.CallTroops);
            _callTroopsTransitionEndsAt =
                Mission.CurrentTime + CoopHideoutAmbushContract.CallTroopsTransitionSeconds;
            ModLogger.Info(
                "CoopExactCampaignHideoutAmbushMissionController: call-troops transition started. " +
                "Reason=" + (reason ?? "unknown") +
                " SpawnedAllies=" + (_reinforcementsSpawned ? _reservedPlayerOrigins.Count : 0) +
                " DurationSeconds=" + CoopHideoutAmbushContract.CallTroopsTransitionSeconds + ".");
        }

        private void FadeRemainingStealthAreaSentries()
        {
            int requested = _sentryAgentIndices.Count;
            int faded = 0;
            foreach (Agent agent in _spawnedEnemyAgents.Where(candidate =>
                         candidate != null &&
                         _sentryAgentIndices.Contains(candidate.Index) &&
                         candidate.IsActive()).ToList())
            {
                try
                {
                    agent.FadeOut(true, true);
                    faded++;
                }
                catch
                {
                }
            }

            _sentryAgentIndices.Clear();
            ModLogger.Info(
                "CoopExactCampaignHideoutAmbushMissionController: removed remaining stealth-area sentries for call-troops transition. " +
                "Requested=" + requested + " Faded=" + faded + ".");
        }

        private void SpawnReservedPlayerGroup()
        {
            if (_reinforcementsSpawned)
                return;

            List<MatrixFrame> spawnFrames = FindSceneFrames(
                CoopHideoutAmbushContract.ReinforcementSpawnPointTag);
            List<MatrixFrame> waitFrames = FindSceneFrames(
                CoopHideoutAmbushContract.ReinforcementWaitPointTag);
            for (int index = 0; index < _reservedPlayerOrigins.Count; index++)
            {
                MatrixFrame? spawnFrame = spawnFrames.Count > 0
                    ? spawnFrames[index % spawnFrames.Count]
                    : (MatrixFrame?)null;
                Vec3? position = spawnFrame?.origin;
                Vec2? direction = null;
                if (spawnFrame.HasValue)
                {
                    Vec2 resolvedDirection = spawnFrame.Value.rotation.f.AsVec2;
                    if (resolvedDirection.LengthSquared < 0.0001f)
                        resolvedDirection = new Vec2(0f, 1f);
                    resolvedDirection.Normalize();
                    direction = resolvedDirection;
                }

                Agent agent = Mission.SpawnTroop(
                    _reservedPlayerOrigins[index],
                    isPlayerSide: true,
                    hasFormation: true,
                    spawnWithHorse: false,
                    isReinforcement: true,
                    _reservedPlayerOrigins.Count,
                    index,
                    isAlarmed: false,
                    wieldInitialWeapons: false,
                    position,
                    direction);
                if (agent == null)
                    continue;

                _spawnedPlayerAgents.Add(agent);
                if (waitFrames.Count > 0)
                    TryMoveAgentToFrame(agent, waitFrames[index % waitFrames.Count]);
            }

            _reinforcementsSpawned = true;
            ModLogger.Info(
                "CoopExactCampaignHideoutAmbushMissionController: allied reserve materialized at authored reinforcement points. " +
                "Requested=" + _reservedPlayerOrigins.Count +
                " SpawnFrames=" + spawnFrames.Count +
                " WaitFrames=" + waitFrames.Count + ".");
        }

        private void ActivateMainCampBattle()
        {
            Mission.SetMissionMode((MissionMode)2, false);
            foreach (Agent agent in _spawnedPlayerAgents)
            {
                if (agent?.IsActive() != true)
                    continue;
                agent.SetIsAIPaused(false);
                agent.DisableScriptedMovement();
                agent.SetWatchState(Agent.WatchState.Alarmed);
                TryWieldInitialSlots(agent);
            }

            CoopHideoutStealthPatrolController stealthController =
                Mission.GetMissionBehavior<CoopHideoutStealthPatrolController>();
            stealthController?.ActivateGlobalAlarm("night-hideout-main-camp-transition");

            Team playerTeam = ResolveTeam(_playerSide);
            foreach (Formation formation in playerTeam?.FormationsIncludingEmpty ?? Enumerable.Empty<Formation>())
            {
                if (formation == null || formation.CountOfUnits <= 0)
                    continue;
                formation.SetMovementOrder(MovementOrder.MovementOrderCharge);
                formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
            }

            _combatActivated = true;
            SetPhase(CoopHideoutAmbushPhase.MainCampBattle);
            ModLogger.Info(
                "CoopExactCampaignHideoutAmbushMissionController: main camp battle activated. " +
                "PlayerActive=" + CountActive(_spawnedPlayerAgents) +
                " EnemyActive=" + CountActive(_spawnedEnemyAgents) +
                " ReservedBossGroup=" + NightReservedBossGroupCount + ".");
        }

        private bool IsSentrySlot(DefenderPlacementSlot slot)
        {
            if (slot?.PatrolPoints == null || _sceneManifest?.StealthAreaMarkers == null)
                return false;

            if (!CoopHideoutAmbushContract.IsSentrySpawnGroup(
                    ResolveSpawnGroupTag(slot)))
                return false;

            Vec3 position = slot.SpawnFrame.origin;
            return _sceneManifest.StealthAreaMarkers.Any(marker =>
                marker?.Contains(position.x, position.y) == true);
        }

        private static string ResolveSpawnGroupTag(DefenderPlacementSlot slot)
        {
            return slot?.PatrolPoints?
                .Select(point => point?.SpawnGroupTag)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ??
                string.Empty;
        }

        private void SetPhase(CoopHideoutAmbushPhase phase)
        {
            if (_phase == phase)
                return;
            _phase = phase;
            _stateRevision++;
        }

        private bool AreSentriesCleared()
        {
            return _sentryAgentIndices.Count == 0;
        }

        private static bool IsSupportedUsePoint(UsableMissionObject usedObject)
        {
            if (usedObject == null)
                return false;
            if (string.Equals(
                    usedObject.GetType().FullName,
                    CoopHideoutAmbushContract.StealthAreaUsePointTypeName,
                    StringComparison.Ordinal))
            {
                return true;
            }

            try
            {
                WeakGameEntity gameEntity = usedObject.GameEntity;
                return gameEntity.IsValid &&
                       string.Equals(
                           gameEntity.Name,
                           "stealth_area_use_point",
                           StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsHostAgent(Agent agent)
        {
            if (agent?.IsActive() != true || agent.MissionPeer == null)
                return false;

            NetworkCommunicator peer = agent.MissionPeer.GetNetworkPeer();
            if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive)
                return false;

            if (HostSelfJoinRedirectState.TryResolvePersistedHostedPeerUserName(
                    out string hostUserName) &&
                !string.IsNullOrWhiteSpace(hostUserName))
            {
                return string.Equals(
                    peer.UserName,
                    hostUserName,
                    StringComparison.OrdinalIgnoreCase);
            }

            return GameNetwork.NetworkPeers?
                .Where(candidate =>
                    candidate != null &&
                    !candidate.IsServerPeer &&
                    candidate.IsConnectionActive &&
                    candidate.IsSynchronized)
                .OrderBy(candidate => candidate.Index)
                .FirstOrDefault()?.Index == peer.Index;
        }

        private static Agent ResolveHostAgent()
        {
            if (GameNetwork.NetworkPeers == null)
                return null;

            if (HostSelfJoinRedirectState.TryResolvePersistedHostedPeerUserName(
                    out string hostUserName) &&
                !string.IsNullOrWhiteSpace(hostUserName))
            {
                NetworkCommunicator markedHost = GameNetwork.NetworkPeers.FirstOrDefault(peer =>
                    peer != null &&
                    !peer.IsServerPeer &&
                    peer.IsConnectionActive &&
                    peer.IsSynchronized &&
                    string.Equals(peer.UserName, hostUserName, StringComparison.OrdinalIgnoreCase));
                Agent markedAgent = ResolveControlledAgent(markedHost);
                if (markedAgent?.IsActive() == true)
                    return markedAgent;
            }

            return GameNetwork.NetworkPeers
                .Where(peer =>
                    peer != null &&
                    !peer.IsServerPeer &&
                    peer.IsConnectionActive &&
                    peer.IsSynchronized)
                .OrderBy(peer => peer.Index)
                .Select(ResolveControlledAgent)
                .FirstOrDefault(agent => agent?.IsActive() == true);
        }

        private static Agent ResolveControlledAgent(NetworkCommunicator peer)
        {
            MissionPeer missionPeer = peer?.GetComponent<MissionPeer>();
            return missionPeer?.ControlledAgent ?? peer?.ControlledAgent;
        }

        private static IAgentOriginBase SelectNightBossOrigin(
            IEnumerable<IAgentOriginBase> origins)
        {
            return (origins ?? Enumerable.Empty<IAgentOriginBase>())
                .Select((origin, index) => new
                {
                    Origin = origin,
                    Index = index,
                    Priority = ResolveNightBossOriginPriority(origin)
                })
                .Where(candidate => candidate.Origin != null && candidate.Priority > 0)
                .OrderByDescending(candidate => candidate.Priority)
                .ThenBy(candidate => candidate.Index)
                .Select(candidate => candidate.Origin)
                .FirstOrDefault();
        }

        private static int ResolveNightBossOriginPriority(IAgentOriginBase origin)
        {
            var exactOrigin = origin as ExactCampaignSnapshotAgentOrigin;
            RosterEntryState entry = string.IsNullOrWhiteSpace(exactOrigin?.EntryId)
                ? null
                : BattleSnapshotRuntimeState.GetEntryState(exactOrigin.EntryId);
            return CoopHideoutAmbushContract.ResolveBossIdentityPriority(
                exactOrigin?.EntryId,
                exactOrigin?.TroopId,
                entry?.OriginalCharacterId,
                entry?.HeroTemplateId,
                entry?.CharacterId,
                entry?.SpawnTemplateId,
                entry?.TroopName,
                origin?.Troop?.StringId);
        }

        private IAgentOriginBase ResolveRandomNightBodyguardTemplate()
        {
            if (_nightBodyguardTemplates.Count == 0)
                return null;

            return _nightBodyguardTemplates[
                MBRandom.RandomInt(_nightBodyguardTemplates.Count)];
        }

        private IAgentOriginBase CreateSyntheticOrigin(IAgentOriginBase template)
        {
            return template == null
                ? null
                : new SyntheticHideoutAgentOrigin(
                    template,
                    ++_syntheticOriginOrdinal);
        }

        private List<MatrixFrame> FindSceneFrames(string tag)
        {
            try
            {
                return (Mission?.Scene?.FindEntitiesWithTag(tag) ?? Enumerable.Empty<GameEntity>())
                    .Where(entity => entity != null)
                    .Select(entity => entity.GetGlobalFrame())
                    .ToList();
            }
            catch
            {
                return new List<MatrixFrame>();
            }
        }

        private void TryMoveAgentToFrame(Agent agent, MatrixFrame frame)
        {
            if (agent?.IsActive() != true)
                return;

            try
            {
                Vec2 direction = frame.rotation.f.AsVec2;
                if (direction.LengthSquared < 0.0001f)
                    direction = new Vec2(0f, 1f);
                direction.Normalize();
                var worldPosition = new WorldPosition(
                    Mission.Scene,
                    UIntPtr.Zero,
                    frame.origin,
                    hasValidZ: false);
                agent.SetScriptedPositionAndDirection(
                    ref worldPosition,
                    direction.RotationInRadians,
                    addHumanLikeDelay: false);
            }
            catch
            {
            }
        }
    }
}
