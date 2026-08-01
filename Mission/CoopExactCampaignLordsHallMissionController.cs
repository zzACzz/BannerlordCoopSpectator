using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Objects;

namespace CoopSpectator.MissionBehaviors
{
    /// <summary>
    /// Snapshot-backed lords-hall mission controller that mirrors the native
    /// indoor siege fight contract without activating the standard battle
    /// spawn logic.
    /// </summary>
    internal sealed class CoopExactCampaignLordsHallMissionController : MissionLogic, IMissionAgentSpawnLogic
    {
        private sealed class MissionSide
        {
            private readonly BattleSideEnum _side;
            private readonly IMissionTroopSupplier _troopSupplier;
            private readonly bool _isPlayerSide;
            private readonly List<Agent> _spawnedAgents = new List<Agent>();
            private bool _troopSpawningActive = true;
            private bool _combatActivated;
            private int _numberOfSpawnedTroops;

            public MissionSide(BattleSideEnum side, IMissionTroopSupplier troopSupplier, bool isPlayerSide)
            {
                _side = side;
                _troopSupplier = troopSupplier;
                _isPlayerSide = isPlayerSide;
            }

            public bool TroopSpawningActive => _troopSpawningActive;

            public int NumberOfActiveTroops => _numberOfSpawnedTroops - (_troopSupplier?.NumRemovedTroops ?? 0);

            public int NumberOfSpawnedTroops => _numberOfSpawnedTroops;

            public int NumberOfRemainingTroops => Math.Max(0, _troopSupplier?.NumTroopsNotSupplied ?? 0);

            public int TotalTroopCount => _troopSupplier?.GetAllTroops()?.Count() ?? 0;

            public int PlayerControllableTroopCount => _troopSupplier?.GetNumberOfPlayerControllableTroops() ?? 0;

            public IEnumerable<IAgentOriginBase> GetAllTroops()
            {
                return _troopSupplier?.GetAllTroops() ?? Array.Empty<IAgentOriginBase>();
            }

            public void SetSpawnTroops(bool spawnTroops)
            {
                _troopSpawningActive = spawnTroops;
            }

            public void SpawnDefenders(
                Dictionary<int, Dictionary<int, AreaData>> areaMarkerDictionary,
                int spawnCount)
            {
                if (_troopSupplier == null || areaMarkerDictionary == null || areaMarkerDictionary.Count <= 0)
                    return;

                List<IAgentOriginBase> troops = _troopSupplier
                    .SupplyTroops(spawnCount)
                    .OrderByDescending(origin =>
                    {
                        FormationClass troopClass = Mission.Current.GetAgentTroopClass(_side, origin.Troop);
                        return troopClass == FormationClass.Ranged || troopClass == FormationClass.HorseArcher ? 1 : 0;
                    })
                    .ToList();

                for (int index = 0; index < troops.Count; index++)
                {
                    IAgentOriginBase origin = troops[index];
                    bool isRanged = Mission.Current.GetAgentTroopClass(_side, origin.Troop).IsRanged();
                    List<KeyValuePair<int, AreaData>> subAreas =
                        areaMarkerDictionary.ElementAt(index % areaMarkerDictionary.Count).Value.ToList();
                    var weightedAreas = new List<(KeyValuePair<int, AreaData> Area, float Weight)>();

                    foreach (KeyValuePair<int, AreaData> subArea in subAreas)
                    {
                        int weight =
                            1000 * subArea.Value.GetAvailableMachines(isRanged).Count() +
                            subArea.Value.GetAvailableMachines(!isRanged).Count();
                        weightedAreas.Add((subArea, weight));
                    }

                    KeyValuePair<int, AreaData> chosenArea = MBRandom.ChooseWeighted(weightedAreas);
                    AreaEntityData spawnPoint =
                        chosenArea.Value.GetAvailableMachines(isRanged).GetRandomElementInefficiently() ??
                        chosenArea.Value.GetAvailableMachines(!isRanged).GetRandomElementInefficiently();
                    if (spawnPoint == null)
                        continue;

                    MatrixFrame spawnFrame = spawnPoint.Entity.GetGlobalFrame();
                    Agent agent = Mission.Current.SpawnTroop(
                        origin,
                        isPlayerSide: false,
                        hasFormation: false,
                        spawnWithHorse: false,
                        isReinforcement: false,
                        0,
                        0,
                        isAlarmed: false,
                        wieldInitialWeapons: false,
                        spawnFrame.origin,
                        spawnFrame.rotation.f.AsVec2.Normalized());
                    if (agent == null)
                        continue;

                    _numberOfSpawnedTroops++;
                    _spawnedAgents.Add(agent);
                    AgentFlag agentFlags = agent.GetAgentFlags();
                    agent.SetAgentFlags((AgentFlag)((uint)agentFlags & 0xFFEFFFFFu));
                    agent.WieldInitialWeapons(Agent.WeaponWieldActionType.Instant);
                    agent.SetWatchState(Agent.WatchState.Alarmed);
                    agent.SetBehaviorValueSet(HumanAIComponent.BehaviorValueSet.DefensiveArrangementMove);
                    spawnPoint.AssignAgent(agent);
                }
            }

