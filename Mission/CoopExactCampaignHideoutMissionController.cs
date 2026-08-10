using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.Hideout;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.MissionBehaviors
{
    internal sealed class CoopHideoutPatrolPointDefinition
    {
        internal MatrixFrame Frame { get; set; }

        internal int Index { get; set; }

        internal int WaitDurationSeconds { get; set; } = 1;

        internal int WaitDeviationSeconds { get; set; }

        internal bool IsInfiniteWaitPoint { get; set; }

        internal float PatrollingSpeed { get; set; } = -1f;

        internal string LoopAction { get; set; } = string.Empty;

        internal string SpawnGroupTag { get; set; } = string.Empty;

        internal bool HasTorchTag { get; set; }
    }

    /// <summary>
    /// Snapshot-backed day-hideout materializer. It keeps the multiplayer shell and
    /// campaign snapshot contracts, but follows the native hideout placement shape
    /// instead of routing the scene through the open-field AgentBuildData path.
    /// </summary>
    internal class CoopExactCampaignHideoutMissionController : MissionLogic, IMissionAgentSpawnLogic
    {
        private const string HideoutBanditActionSetSuffix = "_hideout_bandit";
        private const string PatrolPointScriptName = "PatrolPoint";
        private const string PatrolPointTypeFullName = "SandBox.Objects.PatrolPoint";
        private const string UnsetScriptStringValue = "__coop_unset_script_value__";

        protected sealed class DefenderPlacementSlot
        {
            internal MatrixFrame SpawnFrame { get; set; }

            internal List<CoopHideoutPatrolPointDefinition> PatrolPoints { get; set; } =
                new List<CoopHideoutPatrolPointDefinition>();
        }

        protected readonly IMissionTroopSupplier[] _suppliers;
        protected readonly BattleSideEnum _playerSide;
        protected readonly int _firstPhaseEnemyTroopCount;
        protected readonly List<Agent> _spawnedPlayerAgents = new List<Agent>();
        protected readonly List<Agent> _spawnedEnemyAgents = new List<Agent>();
        protected bool _initialized;
        protected bool _started;
        protected bool _initialAssaultMaterialized;
        protected bool _combatActivated;
        protected bool _reservedBossGroupSpawned;
        protected bool _materializationFaulted;
        private bool _attackerSpawnerEnabled = true;
        private bool _defenderSpawnerEnabled = true;
        protected int _initialAssaultEnemyCount;
        private Agent _reservedBossAgent;

        public CoopExactCampaignHideoutMissionController(
            IMissionTroopSupplier[] suppliers,
            BattleSideEnum playerSide,
            int firstPhaseEnemyTroopCount)
        {
            _suppliers = suppliers ?? Array.Empty<IMissionTroopSupplier>();
            _playerSide = playerSide;
            _firstPhaseEnemyTroopCount = firstPhaseEnemyTroopCount;
        }

        public bool HasStarted => _started;

        public BattleSideEnum PlayerSide => _playerSide;

        public int FirstPhaseEnemyTroopCount => _firstPhaseEnemyTroopCount;

        public bool HasMaterializedBothSides =>
            _initialAssaultMaterialized &&
            _spawnedPlayerAgents.Any(agent => agent?.IsActive() == true) &&
            _spawnedEnemyAgents.Any(agent => agent?.IsActive() == true);

        public bool HasReservedBossGroup =>
            !_reservedBossGroupSpawned && ReservedEnemyCount > 0;

        public int InitialAssaultEnemyCount => _initialAssaultEnemyCount;

        public int ReservedEnemyCount =>
            _reservedBossGroupSpawned
                ? 0
                : Math.Max(
                    0,
                    GetSupplier(OpposingSide(_playerSide))?.NumTroopsNotSupplied ?? 0);

        public Agent ReservedBossAgent => _reservedBossAgent;

        public virtual bool IsBossPhaseEligible => _combatActivated;

        public void EnsureInitializedAndStarted()
        {
            if (!_initialized)
                OnBehaviorInitialize();
            if (!_started)
                AfterStart();
        }

        public override void OnBehaviorInitialize()
        {
            if (_initialized)
                return;

            base.OnBehaviorInitialize();
            Mission.DoesMissionRequireCivilianEquipment = false;
            _initialized = true;
        }

        public override void AfterStart()
        {
            if (_started)
                return;

            base.AfterStart();
            _started = true;
        }

        public override void OnMissionTick(float dt)
        {
            if (!GameNetwork.IsServer || !_started || _materializationFaulted)
                return;

            if (!_initialAssaultMaterialized)
            {
                if (CoopBattlePhaseRuntimeState.GetPhase() < CoopBattlePhase.SideSelection)
                    return;

                TryMaterializeInitialAssault();
                return;
            }

            if (!_combatActivated &&
                CoopBattlePhaseRuntimeState.GetPhase() >= CoopBattlePhase.BattleActive &&
                CoopBattlePhaseRuntimeState.GetPhase() < CoopBattlePhase.BattleEnded)
            {
                ActivateCombat();
            }
        }

        public bool TrySpawnReservedBossGroup(out Agent bossAgent, out int spawnedCount)
        {
            bossAgent = _reservedBossAgent;
            spawnedCount = 0;
            if (!GameNetwork.IsServer ||
                !_initialAssaultMaterialized ||
                _reservedBossGroupSpawned ||
                ReservedEnemyCount == 0)
            {
                return bossAgent?.IsActive() == true;
            }

            Team enemyTeam = ResolveTeam(OpposingSide(_playerSide));
            if (enemyTeam == null)
                return false;

            IMissionTroopSupplier enemySupplier = GetSupplier(OpposingSide(_playerSide));
            int requestedCount = Math.Max(0, enemySupplier?.NumTroopsNotSupplied ?? 0);
            List<MatrixFrame> frames = BuildBossSpawnFrames(requestedCount);
            if (frames.Count == 0)
                return false;

            List<IAgentOriginBase> reservedEnemyOrigins = enemySupplier
                .SupplyTroops(requestedCount)
                .Where(origin => origin != null)
                .ToList();
            for (int index = 0; index < reservedEnemyOrigins.Count; index++)
            {
                IAgentOriginBase origin = reservedEnemyOrigins[index];
                MatrixFrame frame = frames[index % frames.Count];
                Agent agent = SpawnEnemy(origin, frame, isAlarmed: true, wieldInitialWeapons: true);
                if (agent == null)
                    continue;

                _spawnedEnemyAgents.Add(agent);
                spawnedCount++;
                if (bossAgent == null || IsBossOrigin(origin))
                    bossAgent = agent;
            }

            _reservedBossAgent = bossAgent;
            _reservedBossGroupSpawned = true;
            ModLogger.Info(
                "CoopExactCampaignHideoutMissionController: materialized reserved boss group. " +
                "Spawned=" + spawnedCount +
                " Requested=" + requestedCount +
                " BossAgent=" + (bossAgent?.Index.ToString() ?? "null") + ".");
            return spawnedCount > 0 && bossAgent?.IsActive() == true;
        }

        public string BuildRuntimeSummary()
        {
            return
                "Mode=Hideout" +
                " Started=" + _started +
                " Materialized=" + _initialAssaultMaterialized +
                " CombatActive=" + _combatActivated +
                " Faulted=" + _materializationFaulted +
                " PlayerActive=" + CountActive(_spawnedPlayerAgents) +
                " EnemyActive=" + CountActive(_spawnedEnemyAgents) +
                " InitialEnemy=" + _initialAssaultEnemyCount +
                " ReservedEnemy=" + ReservedEnemyCount;
        }

        public virtual int GetRemainingTroopCount(BattleSideEnum side)
        {
            if (side == OpposingSide(_playerSide))
                return ReservedEnemyCount;
            return 0;
        }

        public void StartSpawner(BattleSideEnum side)
        {
            SetSpawnerEnabled(side, true);
        }

        public void StopSpawner(BattleSideEnum side)
        {
            SetSpawnerEnabled(side, false);
        }

        public bool IsSideSpawnEnabled(BattleSideEnum side)
        {
            return side == BattleSideEnum.Attacker
                ? _attackerSpawnerEnabled
                : side == BattleSideEnum.Defender && _defenderSpawnerEnabled;
        }

        public float GetReinforcementInterval(BattleSideEnum side = BattleSideEnum.None)
        {
            return 0f;
        }

        public virtual bool IsSideDepleted(BattleSideEnum side)
        {
            if (side == _playerSide)
                return CountActive(_spawnedPlayerAgents) == 0;
            if (side == OpposingSide(_playerSide))
                return CountActive(_spawnedEnemyAgents) == 0 && ReservedEnemyCount == 0;
            return true;
        }

        public IEnumerable<IAgentOriginBase> GetAllTroopsForSide(BattleSideEnum side)
        {
            IMissionTroopSupplier supplier = GetSupplier(side);
            return supplier?.GetAllTroops() ?? Array.Empty<IAgentOriginBase>();
        }

        public int GetNumberOfPlayerControllableTroops()
        {
            return GetSupplier(_playerSide)?.GetNumberOfPlayerControllableTroops() ?? 0;
        }

        public bool GetSpawnHorses(BattleSideEnum side)
        {
            return false;
        }

        protected virtual void TryMaterializeInitialAssault()
        {
            try
            {
                if (_playerSide != BattleSideEnum.Attacker)
                    throw new InvalidOperationException("day-hideout-player-side-must-be-attacker");

                if (!Mission.MissionBehaviors.Any(behavior => behavior is AgentHumanAILogic))
                    throw new InvalidOperationException("AgentHumanAILogic-missing-before-hideout-materialization");

                Team playerTeam = ResolveTeam(_playerSide);
                Team enemyTeam = ResolveTeam(OpposingSide(_playerSide));
                if (playerTeam == null || enemyTeam == null)
                    return;

                IMissionTroopSupplier playerSupplier = GetSupplier(_playerSide);
                IMissionTroopSupplier enemySupplier = GetSupplier(OpposingSide(_playerSide));
                if (playerSupplier == null || enemySupplier == null)
                    throw new InvalidOperationException("hideout-troop-supplier-missing");

                int playerCount = Math.Max(0, playerSupplier.NumTroopsNotSupplied);
                int enemyTotalCount = Math.Max(0, enemySupplier.NumTroopsNotSupplied);
                if (playerCount <= 0 ||
                    !CoopHideoutBossPhaseContract.IsValidFirstPhaseParticipantCount(
                        enemyTotalCount,
                        _firstPhaseEnemyTroopCount))
                {
                    throw new InvalidOperationException(
                        "hideout-initial-roster-contract-invalid Player=" + playerCount +
                        " EnemyTotal=" + enemyTotalCount +
                        " EnemyFirstPhase=" + _firstPhaseEnemyTroopCount);
                }

                List<DefenderPlacementSlot> defenderSlots = CollectHideoutDefenderSlots();
                if (defenderSlots.Count == 0)
                    throw new InvalidOperationException("hideout-defender-scene-frames-empty");

                List<IAgentOriginBase> playerOrigins = SupplyAll(playerSupplier);
                List<IAgentOriginBase> initialEnemyOrigins = enemySupplier
                    .SupplyTroops(_firstPhaseEnemyTroopCount)
                    .Where(origin => origin != null)
                    .ToList();
                if (playerOrigins.Count == 0 || initialEnemyOrigins.Count == 0)
                    throw new InvalidOperationException("hideout-initial-roster-empty");
                if (initialEnemyOrigins.Count != _firstPhaseEnemyTroopCount)
                {
                    throw new InvalidOperationException(
                        "hideout-first-phase-enemy-supply-incomplete Expected=" +
                        _firstPhaseEnemyTroopCount +
                        " Actual=" + initialEnemyOrigins.Count);
                }

                Mission.DeploymentPlan.MakeDefaultDeploymentPlans();
                SetTeamsAsEnemies(playerTeam, enemyTeam, false);

                SpawnPlayerGroup(playerOrigins);
                CoopHideoutStealthPatrolController stealthController =
                    Mission.GetMissionBehavior<CoopHideoutStealthPatrolController>();
                for (int index = 0; index < initialEnemyOrigins.Count; index++)
                {
                    DefenderPlacementSlot slot = defenderSlots[index % defenderSlots.Count];
                    Agent agent = SpawnEnemy(
                        initialEnemyOrigins[index],
                        slot.SpawnFrame,
                        isAlarmed: false,
                        wieldInitialWeapons: false);
                    if (agent != null)
                    {
                        _spawnedEnemyAgents.Add(agent);
                        stealthController?.RegisterDefender(agent, slot.PatrolPoints);
                    }
                }

                _initialAssaultEnemyCount = _spawnedEnemyAgents.Count;
                if (_spawnedPlayerAgents.Count == 0 || _initialAssaultEnemyCount == 0)
                    throw new InvalidOperationException("hideout-initial-agent-materialization-empty");

                HoldPlayerFormations();
                _initialAssaultMaterialized = true;
                ModLogger.Info(
                    "CoopExactCampaignHideoutMissionController: initial day-hideout assault materialized. " +
                    "PlayerAgents=" + _spawnedPlayerAgents.Count +
                    " EnemyAgents=" + _initialAssaultEnemyCount +
                    " ReservedBossGroup=" + ReservedEnemyCount +
                    " DefenderSlots=" + defenderSlots.Count +
                    " PatrolRoutes=" + defenderSlots.Count(slot => slot.PatrolPoints.Count > 1) +
                    " IdleActions=" + defenderSlots.Sum(slot =>
                        slot.PatrolPoints.Count(point => !string.IsNullOrWhiteSpace(point.LoopAction))) + ".");
            }
            catch (Exception ex)
            {
                _materializationFaulted = true;
                ModLogger.Error(
                    "CoopExactCampaignHideoutMissionController: initial materialization failed; " +
                    "the isolated controller will not fall back to open-field materialization.",
                    ex);
            }
        }

        protected void SpawnPlayerGroup(IReadOnlyList<IAgentOriginBase> origins)
        {
            for (int index = 0; index < origins.Count; index++)
            {
                Agent agent = Mission.SpawnTroop(
                    origins[index],
                    isPlayerSide: true,
                    hasFormation: true,
                    spawnWithHorse: false,
                    isReinforcement: false,
                    origins.Count,
                    index,
                    isAlarmed: false,
                    wieldInitialWeapons: false,
                    null,
                    null);
                if (agent != null)
                    _spawnedPlayerAgents.Add(agent);
            }
        }

        protected Agent SpawnEnemy(
            IAgentOriginBase origin,
            MatrixFrame frame,
            bool isAlarmed,
            bool wieldInitialWeapons)
        {
            Vec2 direction = frame.rotation.f.AsVec2;
            if (direction.LengthSquared < 0.0001f)
                direction = new Vec2(0f, 1f);
            direction.Normalize();

            Agent agent = Mission.SpawnTroop(
                origin,
                false,
                false,
                false,
                false,
                0,
                0,
                isAlarmed,
                wieldInitialWeapons,
                frame.origin,
                direction,
                HideoutBanditActionSetSuffix,
                null,
                FormationClass.NumberOfRegularFormations,
                false);
            if (agent == null)
                return null;

            AgentFlag flags = agent.GetAgentFlags();
            agent.SetAgentFlags((AgentFlag)(((uint)flags | 0x10000u) & 0xFFEFFFFFu));
            agent.SetAutomaticTargetSelection(isAlarmed);
            if (isAlarmed)
            {
                agent.SetWatchState(Agent.WatchState.Alarmed);
                TryWieldInitialSlots(agent);
            }
            return agent;
        }

        protected virtual void ActivateCombat()
        {
            Team playerTeam = ResolveTeam(_playerSide);
            Team enemyTeam = ResolveTeam(OpposingSide(_playerSide));
            SetTeamsAsEnemies(playerTeam, enemyTeam, true);

            CoopHideoutStealthPatrolController stealthController =
                Mission.GetMissionBehavior<CoopHideoutStealthPatrolController>();

            foreach (Agent agent in _spawnedPlayerAgents)
            {
                if (agent?.IsActive() != true)
                    continue;
                agent.SetWatchState(Agent.WatchState.Alarmed);
                TryWieldInitialSlots(agent);
            }

            if (stealthController != null)
            {
                stealthController.Activate();
            }
            else
            {
                foreach (Agent agent in _spawnedEnemyAgents)
                {
                    if (agent?.IsActive() != true)
                        continue;
                    agent.SetAutomaticTargetSelection(true);
                    agent.SetWatchState(Agent.WatchState.Alarmed);
                    TryWieldInitialSlots(agent);
                }
            }

            if (playerTeam == null)
                return;

            foreach (Formation formation in playerTeam.FormationsIncludingEmpty)
            {
                if (formation == null || formation.CountOfUnits <= 0)
                    continue;
                Agent playerAgent = _spawnedPlayerAgents.FirstOrDefault(agent =>
                    agent?.IsActive() == true && !agent.IsAIControlled);
                formation.SetMovementOrder(playerAgent != null
                    ? MovementOrder.MovementOrderFollow(playerAgent)
                    : MovementOrder.MovementOrderStop);
                formation.SetFiringOrder(FiringOrder.FiringOrderHoldYourFire);
            }

            _combatActivated = true;
            ModLogger.Info(
                "CoopExactCampaignHideoutMissionController: initial assault combat activated. " +
                "PlayerActive=" + CountActive(_spawnedPlayerAgents) +
                " EnemyActive=" + CountActive(_spawnedEnemyAgents) + ".");
        }

        protected void HoldPlayerFormations()
        {
            Team playerTeam = ResolveTeam(_playerSide);
            if (playerTeam == null)
                return;

            foreach (Formation formation in playerTeam.FormationsIncludingEmpty)
            {
                if (formation == null || formation.CountOfUnits <= 0)
                    continue;
                formation.SetMovementOrder(MovementOrder.MovementOrderStop);
                formation.SetFiringOrder(FiringOrder.FiringOrderHoldYourFire);
            }
        }

        protected List<DefenderPlacementSlot> CollectHideoutDefenderSlots(
            bool allowPartialManifestMetadata = false)
        {
            var slots = new List<DefenderPlacementSlot>();
            AppendGuardPatrolSlots(slots);
            AppendDynamicPatrolSlots(
                slots,
                allowPartialManifestMetadata,
                out string sceneManifestDiagnostics);

            int idleActionCount = slots.Sum(slot =>
                slot.PatrolPoints.Count(point => !string.IsNullOrWhiteSpace(point.LoopAction)));
            string source = idleActionCount > 0
                ? "engine-scene-routes+xscene-manifest"
                : "engine-scene-routes";
            if (slots.Count > 0)
            {
                ModLogger.Info(
                    "CoopExactCampaignHideoutMissionController: resolved defender scene slots. " +
                    "Count=" + slots.Count +
                    " Routes=" + slots.Count(slot => slot.PatrolPoints.Count > 1) +
                    " IdleActions=" + idleActionCount +
                    " Source=" + source +
                    " Manifest={" + sceneManifestDiagnostics + "}.");
                return slots;
            }

            var frames = new List<MatrixFrame>();
            if (Mission?.ActiveMissionObjects != null)
            {
                List<MissionObject> markers = Mission.ActiveMissionObjects
                    .Where(missionObject =>
                        missionObject != null &&
                        (string.Equals(missionObject.GetType().Name, "CommonAreaMarker", StringComparison.Ordinal) ||
                         string.Equals(missionObject.GetType().Name, "PatrolArea", StringComparison.Ordinal)))
                    .OrderBy(ReadAreaIndex)
                    .ToList();

                foreach (MissionObject marker in markers)
                {
                    int frameCountBeforeMarker = frames.Count;
                    AppendStandingPointFrames(marker, frames);
                    if (frames.Count == frameCountBeforeMarker)
                        AppendFrame(marker, frames);
                }
            }

            source = "managed-mission-objects";
            if (frames.Count == 0)
            {
                AppendSceneEntityFrames(
                    CoopHideoutBossPhaseContract.DefenderGuardPatrolEntityTag,
                    frames);
                AppendSceneEntityFrames(
                    CoopHideoutBossPhaseContract.DefenderDynamicPatrolAreaEntityTag,
                    frames);
                source = "engine-scene-tags";
            }

            foreach (MatrixFrame frame in frames)
            {
                slots.Add(new DefenderPlacementSlot
                {
                    SpawnFrame = frame,
                    PatrolPoints = new List<CoopHideoutPatrolPointDefinition>
                    {
                        CreateDefaultPatrolPoint(frame, 0)
                    }
                });
            }

            ModLogger.Info(
                "CoopExactCampaignHideoutMissionController: resolved defender scene slots. " +
                "Count=" + slots.Count +
                " Routes=0" +
                " Source=" + source +
                " Manifest={" + sceneManifestDiagnostics + "}.");
            return slots;
        }

        private void AppendGuardPatrolSlots(List<DefenderPlacementSlot> slots)
        {
            if (slots == null)
                return;

            try
            {
                IEnumerable<GameEntity> entities = Mission?.Scene?.FindEntitiesWithTag(
                    CoopHideoutBossPhaseContract.DefenderGuardPatrolEntityTag);
                foreach (GameEntity entity in (entities ?? Enumerable.Empty<GameEntity>())
                             .Where(candidate => candidate != null))
                {
                    List<MatrixFrame> route = entity.GetChildren()
                        .Where(child => child != null)
                        .OrderBy(child => ReadEntityOrder(child.Name))
                        .Select(child => child.GetGlobalFrame())
                        .ToList();
                    if (route.Count == 0)
                        route.Add(entity.GetGlobalFrame());
                    slots.Add(new DefenderPlacementSlot
                    {
                        SpawnFrame = route[0],
                        PatrolPoints = route
                            .Select((frame, index) => CreateDefaultPatrolPoint(frame, index))
                            .ToList()
                    });
                }
            }
            catch
            {
            }
        }

        private void AppendDynamicPatrolSlots(
            List<DefenderPlacementSlot> slots,
            bool allowPartialManifestMetadata,
            out string manifestDiagnostics)
        {
            manifestDiagnostics = "not-attempted";
            if (slots == null)
                return;

            bool hasManifest = CoopHideoutSceneManifestRuntime.TryResolve(
                Mission?.SceneName,
                out CoopHideoutSceneManifest manifest,
                out string loadDiagnostics);
            var consumedManifestAreaIndices = new HashSet<int>();
            int matchedAreaCount = 0;
            int appliedAreaCount = 0;
            int pointCountMismatchCount = 0;
            int unmatchedAreaCount = 0;
            int appliedIdleActionCount = 0;
            try
            {
                IEnumerable<GameEntity> entities = Mission?.Scene?.FindEntitiesWithTag(
                    CoopHideoutBossPhaseContract.DefenderDynamicPatrolAreaEntityTag);
                foreach (GameEntity entity in (entities ?? Enumerable.Empty<GameEntity>())
                             .Where(candidate => candidate != null))
                {
                    List<CoopHideoutPatrolPointDefinition> route = ResolveDynamicPatrolPoints(entity);
                    if (route.Count == 0)
                        route.Add(CreateDefaultPatrolPoint(entity.GetGlobalFrame(), 0));

                    if (hasManifest &&
                        TryTakeNearestManifestArea(
                            entity.GetGlobalFrame().origin,
                            manifest,
                            consumedManifestAreaIndices,
                            out CoopHideoutPatrolAreaManifest areaManifest))
                    {
                        matchedAreaCount++;
                        bool pointCountMatched =
                            route.Count == (areaManifest?.PatrolPoints?.Count ?? 0);
                        if (!pointCountMatched)
                            pointCountMismatchCount++;
                        if (TryApplyManifestPatrolPoints(
                                route,
                                areaManifest,
                                allowPartialManifestMetadata,
                                out int idleActionsApplied))
                        {
                            appliedAreaCount++;
                            appliedIdleActionCount += idleActionsApplied;
                        }
                    }
                    else if (hasManifest)
                    {
                        unmatchedAreaCount++;
                    }

                    slots.Add(new DefenderPlacementSlot
                    {
                        SpawnFrame = route[0].Frame,
                        PatrolPoints = route
                    });
                }
            }
            catch (Exception ex)
            {
                manifestDiagnostics =
                    "apply-failed:" + ex.GetType().Name + ":" + ex.Message +
                    " Load={" + loadDiagnostics + "}";
                return;
            }

            manifestDiagnostics =
                "Available=" + hasManifest +
                " Areas=" + (manifest?.PatrolAreas?.Count ?? 0) +
                " Matched=" + matchedAreaCount +
                " Applied=" + appliedAreaCount +
                " PointCountMismatch=" + pointCountMismatchCount +
                " Unmatched=" + unmatchedAreaCount +
                " IdleActionsApplied=" + appliedIdleActionCount +
                " Load={" + loadDiagnostics + "}";
        }

        private static bool TryTakeNearestManifestArea(
            Vec3 runtimePosition,
            CoopHideoutSceneManifest manifest,
            HashSet<int> consumedAreaIndices,
            out CoopHideoutPatrolAreaManifest areaManifest)
        {
            const float maximumDistanceSquared = 0.75f * 0.75f;
            areaManifest = null;
            if (manifest?.PatrolAreas == null || consumedAreaIndices == null)
                return false;

            int bestAreaIndex = -1;
            float bestDistanceSquared = maximumDistanceSquared;
            for (int areaIndex = 0; areaIndex < manifest.PatrolAreas.Count; areaIndex++)
            {
                if (consumedAreaIndices.Contains(areaIndex))
                    continue;

                CoopHideoutPatrolAreaManifest candidate = manifest.PatrolAreas[areaIndex];
                if (candidate == null)
                    continue;

                float deltaX = runtimePosition.x - candidate.PositionX;
                float deltaY = runtimePosition.y - candidate.PositionY;
                float deltaZ = runtimePosition.z - candidate.PositionZ;
                float distanceSquared = deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ;
                if (distanceSquared > bestDistanceSquared)
                    continue;

                bestDistanceSquared = distanceSquared;
                bestAreaIndex = areaIndex;
            }

            if (bestAreaIndex < 0)
                return false;

            consumedAreaIndices.Add(bestAreaIndex);
            areaManifest = manifest.PatrolAreas[bestAreaIndex];
            return true;
        }

        private static bool TryApplyManifestPatrolPoints(
            List<CoopHideoutPatrolPointDefinition> runtimePoints,
            CoopHideoutPatrolAreaManifest areaManifest,
            bool allowPartialPointCount,
            out int idleActionsApplied)
        {
            idleActionsApplied = 0;
            if (runtimePoints == null ||
                areaManifest?.PatrolPoints == null ||
                runtimePoints.Count == 0 ||
                areaManifest.PatrolPoints.Count == 0 ||
                (!allowPartialPointCount &&
                 runtimePoints.Count != areaManifest.PatrolPoints.Count))
            {
                return false;
            }

            int appliedPointCount = Math.Min(
                runtimePoints.Count,
                areaManifest.PatrolPoints.Count);
            for (int pointIndex = 0; pointIndex < appliedPointCount; pointIndex++)
            {
                CoopHideoutPatrolPointDefinition runtimePoint = runtimePoints[pointIndex];
                CoopHideoutPatrolPointManifest manifestPoint = areaManifest.PatrolPoints[pointIndex];
                if (runtimePoint == null || manifestPoint == null)
                    return false;

                runtimePoint.Index = manifestPoint.Index;
                runtimePoint.WaitDurationSeconds = manifestPoint.WaitDurationSeconds;
                runtimePoint.WaitDeviationSeconds = manifestPoint.WaitDeviationSeconds;
                runtimePoint.IsInfiniteWaitPoint = manifestPoint.IsInfiniteWaitPoint;
                runtimePoint.PatrollingSpeed = manifestPoint.PatrollingSpeed;
                runtimePoint.LoopAction = manifestPoint.LoopAction ?? string.Empty;
                runtimePoint.SpawnGroupTag = manifestPoint.SpawnGroupTag ?? string.Empty;
                runtimePoint.HasTorchTag = manifestPoint.HasTorchTag;
                if (!string.IsNullOrWhiteSpace(runtimePoint.LoopAction))
                    idleActionsApplied++;
            }

            runtimePoints.Sort((left, right) => left.Index.CompareTo(right.Index));
            return true;
        }

        private static List<CoopHideoutPatrolPointDefinition> ResolveDynamicPatrolPoints(GameEntity entity)
        {
            var points = new List<CoopHideoutPatrolPointDefinition>();
            if (entity == null)
                return points;

            try
            {
                List<GameEntity> pointContainers = entity.GetChildren()
                    .Where(child => child != null)
                    .OrderBy(child => ReadEntityOrder(child.Name))
                    .ToList();
                int fallbackIndex = 0;
                foreach (GameEntity pointContainer in pointContainers)
                {
                    GameEntity patrolPoint = FindPatrolPointEntity(pointContainer);
                    if (patrolPoint == null)
                        continue;

                    CoopHideoutPatrolPointDefinition definition = ReadPatrolPointDefinition(
                        patrolPoint,
                        fallbackIndex++);
                    if (definition != null)
                        points.Add(definition);
                }

                if (points.Count == 0)
                {
                    var descendants = new List<GameEntity>();
                    entity.GetChildrenRecursive(ref descendants);
                    fallbackIndex = 0;
                    foreach (GameEntity patrolPoint in descendants
                                 .Where(candidate =>
                                     candidate != null &&
                                     string.Equals(candidate.Name, "patrol_point", StringComparison.OrdinalIgnoreCase)))
                    {
                        CoopHideoutPatrolPointDefinition definition = ReadPatrolPointDefinition(
                            patrolPoint,
                            fallbackIndex++);
                        if (definition != null)
                            points.Add(definition);
                    }
                }
            }
            catch
            {
            }

            return points
                .OrderBy(point => point.Index)
                .ToList();
        }

        private static GameEntity FindPatrolPointEntity(GameEntity entity)
        {
            if (entity == null)
                return null;
            if (string.Equals(entity.Name, "patrol_point", StringComparison.OrdinalIgnoreCase))
                return entity;

            try
            {
                var descendants = new List<GameEntity>();
                entity.GetChildrenRecursive(ref descendants);
                return descendants.FirstOrDefault(candidate =>
                    candidate != null &&
                    string.Equals(candidate.Name, "patrol_point", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        private static CoopHideoutPatrolPointDefinition ReadPatrolPointDefinition(
            GameEntity patrolPoint,
            int fallbackIndex)
        {
            if (patrolPoint == null)
                return null;

            CoopHideoutPatrolPointDefinition definition = CreateDefaultPatrolPoint(
                patrolPoint.GetGlobalFrame(),
                fallbackIndex);
            ScriptComponentBehavior managedPatrolPoint = FindManagedPatrolPointComponent(patrolPoint);
            if (TryReadManagedPatrolPointField(managedPatrolPoint, "Index", out int index) ||
                TryReadPatrolPointInt(patrolPoint, "Index", out index))
                definition.Index = index;
            if (TryReadManagedPatrolPointField(managedPatrolPoint, "WaitDuration", out int waitDuration) ||
                TryReadPatrolPointInt(patrolPoint, "WaitDuration", out waitDuration))
                definition.WaitDurationSeconds = Math.Max(0, waitDuration);
            if (TryReadManagedPatrolPointField(managedPatrolPoint, "WaitDeviation", out int waitDeviation) ||
                TryReadPatrolPointInt(patrolPoint, "WaitDeviation", out waitDeviation))
                definition.WaitDeviationSeconds = Math.Max(0, waitDeviation);
            if (TryReadManagedPatrolPointField(managedPatrolPoint, "IsInfiniteWaitPoint", out bool isInfinite) ||
                TryReadPatrolPointBool(patrolPoint, "IsInfiniteWaitPoint", out isInfinite))
                definition.IsInfiniteWaitPoint = isInfinite;
            if (TryReadManagedPatrolPointField(managedPatrolPoint, "PatrollingSpeed", out float patrollingSpeed) ||
                TryReadPatrolPointFloat(patrolPoint, "PatrollingSpeed", out patrollingSpeed))
                definition.PatrollingSpeed = patrollingSpeed;
            if (TryReadManagedPatrolPointField(managedPatrolPoint, "LoopAction", out string loopAction) ||
                TryReadPatrolPointString(patrolPoint, "LoopAction", out loopAction))
                definition.LoopAction = loopAction ?? string.Empty;
            return definition;
        }

        private static ScriptComponentBehavior FindManagedPatrolPointComponent(GameEntity entity)
        {
            try
            {
                return entity?.GetScriptComponents()
                    .FirstOrDefault(component =>
                        string.Equals(
                            component?.GetType().FullName,
                            PatrolPointTypeFullName,
                            StringComparison.Ordinal));
            }
            catch
            {
                return null;
            }
        }

        private static bool TryReadManagedPatrolPointField<T>(
            ScriptComponentBehavior component,
            string fieldName,
            out T value)
        {
            value = default(T);
            if (component == null || string.IsNullOrWhiteSpace(fieldName))
                return false;

            try
            {
                FieldInfo field = component.GetType().GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object rawValue = field?.GetValue(component);
                if (rawValue is T typedValue)
                {
                    value = typedValue;
                    return true;
                }

                if (typeof(T) == typeof(string) && rawValue == null && field != null)
                {
                    value = (T)(object)string.Empty;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static CoopHideoutPatrolPointDefinition CreateDefaultPatrolPoint(
            MatrixFrame frame,
            int index)
        {
            return new CoopHideoutPatrolPointDefinition
            {
                Frame = frame,
                Index = Math.Max(0, index),
                WaitDurationSeconds = 1,
                WaitDeviationSeconds = 0,
                IsInfiniteWaitPoint = false,
                PatrollingSpeed = -1f,
                LoopAction = string.Empty,
                SpawnGroupTag = string.Empty,
                HasTorchTag = false
            };
        }

        private static bool TryReadPatrolPointInt(GameEntity entity, string fieldName, out int value)
        {
            value = 0;
            try
            {
                var holder = new ScriptComponentFieldHolder { i = int.MinValue };
                entity.GetNativeScriptComponentVariable(
                    PatrolPointScriptName,
                    fieldName,
                    ref holder,
                    RglScriptFieldType.RglSftInt);
                if (holder.i == int.MinValue)
                    return false;
                value = holder.i;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadPatrolPointBool(GameEntity entity, string fieldName, out bool value)
        {
            value = false;
            try
            {
                var holder = new ScriptComponentFieldHolder { b = int.MinValue };
                entity.GetNativeScriptComponentVariable(
                    PatrolPointScriptName,
                    fieldName,
                    ref holder,
                    RglScriptFieldType.RglSftBool);
                if (holder.b == int.MinValue)
                    return false;
                value = holder.b != 0;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadPatrolPointFloat(GameEntity entity, string fieldName, out float value)
        {
            value = -1f;
            try
            {
                var holder = new ScriptComponentFieldHolder { f = float.NaN };
                entity.GetNativeScriptComponentVariable(
                    PatrolPointScriptName,
                    fieldName,
                    ref holder,
                    RglScriptFieldType.RglSftFloat);
                if (float.IsNaN(holder.f))
                    return false;
                value = holder.f;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadPatrolPointString(GameEntity entity, string fieldName, out string value)
        {
            value = null;
            try
            {
                var holder = new ScriptComponentFieldHolder { s = UnsetScriptStringValue };
                entity.GetNativeScriptComponentVariable(
                    PatrolPointScriptName,
                    fieldName,
                    ref holder,
                    RglScriptFieldType.RglSftString);
                if (string.Equals(holder.s, UnsetScriptStringValue, StringComparison.Ordinal))
                    return false;
                value = holder.s ?? string.Empty;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int ReadEntityOrder(string entityName)
        {
            if (int.TryParse(entityName, out int order))
                return order;

            if (string.IsNullOrWhiteSpace(entityName))
                return int.MaxValue;

            int end = entityName.Length - 1;
            while (end >= 0 && char.IsDigit(entityName[end]))
                end--;
            if (end >= entityName.Length - 1)
                return int.MaxValue;

            return int.TryParse(entityName.Substring(end + 1), out order)
                ? order
                : int.MaxValue;
        }

        private void AppendSceneEntityFrames(
            string tag,
            List<MatrixFrame> frames)
        {
            if (string.IsNullOrWhiteSpace(tag) || frames == null)
                return;

            try
            {
                IEnumerable<GameEntity> entities = Mission?.Scene?.FindEntitiesWithTag(tag);
                if (entities == null)
                    return;

                foreach (GameEntity entity in entities.Where(candidate => candidate != null))
                    frames.Add(entity.GetGlobalFrame());
            }
            catch
            {
            }
        }

        private static void AppendStandingPointFrames(MissionObject marker, List<MatrixFrame> frames)
        {
            if (marker == null || frames == null)
                return;

            try
            {
                AppendStandingPointsFromMachine(marker, frames);

                MethodInfo method = marker.GetType().GetMethod(
                    "GetUsableMachinesInRange",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(string) },
                    modifiers: null);
                IEnumerable machines = method?.Invoke(marker, new object[] { null }) as IEnumerable;
                if (machines == null)
                    return;

                foreach (object machine in machines)
                    AppendStandingPointsFromMachine(machine, frames);
            }
            catch
            {
            }
        }

        private static void AppendStandingPointsFromMachine(
            object machine,
            List<MatrixFrame> frames)
        {
            PropertyInfo standingPointsProperty = machine?.GetType().GetProperty(
                "StandingPoints",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            IEnumerable standingPoints = standingPointsProperty?.GetValue(machine) as IEnumerable;
            if (standingPoints == null)
                return;

            foreach (object standingPoint in standingPoints)
            {
                if (standingPoint is ScriptComponentBehavior component && component.GameEntity != null)
                    frames.Add(component.GameEntity.GetGlobalFrame());
            }
        }

        private static void AppendFrame(MissionObject missionObject, List<MatrixFrame> frames)
        {
            try
            {
                if (missionObject?.GameEntity != null)
                    frames.Add(missionObject.GameEntity.GetGlobalFrame());
            }
            catch
            {
            }
        }

        protected List<MatrixFrame> BuildBossSpawnFrames(int count)
        {
            var frames = new List<MatrixFrame>();
            GameEntity anchor = null;
            try
            {
                anchor = Mission?.Scene?.FindEntityWithTag(CoopHideoutBossPhaseContract.BossFightEntityTag);
            }
            catch
            {
            }

            if (anchor == null)
                return frames;

            MatrixFrame anchorFrame = anchor.GetGlobalFrame();
            Vec2 forward = anchorFrame.rotation.f.AsVec2;
            if (forward.LengthSquared < 0.0001f)
                forward = new Vec2(0f, 1f);
            forward.Normalize();
            Vec2 side = new Vec2(forward.y, -forward.x);
            for (int index = 0; index < Math.Max(1, count); index++)
            {
                int step = index / 2 + 1;
                float sign = index % 2 == 0 ? 1f : -1f;
                MatrixFrame frame = anchorFrame;
                Vec2 offset = side * (sign * step * 1.5f);
                frame.origin += new Vec3(offset.x, offset.y, 0f);
                frames.Add(frame);
            }
            return frames;
        }

        private static int ReadAreaIndex(MissionObject missionObject)
        {
            try
            {
                PropertyInfo property = missionObject?.GetType().GetProperty(
                    "AreaIndex",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object value = property?.GetValue(missionObject);
                return value is int index ? index : int.MaxValue;
            }
            catch
            {
                return int.MaxValue;
            }
        }

        protected static bool IsBossOrigin(IAgentOriginBase origin)
        {
            RosterEntryState entry = ResolveEntry(origin);
            return ContainsBossToken(entry?.OriginalCharacterId) ||
                   ContainsBossToken(entry?.HeroTemplateId) ||
                   ContainsBossToken(entry?.CharacterId) ||
                   ContainsBossToken(entry?.TroopName) ||
                   ContainsBossToken(origin?.Troop?.StringId);
        }

        private static RosterEntryState ResolveEntry(IAgentOriginBase origin)
        {
            string entryId = (origin as ExactCampaignSnapshotAgentOrigin)?.EntryId;
            return string.IsNullOrWhiteSpace(entryId)
                ? null
                : BattleSnapshotRuntimeState.GetEntryState(entryId);
        }

        private static bool ContainsBossToken(string value)
        {
            return ContainsToken(value, "boss") ||
                   ContainsToken(value, "chief") ||
                   ContainsToken(value, "leader");
        }

        private static bool ContainsToken(string value, string token)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        protected static List<IAgentOriginBase> SupplyAll(IMissionTroopSupplier supplier)
        {
            int count = Math.Max(0, supplier?.NumTroopsNotSupplied ?? 0);
            return count == 0
                ? new List<IAgentOriginBase>()
                : supplier.SupplyTroops(count).Where(origin => origin != null).ToList();
        }

        protected IMissionTroopSupplier GetSupplier(BattleSideEnum side)
        {
            int index = (int)side;
            return index >= 0 && index < _suppliers.Length ? _suppliers[index] : null;
        }

        protected Team ResolveTeam(BattleSideEnum side)
        {
            return side == BattleSideEnum.Attacker
                ? Mission?.AttackerTeam
                : side == BattleSideEnum.Defender
                    ? Mission?.DefenderTeam
                    : null;
        }

        protected static BattleSideEnum OpposingSide(BattleSideEnum side)
        {
            return side == BattleSideEnum.Attacker
                ? BattleSideEnum.Defender
                : side == BattleSideEnum.Defender
                    ? BattleSideEnum.Attacker
                    : BattleSideEnum.None;
        }

        private void SetSpawnerEnabled(BattleSideEnum side, bool enabled)
        {
            if (side == BattleSideEnum.Attacker)
                _attackerSpawnerEnabled = enabled;
            else if (side == BattleSideEnum.Defender)
                _defenderSpawnerEnabled = enabled;
        }

        protected static int CountActive(IEnumerable<Agent> agents)
        {
            return (agents ?? Enumerable.Empty<Agent>()).Count(agent => agent?.IsActive() == true);
        }

        protected static void SetTeamsAsEnemies(Team left, Team right, bool enemies)
        {
            if (left == null || right == null)
                return;
            left.SetIsEnemyOf(right, enemies);
            right.SetIsEnemyOf(left, enemies);
        }

        internal static void TryWieldInitialSlots(Agent agent)
        {
            if (agent == null)
                return;

            string entryId = (agent.Origin as ExactCampaignSnapshotAgentOrigin)?.EntryId;
            if (string.IsNullOrWhiteSpace(entryId))
                CoopMissionSpawnLogic.TryResolveSelectableEntryId(agent, out entryId);

            RosterEntryState entry = !string.IsNullOrWhiteSpace(entryId)
                ? BattleSnapshotRuntimeState.GetEntryState(entryId)
                : null;
            if (entry == null)
            {
                agent.WieldInitialWeapons(Agent.WeaponWieldActionType.Instant);
                return;
            }

            ExactWeaponSlotMaterializationPolicy.TryWieldResolvedInitialSlots(
                agent,
                entry,
                Agent.WeaponWieldActionType.Instant,
                out _,
                out _);
        }

        internal static bool TryEquipNightTorch(Agent agent)
        {
            if (agent?.IsActive() != true || !GameNetwork.IsServerOrRecorder)
                return false;

            try
            {
                ItemObject torch = MBObjectManager.Instance?.GetObject<ItemObject>(
                    CoopHideoutAmbushContract.TorchMirrorItemId);
                if (torch == null)
                    return false;

                EquipmentIndex slot = EquipmentIndex.ExtraWeaponSlot;
                bool hasExistingTorch =
                    agent.Equipment != null &&
                    !agent.Equipment[slot].IsEmpty &&
                    string.Equals(
                        agent.Equipment[slot].Item?.StringId,
                        CoopHideoutAmbushContract.TorchMirrorItemId,
                        StringComparison.OrdinalIgnoreCase);
                if (hasExistingTorch)
                {
                    agent.TryToWieldWeaponInSlot(
                        slot,
                        Agent.WeaponWieldActionType.InstantAfterPickUp,
                        isWieldedOnSpawn: false);
                }
                else
                {
                    var missionWeapon = new MissionWeapon(torch, null, null);
                    agent.EquipWeaponToExtraSlotAndWield(ref missionWeapon);
                }

                bool torchPresent =
                    agent.Equipment != null &&
                    !agent.Equipment[slot].IsEmpty &&
                    string.Equals(
                        agent.Equipment[slot].Item?.StringId,
                        CoopHideoutAmbushContract.TorchMirrorItemId,
                        StringComparison.OrdinalIgnoreCase);
                return torchPresent &&
                       (agent.GetPrimaryWieldedItemIndex() == slot ||
                        agent.GetOffhandWieldedItemIndex() == slot);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopExactCampaignHideoutMissionController: failed to equip authored night torch. " +
                    "Agent=" + (agent?.Index.ToString() ?? "null") +
                    " Error=" + ex.GetType().Name + ":" + ex.Message + ".");
                return false;
            }
        }

        internal static void TryRemoveNightTorch(Agent agent)
        {
            if (agent?.IsActive() != true || agent.Equipment == null)
                return;

            try
            {
                EquipmentIndex slot = EquipmentIndex.ExtraWeaponSlot;
                if (!agent.Equipment[slot].IsEmpty &&
                    string.Equals(
                        agent.Equipment[slot].Item?.StringId,
                        CoopHideoutAmbushContract.TorchMirrorItemId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    agent.RemoveEquippedWeapon(slot);
                }
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Dedicated-safe hideout stealth and patrol runtime. It deliberately uses
    /// only MountAndBlade/Engine APIs because the dedicated mission has no live
    /// campaign or SandBox CampaignAgentComponent graph.
    /// </summary>
    internal sealed class CoopHideoutStealthPatrolController : MissionLogic
    {
        private const float VisionRange = 20f;
        private const float CrouchedVisionFactor = 0.65f;
        private const float CloseAwarenessRange = 4f;
        private const float RearAwarenessRange = 6f;
        private const float MinimumFrontDot = 0.15f;
        private const float CautiousThreshold = 0.35f;
        private const float AlarmThreshold = 1.25f;
        private const float AlarmPropagationRange = 12f;
        private const float CorpseAwarenessRange = 10f;
        private const float DetectionInterval = 0.15f;
        private const float PatrolArrivalDistanceSquared = 2.25f;
        private const float PatrolWaitSeconds = 1.25f;
        private const float DefaultPatrollingSpeed = 1.05f;
        private const float PatrolProgressTimeoutSeconds = 12f;
        private const float StalledAssaultAlarmSeconds = 45f;

        private sealed class DefenderState
        {
            internal Agent Agent { get; set; }

            internal List<CoopHideoutPatrolPointDefinition> Route { get; set; }

            internal int RouteIndex { get; set; }

            internal float Suspicion { get; set; }

            internal bool IsCautious { get; set; }

            internal bool IsAlarmed { get; set; }

            internal int ObservedAgentIndex { get; set; } = -1;

            internal float NextRouteAt { get; set; }

            internal float NextCommandAt { get; set; }

            internal Vec2 PreviousPosition { get; set; }

            internal float LastProgressAt { get; set; }

            internal bool IsWaitingAtPoint { get; set; }

            internal bool IsIdleActionPlaying { get; set; }

            internal bool ShouldCarryNightTorch { get; set; }

            internal bool ShouldWalkPatrol { get; set; }
        }

        private readonly Dictionary<int, DefenderState> _defenders =
            new Dictionary<int, DefenderState>();
        private bool _active;
        private bool _globalAlarm;
        private bool _useNightAmbushAlarmSemantics;
        private float _detectionAccumulator;
        private float _lastCombatProgressAt;

        internal bool HasGlobalAlarm =>
            _useNightAmbushAlarmSemantics
                ? CoopHideoutAmbushContract.ShouldKeepAlarmFailureCounterRunning(
                    _defenders.Values.Count(state =>
                        state?.Agent?.IsActive() == true && state.IsAlarmed))
                : _globalAlarm;

        internal IReadOnlyList<CoopHideoutAmbushAwarenessSnapshot> GetAwarenessSnapshots()
        {
            return _defenders.Values
                .Where(state => state?.Agent != null)
                .OrderBy(state => state.Agent.Index)
                .Select(state => new CoopHideoutAmbushAwarenessSnapshot
                {
                    GuardAgentIndex = state.Agent.Index,
                    ObservedAgentIndex = state.ObservedAgentIndex,
                    Suspicion01 = Math.Max(0f, Math.Min(1f, state.Suspicion / AlarmThreshold)),
                    IsAlarmed = state.IsAlarmed
                })
                .ToArray();
        }

        internal void RegisterDefender(
            Agent agent,
            IReadOnlyList<CoopHideoutPatrolPointDefinition> routePoints,
            bool carryNightTorch = false,
            bool forceWalkPatrol = false)
        {
            if (!GameNetwork.IsServer || agent == null)
                return;

            List<CoopHideoutPatrolPointDefinition> route =
                (routePoints ?? Array.Empty<CoopHideoutPatrolPointDefinition>())
                .Where(point => point != null)
                .OrderBy(point => point.Index)
                .ToList();
            if (route.Count == 0)
            {
                MatrixFrame frame = MatrixFrame.Identity;
                frame.origin = agent.Position;
                route.Add(new CoopHideoutPatrolPointDefinition
                {
                    Frame = frame,
                    Index = 0,
                    WaitDurationSeconds = 1,
                    PatrollingSpeed = -1f
                });
            }

            var state = new DefenderState
            {
                Agent = agent,
                Route = route,
                RouteIndex = 0,
                PreviousPosition = agent.Position.AsVec2,
                LastProgressAt = Mission?.CurrentTime ?? 0f,
                ShouldCarryNightTorch = carryNightTorch,
                ShouldWalkPatrol = forceWalkPatrol
            };
            _defenders[agent.Index] = state;
            PreparePatrollingAgent(state, issueMovement: true);
        }

        internal void Activate(bool useNightAmbushAlarmSemantics = false)
        {
            if (!GameNetwork.IsServer || _active)
                return;

            _useNightAmbushAlarmSemantics = useNightAmbushAlarmSemantics;
            _active = true;
            _lastCombatProgressAt = Mission?.CurrentTime ?? 0f;
            int sheathHandsAttempted = 0;
            int sheathHandsCleared = 0;
            int sheathFailures = 0;
            int nightTorchesRequested = 0;
            int nightTorchesWielded = 0;
            foreach (DefenderState state in _defenders.Values)
            {
                PreparePatrollingAgent(state, issueMovement: true);
                TrySheathePatrolWeapons(
                    state.Agent,
                    ref sheathHandsAttempted,
                    ref sheathHandsCleared,
                    ref sheathFailures);
                if (state.ShouldCarryNightTorch)
                {
                    nightTorchesRequested++;
                    if (CoopExactCampaignHideoutMissionController.TryEquipNightTorch(
                            state.Agent))
                    {
                        nightTorchesWielded++;
                    }
                }
            }

            ModLogger.Info(
                "CoopHideoutStealthPatrolController: isolated stealth patrol runtime activated. " +
                "Defenders=" + _defenders.Count +
                " Routes=" + _defenders.Values.Count(state => state.Route.Count > 1) +
                " SheathHandsAttempted=" + sheathHandsAttempted +
                " SheathHandsCleared=" + sheathHandsCleared +
                " SheathFailures=" + sheathFailures +
                " NightTorchesRequested=" + nightTorchesRequested +
                " NightTorchesWielded=" + nightTorchesWielded + ".");
        }

        internal void ActivateGlobalAlarm(string reason)
        {
            if (!GameNetwork.IsServer)
                return;

            AlarmAll(string.IsNullOrWhiteSpace(reason)
                ? "external-hideout-phase-transition"
                : reason);
        }

        public override void OnMissionTick(float dt)
        {
            if (!GameNetwork.IsServer || !_active || Mission == null || Mission.MissionEnded)
                return;

            CoopBattlePhase phase = CoopBattlePhaseRuntimeState.GetPhase();
            if (phase < CoopBattlePhase.BattleActive || phase >= CoopBattlePhase.BattleEnded)
                return;

            float now = Mission.CurrentTime;
            foreach (DefenderState state in _defenders.Values.ToArray())
            {
                if (state.Agent?.IsActive() != true || state.IsAlarmed)
                    continue;
                TickPatrol(state, now);
            }

            _detectionAccumulator += Math.Max(0f, dt);
            if (_detectionAccumulator >= DetectionInterval)
            {
                float detectionDt = _detectionAccumulator;
                _detectionAccumulator = 0f;
                foreach (DefenderState state in _defenders.Values.ToArray())
                {
                    if (state.Agent?.IsActive() == true && !state.IsAlarmed)
                        TickAwareness(state, detectionDt);
                }
            }

            if (!_useNightAmbushAlarmSemantics &&
                _globalAlarm &&
                now - _lastCombatProgressAt >= StalledAssaultAlarmSeconds &&
                _defenders.Values.Any(state => state.Agent?.IsActive() == true && !state.IsAlarmed))
            {
                AlarmAll("stalled-assault-failsafe");
            }
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
            if (GameNetwork.IsServer &&
                affectedAgent != null &&
                _defenders.TryGetValue(affectedAgent.Index, out DefenderState affectedState) &&
                affectedAgent.IsActive())
            {
                if (_useNightAmbushAlarmSemantics)
                    AlarmDefender(affectedState, "defender-hit");
                else
                    AlarmWithPropagation(affectedState, "defender-hit");
            }

            base.OnScoreHit(
                affectedAgent,
                affectorAgent,
                attackerWeapon,
                isBlocked,
                isSiegeEngineHit,
                blow,
                collisionData,
                damagedHp,
                hitDistance,
                shotDifficulty);
        }

        public override void OnAgentRemoved(
            Agent affectedAgent,
            Agent affectorAgent,
            AgentState agentState,
            KillingBlow blow)
        {
            if (GameNetwork.IsServer &&
                affectedAgent != null &&
                _defenders.TryGetValue(affectedAgent.Index, out DefenderState removedState))
            {
                _lastCombatProgressAt = Mission?.CurrentTime ?? _lastCombatProgressAt;
                if (_useNightAmbushAlarmSemantics)
                {
                    removedState.IsAlarmed = false;
                    removedState.IsCautious = false;
                    removedState.Suspicion = 0f;
                    removedState.ObservedAgentIndex = -1;
                    if (!HasGlobalAlarm)
                        _globalAlarm = false;
                }
                else
                {
                    AlarmNearby(
                        removedState.Agent.Position,
                        CorpseAwarenessRange,
                        "nearby-defender-removed");
                }
            }

            base.OnAgentRemoved(affectedAgent, affectorAgent, agentState, blow);
        }

        private void PreparePatrollingAgent(DefenderState state, bool issueMovement)
        {
            Agent agent = state?.Agent;
            if (agent?.IsActive() != true || state.IsAlarmed)
                return;

            agent.SetAutomaticTargetSelection(false);
            agent.SetFiringOrder(FiringOrder.RangedWeaponUsageOrderEnum.HoldYourFire);
            agent.SetWatchState(state.IsCautious
                ? Agent.WatchState.Cautious
                : Agent.WatchState.Patrolling);
            if (issueMovement)
                IssuePatrolTarget(state);
        }

        private static void TrySheathePatrolWeapons(
            Agent agent,
            ref int handsAttempted,
            ref int handsCleared,
            ref int failures)
        {
            if (agent?.IsActive() != true)
                return;

            try
            {
                if (agent.GetOffhandWieldedItemIndex() != EquipmentIndex.None)
                {
                    handsAttempted++;
                    agent.TryToSheathWeaponInHand(
                        Agent.HandIndex.OffHand,
                        Agent.WeaponWieldActionType.Instant);
                    if (agent.GetOffhandWieldedItemIndex() == EquipmentIndex.None)
                        handsCleared++;
                    else
                        failures++;
                }
            }
            catch
            {
                failures++;
            }

            try
            {
                if (agent.GetPrimaryWieldedItemIndex() != EquipmentIndex.None)
                {
                    handsAttempted++;
                    agent.TryToSheathWeaponInHand(
                        Agent.HandIndex.MainHand,
                        Agent.WeaponWieldActionType.Instant);
                    if (agent.GetPrimaryWieldedItemIndex() == EquipmentIndex.None)
                        handsCleared++;
                    else
                        failures++;
                }
            }
            catch
            {
                failures++;
            }
        }

        private void TickPatrol(DefenderState state, float now)
        {
            if (state.Route == null || state.Route.Count == 0)
                return;

            CoopHideoutPatrolPointDefinition targetPoint =
                state.Route[state.RouteIndex % state.Route.Count];
            MatrixFrame targetFrame = targetPoint.Frame;
            float distanceSquared = state.Agent.Position.AsVec2.DistanceSquared(targetFrame.origin.AsVec2);
            Vec2 currentPosition = state.Agent.Position.AsVec2;
            if (currentPosition.DistanceSquared(state.PreviousPosition) > 0.09f)
            {
                state.PreviousPosition = currentPosition;
                state.LastProgressAt = now;
            }

            if (distanceSquared <= PatrolArrivalDistanceSquared)
            {
                if (!state.IsWaitingAtPoint)
                    BeginPatrolPointWait(state, targetPoint, now);

                if (targetPoint.IsInfiniteWaitPoint)
                    return;

                if (now < state.NextRouteAt)
                    return;

                EndPatrolPointWait(state);
                state.RouteIndex = (state.RouteIndex + 1) % state.Route.Count;
                ApplyPatrollingSpeed(state.Agent, targetPoint.PatrollingSpeed);
                state.PreviousPosition = state.Agent.Position.AsVec2;
                state.LastProgressAt = now;
                state.NextCommandAt = now;
            }
            else if (distanceSquared > PatrolArrivalDistanceSquared &&
                     now - state.LastProgressAt >= PatrolProgressTimeoutSeconds)
            {
                EndPatrolPointWait(state);
                state.RouteIndex = (state.RouteIndex + 1) % state.Route.Count;
                state.LastProgressAt = now;
                state.NextCommandAt = now;
                ModLogger.Verbose(
                    "CoopHideoutStealthPatrolController: advanced a stalled patrol route. " +
                    "Agent=" + state.Agent.Index + ".");
            }

            if (now >= state.NextCommandAt)
            {
                IssuePatrolTarget(state);
                state.NextCommandAt = now + 1f;
            }
        }

        private void BeginPatrolPointWait(
            DefenderState state,
            CoopHideoutPatrolPointDefinition point,
            float now)
        {
            if (state == null || point == null)
                return;

            state.IsWaitingAtPoint = true;
            float waitSeconds = Math.Max(0f, point.WaitDurationSeconds);
            if (point.WaitDeviationSeconds > 0)
            {
                waitSeconds += MBRandom.RandomFloatRanged(
                    -point.WaitDeviationSeconds,
                    point.WaitDeviationSeconds);
                waitSeconds = Math.Max(0f, waitSeconds);
            }
            if (waitSeconds <= 0f && !point.IsInfiniteWaitPoint)
                waitSeconds = PatrolWaitSeconds;
            state.NextRouteAt = point.IsInfiniteWaitPoint
                ? float.MaxValue
                : now + waitSeconds;

            if (string.IsNullOrWhiteSpace(point.LoopAction))
                return;

            try
            {
                ActionIndexCache action = ActionIndexCache.Create(point.LoopAction);
                if (action.Index < 0)
                {
                    ModLogger.Verbose(
                        "CoopHideoutStealthPatrolController: scene patrol action was not found. " +
                        "Action=" + point.LoopAction + ".");
                    return;
                }

                state.IsIdleActionPlaying = state.Agent.SetActionChannel(
                    0,
                    in action,
                    ignorePriority: false,
                    additionalFlags: (AnimFlags)0,
                    blendWithNextActionFactor: 0f,
                    actionSpeed: 1f,
                    blendInPeriod: -0.2f,
                    blendOutPeriodToNoAnim: 0.4f,
                    startProgress: 0f,
                    useLinearSmoothing: false,
                    blendOutPeriod: -0.2f,
                    actionShift: 0,
                    forceFaceMorphRestart: true);
                if (!state.IsIdleActionPlaying)
                {
                    ModLogger.Verbose(
                        "CoopHideoutStealthPatrolController: scene patrol action was rejected by the agent. " +
                        "Action=" + point.LoopAction +
                        " Agent=" + state.Agent.Index + ".");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Verbose(
                    "CoopHideoutStealthPatrolController: failed to apply scene patrol action. " +
                    "Action=" + point.LoopAction +
                    " Error=" + ex.GetType().Name + ":" + ex.Message + ".");
            }
        }

        private static void EndPatrolPointWait(DefenderState state)
        {
            if (state == null)
                return;

            state.IsWaitingAtPoint = false;
            state.NextRouteAt = 0f;
            if (!state.IsIdleActionPlaying || state.Agent == null)
                return;

            try
            {
                ActionIndexCache noAction = ActionIndexCache.act_none;
                state.Agent.SetActionChannel(
                    0,
                    in noAction,
                    ignorePriority: true,
                    additionalFlags: (AnimFlags)0,
                    blendWithNextActionFactor: 0f,
                    actionSpeed: 1f,
                    blendInPeriod: -0.2f,
                    blendOutPeriodToNoAnim: 0.4f,
                    startProgress: 0f,
                    useLinearSmoothing: false,
                    blendOutPeriod: -0.2f,
                    actionShift: 0,
                    forceFaceMorphRestart: true);
            }
            catch
            {
            }
            finally
            {
                state.IsIdleActionPlaying = false;
            }
        }

        private static void ApplyPatrollingSpeed(Agent agent, float sceneSpeed)
        {
            if (agent == null)
                return;
            try
            {
                agent.SetMaximumSpeedLimit(
                    sceneSpeed < 0f ? DefaultPatrollingSpeed : sceneSpeed,
                    isMultiplier: false);
            }
            catch
            {
            }
        }

        private void IssuePatrolTarget(DefenderState state)
        {
            Agent agent = state?.Agent;
            if (agent?.IsActive() != true || state.Route == null || state.Route.Count == 0)
                return;

            try
            {
                MatrixFrame targetFrame = state.Route[state.RouteIndex % state.Route.Count].Frame;
                Vec2 direction = targetFrame.rotation.f.AsVec2;
                if (direction.LengthSquared < 0.0001f)
                    direction = new Vec2(0f, 1f);
                direction.Normalize();
                var targetPosition = new WorldPosition(
                    Mission.Scene,
                    UIntPtr.Zero,
                    targetFrame.origin,
                    hasValidZ: false);
                agent.SetScriptedPositionAndDirection(
                    ref targetPosition,
                    direction.RotationInRadians,
                    addHumanLikeDelay: true,
                    additionalFlags: state.ShouldWalkPatrol
                        ? Agent.AIScriptedFrameFlags.DoNotRun
                        : Agent.AIScriptedFrameFlags.None);
            }
            catch
            {
            }
        }

        private void TickAwareness(DefenderState state, float dt)
        {
            Agent guard = state.Agent;
            Agent visibleTarget = null;
            float strongestRate = 0f;
            foreach (Agent candidate in Mission.Agents)
            {
                if (!IsPotentialPlayerTarget(guard, candidate))
                    continue;

                float rate = ResolveDetectionRate(guard, candidate);
                if (rate <= strongestRate)
                    continue;
                strongestRate = rate;
                visibleTarget = candidate;
            }

            if (visibleTarget != null)
            {
                state.ObservedAgentIndex = visibleTarget.Index;
                state.Suspicion = Math.Min(AlarmThreshold, state.Suspicion + strongestRate * dt);
                if (!state.IsCautious && state.Suspicion >= CautiousThreshold)
                {
                    state.IsCautious = true;
                    guard.SetWatchState(Agent.WatchState.Cautious);
                    guard.SetLookAgent(visibleTarget);
                }

                if (state.Suspicion >= AlarmThreshold)
                {
                    if (_useNightAmbushAlarmSemantics)
                        AlarmDefender(state, "visual-detection");
                    else
                        AlarmWithPropagation(state, "visual-detection");
                }
                return;
            }

            state.ObservedAgentIndex = -1;
            state.Suspicion = Math.Max(0f, state.Suspicion - dt * 0.3f);
            if (state.IsCautious && state.Suspicion < CautiousThreshold * 0.4f)
            {
                state.IsCautious = false;
                guard.SetLookAgent(null);
                guard.SetWatchState(Agent.WatchState.Patrolling);
            }
        }

        private float ResolveDetectionRate(Agent guard, Agent target)
        {
            Vec3 guardEye = guard.Position + Vec3.Up * 1.55f;
            Vec3 targetEye = target.Position + Vec3.Up * 1.35f;
            Vec3 delta = targetEye - guardEye;
            float distanceSquared = delta.AsVec2.LengthSquared;
            if (distanceSquared <= 0.0001f || Math.Abs(delta.z) > 5f)
                return 0f;

            float distance = (float)Math.Sqrt(distanceSquared);
            float effectiveRange = VisionRange * (target.CrouchMode ? CrouchedVisionFactor : 1f);
            if (distance > effectiveRange)
                return 0f;

            Vec2 toTarget = delta.AsVec2;
            toTarget.Normalize();
            Vec2 look = guard.LookDirection.AsVec2;
            if (look.LengthSquared < 0.0001f)
                look = new Vec2(0f, 1f);
            look.Normalize();
            float frontDot = look.x * toTarget.x + look.y * toTarget.y;
            if (distance > CloseAwarenessRange &&
                frontDot < MinimumFrontDot &&
                distance > RearAwarenessRange)
            {
                return 0f;
            }

            if (!HasLineOfSight(guardEye, targetEye, distance))
                return 0f;

            if (distance <= CloseAwarenessRange)
                return AlarmThreshold / DetectionInterval;

            float proximity = Math.Max(0f, (effectiveRange - distance) / effectiveRange);
            float facing = Math.Max(0.2f, (frontDot + 1f) * 0.5f);
            return (0.45f + proximity * 1.35f) * facing;
        }

        private bool HasLineOfSight(Vec3 from, Vec3 to, float distance)
        {
            try
            {
                if (!Mission.Scene.RayCastForClosestEntityOrTerrain(
                        from,
                        to,
                        out float collisionDistance,
                        out Vec3 collisionPoint,
                        out WeakGameEntity collidedEntity,
                        0.05f,
                        (BodyFlags)67188481))
                {
                    return true;
                }

                return collisionDistance >= distance - 0.75f;
            }
            catch
            {
                return true;
            }
        }

        private static bool IsPotentialPlayerTarget(Agent guard, Agent candidate)
        {
            return guard != null &&
                   candidate?.IsActive() == true &&
                   candidate.IsHuman &&
                   candidate.Team != null &&
                   candidate.Team != Team.Invalid &&
                   guard.Team != null &&
                   candidate.Team.Side != guard.Team.Side;
        }

        private void AlarmWithPropagation(DefenderState source, string reason)
        {
            if (source == null)
                return;

            AlarmDefender(source, reason);
            AlarmNearby(source.Agent.Position, AlarmPropagationRange, "alarm-propagation");
        }

        private void AlarmNearby(Vec3 position, float range, string reason)
        {
            float rangeSquared = range * range;
            foreach (DefenderState state in _defenders.Values)
            {
                if (state.Agent?.IsActive() != true || state.IsAlarmed)
                    continue;
                if (state.Agent.Position.AsVec2.DistanceSquared(position.AsVec2) <= rangeSquared)
                    AlarmDefender(state, reason);
            }
        }

        private void AlarmAll(string reason)
        {
            foreach (DefenderState state in _defenders.Values)
            {
                if (state.Agent?.IsActive() == true && !state.IsAlarmed)
                    AlarmDefender(state, reason);
            }
            _lastCombatProgressAt = Mission?.CurrentTime ?? _lastCombatProgressAt;
            ModLogger.Info(
                "CoopHideoutStealthPatrolController: alarmed all remaining defenders. " +
                "Reason=" + reason + ".");
        }

        private void AlarmDefender(DefenderState state, string reason)
        {
            Agent agent = state?.Agent;
            if (agent?.IsActive() != true || state.IsAlarmed)
                return;

            state.IsAlarmed = true;
            state.IsCautious = false;
            state.Suspicion = AlarmThreshold;
            state.ObservedAgentIndex = -1;
            EndPatrolPointWait(state);
            CoopExactCampaignHideoutMissionController.TryRemoveNightTorch(agent);
            try
            {
                agent.SetMaximumSpeedLimit(-1f, isMultiplier: false);
            }
            catch
            {
            }
            agent.DisableScriptedMovement();
            agent.SetLookAgent(null);
            agent.SetWatchState(Agent.WatchState.Alarmed);
            agent.SetAlarmState(Agent.AIStateFlag.Alarmed);
            agent.SetAutomaticTargetSelection(true);
            agent.SetFiringOrder(FiringOrder.RangedWeaponUsageOrderEnum.FireAtWill);
            CoopExactCampaignHideoutMissionController.TryWieldInitialSlots(agent);
            agent.ResetEnemyCaches();
            agent.HumanAIComponent?.SyncBehaviorParamsIfNecessary();
            agent.ForceAiBehaviorSelection();

            if (!_globalAlarm)
            {
                _globalAlarm = true;
                _lastCombatProgressAt = Mission?.CurrentTime ?? 0f;
                ModLogger.Info(
                    "CoopHideoutStealthPatrolController: hideout alarm started. " +
                    "Agent=" + agent.Index +
                    " Reason=" + reason + ".");
            }
        }
    }
}
