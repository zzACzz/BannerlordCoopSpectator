using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure.Hideout;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Missions.MissionLogics;
using TaleWorlds.MountAndBlade.Missions.Objectives;

namespace CoopSpectator.UI
{
    public sealed class CoopHideoutMissionObjectiveController : MissionLogic
    {
        private readonly bool _isNight;
        private MissionObjectiveLogic _objectiveLogic;
        private CoopHideoutObjectiveStage _activeStage =
            CoopHideoutObjectiveStage.Hidden;
        private CoopHideoutClearMainCampObjective _clearObjective;

        public CoopHideoutMissionObjectiveController(bool isNight)
        {
            _isNight = isNight;
        }

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            _objectiveLogic = Mission.GetMissionBehavior<MissionObjectiveLogic>();
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (!GameNetwork.IsClient || _objectiveLogic == null)
                return;

            CoopHideoutObjectiveStage desiredStage = ResolveDesiredStage();
            if (desiredStage == _activeStage)
            {
                if (_clearObjective != null)
                    _clearObjective.SynchronizeTargets(CollectActiveEnemyAgents());
                return;
            }

            if (desiredStage == CoopHideoutObjectiveStage.ClearMainCamp)
            {
                List<Agent> activeEnemies = CollectActiveEnemyAgents();
                if (activeEnemies.Count == 0)
                    return;
                ChangeObjective(desiredStage, activeEnemies);
                return;
            }

            ChangeObjective(desiredStage, null);
        }

        public override void OnAgentBuild(Agent affectedAgent, Banner banner)
        {
            base.OnAgentBuild(affectedAgent, banner);
            if (_activeStage == CoopHideoutObjectiveStage.ClearMainCamp &&
                IsEnemyAgent(affectedAgent))
            {
                _clearObjective?.AddTarget(affectedAgent);
            }
        }

        public override void OnAgentRemoved(
            Agent affectedAgent,
            Agent affectorAgent,
            AgentState agentState,
            KillingBlow blow)
        {
            base.OnAgentRemoved(
                affectedAgent,
                affectorAgent,
                agentState,
                blow);
            _clearObjective?.RemoveTarget(affectedAgent);
        }

        public override void OnMissionStateFinalized()
        {
            if (_objectiveLogic?.CurrentObjective != null)
                _objectiveLogic.CompleteCurrentObjective();
            _clearObjective = null;
            _objectiveLogic = null;
            _activeStage = CoopHideoutObjectiveStage.Hidden;
            base.OnMissionStateFinalized();
        }

        private CoopHideoutObjectiveStage ResolveDesiredStage()
        {
            CoopHideoutBossPhaseSession bossState =
                CoopHideoutBossPhaseController.CurrentClientState;
            CoopHideoutAmbushState ambushState =
                CoopHideoutAmbushNetworkController.CurrentClientState;
            return CoopHideoutBossPhaseContract.ResolveHideoutObjectiveStage(
                _isNight,
                ambushState?.Phase ??
                    CoopHideoutAmbushPhase.WaitingForMaterialization,
                bossState != null,
                bossState?.Phase ?? CoopHideoutBossPhase.InitialAssault);
        }

        private void ChangeObjective(
            CoopHideoutObjectiveStage stage,
            List<Agent> activeEnemies)
        {
            if (_objectiveLogic.CurrentObjective != null)
                _objectiveLogic.CompleteCurrentObjective();

            _clearObjective = null;
            _activeStage = stage;
            MissionObjective objective = null;
            switch (stage)
            {
                case CoopHideoutObjectiveStage.LocateMainCamp:
                    objective = new CoopHideoutTextObjective(
                        Mission,
                        "hideout_mission_locate_the_main_camp_objective",
                        new TextObject("{=2g03vuC7}Locate the Main Camp"),
                        new TextObject("{=wmvJ0bcH}Sneak your way through the sentries."));
                    break;
                case CoopHideoutObjectiveStage.ClearMainCamp:
                    _clearObjective = new CoopHideoutClearMainCampObjective(
                        Mission,
                        activeEnemies ?? new List<Agent>());
                    objective = _clearObjective;
                    break;
                case CoopHideoutObjectiveStage.WinDuel:
                    objective = new CoopHideoutTextObjective(
                        Mission,
                        "hideout_mission_defeat_hideout_boss_objective",
                        new TextObject("{=QEynMlwL}Win the Duel"),
                        new TextObject("{=t13oVKkw}Win the duel against the bandit boss."));
                    break;
                case CoopHideoutObjectiveStage.WinFight:
                    objective = new CoopHideoutTextObjective(
                        Mission,
                        "hideout_mission_defeat_hideout_boss_objective",
                        new TextObject("{=0sPTRh6L}Win the Fight"),
                        new TextObject("{=7vqW1CsE}Eliminate the bandit boss and his troops."));
                    break;
            }

            if (objective != null)
                _objectiveLogic.StartObjective(objective);
        }

