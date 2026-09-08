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
            ResultPolicyVariable,
            CoopAutomationRuntimeContract.SpawnSmokeProfileVariable
        };
        string[] oldValues = new string[names.Length];
        for (int i = 0; i < names.Length; i++)
            oldValues[i] = Environment.GetEnvironmentVariable(names[i]);

        try
        {
            Directory.CreateDirectory(runRoot);
            Environment.SetEnvironmentVariable(CoopAutomationRuntimeContract.SpawnSmokeProfileVariable, null);
            ValidateProductionDecision();
            ConfigureValidRun(runId, runRoot, token, moduleHash);
            ValidateConfiguration(runId, runRoot, token, moduleHash);
            ValidateLocalStorage(runId, runRoot, token, moduleHash);
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

    private static void ValidateLocalStorage(string runId, string runRoot, string token, string moduleHash)
    {
        Assert(CoopAutomationRuntimeBridge.TryResolveConfiguration(out var config, out _, out _), "Valid local config missing.");
        int documentsCalls = 0;
        Func<string> fakeDocuments = () => { documentsCalls++; return Path.Combine(runRoot, "fake-documents"); };
        Func<string> forbiddenDocuments = () => throw new Exception("Production Documents resolver was invoked.");
        string expectedProduction = Path.Combine(runRoot, "fake-documents", "Mount and Blade II Bannerlord", "CoopSpectator");
        Assert(CoopAutomationRuntimeContract.ResolveCoopFolderPath(false, "stale-profile", null, fakeDocuments) == expectedProduction,
            "Disabled automation must retain the production path.");
        Assert(CoopAutomationRuntimeContract.ResolveCoopFolderPath(true, null, null, fakeDocuments) == expectedProduction && documentsCalls == 2,
            "Existing non-smoke automation must retain its original path.");
        string local = CoopAutomationRuntimeContract.ResolveCoopFolderPath(true,
            CoopAutomationRuntimeContract.SpawnSmokeProfile, config, forbiddenDocuments);
        Assert(local == Path.Combine(runRoot, "state", "bridge"), "Smoke storage is not run-local.");
        foreach (string profile in new[] { "unknown", " ", "fielddedicatedspawnsmokev1" })
            AssertLocalStorageRejects(() => CoopAutomationRuntimeContract.ResolveCoopFolderPath(true, profile, config, forbiddenDocuments));
        AssertLocalStorageRejects(() => CoopAutomationRuntimeContract.ResolveCoopFolderPath(true,
            CoopAutomationRuntimeContract.SpawnSmokeProfile, null, forbiddenDocuments));
        string oldRoot = config.RunRoot;
        config.RunRoot = runRoot + "-foreign";
        AssertLocalStorageRejects(() => CoopAutomationRuntimeContract.ResolveCoopFolderPath(true,
            CoopAutomationRuntimeContract.SpawnSmokeProfile, config, forbiddenDocuments));
        config.RunRoot = oldRoot;
        config.ResultPolicy = "Publish";
        AssertLocalStorageRejects(() => CoopAutomationRuntimeContract.ResolveCoopFolderPath(true,
            CoopAutomationRuntimeContract.SpawnSmokeProfile, config, forbiddenDocuments));
        config.ResultPolicy = "Suppress";
        foreach (string relative in new[] { "../outside", "state/../../outside", "state/file:stream", runRoot, "." })
            Assert(!CoopAutomationRuntimeContract.TryResolveContainedPath(runRoot, relative, out _, out _), "Escaping local path accepted.");
        Environment.SetEnvironmentVariable(CoopAutomationRuntimeContract.SpawnSmokeProfileVariable, CoopAutomationRuntimeContract.SpawnSmokeProfile);
        Assert(CoopAutomationRuntimeBridge.ResolveCoopFolderPath() == local, "Runtime adapter did not select local storage.");
        Environment.SetEnvironmentVariable(RunRootVariable, runRoot + "-foreign");
        AssertLocalStorageRejects(() => CoopAutomationRuntimeBridge.ResolveCoopFolderPath());
        ConfigureValidRun(runId, runRoot, token, moduleHash);
        Environment.SetEnvironmentVariable(CoopAutomationRuntimeContract.SpawnSmokeProfileVariable, null);
    }

    private static void AssertLocalStorageRejects(Action action)
    {
        bool rejected = false;
        try { action(); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected, "Invalid local-storage request did not fail closed.");
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
        Assert(status.SchemaVersion == 2 && status.ProtocolMinorVersion == 1, "RoleHealthV1 must use exact schema 2 and protocol 1.1.");
        Assert(status.ModuleSha256 == moduleHash, "Role status must report the measured loaded module hash.");
        Assert(status.ExpectedModuleSha256 == moduleHash, "Role status must retain the requested module hash.");
        Assert(status.Capabilities.Contains("RoleHealthV1"), "The role status must explicitly declare RoleHealthV1.");
        Assert(status.StateRevision == 1 && status.HeartbeatUtc != default && status.LastProgressUtc != default,
            "The initial role status must carry a complete liveness and progress timeline.");

        Assert(CoopAutomationRuntimeBridge.TryResolveConfiguration(
            out CoopAutomationRuntimeConfiguration configuration,
            out failureCode,
            out failureMessage),
            "The role-health validator requires the exact active configuration: " + failureCode + ": " + failureMessage);
        Assert(CoopAutomationRuntimeContract.TryValidateRoleStatus(
            status,
            configuration,
            "ContractTest",
            "contract-test-01",
            DateTime.UtcNow,
            out failureCode,
            out failureMessage),
            "The initial role-health status must validate: " + failureCode + ": " + failureMessage);

        System.Threading.Thread.Sleep(1100);
        CoopAutomationRuntimeBridge.PumpRoleStatus(
            "ContractTest",
            "contract-test-01",
            "ContractProgress",
            "CoopAutomationRuntime.ContractTests",
            "step-2",
            string.Empty,
            string.Empty);
        Assert(CoopAutomationProtocolFileIO.TryReadJson(
            statusPath,
            1024 * 1024,
            out status,
            out failureCode,
            out failureMessage),
            "Updated role status must be readable: " + failureCode + ": " + failureMessage);
        Assert(status.State == "ContractProgress" && status.StateRevision == 2,
            "A state transition must advance the exact monotonic state revision.");
        Assert(CoopAutomationRuntimeContract.ClassifyRoleHealth(
            status,
            DateTime.UtcNow,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5)) == "Healthy",
            "A freshly pumped role must classify as healthy.");
        status.HeartbeatUtc = DateTime.UtcNow.AddSeconds(-10);
        Assert(CoopAutomationRuntimeContract.ClassifyRoleHealth(
            status,
            DateTime.UtcNow,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30)) == "NoHeartbeat",
            "A stale heartbeat must classify distinctly as NoHeartbeat.");
        status.HeartbeatUtc = DateTime.UtcNow;
        status.LastProgressUtc = DateTime.UtcNow.AddSeconds(-40);
        Assert(CoopAutomationRuntimeContract.ClassifyRoleHealth(
            status,
            DateTime.UtcNow,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30)) == "NoProgress",
            "A live but stalled role must classify distinctly as NoProgress.");
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

            var smokeRequest = Newtonsoft.Json.JsonConvert.DeserializeObject<CoopAutomationDedicatedBootstrapRequest>(
                Newtonsoft.Json.JsonConvert.SerializeObject(request));
            smokeRequest.BootstrapProfile = CoopAutomationSpawnSmokeContract.Profile;
            smokeRequest.GameType = CoopAutomationSpawnSmokeContract.GameType;
            smokeRequest.Map = CoopAutomationSpawnSmokeContract.Scene;
            smokeRequest.FixtureId = CoopAutomationSpawnSmokeContract.FixtureId;
            smokeRequest.FixtureRelativeRoot = CoopAutomationSpawnSmokeContract.FixtureRelativeRoot;
            smokeRequest.FixtureLength = CoopAutomationSpawnSmokeContract.PayloadLength;
            smokeRequest.FixtureSha256 = CoopAutomationSpawnSmokeContract.PayloadSha256;
            smokeRequest.OracleSha256 = CoopAutomationSpawnSmokeContract.OracleSha256;
            smokeRequest.CampaignId = CoopAutomationSpawnSmokeContract.CampaignId;
            smokeRequest.BattleId = CoopAutomationSpawnSmokeContract.BattleId;
            smokeRequest.BattleInstanceId = CoopAutomationSpawnSmokeContract.BattleInstanceId;
            smokeRequest.Stage = CoopAutomationSpawnSmokeContract.Stage;
            Assert(CoopAutomationDedicatedControlContract.TryValidateRequest(
                smokeRequest, configuration, moduleHash, process.Id, processStartUtc, executablePath, nowUtc,
                out failureCode, out failureMessage), "Pinned smoke request rejected: " + failureCode);
            foreach (Action<CoopAutomationDedicatedBootstrapRequest> mutate in new Action<CoopAutomationDedicatedBootstrapRequest>[]
            {
                r => r.BootstrapProfile = "Unknown",
                r => r.FixtureId = "field-current",
                r => r.FixtureRelativeRoot = "../field-current",
                r => r.FixtureLength--,
                r => r.FixtureSha256 = new string('0', 64),
                r => r.OracleSha256 = new string('0', 64),
                r => r.CampaignId = "other-campaign",
                r => r.BattleId = "other-battle",
                r => r.BattleInstanceId = "stale-instance",
                r => r.Map = "mp_tdm_map_001",
                r => r.GameType = "Siege",
                r => r.Stage = "BattleActive",
                r => r.RunTokenSha256 = new string('0', 64),
                r => r.RunId = "stale-run",
                r => r.ExpiresUtc = nowUtc.AddSeconds(-1),
                r => r.Sequence = 2
            })
            {
                var invalidSmoke = Newtonsoft.Json.JsonConvert.DeserializeObject<CoopAutomationDedicatedBootstrapRequest>(
                    Newtonsoft.Json.JsonConvert.SerializeObject(smokeRequest));
                mutate(invalidSmoke);
                Assert(!CoopAutomationDedicatedControlContract.TryValidateRequest(
                    invalidSmoke, configuration, moduleHash, process.Id, processStartUtc, executablePath, nowUtc,
                    out failureCode, out _), "Invalid smoke request accepted.");
            }
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
