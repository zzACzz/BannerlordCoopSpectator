using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Keeps the native multiplayer bot-control counters aligned with exact campaign
    /// reinforcements that enter a formation after a player has taken command.
    /// </summary>
    public static class CoopBotsControlledCountPatch
    {
        private const int MaxNetworkBotCount = 255;
        private static readonly object ApplyLock = new object();
        private static readonly PropertyInfo BotsUnderControlTotalProperty =
            typeof(MissionPeer).GetProperty(
                "BotsUnderControlTotal",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo BotsUnderControlTotalBackingField =
            typeof(MissionPeer).GetField(
                "<BotsUnderControlTotal>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static bool _applied;

        public static void Apply(Harmony harmony)
        {
            if (harmony == null)
                return;

            lock (ApplyLock)
            {
                if (_applied)
                    return;

                MethodInfo onAgentBuild = AccessTools.Method(
                    typeof(MissionLobbyComponent),
                    "OnAgentBuild",
                    new[] { typeof(Agent), typeof(Banner) });
                MethodInfo onBotDies = AccessTools.Method(
                    typeof(MissionLobbyComponent),
                    "OnBotDies",
                    new[] { typeof(Agent), typeof(MissionPeer), typeof(MissionPeer) });
                MethodInfo onAgentBuildPrefix = AccessTools.Method(
                    typeof(CoopBotsControlledCountPatch),
                    nameof(MissionLobbyComponent_OnAgentBuild_Prefix));
                MethodInfo onBotDiesPrefix = AccessTools.Method(
                    typeof(CoopBotsControlledCountPatch),
                    nameof(MissionLobbyComponent_OnBotDies_Prefix));

                if (onAgentBuild == null ||
                    onBotDies == null ||
                    onAgentBuildPrefix == null ||
                    onBotDiesPrefix == null)
                {
                    throw new MissingMethodException(
                        "Unable to resolve MissionLobbyComponent bot-control counter patch targets.");
                }

                harmony.Patch(onAgentBuild, prefix: new HarmonyMethod(onAgentBuildPrefix));
                harmony.Patch(onBotDies, prefix: new HarmonyMethod(onBotDiesPrefix));
                _applied = true;
            }
        }

        private static void MissionLobbyComponent_OnAgentBuild_Prefix(Agent agent)
        {
            if (!ShouldRunForCurrentMission() ||
                agent == null ||
                agent.IsMount ||
                agent.MissionPeer != null ||
                agent.OwningAgentMissionPeer != null ||
                agent.Formation == null)
            {
                return;
            }

            MissionPeer controllingPeer = FindControllingPeer(agent.Formation);
            if (controllingPeer == null)
                return;

            // Let the native OnAgentBuild path perform its normal increment by making
            // the late reinforcement a vanilla-owned bot before that path observes it.
            agent.SetOwningAgentMissionPeer(controllingPeer);
        }

        private static void MissionLobbyComponent_OnBotDies_Prefix(Agent botAgent)
        {
            if (!ShouldRunForCurrentMission() ||
                botAgent == null ||
                botAgent.IsMount ||
                botAgent.MissionPeer != null ||
                botAgent.Formation == null)
            {
                return;
            }

            try
            {
                MissionPeer controllingPeer = FindControllingPeer(botAgent.Formation);
                if (controllingPeer == null)
                    return;

                int otherActiveBots = CountOtherActiveBotsInFormation(
                    Mission.Current,
                    botAgent.Formation,
                    botAgent);
                int nativePreDecrementAlive = ClampNetworkCount(otherActiveBots + 1);
                if (nativePreDecrementAlive <= 0)
                    nativePreDecrementAlive = 1;

                int total = ClampNetworkCount(controllingPeer.BotsUnderControlTotal);
                if (total < nativePreDecrementAlive)
                    total = nativePreDecrementAlive;

                SetBotsUnderControlTotal(controllingPeer, total);
                controllingPeer.BotsUnderControlAlive = nativePreDecrementAlive;
            }
            catch
            {
                // The native method decrements immediately after this prefix. Preserve
                // its network contract even if exact reconciliation cannot complete.
                MissionPeer controllingPeer = FindControllingPeer(botAgent.Formation);
                if (controllingPeer == null)
                    return;

                int safeAlive = Math.Max(1, ClampNetworkCount(controllingPeer.BotsUnderControlAlive));
                int safeTotal = Math.Max(
                    safeAlive,
                    ClampNetworkCount(controllingPeer.BotsUnderControlTotal));
                SetBotsUnderControlTotal(controllingPeer, safeTotal);
                controllingPeer.BotsUnderControlAlive = safeAlive;
            }
        }

        private static bool ShouldRunForCurrentMission()
        {
            Mission mission = Mission.Current;
            if (mission == null ||
                !GameNetwork.IsServer ||
                !GameNetwork.IsSessionActive ||
                !SceneRuntimeClassifier.IsExactCampaignBattleScene(
                    mission.SceneName ?? string.Empty))
            {
                return false;
            }

            return mission.GetMissionBehavior<CoopMissionSpawnLogic>() != null ||
                   mission.GetMissionBehavior<CoopMissionNetworkBridge>() != null;
        }

        private static MissionPeer FindControllingPeer(Formation formation)
        {
            if (formation == null ||
                formation.PlayerOwner == null ||
                GameNetwork.NetworkPeers == null)
            {
                return null;
            }

            foreach (NetworkCommunicator networkPeer in GameNetwork.NetworkPeers)
            {
                MissionPeer missionPeer = networkPeer?.GetComponent<MissionPeer>();
                if (missionPeer != null &&
                    ReferenceEquals(missionPeer.ControlledFormation, formation))
                {
                    return missionPeer;
                }
            }

            return null;
        }

        private static int CountOtherActiveBotsInFormation(
            Mission mission,
            Formation formation,
            Agent excludedAgent)
        {
            if (mission?.AllAgents == null || formation == null)
                return 0;

            int count = 0;
            for (int i = 0; i < mission.AllAgents.Count; i++)
            {
                Agent candidate = mission.AllAgents[i];
                if (candidate == null ||
                    ReferenceEquals(candidate, excludedAgent) ||
                    candidate.IsMount ||
                    candidate.MissionPeer != null ||
                    !candidate.IsActive() ||
                    !ReferenceEquals(candidate.Formation, formation))
                {
                    continue;
                }

                count++;
                if (count >= MaxNetworkBotCount - 1)
                    break;
            }

            return count;
        }

        private static int ClampNetworkCount(int value)
        {
            return Math.Max(0, Math.Min(MaxNetworkBotCount, value));
        }

        private static void SetBotsUnderControlTotal(MissionPeer missionPeer, int value)
        {
            if (missionPeer == null)
                return;

            int safeValue = ClampNetworkCount(value);
            MethodInfo setter = BotsUnderControlTotalProperty?.GetSetMethod(nonPublic: true);
            if (setter != null)
            {
                setter.Invoke(missionPeer, new object[] { safeValue });
                return;
            }

            BotsUnderControlTotalBackingField?.SetValue(missionPeer, safeValue);
        }
    }
}
