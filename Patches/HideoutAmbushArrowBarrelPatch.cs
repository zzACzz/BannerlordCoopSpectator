using System;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.Hideout;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Objects.Usables;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// The authored call-troops arrow barrel overlaps the ordinary ammo barrel.
    /// Suppress only that child object's ammo interaction during the isolated
    /// cooperative night phase so the parent StealthAreaUsePoint receives input.
    /// </summary>
    internal static class HideoutAmbushArrowBarrelPatch
    {
        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
                return;

            ApplyArrowBarrelChildPatch(harmony);
        }

        private static void ApplyArrowBarrelChildPatch(Harmony harmony)
        {
            try
            {
                var original = AccessTools.Method(
                    typeof(StandingPointWithWeaponRequirement),
                    nameof(StandingPointWithWeaponRequirement.IsDisabledForAgent),
                    new[] { typeof(Agent) });
                var prefix = AccessTools.Method(
                    typeof(HideoutAmbushArrowBarrelPatch),
                    nameof(Prefix));
                if (original == null || prefix == null)
                    throw new MissingMethodException("hideout-ambush-arrow-barrel-patch-target-missing");

                harmony.Patch(original, prefix: new HarmonyMethod(prefix));
                ModLogger.Info(
                    "HideoutAmbushArrowBarrelPatch: isolated night interaction patch applied.");
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "HideoutAmbushArrowBarrelPatch: isolated night interaction patch was skipped; core patches remain active.",
                    ex);
            }
        }

        private static bool Prefix(
            StandingPointWithWeaponRequirement __instance,
            Agent agent,
            ref bool __result)
        {
            CoopHideoutAmbushState state =
                CoopHideoutAmbushNetworkController.CurrentClientState;
            if (!GameNetwork.IsClient ||
                state?.Phase != CoopHideoutAmbushPhase.Stealth ||
                agent != Agent.Main ||
                !IsAuthoredCallTroopsArrowBarrel(__instance))
            {
                return true;
            }

            __result = true;
            return false;
        }

        private static bool IsAuthoredCallTroopsArrowBarrel(
            StandingPointWithWeaponRequirement instance)
        {
            try
            {
                WeakGameEntity entity = instance.GameEntity;
                while (entity.IsValid)
                {
                    if (entity.HasTag(
                            CoopHideoutAmbushContract.CallTroopsArrowBarrelTag))
                    {
                        return true;
                    }
                    entity = entity.Parent;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
