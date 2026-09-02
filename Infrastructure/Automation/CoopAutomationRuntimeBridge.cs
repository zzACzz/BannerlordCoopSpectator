using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;

namespace CoopSpectator.Infrastructure.Automation
{
    internal static class CoopAutomationRuntimeBridge
    {
        public const string TestAutomationVariable = "COOPSPECTATOR_TEST_AUTOMATION";
        public const string RunIdVariable = "COOPSPECTATOR_AUTOMATION_RUN_ID";
        public const string RunRootVariable = "COOPSPECTATOR_AUTOMATION_RUN_ROOT";
        public const string RunTokenVariable = "COOPSPECTATOR_AUTOMATION_RUN_TOKEN";
        public const string ExpectedModuleSha256Variable = "COOPSPECTATOR_AUTOMATION_EXPECTED_MODULE_SHA256";
        public const string ResultPolicyVariable = "COOPSPECTATOR_AUTOMATION_RESULT_POLICY";

        private const int MaximumProtocolFileBytes = 1024 * 1024;
        private static readonly object RoleStatusLock = new object();
        private static readonly TimeSpan RoleStatusWriteInterval = TimeSpan.FromSeconds(1);
        private static readonly Stopwatch RoleStopwatch = new Stopwatch();
        private static CoopAutomationRuntimeConfiguration _roleConfiguration;
        private static string _roleType = string.Empty;
        private static string _roleInstanceId = string.Empty;
        private static string _roleModulePath = string.Empty;
        private static string _roleModuleHash = string.Empty;
        private static string _roleState = string.Empty;
        private static string _roleProgressToken = string.Empty;
        private static string _roleAuthoritativeSource = string.Empty;
        private static string _roleFailureCode = string.Empty;
        private static string _roleFailureMessage = string.Empty;
        private static DateTime _roleStateEnteredUtc = DateTime.MinValue;
        private static DateTime _roleLastProgressUtc = DateTime.MinValue;
        private static DateTime _roleNextWriteUtc = DateTime.MinValue;
        private static long _roleStateRevision;
        private static long _roleLastProgressElapsedMilliseconds;

        public static bool IsAutomationEnabled =>
            string.Equals(Environment.GetEnvironmentVariable(TestAutomationVariable), "1", StringComparison.Ordinal);

        public static bool TryInitializeRole(
            string roleType,
            string roleInstanceId,
            out string failureCode,
            out string failureMessage)
        {
            failureCode = string.Empty;
            failureMessage = string.Empty;
            if (!IsAutomationEnabled)
                return true;

            if (!TryResolveConfiguration(out CoopAutomationRuntimeConfiguration configuration, out failureCode, out failureMessage))
                return false;

            string modulePath;
            string moduleHash;
            try
            {
                modulePath = Assembly.GetExecutingAssembly().Location;
                moduleHash = CoopAutomationRuntimeContract.ComputeFileSha256(modulePath);
            }
            catch (Exception ex)
            {
                return Fail("ModuleIdentityFailed", "The loaded module identity could not be measured: " + ex.Message, out failureCode, out failureMessage);
            }

            if (!string.Equals(moduleHash, configuration.ExpectedModuleSha256, StringComparison.Ordinal))
            {
                WriteOneShotRoleStatus(
                    configuration,
                    roleType,
                    roleInstanceId,
                    "Failed",
                    modulePath,
                    moduleHash,
                    "ModuleHashMismatch",
                    "The loaded module SHA-256 does not match the expected runtime identity.");
                return Fail("ModuleHashMismatch", "The loaded module SHA-256 does not match the expected runtime identity.", out failureCode, out failureMessage);
            }

            try
            {
                InitializeRoleStatus(configuration, roleType, roleInstanceId, modulePath, moduleHash);
                return true;
            }
            catch (Exception ex)
            {
                return Fail("RoleStatusWriteFailed", "The runtime role status could not be written: " + ex.Message, out failureCode, out failureMessage);
            }
        }