            public void SpawnAttackers(int spawnCount, bool isReinforcement)
            {
                if (!_troopSpawningActive || _troopSupplier == null)
                    return;

                List<IAgentOriginBase> troops = _troopSupplier.SupplyTroops(spawnCount).ToList();
                for (int index = 0; index < troops.Count; index++)
                {
                    if (_side != BattleSideEnum.Attacker)
                        continue;

                    Agent agent = Mission.Current.SpawnTroop(
                        troops[index],
                        _isPlayerSide,
                        hasFormation: true,
                        spawnWithHorse: false,
                        isReinforcement,
                        spawnCount,
                        index,
                        isAlarmed: _combatActivated,
                        wieldInitialWeapons: _combatActivated,
                        null,
                        null);
                    if (agent == null)
                        continue;

                    _numberOfSpawnedTroops++;
                    _spawnedAgents.Add(agent);
                }
            }

            public int ActivateCombat()
            {
                if (_combatActivated)
                    return 0;

                _combatActivated = true;
                int activatedCount = 0;
                foreach (Agent agent in _spawnedAgents)
                {
                    if (agent == null || !agent.IsActive())
                        continue;

                    if (_side != BattleSideEnum.Defender)
                    {
                        agent.WieldInitialWeapons(Agent.WeaponWieldActionType.Instant);
                        agent.SetWatchState(Agent.WatchState.Alarmed);
                    }
                    activatedCount++;
                }

                return activatedCount;
            }
        }

        private sealed class AreaData
        {
            private readonly List<FightAreaMarker> _areaList = new List<FightAreaMarker>();
            private readonly List<AreaEntityData> _archerUsablePoints = new List<AreaEntityData>();
            private readonly List<AreaEntityData> _infantryUsablePoints = new List<AreaEntityData>();

            public IEnumerable<FightAreaMarker> AreaList => _areaList;

            public IEnumerable<AreaEntityData> ArcherUsablePoints => _archerUsablePoints;

            public IEnumerable<AreaEntityData> InfantryUsablePoints => _infantryUsablePoints;

            public AreaData(IEnumerable<FightAreaMarker> areaList)
            {
                foreach (FightAreaMarker area in areaList ?? Enumerable.Empty<FightAreaMarker>())
                    AddAreaMarker(area);
            }

            public IEnumerable<AreaEntityData> GetAvailableMachines(bool isArcher)
            {
                List<AreaEntityData> points = isArcher ? _archerUsablePoints : _infantryUsablePoints;
                foreach (AreaEntityData point in points)
                {
                    if (!point.InUse)
                        yield return point;
                }
            }

