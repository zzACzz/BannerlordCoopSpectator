using System.Collections.Generic; // List<Agent>
using CoopSpectator.Infrastructure; // ModLogger
using TaleWorlds.Core; // BattleSideEnum, BasicCharacterObject, AgentBuildData, BasicBattleAgentOrigin, AgentControllerType
using TaleWorlds.Library; // Vec2, Vec3
using TaleWorlds.MountAndBlade; // Mission, GameNetwork, Team, MissionLogic, Agent, FiringOrder
using TaleWorlds.MountAndBlade.Multiplayer; // MissionMultiplayerGameModeBase, MultiplayerGameType
using TaleWorlds.ObjectSystem; // MBObjectManager

namespace CoopSpectator.GameMode
{
    /// <summary>
    /// Серверна логіка TdmClone: клон TDM 1:1 — ті самі команди Attacker/Defender і мінімальний спавн що й CoopTdm.
    /// Єдина відмінність від CoopTdm — ім'я режиму (GameModeId) для перевірки узгодження ID у трьох місцях.
    /// </summary>
    public sealed class MissionMultiplayerTdmClone : MissionMultiplayerGameModeBase
    {
        /// <summary>Етап 1: кількість бійців на сторону (3+3 без золота).</summary>
        private const int AgentsPerSide = 3;

        private bool _hasInitialized;

        public override MultiplayerGameType GetMissionType()
        {
            return MultiplayerGameType.TeamDeathmatch;
        }

        public override bool IsGameModeUsingOpposingTeams => true;
        public override bool IsGameModeHidingAllAgentVisuals => false;

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            _hasInitialized = false;
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            if (_hasInitialized || Mission?.Teams == null)
                return;

            _hasInitialized = true;

            if (!GameNetwork.IsServer)
                return;

            try
            {
                InitializeTeamsAndMinimalSpawn();
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("TdmClone server: InitializeTeamsAndMinimalSpawn failed.", ex);
            }
        }

        private void InitializeTeamsAndMinimalSpawn()
        {
            Mission mission = Mission;
            if (mission == null) return;

            if (mission.Teams.Attacker == null)
                mission.Teams.Add(BattleSideEnum.Attacker, 0xFFCC2222u, 0xFF661111u, null, false, false, false);
            if (mission.Teams.Defender == null)
                mission.Teams.Add(BattleSideEnum.Defender, 0xFF2222CCu, 0xFF111166u, null, false, false, false);

            Team attackerTeam = mission.Teams.Attacker;
            Team defenderTeam = mission.Teams.Defender;
            attackerTeam.SetIsEnemyOf(defenderTeam, true);
            defenderTeam.SetIsEnemyOf(attackerTeam, true);

            // Етап 1: troop тільки з MBObjectManager (без Campaign — працює на дедику).
            BasicCharacterObject troop = GetInfantryTroopForDedicated();
            if (troop == null)
            {
                ModLogger.Info("TdmClone: no troop found, skipping agent spawn.");
                return;
            }

            // Базові позиції: attackers зліва, defenders справа (прості зміщення від центру).
            Vec3 attackerBase = new Vec3(-12f, 0f, 0f);
            Vec3 defenderBase = new Vec3(12f, 0f, 0f);
            Vec2 right = new Vec2(0f, 1f);
            float lateralStep = 2f;

            var attackerAgents = new List<Agent>();
            var defenderAgents = new List<Agent>();

            for (int i = 0; i < AgentsPerSide; i++)
            {
                float lateral = (i - (AgentsPerSide - 1) * 0.5f) * lateralStep;
                Vec3 posAtt = attackerBase + new Vec3(right.x * lateral, right.y * lateral, 0f);
                Vec3 posDef = defenderBase + new Vec3(-right.x * lateral, -right.y * lateral, 0f);

                Agent a = SpawnAiAgent(mission, attackerTeam, troop, posAtt, defenderBase);
                if (a != null) attackerAgents.Add(a);

                Agent d = SpawnAiAgent(mission, defenderTeam, troop, posDef, attackerBase);
                if (d != null) defenderAgents.Add(d);
            }

            for (int i = 0; i < attackerAgents.Count && defenderAgents.Count > 0; i++)
            {
                Agent a = attackerAgents[i];
                Agent d = defenderAgents[i % defenderAgents.Count];
                a.SetTargetAgent(d);
                a.SetAutomaticTargetSelection(true);
                a.ForceAiBehaviorSelection();
            }
            for (int i = 0; i < defenderAgents.Count && attackerAgents.Count > 0; i++)
            {
                Agent d = defenderAgents[i];
                Agent a = attackerAgents[i % attackerAgents.Count];
                d.SetTargetAgent(a);
                d.SetAutomaticTargetSelection(true);
                d.ForceAiBehaviorSelection();
            }

            ModLogger.Info("TdmClone mission started: teams + " + attackerAgents.Count + "+" + defenderAgents.Count + " agents (Stage 1).");
        }

        /// <summary>Повертає одного піхотного troop з MBObjectManager (без Campaign). Для дедика.</summary>
        private static BasicCharacterObject GetInfantryTroopForDedicated()
        {
            string[] ids = { "imperial_infantryman", "imperial_legionary", "aserai_infantryman", "sturgian_warrior" };
            foreach (string id in ids)
            {
                BasicCharacterObject o = MBObjectManager.Instance?.GetObject<BasicCharacterObject>(id);
                if (o != null && !o.IsMounted) return o;
            }
            return null;
        }

        private static Agent SpawnAiAgent(Mission mission, Team team, BasicCharacterObject troop, Vec3 position, Vec3 lookAt)
        {
            var origin = new BasicBattleAgentOrigin(troop);
            Vec3 toEnemy3 = lookAt - position;
            Vec2 toEnemy2 = new Vec2(toEnemy3.x, toEnemy3.y);
            if (toEnemy2.LengthSquared < 0.001f) toEnemy2 = new Vec2(1f, 0f);
            toEnemy2.Normalize();

            AgentBuildData buildData = new AgentBuildData(troop);
            buildData.Team(team);
            buildData.Controller(AgentControllerType.AI);
            buildData.TroopOrigin(origin);
            buildData.InitialPosition(in position);
            buildData.InitialDirection(in toEnemy2);
            buildData.SpawnsIntoOwnFormation(false);
            buildData.SpawnsUsingOwnTroopClass(false);

            Agent agent = mission.SpawnAgent(buildData, spawnFromAgentVisuals: false);
            if (agent != null)
            {
                agent.SetFiringOrder(FiringOrder.RangedWeaponUsageOrderEnum.FireAtWill);
                agent.SetAutomaticTargetSelection(true);
            }
            return agent;
        }
    }
}
