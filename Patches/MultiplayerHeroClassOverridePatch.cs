using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Server-only override for the exact vanilla class-selection path used by
    /// TeamDeathmatch spawning. This replaces observer-based troop-index hacks.
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

                MethodInfo postfix = typeof(MultiplayerHeroClassOverridePatch).GetMethod(
                    nameof(GetMPHeroClassForPeer_Postfix),
                    BindingFlags.Static | BindingFlags.NonPublic);

                if (postfix == null)
                {
                    ModLogger.Info("MultiplayerHeroClassOverridePatch: postfix method not found. Skip.");
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                ModLogger.Info("MultiplayerHeroClassOverridePatch: postfix applied to MultiplayerClassDivisions.GetMPHeroClassForPeer.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("MultiplayerHeroClassOverridePatch.Apply failed.", ex);
            }
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
