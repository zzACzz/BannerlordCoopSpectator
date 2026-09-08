using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using CoopSpectator.Infrastructure.Automation;

internal static class Program
{
    private static int _checks;
    private static string Repository()
    {
        string configured = Environment.GetEnvironmentVariable("COOPSPECTATOR_REPOSITORY_ROOT");
        if (!string.IsNullOrEmpty(configured)) return configured;
        for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Tests", "contract-tests.manifest.json"))) return dir.FullName;
        throw new Exception("Repository root not found.");
    }

    private static void Check(bool value, string message)
    {
        _checks++;
        if (!value) throw new Exception(message);
    }

    private static int Main()
    {
        string runId = "m4-contract-" + Guid.NewGuid().ToString("N");
        string root = Path.Combine(Path.GetTempPath(), "CoopSpectator", "Automation", runId);
        string fixtureRoot = Path.Combine(root, "payloads", "field-current");
        Directory.CreateDirectory(fixtureRoot);
        try
        {
            foreach (string name in new[] { "battle_roster.sanitized.json", "fixture.sanitized.metadata.json", "fixture.oracle.json" })
                File.Copy(Path.Combine(Repository(), "Tests", "Fixtures", "Automation", "field-current", name), Path.Combine(fixtureRoot, name));
            Check(CoopAutomationSpawnSmokeContract.TryLoadFixture(root, "payloads/field-current", "field-current-sanitized-v1",
                out var fixture, out var reason), "Pinned fixture rejected: " + reason);
            Check(!CoopAutomationSpawnSmokeContract.TryLoadFixture(root, "payloads/field-current", "field-current",
                out _, out reason) && reason == "FixtureIdMismatch", "Directory name must not replace fixture identity.");
            foreach (string path in new[] { "../escape.json", "payloads/../../escape", Path.Combine(Path.GetTempPath(), "escape"), "payloads/file:stream" })
                Check(!CoopAutomationSpawnSmokeContract.TryResolveContainedPath(root, path, out _, out _), "Unsafe path accepted: " + path);
            foreach (string scenario in new[] { "VillageBattle", "Siege", "SiegeAssault", "SallyOut", "SiegeAmbush", "LordsHall", "Hideout", "HideoutAmbush", "Blockade", "BlockadeSallyOut" })
                Check(!CoopAutomationSpawnSmokeContract.IsFirstFieldScenario(scenario, "FieldBattle", false), "Non-field route accepted: " + scenario);
            Check(!CoopAutomationSpawnSmokeContract.IsFirstFieldScenario("FieldBattle", "SiegeOutside", false), "Relief accepted.");
            Check(!CoopAutomationSpawnSmokeContract.IsFirstFieldScenario("FieldBattle", "FieldBattle", true), "Siege accepted.");

            string payload = Path.Combine(fixtureRoot, "battle_roster.sanitized.json");
            byte[] original = File.ReadAllBytes(payload);
            byte[] corrupt = (byte[])original.Clone();
            corrupt[100] ^= 1;
            File.WriteAllBytes(payload, corrupt);
            Check(!CoopAutomationSpawnSmokeContract.TryLoadFixture(root, "payloads/field-current", "field-current-sanitized-v1", out _, out reason) &&
                reason == "PayloadHashMismatch", "Same-length corruption accepted.");
            File.WriteAllBytes(payload, original.Take(original.Length - 1).ToArray());
            Check(!CoopAutomationSpawnSmokeContract.TryLoadFixture(root, "payloads/field-current", "field-current-sanitized-v1", out _, out reason) &&
                reason == "PayloadLengthMismatch", "Wrong length accepted.");
            File.WriteAllBytes(payload, original);

            var baseline = Observation(fixture.RosterJson);
            Check(CoopAutomationSpawnSmokeContract.TryValidateObservation(fixture, baseline, out reason), "Valid observation rejected: " + reason);
            Reject(fixture, baseline, o => o.Agents[0] = null, "null agent");
            Reject(fixture, baseline, o => o.Agents.RemoveAt(0), "missing agent");
            Reject(fixture, baseline, o => o.Agents[0].AgentIndex = o.Agents[1].AgentIndex, "reused index");
            Reject(fixture, baseline, o => o.Agents[0].EntryId = "stale-entry", "stale entry");
            Reject(fixture, baseline, o => o.Agents[0].NativeOriginAndLedgerMatch = false, "native origin");
            Reject(fixture, baseline, o => o.Agents[0].NativeCharacterId = "wrong-character", "native character");
            Reject(fixture, baseline, o => o.Agents[0].HeroId = "wrong-hero", "hero");
            Reject(fixture, baseline, o => o.Agents[0].Formation = "Infantry", "formation");
            Reject(fixture, baseline, o => o.Agents[0].FormationTeamMatches = false, "formation team");
            Reject(fixture, baseline, o => o.Agents[0].Equipment["Item0"].ItemId = "wrong-item", "equipment");
            Reject(fixture, baseline, o => o.Agents[0].Equipment["Item1"].Amount = 999, "ammunition");
            Reject(fixture, baseline, o => o.Agents[0].ReciprocalMountLink = false, "rider link");
            Reject(fixture, baseline, o => o.ActiveMountCount = 17, "mounted stack/unit confusion");
            Reject(fixture, baseline, o => o.NativeMaterializationComplete = false, "unfinished native spawn");
            Reject(fixture, baseline, o => o.Phase = "BattleActive", "active battle");
            Reject(fixture, baseline, o => o.ConnectedClientCount = 1, "client present");
            Reject(fixture, baseline, o => o.Scene = "mp_tdm_map_001", "scene");
            Reject(fixture, baseline, o => o.ScenarioKind = "VillageBattle", "route");
            Reject(fixture, baseline, o => o.ResultGuardWasClear = false, "stale result guard");
            Reject(fixture, baseline, o => o.ResultEntryCount = 0, "empty result-builder state");
            Reject(fixture, baseline, o => o.Violations.Add("fatal"), "fatal violation");
            Reject(fixture, baseline, o => o.Controllers.Clear(), "missing native controller");

            var life = new CoopAutomationSmokeLifecycle();
            Check(!life.TryClaimStart(false, false, out reason) && reason == "NativeCommandBusy" && life.StartRequests == 0, "Busy handler consumed claim.");
            Check(life.TryClaimStart(true, false, out _), "Start claim failed.");
            Check(!life.TryClaimStart(true, false, out reason) && reason == "DuplicateStartMission", "Repeated dispatch accepted.");
            Check(!life.TryClaimEnd(true, true, "BattleActive", out _), "Late abort accepted.");
            Check(life.TryClaimEnd(true, true, "PreBattleHold", out _), "Early abort rejected.");
            Check(!life.TryClaimEnd(true, true, "PreBattleHold", out _), "Repeated abort accepted.");
            var nextLife = new CoopAutomationSmokeLifecycle();
            Check(nextLife.StartRequests == 0 && nextLife.EndRequests == 0, "Command latch leaked.");

            ValidateBridge(root, runId);
            ValidatePowerShell(root, baseline);
            Console.WriteLine("M4 spawn-smoke contracts passed: " + _checks + " assertions; no product process launched.");
            return 0;
        }
        finally
        {
            CoopAutomationSpawnSmokeBridge.Reset();
            // Only this freshly created and verified temporary test root is removed.
            string prefix = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CoopSpectator", "Automation")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(root).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new Exception("Unsafe test cleanup root.");
            Directory.Delete(root, true);
        }
    }



