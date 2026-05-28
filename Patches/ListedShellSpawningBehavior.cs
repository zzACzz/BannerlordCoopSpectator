using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Missions.Multiplayer;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Patches
{
    internal sealed class ListedShellSpawningBehavior : SpawningBehaviorBase
    {
        private static string _lastInitializedMissionKey = string.Empty;

        public override void Initialize(SpawnComponent spawnComponent)
        {
            base.Initialize(spawnComponent);

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

        public override void OnTick(float dt)
        {
            if (IsSpawningEnabled && SpawnCheckTimer.Check(Mission.CurrentTime))
                SpawnAgents();

            base.OnTick(dt);
        }

        protected override void SpawnAgents()
        {
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
                    missionPeer.HasSpawnedAgentVisuals ||
                    missionPeer.Team == null ||
                    missionPeer.Team == Mission.SpectatorTeam ||
                    !missionPeer.TeamInitialPerkInfoReady ||
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

                if (GameMode.ShouldSpawnVisualsForServer(networkPeer))
                {
                    AgentVisualSpawnComponent.SpawnAgentVisualsForPeer(
                        missionPeer,
                        agentBuildData,
                        missionPeer.SelectedTroopIndex,
                        false,
                        0);
                    if (agentBuildData.AgentVisualsIndex == 0)
                    {
                        missionPeer.HasSpawnedAgentVisuals = true;
                        missionPeer.EquipmentUpdatingExpired = false;
                    }
                }

                GameMode.HandleAgentVisualSpawning(networkPeer, agentBuildData, 0, useCosmetics: true);
            }
        }

        public override bool AllowEarlyAgentVisualsDespawning(MissionPeer lobbyPeer)
        {
            return true;
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