            public void AddAreaMarker(FightAreaMarker marker)
            {
                if (marker == null)
                    return;

                _areaList.Add(marker);
                foreach (GameEntity entity in marker.GetGameEntitiesWithTagInRange("defender_archer"))
                {
                    PathFaceRecord record = PathFaceRecord.NullFaceRecord;
                    Mission.Current.Scene.GetNavMeshFaceIndex(ref record, entity.GetGlobalFrame().origin, checkIfDisabled: true);
                    if (record.FaceIndex != -1 && _archerUsablePoints.All(point => point.Entity != entity))
                        _archerUsablePoints.Add(new AreaEntityData(entity));
                }

                foreach (GameEntity entity in marker.GetGameEntitiesWithTagInRange("defender_infantry"))
                {
                    if (_infantryUsablePoints.All(point => point.Entity != entity))
                        _infantryUsablePoints.Add(new AreaEntityData(entity));
                }
            }

            public AreaEntityData FindAgentMachine(Agent agent)
            {
                return _infantryUsablePoints.FirstOrDefault(point => point.UserAgent == agent) ??
                       _archerUsablePoints.FirstOrDefault(point => point.UserAgent == agent);
            }
        }

        private sealed class AreaEntityData
        {
            public readonly GameEntity Entity;

            public AreaEntityData(GameEntity entity)
            {
                Entity = entity;
            }

            public Agent UserAgent { get; private set; }

            public bool InUse => UserAgent != null;

            public void AssignAgent(Agent agent)
            {
                UserAgent = agent;
                if (UserAgent == null)
                    return;

                MatrixFrame spawnFrame = Entity.GetGlobalFrame();
                UserAgent.SetBehaviorValueSet(HumanAIComponent.BehaviorValueSet.DefaultMove);
                UserAgent.SetFormationFrameEnabled(
                    new WorldPosition(agent.Mission.Scene, spawnFrame.origin),
                    spawnFrame.rotation.f.AsVec2.Normalized(),
                    Vec2.Zero,
                    0f);
            }

            public void StopUse()
            {
                if (UserAgent?.IsActive() == true)
                    UserAgent.SetFormationFrameDisabled();

                UserAgent = null;
            }
        }

        private readonly float _areaLostRatio;
        private readonly float _attackerDefenderTroopCountRatio;
        private readonly int _attackerSideTroopCountMax;
        private readonly int _defenderSideTroopCountMax;
        private readonly MissionSide[] _missionSides;
        private readonly BattleSideEnum _playerSide;

        private Team[] _attackerTeams;
        private Team[] _defenderTeams;
        private Dictionary<int, Dictionary<int, AreaData>> _dividedAreaDictionary;
        private List<int> _areaIndexList;
        private int _lastAreaLostByDefender;
        private bool _isMissionInitialized;
        private bool _spawnReinforcements;
        private bool _setChargeOrderNextFrame;
        private bool _initialized;
        private bool _started;
        private bool _reinforcementsEnabled;
        private bool _combatActivated;
        private int _removedAllyCounter;

        public CoopExactCampaignLordsHallMissionController(
            IMissionTroopSupplier[] suppliers,
            float areaLostRatio,
            float attackerDefenderTroopCountRatio,
            int attackerSideTroopCountMax,
            int defenderSideTroopCountMax,
            BattleSideEnum playerSide)
        {
            _areaLostRatio = areaLostRatio;
            _attackerDefenderTroopCountRatio = attackerDefenderTroopCountRatio;
            _attackerSideTroopCountMax = Math.Max(0, attackerSideTroopCountMax);
            _defenderSideTroopCountMax = Math.Max(0, defenderSideTroopCountMax);
            _playerSide = playerSide;
            _missionSides = new MissionSide[2];

            for (int index = 0; index < 2; index++)
            {
                BattleSideEnum side = (BattleSideEnum)index;
                IMissionTroopSupplier supplier =
                    suppliers != null && index < suppliers.Length ? suppliers[index] : null;
                _missionSides[index] = new MissionSide(side, supplier, side == playerSide);
            }
        }

