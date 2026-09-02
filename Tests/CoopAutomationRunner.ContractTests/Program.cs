using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

internal static class Program
{
    private static int Main()
    {
        string repositoryRoot = ResolveRepositoryRoot();
        string corePath = Path.Combine(repositoryRoot, "scripts", "CoopAutomationRunner.Core.ps1");
        string runnerPath = Path.Combine(repositoryRoot, "scripts", "Invoke-CoopTest.ps1");
        string clientLauncherPath = Path.Combine(repositoryRoot, "scripts", "Start-CoopBattleTestClient.ps1");
        string fixtureCaptureLauncherPath = Path.Combine(repositoryRoot, "scripts", "Start-CoopFieldFixtureCapture.ps1");
        Assert(File.Exists(corePath), "Runner core helper must exist: " + corePath);
        Assert(File.Exists(runnerPath), "Aggregate runner must exist: " + runnerPath);
        Assert(File.Exists(clientLauncherPath), "Client launcher must exist: " + clientLauncherPath);
        Assert(File.Exists(fixtureCaptureLauncherPath), "Fixture capture launcher must exist: " + fixtureCaptureLauncherPath);
        ValidateRunnerIntegration(runnerPath, corePath, clientLauncherPath, fixtureCaptureLauncherPath);

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
            ValidateActualConsoleCancellation("powershell.exe", corePath, temporaryRoot);
            ValidateActualConsoleCancellation("pwsh.exe", corePath, temporaryRoot);
            Console.WriteLine("Coop automation runner contract tests passed in Windows PowerShell 5.1 and PowerShell 7.");
            return 0;
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static void ValidateRunnerIntegration(
        string runnerPath,
        string corePath,
        string clientLauncherPath,
        string fixtureCaptureLauncherPath)
    {
        string source = File.ReadAllText(runnerPath);
        string coreSource = File.ReadAllText(corePath);
        string clientLauncherSource = File.ReadAllText(clientLauncherPath);
        string fixtureCaptureLauncherSource = File.ReadAllText(fixtureCaptureLauncherPath);
        Assert(
            source.Contains(". $runnerCorePath", StringComparison.Ordinal),
            "Aggregate runner must dot-source the tested core helper.");
        Assert(
            source.Contains("'Recover', 'Cancel'", StringComparison.Ordinal) &&
            source.Contains("Invoke-CoopExistingRunCancellation", StringComparison.Ordinal) &&
            source.Contains("CancellationV1", StringComparison.Ordinal),
            "The aggregate runner must expose exact run-scoped CancellationV1.");
        Assert(
            source.Contains("Assert-CoopRuntimeRoleHealth", StringComparison.Ordinal) &&
            coreSource.Contains("'NoHeartbeat'", StringComparison.Ordinal) &&
            coreSource.Contains("'NoProgress'", StringComparison.Ordinal),
            "The aggregate runner must classify missing liveness and missing progress separately.");
        Assert(
            source.Contains("Get-CoopSharedRuntimeResourceIdsCore", StringComparison.Ordinal) &&
            source.Contains("Enter-CoopSharedRuntimeLocksCore", StringComparison.Ordinal) &&
            source.Contains("expectedSharedRuntimeResourceCount", StringComparison.Ordinal) &&
            source.Contains("shared-runtime-lock-release.json", StringComparison.Ordinal),
            "Feasibility must construct, acquire, and verify every canonical shared runtime lock.");
        Assert(
            source.Contains("coop-runtime-recovery-v2", StringComparison.Ordinal) &&
            source.Contains("RejectedIdentities", StringComparison.Ordinal) &&
            source.Contains("DeletedRunRoot = $false", StringComparison.Ordinal),
            "RecoveryV2 must report PID-reuse rejection and must never delete the run root.");
        Assert(
            source.Contains("Write-CoopRuntimeFailureEvidence", StringComparison.Ordinal) &&
            source.Contains("artifacts\\crashes\\' + $fileName", StringComparison.Ordinal) &&
            source.Contains("DumpAttemptState", StringComparison.Ordinal) &&
            coreSource.Contains("Get-CoopCorrelatedFailureProcessesFromSnapshot", StringComparison.Ordinal),
            "FailureEvidenceV1 must retain structured crash/hang evidence and exact process correlation.");
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
            source.Contains("Copy-CoopPidCorrelatedNativeLogs", StringComparison.Ordinal) &&
            source.Contains("DedicatedNativeLogInventory", StringComparison.Ordinal) &&
            source.Contains("ClientNativeLogInventory", StringComparison.Ordinal) &&
            source.Contains("artifacts\\logs\\client\\native", StringComparison.Ordinal),
            "Feasibility must retain separate exact PID-correlated dedicated and client native logs.");
        Assert(
            source.Contains("Get-CoopPidCorrelatedNativeLogDescriptors", StringComparison.Ordinal) &&
            source.Contains("State = 'NotProduced'", StringComparison.Ordinal) &&
            source.Contains("Required PID-correlated native log is missing", StringComparison.Ordinal),
            "Native log capture must distinguish required engine logs from an optional absent watchdog log.");
        Assert(
            source.Contains("$failure.Data['CoopClientJoinStatus'] = $status", StringComparison.Ordinal) &&
            source.Contains("$lastValidatedStatus = $status", StringComparison.Ordinal) &&
            source.Contains("$timeout.Data['CoopClientJoinStatus'] = $lastValidatedStatus", StringComparison.Ordinal) &&
            source.Contains("$clientJoinStatus = $statusHint", StringComparison.Ordinal) &&
            source.Contains("ClientJoinStatus = $clientJoinStatus", StringComparison.Ordinal),
            "A terminal failure or the last validated non-terminal client status must survive the wait exception into the feasibility report.");
        Assert(
            source.Contains("Get-CoopSingularCommandResult", StringComparison.Ordinal),
            "Aggregate command dispatch must validate a singular structured result.");
        Assert(
            source.Contains("'Record' { Invoke-CoopFixtureRecord }", StringComparison.Ordinal) &&
            source.Contains("FieldBattleFixtureCapture", StringComparison.Ordinal) &&
            source.Contains("FixtureRecorded", StringComparison.Ordinal) &&
            source.Contains("$Command -eq 'Feasibility' -or $Command -eq 'Record'", StringComparison.Ordinal),
            "Record must be a first-class runner command with exact runtime locks and capture-only evidence semantics.");
        Assert(
            source.Contains("Wait-CoopFixtureRecordStatus", StringComparison.Ordinal) &&
            source.Contains("Confirm-CoopRecordedFixture", StringComparison.Ordinal) &&
            source.Contains("UnreviewedPrivateRunArtifact", StringComparison.Ordinal) &&
            source.Contains("IndependentOracleComplete = $false", StringComparison.Ordinal) &&
            source.Contains("L2OrL3PassClaimed = $false", StringComparison.Ordinal),
            "Record must validate exact fixture bytes while retaining private, non-oracle, non-L2/L3 evidence boundaries.");
        Assert(
            source.Contains("-GameType 'TeamDeathmatch'", StringComparison.Ordinal) &&
            !source.Contains("-UniqueMapId 'mp_tdm_map_001'", StringComparison.Ordinal),
            "The aggregate runner must keep the native map name out of the optional UniqueMapId filter.");
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
            clientLauncherSource.Contains("SchemaVersion = 3", StringComparison.Ordinal) &&
            clientLauncherSource.Contains("Schema = 'coop-automation-client-launch-v4'", StringComparison.Ordinal) &&
            clientLauncherSource.Contains("ProcessIdentity = $verifiedProcessIdentity", StringComparison.Ordinal),
            "Client launch identity must use schema-3 join intent, the tested core, exact runner parent PID, and versioned verified artifact.");
        Assert(
            clientLauncherSource.Contains("if (-not $finalLaunchArtifactPublished)", StringComparison.Ordinal) &&
            clientLauncherSource.Contains("Stop-CoopExactProcessIdentityCore", StringComparison.Ordinal) &&
            clientLauncherSource.Contains("$wrapped.Data['CoopRuntimeOutcome'] = 'RunnerInternalError'", StringComparison.Ordinal),
            "A post-start client handoff failure must remain internal and clean the exact provisional process.");
        Assert(
            clientLauncherSource.IndexOf("Write-Host", clientFinalArtifact, StringComparison.Ordinal) < 0,
            "No fallible user-output operation may follow the atomic final client handoff artifact.");

