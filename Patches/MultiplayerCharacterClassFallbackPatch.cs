using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    public static class MultiplayerCharacterClassFallbackPatch
    {
        private static readonly HashSet<string> LoggedFallbackKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> LoggedTroopKeys =
            new HashSet<string>(StringComparer.Ordinal);

        public static void Apply(Harmony harmony)
        {
            if (!ExperimentalFeatures.EnableCampaignCharacterMpHeroClassFallback)
                return;

            try
            {
                MethodInfo getHeroClassTarget = AccessTools.Method(
                    typeof(MultiplayerClassDivisions),
                    "GetMPHeroClassForCharacter",
                    new[] { typeof(BasicCharacterObject) });
                MethodInfo getHeroClassPostfix = typeof(MultiplayerCharacterClassFallbackPatch).GetMethod(
                    nameof(GetMPHeroClassForCharacter_Postfix),
                    BindingFlags.Static | BindingFlags.NonPublic);

                MethodInfo isTroopCharacterTarget = AccessTools.Method(
                    typeof(MultiplayerClassDivisions.MPHeroClass),
                    "IsTroopCharacter",
                    new[] { typeof(BasicCharacterObject) });
                MethodInfo isTroopCharacterPostfix = typeof(MultiplayerCharacterClassFallbackPatch).GetMethod(
                    nameof(IsTroopCharacter_Postfix),
                    BindingFlags.Static | BindingFlags.NonPublic);

                if (getHeroClassTarget == null || getHeroClassPostfix == null ||
                    isTroopCharacterTarget == null || isTroopCharacterPostfix == null)
                {
                    ModLogger.Info("MultiplayerCharacterClassFallbackPatch: targets not found. Skip.");
                    return;
                }

                harmony.Patch(getHeroClassTarget, postfix: new HarmonyMethod(getHeroClassPostfix));
                harmony.Patch(isTroopCharacterTarget, postfix: new HarmonyMethod(isTroopCharacterPostfix));
                ModLogger.Info("MultiplayerCharacterClassFallbackPatch: postfixes applied.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("MultiplayerCharacterClassFallbackPatch.Apply failed.", ex);
            }
        }

        private static void GetMPHeroClassForCharacter_Postfix(
            BasicCharacterObject character,
            ref MultiplayerClassDivisions.MPHeroClass __result)
        {
            if (__result != null || character == null)
                return;

            try
            {
                if (!CampaignMultiplayerHeroClassResolver.TryResolve(
                        character,
                        out MultiplayerClassDivisions.MPHeroClass resolvedClass,
                        out bool treatAsTroop,
                        out string diagnostics) ||
                    resolvedClass == null)
                {
                    return;
                }

                __result = resolvedClass;
                string logKey = (character.StringId ?? "null") + "|" + resolvedClass.StringId + "|" + treatAsTroop;
                if (LoggedFallbackKeys.Add(logKey))
                {
                    ModLogger.Info(
                        "MultiplayerCharacterClassFallbackPatch: supplied surrogate MPHeroClass for original campaign character. " +
                        "Character=" + (character.StringId ?? "null") +
                        " HeroClass=" + resolvedClass.StringId +
                        " TreatAsTroop=" + treatAsTroop +
                        " Diagnostics=" + diagnostics);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("MultiplayerCharacterClassFallbackPatch: GetMPHeroClassForCharacter postfix failed: " + ex.Message);
            }
        }

        private static void IsTroopCharacter_Postfix(
            MultiplayerClassDivisions.MPHeroClass __instance,
            BasicCharacterObject character,
            ref bool __result)
        {
            if (__result || __instance == null || character == null)
                return;

            try
            {
                if (!CampaignMultiplayerHeroClassResolver.MatchesTroopClass(character, __instance, out string diagnostics))
                    return;

                __result = true;
                string logKey = (character.StringId ?? "null") + "|" + __instance.StringId;
                if (LoggedTroopKeys.Add(logKey))
                {
                    ModLogger.Info(
                        "MultiplayerCharacterClassFallbackPatch: treated original campaign character as troop member of surrogate MPHeroClass. " +
                        "Character=" + (character.StringId ?? "null") +
                        " HeroClass=" + __instance.StringId +
                        " Diagnostics=" + diagnostics);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("MultiplayerCharacterClassFallbackPatch: IsTroopCharacter postfix failed: " + ex.Message);
            }
        }
    }
}
