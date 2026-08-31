using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using CoopSpectator.Infrastructure.Automation;

internal static class Program
{
    private const string AutomationVariable = "COOPSPECTATOR_TEST_AUTOMATION";
    private const string RunIdVariable = "COOPSPECTATOR_AUTOMATION_RUN_ID";
    private const string RunRootVariable = "COOPSPECTATOR_AUTOMATION_RUN_ROOT";
    private const string RunTokenVariable = "COOPSPECTATOR_AUTOMATION_RUN_TOKEN";
    private const string ExpectedHashVariable = "COOPSPECTATOR_AUTOMATION_EXPECTED_MODULE_SHA256";
    private const string ResultPolicyVariable = "COOPSPECTATOR_AUTOMATION_RESULT_POLICY";

    private static int Main()
    {
        string runId = "runtime-contract-" + Guid.NewGuid().ToString("N");
        string runRoot = Path.Combine(Path.GetTempPath(), "CoopSpectator", "Automation", runId);
        string token = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        string modulePath = Assembly.GetExecutingAssembly().Location;
        string moduleHash = CoopAutomationRuntimeContract.ComputeFileSha256(modulePath);
        string[] names =
        {
            AutomationVariable,
            RunIdVariable,
            RunRootVariable,
            RunTokenVariable,
            ExpectedHashVariable,
            ResultPolicyVariable
        };
        string[] oldValues = new string[names.Length];
        for (int i = 0; i < names.Length; i++)
            oldValues[i] = Environment.GetEnvironmentVariable(names[i]);

        try
        {
            Directory.CreateDirectory(runRoot);
            ValidateProductionDecision();
            ConfigureValidRun(runId, runRoot, token, moduleHash);
            ValidateConfiguration(runId, runRoot, token, moduleHash);
            ValidateRoleIdentity(runRoot, moduleHash);
            ValidateSuppressDecision();
            ValidateOwnedHost(runId, runRoot, token);
            ValidateDedicatedControlContract(runId, runRoot, token, moduleHash);
            ValidateInvalidAutomationRejects(runId, runRoot, token, moduleHash);
            Console.WriteLine("Coop automation runtime contract tests passed.");
            return 0;
        }
        finally
        {
            for (int i = 0; i < names.Length; i++)
                Environment.SetEnvironmentVariable(names[i], oldValues[i]);
            if (Directory.Exists(runRoot))
                Directory.Delete(runRoot, recursive: true);
        }
    }

    private static void ValidateProductionDecision()
    {
        Environment.SetEnvironmentVariable(AutomationVariable, null);
        Assert(
            CoopAutomationRuntimeContract.ResolveResultPublicationDecision(false, false, string.Empty) ==
            CoopAutomationResultPublicationDecision.Publish,
            "Production runtime must preserve canonical result publication.");
    }

    private static void ConfigureValidRun(string runId, string runRoot, string token, string moduleHash)
    {
        Environment.SetEnvironmentVariable(AutomationVariable, "1");
        Environment.SetEnvironmentVariable(RunIdVariable, runId);
        Environment.SetEnvironmentVariable(RunRootVariable, runRoot);
        Environment.SetEnvironmentVariable(RunTokenVariable, token);
        Environment.SetEnvironmentVariable(ExpectedHashVariable, moduleHash);
        Environment.SetEnvironmentVariable(ResultPolicyVariable, CoopAutomationRuntimeContract.SuppressResultPolicy);
    }

