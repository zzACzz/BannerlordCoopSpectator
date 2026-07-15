using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Protects the exact vanilla peer-class path while a coop player observes
    /// their released AI agent, and keeps the server-side spawn class override.
    /// </summary>
    public static class MultiplayerHeroClassOverridePatch
    {
        private static readonly HashSet<string> LoggedOverrideKeys = new HashSet<string>(StringComparer.Ordinal);

        public static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(MultiplayerClassDivisions),
                    "GetMPHeroClassForPeer",
                    new[] { typeof(MissionPeer), typeof(bool) });

                if (target == null)
                {
                    ModLogger.Info("MultiplayerHeroClassOverridePatch: GetMPHeroClassForPeer(MissionPeer, bool) not found. Skip.");
                    return;
                }

                MethodInfo prefix = typeof(MultiplayerHeroClassOverridePatch).GetMethod(
                    nameof(GetMPHeroClassForPeer_Prefix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo postfix = typeof(MultiplayerHeroClassOverridePatch).GetMethod(
                    nameof(GetMPHeroClassForPeer_Postfix),
                    BindingFlags.Static | BindingFlags.NonPublic);

                if (prefix == null || postfix == null)
                {
                    ModLogger.Info("MultiplayerHeroClassOverridePatch: prefix/postfix method not found. Skip.");
                    return;
                }

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(prefix),
                    postfix: new HarmonyMethod(postfix));
                ModLogger.Info(
                    "MultiplayerHeroClassOverridePatch: client detached-peer guard and server override applied to " +
                    "MultiplayerClassDivisions.GetMPHeroClassForPeer.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("MultiplayerHeroClassOverridePatch.Apply failed.", ex);
            }
        }

        private static bool GetMPHeroClassForPeer_Prefix(
            MissionPeer peer,
            bool skipTeamCheck,
            ref MultiplayerClassDivisions.MPHeroClass __result)
        {
            if (!GameNetwork.IsClient || peer == null || peer.ControlledAgent != null)
                return true;

            Mission mission = Mission.Current;
            if (mission == null ||
                mission.GetMissionBehavior<CoopMissionNetworkBridge>() == null ||
                peer.SelectedTroopIndex < 0 ||
                (!skipTeamCheck &&
                 (peer.Team == null || peer.Team.Side == BattleSideEnum.None)))
            {
                return true;
            }

            try
            {
                if (IsPeerCultureHeroClassIndexValid(peer, peer.SelectedTroopIndex))
                    return true;

                Agent detachedAgent = ResolveDetachedPeerAgent(mission, peer);
                __result = detachedAgent == null
                    ? null
                    : MultiplayerClassDivisions.GetMPHeroClassForCharacter(detachedAgent.Character);
                return false;
            }
            catch
            {
                // Native callers support a null class, but the native method does
                // not support an out-of-range culture-class index.
                __result = null;
                return false;
            }
        }

        private static bool IsPeerCultureHeroClassIndexValid(MissionPeer peer, int selectedTroopIndex)
        {
            if (peer?.Culture == null || selectedTroopIndex < 0)
                return false;

            int index = 0;
            foreach (MultiplayerClassDivisions.MPHeroClass ignored in
                     MultiplayerClassDivisions.GetMPHeroClasses(peer.Culture))
            {
                if (index == selectedTroopIndex)
                    return true;

                index++;
            }

            return false;
        }

        private static Agent ResolveDetachedPeerAgent(Mission mission, MissionPeer peer)
        {
            MissionPeer localMissionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
            if (ReferenceEquals(peer, localMissionPeer) &&
                CoopBattleAgentControlRuntimeState.TryGetActiveClientObservedAgent(
                    mission,
                    out Agent observedAgent))
            {
                return observedAgent;
            }

            Agent followedAgent = peer.FollowedAgent;
            return followedAgent != null &&
                   ReferenceEquals(followedAgent.Mission, mission) &&
                   followedAgent.IsActive()
                ? followedAgent
                : null;
        }

        private static void GetMPHeroClassForPeer_Postfix(
            MissionPeer peer,
            bool skipTeamCheck,
            ref MultiplayerClassDivisions.MPHeroClass __result)
        {
            MissionPeer missionPeer = peer;
            if (!GameNetwork.IsServer || missionPeer == null)
                return;

            try
            {
                if (!CoopMissionSpawnLogic.TryResolvePreferredHeroClassForPeer(
                        missionPeer,
                        __result,
                        out MultiplayerClassDivisions.MPHeroClass preferredClass,
                        out int preferredTroopIndex,
                        out string debugReason))
                {
                    return;
                }

                __result = preferredClass;

                if (preferredTroopIndex >= 0 && missionPeer.SelectedTroopIndex != preferredTroopIndex)
                {
                    missionPeer.SelectedTroopIndex = preferredTroopIndex;

                    NetworkCommunicator networkPeer = missionPeer.GetNetworkPeer();
                    if (networkPeer != null)
                    {
                        GameNetwork.BeginBroadcastModuleEvent();
                        GameNetwork.WriteMessage(new NetworkMessages.FromServer.UpdateSelectedTroopIndex(networkPeer, preferredTroopIndex));
                        GameNetwork.EndBroadcastModuleEvent(GameNetwork.EventBroadcastFlags.None);
                    }
                }

                string classId = preferredClass?.HeroCharacter?.StringId ?? "null";
                string peerName = missionPeer.GetNetworkPeer()?.UserName ?? missionPeer.GetNetworkPeer()?.Index.ToString() ?? "unknown";
                string logKey = peerName + "|" + classId + "|" + preferredTroopIndex;
                if (LoggedOverrideKeys.Add(logKey))
                {
                    ModLogger.Info(
                        "MultiplayerHeroClassOverridePatch: overridden MPHeroClass in vanilla spawn path. " +
                        "Peer=" + peerName +
                        " Culture=" + (missionPeer.Culture?.StringId ?? "null") +
                        " TroopIndex=" + preferredTroopIndex +
                        " HeroClass=" + classId +
                        " Reason=" + debugReason);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("MultiplayerHeroClassOverridePatch: postfix failed: " + ex.Message);
            }
        }
    }
}
