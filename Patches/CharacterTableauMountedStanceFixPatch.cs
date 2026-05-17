using System;
using System.Collections.Concurrent;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade.View.Tableaus;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Normalizes mounted preview stance directly inside CharacterTableau.
    /// Vanila preview VMs often provide mount equipment with stance None (0),
    /// which makes CharacterTableau build a horse + foot preview hybrid.
    /// </summary>
    public static class CharacterTableauMountedStanceFixPatch
    {
        private const int MountedStanceIndex = 4;

        private static readonly BindingFlags AnyInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly ConcurrentDictionary<string, byte> OnceKeys =
            new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        private static readonly FieldInfo EquipmentField =
            typeof(CharacterTableau).GetField("_equipment", AnyInstance);
        private static readonly FieldInfo StanceIndexField =
            typeof(CharacterTableau).GetField("_stanceIndex", AnyInstance);
        private static readonly FieldInfo CharStringIdField =
            typeof(CharacterTableau).GetField("_charStringId", AnyInstance);
        private static readonly FieldInfo MountCreationKeyField =
            typeof(CharacterTableau).GetField("_mountCreationKey", AnyInstance);

        public static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo target = AccessTools.Method(typeof(CharacterTableau), "RefreshCharacterTableau");
                MethodInfo prefix = typeof(CharacterTableauMountedStanceFixPatch).GetMethod(
                    nameof(CharacterTableau_RefreshCharacterTableau_Prefix),
                    BindingFlags.Static | BindingFlags.NonPublic);

                if (target == null || prefix == null)
                {
                    ModLogger.Info("CharacterTableauMountedStanceFixPatch: target not found. Skip.");
                    return;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                ModLogger.Info("CharacterTableauMountedStanceFixPatch: applied.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("CharacterTableauMountedStanceFixPatch.Apply failed.", ex);
            }
        }

        private static void CharacterTableau_RefreshCharacterTableau_Prefix(CharacterTableau __instance)
        {
            try
            {
                if (EquipmentField == null || StanceIndexField == null)
                    return;

                Equipment equipment = (Equipment)EquipmentField.GetValue(__instance);
                if (equipment == null)
                    return;

                EquipmentElement horseElement = equipment[(EquipmentIndex)10];
                if (horseElement.Item?.HorseComponent == null)
                    return;

                object currentStance = StanceIndexField.GetValue(__instance);
                int currentStanceValue = Convert.ToInt32(currentStance);
                bool appliedMountedStance = false;
                if (currentStanceValue == 0)
                {
                    Type stanceType = StanceIndexField.FieldType;
                    object mountedStance = Enum.ToObject(stanceType, MountedStanceIndex);
                    StanceIndexField.SetValue(__instance, mountedStance);
                    appliedMountedStance = true;
                }

                if (!appliedMountedStance)
                    return;

                string charStringId = CharStringIdField?.GetValue(__instance) as string ?? "unknown";
                string mountCreationKey = MountCreationKeyField?.GetValue(__instance) as string ?? "empty";
                string horseId = horseElement.Item?.StringId ?? "empty";
                string logKey = string.Concat(
                    charStringId, "|",
                    mountCreationKey, "|",
                    horseId, "|",
                    RuntimeHelpersHash(__instance));

                LogOnce(
                    logKey,
                    "CharacterTableauMountedStanceFixPatch: normalized mounted preview contract. " +
                    "Instance=" + RuntimeHelpersHash(__instance) +
                    " CharStringId=" + charStringId +
                    " Horse=" + horseId +
                    " MountCreationKey=" + mountCreationKey +
                    " PreviousStance=" + currentStanceValue +
                    " NewStance=" + Convert.ToInt32(StanceIndexField.GetValue(__instance)) +
                    " AppliedStance=" + appliedMountedStance);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CharacterTableauMountedStanceFixPatch: RefreshCharacterTableau prefix failed open: " + ex.Message);
            }
        }

        private static void LogOnce(string key, string message)
        {
            if (OnceKeys.TryAdd(key, 0))
                ModLogger.Info(message);
        }

        private static int RuntimeHelpersHash(object instance)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(instance);
        }
    }
}
