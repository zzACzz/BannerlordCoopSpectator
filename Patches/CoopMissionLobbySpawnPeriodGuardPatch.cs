using System;
using System.Reflection;
using CoopSpectator.Infrastructure.Hideout;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Keeps the native lobby death bookkeeping available in isolated daytime and
    /// nighttime hideouts while supplying the spawn-period value that their minimal
    /// mission stacks deliberately cannot obtain from a native SpawnComponent.
    /// </summary>
    public static class CoopMissionLobbySpawnPeriodGuardPatch
    {
        private static readonly object ApplyLock = new object();
        private static bool _applied;

        public static void Apply(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));

            lock (ApplyLock)
            {
                if (_applied)
                    return;

                MethodInfo target = AccessTools.Method(
                    typeof(MissionLobbyComponent),
                    nameof(MissionLobbyComponent.GetSpawnPeriodDurationForPeer),
                    new[] { typeof(MissionPeer) });
                MethodInfo prefix = AccessTools.Method(
                    typeof(CoopMissionLobbySpawnPeriodGuardPatch),
                    nameof(MissionLobbyComponent_GetSpawnPeriodDurationForPeer_Prefix));
                if (target == null || prefix == null)
                {
                    throw new MissingMethodException(
                        "Unable to resolve the isolated hideout spawn-period guard target.");
                }

                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                _applied = true;
            }
        }

        private static bool MissionLobbyComponent_GetSpawnPeriodDurationForPeer_Prefix(
            ref int __result)
        {
            Mission mission = Mission.Current;
            bool hasIsolatedHideoutController =
                mission?.GetMissionBehavior<CoopExactCampaignHideoutMissionController>() != null;
            bool hasSpawnComponent = mission?.GetMissionBehavior<SpawnComponent>() != null;
            if (!CoopHideoutAmbushContract.ShouldUseMissingSpawnComponentFallback(
                    GameNetwork.IsServer,
                    hasIsolatedHideoutController,
                    hasSpawnComponent))
            {
                return true;
            }

            __result = 0;
            return false;
        }
    }
}
