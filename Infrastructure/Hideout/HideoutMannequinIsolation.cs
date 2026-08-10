using System;

namespace CoopSpectator.Infrastructure.Hideout
{
    internal enum HideoutMannequinIsolationMode
    {
        None = 0,
        AftermathAllOff = 1
    }

    /// <summary>
    /// Explicit, default-off isolation switch for the nighttime-hideout mannequin investigation.
    /// Behavior changes require both verbose diagnostics and the exact isolation mode value.
    /// </summary>
    internal static class HideoutMannequinIsolation
    {
        private const string IsolationEnvironmentVariable =
            "COOPSPECTATOR_HIDEOUT_MANNEQUIN_ISOLATION";
        private const string AftermathAllOffValue = "aftermath_all_off";

        private static readonly object LogSync = new object();
        private static readonly HideoutMannequinIsolationMode RequestedMode =
            ResolveRequestedMode();
        private static string _lastLoggedResultKey;

        internal static string RequestedModeName =>
            RequestedMode == HideoutMannequinIsolationMode.AftermathAllOff
                ? AftermathAllOffValue
                : "none";

        internal static bool ShouldSuppressAftermath(
            bool isCampaignHostProcess,
            bool isPlayerHideoutBattle,
            bool isNightHideoutMission,
            bool isNightHideoutSnapshot,
            bool isFinalHideoutResult,
            bool battleInstanceMatches,
            out string diagnostics)
        {
            if (RequestedMode != HideoutMannequinIsolationMode.AftermathAllOff)
            {
                diagnostics = "Mode=none";
                return false;
            }

            if (!ExperimentalFeatures.EnableVerboseDiagnostics)
            {
                diagnostics =
                    "Mode=" + AftermathAllOffValue +
                    " Rejected=verbose-diagnostics-disabled";
                return false;
            }

            bool contextAccepted =
                isCampaignHostProcess &&
                isPlayerHideoutBattle &&
                isNightHideoutMission &&
                isNightHideoutSnapshot &&
                isFinalHideoutResult &&
                battleInstanceMatches;
            diagnostics =
                "Mode=" + AftermathAllOffValue +
                " ContextAccepted=" + contextAccepted +
                " CampaignHost=" + isCampaignHostProcess +
                " PlayerHideout=" + isPlayerHideoutBattle +
                " NightMission=" + isNightHideoutMission +
                " NightSnapshot=" + isNightHideoutSnapshot +
                " FinalHideoutResult=" + isFinalHideoutResult +
                " BattleInstanceMatches=" + battleInstanceMatches;
            return contextAccepted;
        }

        internal static void LogSuppressionOnce(
            string resultKey,
            string battleInstanceId,
            string diagnostics)
        {
            string normalizedResultKey = resultKey ?? string.Empty;
            lock (LogSync)
            {
                if (string.Equals(
                        _lastLoggedResultKey,
                        normalizedResultKey,
                        StringComparison.Ordinal))
                {
                    return;
                }

                _lastLoggedResultKey = normalizedResultKey;
            }

            ModLogger.Info(
                "HideoutMannequinIsolation: suppressing nighttime-hideout campaign aftermath. " +
                "Mode=" + AftermathAllOffValue +
                " Suppressed=[MainPartyBattleResultPreview;NativeHideoutCasualtyLedger] " +
                "BattleInstanceId=" + (battleInstanceId ?? "null") +
                " ResultKey=" + (resultKey ?? "null") +
                " Diagnostics={" + (diagnostics ?? "none") + "}.");
        }

        private static HideoutMannequinIsolationMode ResolveRequestedMode()
        {
            string value = Environment.GetEnvironmentVariable(IsolationEnvironmentVariable);
            return string.Equals(
                (value ?? string.Empty).Trim(),
                AftermathAllOffValue,
                StringComparison.OrdinalIgnoreCase)
                ? HideoutMannequinIsolationMode.AftermathAllOff
                : HideoutMannequinIsolationMode.None;
        }
    }
}