        public bool HasStarted => _started;

        public bool ReinforcementsEnabled => _reinforcementsEnabled;

        public bool HasMaterializedBothSides =>
            _missionSides[(int)BattleSideEnum.Attacker].NumberOfSpawnedTroops > 0 &&
            _missionSides[(int)BattleSideEnum.Defender].NumberOfSpawnedTroops > 0;

        public BattleSideEnum PlayerSide => _playerSide;

        public void EnsureInitializedAndStarted()
        {
            if (!_initialized)
                OnBehaviorInitialize();

            if (!_started)
                AfterStart();
        }

        public void SetReinforcementsEnabled(bool enabled)
        {
            _reinforcementsEnabled = enabled;
        }

        public string BuildRuntimeSummary()
        {
            MissionSide defender = _missionSides[(int)BattleSideEnum.Defender];
            MissionSide attacker = _missionSides[(int)BattleSideEnum.Attacker];
            int defenderActive = Mission.Current?.DefenderTeam?.ActiveAgents?.Count ?? 0;
            int attackerActive = Mission.Current?.AttackerTeam?.ActiveAgents?.Count ?? 0;

            return
                "Mode=LordsHall" +
                " Started=" + _started +
                " MissionInitialized=" + _isMissionInitialized +
                " CombatActivated=" + _combatActivated +
                " ReinforcementsEnabled=" + _reinforcementsEnabled +
                " Attacker[Active=" + attackerActive +
                ",SpawnActive=" + attacker.TroopSpawningActive +
                ",Remaining=" + attacker.NumberOfRemainingTroops +
                ",Total=" + attacker.TotalTroopCount + "]" +
                " Defender[Active=" + defenderActive +
                ",SpawnActive=" + defender.TroopSpawningActive +
                ",Remaining=" + defender.NumberOfRemainingTroops +
                ",Total=" + defender.TotalTroopCount + "]" +
                " Areas=" + (_areaIndexList?.Count ?? 0) +
                " LastLostArea=" + _lastAreaLostByDefender +
                " RemovedAllyCounter=" + _removedAllyCounter +
                " PendingAttackerReinforcement=" + _spawnReinforcements;
        }

        public override void OnBehaviorInitialize()
        {
            if (_initialized)
                return;

            base.OnBehaviorInitialize();
            Mission.GetAgentTroopClass_Override += GetLordsHallFightTroopClass;
            _initialized = true;
        }

        public override void AfterStart()
        {
            if (_started)
                return;

            base.AfterStart();
            Mission.DoesMissionRequireCivilianEquipment = false;
            _started = true;
        }

        public override void OnMissionStateFinalized()
        {
            base.OnMissionStateFinalized();
            Mission.GetAgentTroopClass_Override -= GetLordsHallFightTroopClass;
        }

        public override void OnMissionTick(float dt)
        {
            if (!_isMissionInitialized)
            {
                InitializeMission();
                _isMissionInitialized = true;
                return;
            }

            if (!_combatActivated)
            {
                CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
                if (currentPhase < CoopBattlePhase.BattleActive || currentPhase >= CoopBattlePhase.BattleEnded)
                    return;

                ActivateCombat();
            }

            if (_setChargeOrderNextFrame)
            {
                if (Mission.PlayerTeam?.ActiveAgents?.Count > 0)
                {
                    Mission.PlayerTeam.PlayerOrderController.SelectAllFormations();
                    Mission.PlayerTeam.PlayerOrderController.SetOrder(OrderType.Charge);
                }

                _setChargeOrderNextFrame = false;
            }

            CheckForReinforcement();
            CheckIfAnyAreaIsLostByDefender();
        }