        public static void PumpRoleStatus(
            string roleType,
            string roleInstanceId,
            string state,
            string authoritativeSource,
            string progressToken,
            string failureCode,
            string failureMessage)
        {
            if (!IsAutomationEnabled)
                return;

            lock (RoleStatusLock)
            {
                if (_roleConfiguration == null ||
                    !string.Equals(_roleType, roleType ?? string.Empty, StringComparison.Ordinal) ||
                    !string.Equals(_roleInstanceId, roleInstanceId ?? string.Empty, StringComparison.Ordinal))
                    return;

                DateTime nowUtc = DateTime.UtcNow;
                string nextState = state ?? string.Empty;
                string nextProgressToken = progressToken ?? string.Empty;
                string nextFailureCode = failureCode ?? string.Empty;
                string nextFailureMessage = failureMessage ?? string.Empty;
                bool stateChanged = !string.Equals(_roleState, nextState, StringComparison.Ordinal);
                bool progressChanged = stateChanged ||
                    !string.Equals(_roleProgressToken, nextProgressToken, StringComparison.Ordinal) ||
                    !string.Equals(_roleFailureCode, nextFailureCode, StringComparison.Ordinal) ||
                    !string.Equals(_roleFailureMessage, nextFailureMessage, StringComparison.Ordinal);

                if (stateChanged)
                {
                    _roleState = nextState;
                    _roleStateEnteredUtc = nowUtc;
                    _roleStateRevision++;
                }
                if (progressChanged)
                {
                    _roleProgressToken = nextProgressToken;
                    _roleFailureCode = nextFailureCode;
                    _roleFailureMessage = nextFailureMessage;
                    _roleLastProgressUtc = nowUtc;
                    _roleLastProgressElapsedMilliseconds = RoleStopwatch.ElapsedMilliseconds;
                }
                _roleAuthoritativeSource = authoritativeSource ?? string.Empty;

                if (!stateChanged && nowUtc < _roleNextWriteUtc)
                    return;

                try
                {
                    WriteCurrentRoleStatus(nowUtc);
                    _roleNextWriteUtc = nowUtc.Add(RoleStatusWriteInterval);
                }
                catch (Exception ex)
                {
                    _roleFailureCode = "RoleStatusWriteFailed";
                    _roleFailureMessage = ex.Message;
                    _roleNextWriteUtc = nowUtc.Add(RoleStatusWriteInterval);
                }
            }
        }

        public static CoopAutomationResultPublicationDecision ResolveResultPublicationDecision(
            out CoopAutomationRuntimeConfiguration configuration,
            out string failureCode,
            out string failureMessage)
        {
            configuration = null;
            failureCode = string.Empty;
            failureMessage = string.Empty;
            if (!IsAutomationEnabled)
                return CoopAutomationResultPublicationDecision.Publish;

            bool valid = TryResolveConfiguration(out configuration, out failureCode, out failureMessage);
            return CoopAutomationRuntimeContract.ResolveResultPublicationDecision(
                automationEnabled: true,
                configurationValid: valid,
                resultPolicy: configuration?.ResultPolicy);
        }

        public static void WriteResultPublicationStatus(
            CoopAutomationRuntimeConfiguration configuration,
            CoopAutomationResultPublicationDecision decision,
            string battleId,
            string source,
            string failureCode,
            string failureMessage)
        {
            if (configuration == null)
                return;

            var status = new CoopAutomationResultPublicationStatus
            {
                SchemaVersion = CoopAutomationRuntimeContract.CurrentSchemaVersion,
                ProtocolMajorVersion = CoopAutomationRuntimeContract.CurrentProtocolMajorVersion,
                ProtocolMinorVersion = CoopAutomationRuntimeContract.CurrentProtocolMinorVersion,
                RunId = configuration.RunId,
                RunTokenSha256 = configuration.RunTokenSha256,
                ResultPolicy = configuration.ResultPolicy,
                Decision = decision.ToString(),
                BattleId = battleId ?? string.Empty,
                Source = source ?? string.Empty,
                UpdatedUtc = DateTime.UtcNow,
                FailureCode = failureCode ?? string.Empty,
                FailureMessage = failureMessage ?? string.Empty
            };
            string path = CoopAutomationRuntimeContract.CombineRunPath(
                configuration.RunRoot,
                CoopAutomationRuntimeContract.ResultPublicationRelativePath);
            CoopAutomationProtocolFileIO.WriteJsonStrictAtomic(path, status);
        }

