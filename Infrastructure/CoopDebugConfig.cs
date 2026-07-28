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
        private const string EnvFieldBattleBoundaryDiagnostics = "COOPSPECTATOR_FIELD_BOUNDARY_DIAGNOSTICS";
        private const string EnvPossessionDiagnostics = "COOPSPECTATOR_POSSESSION_DIAGNOSTICS";
        private const string EnvMoraleDiagnostics = "COOPSPECTATOR_MORALE_DIAGNOSTICS";
        private const string EnvCombatModelDiagnostics = "COOPSPECTATOR_COMBAT_MODEL_DIAGNOSTICS";
        private const double SharedDebugOverrideCacheSeconds = 1.0d;
        private const string SharedPossessionDiagnosticsKey = "possession";
        private const string SharedMoraleDiagnosticsKey = "morale";
        private static bool? _possessionDiagnosticsRuntimeOverride;
        private static bool? _moraleDiagnosticsRuntimeOverride;
        private static bool? _sharedPossessionDiagnosticsOverride;
        private static bool? _sharedMoraleDiagnosticsOverride;
        private static DateTime _sharedDebugOverrideCacheExpiresUtc = DateTime.MinValue;
        private static readonly object SharedDebugOverrideLock = new object();

        /// <summary>Увімкнути reflection dump API GameTextManager та двофазну валідацію ключа (BeforeLoad/AfterLoad).</summary>
        public static bool DebugTexts => GetEnvBool(EnvDebugTexts);

        /// <summary>Увімкнути збір stdout/stderr процесу Dedicated Helper у файл (dedicated_stdout.log).</summary>
        public static bool DebugDedicatedStdio => GetEnvBool(EnvDebugDedicatedStdio);

        /// <summary>Enable focused Order of Battle formation/count diagnostics.</summary>
        public static bool OrderOfBattleDiagnostics => GetEnvBool(EnvOrderOfBattleDiagnostics);

        /// <summary>Enable focused, deduplicated exact field-battle deployment-boundary diagnostics.</summary>
        public static bool FieldBattleBoundaryDiagnostics =>
            GetEnvBool(EnvFieldBattleBoundaryDiagnostics);

        /// <summary>Enable focused possession/corpse/controlled-agent diagnostics.</summary>
        public static bool PossessionDiagnostics =>
            _possessionDiagnosticsRuntimeOverride ??
            GetSharedPossessionDiagnosticsOverride() ??
            GetEnvBool(EnvPossessionDiagnostics);

        /// <summary>Enable focused exact-siege morale, panic, and retreat diagnostics.</summary>
        public static bool MoraleDiagnostics =>
            _moraleDiagnosticsRuntimeOverride ??
            GetSharedMoraleDiagnosticsOverride() ??
            GetEnvBool(EnvMoraleDiagnostics);

        /// <summary>Enable per-agent combat-model damage and projectile samples.</summary>
        public static bool CombatModelDiagnostics => GetEnvBool(EnvCombatModelDiagnostics);

        public static void SetPossessionDiagnosticsRuntimeOverride(bool? enabled)
        {
            _possessionDiagnosticsRuntimeOverride = enabled;
            SetSharedDebugOverride(SharedPossessionDiagnosticsKey, enabled);
        }

        public static string GetPossessionDiagnosticsStatus()
        {
            string runtimeState =
                _possessionDiagnosticsRuntimeOverride.HasValue
                    ? (_possessionDiagnosticsRuntimeOverride.Value ? "runtime-on" : "runtime-off")
                    : "runtime-inherit-env";
            string sharedState = FormatSharedOverrideState(GetSharedPossessionDiagnosticsOverride());
            return "PossessionDiagnostics=" + (PossessionDiagnostics ? "ON" : "OFF") + " (" + runtimeState + ", " + sharedState + ")";
        }

        public static void SetMoraleDiagnosticsRuntimeOverride(bool? enabled)
        {
            _moraleDiagnosticsRuntimeOverride = enabled;
            SetSharedDebugOverride(SharedMoraleDiagnosticsKey, enabled);
        }

        public static string GetMoraleDiagnosticsStatus()
        {
            string runtimeState =
                _moraleDiagnosticsRuntimeOverride.HasValue
                    ? (_moraleDiagnosticsRuntimeOverride.Value ? "runtime-on" : "runtime-off")
                    : "runtime-inherit-env";
            string sharedState = FormatSharedOverrideState(GetSharedMoraleDiagnosticsOverride());
            return "MoraleDiagnostics=" + (MoraleDiagnostics ? "ON" : "OFF") + " (" + runtimeState + ", " + sharedState + ")";
        }

        private static bool? GetSharedPossessionDiagnosticsOverride()
        {
            RefreshSharedDebugOverrideCacheIfNeeded();
            return _sharedPossessionDiagnosticsOverride;
        }

        private static bool? GetSharedMoraleDiagnosticsOverride()
        {
            RefreshSharedDebugOverrideCacheIfNeeded();
            return _sharedMoraleDiagnosticsOverride;
        }

        private static string FormatSharedOverrideState(bool? value)
        {
            if (!value.HasValue)
                return "shared-inherit-env";

            return value.Value ? "shared-on" : "shared-off";
        }

        private static void RefreshSharedDebugOverrideCacheIfNeeded()
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (_sharedDebugOverrideCacheExpiresUtc > nowUtc)
                return;

            lock (SharedDebugOverrideLock)
            {
                if (_sharedDebugOverrideCacheExpiresUtc > nowUtc)
                    return;

                _sharedPossessionDiagnosticsOverride = null;
                _sharedMoraleDiagnosticsOverride = null;
                try
                {
                    string path = GetSharedDebugOverridePath();
                    if (System.IO.File.Exists(path))
                    {
                        string[] lines = System.IO.File.ReadAllLines(path);
                        for (int i = 0; i < lines.Length; i++)
                        {
                            string line = lines[i]?.Trim();
                            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                                continue;

                            int separatorIndex = line.IndexOf('=');
                            if (separatorIndex <= 0)
                                continue;

                            string key = line.Substring(0, separatorIndex).Trim();
                            string value = line.Substring(separatorIndex + 1).Trim();
                            bool? parsedValue = ParseBoolOverride(value);
                            if (!parsedValue.HasValue)
                                continue;

                            if (string.Equals(key, SharedPossessionDiagnosticsKey, StringComparison.OrdinalIgnoreCase))
                                _sharedPossessionDiagnosticsOverride = parsedValue.Value;
                            else if (string.Equals(key, SharedMoraleDiagnosticsKey, StringComparison.OrdinalIgnoreCase))
                                _sharedMoraleDiagnosticsOverride = parsedValue.Value;
                        }
                    }
                }
                catch
                {
                    _sharedPossessionDiagnosticsOverride = null;
                    _sharedMoraleDiagnosticsOverride = null;
                }
                finally
                {
                    _sharedDebugOverrideCacheExpiresUtc = nowUtc.AddSeconds(SharedDebugOverrideCacheSeconds);
                }
            }
        }

        private static void SetSharedDebugOverride(string key, bool? enabled)
        {
            lock (SharedDebugOverrideLock)
            {
                try
                {
                    var overrides = new System.Collections.Generic.Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                    string path = GetSharedDebugOverridePath();
                    if (System.IO.File.Exists(path))
                    {
                        string[] lines = System.IO.File.ReadAllLines(path);
                        for (int i = 0; i < lines.Length; i++)
                        {
                            string line = lines[i]?.Trim();
                            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                                continue;

                            int separatorIndex = line.IndexOf('=');
                            if (separatorIndex <= 0)
                                continue;

                            bool? parsedValue = ParseBoolOverride(line.Substring(separatorIndex + 1).Trim());
                            if (parsedValue.HasValue)
                                overrides[line.Substring(0, separatorIndex).Trim()] = parsedValue.Value;
                        }
                    }

                    if (enabled.HasValue)
                        overrides[key] = enabled.Value;
                    else
                        overrides.Remove(key);

                    string directory = System.IO.Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(directory))
                        System.IO.Directory.CreateDirectory(directory);

                    if (overrides.Count == 0)
                    {
                        if (System.IO.File.Exists(path))
                            System.IO.File.Delete(path);
                    }
                    else
                    {
                        var lines = new System.Collections.Generic.List<string>();
                        foreach (System.Collections.Generic.KeyValuePair<string, bool> pair in overrides)
                            lines.Add(pair.Key + "=" + (pair.Value ? "1" : "0"));
                        System.IO.File.WriteAllLines(path, lines.ToArray());
                    }

                    _sharedDebugOverrideCacheExpiresUtc = DateTime.MinValue;
                }
                catch
                {
                    _sharedDebugOverrideCacheExpiresUtc = DateTime.MinValue;
                }
            }
        }

        private static bool? ParseBoolOverride(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string normalized = value.Trim();
            if ("1".Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                "true".Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                "yes".Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                "on".Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if ("0".Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                "false".Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                "no".Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                "off".Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return null;
        }

        private static string GetSharedDebugOverridePath()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (string.IsNullOrWhiteSpace(root))
                root = System.IO.Path.GetTempPath();

            return System.IO.Path.Combine(
                root,
                "Mount and Blade II Bannerlord",
                "CoopSpectator",
                "debug_overrides.txt");
        }

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