        int fixtureProcessStart = fixtureCaptureLauncherSource.IndexOf(
            "$process = [System.Diagnostics.Process]::Start($startInfo)",
            StringComparison.Ordinal);
        int fixtureProvisionalIdentity = fixtureCaptureLauncherSource.IndexOf(
            "$provisionalIdentity = New-CoopProvisionalProcessIdentity",
            fixtureProcessStart,
            StringComparison.Ordinal);
        int fixtureProvisionalArtifact = fixtureCaptureLauncherSource.IndexOf(
            "Write-JsonAtomic -Path $provisionalLaunchArtifactPath",
            fixtureProvisionalIdentity,
            StringComparison.Ordinal);
        int fixtureExactObservation = fixtureCaptureLauncherSource.IndexOf(
            "$processObservation = Resolve-CoopProcessObservation",
            fixtureProvisionalArtifact,
            StringComparison.Ordinal);
        int fixtureFinalArtifact = fixtureCaptureLauncherSource.IndexOf(
            "Write-JsonAtomic -Path $launchArtifactPath",
            fixtureExactObservation,
            StringComparison.Ordinal);
        int fixtureHandoffPublished = fixtureCaptureLauncherSource.IndexOf(
            "$finalLaunchArtifactPublished = $true",
            fixtureFinalArtifact,
            StringComparison.Ordinal);
        Assert(
            fixtureProcessStart >= 0 &&
            fixtureProvisionalIdentity > fixtureProcessStart &&
            fixtureProvisionalArtifact > fixtureProvisionalIdentity &&
            fixtureExactObservation > fixtureProvisionalArtifact &&
            fixtureFinalArtifact > fixtureExactObservation &&
            fixtureHandoffPublished > fixtureFinalArtifact,
            "Fixture capture launch must publish provisional ownership and exact identity before the final handoff artifact.");
        Assert(
            fixtureCaptureLauncherSource.Contains(
                "$gameArguments = \"/singleplayer $modulesArgument\"",
                StringComparison.Ordinal) &&
            fixtureCaptureLauncherSource.Contains(
                "_MODULES_*Native*SandBoxCore*CustomBattle*Sandbox*StoryMode*CoopSpectator*_MODULES_",
                StringComparison.Ordinal) &&
            fixtureCaptureLauncherSource.Contains("-ExpectedParentProcessId $PID", StringComparison.Ordinal) &&
            fixtureCaptureLauncherSource.Contains("Schema = 'coop-campaign-capture-launch-v1'", StringComparison.Ordinal),
            "Fixture capture must use the exact installed 1.4.8 singleplayer argument shape and runner-owned process identity.");
        Assert(
            fixtureCaptureLauncherSource.Contains("COOPSPECTATOR_AUTOMATION_FIXTURE_RECORD", StringComparison.Ordinal) &&
            fixtureCaptureLauncherSource.Contains("COOPSPECTATOR_AUTOMATION_FIXTURE_ID", StringComparison.Ordinal) &&
            fixtureCaptureLauncherSource.Contains("COOPSPECTATOR_AUTOMATION_SOURCE_REVISION", StringComparison.Ordinal) &&
            fixtureCaptureLauncherSource.Contains("COOPSPECTATOR_AUTOMATION_GAME_VERSION", StringComparison.Ordinal) &&
            fixtureCaptureLauncherSource.Contains("$startInfo.EnvironmentVariables.Remove($serverPasswordVariableName)", StringComparison.Ordinal) &&
            fixtureCaptureLauncherSource.Contains("RunTokenPersisted = $false", StringComparison.Ordinal) &&
            fixtureCaptureLauncherSource.Contains("CredentialsPersisted = $false", StringComparison.Ordinal) &&
            fixtureCaptureLauncherSource.Contains("UiAutomationUsed = $false", StringComparison.Ordinal),
            "Fixture capture must supply only the explicit recording environment and persist no token, credential, or UI automation claim.");
        Assert(
            fixtureCaptureLauncherSource.Contains("if ($null -ne $process -and -not $finalLaunchArtifactPublished)", StringComparison.Ordinal) &&
            fixtureCaptureLauncherSource.Contains("Stop-CoopExactProcessIdentityCore", StringComparison.Ordinal) &&
            fixtureCaptureLauncherSource.Contains("$wrapped.Data['CoopRuntimeOutcome'] = 'RunnerInternalError'", StringComparison.Ordinal),
            "A post-start fixture-capture handoff failure must remain internal and clean the exact provisional campaign process.");

