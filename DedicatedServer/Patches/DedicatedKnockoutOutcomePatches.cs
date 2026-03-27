using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Dedicated-side low-level outcome patch for coop campaign battles.
    /// In vanilla campaign, blunt damage without CanKillEvenIfBlunt resolves to unconscious/wounded.
    /// In the current MP/TDM flow, outcomes collapse to Killed before our late reconcile sees them.
    /// This patch restores a campaign-like unconscious outcome at the AgentDecideKilledOrUnconsciousModel layer.
    /// </summary>
    public static class DedicatedKnockoutOutcomePatches
    {
        private const int MaxForcedOutcomeLogs = 32;

        private static int _lastLoggedMissionIdentity;
        private static string _lastLoggedModelType;
        private static int _forcedOutcomeLogCount;

        public static void Apply(Harmony harmony)
        {
            TryApplyPatch("MissionGameModels.AgentDecideKilledOrUnconsciousModel getter", () => PatchMissionGameModelsGetter(harmony));
            TryApplyPatch(typeof(DefaultAgentDecideKilledOrUnconsciousModel).FullName + ".GetAgentStateProbability", () => PatchDecisionModel(harmony, typeof(DefaultAgentDecideKilledOrUnconsciousModel)));
            TryApplyPatch("SandBox.GameComponents.SandboxAgentDecideKilledOrUnconsciousModel.GetAgentStateProbability", () => PatchDecisionModel(harmony, AccessTools.TypeByName("SandBox.GameComponents.SandboxAgentDecideKilledOrUnconsciousModel")));
        }

        private static void TryApplyPatch(string patchLabel, Action apply)
        {
            try
            {
                apply?.Invoke();
            }
            catch (Exception ex)
            {
                ModLogger.Error("DedicatedKnockoutOutcomePatches: patch apply failed for " + (patchLabel ?? "unknown") + ".", ex);
            }
        }

        private static void PatchMissionGameModelsGetter(Harmony harmony)
        {
            MethodInfo getter = AccessTools.PropertyGetter(typeof(MissionGameModels), nameof(MissionGameModels.AgentDecideKilledOrUnconsciousModel));
            if (getter == null)
            {
                ModLogger.Info("DedicatedKnockoutOutcomePatches: MissionGameModels.AgentDecideKilledOrUnconsciousModel getter not found. Skip getter log patch.");
                return;
            }

            MethodInfo postfix = typeof(DedicatedKnockoutOutcomePatches).GetMethod(
                nameof(AgentDecideKilledOrUnconsciousModelGetter_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);

            if (postfix == null)
            {
                ModLogger.Info("DedicatedKnockoutOutcomePatches: getter postfix method not found. Skip getter log patch.");
                return;
            }

            harmony.Patch(getter, postfix: new HarmonyMethod(postfix));
            ModLogger.Info("DedicatedKnockoutOutcomePatches: patched MissionGameModels.AgentDecideKilledOrUnconsciousModel getter.");
        }

        private static void PatchDecisionModel(Harmony harmony, Type modelType)
        {
            if (modelType == null)
                return;

            MethodInfo target = AccessTools.Method(
                modelType,
                "GetAgentStateProbability",
                new[]
                {
                    typeof(Agent),
                    typeof(Agent),
                    typeof(DamageTypes),
                    typeof(WeaponFlags),
                    typeof(float).MakeByRefType()
                });

            if (target == null)
            {
                ModLogger.Info("DedicatedKnockoutOutcomePatches: " + modelType.FullName + ".GetAgentStateProbability not found. Skip.");
                return;
            }

            MethodInfo postfix = typeof(DedicatedKnockoutOutcomePatches).GetMethod(
                nameof(GetAgentStateProbability_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);

            if (postfix == null)
            {
                ModLogger.Info("DedicatedKnockoutOutcomePatches: outcome postfix method not found. Skip " + modelType.FullName + ".");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            ModLogger.Info("DedicatedKnockoutOutcomePatches: patched " + modelType.FullName + ".GetAgentStateProbability.");
        }

        private static void AgentDecideKilledOrUnconsciousModelGetter_Postfix(object __result)
        {
            try
            {
                Mission mission = Mission.Current;
                if (mission == null)
                    return;

                int missionIdentity = RuntimeHelpers.GetHashCode(mission);
                string modelType = __result?.GetType().FullName ?? "<null>";
                if (missionIdentity == _lastLoggedMissionIdentity && string.Equals(modelType, _lastLoggedModelType, StringComparison.Ordinal))
                    return;

                _lastLoggedMissionIdentity = missionIdentity;
                _lastLoggedModelType = modelType;

                ModLogger.Info(
                    "DedicatedKnockoutOutcomePatches: MissionGameModels.AgentDecideKilledOrUnconsciousModel=" + modelType +
                    " MissionType=" + mission.GetType().FullName +
                    " Scene=" + (mission.SceneName ?? "<null>") +
                    " HasCoopSpawnLogic=" + (mission.GetMissionBehavior<CoopMissionSpawnLogic>() != null) +
                    " SnapshotReady=" + (BattleSnapshotRuntimeState.GetCurrent() != null));
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedKnockoutOutcomePatches: getter postfix failed: " + ex.Message);
            }
        }

        private static void GetAgentStateProbability_Postfix(
            object __instance,
            Agent affectorAgent,
            Agent effectedAgent,
            DamageTypes damageType,
            WeaponFlags weaponFlags,
            ref float useSurgeryProbability,
            ref float __result)
        {
            try
            {
                if (!ShouldForceCampaignLikeUnconscious(effectedAgent, damageType, weaponFlags))
                    return;

                float originalProbability = __result;
                float originalSurgeryProbability = useSurgeryProbability;

                __result = 0f;
                useSurgeryProbability = 0f;

                if (_forcedOutcomeLogCount < MaxForcedOutcomeLogs)
                {
                    _forcedOutcomeLogCount++;
                    ModLogger.Info(
                        "DedicatedKnockoutOutcomePatches: forced blunt knockout to unconscious. " +
                        "Model=" + (__instance?.GetType().FullName ?? "<null>") +
                        " Victim=" + BuildAgentLabel(effectedAgent) +
                        " Attacker=" + BuildAgentLabel(affectorAgent) +
                        " DamageType=" + damageType +
                        " WeaponFlags=" + weaponFlags +
                        " OriginalProbability=" + originalProbability.ToString("0.###") +
                        " OriginalSurgeryProbability=" + originalSurgeryProbability.ToString("0.###"));
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedKnockoutOutcomePatches: outcome postfix failed: " + ex.Message);
            }
        }

        private static bool ShouldForceCampaignLikeUnconscious(Agent effectedAgent, DamageTypes damageType, WeaponFlags weaponFlags)
        {
            if (!GameNetwork.IsServer)
                return false;

            Mission mission = Mission.Current;
            if (mission == null)
                return false;

            if (mission.GetMissionBehavior<CoopMissionSpawnLogic>() == null)
                return false;

            if (BattleSnapshotRuntimeState.GetCurrent() == null)
                return false;

            if (effectedAgent == null || !effectedAgent.IsHuman || effectedAgent.IsMount)
                return false;

            if (damageType != DamageTypes.Blunt)
                return false;

            return (weaponFlags & WeaponFlags.CanKillEvenIfBlunt) == 0;
        }

        private static string BuildAgentLabel(Agent agent)
        {
            if (agent == null)
                return "<null>";

            string name = agent.Name?.ToString();
            if (string.IsNullOrWhiteSpace(name))
                name = agent.Character?.Name?.ToString();
            if (string.IsNullOrWhiteSpace(name))
                name = "agent";

            return name + "#" + agent.Index + " State=" + agent.State + " HP=" + agent.Health.ToString("0.##");
        }
    }
}
