using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Client-side canonicalization of ChangeCulture messages for the fixed
    /// coop test setup. This keeps UI state aligned with server-authoritative
    /// Attacker=Empire / Defender=Vlandia even when vanilla TDM emits random
    /// intermediate cultures.
    /// </summary>
    public static class ClientChangeCultureCanonicalizationPatch
    {
        private const string FixedAttackerCultureId = "empire";
        private const string FixedDefenderCultureId = "vlandia";
        private static readonly Dictionary<int, string> LastAppliedCultureByPeerIndex = new Dictionary<int, string>();

        public static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo target = typeof(MissionLobbyComponent).GetMethod(
                    "HandleServerEventChangeCulture",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (target == null)
                {
                    ModLogger.Info("ClientChangeCultureCanonicalizationPatch: HandleServerEventChangeCulture not found. Skip.");
                    return;
                }

                MethodInfo postfix = typeof(ClientChangeCultureCanonicalizationPatch).GetMethod(
                    nameof(HandleServerEventChangeCulture_Postfix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (postfix == null)
                {
                    ModLogger.Info("ClientChangeCultureCanonicalizationPatch: postfix not found. Skip.");
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                ModLogger.Info("ClientChangeCultureCanonicalizationPatch: postfix applied to MissionLobbyComponent.HandleServerEventChangeCulture.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ClientChangeCultureCanonicalizationPatch.Apply failed.", ex);
            }
        }

        private static void HandleServerEventChangeCulture_Postfix(object baseMessage)
        {
            if (!GameNetwork.IsClient || baseMessage == null)
                return;

            try
            {
                object peerObject = GetMemberValue(baseMessage, "Peer") ??
                                    GetMemberValue(baseMessage, "_peer") ??
                                    GetMemberValue(baseMessage, "MissionPeer");
                MissionPeer missionPeer = peerObject as MissionPeer;
                if (missionPeer == null || missionPeer.Team == null || Mission.Current == null || ReferenceEquals(missionPeer.Team, Mission.Current.SpectatorTeam))
                    return;

                string targetCultureId = ResolveFixedCultureIdForTeam(missionPeer.Team);
                if (string.IsNullOrWhiteSpace(targetCultureId))
                    return;

                BasicCultureObject targetCulture = MBObjectManager.Instance?.GetObject<BasicCultureObject>(targetCultureId);
                if (targetCulture == null)
                    return;

                string currentCultureId = missionPeer.Culture?.StringId;
                if (string.Equals(currentCultureId, targetCultureId, StringComparison.Ordinal))
                    return;

                missionPeer.Culture = targetCulture;
                InvokeCultureChanged(missionPeer, targetCulture);

                NetworkCommunicator peer = missionPeer.GetNetworkPeer();
                int peerIndex = peer?.Index ?? -1;
                if (peerIndex >= 0)
                    LastAppliedCultureByPeerIndex[peerIndex] = targetCultureId;

                ModLogger.Info(
                    "ClientChangeCultureCanonicalizationPatch: canonicalized ChangeCulture on client. " +
                    "Peer=" + (peer?.UserName ?? peerIndex.ToString()) +
                    " TeamIndex=" + missionPeer.Team.TeamIndex +
                    " Side=" + missionPeer.Team.Side +
                    " PreviousCulture=" + (currentCultureId ?? "null") +
                    " AppliedCulture=" + targetCultureId);
            }
            catch (Exception ex)
            {
                ModLogger.Info("ClientChangeCultureCanonicalizationPatch: postfix failed: " + ex.Message);
            }
        }

        private static string ResolveFixedCultureIdForTeam(Team team)
        {
            if (team == null)
                return null;

            if (team.Side == BattleSideEnum.Attacker)
                return FixedAttackerCultureId;

            if (team.Side == BattleSideEnum.Defender)
                return FixedDefenderCultureId;

            return null;
        }

        private static void InvokeCultureChanged(MissionPeer missionPeer, BasicCultureObject culture)
        {
            try
            {
                MethodInfo method = typeof(MissionPeer).GetMethod(
                    "CultureChanged",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(BasicCultureObject) },
                    null);
                method?.Invoke(missionPeer, new object[] { culture });
            }
            catch
            {
            }
        }

        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return null;

            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    return property.GetValue(instance, null);
                }
                catch
                {
                }
            }

            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                try
                {
                    return field.GetValue(instance);
                }
                catch
                {
                }
            }

            return null;
        }
    }
}