        int clientHandoffValidation = source.IndexOf("$clientIdentity = Assert-CoopClientLaunchArtifact", StringComparison.Ordinal);
        int clientOwnershipRegistration = source.IndexOf("Add-CoopOwnedRuntimeProcess -Identity $clientIdentity", clientHandoffValidation, StringComparison.Ordinal);
        int clientIdentityPromotion = source.IndexOf("$clientIdentity = Get-CoopProcessIdentity", clientOwnershipRegistration, StringComparison.Ordinal);
        Assert(
            clientHandoffValidation >= 0 &&
            clientOwnershipRegistration > clientHandoffValidation &&
            clientIdentityPromotion > clientOwnershipRegistration &&
            source.IndexOf("-ObservedParentProcessId $PID", clientHandoffValidation, StringComparison.Ordinal) > clientHandoffValidation &&
            !source.Contains("[string]$clientLaunch.EntryStartUtc", StringComparison.Ordinal),
            "Aggregate client handoff must preserve validated ownership before fallible live revalidation and must not stringify JSON dates.");
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

    private static void ValidateActualConsoleCancellation(
        string executable,
        string corePath,
        string temporaryRoot)
    {
        if (!OperatingSystem.IsWindows())
            return;

        string discriminator = Path.GetFileNameWithoutExtension(executable);
        string scriptPath = Path.Combine(temporaryRoot, "ConsoleCancellation-" + discriminator + ".ps1");
        string readyPath = Path.Combine(temporaryRoot, "ConsoleCancellation-" + discriminator + ".ready");
        string observedPath = Path.Combine(temporaryRoot, "ConsoleCancellation-" + discriminator + ".observed");
        string script = """
param(
    [Parameter(Mandatory = $true)][string]$CorePath,
    [Parameter(Mandatory = $true)][string]$ReadyPath,
    [Parameter(Mandatory = $true)][string]$ObservedPath)
$ErrorActionPreference = 'Stop'
. $CorePath
Initialize-CoopCancellationSignalCore
try {
    [System.IO.File]::WriteAllText($ReadyPath, 'ready')
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while ([DateTime]::UtcNow -lt $deadline -and -not (Test-CoopConsoleCancellationRequestedCore)) {
        Start-Sleep -Milliseconds 50
    }
    if (-not (Test-CoopConsoleCancellationRequestedCore)) { exit 2 }
    [System.IO.File]::WriteAllText($ObservedPath, 'observed')
    exit 0
}
finally {
    Remove-CoopCancellationSignalCore
}
""";
        File.WriteAllText(scriptPath, script, new UTF8Encoding(false));

        string commandLine = QuoteWindowsArgument(executable) +
            " -NoProfile -ExecutionPolicy Bypass -File " + QuoteWindowsArgument(scriptPath) +
            " -CorePath " + QuoteWindowsArgument(corePath) +
            " -ReadyPath " + QuoteWindowsArgument(readyPath) +
            " -ObservedPath " + QuoteWindowsArgument(observedPath);
        var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
        Assert(
            CreateProcess(
                null,
                new StringBuilder(commandLine),
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                CreateNewProcessGroup,
                IntPtr.Zero,
                temporaryRoot,
                ref startup,
                out ProcessInformation processInformation),
            executable + " must start in an isolated console process group. Win32=" + Marshal.GetLastWin32Error());

        try
        {
            using Process process = Process.GetProcessById(unchecked((int)processInformation.dwProcessId));
            Assert(WaitForFile(readyPath, TimeSpan.FromSeconds(10)), executable + " cancellation fixture did not become ready.");
            Assert(
                GenerateConsoleCtrlEvent(CtrlBreakEvent, processInformation.dwProcessId),
                "CTRL_BREAK_EVENT must be delivered to the isolated " + executable + " process group. Win32=" + Marshal.GetLastWin32Error());
            Assert(process.WaitForExit(10000), executable + " did not terminate after observing the console cancellation signal.");
            Assert(File.Exists(observedPath), executable + " did not preserve cleanup control after CTRL_BREAK_EVENT.");
        }
        finally
        {
            CloseHandle(processInformation.hThread);
            CloseHandle(processInformation.hProcess);
        }
    }