    private static void ValidateConfiguration(string runId, string runRoot, string token, string moduleHash)
    {
        Assert(
            CoopAutomationRuntimeContract.TryCreateConfiguration(
                true,
                runId,
                runRoot,
                token,
                moduleHash,
                CoopAutomationRuntimeContract.SuppressResultPolicy,
                out CoopAutomationRuntimeConfiguration configuration,
                out string failureCode,
                out string failureMessage),
            "Valid runtime configuration must be accepted: " + failureCode + ": " + failureMessage);
        Assert(configuration.RunId == runId, "Normalized runtime RunId must remain exact.");
        Assert(configuration.ExpectedModuleSha256 == moduleHash, "Expected module hash must remain exact.");
        Assert(configuration.RunTokenSha256 == CoopAutomationRuntimeContract.ComputeSha256Hex(token), "Only the token hash may enter runtime artifacts.");

        Assert(
            !CoopAutomationRuntimeContract.TryCreateConfiguration(
                true,
                runId,
                Path.Combine(Path.GetTempPath(), "wrong-root"),
                token,
                moduleHash,
                CoopAutomationRuntimeContract.SuppressResultPolicy,
                out _,
                out failureCode,
                out _)
            && failureCode == "RunRootMismatch",
            "A run root outside the exact RunId-scoped temp path must be rejected.");
    }

    private static void ValidateRoleIdentity(string runRoot, string moduleHash)
    {
        Assert(
            CoopAutomationRuntimeBridge.TryInitializeRole(
                "ContractTest",
                "contract-test-01",
                out string failureCode,
                out string failureMessage),
            "The loaded exact module must publish ModuleReady: " + failureCode + ": " + failureMessage);

        string statusPath = Path.Combine(runRoot, "status", "contract-test-01.json");
        Assert(
            CoopAutomationProtocolFileIO.TryReadJson(
                statusPath,
                1024 * 1024,
                out CoopAutomationRuntimeRoleStatus status,
                out failureCode,
                out failureMessage),
            "Role status must be readable: " + failureCode + ": " + failureMessage);
        Assert(status.State == "ModuleReady", "Role status must report ModuleReady.");
        Assert(status.ModuleSha256 == moduleHash, "Role status must report the measured loaded module hash.");
        Assert(status.ExpectedModuleSha256 == moduleHash, "Role status must retain the requested module hash.");
    }

    private static void ValidateSuppressDecision()
    {
        CoopAutomationResultPublicationDecision decision =
            CoopAutomationRuntimeBridge.ResolveResultPublicationDecision(
                out CoopAutomationRuntimeConfiguration configuration,
                out string failureCode,
                out string failureMessage);
        Assert(decision == CoopAutomationResultPublicationDecision.Suppress, "A valid M2B run must suppress canonical result publication.");
        Assert(configuration != null, "A suppressed publication must retain the validated run context.");
        Assert(string.IsNullOrEmpty(failureCode) && string.IsNullOrEmpty(failureMessage), "A valid suppression decision must not carry a failure.");
    }

    private static void ValidateOwnedHost(string runId, string runRoot, string token)
    {
        using (Process process = Process.GetCurrentProcess())
        {
            var host = new CoopAutomationOwnedHostStatus
            {
                SchemaVersion = CoopAutomationRuntimeContract.CurrentSchemaVersion,
                ProtocolMajorVersion = CoopAutomationRuntimeContract.CurrentProtocolMajorVersion,
                ProtocolMinorVersion = CoopAutomationRuntimeContract.CurrentProtocolMinorVersion,
                RunId = runId,
                RunTokenSha256 = CoopAutomationRuntimeContract.ComputeSha256Hex(token),
                ServerName = "AC_COOP_RUNTIME_CONTRACT",
                ServerPort = 7210,
                OwnerProcessId = process.Id,
                OwnerProcessStartUtc = process.StartTime.ToUniversalTime(),
                OwnerExecutablePath = process.MainModule?.FileName ?? string.Empty,
                Protocol = "UDP",
                ConfirmedUtc = DateTime.UtcNow
            };
            string hostPath = CoopAutomationRuntimeContract.CombineRunPath(runRoot, CoopAutomationRuntimeContract.OwnedHostRelativePath);
            CoopAutomationProtocolFileIO.WriteJsonStrictAtomic(hostPath, host);

            Assert(
                CoopAutomationRuntimeBridge.TryConfirmOwnedHostSession(
                    host.ServerName,
                    host.ServerPort,
                    out string failureCode,
                    out string failureMessage),
                "A token-bound live process identity must confirm host ownership: " + failureCode + ": " + failureMessage);
            Assert(
                !CoopAutomationRuntimeBridge.TryConfirmOwnedHostSession(
                    "WRONG_SERVER",
                    host.ServerPort,
                    out failureCode,
                    out _)
                && failureCode == "OwnedHostServerNameMismatch",
                "A different server name must not reuse the owned-host proof.");
        }
    }

