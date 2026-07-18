using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.Patches;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.MissionBehaviors
{
    /// <summary>
    /// Snapshot-backed SallyOut/Relief mission controller that reuses the native
    /// ambush deployment contract without requiring custom-battle combatants.
    /// </summary>
    internal sealed class CoopExactCampaignSiegeAmbushMissionController : SallyOutMissionController
    {
        private int _defenderTotalTroopCount;
        private int _attackerTotalTroopCount;
        private readonly bool _isSallyOutAmbush;
        private bool _initialized;
        private bool _started;
        private bool _deploymentFinishedPending;
        private bool _battleLifecycleActivated;
        private readonly List<CastleGate> _retreatCorridorGates = new List<CastleGate>();
        private readonly Dictionary<Formation, Vec2> _manualWithdrawalTargets =
            new Dictionary<Formation, Vec2>();
        private readonly HashSet<Formation> _playerDirectedSiegeWeaponAttackFormations =
            new HashSet<Formation>();
        private float _retreatGateHoldUntilMissionTime = float.MinValue;
        private bool _retreatGateHoldActive;
        private float _nextPlayerDirectedSiegeWeaponAttackMaintenanceTime;

        private const float RetreatGateReleaseGraceSeconds = 5f;
        private const float WithdrawalTargetToleranceSquared = 9f;
        private const float PlayerDirectedSiegeWeaponAttackMaintenanceIntervalSeconds = 0.5f;

        private static readonly FieldInfo BesiegedDeploymentTimerField =
            typeof(SallyOutMissionController).GetField(
                "_besiegedDeploymentTimer",
                BindingFlags.Instance | BindingFlags.NonPublic);

        public CoopExactCampaignSiegeAmbushMissionController(
            int defenderTotalTroopCount,
            int attackerTotalTroopCount,
            bool isSallyOutAmbush)
            : base(isSallyOutAmbush)
        {
            _defenderTotalTroopCount = Math.Max(0, defenderTotalTroopCount);
            _attackerTotalTroopCount = Math.Max(0, attackerTotalTroopCount);
            _isSallyOutAmbush = isSallyOutAmbush;
        }

        public bool HasStarted => _started;

        public bool IsSallyOutAmbush => _isSallyOutAmbush;

        public bool IsBattleLifecycleActivated => _battleLifecycleActivated;

        public void UpdateTroopCounts(int defenderTotalTroopCount, int attackerTotalTroopCount)
        {
            if (_started)
                return;

            _defenderTotalTroopCount = Math.Max(0, defenderTotalTroopCount);
            _attackerTotalTroopCount = Math.Max(0, attackerTotalTroopCount);
        }

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
            _initialized = true;
        }

        public override void AfterStart()
        {
            if (_started)
                return;

            base.AfterStart();
            CacheRetreatCorridorGates();
            PauseBesiegedDeploymentTimer();
            _started = true;
        }

        public override void OnDeploymentFinished()
        {
            if (_battleLifecycleActivated)
                return;

            _deploymentFinishedPending = true;
            if (CoopBattlePhaseRuntimeState.GetPhase() >= CoopBattlePhase.BattleActive)
                ActivateBattleLifecycle("native-deployment-finished");
        }

        public override void OnMissionTick(float dt)
        {
            CoopBattlePhase phase = CoopBattlePhaseRuntimeState.GetPhase();
            if (!_battleLifecycleActivated)
            {
                if (phase < CoopBattlePhase.BattleActive ||
                    phase >= CoopBattlePhase.BattleEnded)
                {
                    return;
                }

                ActivateBattleLifecycle(
                    _deploymentFinishedPending
                        ? "battle-active-after-deployment"
                        : "battle-active-fallback");
            }

            base.OnMissionTick(dt);
            MaintainPlayerDirectedSiegeWeaponAttacks();
            MaintainDefenderManualWithdrawalTargets();
            MaintainDefenderRetreatCorridor();
        }

        public void EnsureBattleLifecycleActivated(string source)
        {
            if (_battleLifecycleActivated ||
                CoopBattlePhaseRuntimeState.GetPhase() < CoopBattlePhase.BattleActive)
            {
                return;
            }

            ActivateBattleLifecycle(source);
        }

        protected override void GetInitialTroopCounts(
            out int besiegedTotalTroopCount,
            out int besiegerTotalTroopCount)
        {
            besiegedTotalTroopCount = _defenderTotalTroopCount;
            besiegerTotalTroopCount = _attackerTotalTroopCount;
        }

        internal bool TryApplyPlayerDirectedSiegeWeaponAttack(
            Team team,
            IEnumerable<Formation> formations,
            out string diagnostics)
        {
            diagnostics = "invalid-context";
            if (!GameNetwork.IsServer ||
                Mission == null ||
                !_battleLifecycleActivated ||
                CoopBattlePhaseRuntimeState.GetPhase() != CoopBattlePhase.BattleActive ||
                team == null ||
                !ReferenceEquals(team, Mission.DefenderTeam))
            {
                return false;
            }

            if (!HasActiveEnemySiegeWeapon(team))
            {
                diagnostics = "no-active-enemy-siege-weapons";
                return false;
            }

            var acceptedFormations = new List<Formation>();
            int rejectedFormationCount = 0;
            foreach (Formation formation in formations ?? Array.Empty<Formation>())
            {
                if (formation == null ||
                    !ReferenceEquals(formation.Team, team) ||
                    formation.Index < 0 ||
                    formation.Index >= (int)FormationClass.NumberOfRegularFormations ||
                    formation.CountOfUnits <= 0 ||
                    formation.AI?.GetBehavior<BehaviorDestroySiegeWeapons>() == null)
                {
                    rejectedFormationCount++;
                    continue;
                }

                if (!acceptedFormations.Contains(formation))
                    acceptedFormations.Add(formation);
            }

            if (acceptedFormations.Count <= 0)
            {
                diagnostics =
                    "no-eligible-formations Rejected=" +
                    rejectedFormationCount;
                return false;
            }

            foreach (Formation formation in acceptedFormations)
            {
                formation.AI.ResetBehaviorWeights();
                TacticComponent.SetDefaultBehaviorWeights(formation);
                formation.AI.SetBehaviorWeight<BehaviorDestroySiegeWeapons>(1f);
                formation.AI.SetBehaviorWeight<BehaviorCharge>(0.1f);
                _playerDirectedSiegeWeaponAttackFormations.Add(formation);
            }

            CoopMissionNetworkBridge.UpdateVoluntaryFormationAiControl(
                Mission,
                team,
                acceptedFormations,
                isAiControlled: true,
                "player-directed-siege-weapon-attack");

            diagnostics =
                "applied Formations=" +
                string.Join(
                    ",",
                    acceptedFormations.ConvertAll(
                        formation => formation.Index.ToString()).ToArray()) +
                " Rejected=" +
                rejectedFormationCount;
            return true;
        }

        internal void CancelPlayerDirectedSiegeWeaponAttack(
            Team team,
            IEnumerable<Formation> formations,
            string source)
        {
            if (!GameNetwork.IsServer ||
                Mission == null ||
                team == null ||
                formations == null)
            {
                return;
            }

            var canceledFormations = new List<Formation>();
            foreach (Formation formation in formations)
            {
                if (formation == null ||
                    !ReferenceEquals(formation.Team, team) ||
                    !_playerDirectedSiegeWeaponAttackFormations.Remove(formation))
                {
                    continue;
                }

                if (formation.AI != null)
                {
                    formation.AI.ResetBehaviorWeights();
                    TacticComponent.SetDefaultBehaviorWeights(formation);
                }

                canceledFormations.Add(formation);
            }

            if (canceledFormations.Count <= 0)
                return;

            CoopMissionNetworkBridge.UpdateVoluntaryFormationAiControl(
                Mission,
                team,
                canceledFormations,
                isAiControlled: false,
                "cancel-player-directed-siege-weapon-attack:" +
                (source ?? "unknown"));

            ModLogger.Info(
                "CoopExactCampaignSiegeAmbushMissionController: canceled player-directed siege weapon attack. " +
                "Side=" + team.Side +
                " Formations=" +
                string.Join(
                    ",",
                    canceledFormations.ConvertAll(
                        formation => formation.Index.ToString()).ToArray()) +
                " Source=" + (source ?? "unknown"));
        }

        private void MaintainPlayerDirectedSiegeWeaponAttacks()
        {
            if (!GameNetwork.IsServer ||
                Mission == null ||
                _playerDirectedSiegeWeaponAttackFormations.Count <= 0)
            {
                return;
            }

            if (CoopBattlePhaseRuntimeState.GetPhase() != CoopBattlePhase.BattleActive)
            {
                _playerDirectedSiegeWeaponAttackFormations.Clear();
                return;
            }

            if (Mission.CurrentTime < _nextPlayerDirectedSiegeWeaponAttackMaintenanceTime)
                return;

            _nextPlayerDirectedSiegeWeaponAttackMaintenanceTime =
                Mission.CurrentTime +
                PlayerDirectedSiegeWeaponAttackMaintenanceIntervalSeconds;

            var invalidFormations = new List<Formation>();
            var nativeRetreatFormations = new List<Formation>();
            foreach (Formation formation in _playerDirectedSiegeWeaponAttackFormations)
            {
                if (formation == null ||
                    formation.CountOfUnits <= 0 ||
                    formation.AI == null ||
                    !formation.IsAIControlled)
                {
                    invalidFormations.Add(formation);
                    continue;
                }

                MovementOrder movementOrder =
                    formation.GetReadonlyMovementOrderReference();
                BehaviorComponent activeBehavior = formation.AI.ActiveBehavior;
                if (movementOrder.OrderEnum == MovementOrder.MovementOrderEnum.Retreat ||
                    activeBehavior is BehaviorRetreatToCastle ||
                    activeBehavior is BehaviorRetreatToKeep)
                {
                    nativeRetreatFormations.Add(formation);
                }
            }

            foreach (Formation formation in invalidFormations)
                _playerDirectedSiegeWeaponAttackFormations.Remove(formation);
            foreach (Formation formation in nativeRetreatFormations)
                _playerDirectedSiegeWeaponAttackFormations.Remove(formation);

            if (_playerDirectedSiegeWeaponAttackFormations.Count <= 0)
                return;

            Team defenderTeam = Mission.DefenderTeam;
            if (defenderTeam == null || HasActiveEnemySiegeWeapon(defenderTeam))
                return;

            CancelPlayerDirectedSiegeWeaponAttack(
                defenderTeam,
                new List<Formation>(_playerDirectedSiegeWeaponAttackFormations),
                "no-active-enemy-siege-weapons");
        }

        private bool HasActiveEnemySiegeWeapon(Team team)
        {
            if (Mission?.ActiveMissionObjects == null || team == null)
                return false;

            foreach (SiegeWeapon siegeWeapon in
                     Mission.ActiveMissionObjects.FindAllWithType<SiegeWeapon>())
            {
                if (siegeWeapon != null &&
                    siegeWeapon.Side != team.Side &&
                    siegeWeapon.IsDestructible &&
                    !siegeWeapon.IsDestroyed &&
                    !siegeWeapon.IsDisabled)
                {
                    return true;
                }
            }

            return false;
        }

        private void PauseBesiegedDeploymentTimer()
        {
            if (BesiegedDeploymentTimerField == null)
                throw new MissingFieldException(
                    typeof(SallyOutMissionController).FullName,
                    "_besiegedDeploymentTimer");

            BesiegedDeploymentTimerField.SetValue(this, null);
        }

        private void ActivateBattleLifecycle(string source)
        {
            if (_battleLifecycleActivated)
                return;

            if (BesiegedDeploymentTimerField == null)
                throw new MissingFieldException(
                    typeof(SallyOutMissionController).FullName,
                    "_besiegedDeploymentTimer");

            _battleLifecycleActivated = true;
            BesiegedDeploymentTimerField.SetValue(this, new BasicMissionTimer());
            base.OnDeploymentFinished();
            ExactSiegeAmbushDeploymentControllerPatch.ReleaseBattleAgentHold(
                Mission,
                "native SallyOut battle lifecycle activated");
            ModLogger.Info(
                "CoopExactCampaignSiegeAmbushMissionController: activated native SallyOut battle lifecycle. " +
                "DeploymentFinishedPending=" + _deploymentFinishedPending +
                " Phase=" + CoopBattlePhaseRuntimeState.GetPhase() +
                " Source=" + (source ?? "unknown"));
        }

        private void CacheRetreatCorridorGates()
        {
            _retreatCorridorGates.Clear();
            if (Mission?.MissionObjects == null)
                return;

            foreach (CastleGate gate in Mission.MissionObjects.FindAllWithType<CastleGate>())
            {
                if (gate != null)
                    _retreatCorridorGates.Add(gate);
            }
        }

        private void MaintainDefenderRetreatCorridor()
        {
            if (!GameNetwork.IsServer ||
                !_battleLifecycleActivated ||
                Mission == null ||
                CoopBattlePhaseRuntimeState.GetPhase() != CoopBattlePhase.BattleActive)
            {
                return;
            }

            float currentMissionTime = Mission.CurrentTime;
            bool withdrawalActive = IsDefenderWithdrawalActive();
            if (withdrawalActive)
            {
                float holdUntil = currentMissionTime + RetreatGateReleaseGraceSeconds;
                if (holdUntil > _retreatGateHoldUntilMissionTime)
                    _retreatGateHoldUntilMissionTime = holdUntil;
            }

            bool shouldHoldOpen =
                withdrawalActive ||
                (_retreatGateHoldActive &&
                 currentMissionTime <= _retreatGateHoldUntilMissionTime);
            if (!shouldHoldOpen)
            {
                _retreatGateHoldActive = false;
                return;
            }

            if (_retreatCorridorGates.Count == 0)
                CacheRetreatCorridorGates();

            bool enteringHold = !_retreatGateHoldActive;
            _retreatGateHoldActive = true;
            foreach (CastleGate gate in _retreatCorridorGates)
            {
                if (gate == null || gate.IsDestroyed)
                    continue;

                if (enteringHold)
                    gate.SetAutoOpenState(isEnabled: true);

                if (!gate.IsGateOpen)
                    gate.OpenDoor();
            }
        }

        private void MaintainDefenderManualWithdrawalTargets()
        {
            if (!GameNetwork.IsServer ||
                !_battleLifecycleActivated ||
                Mission == null ||
                CoopBattlePhaseRuntimeState.GetPhase() != CoopBattlePhase.BattleActive)
            {
                return;
            }

            Team defenderTeam = Mission.DefenderTeam;
            if (defenderTeam == null)
            {
                _manualWithdrawalTargets.Clear();
                return;
            }

            foreach (Formation formation in defenderTeam.FormationsIncludingEmpty)
            {
                if (formation == null || formation.CountOfUnits <= 0)
                {
                    if (formation != null)
                        _manualWithdrawalTargets.Remove(formation);
                    continue;
                }

                MovementOrder currentOrder = formation.GetReadonlyMovementOrderReference();
                if (currentOrder.OrderEnum == MovementOrder.MovementOrderEnum.Retreat)
                {
                    if (!TryCreateDefenderCastleWithdrawalPosition(
                            defenderTeam,
                            formation,
                            out WorldPosition withdrawalPosition))
                    {
                        continue;
                    }

                    formation.SetMovementOrder(MovementOrder.MovementOrderMove(withdrawalPosition));
                    _manualWithdrawalTargets[formation] = withdrawalPosition.AsVec2;
                    ModLogger.Info(
                        "CoopExactCampaignSiegeAmbushMissionController: converted manual retreat into castle withdrawal. " +
                        "Formation=" + formation.FormationIndex +
                        " Units=" + formation.CountOfUnits +
                        " Target=" + withdrawalPosition.AsVec2);
                    continue;
                }

                if (!_manualWithdrawalTargets.TryGetValue(formation, out Vec2 expectedTarget))
                    continue;

                if (TeamAISiegeComponent.IsFormationInsideCastle(
                        formation,
                        includeOnlyPositionedUnits: false,
                        thresholdPercentage: 0.9f))
                {
                    _manualWithdrawalTargets.Remove(formation);
                    ModLogger.Info(
                        "CoopExactCampaignSiegeAmbushMissionController: castle withdrawal completed. " +
                        "Formation=" + formation.FormationIndex +
                        " Units=" + formation.CountOfUnits);
                    continue;
                }

                if (currentOrder.OrderEnum != MovementOrder.MovementOrderEnum.Move)
                {
                    _manualWithdrawalTargets.Remove(formation);
                    ModLogger.Info(
                        "CoopExactCampaignSiegeAmbushMissionController: castle withdrawal canceled by a new order. " +
                        "Formation=" + formation.FormationIndex +
                        " NewOrder=" + currentOrder.OrderEnum);
                    continue;
                }

                try
                {
                    Vec2 currentTarget = currentOrder.GetPosition(formation);
                    if (!currentTarget.IsValid ||
                        currentTarget.DistanceSquared(expectedTarget) >
                        WithdrawalTargetToleranceSquared)
                    {
                        _manualWithdrawalTargets.Remove(formation);
                        ModLogger.Info(
                            "CoopExactCampaignSiegeAmbushMissionController: castle withdrawal canceled by a new move target. " +
                            "Formation=" + formation.FormationIndex);
                    }
                }
                catch
                {
                    _manualWithdrawalTargets.Remove(formation);
                }
            }
        }

        private bool TryCreateDefenderCastleWithdrawalPosition(
            Team defenderTeam,
            Formation formation,
            out WorldPosition withdrawalPosition)
        {
            withdrawalPosition = WorldPosition.Invalid;
            if (Mission?.DeploymentPlan == null ||
                defenderTeam == null ||
                formation == null)
            {
                return false;
            }

            var candidateClasses = new[]
            {
                formation.PhysicalClass,
                FormationClass.Cavalry,
                FormationClass.Infantry,
                FormationClass.Ranged,
                FormationClass.HorseArcher
            };
            var attemptedClasses = new HashSet<FormationClass>();
            foreach (FormationClass candidateClass in candidateClasses)
            {
                if (!attemptedClasses.Add(candidateClass))
                    continue;

                try
                {
                    IFormationDeploymentPlan formationPlan =
                        Mission.DeploymentPlan.GetFormationPlan(
                            defenderTeam,
                            candidateClass);
                    withdrawalPosition =
                        formationPlan?.CreateNewDeploymentWorldPosition(
                            WorldPosition.WorldPositionEnforcedCache.GroundVec3) ??
                        WorldPosition.Invalid;
                    if (withdrawalPosition.IsValid)
                        return true;
                }
                catch
                {
                    withdrawalPosition = WorldPosition.Invalid;
                }
            }

            return false;
        }

        private bool IsDefenderWithdrawalActive()
        {
            Team defenderTeam = Mission?.DefenderTeam;
            if (defenderTeam == null)
                return false;

            if (_manualWithdrawalTargets.Count > 0)
                return true;

            foreach (Agent agent in defenderTeam.ActiveAgents)
            {
                if (agent == null ||
                    !agent.IsActive() ||
                    !agent.IsHuman)
                {
                    continue;
                }

                if (agent.IsRetreating() ||
                    (agent.CommonAIComponent?.IsRetreating ?? false))
                {
                    return true;
                }
            }

            foreach (Formation formation in defenderTeam.FormationsIncludingEmpty)
            {
                if (formation == null || formation.CountOfUnits <= 0)
                    continue;

                if (formation.GetReadonlyMovementOrderReference().OrderEnum ==
                    MovementOrder.MovementOrderEnum.Retreat)
                {
                    return true;
                }

                BehaviorComponent activeBehavior = formation.AI?.ActiveBehavior;
                if (activeBehavior is BehaviorRetreatToCastle ||
                    activeBehavior is BehaviorRetreatToKeep)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