    private static bool WaitForFile(string path, TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (File.Exists(path))
                return true;
            Thread.Sleep(25);
        }
        return false;
    }

    private static string QuoteWindowsArgument(string value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
    }

    private const uint CreateNewProcessGroup = 0x00000200;
    private const uint CtrlBreakEvent = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(
        string applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

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
    ProtocolMinorVersion = 1
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
    ProtocolMinorVersion = 1
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

$clientLaunchStartedUtc = [DateTime]::UtcNow.AddSeconds(-2)
$clientProcessStartUtc = $clientLaunchStartedUtc.AddSeconds(1)
$clientLaunchObservedUtc = $clientLaunchStartedUtc.AddSeconds(2)
$clientExecutableSha256 = Get-CoopFileSha256 -Path $script:syntheticRequestedPath
$clientModuleSha256 = 'C' * 64
$clientLaunchOperationId = [Guid]::NewGuid().ToString('D')
$clientLaunchArtifactJson = ([ordered]@{
    Schema = 'coop-automation-client-launch-v4'
    RunId = 'client-launch-contract'
    CommandId = [Guid]::NewGuid().ToString('D')
    LaunchOperationId = $clientLaunchOperationId
    IdentityState = 'Verified'
    EntryPid = 62001
    EntryPath = $script:syntheticRequestedPath
    EntryParentPid = $PID
    EntryStartUtc = $clientProcessStartUtc.ToString('O')
    ProcessIdentity = [ordered]@{
        IdentityState = 'Verified'
        LaunchOperationId = $clientLaunchOperationId
        RoleType = 'MultiplayerClient'
        RoleInstanceId = 'multiplayer-client-01'
        ProcessId = 62001
        ParentProcessId = $PID
        ExpectedParentProcessId = $PID
        ProcessStartUtc = $clientProcessStartUtc.ToString('O')
        ExecutablePath = $script:syntheticRequestedPath
        ExecutableSha256 = $clientExecutableSha256
        PathEvidenceSource = 'ProcessAndWin32Process'
        LaunchStartedUtc = $clientLaunchStartedUtc.ToString('O')
        LaunchObservedUtc = $clientLaunchObservedUtc.ToString('O')
        RegisteredUtc = $clientLaunchObservedUtc.ToString('O')
        VerifiedUtc = $clientLaunchObservedUtc.ToString('O')
    }
    ClientModuleSha256 = $clientModuleSha256
    ExistingRunContractUsed = $true
    ResultPolicy = 'Suppress'
    UiAutomationUsed = $false
    StartGameIssued = $false
    MissionOpenIssued = $false
} | ConvertTo-Json -Depth 10)
$clientLaunchArtifact = $clientLaunchArtifactJson | ConvertFrom-Json
$deserializedEntryStart = Get-CoopOptionalPropertyValue -InputObject $clientLaunchArtifact -Name 'EntryStartUtc'
$normalizedDeserializedStart = ConvertTo-CoopUtcDateTime -Value $deserializedEntryStart
Assert-True ([Math]::Abs(($normalizedDeserializedStart - $clientProcessStartUtc).TotalMilliseconds) -lt 1.0) `
    'UTC conversion must preserve a JSON timestamp whether ConvertFrom-Json returns a string or System.DateTime.'
$normalizedClientIdentity = Assert-CoopClientLaunchArtifact `
    -Artifact $clientLaunchArtifact `
    -ExpectedRunId 'client-launch-contract' `
    -ExpectedClientModuleSha256 $clientModuleSha256 `
    -ExpectedExecutablePath $script:syntheticRequestedPath `
    -ExpectedParentProcessId $PID
$normalizedClientStartUtc = ConvertTo-CoopUtcDateTime -Value $normalizedClientIdentity.ProcessStartUtc
Assert-True ([Math]::Abs(($normalizedClientStartUtc - $clientProcessStartUtc).TotalMilliseconds) -lt 1.0) `
    'Client launch validation must not apply the local UTC offset twice.'
Assert-True ($normalizedClientIdentity.ParentProcessId -eq $PID -and
    $normalizedClientIdentity.LaunchOperationId -eq $clientLaunchOperationId) `
    'Client launch validation must preserve the exact runner parent and launch-operation identity.'

$shiftedClientLaunchArtifact = $clientLaunchArtifactJson | ConvertFrom-Json
$shiftedClientLaunchArtifact.ProcessIdentity.ProcessStartUtc = $clientProcessStartUtc.AddHours(-3).ToString('O')
$shiftedClientTimeRejected = $false
try {
    $null = Assert-CoopClientLaunchArtifact `
        -Artifact $shiftedClientLaunchArtifact `
        -ExpectedRunId 'client-launch-contract' `
        -ExpectedClientModuleSha256 $clientModuleSha256 `
        -ExpectedExecutablePath $script:syntheticRequestedPath `
        -ExpectedParentProcessId $PID
}
catch { $shiftedClientTimeRejected = $_.Exception.Message -match 'process times do not match' }
Assert-True $shiftedClientTimeRejected `
    'A double-offset or otherwise shifted client process time must be rejected before ownership registration.'

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
Assert-True ($nativeLogNames.Count -eq 3) 'Exactly three PID-correlated native log names are recognized.'
Assert-True (($nativeLogNames -join ',') -eq 'rgl_log_123600.txt,rgl_log_errors_123600.txt,watchdog_log_123600.txt') `
    'Native log selection must use only exact PID-bound file names.'
$nativeLogDescriptors = @(Get-CoopPidCorrelatedNativeLogDescriptors -ProcessId 123600)
Assert-True ($nativeLogDescriptors.Count -eq 3) 'Exactly three PID-correlated native log descriptors are required.'
Assert-True ($nativeLogDescriptors[0].Required -and $nativeLogDescriptors[1].Required -and
    -not $nativeLogDescriptors[2].Required -and $nativeLogDescriptors[2].Kind -eq 'Watchdog') `
    'Engine-native logs must remain required while a non-produced watchdog log is optional and explicit.'

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

$terminalClientStatus = [pscustomobject][ordered]@{
    State = 'Failed'
    FailureCode = 'PlatformLoginStillIdle'
    FailureMessage = 'Synthetic terminal client evidence.'
}

Initialize-CoopCancellationSignalCore
Assert-True (-not (Test-CoopConsoleCancellationRequestedCore)) 'Cancellation signal must start clear.'
Request-CoopConsoleCancellationForTestCore
Assert-True (Test-CoopConsoleCancellationRequestedCore) 'The thread-safe cancellation signal must be observable without pipeline output.'
Remove-CoopCancellationSignalCore

$cancelRunId = 'cancel-contract-' + $PID
$automationRoot = [System.IO.Path]::GetFullPath(
    (Join-Path ([System.IO.Path]::GetTempPath()) 'CoopSpectator\Automation'))
$cancelRunRoot = [System.IO.Path]::GetFullPath((Join-Path $automationRoot $cancelRunId))
Assert-True `
    ($cancelRunRoot.StartsWith($automationRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) `
    'The explicit cancellation fixture must stay under the exact automation temp root.'
$cancelNonce = ('A' * 64)
$cancelReadyPath = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) ('cancel-ready-' + $PID)
$cancelObservedPath = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) ('cancel-observed-' + $PID)
$fakeRunnerPath = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) ('FakeRunner-' + $PID + '.ps1')
$fakeRunnerScript = @'
param(
    [string]$RunRoot,
    [string]$RunId,
    [string]$NonceSha256,
    [string]$ReadyPath,
    [string]$ObservedPath)