    private static void ValidateInvalidAutomationRejects(string runId, string runRoot, string token, string moduleHash)
    {
        Environment.SetEnvironmentVariable(ResultPolicyVariable, "CampaignConsumable");
        CoopAutomationResultPublicationDecision decision =
            CoopAutomationRuntimeBridge.ResolveResultPublicationDecision(
                out CoopAutomationRuntimeConfiguration configuration,
                out string failureCode,
                out _);
        Assert(decision == CoopAutomationResultPublicationDecision.Reject, "An unsupported automation result policy must fail closed.");
        Assert(configuration == null, "Rejected automation configuration must not be exposed as valid.");
        Assert(failureCode == "ResultPolicyUnsupported", "Rejected automation must report the stable policy failure code.");
        ConfigureValidRun(runId, runRoot, token, moduleHash);
    }

    private static void ValidateDedicatedControlContract(string runId, string runRoot, string token, string moduleHash)
    {
        Assert(
            CoopAutomationRuntimeContract.TryCreateConfiguration(
                true,
                runId,
                runRoot,
                token,
                moduleHash,
                CoopAutomationRuntimeContract.SuppressResultPolicy,
                out CoopAutomationRuntimeConfiguration configuration,
                out string failureCode,
                out string failureMessage),
            "Dedicated control configuration must be valid: " + failureCode + ": " + failureMessage);

        using (Process process = Process.GetCurrentProcess())
        {
            DateTime nowUtc = DateTime.UtcNow;
            DateTime processStartUtc = process.StartTime.ToUniversalTime();
            string executablePath = process.MainModule?.FileName ?? string.Empty;
            var request = new CoopAutomationDedicatedBootstrapRequest
            {
                SchemaVersion = CoopAutomationDedicatedControlContract.CurrentSchemaVersion,
                ProtocolMajorVersion = CoopAutomationRuntimeContract.CurrentProtocolMajorVersion,
                ProtocolMinorVersion = CoopAutomationRuntimeContract.CurrentProtocolMinorVersion,
                RunId = runId,
                Sequence = 1,
                CommandId = Guid.NewGuid().ToString("D"),
                SourceRoleType = CoopAutomationDedicatedControlContract.RunnerRoleType,
                SourceRoleInstanceId = CoopAutomationDedicatedControlContract.RunnerRoleInstanceId,
                TargetRoleType = CoopAutomationDedicatedControlContract.DedicatedRoleType,
                TargetRoleInstanceId = CoopAutomationDedicatedControlContract.DedicatedRoleInstanceId,
                CreatedUtc = nowUtc,
                ExpiresUtc = nowUtc.AddMinutes(5),
                RunTokenSha256 = configuration.RunTokenSha256,
                ExpectedDedicatedModuleSha256 = moduleHash,
                ExpectedProcessId = process.Id,
                ExpectedProcessStartUtc = processStartUtc,
                ExpectedExecutablePath = executablePath,
                BootstrapProfile = CoopAutomationDedicatedControlContract.ConnectionFeasibilityProfile,
                ServerName = "AC_COOP_CONTRACT",
                MaxNumberOfPlayers = 16,
                GameType = "TeamDeathmatch",
                Map = "mp_tdm_map_001"
            };

            Assert(
                CoopAutomationDedicatedControlContract.TryValidateRequest(
                    request,
                    configuration,
                    moduleHash,
                    process.Id,
                    processStartUtc,
                    executablePath,
                    nowUtc,
                    out failureCode,
                    out failureMessage),
                "The exact dedicated bootstrap request must be accepted: " + failureCode + ": " + failureMessage);

            request.Sequence = 2;
            Assert(
                !CoopAutomationDedicatedControlContract.TryValidateRequest(
                    request, configuration, moduleHash, process.Id, processStartUtc, executablePath, nowUtc,
                    out failureCode, out _) && failureCode == "SequenceInvalid",
                "A reordered or repeated dedicated bootstrap sequence must be rejected.");
            request.Sequence = 1;

            string validTokenHash = request.RunTokenSha256;
            request.RunTokenSha256 = new string('0', 64);
            Assert(
                !CoopAutomationDedicatedControlContract.TryValidateRequest(
                    request, configuration, moduleHash, process.Id, processStartUtc, executablePath, nowUtc,
                    out failureCode, out _) && failureCode == "RunTokenMismatch",
                "A cross-run dedicated bootstrap token must be rejected.");
            request.RunTokenSha256 = validTokenHash;

            request.ExpectedProcessId = process.Id + 1;
            Assert(
                !CoopAutomationDedicatedControlContract.TryValidateRequest(
                    request, configuration, moduleHash, process.Id, processStartUtc, executablePath, nowUtc,
                    out failureCode, out _) && failureCode == "ProcessIdMismatch",
                "A foreign dedicated process identity must be rejected.");
            request.ExpectedProcessId = process.Id;

            request.CreatedUtc = nowUtc.AddMinutes(-5);
            request.ExpiresUtc = nowUtc.AddSeconds(-1);
            Assert(
                !CoopAutomationDedicatedControlContract.TryValidateRequest(
                    request, configuration, moduleHash, process.Id, processStartUtc, executablePath, nowUtc,
                    out failureCode, out _) && failureCode == "RequestExpired",
                "An expired dedicated bootstrap request must be rejected.");
            request.CreatedUtc = nowUtc;
            request.ExpiresUtc = nowUtc.AddMinutes(5);

            request.GameType = "CoopBattle";
            Assert(
                !CoopAutomationDedicatedControlContract.TryValidateRequest(
                    request, configuration, moduleHash, process.Id, processStartUtc, executablePath, nowUtc,
                    out failureCode, out _) && failureCode == "GameTypeUnsupported",
                "The connection bootstrap must not expose an arbitrary game-type command surface.");
            request.GameType = "TeamDeathmatch";

            request.ServerName = "AC_COOP_CONTRACT;start_game";
            Assert(
                !CoopAutomationDedicatedControlContract.TryValidateRequest(
                    request, configuration, moduleHash, process.Id, processStartUtc, executablePath, nowUtc,
                    out failureCode, out _) && failureCode == "ServerNameInvalid",
                "The server-name field must not expose console separators or an arbitrary command surface.");
            request.ServerName = "AC_COOP_CONTRACT";

            var readyStatus = new CoopAutomationDedicatedControlReadyStatus
            {
                SchemaVersion = CoopAutomationDedicatedControlContract.CurrentSchemaVersion,
                ProtocolMajorVersion = CoopAutomationRuntimeContract.CurrentProtocolMajorVersion,
                ProtocolMinorVersion = CoopAutomationRuntimeContract.CurrentProtocolMinorVersion,
                RunId = runId,
                RunTokenSha256 = configuration.RunTokenSha256,
                RoleType = CoopAutomationDedicatedControlContract.DedicatedRoleType,
                RoleInstanceId = CoopAutomationDedicatedControlContract.DedicatedRoleInstanceId,
                State = CoopAutomationDedicatedControlContract.ReadyState,
                UpdatedUtc = nowUtc,
                ProcessId = process.Id,
                ProcessStartUtc = processStartUtc,
                ExecutablePath = executablePath,
                ModulePath = Assembly.GetExecutingAssembly().Location,
                ModuleSha256 = moduleHash,
                ExpectedModuleSha256 = moduleHash,
                LifecycleSource = "InitialListedGameServerState.OnActivated"
            };
            Assert(
                CoopAutomationDedicatedControlContract.TryValidateReadyStatus(
                    readyStatus,
                    configuration,
                    moduleHash,
                    process.Id,
                    processStartUtc,
                    executablePath,
                    out failureCode,
                    out failureMessage),
                "The exact dedicated readiness acknowledgement must be accepted: " + failureCode + ": " + failureMessage);
            readyStatus.LifecycleSource = "ProcessAlive";
            Assert(
                !CoopAutomationDedicatedControlContract.TryValidateReadyStatus(
                    readyStatus, configuration, moduleHash, process.Id, processStartUtc, executablePath,
                    out failureCode, out _) && failureCode == "ReadyLifecycleSourceInvalid",
                "Process liveness must not impersonate the authoritative readiness lifecycle.");
            readyStatus.LifecycleSource = "InitialListedGameServerState.OnActivated";

            var terminalStatus = new CoopAutomationDedicatedBootstrapStatus
            {
                SchemaVersion = CoopAutomationDedicatedControlContract.CurrentSchemaVersion,
                ProtocolMajorVersion = CoopAutomationRuntimeContract.CurrentProtocolMajorVersion,
                ProtocolMinorVersion = CoopAutomationRuntimeContract.CurrentProtocolMinorVersion,
                RunId = runId,
                Sequence = request.Sequence,
                CommandId = request.CommandId,
                SourceRoleType = CoopAutomationDedicatedControlContract.DedicatedRoleType,
                SourceRoleInstanceId = CoopAutomationDedicatedControlContract.DedicatedRoleInstanceId,
                TargetRoleType = CoopAutomationDedicatedControlContract.RunnerRoleType,
                TargetRoleInstanceId = CoopAutomationDedicatedControlContract.RunnerRoleInstanceId,
                RunTokenSha256 = configuration.RunTokenSha256,
                DedicatedModuleSha256 = moduleHash,
                ProcessId = process.Id,
                ProcessStartUtc = processStartUtc,
                ExecutablePath = executablePath,
                State = CoopAutomationDedicatedControlContract.BootstrapAcceptedState,
                IsTerminal = true,
                UpdatedUtc = nowUtc,
                Acknowledgements = CreateDedicatedAcknowledgements(nowUtc)
            };
            Assert(
                CoopAutomationDedicatedControlContract.TryValidateTerminalStatus(
                    terminalStatus,
                    request,
                    configuration,
                    moduleHash,
                    process.Id,
                    processStartUtc,
                    executablePath,
                    out failureCode,
                    out failureMessage),
                "The complete dedicated bootstrap acknowledgement history must be accepted: " + failureCode + ": " + failureMessage);
            terminalStatus.Acknowledgements[5].Step = "StartGameConfirmed";
            Assert(
                !CoopAutomationDedicatedControlContract.TryValidateTerminalStatus(
                    terminalStatus, request, configuration, moduleHash, process.Id, processStartUtc, executablePath,
                    out failureCode, out _) && failureCode == "AcknowledgementSequenceInvalid",
                "A reordered dedicated acknowledgement history must be rejected.");
        }
    }

    private static System.Collections.Generic.List<CoopAutomationDedicatedBootstrapAcknowledgement> CreateDedicatedAcknowledgements(DateTime nowUtc)
    {
        string[] steps =
        {
            "ServerName", "MaxNumberOfPlayers", "GameType", "Map", "UsableMap",
            "StartGameRequested", "StartGameConfirmed"
        };
        var acknowledgements = new System.Collections.Generic.List<CoopAutomationDedicatedBootstrapAcknowledgement>();
        for (int i = 0; i < steps.Length; i++)
        {
            acknowledgements.Add(new CoopAutomationDedicatedBootstrapAcknowledgement
            {
                StepSequence = i + 1,
                Step = steps[i],
                State = i == 5 ? "Requested" : i == 6 ? "Confirmed" : i == 4 ? "Accepted" : "Applied",
                ExpectedValue = steps[i],
                ObservedValue = steps[i],
                AcknowledgedUtc = nowUtc.AddMilliseconds(i)
            });
        }
        return acknowledgements;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
