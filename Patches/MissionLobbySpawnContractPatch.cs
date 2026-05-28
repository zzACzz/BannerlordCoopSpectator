using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    internal static class MissionLobbySpawnContractPatch
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo target = typeof(MissionLobbyComponent).GetMethod(
                    nameof(MissionLobbyComponent.GetSpawnPeriodDurationForPeer),
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: new[] { typeof(MissionPeer) },
                    modifiers: null);
                MethodInfo prefix = typeof(MissionLobbySpawnContractPatch).GetMethod(
                    nameof(GetSpawnPeriodDurationForPeer_Prefix),
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (target == null || prefix == null)
                {
                    ModLogger.Info("MissionLobbySpawnContractPatch: target or prefix not found. Skip.");
                    return;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                ModLogger.Info("MissionLobbySpawnContractPatch: patched MissionLobbyComponent.GetSpawnPeriodDurationForPeer.");
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionLobbySpawnContractPatch.Apply failed: " + ex.Message);
            }
        }

        private static bool GetSpawnPeriodDurationForPeer_Prefix(MissionPeer peer, ref int __result)
        {
            try
            {
                Mission mission = Mission.Current;
                if (!ShouldUseListedShellPassiveSpawnContract(mission))
                    return true;

                __result = ListedShellPassiveSpawningBehavior.ResolveRespawnPeriodForPeer(mission, peer);
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionLobbySpawnContractPatch: prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static bool ShouldUseListedShellPassiveSpawnContract(Mission mission)
        {
            if (mission == null)
                return false;

            return mission.GetMissionBehavior<ListedShellCompatibilityMode>() != null ||
                mission.GetMissionBehavior<ListedShellCompatibilityModeClient>() != null;
        }
    }
}