$ErrorActionPreference = 'Stop'
$process = Get-Process -Id $PID
[System.IO.Directory]::CreateDirectory((Join-Path $RunRoot 'work')) | Out-Null
[System.IO.Directory]::CreateDirectory((Join-Path $RunRoot 'commands\inbox')) | Out-Null
[System.IO.Directory]::CreateDirectory((Join-Path $RunRoot 'status')) | Out-Null
$lock = New-Object System.IO.FileStream(
    (Join-Path $RunRoot 'work\runner.lock'),
    [System.IO.FileMode]::CreateNew,
    [System.IO.FileAccess]::ReadWrite,
    [System.IO.FileShare]::None)
try {
    $now = [DateTime]::UtcNow
    $manifest = [ordered]@{
        ProtocolMajorVersion = 1
        ProtocolMinorVersion = 1
        RunId = $RunId
        NonceSha256 = $NonceSha256
        Roles = @([ordered]@{
            RoleType = 'Runner'
            RoleInstanceId = 'runner-01'
            ProcessId = $PID
            ProcessStartUtc = $process.StartTime.ToUniversalTime().ToString('O')
            ExecutablePath = $process.Path
            Capabilities = @('CancellationV1', 'RecoveryV2')
        })
    }
    $lease = [ordered]@{
        RunId = $RunId
        OwnerProcessId = $PID
        OwnerProcessStartUtc = $process.StartTime.ToUniversalTime().ToString('O')
        LastHeartbeatUtc = $now.ToString('O')
        ExpiresUtc = $now.AddMinutes(5).ToString('O')
        Status = 'Active'
    }
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText((Join-Path $RunRoot 'manifest.json'), ($manifest | ConvertTo-Json -Depth 20), $utf8)
    [System.IO.File]::WriteAllText((Join-Path $RunRoot 'work\runner.lease.json'), ($lease | ConvertTo-Json -Depth 20), $utf8)
    [System.IO.File]::WriteAllText($ReadyPath, 'ready', $utf8)
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    $requestPath = Join-Path $RunRoot 'commands\inbox\cancel.request.json'
    while ([DateTime]::UtcNow -lt $deadline -and -not [System.IO.File]::Exists($requestPath)) {
        Start-Sleep -Milliseconds 50
    }
    if (-not [System.IO.File]::Exists($requestPath)) { exit 2 }
    [System.IO.File]::WriteAllText($ObservedPath, 'observed', $utf8)
    exit 0
}
finally {
    $lock.Dispose()
}
'@
[System.IO.File]::WriteAllText($fakeRunnerPath, $fakeRunnerScript, (New-Object System.Text.UTF8Encoding($false)))
$hostPath = (Get-Process -Id $PID).Path
function ConvertTo-CancelFixtureArgument {
    param([string]$Value)
    $text = if ($null -eq $Value) { '' } else { $Value }
    return '"' + $text.Replace('"', '\"') + '"'
}
$fakeStartInfo = New-Object System.Diagnostics.ProcessStartInfo
$fakeStartInfo.FileName = $hostPath
$fakeStartInfo.UseShellExecute = $false
$fakeStartInfo.CreateNoWindow = $true
$fakeStartInfo.Arguments = @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $fakeRunnerPath,
    '-RunRoot', $cancelRunRoot, '-RunId', $cancelRunId, '-NonceSha256', $cancelNonce,
    '-ReadyPath', $cancelReadyPath, '-ObservedPath', $cancelObservedPath |
        ForEach-Object { ConvertTo-CancelFixtureArgument -Value $_ }) -join ' '
