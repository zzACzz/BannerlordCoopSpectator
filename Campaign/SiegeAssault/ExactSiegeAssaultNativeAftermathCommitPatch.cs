using System;
using CoopSpectator.Campaign.Hideout;
using CoopSpectator.Campaign.SiegeAmbush;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.CampaignSystem.MapEvents;

namespace CoopSpectator.Campaign.SiegeAssault
{
    [HarmonyPatch(typeof(MapEvent), "CalculateAndCommitMapEventResults")]
    internal static class ExactSiegeAssaultNativeAftermathCommitPatch
    {
        private static void Prefix(MapEvent __instance)
        {
            try
            {
                if (ExactSiegeAmbushNativeAftermathRuntime.TryBeginNativeCalculation(
                        __instance,
                        out string diagnostics))
                {
                    ModLogger.Info(
                        "ExactSiegeAssaultNativeAftermathCommitPatch: prepared decisive siege-ambush winner immediately before native map-event results. " +
                        diagnostics + ".");
                }
            }
            catch (Exception ex)
            {
                string rollbackDiagnostics = "rollback-not-attempted";
                try
                {
                    ExactSiegeAmbushNativeAftermathRuntime.TryRollback(
                        __instance,
                        resultId: null,
                        out rollbackDiagnostics);
                }
                catch (Exception rollbackException)
                {
                    rollbackDiagnostics =
                        "rollback-failed:" + rollbackException.GetType().Name +
                        ":" + rollbackException.Message;
                }

                ModLogger.Info(
                    "ExactSiegeAssaultNativeAftermathCommitPatch: failed to prepare decisive siege-ambush winner before native map-event results. " +
                    "Rollback={" + rollbackDiagnostics + "} " +
                    "Error=" + ex.GetType().Name + ":" + ex.Message + ".");
            }
        }

        private static Exception Finalizer(
            MapEvent __instance,
            Exception __exception)
        {
            try
            {
                if (__exception == null)
                {
                    if (ExactSiegeAssaultNativeAftermathRuntime.TryCommit(
                            __instance,
                            out string commitDiagnostics))
                    {
                        ModLogger.Info(
                            "ExactSiegeAssaultNativeAftermathCommitPatch: committed exact siege casualty ledgers after native map-event results. " +
                            commitDiagnostics + ".");
                    }

                    if (ExactSiegeAmbushNativeAftermathRuntime.TryCommit(
                            __instance,
                            out string siegeAmbushCommitDiagnostics))
                    {
                        ModLogger.Info(
                            "ExactSiegeAssaultNativeAftermathCommitPatch: committed exact siege-ambush casualty ledgers after native map-event results. " +
                            siegeAmbushCommitDiagnostics + ".");
                    }

                    if (ExactHideoutNativeAftermathRuntime.TryCommit(
                            __instance,
                            out string hideoutCommitDiagnostics))
                    {
                        ModLogger.Info(
                            "ExactSiegeAssaultNativeAftermathCommitPatch: committed exact hideout casualty ledgers after native map-event results. " +
                            hideoutCommitDiagnostics + ".");
                    }
                }
                else
                {
                    if (ExactSiegeAssaultNativeAftermathRuntime.TryRollback(
                            __instance,
                            resultId: null,
                            out string rollbackDiagnostics))
                    {
                        ModLogger.Info(
                            "ExactSiegeAssaultNativeAftermathCommitPatch: rolled back exact siege casualty ledgers because native map-event results faulted. " +
                            rollbackDiagnostics +
                            " Error=" + __exception.GetType().Name + ":" + __exception.Message + ".");
                    }

                    if (ExactSiegeAmbushNativeAftermathRuntime.TryRollback(
                            __instance,
                            resultId: null,
                            out string siegeAmbushRollbackDiagnostics))
                    {
                        ModLogger.Info(
                            "ExactSiegeAssaultNativeAftermathCommitPatch: rolled back exact siege-ambush casualty ledgers and winner because native map-event results faulted. " +
                            siegeAmbushRollbackDiagnostics +
                            " Error=" + __exception.GetType().Name + ":" + __exception.Message + ".");
                    }

                    if (ExactHideoutNativeAftermathRuntime.TryRollback(
                            __instance,
                            resultId: null,
                            out string hideoutRollbackDiagnostics))
                    {
                        ModLogger.Info(
                            "ExactSiegeAssaultNativeAftermathCommitPatch: rolled back exact hideout casualty ledgers because native map-event results faulted. " +
                            hideoutRollbackDiagnostics +
                            " Error=" + __exception.GetType().Name + ":" + __exception.Message + ".");
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "ExactSiegeAssaultNativeAftermathCommitPatch: failed to finalize exact siege casualty ledgers; native exception flow is preserved. " +
                    "Error=" + ex.GetType().Name + ":" + ex.Message + ".");
            }

            return __exception;
        }
    }
}