    private static void ValidateTerminal(string root, CoopAutomationDedicatedSpawnSmokeEvidence evidence)
    {
        string hash = new string('A', 64);
        DateTime now = DateTime.UtcNow, started = now.AddMinutes(-1);
        var configuration = new CoopAutomationRuntimeConfiguration
        { RunId = "terminal-contract", RunRoot = root, RunTokenSha256 = hash, ExpectedModuleSha256 = hash, ResultPolicy = "Suppress" };
        var request = new CoopAutomationDedicatedBootstrapRequest
        {
            SchemaVersion = 1, ProtocolMajorVersion = 1, ProtocolMinorVersion = 1,
            RunId = configuration.RunId, Sequence = 1, CommandId = Guid.NewGuid().ToString("D"),
            SourceRoleType = "Runner", SourceRoleInstanceId = "runner-01",
            TargetRoleType = "DedicatedServer", TargetRoleInstanceId = "dedicated-server-01",
            CreatedUtc = now, ExpiresUtc = now.AddMinutes(5), RunTokenSha256 = hash,
            ExpectedDedicatedModuleSha256 = hash, ExpectedProcessId = 123, ExpectedProcessStartUtc = started,
            ExpectedExecutablePath = Path.Combine(root, "synthetic-server.exe"), ServerName = "AC_COOP_CONTRACT",
            MaxNumberOfPlayers = 16, BootstrapProfile = CoopAutomationSpawnSmokeContract.Profile,
            GameType = "CoopBattle", Map = "battle_terrain_029", FixtureId = CoopAutomationSpawnSmokeContract.FixtureId,
            FixtureRelativeRoot = "payloads/field-current", FixtureLength = CoopAutomationSpawnSmokeContract.PayloadLength,
            FixtureSha256 = CoopAutomationSpawnSmokeContract.PayloadSha256, OracleSha256 = CoopAutomationSpawnSmokeContract.OracleSha256,
            CampaignId = CoopAutomationSpawnSmokeContract.CampaignId, BattleId = CoopAutomationSpawnSmokeContract.BattleId,
            BattleInstanceId = CoopAutomationSpawnSmokeContract.BattleInstanceId, Stage = "PreBattleHold"
        };
        var status = new CoopAutomationDedicatedBootstrapStatus
        {
            SchemaVersion = 1, ProtocolMajorVersion = 1, ProtocolMinorVersion = 1,
            RunId = request.RunId, Sequence = request.Sequence, CommandId = request.CommandId, RunTokenSha256 = hash,
            SourceRoleType = "DedicatedServer", SourceRoleInstanceId = "dedicated-server-01",
            TargetRoleType = "Runner", TargetRoleInstanceId = "runner-01",
            ProcessId = 123, ProcessStartUtc = started, ExecutablePath = request.ExpectedExecutablePath,
            DedicatedModuleSha256 = hash, UpdatedUtc = now, State = "SpawnSmokePassed", IsTerminal = true, SmokeEvidence = evidence
        };
        string[] steps = { "ServerName", "MaxNumberOfPlayers", "GameType", "Map", "UsableMap", "StartGameRequested", "StartGameConfirmed" };
        string[] values = { request.ServerName, "16", "CoopBattle", "battle_terrain_029", "battle_terrain_029", "start_game",
            "IsPlaying=true;GameType=CoopBattle;Map=battle_terrain_029" };
        for (int i = 0; i < steps.Length; i++)
            status.Acknowledgements.Add(new CoopAutomationDedicatedBootstrapAcknowledgement
            { StepSequence = i + 1, Step = steps[i], State = "Confirmed", ObservedValue = values[i], AcknowledgedUtc = now });
        bool Validate(CoopAutomationDedicatedBootstrapStatus candidate) => CoopAutomationDedicatedControlContract.TryValidateTerminalStatus(
            candidate, request, configuration, hash, 123, started, request.ExpectedExecutablePath, out _, out _);
        Check(Validate(status), "Valid terminal smoke rejected.");
        foreach (Action<CoopAutomationDedicatedBootstrapStatus> mutation in new Action<CoopAutomationDedicatedBootstrapStatus>[]
        {
            x => x.CommandId = Guid.NewGuid().ToString("D"), x => x.RunTokenSha256 = new string('B',64),
            x => x.RunId = "stale-run", x => x.ProcessStartUtc = started.AddSeconds(2),
            x => x.State = "BootstrapAccepted", x => x.Acknowledgements[2].ObservedValue = "Siege",
            x => x.SmokeEvidence.AuthoritativeSource = "synthetic-untrusted",
            x => x.SmokeEvidence.ResultEntriesAtAttempt = 0, x => x.SmokeEvidence.SuppressedResults = 0,
            x => x.SmokeEvidence.Observation.Agents[0].NativeCharacterId = "foreign"
        })
        {
            var invalid = JsonConvert.DeserializeObject<CoopAutomationDedicatedBootstrapStatus>(JsonConvert.SerializeObject(status));
            mutation(invalid);
            Check(!Validate(invalid), "Invalid terminal evidence accepted.");
        }
        File.WriteAllText(Path.Combine(root, "terminal.json"), JsonConvert.SerializeObject(status));
        File.WriteAllText(Path.Combine(root, "request.json"), JsonConvert.SerializeObject(request));
    }

