using System;
using System.Diagnostics;
using TWDebug = TaleWorlds.Library.Debug;

namespace CoopSpectator.Infrastructure
{
    /// <summary>
    /// Central logger wrapper for the mod.
    /// Runtime diagnostics default to the minimal tier so hot-path tracing stays off in large battles.
    /// Use the COOPSPECTATOR_RUNTIME_DIAGNOSTICS environment variable with values off|minimal|verbose
    /// to change the active runtime diagnostic tier without editing code.
    /// </summary>
    public static class ModLogger
    {
        public enum RuntimeDiagnosticLevel
        {
            Off = 0,
            Minimal = 1,
            Verbose = 2
        }

        private const string Prefix = "[CoopSpectator]";
        private static readonly RuntimeDiagnosticLevel ActiveRuntimeDiagnosticsLevel = ResolveRuntimeDiagnosticLevel();

        public static void Info(string message)
        {
            Print("INFO", message, null);
        }

        public static void Warn(string message)
        {
            Print("WARN", message, null);
        }

        public static void Error(string message, Exception exception)
        {
            Print("ERROR", message, exception);
        }

        public static bool IsRuntimeDiagnosticEnabled(RuntimeDiagnosticLevel level)
        {
            return ActiveRuntimeDiagnosticsLevel >= level;
        }

        public static void RuntimeDiagnostic(RuntimeDiagnosticLevel level, Func<string> messageFactory)
        {
            if (!IsRuntimeDiagnosticEnabled(level) || messageFactory == null)
                return;

            Info(messageFactory());
        }

        private static void Print(string level, string message, Exception exception)
        {
            string safeMessage = message ?? string.Empty;
            string exceptionText = exception != null ? (" | " + exception) : string.Empty;
            string line = $"{Prefix} {level}: {safeMessage}{exceptionText}";

            try
            {
                TWDebug.Print(line);
            }
            catch (Exception)
            {
                Debug.WriteLine(line);
            }
        }

        private static RuntimeDiagnosticLevel ResolveRuntimeDiagnosticLevel()
        {
            try
            {
                string rawLevel = Environment.GetEnvironmentVariable("COOPSPECTATOR_RUNTIME_DIAGNOSTICS");
                switch ((rawLevel ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "0":
                    case "off":
                    case "none":
                        return RuntimeDiagnosticLevel.Off;
                    case "2":
                    case "verbose":
                    case "trace":
                        return RuntimeDiagnosticLevel.Verbose;
                    case "":
                    case "1":
                    case "minimal":
                    default:
                        return RuntimeDiagnosticLevel.Minimal;
                }
            }
            catch (Exception)
            {
                return RuntimeDiagnosticLevel.Minimal;
            }
        }
    }
}
