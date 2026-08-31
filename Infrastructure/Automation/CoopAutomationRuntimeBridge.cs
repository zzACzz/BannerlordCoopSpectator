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
                TryWriteRoleStatus(
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
                TryWriteRoleStatus(
                    configuration,
                    roleType,
                    roleInstanceId,
                    "ModuleReady",
                    modulePath,
                    moduleHash,
                    string.Empty,
                    string.Empty);
                return true;
            }
            catch (Exception ex)
            {
                return Fail("RoleStatusWriteFailed", "The runtime role status could not be written: " + ex.Message, out failureCode, out failureMessage);
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

        private static void TryWriteRoleStatus(
            CoopAutomationRuntimeConfiguration configuration,
            string roleType,
            string roleInstanceId,
            string state,
            string modulePath,
            string moduleHash,
            string failureCode,
            string failureMessage)
        {
            using (Process process = Process.GetCurrentProcess())
            {
                var status = new CoopAutomationRuntimeRoleStatus
                {
                    SchemaVersion = CoopAutomationRuntimeContract.CurrentSchemaVersion,
                    ProtocolMajorVersion = CoopAutomationRuntimeContract.CurrentProtocolMajorVersion,
                    ProtocolMinorVersion = CoopAutomationRuntimeContract.CurrentProtocolMinorVersion,
                    RunId = configuration.RunId,
                    RunTokenSha256 = configuration.RunTokenSha256,
                    RoleType = roleType ?? string.Empty,
                    RoleInstanceId = roleInstanceId ?? string.Empty,
                    State = state ?? string.Empty,
                    UpdatedUtc = DateTime.UtcNow,
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