    private static void ValidatePowerShell(string root, CoopAutomationSmokeObservation observation)
    {
        var evidence = new CoopAutomationDedicatedSpawnSmokeEvidence
        {
            StartMissionRequests = 1, EndMissionRequests = 1, InitialStateWasClean = true,
            MissionDisposed = true, ProtectedResultUnchanged = true, PhaseBeforeEnd = "PreBattleHold",
            ResultAttempts = 1, SuppressedResults = 1, ResultEntriesAtAttempt = 47, Observation = observation
        };
        ValidateTerminal(root, evidence);
        string evidencePath = Path.Combine(root, "evidence.json");
        File.WriteAllText(evidencePath, JsonConvert.SerializeObject(evidence));
        string harnessPath = Path.Combine(root, "runner-contract.ps1");
        File.WriteAllText(harnessPath, PowerShellHarness);
        foreach (string shell in new[] { "powershell.exe", "pwsh.exe" })
        {
            var info = new System.Diagnostics.ProcessStartInfo(shell)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", harnessPath,
                "-Repository", Repository(), "-RunRoot", root }) info.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(info);
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60000)) { process.Kill(true); throw new Exception("Synthetic PowerShell contract timeout: " + shell); }
            Check(process.ExitCode == 0, shell + ": " + stdout.Result + stderr.Result);
            Console.WriteLine(stdout.Result.Trim());
        }
    }

    private const string PowerShellHarness = """
param([string]$Repository, [string]$RunRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $Repository 'scripts\CoopAutomationRunner.Core.ps1')
function Check($value, [string]$reason) { if (-not $value) { throw $reason } }
function Reject([scriptblock]$action) {
    $rejected = $false
    try { & $action } catch { $rejected = $true }
    Check $rejected 'Invalid evidence was accepted.'
}
$p = Get-CoopSpawnSmokeProfileCore
$coreSource = [IO.File]::ReadAllText((Join-Path $Repository 'Infrastructure\Automation\CoopAutomationSpawnSmokeContract.cs'))
foreach ($key in @('Profile','FixtureId','Scene','GameType','MissionShell','CampaignId','BattleId','BattleInstanceId','Stage','PayloadSha256','OracleSha256')) {
    Check ($coreSource.Contains('"' + $p[$key] + '"')) ('C#/PowerShell profile mismatch: ' + $key)
}
$copyRoot = Join-Path $RunRoot ([Guid]::NewGuid().ToString('N'))
$null = Copy-CoopSpawnSmokeFixtureCore -RepositoryRoot $Repository -RunRoot $copyRoot
Reject { Copy-CoopSpawnSmokeFixtureCore -RepositoryRoot $Repository -RunRoot $copyRoot }
$baseline = [IO.File]::ReadAllText((Join-Path $RunRoot 'evidence.json'))
$e = $baseline | ConvertFrom-Json
Assert-CoopSpawnSmokeEvidenceCore -Evidence $e -RunRoot $copyRoot
foreach ($mutation in @(
    { param($x) $x.StartMissionRequests = 2 },
    { param($x) $x.ResultEntriesAtAttempt = 0 },
    { param($x) $x.SuppressedResults = 0 },
    { param($x) $x.MissionDisposed = $false },
    { param($x) $x.InitialStateWasClean = $false },
    { param($x) $x.ProtectedResultUnchanged = $false },
    { param($x) $x.Observation.Agents[0].HeroId = 'foreign-hero' },
    { param($x) $x.Observation.Agents[0].Formation = 'Infantry' },
    { param($x) $x.Observation.Agents[0].NativeOriginAndLedgerMatch = $false },
    { param($x) $x.Observation.Agents[0].Equipment.Item1.Amount = 999 },
    { param($x) $x.Observation.Agents[0].ReciprocalMountLink = $false },
    { param($x) $x.Observation.NativeMaterializationComplete = $false },
    { param($x) $x.Observation.Phase = 'BattleActive' },
    { param($x) $x.Observation.ScenarioKind = 'SiegeAssault' },
    { param($x) $x.Observation.ConnectedClientCount = 1 },
    { param($x) $x.Observation.Controllers = @() }
)) {
    $candidate = $baseline | ConvertFrom-Json
    & $mutation $candidate
    Reject { Assert-CoopSpawnSmokeEvidenceCore -Evidence $candidate -RunRoot $copyRoot }
}
$identity = [ordered]@{ CommandId = [Guid]::NewGuid().ToString('D'); RunId = 'attempt-01'; RunTokenSha256 = ('A' * 64); BootstrapProfile = 'ConnectionFeasibilityV1'; GameType = 'TeamDeathmatch'; Map = 'mp_tdm_map_001' }
$request = New-CoopSpawnSmokeRequestCore -BootstrapRequest $identity
Check ($request.CommandId -ceq $identity.CommandId -and $request.RunTokenSha256 -ceq $identity.RunTokenSha256 -and
    $request.BootstrapProfile -ceq $p.Profile -and $request.FixtureLength -eq 259744 -and $request.Map -ceq $p.Scene) 'Smoke request identity changed.'
Check ($identity.BootstrapProfile -ceq 'ConnectionFeasibilityV1') 'Builder mutated legacy request.'
$a = [pscustomobject]@{ RunId='pair-01'; ParentRunId='pair'; Attempt=1; NonceSha256=('A'*64); Outcome='Pass'; ResultPolicy='Suppress';
    GlobalBattleResultUnchanged=$true; NoFatalHelpersConfirmed=$true; RemainingOwnedProcesses=@(); RemainingRequiredPorts=@()
    BootstrapRequest=[pscustomobject]@{ CommandId='command-01' }
    DedicatedIdentity=[pscustomobject]@{ ProcessId=100; ProcessStartUtc='2026-09-08T01:00:00Z' }
    DedicatedBootstrapStatus=[pscustomobject]@{ SmokeEvidence=$e } }
$b = $a | ConvertTo-Json -Depth 30 | ConvertFrom-Json
$b.RunId='pair-02'; $b.Attempt=2; $b.NonceSha256=('B'*64); $b.BootstrapRequest.CommandId='command-02'
$b.DedicatedIdentity.ProcessStartUtc='2026-09-08T01:01:00Z'
Assert-CoopSpawnSmokePairCore -Reports @($a,$b)
$b.NonceSha256=$a.NonceSha256
Reject { Assert-CoopSpawnSmokePairCore -Reports @($a,$b) }
$b.NonceSha256=('B'*64); $b.BootstrapRequest.CommandId=$a.BootstrapRequest.CommandId
Reject { Assert-CoopSpawnSmokePairCore -Reports @($a,$b) }
$b.BootstrapRequest.CommandId='command-02'; $b.DedicatedIdentity.ProcessStartUtc=$a.DedicatedIdentity.ProcessStartUtc
Reject { Assert-CoopSpawnSmokePairCore -Reports @($a,$b) }
$b.DedicatedIdentity.ProcessStartUtc='2026-09-08T01:01:00Z'; $b.RemainingOwnedProcesses=@([pscustomobject]@{ProcessId=100})
Reject { Assert-CoopSpawnSmokePairCore -Reports @($a,$b) }
Reject { Assert-CoopSpawnSmokePairCore -Reports @($a) }
$m=[pscustomobject]@{ RequestedCommand='DedicatedSpawnSmoke'; TerminalOutcome='Pass'; RunId='pair-01'; ParentRunId='pair';
    SpawnSmokeAttempt=1; RepositoryDirty=$false; NonceSha256=$a.NonceSha256 }
$rr=[pscustomobject]@{ RunId='pair-01'; ReleasedAndReacquired=$true }
$sr=[pscustomobject]@{ RunId='pair-01'; Locks=@(1..6 | ForEach-Object { [pscustomobject]@{ReleasedAndReacquired=$true} }) }
$argsForArtifacts=@{Report=$a; Manifest=$m; RunnerRelease=$rr; SharedRelease=$sr; ExpectedRunId='pair-01'; ExpectedParentRunId='pair'; ExpectedAttempt=1}
Assert-CoopSpawnSmokeAttemptArtifactsCore @argsForArtifacts
$rr.ReleasedAndReacquired=$false
Reject { Assert-CoopSpawnSmokeAttemptArtifactsCore @argsForArtifacts }
$rr.ReleasedAndReacquired=$true; $sr.Locks[2].ReleasedAndReacquired=$false
Reject { Assert-CoopSpawnSmokeAttemptArtifactsCore @argsForArtifacts }
$sr.Locks[2].ReleasedAndReacquired=$true; $m.ParentRunId='stale'
Reject { Assert-CoopSpawnSmokeAttemptArtifactsCore @argsForArtifacts }
$tokens=$null; $errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $Repository 'scripts\Invoke-CoopTest.ps1'), [ref]$tokens, [ref]$errors)
Check ($errors.Count -eq 0) 'Aggregate runner parse failed.'
$functions=@($ast.FindAll({param($x) $x -is [Management.Automation.Language.FunctionDefinitionAst]}, $true))
$attempt=($functions | Where-Object Name -eq 'Invoke-CoopDedicatedSpawnSmokeAttempt').Extent.Text
Check (-not $attempt.Contains('Start-CoopBattleTestClient') -and -not $attempt.Contains('Start-CoopCampaignFixtureCapture') -and
    $attempt.Contains('Copy-CoopSpawnSmokeFixtureCore') -and $attempt.Contains('Stop-CoopOwnedRuntimeProcesses') -and
    $attempt.Contains('RemainingRequiredPorts')) 'Dedicated attempt crossed its role/isolation boundary.'
$parent=($functions | Where-Object Name -eq 'Invoke-CoopDedicatedSpawnSmoke').Extent.Text
Check ($parent.Contains('$attemptNumber -le 2') -and $parent.Contains('Assert-CoopSpawnSmokePairCore') -and
    $parent.Contains('cancel.request.json') -and -not $parent.Contains('.Kill(')) 'Pair/cancellation contract missing.'
# Syntax-extracted quotation helper actually receives paths containing spaces; do not launch product processes.
$quote=($functions | Where-Object Name -eq 'ConvertTo-CoopCommandLineArgument').Extent.Text
Invoke-Expression $quote
Check ((ConvertTo-CoopCommandLineArgument -Value 'C:\path with spaces\game') -ceq '"C:\path with spaces\game"') 'Child quoting changed.'

$terminal = [IO.File]::ReadAllText((Join-Path $RunRoot 'terminal.json')) | ConvertFrom-Json
$terminalRequest = [IO.File]::ReadAllText((Join-Path $RunRoot 'request.json')) | ConvertFrom-Json
$confirm=@{
    Status=$terminal; Request=$terminalRequest; ExpectedRunId=$terminalRequest.RunId
    ExpectedRunTokenSha256=$terminalRequest.RunTokenSha256; ExpectedDedicatedModuleSha256=$terminalRequest.ExpectedDedicatedModuleSha256
    ExpectedProcessId=$terminalRequest.ExpectedProcessId; ExpectedProcessStartUtc=(ConvertTo-CoopUtcDateTime $terminalRequest.ExpectedProcessStartUtc)
    ExpectedExecutablePath=$terminalRequest.ExpectedExecutablePath; RunRoot=$copyRoot
}
Check (Confirm-CoopDedicatedBootstrapStatus @confirm) 'PowerShell rejected a valid C# terminal envelope.'
$terminal.Acknowledgements[2].ObservedValue='Siege'
Reject { Confirm-CoopDedicatedBootstrapStatus @confirm }
$terminal.Acknowledgements[2].ObservedValue='CoopBattle'; $terminal.CommandId=[Guid]::NewGuid().ToString('D')
Reject { Confirm-CoopDedicatedBootstrapStatus @confirm }
$oraclePath=Join-Path $copyRoot 'payloads\field-current\fixture.oracle.json'
[IO.File]::AppendAllText($oraclePath,' ')
Reject { Assert-CoopSpawnSmokeEvidenceCore -Evidence $e -RunRoot $copyRoot }

Write-Output ('Spawn-smoke runner contracts passed in PowerShell ' + $PSVersionTable.PSVersion.ToString())
""";

    private static void ValidateBridge(string root, string runId)
    {
        string[] names = { CoopAutomationRuntimeBridge.TestAutomationVariable, CoopAutomationSpawnSmokeBridge.ProfileVariable };
        string[] previous = names.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            Environment.SetEnvironmentVariable(names[0], null);
            Environment.SetEnvironmentVariable(names[1], null);
            Check(!CoopAutomationSpawnSmokeBridge.IsRequested && !CoopAutomationSpawnSmokeBridge.IsActive, "Disabled gate armed.");
            Environment.SetEnvironmentVariable(names[0], "1");
            Environment.SetEnvironmentVariable(names[1], CoopAutomationSpawnSmokeContract.Profile);
            var configuration = new CoopAutomationRuntimeConfiguration
            { RunId = runId, RunRoot = root, ResultPolicy = "Suppress" };
            Check(!CoopAutomationSpawnSmokeBridge.TryActivate(configuration, "wrong", "field-current-sanitized-v1", "payloads/field-current", out _), "Wrong profile armed.");
            Check(CoopAutomationSpawnSmokeBridge.TryActivate(configuration, CoopAutomationSpawnSmokeContract.Profile,
                "field-current-sanitized-v1", "payloads/field-current", out var reason), "Bridge admission failed: " + reason);
            Check(!CoopAutomationSpawnSmokeBridge.TryActivate(configuration, CoopAutomationSpawnSmokeContract.Profile,
                "field-current-sanitized-v1", "payloads/field-current", out _), "Second run adopted existing binding.");
            Check(CoopAutomationSpawnSmokeBridge.ReadRosterJson().Length > 0, "Admitted bytes missing.");
            object mission = new object();
            CoopAutomationSpawnSmokeBridge.ObserveOpening("battle_terrain_029", "MultiplayerBattle");
            CoopAutomationSpawnSmokeBridge.ObserveInitialized(mission);
            Check(CoopAutomationSpawnSmokeBridge.MatchesMission(mission), "Mission identity lost.");
            CoopAutomationSpawnSmokeBridge.ObserveEnding(mission, "PreBattleHold");
            CoopAutomationSpawnSmokeBridge.ObserveResultAttempt(mission, "fixture-battle-001", 47, "BattleEnded", true, true);
            Check(CoopAutomationSpawnSmokeBridge.ResultAttempts == 1 && CoopAutomationSpawnSmokeBridge.SuppressedResults == 1 &&
                CoopAutomationSpawnSmokeBridge.CheckProtectedResult(), "Early-abort evidence lost.");
            CoopAutomationSpawnSmokeBridge.Reset();
            Check(!CoopAutomationSpawnSmokeBridge.IsActive && !CoopAutomationSpawnSmokeBridge.MissionEnded &&
                CoopAutomationSpawnSmokeBridge.ResultAttempts == 0 && CoopAutomationSpawnSmokeBridge.OpenedShell == "", "Reset retained prior evidence.");
        }
        finally
        {
            CoopAutomationSpawnSmokeBridge.Reset();
            for (int i = 0; i < names.Length; i++) Environment.SetEnvironmentVariable(names[i], previous[i]);
        }
    }

    private static void Reject(CoopAutomationSmokeFixture fixture, CoopAutomationSmokeObservation baseline,
        Action<CoopAutomationSmokeObservation> mutation, string label)
    {
        var value = JsonConvert.DeserializeObject<CoopAutomationSmokeObservation>(JsonConvert.SerializeObject(baseline));
        mutation(value);
        Check(!CoopAutomationSpawnSmokeContract.TryValidateObservation(fixture, value, out _), "Mutation accepted: " + label);
    }

    private static CoopAutomationSmokeObservation Observation(string roster)
    {
        var result = new CoopAutomationSmokeObservation
        {
            CampaignId = "fixture-campaign-001", BattleId = "fixture-battle-001", BattleInstanceId = "fixture-battle-instance-001",
            Stage = "PreBattleHold", Phase = "PreBattleHold", Scene = "battle_terrain_029", MissionShell = "MultiplayerBattle",
            ScenarioKind = "FieldBattle", CampaignBattleType = "FieldBattle", NativeMaterializationComplete = true,
            ActiveMountCount = 21, ResultEntryCount = 47, ResultGuardWasClear = true,
            Controllers = new List<string> { "MissionMultiplayerCoopBattle", "CoopMissionSpawnLogic", "CoopMissionNetworkBridge",
                "MissionLobbyComponent", "MissionAgentSpawnLogic", "BannerBearerLogic" }
        };
        int agentIndex = 0, mountIndex = 100, team = 0;
        foreach (var side in JObject.Parse(roster)["Snapshot"]["Sides"])
        {
            foreach (var entry in side["Troops"])
            {
                int count = (int)entry["Count"] - (int)entry["WoundedCount"];
                for (int i = 0; i < count; i++)
                {
                    bool mounted = (bool)entry["IsMounted"];
                    var agent = new CoopAutomationSmokeAgent
                    {
                        AgentIndex = agentIndex++, EntryId = (string)entry["EntryId"], SideId = (string)entry["SideId"],
                        Side = team == 0 ? "Attacker" : "Defender", TeamIndex = team, FormationTeamMatches = true,
                        Formation = (string)entry["CampaignFormationClass"], OriginalCharacterId = (string)entry["OriginalCharacterId"],
                        HeroId = (string)entry["HeroId"], IsHero = (bool)entry["IsHero"],
                        NativeCharacterId = "native-network-character", ContractNativeCharacterId = "native-network-character",
                        NativeOriginAndLedgerMatch = true, ExactContractValid = true, PreSpawnEquipmentInjected = true,
                        Active = true, Mounted = mounted, MountAgentIndex = mounted ? mountIndex++ : -1,
                        ReciprocalMountLink = mounted, MountHorseId = (string)entry["CombatHorseId"],
                        MountHarnessId = (string)entry["CombatHorseHarnessId"]
                    };
                    foreach (string slot in CoopAutomationSpawnSmokeContract.Slots)
                        agent.Equipment[slot] = new CoopAutomationSmokeSlot
                        {
                            ItemId = (string)entry["Combat" + slot + "Id"], ModifierId = (string)entry["Combat" + slot + "ModifierId"],
                            Amount = entry["Combat" + slot + "Amount"]?.Value<int?>()
                        };
                    result.Agents.Add(agent);
                }
            }
            team++;
        }
        return result;
    }
}