        public override void OnAgentRemoved(
            Agent affectedAgent,
            Agent affectorAgent,
            AgentState agentState,
            KillingBlow blow)
        {
            if (affectedAgent?.Team == null)
                return;

            if (affectedAgent.Team.IsDefender)
            {
                FindAgentMachine(affectedAgent)?.Item2.StopUse();
                return;
            }

            _setChargeOrderNextFrame = affectedAgent.IsMainAgent;
            _removedAllyCounter++;
            if (_removedAllyCounter >= 5)
            {
                _spawnReinforcements = true;
                _removedAllyCounter = 0;
            }
        }

        public void StartSpawner(BattleSideEnum side)
        {
            _missionSides[(int)side].SetSpawnTroops(true);
        }

        public void StopSpawner(BattleSideEnum side)
        {
            _missionSides[(int)side].SetSpawnTroops(false);
        }

        public bool IsSideSpawnEnabled(BattleSideEnum side)
        {
            return _missionSides[(int)side].TroopSpawningActive;
        }

        public bool IsSideDepleted(BattleSideEnum side)
        {
            return _missionSides[(int)side].NumberOfActiveTroops == 0;
        }

        public float GetReinforcementInterval(BattleSideEnum side = BattleSideEnum.None)
        {
            return 0f;
        }

        public IEnumerable<IAgentOriginBase> GetAllTroopsForSide(BattleSideEnum side)
        {
            return _missionSides[(int)side].GetAllTroops();
        }

        public int GetNumberOfPlayerControllableTroops()
        {
            if (_playerSide == BattleSideEnum.None)
                return 0;

            return _missionSides[(int)_playerSide].PlayerControllableTroopCount;
        }

        public bool GetSpawnHorses(BattleSideEnum side)
        {
            return false;
        }

        private Tuple<int, AreaEntityData> FindAgentMachine(Agent agent)
        {
            if (_dividedAreaDictionary == null)
                return null;

            foreach (KeyValuePair<int, Dictionary<int, AreaData>> areaEntry in _dividedAreaDictionary)
            {
                foreach (KeyValuePair<int, AreaData> subAreaEntry in areaEntry.Value)
                {
                    AreaEntityData areaEntity = subAreaEntry.Value.FindAgentMachine(agent);
                    if (areaEntity != null)
                        return new Tuple<int, AreaEntityData>(areaEntry.Key, areaEntity);
                }
            }

            return null;
        }

        private void InitializeMission()
        {
            _areaIndexList = new List<int>();
            _dividedAreaDictionary = new Dictionary<int, Dictionary<int, AreaData>>();

            IOrderedEnumerable<FightAreaMarker> orderedAreas =
                from area in Mission.ActiveMissionObjects.FindAllWithType<FightAreaMarker>()
                orderby area.AreaIndex
                select area;

            Mission.DeploymentPlan.MakeDefaultDeploymentPlans();

            foreach (FightAreaMarker area in orderedAreas)
            {
                if (!_dividedAreaDictionary.ContainsKey(area.AreaIndex))
                    _dividedAreaDictionary.Add(area.AreaIndex, new Dictionary<int, AreaData>());

                if (!_dividedAreaDictionary[area.AreaIndex].ContainsKey(area.SubAreaIndex))
                {
                    _dividedAreaDictionary[area.AreaIndex].Add(
                        area.SubAreaIndex,
                        new AreaData(new[] { area }));
                }
                else
                {
                    _dividedAreaDictionary[area.AreaIndex][area.SubAreaIndex].AddAreaMarker(area);
                }
            }

            _areaIndexList = _dividedAreaDictionary.Keys.ToList();

            _missionSides[(int)BattleSideEnum.Defender].SpawnDefenders(
                _dividedAreaDictionary,
                _defenderSideTroopCountMax);
            int defenderActiveTroopCount = _missionSides[(int)BattleSideEnum.Defender].NumberOfActiveTroops;

            _defenderTeams = new[]
            {
                Mission.Current.DefenderTeam,
                Mission.Current.DefenderAllyTeam
            };

            int attackerInitialSpawnCount = MathF.Max(
                1,
                MathF.Min(
                    _attackerSideTroopCountMax,
                    MathF.Round(defenderActiveTroopCount * _attackerDefenderTroopCountRatio)));
            _missionSides[(int)BattleSideEnum.Attacker].SpawnAttackers(attackerInitialSpawnCount, isReinforcement: false);

            bool playerOwnsAttackerTeam =
                Mission.Current.AttackerTeam == Mission.Current.PlayerTeam ||
                (Mission.Current.AttackerAllyTeam != null &&
                 Mission.Current.AttackerAllyTeam == Mission.Current.PlayerTeam);

            _attackerTeams = new[]
            {
                Mission.Current.AttackerTeam,
                Mission.Current.AttackerAllyTeam
            };

            foreach (Team team in _attackerTeams)
            {
                if (team == null)
                    continue;

                foreach (Formation formation in team.FormationsIncludingEmpty)
                {
                    if (formation.CountOfUnits > 0)
                    {
                        formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderSquare);
                        formation.SetFormOrder(FormOrder.FormOrderDeep);
                    }

                    formation.SetMovementOrder(MovementOrder.MovementOrderStop);
                    formation.SetFiringOrder(FiringOrder.FiringOrderHoldYourFire);

                    if (playerOwnsAttackerTeam)
                        formation.PlayerOwner = Mission.Current.MainAgent;
                }
            }
        }