$fakeRunner = [System.Diagnostics.Process]::Start($fakeStartInfo)
$cancelFixtureDeadline = [DateTime]::UtcNow.AddSeconds(10)
while ([DateTime]::UtcNow -lt $cancelFixtureDeadline -and -not [System.IO.File]::Exists($cancelReadyPath)) {
    Start-Sleep -Milliseconds 25
}
Assert-True ([System.IO.File]::Exists($cancelReadyPath)) 'The exact fake active runner must become ready.'
$runnerScriptPath = Join-Path (Split-Path -Parent $CorePath) 'Invoke-CoopTest.ps1'
$previewStartInfo = New-Object System.Diagnostics.ProcessStartInfo
$previewStartInfo.FileName = $hostPath
$previewStartInfo.UseShellExecute = $false
$previewStartInfo.CreateNoWindow = $true
$previewStartInfo.Arguments = @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $runnerScriptPath,
    '-Command', 'Recover', '-RunId', $cancelRunId |
        ForEach-Object { ConvertTo-CancelFixtureArgument -Value $_ }) -join ' '
$previewController = [System.Diagnostics.Process]::Start($previewStartInfo)
Assert-True ($previewController.WaitForExit(10000)) 'Recovery preview against an active run must terminate promptly.'
Assert-True ($previewController.ExitCode -eq 0) 'Recovery preview must remain read-only even while the runner lock is active.'
Assert-True `
    (-not [System.IO.File]::Exists((Join-Path $cancelRunRoot 'artifacts\processes\recovery.json'))) `
    'Recovery preview must not create recovery.json.'
Assert-True (-not $fakeRunner.HasExited) 'Recovery preview must not interrupt the exact active runner.'
$blockedRecoveryStartInfo = New-Object System.Diagnostics.ProcessStartInfo
$blockedRecoveryStartInfo.FileName = $hostPath
$blockedRecoveryStartInfo.UseShellExecute = $false
$blockedRecoveryStartInfo.CreateNoWindow = $true
$blockedRecoveryStartInfo.Arguments = @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $runnerScriptPath,
    '-Command', 'Recover', '-RunId', $cancelRunId, '-ApplyRecovery' |
        ForEach-Object { ConvertTo-CancelFixtureArgument -Value $_ }) -join ' '
$blockedRecoveryController = [System.Diagnostics.Process]::Start($blockedRecoveryStartInfo)
Assert-True ($blockedRecoveryController.WaitForExit(10000)) 'Blocked RecoveryV2 apply must terminate promptly.'
Assert-True ($blockedRecoveryController.ExitCode -eq 10) 'RecoveryV2 apply must return EnvironmentBlocked while the exact runner lock is active.'
Assert-True (-not $fakeRunner.HasExited) 'Blocked recovery must not interrupt the exact active runner.'
$cancelStartInfo = New-Object System.Diagnostics.ProcessStartInfo
$cancelStartInfo.FileName = $hostPath
$cancelStartInfo.UseShellExecute = $false
$cancelStartInfo.CreateNoWindow = $true
$cancelStartInfo.Arguments = @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
    $runnerScriptPath,
    '-Command', 'Cancel', '-RunId', $cancelRunId |
        ForEach-Object { ConvertTo-CancelFixtureArgument -Value $_ }) -join ' '