        private List<Agent> CollectActiveEnemyAgents()
        {
            return Mission?.Agents?
                       .Where(agent => agent?.IsActive() == true && IsEnemyAgent(agent))
                       .ToList() ??
                   new List<Agent>();
        }

        private bool IsEnemyAgent(Agent agent)
        {
            Team agentTeam = agent?.Team;
            if (agentTeam == null || agentTeam == Team.Invalid)
                return false;

            Team enemyTeam = Mission?.PlayerEnemyTeam;
            if (enemyTeam != null && enemyTeam != Team.Invalid)
                return ReferenceEquals(agentTeam, enemyTeam);

            Team playerTeam = Mission?.PlayerTeam ?? Agent.Main?.Team;
            return playerTeam != null &&
                   playerTeam != Team.Invalid &&
                   agentTeam.IsEnemyOf(playerTeam);
        }
    }

    internal class CoopHideoutTextObjective : MissionObjective
    {
        private readonly string _uniqueId;
        private readonly TextObject _name;
        private readonly TextObject _description;

        internal CoopHideoutTextObjective(
            Mission mission,
            string uniqueId,
            TextObject name,
            TextObject description)
            : base(mission)
        {
            _uniqueId = uniqueId;
            _name = name;
            _description = description;
        }

        public override string UniqueId => _uniqueId;

        public override TextObject Name => _name;

        public override TextObject Description => _description;
    }

    internal sealed class CoopHideoutClearMainCampObjective
        : CoopHideoutTextObjective
    {
        private readonly HashSet<int> _activeTargetIndices = new HashSet<int>();
        private int _requiredProgressAmount;

        internal CoopHideoutClearMainCampObjective(
            Mission mission,
            IEnumerable<Agent> agents)
            : base(
                mission,
                "hideout_mission_clear_the_main_camp_objective",
                new TextObject("{=OLWkIYxa}Clear the Main Camp"),
                new TextObject("{=lGZLiIey}Clear the main camp with your troops."))
        {
            SynchronizeTargets(agents);
        }

        internal void SynchronizeTargets(IEnumerable<Agent> agents)
        {
            if (agents == null)
                return;

            HashSet<int> activeIndices = new HashSet<int>();
            foreach (Agent agent in agents)
            {
                if (agent?.IsActive() != true)
                    continue;
                activeIndices.Add(agent.Index);
                AddTarget(agent);
            }

            foreach (int agentIndex in _activeTargetIndices.ToArray())
            {
                if (!activeIndices.Contains(agentIndex))
                    _activeTargetIndices.Remove(agentIndex);
            }
        }

        internal void AddTarget(Agent agent)
        {
            if (agent == null || !_activeTargetIndices.Add(agent.Index))
                return;
            _requiredProgressAmount++;
        }

        internal void RemoveTarget(Agent agent)
        {
            if (agent != null)
                _activeTargetIndices.Remove(agent.Index);
        }

        public override MissionObjectiveProgressInfo GetCurrentProgress()
        {
            return new MissionObjectiveProgressInfo
            {
                CurrentProgressAmount = Math.Max(
                    0,
                    _requiredProgressAmount - _activeTargetIndices.Count),
                RequiredProgressAmount = _requiredProgressAmount
            };
        }
    }
}
