using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.Network.Messages;
using TaleWorlds.CampaignSystem.Encounters;

namespace CoopSpectator.Campaign
{
    internal static class CampaignFieldBattleImportBridge
    {
        private sealed class LiveDescriptorBinding
        {
            public int DescriptorSeed { get; set; }
            public string SideId { get; set; }
            public string PartyId { get; set; }
            public string CharacterId { get; set; }
        }

        public static void ProbeLiveDescriptorRebind(CanonicalBattleContract contract)
        {
            if (!ExperimentalFeatures.EnableCanonicalFieldBattleImportBridgeProbe || contract == null)
                return;

            try
            {
                Dictionary<int, LiveDescriptorBinding> liveIndex = BuildLiveDescriptorIndex();
                List<CanonicalTroopInstance> exportedMissionParticipants = contract.TroopInstances?
                    .Where(instance => instance != null && instance.IsMissionParticipant && instance.CampaignTroopDescriptorSeed.HasValue)
                    .ToList() ?? new List<CanonicalTroopInstance>();

                int matched = 0;
                var unmatchedSamples = new List<string>();

                foreach (CanonicalTroopInstance instance in exportedMissionParticipants)
                {
                    int seed = instance.CampaignTroopDescriptorSeed ?? 0;
                    if (seed > 0 && liveIndex.TryGetValue(seed, out LiveDescriptorBinding binding))
                    {
                        matched++;
                        continue;
                    }

                    if (unmatchedSamples.Count < 8)
                    {
                        unmatchedSamples.Add(
                            (instance.InstanceId ?? "unknown") +
                            " seed=" + seed +
                            " entry=" + (instance.EntryId ?? "unknown") +
                            " party=" + (instance.PartyId ?? "unknown"));
                    }
                }

                ModLogger.Info(
                    "CampaignFieldBattleImportBridge: live descriptor rebind probe. " +
                    "BattleId=" + (contract.Context?.BattleId ?? "unknown") +
                    " ExportedMissionParticipants=" + exportedMissionParticipants.Count +
                    " LiveDescriptorIndex=" + liveIndex.Count +
                    " Matched=" + matched +
                    " Unmatched=" + Math.Max(0, exportedMissionParticipants.Count - matched) +
                    (unmatchedSamples.Count > 0
                        ? " UnmatchedSamples=[" + string.Join("; ", unmatchedSamples) + "]"
                        : string.Empty) +
                    ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CampaignFieldBattleImportBridge: live descriptor rebind probe failed: " + ex.Message);
            }
        }

        private static Dictionary<int, LiveDescriptorBinding> BuildLiveDescriptorIndex()
        {
            var index = new Dictionary<int, LiveDescriptorBinding>();
            object battle = TryGetCurrentBattleObject();
            if (battle == null)
                return index;

            AddLiveSideDescriptors(index, TryGetPropertyValue(battle, "AttackerSide"), "attacker");
            AddLiveSideDescriptors(index, TryGetPropertyValue(battle, "DefenderSide"), "defender");
            return index;
        }

        private static void AddLiveSideDescriptors(
            IDictionary<int, LiveDescriptorBinding> index,
            object sideObject,
            string sideId)
        {
            if (index == null || sideObject == null)
                return;

            TryInvokeMethod(sideObject, "MakeReadyForMission", null);
            foreach (object descriptor in TryGetMissionReadyTroopDescriptors(sideObject))
            {
                int? seed = TryGetDescriptorSeed(descriptor);
                if (!seed.HasValue || seed.Value <= 0 || index.ContainsKey(seed.Value))
                    continue;

                object readyTroop =
                    TryInvokeMethod(sideObject, "GetReadyTroop", descriptor) ??
                    TryInvokeMethod(sideObject, "GetAllocatedTroop", descriptor);
                object readyParty =
                    TryInvokeMethod(sideObject, "GetReadyTroopParty", descriptor) ??
                    TryInvokeMethod(sideObject, "GetAllocatedTroopParty", descriptor);

                index[seed.Value] = new LiveDescriptorBinding
                {
                    DescriptorSeed = seed.Value,
                    SideId = sideId,
                    PartyId = TryGetStringId(readyParty),
                    CharacterId = TryGetStringId(readyTroop)
                };
            }
        }

        private static object TryGetCurrentBattleObject()
        {
            object currentEncounter = PlayerEncounter.Current;
            if (currentEncounter == null)
                return null;

            return TryGetPropertyValue(currentEncounter, "Battle") ??
                   TryGetPropertyValue(currentEncounter, "_mapEvent");
        }

        private static List<object> TryGetMissionReadyTroopDescriptors(object sideObject)
        {
            var descriptors = new List<object>();
            if (sideObject == null)
                return descriptors;

            try
            {
                MethodInfo getAllTroopsMethod = sideObject
                    .GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(method =>
                    {
                        if (!string.Equals(method.Name, "GetAllTroops", StringComparison.Ordinal))
                            return false;

                        ParameterInfo[] parameters = method.GetParameters();
                        return parameters.Length == 1;
                    });
                if (getAllTroopsMethod == null)
                    return descriptors;

                object[] arguments = { null };
                getAllTroopsMethod.Invoke(sideObject, arguments);
                if (!(arguments[0] is IEnumerable enumerable) || arguments[0] is string)
                    return descriptors;

                foreach (object descriptor in enumerable)
                {
                    if (descriptor != null)
                        descriptors.Add(descriptor);
                }
            }
            catch
            {
            }

            return descriptors;
        }

        private static int? TryGetDescriptorSeed(object descriptor)
        {
            if (descriptor == null)
                return null;

            try
            {
                object value = TryGetPropertyValue(descriptor, "UniqueSeed");
                if (value == null)
                    return null;

                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private static object TryInvokeMethod(object instance, string methodName, params object[] args)
        {
            if (instance == null || string.IsNullOrWhiteSpace(methodName))
                return null;

            try
            {
                MethodInfo method = instance
                    .GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(candidate =>
                        string.Equals(candidate.Name, methodName, StringComparison.Ordinal) &&
                        candidate.GetParameters().Length == (args?.Length ?? 0));
                return method?.Invoke(instance, args);
            }
            catch
            {
                return null;
            }
        }

        private static object TryGetPropertyValue(object instance, string propertyName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(propertyName))
                return null;

            try
            {
                PropertyInfo property = instance.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (property != null)
                    return property.GetValue(instance, null);

                FieldInfo field = instance.GetType().GetField(
                    propertyName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return field?.GetValue(instance);
            }
            catch
            {
                return null;
            }
        }

        private static string TryGetStringId(object instance)
        {
            object value = TryGetPropertyValue(instance, "StringId");
            return value?.ToString();
        }
    }
}