$cancelController = [System.Diagnostics.Process]::Start($cancelStartInfo)
Assert-True ($cancelController.WaitForExit(10000)) 'The explicit Cancel command must terminate promptly.'
Assert-True ($cancelController.ExitCode -eq 0) 'The explicit Cancel command must accept the exact active run.'
Assert-True ($fakeRunner.WaitForExit(10000)) 'The fake runner must observe the run-scoped cancellation request.'
Assert-True ([System.IO.File]::Exists($cancelObservedPath)) 'The active run must observe the exact cancellation file.'
$cancelRequest = Get-Content -LiteralPath (Join-Path $cancelRunRoot 'commands\inbox\cancel.request.json') -Raw | ConvertFrom-Json
Assert-True `
    ($cancelRequest.ProtocolMinorVersion -eq 1 -and $cancelRequest.RunId -eq $cancelRunId -and
        $cancelRequest.NonceSha256 -eq $cancelNonce -and $cancelRequest.TargetRoleInstanceId -eq 'runner-01') `
    'The explicit cancellation request must retain exact protocol, run, nonce, and target identity.'
if ([System.IO.Directory]::Exists($cancelRunRoot)) {
    Remove-Item -LiteralPath $cancelRunRoot -Recurse -Force
}

$healthNow = [DateTime]::UtcNow
$healthyStatus = [pscustomobject][ordered]@{
    SchemaVersion = 2
    ProtocolMajorVersion = 1
    ProtocolMinorVersion = 1
    HeartbeatUtc = $healthNow.ToString('O')
    LastProgressUtc = $healthNow.ToString('O')
    StateEnteredUtc = $healthNow.AddSeconds(-1).ToString('O')
    StateRevision = 2L
    AuthoritativeSource = 'RunnerContract'
}
Assert-True `
    ((Get-CoopRoleHealthClassificationCore -Status $healthyStatus -NowUtc $healthNow) -eq 'Healthy') `
    'A complete fresh RoleHealthV1 status must classify as Healthy.'
$healthyStatus.LastProgressUtc = $healthNow.AddMinutes(-5).ToString('O')
Assert-True `
    ((Get-CoopRoleHealthClassificationCore -Status $healthyStatus -NowUtc $healthNow -ProgressDeadlineSeconds 30) -eq 'NoProgress') `
    'A live role with stale progress must classify as NoProgress.'
$healthyStatus.HeartbeatUtc = $healthNow.AddMinutes(-5).ToString('O')
$healthyStatus.StateEnteredUtc = $healthNow.AddMinutes(-6).ToString('O')
Assert-True `
    ((Get-CoopRoleHealthClassificationCore -Status $healthyStatus -NowUtc $healthNow -HeartbeatDeadlineSeconds 5 -ProgressDeadlineSeconds 30) -eq 'NoHeartbeat') `
    'A stale role heartbeat must take precedence as NoHeartbeat.'

$lockFixtureRoot = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) ('locks-' + $PID)
$resourceFixtureRoot = Join-Path $lockFixtureRoot 'resource-fixture'
$productionResourceIds = @(Get-CoopSharedRuntimeResourceIdsCore `
    -AutomationRoot (Join-Path $resourceFixtureRoot 'automation') `
    -GameRoot (Join-Path $resourceFixtureRoot 'game') `
    -DedicatedServerRoot (Join-Path $resourceFixtureRoot 'dedicated') `
    -ComputerName 'fixture-machine' `
    -MachineProfileName 'fixture-profile' `
    -UdpPorts ([int[]]@(7210, 7777)))
$expectedProductionResourceIds = @(@(
        [string]::Concat('bridge-root:', ([System.IO.Path]::GetFullPath((Join-Path $resourceFixtureRoot 'automation'))).ToUpperInvariant()),
        [string]::Concat('game-install:', ([System.IO.Path]::GetFullPath((Join-Path $resourceFixtureRoot 'game'))).ToUpperInvariant()),
        [string]::Concat('dedicated-install:', ([System.IO.Path]::GetFullPath((Join-Path $resourceFixtureRoot 'dedicated'))).ToUpperInvariant()),
        'machine-profile:FIXTURE-MACHINE:FIXTURE-PROFILE',
        'udp-port:7210',
        'udp-port:7777') | Sort-Object)
Assert-True `
    ($productionResourceIds.Count -eq 6 -and
        @(Compare-Object $productionResourceIds $expectedProductionResourceIds).Count -eq 0) `
    'The production resource-id builder must return six distinct canonical resources.'
$deduplicatedPortResourceIds = @(Get-CoopSharedRuntimeResourceIdsCore `
    -AutomationRoot (Join-Path $resourceFixtureRoot 'automation') `
    -GameRoot (Join-Path $resourceFixtureRoot 'game') `
    -DedicatedServerRoot (Join-Path $resourceFixtureRoot 'dedicated') `
    -ComputerName 'fixture-machine' `
    -MachineProfileName 'fixture-profile' `
    -UdpPorts ([int[]]@(7777, 7777)))
Assert-True `
    ($deduplicatedPortResourceIds.Count -eq 5 -and
        @($deduplicatedPortResourceIds | Where-Object { $_ -eq 'udp-port:7777' }).Count -eq 1) `
    'The production resource-id builder must deduplicate an overlapping requested/default UDP port.'
