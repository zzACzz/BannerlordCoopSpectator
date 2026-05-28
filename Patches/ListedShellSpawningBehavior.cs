using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Missions.Multiplayer;
using TaleWorlds.MountAndBlade.Multiplayer;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Patches
{
    internal sealed class ListedShellSpawningBehavior : SpawningBehaviorBase
    {
        private static string _lastInitializedMissionKey = string.Empty;

        public override void Initialize(SpawnComponent spawnComponent)
        {
            SpawnComponent = spawnComponent;
            GameMode = Mission.GetMissionBehavior<MissionMultiplayerGameModeBase>();
            MissionLobbyComponent = Mission.GetMissionBehavior<MissionLobbyComponent>();
            SpawnCheckTimer = new Timer(Mission.Current.CurrentTime, 0.2f);

            string missionKey =
                (Mission?.SceneName ?? Mission.Current?.SceneName ?? "unknown") +
                "|" +
                (GameNetwork.IsServer ? "server" : "client");
            if (!string.Equals(_lastInitializedMissionKey, missionKey, StringComparison.Ordinal))
            {
                _lastInitializedMissionKey = missionKey;
                ModLogger.Info(
                    "ListedShellSpawningBehavior: installed listed-shell spawn ingress without TDM gold gate or troop-cost deduction. " +
                    "Mission=" + (Mission?.SceneName ?? Mission.Current?.SceneName ?? "unknown") +
                    " Side=" + (GameNetwork.IsServer ? "server" : "client") + ".");
            }

            if (GameMode.WarmupComponent == null)
                RequestStartSpawnSession();
        }

        public override void Clear()
        {
        }

        public override void OnTick(float dt)
        {
            if (!GameNetwork.IsServer)
                return;

            if (IsSpawningEnabled && SpawnCheckTimer.Check(Mission.CurrentTime))
                SpawnAgents();
        }

        protected override void SpawnAgents()
        {
            if (Mission == null || GameMode == null || SpawnComponent == null)
                return;

            BasicCultureObject attackerCulture = MBObjectManager.Instance.GetObject<BasicCultureObject>(
                MultiplayerOptions.OptionType.CultureTeam1.GetStrValue());
            BasicCultureObject defenderCulture = MBObjectManager.Instance.GetObject<BasicCultureObject>(
                MultiplayerOptions.OptionType.CultureTeam2.GetStrValue());
            MultiplayerBattleColors battleColors = MultiplayerBattleColors.CreateWith(attackerCulture, defenderCulture);

            foreach (NetworkCommunicator networkPeer in GameNetwork.NetworkPeers)
            {
                if (!networkPeer.IsSynchronized)
                    continue;

                MissionPeer missionPeer = networkPeer.GetComponent<MissionPeer>();
                if (missionPeer == null ||
                    missionPeer.ControlledAgent != null ||
                    missionPeer.Team == null ||
                    missionPeer.Team == Mission.SpectatorTeam)
                {
                    continue;
                }

                if (!CoopMissionSpawnLogic.TryArmListedShellNativeSpawnCompatibilityState(
                        Mission,
                        missionPeer,
                        networkPeer,
                        "ListedShellSpawningBehavior.SpawnAgents"))
                {
                    continue;
                }

                if (!missionPeer.TeamInitialPerkInfoReady ||
                    !missionPeer.SpawnTimer.Check(Mission.CurrentTime))
                {
                    continue;
                }

                MultiplayerClassDivisions.MPHeroClass heroClass =
                    MultiplayerClassDivisions.GetMPHeroClassForPeer(missionPeer, false);
                if (heroClass == null)
                    continue;

                BasicCharacterObject heroCharacter = heroClass.HeroCharacter;
                Equipment equipment = heroCharacter.Equipment.Clone();
                MPPerkObject.MPOnSpawnPerkHandler onSpawnPerkHandler = MPPerkObject.GetOnSpawnPerkHandler(missionPeer);
                IEnumerable<(EquipmentIndex, EquipmentElement)> alternativeEquipment =
                    onSpawnPerkHandler?.GetAlternativeEquipments(isPlayer: true);
                if (alternativeEquipment != null)
                {
                    foreach ((EquipmentIndex index, EquipmentElement element) in alternativeEquipment)
                        equipment[index] = element;
                }
                GameMode.AddCosmeticItemsToEquipment(
                    equipment,
                    GameMode.GetUsedCosmeticsFromPeer(missionPeer, heroCharacter));

                MultiplayerBattleColors.MultiplayerCultureColorInfo peerColors = battleColors.GetPeerColors(missionPeer);
                BasicCultureObject bodyCulture = ResolveBodyCulture(missionPeer, attackerCulture, defenderCulture);
                AgentBuildData agentBuildData = new AgentBuildData(heroCharacter)
                    .MissionPeer(missionPeer)
                    .Equipment(equipment)
                    .Team(missionPeer.Team)
                    .TroopOrigin(new BasicBattleAgentOrigin(heroCharacter))
                    .IsFemale(missionPeer.Peer.IsFemale)
                    .BodyProperties(GetBodyProperties(missionPeer, bodyCulture))
                    .VisualsIndex(0)
                    .ClothingColor1(peerColors.ClothingColor1Uint)
                    .ClothingColor2(peerColors.ClothingColor2Uint);

                if (!TryApplySpawnFrame(agentBuildData, missionPeer, equipment))
                    continue;

                Agent agent = Mission.SpawnAgent(agentBuildData, spawnFromAgentVisuals: false);
                if (agent == null)
                    continue;

                agent.AddComponent(new MPPerksAgentComponent(agent));
                agent.MountAgent?.UpdateAgentProperties();
                agent.HealthLimit += onSpawnPerkHandler?.GetHitpoints(isPlayer: true) ?? 0f;
                agent.Health = agent.HealthLimit;
                agent.WieldInitialWeapons();
                missionPeer.SpawnCountThisRound++;
                missionPeer.FollowedAgent = agent;
                CoopMissionSpawnLogic.FinalizeListedShellDirectSpawnCompatibilityState(
                    Mission,
                    missionPeer,
                    "ListedShellSpawningBehavior.SpawnAgents");
                MPPerkObject.GetPerkHandler(missionPeer)?.OnEvent(MPPerkCondition.PerkEventFlags.SpawnEnd);
            }
        }

        public override bool CanUpdateSpawnEquipment(MissionPeer missionPeer)
        {
            return false;
        }

        public override bool AllowEarlyAgentVisualsDespawning(MissionPeer lobbyPeer)
        {
            return false;
        }

        public override int GetMaximumReSpawnPeriodForPeer(MissionPeer peer)
        {
            if (GameMode.WarmupComponent != null && GameMode.WarmupComponent.IsInWarmup)
                return 3;

            if (peer?.Team != null)
            {
                if (peer.Team.Side == BattleSideEnum.Attacker)
                    return MultiplayerOptions.OptionType.RespawnPeriodTeam2.GetIntValue();

                if (peer.Team.Side == BattleSideEnum.Defender)
                    return MultiplayerOptions.OptionType.RespawnPeriodTeam1.GetIntValue();
            }

            return -1;
        }

        protected override bool IsRoundInProgress()
        {
            return Mission.Current.CurrentState == Mission.State.Continuing;
        }

        private bool TryApplySpawnFrame(AgentBuildData agentBuildData, MissionPeer missionPeer, Equipment equipment)
        {
            if (agentBuildData == null || missionPeer?.Team == null)
                return false;

            bool hasMount = equipment[EquipmentIndex.ArmorItemEndSlot].Item != null;
            MatrixFrame spawnFrame = SpawnComponent.GetSpawnFrame(
                missionPeer.Team,
                hasMount,
                missionPeer.SpawnCountThisRound == 0);
            if (spawnFrame.IsIdentity)
            {
                ModLogger.Info(
                    "ListedShellSpawningBehavior: skipped direct listed-shell spawn because spawn frame was unavailable. " +
                    "Mission=" + (Mission?.SceneName ?? "unknown") +
                    " Peer=" + (missionPeer.GetNetworkPeer()?.UserName ?? missionPeer.GetNetworkPeer()?.Index.ToString() ?? "none") +
                    " TeamIndex=" + missionPeer.Team.TeamIndex +
                    " HasMount=" + hasMount +
                    " InitialSpawn=" + (missionPeer.SpawnCountThisRound == 0));
                return false;
            }

            spawnFrame.rotation.OrthonormalizeAccordingToForwardAndKeepUpAsZAxis();
            agentBuildData.InitialPosition(in spawnFrame.origin);
            agentBuildData.InitialDirection(spawnFrame.rotation.f.AsVec2.Normalized());
            return true;
        }

        private static BasicCultureObject ResolveBodyCulture(
            MissionPeer missionPeer,
            BasicCultureObject attackerCulture,
            BasicCultureObject defenderCulture)
        {
            if (missionPeer?.Culture != null)
            {
                if (ReferenceEquals(missionPeer.Culture, attackerCulture))
                    return attackerCulture;

                if (ReferenceEquals(missionPeer.Culture, defenderCulture))
                    return defenderCulture;
            }

            if (missionPeer?.Team?.Side == BattleSideEnum.Attacker)
                return attackerCulture;

            return defenderCulture ?? attackerCulture;
        }
    }
}
