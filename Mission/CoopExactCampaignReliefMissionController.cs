using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.MissionBehaviors
{
    /// <summary>
    /// Exact campaign relief controller. It preserves the native relief battle
    /// lifecycle without the siege-ambush weapon and castle-withdrawal rules.
    /// </summary>
    internal sealed class CoopExactCampaignReliefMissionController :
        SallyOutMissionController
    {
        private int _defenderTotalTroopCount;
        private int _attackerTotalTroopCount;
        private bool _initialized;
        private bool _started;
        private bool _deploymentFinishedPending;
        private bool _battleLifecycleActivated;

        private static readonly FieldInfo BesiegedDeploymentTimerField =
            typeof(SallyOutMissionController).GetField(
                "_besiegedDeploymentTimer",
                BindingFlags.Instance | BindingFlags.NonPublic);

        public CoopExactCampaignReliefMissionController(
            int defenderTotalTroopCount,
            int attackerTotalTroopCount)
            : base(false)
        {
            _defenderTotalTroopCount =
                Math.Max(0, defenderTotalTroopCount);
            _attackerTotalTroopCount =
                Math.Max(0, attackerTotalTroopCount);
        }

        public bool HasStarted => _started;

        public bool IsBattleLifecycleActivated =>
            _battleLifecycleActivated;

        public void UpdateTroopCounts(
            int defenderTotalTroopCount,
            int attackerTotalTroopCount)
        {
            if (_started)
                return;

            _defenderTotalTroopCount =
                Math.Max(0, defenderTotalTroopCount);
            _attackerTotalTroopCount =
                Math.Max(0, attackerTotalTroopCount);
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
            PauseBesiegedDeploymentTimer();
            _started = true;
        }

        public override void OnDeploymentFinished()
        {
            if (_battleLifecycleActivated)
                return;

            _deploymentFinishedPending = true;
            if (CoopBattlePhaseRuntimeState.GetPhase() >=
                CoopBattlePhase.BattleActive)
            {
                ActivateBattleLifecycle(
                    "native-deployment-finished");
            }
        }

        public override void OnMissionTick(float dt)
        {
            CoopBattlePhase phase =
                CoopBattlePhaseRuntimeState.GetPhase();
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
        }

        public void EnsureBattleLifecycleActivated(string source)
        {
            if (_battleLifecycleActivated ||
                CoopBattlePhaseRuntimeState.GetPhase() <
                CoopBattlePhase.BattleActive)
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

        private void PauseBesiegedDeploymentTimer()
        {
            if (BesiegedDeploymentTimerField == null)
            {
                throw new MissingFieldException(
                    typeof(SallyOutMissionController).FullName,
                    "_besiegedDeploymentTimer");
            }

            BesiegedDeploymentTimerField.SetValue(this, null);
        }

        private void ActivateBattleLifecycle(string source)
        {
            if (_battleLifecycleActivated)
                return;

            if (BesiegedDeploymentTimerField == null)
            {
                throw new MissingFieldException(
                    typeof(SallyOutMissionController).FullName,
                    "_besiegedDeploymentTimer");
            }

            _battleLifecycleActivated = true;
            BesiegedDeploymentTimerField.SetValue(
                this,
                new BasicMissionTimer());
            base.OnDeploymentFinished();
            ModLogger.Info(
                "CoopExactCampaignReliefMissionController: activated " +
                "native relief battle lifecycle. " +
                "DeploymentFinishedPending=" +
                _deploymentFinishedPending +
                " Phase=" +
                CoopBattlePhaseRuntimeState.GetPhase() +
                " Source=" + (source ?? "unknown"));
        }
    }
}