$lockSet = Enter-CoopSharedRuntimeLocksCore `
    -LockRoot $lockFixtureRoot `
    -ResourceIds $productionResourceIds `
    -RunId 'runner-contract' `
    -OwnerProcessId $PID `
    -OwnerProcessStartUtc (Get-Process -Id $PID).StartTime.ToUniversalTime()
Assert-True `
    ($lockSet.Records.Count -eq 6 -and
        @(Compare-Object $productionResourceIds @($lockSet.Records | ForEach-Object { $_.ResourceId })).Count -eq 0) `
    'Shared lock acquisition must publish the canonical sorted resource order.'
foreach ($lockedResourceId in $productionResourceIds) {
    $lockConflictRejected = $false
    try {
        $null = Enter-CoopSharedRuntimeLocksCore `
            -LockRoot $lockFixtureRoot `
            -ResourceIds @($lockedResourceId) `
            -RunId 'other-run' `
            -OwnerProcessId $PID `
            -OwnerProcessStartUtc (Get-Process -Id $PID).StartTime.ToUniversalTime()
    }
    catch { $lockConflictRejected = $true }
    Assert-True $lockConflictRejected "A collision on $lockedResourceId must fail closed."
}
$lockRelease = @(Exit-CoopSharedRuntimeLocksCore -LockSet $lockSet)
Assert-True `
    ($lockRelease.Count -eq 6 -and
        @($lockRelease | Where-Object { -not $_.ReleasedAndReacquired }).Count -eq 0) `
    'Every shared runtime lock must be verified released.'

$allowedCrashPath = [System.IO.Path]::GetFullPath((Join-Path $lockFixtureRoot 'CrashUploader.Publish.exe'))
$otherPath = [System.IO.Path]::GetFullPath((Join-Path $lockFixtureRoot 'other.exe'))
$failureSnapshot = @(
    [pscustomobject]@{ ProcessId = 100; ParentProcessId = 0; ExecutablePath = $otherPath; CommandLine = 'owned-root' },
    [pscustomobject]@{ ProcessId = 101; ParentProcessId = 100; ExecutablePath = $allowedCrashPath; CommandLine = 'child-helper' },
    [pscustomobject]@{ ProcessId = 102; ParentProcessId = 0; ExecutablePath = $allowedCrashPath; CommandLine = 'unrelated-helper' },
    [pscustomobject]@{ ProcessId = 103; ParentProcessId = 0; ExecutablePath = $allowedCrashPath; CommandLine = 'WerFault -p 100' })
$failureMatches = @(Get-CoopCorrelatedFailureProcessesFromSnapshot `
    -Snapshot $failureSnapshot `
    -OwnedRootProcessIds @(100) `
    -AllowedExecutablePaths @($allowedCrashPath))
Assert-True ($failureMatches.Count -eq 2) 'Only exact path plus owned-tree/command-line PID failure helpers may correlate.'
Assert-True (-not ($failureMatches.ProcessId -contains 102)) 'An unrelated same-name/path helper must never correlate by name alone.'

$cleanupStartInfo = New-Object System.Diagnostics.ProcessStartInfo
$cleanupStartInfo.FileName = (Get-Process -Id $PID).Path
$cleanupStartInfo.UseShellExecute = $false
$cleanupStartInfo.CreateNoWindow = $true
$cleanupStartInfo.Arguments = '-NoProfile -Command "Start-Sleep -Seconds 60"'
$cleanupProcess = [System.Diagnostics.Process]::Start($cleanupStartInfo)
$cleanupIdentity = Resolve-CoopProcessObservation `
    -ProcessId $cleanupProcess.Id `
    -ExpectedExecutablePath $cleanupStartInfo.FileName `
    -ExpectedParentProcessId $PID `
    -LaunchStartedUtc ([DateTime]::UtcNow.AddSeconds(-2)) `
    -LaunchObservedUtc ([DateTime]::UtcNow.AddSeconds(2)) `
    -DeadlineMilliseconds 5000
$cleanupEvidence = Stop-CoopExactProcessIdentityCore -Identity $cleanupIdentity -GraceSeconds 1
Assert-True ($cleanupEvidence.IdentityMatched -and $cleanupEvidence.Outcome -eq 'Stopped') 'Synthetic exact owned-process cleanup must stop only the revalidated identity.'
$pidReuseIdentity = [pscustomobject][ordered]@{
    ProcessId = $PID
    ProcessStartUtc = (Get-Process -Id $PID).StartTime.ToUniversalTime().AddMinutes(-1).ToString('O')
    ExecutablePath = (Get-Process -Id $PID).Path
}
Assert-True (-not (Test-CoopLiveProcessIdentityCore -Identity $pidReuseIdentity)) 'A shifted start time must reject a possible reused PID.'
try {
    $terminalFailure = [System.InvalidOperationException]::new('Synthetic client failure.')
    $terminalFailure.Data['CoopClientJoinStatus'] = $terminalClientStatus
    throw $terminalFailure
}
catch {
    Assert-True `
        ([object]::ReferenceEquals($_.Exception.Data['CoopClientJoinStatus'], $terminalClientStatus)) `
        'PowerShell must preserve the exact terminal client status object through Exception.Data.'
}

Write-Output ('PASS ' + $PSVersionTable.PSVersion.ToString())
""";
}
