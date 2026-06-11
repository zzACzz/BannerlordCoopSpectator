using System;
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
        private bool _initialized;
        private bool _started;

        public CoopExactCampaignSiegeAmbushMissionController(
            int defenderTotalTroopCount,
            int attackerTotalTroopCount)
            : base(isSallyOutAmbush: true)
        {
            _defenderTotalTroopCount = Math.Max(0, defenderTotalTroopCount);
            _attackerTotalTroopCount = Math.Max(0, attackerTotalTroopCount);
        }

        public bool HasStarted => _started;

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
            _started = true;
        }

        protected override void GetInitialTroopCounts(
            out int besiegedTotalTroopCount,
            out int besiegerTotalTroopCount)
        {
            besiegedTotalTroopCount = _defenderTotalTroopCount;
            besiegerTotalTroopCount = _attackerTotalTroopCount;
        }
    }
}