        public static bool TryConfirmOwnedHostSession(
            string serverName,
            int port,
            out string failureCode,
            out string failureMessage)
        {
            failureCode = string.Empty;
            failureMessage = string.Empty;
            if (!IsAutomationEnabled)
                return Fail("AutomationDisabled", "Test automation is disabled.", out failureCode, out failureMessage);
            if (!TryResolveConfiguration(out CoopAutomationRuntimeConfiguration configuration, out failureCode, out failureMessage))
                return false;

            string path = CoopAutomationRuntimeContract.CombineRunPath(
                configuration.RunRoot,
                CoopAutomationRuntimeContract.OwnedHostRelativePath);
            if (!CoopAutomationProtocolFileIO.TryReadJson(
                    path,
                    MaximumProtocolFileBytes,
                    out CoopAutomationOwnedHostStatus host,
                    out failureCode,
                    out failureMessage))
            {
                return false;
            }
            if (!CoopAutomationRuntimeContract.TryValidateOwnedHost(
                    host,
                    configuration,
                    serverName,
                    port,
                    DateTime.UtcNow,
                    out failureCode,
                    out failureMessage))
            {
                return false;
            }

            try
            {
                using (Process process = Process.GetProcessById(host.OwnerProcessId))
                {
                    string executablePath = process.MainModule?.FileName ?? string.Empty;
                    DateTime processStartUtc = process.StartTime.ToUniversalTime();
                    if (!CoopAutomationRuntimeContract.DoesLiveProcessMatch(
                            host,
                            process.Id,
                            processStartUtc,
                            executablePath))
                    {
                        return Fail("OwnedHostProcessMismatch", "The live dedicated process does not match the run-scoped ownership identity.", out failureCode, out failureMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                return Fail("OwnedHostProcessUnavailable", "The owned dedicated process is unavailable: " + ex.Message, out failureCode, out failureMessage);
            }

            return true;
        }

        public static bool TryResolveConfiguration(
            out CoopAutomationRuntimeConfiguration configuration,
            out string failureCode,
            out string failureMessage)
        {
            return CoopAutomationRuntimeContract.TryCreateConfiguration(
                IsAutomationEnabled,
                Environment.GetEnvironmentVariable(RunIdVariable),
                Environment.GetEnvironmentVariable(RunRootVariable),
                Environment.GetEnvironmentVariable(RunTokenVariable),
                Environment.GetEnvironmentVariable(ExpectedModuleSha256Variable),
                Environment.GetEnvironmentVariable(ResultPolicyVariable),
                out configuration,
                out failureCode,
                out failureMessage);
        }

        private static void InitializeRoleStatus(
            CoopAutomationRuntimeConfiguration configuration,
            string roleType,
            string roleInstanceId,
            string modulePath,
            string moduleHash)
        {
            lock (RoleStatusLock)
            {
                DateTime nowUtc = DateTime.UtcNow;
                _roleConfiguration = configuration;
                _roleType = roleType ?? string.Empty;
                _roleInstanceId = roleInstanceId ?? string.Empty;
                _roleModulePath = modulePath ?? string.Empty;
                _roleModuleHash = moduleHash ?? string.Empty;
                _roleState = "ModuleReady";
                _roleProgressToken = "ModuleReady";
                _roleAuthoritativeSource = "CoopAutomationRuntimeBridge.TryInitializeRole";
                _roleFailureCode = string.Empty;
                _roleFailureMessage = string.Empty;
                _roleStateEnteredUtc = nowUtc;
                _roleLastProgressUtc = nowUtc;
                _roleStateRevision = 1;
                RoleStopwatch.Restart();
                _roleLastProgressElapsedMilliseconds = 0;
                WriteCurrentRoleStatus(nowUtc);
                _roleNextWriteUtc = nowUtc.Add(RoleStatusWriteInterval);
            }
        }

        private static void WriteCurrentRoleStatus(DateTime nowUtc)
        {
            WriteRoleStatus(
                _roleConfiguration,
                _roleType,
                _roleInstanceId,
                _roleState,
                _roleModulePath,
                _roleModuleHash,
                _roleFailureCode,
                _roleFailureMessage,
                _roleStateEnteredUtc,
                _roleLastProgressUtc,
                _roleStateRevision,
                RoleStopwatch.ElapsedMilliseconds,
                Math.Max(0L, RoleStopwatch.ElapsedMilliseconds - _roleLastProgressElapsedMilliseconds),
                _roleAuthoritativeSource,
                nowUtc);
        }

        private static void WriteOneShotRoleStatus(
            CoopAutomationRuntimeConfiguration configuration,
            string roleType,
            string roleInstanceId,
            string state,
            string modulePath,
            string moduleHash,
            string failureCode,
            string failureMessage)
        {
            DateTime nowUtc = DateTime.UtcNow;
            WriteRoleStatus(
                configuration,
                roleType,
                roleInstanceId,
                state,
                modulePath,
                moduleHash,
                failureCode,
                failureMessage,
                nowUtc,
                nowUtc,
                1,
                0,
                0,
                "CoopAutomationRuntimeBridge.TryInitializeRole",
                nowUtc);
        }

        private static void WriteRoleStatus(
            CoopAutomationRuntimeConfiguration configuration,
            string roleType,
            string roleInstanceId,
            string state,
            string modulePath,
            string moduleHash,
            string failureCode,
            string failureMessage,
            DateTime stateEnteredUtc,
            DateTime lastProgressUtc,
            long stateRevision,
            long monotonicElapsedMilliseconds,
            long monotonicSinceProgressMilliseconds,
            string authoritativeSource,
            DateTime nowUtc)
        {
            using (Process process = Process.GetCurrentProcess())
            {
                var status = new CoopAutomationRuntimeRoleStatus
                {
                    SchemaVersion = CoopAutomationRuntimeContract.CurrentRoleStatusSchemaVersion,
                    ProtocolMajorVersion = CoopAutomationRuntimeContract.CurrentProtocolMajorVersion,
                    ProtocolMinorVersion = CoopAutomationRuntimeContract.CurrentProtocolMinorVersion,
                    RunId = configuration.RunId,
                    RunTokenSha256 = configuration.RunTokenSha256,
                    RoleType = roleType ?? string.Empty,
                    RoleInstanceId = roleInstanceId ?? string.Empty,
                    State = state ?? string.Empty,
                    UpdatedUtc = nowUtc,
                    HeartbeatUtc = nowUtc,
                    LastProgressUtc = lastProgressUtc,
                    StateEnteredUtc = stateEnteredUtc,
                    StateRevision = stateRevision,
                    MonotonicElapsedMilliseconds = monotonicElapsedMilliseconds,
                    MonotonicSinceProgressMilliseconds = monotonicSinceProgressMilliseconds,
                    AuthoritativeSource = authoritativeSource ?? string.Empty,
                    LastStructuredError = string.IsNullOrWhiteSpace(failureCode)
                        ? string.Empty
                        : (failureCode ?? string.Empty) + ": " + (failureMessage ?? string.Empty),
                    Capabilities = new System.Collections.Generic.List<string>
                    {
                        "RoleHealthV1",
                        "FailureEvidenceV1"
                    },
                    ProcessId = process.Id,
                    ProcessStartUtc = process.StartTime.ToUniversalTime(),
                    ExecutablePath = process.MainModule?.FileName ?? string.Empty,
                    ModulePath = modulePath ?? string.Empty,
                    ModuleSha256 = moduleHash ?? string.Empty,
                    ExpectedModuleSha256 = configuration.ExpectedModuleSha256,
                    ResultPolicy = configuration.ResultPolicy,
                    FailureCode = failureCode ?? string.Empty,
                    FailureMessage = failureMessage ?? string.Empty
                };
                string relativePath = "status/" + (roleInstanceId ?? "unknown-role") + ".json";
                string path = CoopAutomationRuntimeContract.CombineRunPath(configuration.RunRoot, relativePath);
                CoopAutomationProtocolFileIO.WriteJsonStrictAtomic(path, status);
            }
        }

        private static bool Fail(
            string code,
            string message,
            out string failureCode,
            out string failureMessage)
        {
            failureCode = code;
            failureMessage = message;
            return false;
        }
    }
}
