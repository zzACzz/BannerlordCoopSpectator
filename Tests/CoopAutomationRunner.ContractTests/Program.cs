using System;
using System.Diagnostics;
using System.IO;
using System.Text;

internal static class Program
{
    private static int Main()
    {
        string repositoryRoot = ResolveRepositoryRoot();
        string corePath = Path.Combine(repositoryRoot, "scripts", "CoopAutomationRunner.Core.ps1");
        string runnerPath = Path.Combine(repositoryRoot, "scripts", "Invoke-CoopTest.ps1");
        string clientLauncherPath = Path.Combine(repositoryRoot, "scripts", "Start-CoopBattleTestClient.ps1");
        Assert(File.Exists(corePath), "Runner core helper must exist: " + corePath);
        Assert(File.Exists(runnerPath), "Aggregate runner must exist: " + runnerPath);
        Assert(File.Exists(clientLauncherPath), "Client launcher must exist: " + clientLauncherPath);
        ValidateRunnerIntegration(runnerPath, corePath, clientLauncherPath);

        string temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "CoopSpectator",
            "ContractTests",
            "runner-primitives-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        string harnessPath = Path.Combine(temporaryRoot, "Validate-CoopAutomationRunnerCore.ps1");
        File.WriteAllText(harnessPath, HarnessScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        try
        {
            RunHarness("powershell.exe", harnessPath, corePath);
            RunHarness("pwsh.exe", harnessPath, corePath);
            Console.WriteLine("Coop automation runner contract tests passed in Windows PowerShell 5.1 and PowerShell 7.");
            return 0;
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static void ValidateRunnerIntegration(string runnerPath, string corePath, string clientLauncherPath)
    {
        string source = File.ReadAllText(runnerPath);
        string coreSource = File.ReadAllText(corePath);
        string clientLauncherSource = File.ReadAllText(clientLauncherPath);
        Assert(
            source.Contains(". $runnerCorePath", StringComparison.Ordinal),
            "Aggregate runner must dot-source the tested core helper.");
        Assert(
            source.Contains("$dedicatedBootstrapRequest = New-CoopDedicatedBootstrapRequest", StringComparison.Ordinal),
            "Aggregate runner must use the tested structured dedicated-bootstrap request builder.");
        Assert(
            source.Contains("Get-CoopDescendantProcessRecordsFromSnapshot", StringComparison.Ordinal),
            "Aggregate runner must use the tested in-memory process-tree traversal.");
        Assert(
            source.Contains("Get-CimInstance -ClassName Win32_Process -OperationTimeoutSec 10", StringComparison.Ordinal),
            "Aggregate runner process snapshots must use a bounded CIM operation timeout.");
        int provisionalRegistration = source.IndexOf("$dedicatedIdentity = New-CoopProvisionalProcessIdentity", StringComparison.Ordinal);
        int provisionalInventoryWrite = source.IndexOf("Add-CoopOwnedRuntimeProcess -Identity $dedicatedIdentity", provisionalRegistration, StringComparison.Ordinal);
        int verifiedIdentityResolution = source.IndexOf("$dedicatedIdentity = Get-CoopProcessIdentity", provisionalInventoryWrite, StringComparison.Ordinal);
        Assert(
            provisionalRegistration >= 0 && provisionalInventoryWrite > provisionalRegistration && verifiedIdentityResolution > provisionalInventoryWrite,
            "Feasibility must record provisional dedicated ownership before fallible identity enrichment.");
        Assert(
            source.Contains("-ExpectedExecutablePath $dedicatedExecutable", StringComparison.Ordinal) &&
            source.Contains("-ObservedParentProcessId $PID", StringComparison.Ordinal),
            "Dedicated identity promotion must validate the exact requested executable and runner parent PID.");
        Assert(
            source.Contains("$_.Exception.Data['CoopRuntimeOutcome']", StringComparison.Ordinal) &&
            source.Contains("$wrapped.Data['CoopRuntimeOutcome'] = 'RunnerInternalError'", StringComparison.Ordinal),
            "Post-start identity-enrichment defects must be classified as RunnerInternalError.");
        Assert(
            source.Contains("$dedicatedStartInfo.RedirectStandardOutput = $true", StringComparison.Ordinal) &&
            source.Contains("$dedicatedStartInfo.RedirectStandardError = $true", StringComparison.Ordinal) &&
            source.Contains("$dedicatedStartInfo.RedirectStandardInput = $false", StringComparison.Ordinal),
            "Feasibility must retain supplementary output capture without relying on redirected standard input.");
        Assert(
            source.Contains("Wait-CoopDedicatedControlReady", StringComparison.Ordinal) &&
            source.Contains("Wait-CoopDedicatedBootstrapAccepted", StringComparison.Ordinal) &&
            source.Contains("DedicatedControlReadinessEvidence", StringComparison.Ordinal) &&
            source.Contains("DedicatedBootstrapStatus", StringComparison.Ordinal) &&
            !source.Contains("$dedicatedProcess.StandardInput.WriteLine", StringComparison.Ordinal),
            "Feasibility must use run-scoped dedicated readiness/bootstrap acknowledgements and no standard-input command writes.");
        Assert(
            source.Contains("Copy-CoopDedicatedNativeLogs", StringComparison.Ordinal),
            "Feasibility must retain exact PID-correlated native logs.");
        Assert(
            source.Contains("Get-CoopSingularCommandResult", StringComparison.Ordinal),
            "Aggregate command dispatch must validate a singular structured result.");
        Assert(
            coreSource.Contains("Wait-CoopProcessExitNoOutput -Process $process", StringComparison.Ordinal) &&
            !coreSource.Contains("$process.WaitForExit($GraceSeconds * 1000)", StringComparison.Ordinal) &&
            !coreSource.Contains("$process.WaitForExit(10000)", StringComparison.Ordinal) &&
            source.Contains("Stop-CoopExactProcessIdentityCore -Identity $Identity", StringComparison.Ordinal),
            "Cleanup waits must not leak Boolean values into the command result pipeline.");
        Assert(
            coreSource.Contains("function New-CoopDedicatedBootstrapRequest", StringComparison.Ordinal) &&
            coreSource.Contains("function Assert-CoopDedicatedControlReadyStatus", StringComparison.Ordinal) &&
            coreSource.Contains("function Confirm-CoopDedicatedBootstrapStatus", StringComparison.Ordinal),
            "The tested runner core must own the structured dedicated control protocol validation.");

        int clientProcessStart = clientLauncherSource.IndexOf("$process = [System.Diagnostics.Process]::Start($startInfo)", StringComparison.Ordinal);
        int clientProvisionalIdentity = clientLauncherSource.IndexOf("$provisionalIdentity = New-CoopProvisionalProcessIdentity", clientProcessStart, StringComparison.Ordinal);
        int clientProvisionalArtifact = clientLauncherSource.IndexOf("Write-JsonAtomic -Path $provisionalLaunchArtifactPath", clientProvisionalIdentity, StringComparison.Ordinal);
        int clientExactObservation = clientLauncherSource.IndexOf("$processObservation = Resolve-CoopProcessObservation", clientProvisionalArtifact, StringComparison.Ordinal);
        int clientFinalArtifact = clientLauncherSource.IndexOf("Write-JsonAtomic -Path $launchArtifactPath", clientExactObservation, StringComparison.Ordinal);
        int clientHandoffPublished = clientLauncherSource.IndexOf("$finalLaunchArtifactPublished = $true", clientFinalArtifact, StringComparison.Ordinal);
        Assert(
            clientProcessStart >= 0 &&
            clientProvisionalIdentity > clientProcessStart &&
            clientProvisionalArtifact > clientProvisionalIdentity &&
            clientExactObservation > clientProvisionalArtifact &&
            clientFinalArtifact > clientExactObservation &&
            clientHandoffPublished > clientFinalArtifact,
            "Client launch must publish provisional ownership and exact identity before the final handoff artifact.");
        Assert(
            clientLauncherSource.Contains(". $runnerCorePath", StringComparison.Ordinal) &&
            clientLauncherSource.Contains("-ExpectedParentProcessId $PID", StringComparison.Ordinal) &&
            clientLauncherSource.Contains("Schema = 'coop-automation-client-launch-v3'", StringComparison.Ordinal),
            "Client launch identity must use the tested core, exact runner parent PID, and versioned verified artifact.");
        Assert(
            clientLauncherSource.Contains("if (-not $finalLaunchArtifactPublished)", StringComparison.Ordinal) &&
            clientLauncherSource.Contains("Stop-CoopExactProcessIdentityCore", StringComparison.Ordinal) &&
            clientLauncherSource.Contains("$wrapped.Data['CoopRuntimeOutcome'] = 'RunnerInternalError'", StringComparison.Ordinal),
            "A post-start client handoff failure must remain internal and clean the exact provisional process.");
        Assert(
            clientLauncherSource.IndexOf("Write-Host", clientFinalArtifact, StringComparison.Ordinal) < 0,
            "No fallible user-output operation may follow the atomic final client handoff artifact.");
    }

    private static string ResolveRepositoryRoot()
    {
        string configured = Environment.GetEnvironmentVariable("COOPSPECTATOR_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        DirectoryInfo current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "scripts", "Invoke-CoopTest.ps1")) &&
                File.Exists(Path.Combine(current.FullName, "Tests", "contract-tests.manifest.json")))
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("Could not resolve the repository root.");
    }

    private static void RunHarness(string executable, string harnessPath, string corePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        if (string.Equals(executable, "powershell.exe", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
        }
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(harnessPath);
        startInfo.ArgumentList.Add("-CorePath");
        startInfo.ArgumentList.Add(corePath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start " + executable + ".");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(executable + " runner-core contract harness exceeded 30 seconds.");
        }

        Console.Write(standardOutput);
        if (!string.IsNullOrWhiteSpace(standardError))
            Console.Error.Write(standardError);
        Assert(process.ExitCode == 0, executable + " runner-core contract harness failed with exit code " + process.ExitCode + ".");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private const string HarnessScript = """
param([Parameter(Mandatory = $true)][string]$CorePath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. $CorePath

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$controlNowUtc = [DateTime]::UtcNow
$controlProcessStartUtc = $controlNowUtc.AddSeconds(-2)
$controlPath = [System.IO.Path]::GetFullPath($CorePath)
$controlTokenHash = 'A' * 64
$controlModuleHash = 'B' * 64
$controlCommandId = [Guid]::NewGuid()
$request = New-CoopDedicatedBootstrapRequest `
    -RunId 'dedicated-control-contract' `
    -RunTokenSha256 $controlTokenHash `
    -ExpectedDedicatedModuleSha256 $controlModuleHash `
    -ExpectedProcessId 61001 `
    -ExpectedProcessStartUtc $controlProcessStartUtc `
    -ExpectedExecutablePath $controlPath `
    -CommandId $controlCommandId `
    -CreatedUtc $controlNowUtc `
    -ExpiresUtc $controlNowUtc.AddMinutes(5) `
    -ServerName 'AC_COOP_CONTRACT'
Assert-True ($request.SchemaVersion -eq 1) 'Dedicated bootstrap request schema must remain exact.'
Assert-True ($request.Sequence -eq 1) 'A fresh run root must accept only dedicated bootstrap sequence 1.'
Assert-True ($request.CommandId -eq $controlCommandId.ToString('D')) 'Dedicated bootstrap command ID must remain exact.'
Assert-True ($request.BootstrapProfile -eq 'ConnectionFeasibilityV1') 'Dedicated bootstrap profile must remain allowlisted.'
Assert-True ($request.MaxNumberOfPlayers -eq 16 -and $request.GameType -eq 'TeamDeathmatch' -and $request.Map -eq 'mp_tdm_map_001') `
    'Dedicated bootstrap values must remain the narrow connection-feasibility profile.'

$lifetimeRejected = $false
try {
    $null = New-CoopDedicatedBootstrapRequest `
        -RunId 'dedicated-control-contract' `
        -RunTokenSha256 $controlTokenHash `
        -ExpectedDedicatedModuleSha256 $controlModuleHash `
        -ExpectedProcessId 61001 `
        -ExpectedProcessStartUtc $controlProcessStartUtc `
        -ExpectedExecutablePath $controlPath `
        -CommandId ([Guid]::NewGuid()) `
        -CreatedUtc $controlNowUtc `
        -ExpiresUtc $controlNowUtc.AddMinutes(11) `
        -ServerName 'AC_COOP_CONTRACT'
}
catch { $lifetimeRejected = $_.Exception.Message -match 'exceeds ten minutes' }
Assert-True $lifetimeRejected 'An excessive dedicated bootstrap lifetime must be rejected.'

$unsafeServerNameRejected = $false
try {
    $null = New-CoopDedicatedBootstrapRequest `
        -RunId 'dedicated-control-contract' `
        -RunTokenSha256 $controlTokenHash `
        -ExpectedDedicatedModuleSha256 $controlModuleHash `
        -ExpectedProcessId 61001 `
        -ExpectedProcessStartUtc $controlProcessStartUtc `
        -ExpectedExecutablePath $controlPath `
        -CommandId ([Guid]::NewGuid()) `
        -CreatedUtc $controlNowUtc `
        -ExpiresUtc $controlNowUtc.AddMinutes(5) `
        -ServerName 'AC_COOP_CONTRACT;start_game'
}
catch { $unsafeServerNameRejected = $_.Exception.Message -match 'only ASCII letters' }
Assert-True $unsafeServerNameRejected 'The server-name field must not expose console separators or arbitrary commands.'

$readyStatus = [pscustomobject]@{
    SchemaVersion = 1
    ProtocolMajorVersion = 1
    ProtocolMinorVersion = 0
    RunId = $request.RunId
    RunTokenSha256 = $controlTokenHash
    RoleType = 'DedicatedServer'
    RoleInstanceId = 'dedicated-server-01'
    State = 'Ready'
    ProcessId = 61001
    ProcessStartUtc = $controlProcessStartUtc.ToString('O')
    ExecutablePath = $controlPath
    ModuleSha256 = $controlModuleHash
    ExpectedModuleSha256 = $controlModuleHash
    LifecycleSource = 'InitialListedGameServerState.OnActivated'
    FailureCode = ''
    FailureMessage = ''
}
$validatedReady = Assert-CoopDedicatedControlReadyStatus `
    -Status $readyStatus `
    -ExpectedRunId $request.RunId `
    -ExpectedRunTokenSha256 $controlTokenHash `
    -ExpectedDedicatedModuleSha256 $controlModuleHash `
    -ExpectedProcessId 61001 `
    -ExpectedProcessStartUtc $controlProcessStartUtc `
    -ExpectedExecutablePath $controlPath
Assert-True ($validatedReady.State -eq 'Ready') 'The exact lifecycle readiness status must be accepted.'

$acknowledgements = @(
    [pscustomobject]@{ StepSequence = 1; Step = 'ServerName'; State = 'Applied'; ObservedValue = 'AC_COOP_CONTRACT' },
    [pscustomobject]@{ StepSequence = 2; Step = 'MaxNumberOfPlayers'; State = 'Applied'; ObservedValue = '16' },
    [pscustomobject]@{ StepSequence = 3; Step = 'GameType'; State = 'Applied'; ObservedValue = 'TeamDeathmatch' },
    [pscustomobject]@{ StepSequence = 4; Step = 'Map'; State = 'Applied'; ObservedValue = 'mp_tdm_map_001' },
    [pscustomobject]@{ StepSequence = 5; Step = 'UsableMap'; State = 'Accepted'; ObservedValue = 'mp_tdm_map_001' },
    [pscustomobject]@{ StepSequence = 6; Step = 'StartGameRequested'; State = 'Requested'; ObservedValue = 'start_game' },
    [pscustomobject]@{ StepSequence = 7; Step = 'StartGameConfirmed'; State = 'Confirmed'; ObservedValue = 'IsPlaying=true;GameType=TeamDeathmatch;Map=mp_tdm_map_001' })
$terminalStatus = [pscustomobject]@{
    SchemaVersion = 1
    ProtocolMajorVersion = 1
    ProtocolMinorVersion = 0
    RunId = $request.RunId
    Sequence = $request.Sequence
    CommandId = $request.CommandId
    SourceRoleType = 'DedicatedServer'
    SourceRoleInstanceId = 'dedicated-server-01'
    TargetRoleType = 'Runner'
    TargetRoleInstanceId = 'runner-01'
    RunTokenSha256 = $controlTokenHash
    DedicatedModuleSha256 = $controlModuleHash
    ProcessId = 61001
    ProcessStartUtc = $controlProcessStartUtc.ToString('O')
    ExecutablePath = $controlPath
    State = 'BootstrapAccepted'
    IsTerminal = $true
    Acknowledgements = $acknowledgements
    FailureCode = ''
    FailureMessage = ''
}
$accepted = Confirm-CoopDedicatedBootstrapStatus `
    -Status $terminalStatus `
    -Request $request `
    -ExpectedRunId $request.RunId `
    -ExpectedRunTokenSha256 $controlTokenHash `
    -ExpectedDedicatedModuleSha256 $controlModuleHash `
    -ExpectedProcessId 61001 `
    -ExpectedProcessStartUtc $controlProcessStartUtc `
    -ExpectedExecutablePath $controlPath
Assert-True $accepted 'The complete ordered dedicated bootstrap acknowledgement history must be accepted.'
$terminalStatus.Acknowledgements[5].Step = 'StartGameConfirmed'
$reorderedRejected = $false
try {
    $null = Confirm-CoopDedicatedBootstrapStatus `
        -Status $terminalStatus `
        -Request $request `
        -ExpectedRunId $request.RunId `
        -ExpectedRunTokenSha256 $controlTokenHash `
        -ExpectedDedicatedModuleSha256 $controlModuleHash `
        -ExpectedProcessId 61001 `
        -ExpectedProcessStartUtc $controlProcessStartUtc `
        -ExpectedExecutablePath $controlPath
}
catch { $reorderedRejected = $_.Exception.Message -match 'reordered' }
Assert-True $reorderedRejected 'A reordered dedicated bootstrap acknowledgement history must be rejected.'

$snapshot = @(
    [pscustomobject]@{ ProcessId = 10; ParentProcessId = 1; Name = 'root-a' },
    [pscustomobject]@{ ProcessId = 11; ParentProcessId = 10; Name = 'child-a' },
    [pscustomobject]@{ ProcessId = 12; ParentProcessId = 11; Name = 'grandchild-a' },
    [pscustomobject]@{ ProcessId = 13; ParentProcessId = 10; Name = 'child-b' },
    [pscustomobject]@{ ProcessId = 20; ParentProcessId = 1; Name = 'unrelated-root' },
    [pscustomobject]@{ ProcessId = 21; ParentProcessId = 20; Name = 'unrelated-child' },
    [pscustomobject]@{ ProcessId = 30; ParentProcessId = 31; Name = 'unrelated-cycle-a' },
    [pscustomobject]@{ ProcessId = 31; ParentProcessId = 30; Name = 'unrelated-cycle-b' })
$descendants = @(Get-CoopDescendantProcessRecordsFromSnapshot -Snapshot $snapshot -RootProcessIds @(10))
$ids = @($descendants | ForEach-Object { [int]$_.ProcessId } | Sort-Object)
Assert-True ($ids.Count -eq 3) 'Only three descendants belong to root 10.'
Assert-True (($ids -join ',') -eq '11,12,13') 'Nested descendants must be complete and unrelated branches must be excluded.'

$cycleSnapshot = @(
    [pscustomobject]@{ ProcessId = 40; ParentProcessId = 41 },
    [pscustomobject]@{ ProcessId = 41; ParentProcessId = 40 })
$cycleDescendants = @(Get-CoopDescendantProcessRecordsFromSnapshot -Snapshot $cycleSnapshot -RootProcessIds @(40))
Assert-True ($cycleDescendants.Count -eq 1 -and [int]$cycleDescendants[0].ProcessId -eq 41) `
    'A reachable cycle must terminate without reclassifying the root as its own descendant.'

$duplicateRejected = $false
try {
    $null = Get-CoopDescendantProcessRecordsFromSnapshot -Snapshot @(
        [pscustomobject]@{ ProcessId = 50; ParentProcessId = 1 },
        [pscustomobject]@{ ProcessId = 50; ParentProcessId = 2 }) -RootProcessIds @(1)
}
catch { $duplicateRejected = $_.Exception.Message -match 'duplicate process ID 50' }
Assert-True $duplicateRejected 'Duplicate process IDs must be rejected as an ambiguous snapshot.'

$limitRejected = $false
try {
    $null = Get-CoopDescendantProcessRecordsFromSnapshot -Snapshot $snapshot -RootProcessIds @(10) -MaximumDescendants 2
}
catch { $limitRejected = $_.Exception.Message -match 'maximum owned descendant count' }
Assert-True $limitRejected 'The explicit descendant limit must reject an oversized owned tree.'

$empty = @(Get-CoopDescendantProcessRecordsFromSnapshot -Snapshot @() -RootProcessIds @(99))
Assert-True ($empty.Count -eq 0) 'An empty snapshot must produce no descendants.'

$script:syntheticProcessStartUtc = [DateTime]::UtcNow
$script:syntheticRequestedPath = [System.IO.Path]::GetFullPath($CorePath)
$fallbackObservation = Resolve-CoopProcessObservation `
    -ProcessId 61001 `
    -ExpectedExecutablePath $script:syntheticRequestedPath `
    -ExpectedParentProcessId 61000 `
    -LaunchStartedUtc $script:syntheticProcessStartUtc.AddSeconds(-1) `
    -LaunchObservedUtc $script:syntheticProcessStartUtc.AddSeconds(1) `
    -DeadlineMilliseconds 1000 `
    -ProcessRecordProvider {
        param([int]$CandidateProcessId)
        [pscustomobject]@{ Path = $null; StartTime = $script:syntheticProcessStartUtc }
    } `
    -CimRecordProvider {
        param([int]$CandidateProcessId)
        [pscustomobject]@{
            ExecutablePath = $script:syntheticRequestedPath
            CreationDate = $script:syntheticProcessStartUtc
            ParentProcessId = 61000
        }
    }
Assert-True ($fallbackObservation.PathEvidenceSource -eq 'Win32ProcessFallback') `
    'A transient null Process.Path must use the validated Win32_Process executable-path fallback.'
Assert-True ([string]::Equals($fallbackObservation.ExecutablePath, $script:syntheticRequestedPath, [StringComparison]::OrdinalIgnoreCase)) `
    'Win32_Process fallback must retain the exact requested executable path.'

$mismatchedPathRejected = $false
try {
    $null = Resolve-CoopProcessObservation `
        -ProcessId 61002 `
        -ExpectedExecutablePath $script:syntheticRequestedPath `
        -DeadlineMilliseconds 500 `
        -ProcessRecordProvider {
            param([int]$CandidateProcessId)
            [pscustomobject]@{
                Path = $script:syntheticRequestedPath + '.unexpected'
                StartTime = $script:syntheticProcessStartUtc
            }
        } `
        -CimRecordProvider { param([int]$CandidateProcessId) $null }
}
catch { $mismatchedPathRejected = $_.Exception.Message -match 'does not match the exact requested path' }
Assert-True $mismatchedPathRejected 'An observed path mismatch must be rejected before hashing or ownership promotion.'

$boundedNullPathRejected = $false
$nullPathStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $null = Resolve-CoopProcessObservation `
        -ProcessId 61003 `
        -ExpectedExecutablePath $script:syntheticRequestedPath `
        -DeadlineMilliseconds 250 `
        -ProcessRecordProvider {
            param([int]$CandidateProcessId)
            [pscustomobject]@{ Path = $null; StartTime = $script:syntheticProcessStartUtc }
        } `
        -CimRecordProvider { param([int]$CandidateProcessId) $null }
}
catch { $boundedNullPathRejected = $_.Exception.Message -match 'Timed out after 250 ms' }
$nullPathStopwatch.Stop()
Assert-True $boundedNullPathRejected 'A persistently unavailable executable path must fail through a bounded timeout.'
Assert-True ($nullPathStopwatch.Elapsed.TotalSeconds -lt 3) 'Bounded path acquisition must not hang the runner.'

$provisionalIdentity = New-CoopProvisionalProcessIdentity `
    -ProcessId 61001 `
    -RoleType 'DedicatedServer' `
    -RoleInstanceId 'dedicated-server-contract' `
    -ExpectedExecutablePath $script:syntheticRequestedPath `
    -ExpectedParentProcessId 61000 `
    -LaunchStartedUtc $script:syntheticProcessStartUtc.AddSeconds(-1) `
    -LaunchObservedUtc $script:syntheticProcessStartUtc.AddSeconds(1) `
    -LaunchOperationId 'runner-core-contract-launch'
Assert-True (Test-CoopProcessObservationMatchesIdentity -Identity $provisionalIdentity -Observation $fallbackObservation) `
    'Provisional ownership must match the exact launch window, path, PID, and parent PID.'
$reusedPidObservation = [pscustomobject]@{
    ProcessId = 61001
    ParentProcessId = 61000
    ProcessStartUtc = $script:syntheticProcessStartUtc.AddMinutes(1).ToString('O')
    ExecutablePath = $script:syntheticRequestedPath
}
Assert-True (-not (Test-CoopProcessObservationMatchesIdentity -Identity $provisionalIdentity -Observation $reusedPidObservation)) `
    'A reused PID outside the exact launch window must never match provisional ownership.'
$wrongParentObservation = [pscustomobject]@{
    ProcessId = 61001
    ParentProcessId = 61999
    ProcessStartUtc = $script:syntheticProcessStartUtc.ToString('O')
    ExecutablePath = $script:syntheticRequestedPath
}
Assert-True (-not (Test-CoopProcessObservationMatchesIdentity -Identity $provisionalIdentity -Observation $wrongParentObservation)) `
    'A different parent PID must never match provisional root-process ownership.'
$verifiedIdentity = [ordered]@{
    IdentityState = 'Verified'
    ProcessId = 61001
    ProcessStartUtc = $script:syntheticProcessStartUtc.ToString('O')
    ExecutablePath = $script:syntheticRequestedPath
}
Assert-True (Test-CoopProcessObservationMatchesIdentity -Identity $verifiedIdentity -Observation $fallbackObservation) `
    'Verified ownership must match the exact executable path, PID, and process start time.'
Assert-True (-not (Test-CoopProcessObservationMatchesIdentity -Identity $verifiedIdentity -Observation $reusedPidObservation)) `
    'Verified ownership must reject PID reuse even when the executable path is unchanged.'

$cleanupProcess = $null
try {
    $cleanupStartInfo = New-Object System.Diagnostics.ProcessStartInfo
    $cleanupStartInfo.FileName = (Get-Process -Id $PID).Path
    $cleanupStartInfo.UseShellExecute = $false
    $cleanupStartInfo.CreateNoWindow = $true
    $cleanupStartInfo.Arguments = '-NoProfile -Command "Start-Sleep -Seconds 30"'
    $cleanupLaunchStartedUtc = [DateTime]::UtcNow
    $cleanupProcess = [System.Diagnostics.Process]::Start($cleanupStartInfo)
    Assert-True ($null -ne $cleanupProcess) 'Synthetic cleanup process must start.'
    $cleanupLaunchObservedUtc = [DateTime]::UtcNow
    $cleanupIdentity = New-CoopProvisionalProcessIdentity `
        -ProcessId $cleanupProcess.Id `
        -RoleType 'DedicatedServer' `
        -RoleInstanceId 'synthetic-provisional-cleanup' `
        -ExpectedExecutablePath $cleanupStartInfo.FileName `
        -ExpectedParentProcessId $PID `
        -LaunchStartedUtc $cleanupLaunchStartedUtc `
        -LaunchObservedUtc $cleanupLaunchObservedUtc
    Assert-True (Test-CoopLiveProcessIdentityCore -Identity $cleanupIdentity -DeadlineMilliseconds 5000) `
        'A live process must match its provisional launch evidence before cleanup.'
    $syntheticPostStartFailureObserved = $false
    try {
        throw 'Synthetic client artifact publication failed after Process.Start.'
    }
    catch {
        $syntheticPostStartFailureObserved = $_.Exception.Message -match 'artifact publication failed'
        $cleanupEvidence = Stop-CoopExactProcessIdentityCore -Identity $cleanupIdentity -GraceSeconds 1
    }
    Assert-True $syntheticPostStartFailureObserved `
        'The focused harness must inject a failure after process creation and provisional identity capture.'
    Assert-True ($cleanupEvidence.IdentityMatched -and $cleanupEvidence.Outcome -eq 'Stopped') `
        'Exact cleanup must stop a provisionally owned process after post-start artifact failure.'
    Assert-True $cleanupProcess.HasExited 'The provisionally owned synthetic process must not remain running after cleanup.'
}
finally {
    if ($null -ne $cleanupProcess -and -not $cleanupProcess.HasExited) {
        $cleanupProcess.Kill()
        $cleanupProcess.WaitForExit()
    }
    if ($null -ne $cleanupProcess) { $cleanupProcess.Dispose() }
}

$validResult = [ordered]@{ Outcome = 'Timeout'; Reason = 'synthetic'; ArtifactPath = 'synthetic.json' }
$singular = Get-CoopSingularCommandResult -Results @($validResult) -CommandName 'Synthetic'
Assert-True ($singular.Outcome -eq 'Timeout' -and $singular.PrimaryOutcome -eq 'Timeout') `
    'A Windows PowerShell ordered-dictionary result must normalize without losing its primary outcome.'

$zeroRejected = $false
try { $null = Get-CoopSingularCommandResult -Results @() -CommandName 'Zero' }
catch { $zeroRejected = $_.Exception.Message -match 'exactly one structured result object; observed 0' }
Assert-True $zeroRejected 'A zero-result aggregate command must be rejected.'

$multipleRejected = $false
try { $null = Get-CoopSingularCommandResult -Results @($true, $validResult) -CommandName 'Multiple' }
catch { $multipleRejected = $_.Exception.Message -match 'exactly one structured result object; observed 2' }
Assert-True $multipleRejected 'Incidental helper output plus a result object must be rejected.'

$missingPropertyRejected = $false
try {
    $null = Get-CoopSingularCommandResult `
        -Results @([pscustomobject]@{ Outcome = 'Pass'; Reason = 'missing artifact' }) `
        -CommandName 'MissingProperty'
}
catch { $missingPropertyRejected = $_.Exception.Message -match "missing required property 'ArtifactPath'" }
Assert-True $missingPropertyRejected 'A structurally incomplete aggregate result must be rejected.'

$nativeLogNames = @(Get-CoopPidCorrelatedNativeLogNames -ProcessId 123600)
Assert-True ($nativeLogNames.Count -eq 3) 'Exactly three PID-correlated native log names are required.'
Assert-True (($nativeLogNames -join ',') -eq 'rgl_log_123600.txt,rgl_log_errors_123600.txt,watchdog_log_123600.txt') `
    'Native log selection must use only exact PID-bound file names.'

$captureRoot = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) ('capture-fixture-' + $PID)
[System.IO.Directory]::CreateDirectory($captureRoot) | Out-Null
$childScriptPath = Join-Path $captureRoot 'Emit-NativeEvidence.ps1'
$childScript = @'
[Console]::Out.WriteLine('Listed server is ready! You can now enter console commands')
[Console]::Out.Flush()
Start-Sleep -Milliseconds 500
[Console]::Out.WriteLine('--Changed: ServerName, to: AC_COOP_CONTRACT')
[Console]::Out.WriteLine('--Changed: MaxNumberOfPlayers, to: 16')
[Console]::Out.WriteLine('--Changed: GameType, to: TeamDeathmatch')
[Console]::Out.WriteLine('--Changed: Map, to: mp_tdm_map_001')
[Console]::Out.WriteLine('--Game is starting...')
[Console]::Out.WriteLine('--Selected scene: mp_tdm_map_001')
[Console]::Out.Flush()
[Console]::Error.WriteLine('synthetic stderr evidence')
[Console]::Error.Flush()
'@
[System.IO.File]::WriteAllText($childScriptPath, $childScript, (New-Object System.Text.UTF8Encoding($false)))
$childStartInfo = New-Object System.Diagnostics.ProcessStartInfo
$childStartInfo.FileName = (Get-Process -Id $PID).Path
$childStartInfo.UseShellExecute = $false
$childStartInfo.CreateNoWindow = $true
$childStartInfo.RedirectStandardInput = $true
$childStartInfo.RedirectStandardOutput = $true
$childStartInfo.RedirectStandardError = $true
$childStartInfo.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $childScriptPath + '"'
$childProcess = New-Object System.Diagnostics.Process
$childProcess.StartInfo = $childStartInfo
Assert-True ($childProcess.Start()) 'Synthetic output process must start.'
$capture = New-CoopProcessTextCapture `
    -Process $childProcess `
    -StandardOutputPath (Join-Path $captureRoot 'stdout.txt') `
    -StandardErrorPath (Join-Path $captureRoot 'stderr.txt') `
    -MaximumTailLines 256
$readyEvidence = Wait-CoopCapturedTextMarkers `
    -Capture $capture `
    -RequiredSubstrings @('is ready! You can now enter console commands') `
    -DeadlineUtc ([DateTime]::UtcNow.AddSeconds(10)) `
    -EvidenceName 'SyntheticConsoleReady'
$afterReadySequence = [long]$readyEvidence.Matches[0].Sequence
$commandEvidence = Wait-CoopCapturedTextMarkers `
    -Capture $capture `
    -RequiredSubstrings @(
        '--Changed: ServerName, to: AC_COOP_CONTRACT',
        '--Changed: MaxNumberOfPlayers, to: 16',
        '--Changed: GameType, to: TeamDeathmatch',
        '--Changed: Map, to: mp_tdm_map_001',
        '--Game is starting...',
        '--Selected scene: mp_tdm_map_001') `
    -AfterSequence $afterReadySequence `
    -DeadlineUtc ([DateTime]::UtcNow.AddSeconds(10)) `
    -EvidenceName 'SyntheticBootstrapAccepted'
Assert-True ($commandEvidence.Matches.Count -eq 6) 'Every synthetic bootstrap marker must be observed after readiness.'
$waitOutput = @(Wait-CoopProcessExitNoOutput -Process $childProcess -TimeoutMilliseconds 10000)
Assert-True ($waitOutput.Count -eq 0) 'Process.WaitForExit(Boolean) must not enter the PowerShell output pipeline.'
Complete-CoopProcessTextCapture -Capture $capture -DrainTimeoutMilliseconds 5000
$stdoutText = [System.IO.File]::ReadAllText((Join-Path $captureRoot 'stdout.txt'))
$stderrText = [System.IO.File]::ReadAllText((Join-Path $captureRoot 'stderr.txt'))
Assert-True ($stdoutText -match 'Selected scene: mp_tdm_map_001') 'Captured stdout must retain native command evidence.'
Assert-True ($stderrText -match 'synthetic stderr evidence') 'Captured stderr must be drained and retained.'
$missingEvidenceRejected = $false
try {
    $null = Wait-CoopCapturedTextMarkers `
        -Capture $capture `
        -RequiredSubstrings @('marker-that-was-never-emitted') `
        -DeadlineUtc ([DateTime]::UtcNow.AddSeconds(1)) `
        -EvidenceName 'SyntheticMissingEvidence'
}
catch { $missingEvidenceRejected = $true }
Assert-True $missingEvidenceRejected 'A completed process with incomplete native evidence must be rejected.'

Write-Output ('PASS ' + $PSVersionTable.PSVersion.ToString())
""";
}
