using System;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Canonicalizes outgoing server ChangeCulture messages for the fixed
    /// coop test setup so vanilla TDM random cultures never reach clients.
    /// </summary>
    public static class ServerChangeCultureCanonicalizationPatch
    {
        private const string FixedAttackerCultureId = "empire";
        private const string FixedDefenderCultureId = "vlandia";

        public static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo target = typeof(GameNetwork)
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(method =>
                        string.Equals(method.Name, "WriteMessage", StringComparison.Ordinal) &&
                        method.GetParameters().Length == 1);
                if (target == null)
                {
                    ModLogger.Info("ServerChangeCultureCanonicalizationPatch: GameNetwork.WriteMessage(GameNetworkMessage) not found. Skip.");
                    return;
                }

                MethodInfo prefix = typeof(ServerChangeCultureCanonicalizationPatch).GetMethod(
                    nameof(WriteMessage_Prefix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (prefix == null)
                {
                    ModLogger.Info("ServerChangeCultureCanonicalizationPatch: prefix not found. Skip.");
                    return;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                ModLogger.Info("ServerChangeCultureCanonicalizationPatch: prefix applied to GameNetwork.WriteMessage(GameNetworkMessage).");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ServerChangeCultureCanonicalizationPatch.Apply failed.", ex);
            }
        }

        private static void WriteMessage_Prefix(object message)
        {
            if (!GameNetwork.IsServer || message == null)
                return;

            try
            {
                Type messageType = message.GetType();
                if (!string.Equals(messageType.FullName, "NetworkMessages.FromServer.ChangeCulture", StringComparison.Ordinal))
                    return;

                MissionPeer missionPeer = ResolveMissionPeer(message);
                if (missionPeer?.Team == null || Mission.Current == null || ReferenceEquals(missionPeer.Team, Mission.Current.SpectatorTeam))
                    return;

                string targetCultureId = ResolveFixedCultureIdForTeam(missionPeer.Team);
                if (string.IsNullOrWhiteSpace(targetCultureId))
                    return;

                BasicCultureObject targetCulture = MBObjectManager.Instance?.GetObject<BasicCultureObject>(targetCultureId);
                if (targetCulture == null)
                    return;

                BasicCultureObject messageCulture = ResolveCulture(message);
                string currentMessageCultureId = messageCulture?.StringId;
                if (string.Equals(currentMessageCultureId, targetCultureId, StringComparison.Ordinal))
                    return;

                if (!TrySetMemberValue(message, "Culture", targetCulture) &&
                    !TrySetMemberValue(message, "_culture", targetCulture))
                {
                    if (!TrySetFirstCompatibleMemberValue(message, typeof(BasicCultureObject), targetCulture))
                    {
                        ModLogger.Info("ServerChangeCultureCanonicalizationPatch: failed to rewrite outgoing ChangeCulture message culture.");
                        return;
                    }
                }

                missionPeer.Culture = targetCulture;

                NetworkCommunicator peer = missionPeer.GetNetworkPeer();
                ModLogger.Info(
                    "ServerChangeCultureCanonicalizationPatch: canonicalized outgoing ChangeCulture. " +
                    "Peer=" + (peer?.UserName ?? peer?.Index.ToString() ?? "unknown") +
                    " TeamIndex=" + missionPeer.Team.TeamIndex +
                    " Side=" + missionPeer.Team.Side +
                    " PreviousCulture=" + (currentMessageCultureId ?? "null") +
                    " AppliedCulture=" + targetCultureId);
            }
            catch (Exception ex)
            {
                ModLogger.Info("ServerChangeCultureCanonicalizationPatch: prefix failed: " + ex.Message);
            }
        }

        private static MissionPeer ResolveMissionPeer(object instance)
        {
            object peerObject = GetMemberValue(instance, "Peer") ??
                                GetMemberValue(instance, "_peer") ??
                                GetMemberValue(instance, "MissionPeer");
            if (peerObject is MissionPeer missionPeer)
                return missionPeer;

            return FindFirstCompatibleMemberValue(instance, typeof(MissionPeer)) as MissionPeer;
        }

        private static BasicCultureObject ResolveCulture(object instance)
        {
            object cultureObject = GetMemberValue(instance, "Culture") ??
                                   GetMemberValue(instance, "_culture");
            if (cultureObject is BasicCultureObject culture)
                return culture;

            return FindFirstCompatibleMemberValue(instance, typeof(BasicCultureObject)) as BasicCultureObject;
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

        private static bool TrySetMemberValue(object instance, string memberName, object value)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return false;

            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0 && property.PropertyType.IsInstanceOfType(value))
            {
                try
                {
                    property.SetValue(instance, value, null);
                    return true;
                }
                catch
                {
                }
            }

            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.FieldType.IsInstanceOfType(value))
            {
                try
                {
                    field.SetValue(instance, value);
                    return true;
                }
                catch
                {
                }
            }

            return false;
        }

        private static object FindFirstCompatibleMemberValue(object instance, Type targetType)
        {
            if (instance == null || targetType == null)
                return null;

            Type type = instance.GetType();
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0 || !targetType.IsAssignableFrom(property.PropertyType))
                    continue;

                try
                {
                    return property.GetValue(instance, null);
                }
                catch
                {
                }
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!targetType.IsAssignableFrom(field.FieldType))
                    continue;

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

        private static bool TrySetFirstCompatibleMemberValue(object instance, Type targetType, object value)
        {
            if (instance == null || targetType == null || value == null)
                return false;

            Type type = instance.GetType();
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!property.CanWrite || property.GetIndexParameters().Length != 0 || !targetType.IsAssignableFrom(property.PropertyType))
                    continue;

                try
                {
                    property.SetValue(instance, value, null);
                    return true;
                }
                catch
                {
                }
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!targetType.IsAssignableFrom(field.FieldType))
                    continue;

                try
                {
                    field.SetValue(instance, value);
                    return true;
                }
                catch
                {
                }
            }

            return false;
        }
    }
}
