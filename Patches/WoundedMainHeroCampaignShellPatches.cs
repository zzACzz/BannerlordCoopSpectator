using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    internal static class WoundedMainHeroCampaignShellPatches
    {
        [HarmonyPatch]
        internal static class EncounterAttackConditionPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(MenuHelper),
                    nameof(MenuHelper.EncounterAttackCondition),
                    new[] { typeof(MenuCallbackArgs) });
            }

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                MethodInfo woundedGetter = AccessTools.PropertyGetter(typeof(Hero), nameof(Hero.IsWounded));
                MethodInfo effectiveWoundedMethod = AccessTools.Method(
                    typeof(WoundedMainHeroCampaignShellState),
                    nameof(WoundedMainHeroCampaignShellState.ShouldTreatMainHeroAsWoundedForEncounterAttack));
                if (woundedGetter == null || effectiveWoundedMethod == null)
                    throw new InvalidOperationException("Required wounded-main-hero methods were not found.");

                int replacedCalls = 0;
                foreach (CodeInstruction instruction in instructions)
                {
                    if (instruction.Calls(woundedGetter))
                    {
                        var replacement = new CodeInstruction(instruction)
                        {
                            opcode = OpCodes.Call,
                            operand = effectiveWoundedMethod
                        };
                        replacedCalls++;
                        yield return replacement;
                    }
                    else
                    {
                        yield return instruction;
                    }
                }

                if (replacedCalls != 2)
                {
                    throw new InvalidOperationException(
                        "EncounterAttackCondition expected exactly 2 Hero.IsWounded calls, observed " +
                        replacedCalls + ".");
                }

                ModLogger.Info(
                    "WoundedMainHeroCampaignShellPatches: patched both EncounterAttackCondition wounded checks.");
            }
        }

        [HarmonyPatch]
        internal static class MissionOpenPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(MissionState),
                    nameof(MissionState.OpenNew),
                    new[]
                    {
                        typeof(string),
                        typeof(MissionInitializerRecord),
                        typeof(InitializeMissionBehaviorsDelegate),
                        typeof(bool),
                        typeof(bool)
                    });
            }

            private static void Prefix(string missionName)
            {
                WoundedMainHeroCampaignShellState.BeginMissionOpen(missionName);
            }

            private static void Postfix(Mission __result)
            {
                WoundedMainHeroCampaignShellState.BindOpenedMission(__result);
            }

            private static Exception Finalizer(Exception __exception)
            {
                if (__exception != null)
                {
                    WoundedMainHeroCampaignShellState.AbortMissionOpen(
                        "MissionState.OpenNew exception: " + __exception.GetType().Name);
                }

                return __exception;
            }
        }

        [HarmonyPatch]
        internal static class TroopEligibilityPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(DefaultTroopSupplierProbabilityModel),
                    "CanTroopJoinBattle",
                    new[] { typeof(FlattenedTroopRosterElement), typeof(bool) });
            }

            private static bool Prefix(
                FlattenedTroopRosterElement troopRoster,
                bool includePlayer,
                ref bool __result)
            {
                if (!WoundedMainHeroCampaignShellState.ShouldForceMainHeroTroopEligible(
                        troopRoster,
                        includePlayer))
                {
                    return true;
                }

                __result = true;
                return false;
            }
        }

        [HarmonyPatch]
        internal static class OriginWritebackPatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(
                    typeof(PartyGroupAgentOrigin),
                    nameof(PartyGroupAgentOrigin.SetWounded));
                yield return AccessTools.Method(
                    typeof(PartyGroupAgentOrigin),
                    nameof(PartyGroupAgentOrigin.SetKilled));
                yield return AccessTools.Method(
                    typeof(PartyGroupAgentOrigin),
                    nameof(PartyGroupAgentOrigin.OnAgentRemoved),
                    new[] { typeof(float) });
            }

            private static bool Prefix(PartyGroupAgentOrigin __instance)
            {
                return !WoundedMainHeroCampaignShellState.ShouldSuppressVirtualMainHeroWriteback(__instance);
            }
        }

        [HarmonyPatch]
        internal static class MissionEndPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(Mission), "EndMissionInternal");
            }

            private static void Postfix(Mission __instance)
            {
                WoundedMainHeroCampaignShellState.CompleteMission(
                    __instance,
                    "Mission.EndMissionInternal postfix");
            }
        }
    }
}