        private void ActivateCombat()
        {
            if (_combatActivated)
                return;

            int defenderActivated = _missionSides[(int)BattleSideEnum.Defender].ActivateCombat();
            int attackerActivated = _missionSides[(int)BattleSideEnum.Attacker].ActivateCombat();
            foreach (Team team in _attackerTeams ?? Array.Empty<Team>())
            {
                if (team == null)
                    continue;

                foreach (Formation formation in team.FormationsIncludingEmpty)
                {
                    if (formation == null || formation.CountOfUnits <= 0)
                        continue;

                    formation.SetMovementOrder(MovementOrder.MovementOrderCharge);
                    formation.SetFiringOrder(FiringOrder.FiringOrderHoldYourFire);
                    formation.SetControlledByAI(false, false);
                }
            }

            _combatActivated = true;
            ModLogger.Info(
                "CoopExactCampaignLordsHallMissionController: activated lords-hall combat after battle start. " +
                "Attackers=" + attackerActivated +
                " Defenders=" + defenderActivated + ".");
        }

        private void CheckForReinforcement()
        {
            if (!_reinforcementsEnabled || !_spawnReinforcements)
                return;

            _missionSides[(int)BattleSideEnum.Attacker].SpawnAttackers(5, isReinforcement: true);
            _spawnReinforcements = false;
        }

        private void CheckIfAnyAreaIsLostByDefender()
        {
            if (_areaIndexList == null || _dividedAreaDictionary == null)
                return;

            int lostAreaIndex = -1;
            for (int index = 0; index < _areaIndexList.Count; index++)
            {
                int areaIndex = _areaIndexList[index];
                if (areaIndex <= _lastAreaLostByDefender || lostAreaIndex >= 0)
                    continue;

                foreach (KeyValuePair<int, AreaData> subArea in _dividedAreaDictionary[areaIndex])
                {
                    if (IsAreaLostByDefender(subArea.Value))
                    {
                        lostAreaIndex = areaIndex;
                        break;
                    }
                }
            }

            if (lostAreaIndex > 0)
                OnAreaLost(lostAreaIndex);
        }

        private void OnAreaLost(int areaIndex)
        {
            int nextAreaListIndex = MathF.Min(_areaIndexList.IndexOf(areaIndex) + 1, _areaIndexList.Count - 1);
            int firstAffectedIndex = MathF.Max(0, _areaIndexList.IndexOf(_lastAreaLostByDefender));
            for (int index = firstAffectedIndex; index < nextAreaListIndex; index++)
            {
                int key = _areaIndexList[index];
                foreach (KeyValuePair<int, AreaData> subArea in _dividedAreaDictionary[key])
                    StartAreaPullBack(subArea.Value, _areaIndexList[nextAreaListIndex]);
            }

            _lastAreaLostByDefender = areaIndex;
        }

