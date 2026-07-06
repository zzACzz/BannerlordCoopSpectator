using System;

namespace CoopSpectator.Infrastructure
{
    /// <summary>
    /// Перемикачі debug-режимів через змінні оточення (щоб не засмічувати логи в нормальному режимі).
    /// COOP_DEBUG_TEXTS=1 — reflection dump GameTextManager + before/after валідація ключа.
    /// COOP_DEBUG_DEDICATED_STDIO=1 — редірект stdout/stderr дочірнього процесу дедиката у файл.
    /// </summary>
    public static class CoopDebugConfig
    {
        private const string EnvDebugTexts = "COOP_DEBUG_TEXTS";
        private const string EnvDebugDedicatedStdio = "COOP_DEBUG_DEDICATED_STDIO";
        private const string EnvOrderOfBattleDiagnostics = "COOPSPECTATOR_OOB_DIAGNOSTICS";
        private const string EnvShieldBannerDiagnostics = "COOPSPECTATOR_SHIELD_BANNER_DIAGNOSTICS";

        /// <summary>Увімкнути reflection dump API GameTextManager та двофазну валідацію ключа (BeforeLoad/AfterLoad).</summary>
        public static bool DebugTexts => GetEnvBool(EnvDebugTexts);

        /// <summary>Увімкнути збір stdout/stderr процесу Dedicated Helper у файл (dedicated_stdout.log).</summary>
        public static bool DebugDedicatedStdio => GetEnvBool(EnvDebugDedicatedStdio);

        /// <summary>Enable focused Order of Battle formation/count diagnostics.</summary>
        public static bool OrderOfBattleDiagnostics => GetEnvBool(EnvOrderOfBattleDiagnostics);

        /// <summary>Enable focused shield banner materialization diagnostics.</summary>
        public static bool ShieldBannerDiagnostics => GetEnvBool(EnvShieldBannerDiagnostics);

        private static bool GetEnvBool(string name)
        {
            try
            {
                string v = Environment.GetEnvironmentVariable(name);
                return "1".Equals(v?.Trim(), StringComparison.OrdinalIgnoreCase)
                    || "true".Equals(v?.Trim(), StringComparison.OrdinalIgnoreCase)
                    || "yes".Equals(v?.Trim(), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
