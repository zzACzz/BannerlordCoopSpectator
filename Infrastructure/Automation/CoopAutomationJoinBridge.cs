using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace CoopSpectator.Infrastructure.Automation
{
    internal sealed class CoopAutomationJoinConfiguration
    {
        public string RunId { get; set; }
        public string RunRoot { get; set; }
        public string RunTokenSha256 { get; set; }
        public string ServerPassword { get; set; }
        public string ClientModulePath { get; set; }
        public string ClientModuleSha256 { get; set; }
        public string RequestPath { get; set; }
        public string StatusPath { get; set; }
    }

    internal sealed class CoopAutomationJoinStatus
    {
        public int SchemaVersion { get; set; }
        public int ProtocolMajorVersion { get; set; }
        public int ProtocolMinorVersion { get; set; }
        public string RunId { get; set; }
        public long Sequence { get; set; }
        public string CommandId { get; set; }
        public string SourceRoleType { get; set; }
        public string SourceRoleInstanceId { get; set; }
        public string TargetRoleType { get; set; }
        public string TargetRoleInstanceId { get; set; }
        public string RunTokenSha256 { get; set; }
        public string State { get; set; }
        public bool IsTerminal { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public int ProcessId { get; set; }
        public string ClientModulePath { get; set; }
        public string ClientModuleSha256 { get; set; }
        public string LobbyState { get; set; }
        public string ServerId { get; set; }
        public string ServerName { get; set; }
        public string ServerAddress { get; set; }
        public int ServerPort { get; set; }
        public string FailureCode { get; set; }
        public string FailureMessage { get; set; }
    }

    internal static class CoopAutomationJoinBridge
    {
        public const string TestAutomationVariable = "COOPSPECTATOR_TEST_AUTOMATION";
        public const string RunIdVariable = "COOPSPECTATOR_AUTOMATION_RUN_ID";
        public const string RunRootVariable = "COOPSPECTATOR_AUTOMATION_RUN_ROOT";
        public const string RunTokenVariable = "COOPSPECTATOR_AUTOMATION_RUN_TOKEN";
        public const string ServerPasswordVariable = "COOPSPECTATOR_AUTOMATION_SERVER_PASSWORD";

        public static bool TryResolveConfiguration(
            out CoopAutomationJoinConfiguration configuration,
            out string failureCode,
            out string failureMessage)
        {
            configuration = null;
            failureCode = string.Empty;
            failureMessage = string.Empty;

            if (!ExperimentalFeatures.EnableTestAutomation)
                return Fail("AutomationDisabled", "Test automation is disabled.", out failureCode, out failureMessage);

            string runId = (Environment.GetEnvironmentVariable(RunIdVariable) ?? string.Empty).Trim();
            string runRootValue = (Environment.GetEnvironmentVariable(RunRootVariable) ?? string.Empty).Trim();
            string runToken = Environment.GetEnvironmentVariable(RunTokenVariable) ?? string.Empty;
            if (!CoopAutomationJoinContract.IsValidRunId(runId))
                return Fail("RunIdInvalid", "The configured automation RunId is invalid.", out failureCode, out failureMessage);

            if (string.IsNullOrWhiteSpace(runRootValue))
                return Fail("RunRootMissing", "The configured automation run root is missing.", out failureCode, out failureMessage);

            if (runToken.Length < 32)
                return Fail("RunTokenInvalid", "The configured automation run token is missing or too short.", out failureCode, out failureMessage);

            string runRoot;
            string expectedRunRoot;
            try
            {
                runRoot = TrimDirectorySeparator(Path.GetFullPath(runRootValue));
                expectedRunRoot = TrimDirectorySeparator(Path.GetFullPath(
                    Path.Combine(Path.GetTempPath(), "CoopSpectator", "Automation", runId)));
            }
            catch (Exception ex)
            {
                return Fail("RunRootInvalid", "The configured automation run root is invalid: " + ex.Message, out failureCode, out failureMessage);
            }

            if (!string.Equals(runRoot, expectedRunRoot, StringComparison.OrdinalIgnoreCase))
                return Fail("RunRootMismatch", "The automation run root is outside the required RunId-scoped temp path.", out failureCode, out failureMessage);

            string modulePath;
            string moduleHash;
            try
            {
                modulePath = Assembly.GetExecutingAssembly().Location;
                moduleHash = ComputeFileSha256(modulePath);
            }
            catch (Exception ex)
            {
                return Fail("ClientModuleIdentityFailed", "The loaded client module identity could not be measured: " + ex.Message, out failureCode, out failureMessage);
            }

            configuration = new CoopAutomationJoinConfiguration
            {
                RunId = runId,
                RunRoot = runRoot,
                RunTokenSha256 = CoopAutomationJoinContract.ComputeSha256Hex(runToken),
                ServerPassword = Environment.GetEnvironmentVariable(ServerPasswordVariable) ?? string.Empty,
                ClientModulePath = modulePath,
                ClientModuleSha256 = moduleHash,
                RequestPath = Path.Combine(runRoot, CoopAutomationJoinContract.RequestRelativePath.Replace('/', Path.DirectorySeparatorChar)),
                StatusPath = Path.Combine(runRoot, CoopAutomationJoinContract.StatusRelativePath.Replace('/', Path.DirectorySeparatorChar))
            };

            return true;
        }

        public static bool TryReadRequest(
            CoopAutomationJoinConfiguration configuration,
            out CoopAutomationJoinRequest request,
            out string failureCode,
            out string failureMessage)
        {
            request = null;
            failureCode = string.Empty;
            failureMessage = string.Empty;

            if (configuration == null)
                return Fail("ConfigurationMissing", "Automation configuration is missing.", out failureCode, out failureMessage);

            try
            {
                if (!File.Exists(configuration.RequestPath))
                    return Fail("RequestMissing", "The client join request file does not exist.", out failureCode, out failureMessage);

                string[] lines = AtomicBridgeFileIO.ReadAllLinesShared(configuration.RequestPath);
                string json = string.Join(Environment.NewLine, lines ?? Array.Empty<string>());
                request = JsonConvert.DeserializeObject<CoopAutomationJoinRequest>(json);
                return CoopAutomationJoinContract.TryValidateRequest(
                    request,
                    configuration.RunId,
                    configuration.RunTokenSha256,
                    configuration.ClientModuleSha256,
                    DateTime.UtcNow,
                    out failureCode,
                    out failureMessage);
            }
            catch (Exception ex)
            {
                return Fail("RequestReadFailed", "The client join request could not be read: " + ex.Message, out failureCode, out failureMessage);
            }
        }

        public static CoopAutomationJoinStatus TryReadStatus(CoopAutomationJoinConfiguration configuration)
        {
            if (configuration == null || !File.Exists(configuration.StatusPath))
                return null;

            try
            {
                string[] lines = AtomicBridgeFileIO.ReadAllLinesShared(configuration.StatusPath);
                return JsonConvert.DeserializeObject<CoopAutomationJoinStatus>(
                    string.Join(Environment.NewLine, lines ?? Array.Empty<string>()));
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void WriteStatus(
            CoopAutomationJoinConfiguration configuration,
            CoopAutomationJoinRequest request,
            string state,
            string lobbyState,
            CoopAutomationServerDescriptor server,
            string failureCode,
            string failureMessage)
        {
            if (configuration == null || request == null)
                return;

            var status = new CoopAutomationJoinStatus
            {
                SchemaVersion = CoopAutomationJoinContract.CurrentSchemaVersion,
                ProtocolMajorVersion = CoopAutomationRunContract.CurrentProtocolMajorVersion,
                ProtocolMinorVersion = CoopAutomationRunContract.CurrentProtocolMinorVersion,
                RunId = configuration.RunId,
                Sequence = request.Sequence,
                CommandId = request.CommandId,
                SourceRoleType = "MultiplayerClient",
                SourceRoleInstanceId = "multiplayer-client-01",
                TargetRoleType = "Runner",
                TargetRoleInstanceId = "runner-01",
                RunTokenSha256 = configuration.RunTokenSha256,
                State = state ?? string.Empty,
                IsTerminal = CoopAutomationJoinContract.IsTerminalState(state),
                UpdatedUtc = DateTime.UtcNow,
                ProcessId = Process.GetCurrentProcess().Id,
                ClientModulePath = configuration.ClientModulePath,
                ClientModuleSha256 = configuration.ClientModuleSha256,
                LobbyState = lobbyState ?? string.Empty,
                ServerId = server?.Id ?? string.Empty,
                ServerName = server?.ServerName ?? request.ServerName ?? string.Empty,
                ServerAddress = server?.Address ?? string.Empty,
                ServerPort = server?.Port ?? request.ServerPort,
                FailureCode = failureCode ?? string.Empty,
                FailureMessage = failureMessage ?? string.Empty
            };

            string json = JsonConvert.SerializeObject(status, Formatting.Indented);
            AtomicBridgeFileIO.WriteAllLinesStrictAtomic(
                configuration.StatusPath,
                json.Replace("\r", string.Empty).Split('\n'));
        }

        private static string ComputeFileSha256(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha256.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    builder.Append(hash[i].ToString("X2"));
                return builder.ToString();
            }
        }

        private static string TrimDirectorySeparator(string path)
        {
            return (path ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
