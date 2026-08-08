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

namespace CoopSpectator.MissionBehaviors
{
    /// <summary>
    /// Snapshot-backed day-hideout materializer. It keeps the multiplayer shell and
    /// campaign snapshot contracts, but follows the native hideout placement shape
    /// instead of routing the scene through the open-field AgentBuildData path.
    /// </summary>
    internal sealed class CoopExactCampaignHideoutMissionController : MissionLogic, IMissionAgentSpawnLogic
    {
        private const string HideoutBanditActionSetSuffix = "_hideout_bandit";

        private readonly IMissionTroopSupplier[] _suppliers;
        private readonly BattleSideEnum _playerSide;
        private readonly int _firstPhaseEnemyTroopCount;
        private readonly List<Agent> _spawnedPlayerAgents = new List<Agent>();
        private readonly List<Agent> _spawnedEnemyAgents = new List<Agent>();
        private bool _initialized;
        private bool _started;
        private bool _initialAssaultMaterialized;
        private bool _combatActivated;
        private bool _reservedBossGroupSpawned;
        private bool _materializationFaulted;
        private bool _attackerSpawnerEnabled = true;
        private bool _defenderSpawnerEnabled = true;
        private int _initialAssaultEnemyCount;
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

        public int GetRemainingTroopCount(BattleSideEnum side)
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

        public bool IsSideDepleted(BattleSideEnum side)
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

        private void TryMaterializeInitialAssault()
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

                List<MatrixFrame> defenderFrames = CollectHideoutDefenderFrames();
                if (defenderFrames.Count == 0)
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
                for (int index = 0; index < initialEnemyOrigins.Count; index++)
                {
                    MatrixFrame frame = defenderFrames[index % defenderFrames.Count];
                    Agent agent = SpawnEnemy(
                        initialEnemyOrigins[index],
                        frame,
                        isAlarmed: false,
                        wieldInitialWeapons: false);
                    if (agent != null)
                        _spawnedEnemyAgents.Add(agent);
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
                    " DefenderFrames=" + defenderFrames.Count + ".");
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

        private void SpawnPlayerGroup(IReadOnlyList<IAgentOriginBase> origins)
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

        private Agent SpawnEnemy(
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
            agent.SetAutomaticTargetSelection(true);
            if (isAlarmed)
            {
                agent.SetWatchState(Agent.WatchState.Alarmed);
                TryWieldInitialSlots(agent);
            }
            return agent;
        }

        private void ActivateCombat()
        {
            Team playerTeam = ResolveTeam(_playerSide);
            Team enemyTeam = ResolveTeam(OpposingSide(_playerSide));
            SetTeamsAsEnemies(playerTeam, enemyTeam, true);

            foreach (Agent agent in _spawnedPlayerAgents.Concat(_spawnedEnemyAgents))
            {
                if (agent?.IsActive() != true)
                    continue;
                agent.SetWatchState(Agent.WatchState.Alarmed);
                TryWieldInitialSlots(agent);
            }

            if (playerTeam == null)
                return;

            foreach (Formation formation in playerTeam.FormationsIncludingEmpty)
            {
                if (formation == null || formation.CountOfUnits <= 0)
                    continue;
                formation.SetMovementOrder(MovementOrder.MovementOrderCharge);
                formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
            }

            _combatActivated = true;
            ModLogger.Info(
                "CoopExactCampaignHideoutMissionController: initial assault combat activated. " +
                "PlayerActive=" + CountActive(_spawnedPlayerAgents) +
                " EnemyActive=" + CountActive(_spawnedEnemyAgents) + ".");
        }

        private void HoldPlayerFormations()
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

        private List<MatrixFrame> CollectHideoutDefenderFrames()
        {
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

            string source = "managed-mission-objects";
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

            ModLogger.Info(
                "CoopExactCampaignHideoutMissionController: resolved defender scene frames. " +
                "Count=" + frames.Count +
                " Source=" + source + ".");
            return frames;
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

        private List<MatrixFrame> BuildBossSpawnFrames(int count)
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

        private static bool IsBossOrigin(IAgentOriginBase origin)
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

        private static List<IAgentOriginBase> SupplyAll(IMissionTroopSupplier supplier)
        {
            int count = Math.Max(0, supplier?.NumTroopsNotSupplied ?? 0);
            return count == 0
                ? new List<IAgentOriginBase>()
                : supplier.SupplyTroops(count).Where(origin => origin != null).ToList();
        }

        private IMissionTroopSupplier GetSupplier(BattleSideEnum side)
        {
            int index = (int)side;
            return index >= 0 && index < _suppliers.Length ? _suppliers[index] : null;
        }

        private Team ResolveTeam(BattleSideEnum side)
        {
            return side == BattleSideEnum.Attacker
                ? Mission?.AttackerTeam
                : side == BattleSideEnum.Defender
                    ? Mission?.DefenderTeam
                    : null;
        }

        private static BattleSideEnum OpposingSide(BattleSideEnum side)
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

        private static int CountActive(IEnumerable<Agent> agents)
        {
            return (agents ?? Enumerable.Empty<Agent>()).Count(agent => agent?.IsActive() == true);
        }

        private static void SetTeamsAsEnemies(Team left, Team right, bool enemies)
        {
            if (left == null || right == null)
                return;
            left.SetIsEnemyOf(right, enemies);
            right.SetIsEnemyOf(left, enemies);
        }

        private static void TryWieldInitialSlots(Agent agent)
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
    }
}
