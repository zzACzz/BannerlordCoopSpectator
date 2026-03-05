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

        /// <summary>Увімкнути reflection dump API GameTextManager та двофазну валідацію ключа (BeforeLoad/AfterLoad).</summary>
        public static bool DebugTexts => GetEnvBool(EnvDebugTexts);

        /// <summary>Увімкнути збір stdout/stderr процесу Dedicated Helper у файл (dedicated_stdout.log).</summary>
        public static bool DebugDedicatedStdio => GetEnvBool(EnvDebugDedicatedStdio);

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