        private void StartAreaPullBack(AreaData areaData, int nextAreaIndex)
        {
            foreach (AreaEntityData point in areaData.ArcherUsablePoints)
            {
                if (!point.InUse)
                    continue;

                Agent userAgent = point.UserAgent;
                point.StopUse();
                FindPosition(nextAreaIndex, isArcher: true)?.AssignAgent(userAgent);
            }

            foreach (AreaEntityData point in areaData.InfantryUsablePoints)
            {
                if (!point.InUse)
                    continue;

                Agent userAgent = point.UserAgent;
                point.StopUse();
                FindPosition(nextAreaIndex, isArcher: false)?.AssignAgent(userAgent);
            }
        }

        private AreaEntityData FindPosition(int nextAreaIndex, bool isArcher)
        {
            int subAreaIndex = SelectBestSubArea(nextAreaIndex, isArcher);
            if (subAreaIndex < 0)
            {
                isArcher = !isArcher;
                subAreaIndex = SelectBestSubArea(nextAreaIndex, isArcher);
            }

            return subAreaIndex < 0
                ? null
                : _dividedAreaDictionary[nextAreaIndex][subAreaIndex]
                    .GetAvailableMachines(isArcher)
                    .GetRandomElementInefficiently();
        }

        private int SelectBestSubArea(int areaIndex, bool isArcher)
        {
            int result = -1;
            float bestRatio = 0f;
            foreach (KeyValuePair<int, AreaData> subArea in _dividedAreaDictionary[areaIndex])
            {
                float availabilityRatio = GetAreaAvailabilityRatio(subArea.Value, isArcher);
                if (availabilityRatio > bestRatio)
                {
                    bestRatio = availabilityRatio;
                    result = subArea.Key;
                }
            }

            return result;
        }

        private static float GetAreaAvailabilityRatio(AreaData areaData, bool isArcher)
        {
            int totalCount =
                isArcher
                    ? areaData.ArcherUsablePoints.Count()
                    : areaData.InfantryUsablePoints.Count();
            int availableCount =
                isArcher
                    ? areaData.ArcherUsablePoints.Count(point => !point.InUse)
                    : areaData.InfantryUsablePoints.Count(point => !point.InUse);
            return totalCount > 0 ? (float)availableCount / totalCount : 0f;
        }

        private bool IsAreaLostByDefender(AreaData areaData)
        {
            int defendersInArea = 0;
            foreach (Team team in _defenderTeams ?? Array.Empty<Team>())
            {
                if (team == null)
                    continue;

                foreach (Agent agent in team.ActiveAgents)
                {
                    if (IsAgentInArea(agent, areaData))
                        defendersInArea++;
                }
            }

            int threshold = MathF.Round(defendersInArea * _areaLostRatio);
            bool lost = threshold == 0;
            if (lost)
                return true;

            foreach (Team team in _attackerTeams ?? Array.Empty<Team>())
            {
                if (team == null)
                    continue;

                foreach (Agent agent in team.ActiveAgents)
                {
                    if (!IsAgentInArea(agent, areaData))
                        continue;

                    threshold--;
                    if (threshold == 0)
                        return true;
                }
            }

            return false;
        }

        private static bool IsAgentInArea(Agent agent, AreaData areaData)
        {
            if (agent == null || areaData == null)
                return false;

            Vec3 position = agent.Position;
            foreach (FightAreaMarker area in areaData.AreaList)
            {
                if (area.IsPositionInRange(position))
                    return true;
            }

            return false;
        }

        private static FormationClass GetLordsHallFightTroopClass(BattleSideEnum side, BasicCharacterObject agentCharacter)
        {
            return agentCharacter.GetFormationClass().DismountedClass();
        }
    }
}
