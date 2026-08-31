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
        Assert(File.Exists(corePath), "Runner core helper must exist: " + corePath);
        Assert(File.Exists(runnerPath), "Aggregate runner must exist: " + runnerPath);
        ValidateRunnerIntegration(runnerPath);

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

    private static void ValidateRunnerIntegration(string runnerPath)
    {
        string source = File.ReadAllText(runnerPath);
        Assert(
            source.Contains(". $runnerCorePath", StringComparison.Ordinal),
            "Aggregate runner must dot-source the tested core helper.");
        Assert(
            source.Contains("$serverCommands = @(New-CoopDedicatedBootstrapCommands", StringComparison.Ordinal),
            "Aggregate runner must use the tested discrete bootstrap-command builder.");
        Assert(
            source.Contains("Get-CoopDescendantProcessRecordsFromSnapshot", StringComparison.Ordinal),
            "Aggregate runner must use the tested in-memory process-tree traversal.");
        Assert(
            source.Contains("Get-CimInstance -ClassName Win32_Process -OperationTimeoutSec 10", StringComparison.Ordinal),
            "Aggregate runner process snapshots must use a bounded CIM operation timeout.");
        Assert(
            !source.Contains("foreach ($serverCommand in @(\n", StringComparison.Ordinal) &&
            !source.Contains("foreach ($serverCommand in @(\r\n", StringComparison.Ordinal),
            "Aggregate runner must not reconstruct the comma-precedence bootstrap bug.");
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

$commands = @(New-CoopDedicatedBootstrapCommands `
    -ServerName 'AC_COOP_CONTRACT' `
    -MaxNumberOfPlayers 16 `
    -GameType 'TeamDeathmatch' `
    -Map 'mp_tdm_map_001')
$expectedCommands = @(
    'ServerName AC_COOP_CONTRACT',
    'MaxNumberOfPlayers 16',
    'GameType TeamDeathmatch',
    'Map mp_tdm_map_001',
    'add_map_to_usable_maps mp_tdm_map_001 TeamDeathmatch',
    'start_game')
Assert-True ($commands.Count -eq 6) 'Bootstrap must contain exactly six commands.'
for ($index = 0; $index -lt $expectedCommands.Count; $index++) {
    Assert-True ([string]::Equals($commands[$index], $expectedCommands[$index], [StringComparison]::Ordinal)) `
        ('Bootstrap command mismatch at index ' + $index + ': ' + $commands[$index])
}

$alternate = @(New-CoopDedicatedBootstrapCommands `
    -ServerName 'AC_COOP_ALTERNATE' `
    -MaxNumberOfPlayers 32 `
    -GameType 'AlternateMode' `
    -Map 'alternate_map')
Assert-True ($alternate.Count -eq 6) 'Alternate bootstrap must remain six discrete commands.'
Assert-True ($alternate[2] -eq 'GameType AlternateMode') 'Game type must remain a distinct dynamic command.'
Assert-True ($alternate[4] -eq 'add_map_to_usable_maps alternate_map AlternateMode') 'Usable-map command must retain both dynamic identities.'

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

Write-Output ('PASS ' + $PSVersionTable.PSVersion.ToString())
""";
}
