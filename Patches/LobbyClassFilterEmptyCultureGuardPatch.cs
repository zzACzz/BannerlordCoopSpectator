using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    internal static class LobbyClassFilterEmptyCultureGuardPatch
    {
        private static readonly HashSet<string> LoggedCultures =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> LoggedArmoryNullReferenceMethods =
            new HashSet<string>(StringComparer.Ordinal);

        public static void Apply(Harmony harmony)
        {
            try
            {
                Type factionItemType = AccessTools.TypeByName(
                    "TaleWorlds.MountAndBlade.Multiplayer.ViewModelCollection.Lobby.ClassFilter.MPLobbyClassFilterFactionItemVM");
                if (factionItemType == null)
                {
                    ModLogger.Info("LobbyClassFilterEmptyCultureGuardPatch: faction item type not found. Skip.");
                    return;
                }

                MethodInfo target = AccessTools.Method(
                    factionItemType,
                    "CreateClassGroupAndClasses",
                    new[] { typeof(BasicCultureObject) });
                MethodInfo prefix = typeof(LobbyClassFilterEmptyCultureGuardPatch).GetMethod(
                    nameof(CreateClassGroupAndClasses_Prefix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (target == null || prefix == null)
                {
                    ModLogger.Info("LobbyClassFilterEmptyCultureGuardPatch: target or prefix not found. Skip.");
                    return;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                ModLogger.Info("LobbyClassFilterEmptyCultureGuardPatch: prefix applied to MPLobbyClassFilterFactionItemVM.CreateClassGroupAndClasses.");
                PatchArmoryFinalizer(harmony, "OnSelectedClassChanged");
                PatchArmoryFinalizer(harmony, "RefreshPlayerData");
            }
            catch (Exception ex)
            {
                ModLogger.Error("LobbyClassFilterEmptyCultureGuardPatch.Apply failed.", ex);
            }
        }

        private static void PatchArmoryFinalizer(Harmony harmony, string methodName)
        {
            Type armoryType = AccessTools.TypeByName(
                "TaleWorlds.MountAndBlade.Multiplayer.ViewModelCollection.Lobby.Armory.MPArmoryVM");
            if (armoryType == null)
            {
                ModLogger.Info("LobbyClassFilterEmptyCultureGuardPatch: MPArmoryVM type not found. Skip.");
                return;
            }

            MethodInfo target = AccessTools.Method(armoryType, methodName);
            MethodInfo finalizer = typeof(LobbyClassFilterEmptyCultureGuardPatch).GetMethod(
                nameof(SwallowArmoryNullReference),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || finalizer == null)
            {
                ModLogger.Info("LobbyClassFilterEmptyCultureGuardPatch: armory target not found. Method=" + methodName);
                return;
            }

            harmony.Patch(target, finalizer: new HarmonyMethod(finalizer));
            ModLogger.Info("LobbyClassFilterEmptyCultureGuardPatch: finalizer applied to MPArmoryVM." + methodName);
        }

        private static bool CreateClassGroupAndClasses_Prefix(object __instance, BasicCultureObject culture)
        {
            try
            {
                if (__instance == null)
                    return true;

                bool hasAnyHeroClass = culture != null &&
                                       MultiplayerClassDivisions.GetMPHeroClasses(culture)?.Any() == true;
                if (hasAnyHeroClass)
                    return true;

                Type instanceType = __instance.GetType();
                Type classGroupItemType = AccessTools.TypeByName(
                    "TaleWorlds.MountAndBlade.Multiplayer.ViewModelCollection.Lobby.ClassFilter.MPLobbyClassFilterClassGroupItemVM");
                if (classGroupItemType == null)
                    return true;

                Type classGroupListType = typeof(MBBindingList<>).MakeGenericType(classGroupItemType);
                object emptyClassGroups = Activator.CreateInstance(classGroupListType);
                Type dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(string), classGroupItemType);
                object emptyDictionary = Activator.CreateInstance(dictionaryType);

                SetInstanceMember(instanceType, __instance, "ClassGroups", emptyClassGroups);
                SetInstanceMember(instanceType, __instance, "_classGroupDictionary", emptyDictionary);
                SetInstanceMember(instanceType, __instance, "SelectedClassItem", null);
                SetInstanceMember(instanceType, __instance, "IsEnabled", false);
                SetInstanceMember(instanceType, __instance, "IsActive", false);

                string cultureId = culture?.StringId ?? "null";
                if (LoggedCultures.Add(cultureId))
                {
                    ModLogger.Info(
                        "LobbyClassFilterEmptyCultureGuardPatch: disabled empty lobby class-filter culture. " +
                        "Culture=" + cultureId +
                        " Groups=" + GetCount(emptyClassGroups));
                }

                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("LobbyClassFilterEmptyCultureGuardPatch: prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static void SetInstanceMember(Type instanceType, object instance, string memberName, object value)
        {
            PropertyInfo property = instanceType.GetProperty(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite)
            {
                property.SetValue(instance, value);
                return;
            }

            FieldInfo field = instanceType.GetField(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            field?.SetValue(instance, value);
        }

        private static int GetCount(object collection)
        {
            return collection is ICollection countable ? countable.Count : 0;
        }

        private static Exception SwallowArmoryNullReference(Exception __exception, MethodBase __originalMethod)
        {
            if (__exception == null)
                return null;

            if (!(__exception is NullReferenceException))
                return __exception;

            string methodName =
                (__originalMethod?.DeclaringType?.FullName ?? "unknown") +
                "." +
                (__originalMethod?.Name ?? "unknown");
            if (LoggedArmoryNullReferenceMethods.Add(methodName))
            {
                ModLogger.Error(
                    "LobbyClassFilterEmptyCultureGuardPatch: swallowed lobby armory null reference in " + methodName,
                    __exception);
            }

            return null;
        }
    }
}
