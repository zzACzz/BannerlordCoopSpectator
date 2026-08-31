using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

internal static class Program
{
    private static int Main(string[] args)
    {
        if ((args.Length == 2 || args.Length == 3) &&
            (string.Equals(args[0], "--fake-game", StringComparison.Ordinal) ||
             string.Equals(args[0], "--fake-game-delayed", StringComparison.Ordinal)))
        {
            string fakeTarget = Path.Combine(
                args[1],
                "Mount and Blade II Bannerlord",
                "Shaders",
                "CoreShaders",
                "D3D11");
            Directory.CreateDirectory(fakeTarget);
            File.WriteAllText(
                Path.Combine(fakeTarget, "shader_mapping.bin"),
                "created-by-fake-game");

            if (string.Equals(
                    args[0],
                    "--fake-game-delayed",
                    StringComparison.Ordinal))
            {
                Thread.Sleep(int.Parse(args[2]));
            }

            return 0;
        }

        string testRoot = Path.Combine(
            Path.GetTempPath(),
            "CoopShaderCacheModeSwitch.ContractTests",
            Guid.NewGuid().ToString("N"));

        try
        {
            string scriptPath = Path.Combine(
                AppContext.BaseDirectory,
                "CoopShaderCacheModeSwitch.ps1");
            string bannerlordData = Path.Combine(
                testRoot,
                "Mount and Blade II Bannerlord");
            string shadersRoot = Path.Combine(bannerlordData, "Shaders");
            string coreShadersRoot = Path.Combine(shadersRoot, "CoreShaders");
            string target = Path.Combine(coreShadersRoot, "D3D11");
            string sibling = Path.Combine(coreShadersRoot, "D3D12");
            string terrainShaders = Path.Combine(shadersRoot, "TerrainShaders");
            string sentinel = Path.Combine(bannerlordData, "keep.txt");

            Directory.CreateDirectory(target);
            Directory.CreateDirectory(sibling);
            Directory.CreateDirectory(terrainShaders);
            File.WriteAllText(Path.Combine(target, "shader_mapping.bin"), "delete-me");
            File.WriteAllText(Path.Combine(sibling, "keep.bin"), "keep-d3d12");
            File.WriteAllText(Path.Combine(terrainShaders, "keep.bin"), "keep-terrain");
            File.WriteAllText(sentinel, "keep-root");

            RunScript(scriptPath, testRoot);
            AssertFalse(
                Directory.Exists(target),
                "The exact CoreShaders\\D3D11 cache target must be removed.");
            AssertTrue(
                File.Exists(Path.Combine(sibling, "keep.bin")),
                "The adjacent CoreShaders\\D3D12 directory must be preserved.");
            AssertTrue(
                File.Exists(Path.Combine(terrainShaders, "keep.bin")),
                "Terrain shader caches must be preserved.");
            AssertTrue(
                File.Exists(sentinel),
                "Files outside the exact cache target must be preserved.");

            RunScript(scriptPath, testRoot);
            AssertTrue(
                File.Exists(sentinel),
                "A repeated cleanup must remain idempotent and preserve neighboring data.");

            Directory.CreateDirectory(target);
            File.WriteAllText(
                Path.Combine(target, "shader_mapping.bin"),
                "delete-before-wrapper-test");
            RunMultiplayerScript(scriptPath, testRoot);
            AssertFalse(
                Directory.Exists(target),
                "The wrapper must wait for the launched process and clear the cache it creates before returning.");
            AssertTrue(
                File.Exists(Path.Combine(sibling, "keep.bin")),
                "The wrapper cleanup must preserve adjacent shader directories.");

            RunWatcherScript(scriptPath, testRoot, target);
            AssertFalse(
                Directory.Exists(target),
                "The background watcher must clear cache created by a process after that process exits.");
            AssertTrue(
                File.Exists(Path.Combine(sibling, "keep.bin")),
                "The background watcher must preserve adjacent shader directories.");

            RunDetachedWatcherSurvivalTest(scriptPath, testRoot, target);
            AssertFalse(
                Directory.Exists(target),
                "The detached watcher must survive termination of the primary wrapper and clear the cache.");
            AssertTrue(
                File.Exists(Path.Combine(sibling, "keep.bin")),
                "The detached-watcher fallback must preserve adjacent shader directories.");

            string script = File.ReadAllText(scriptPath);
            AssertTrue(
                CountOccurrences(script, "Start-DetachedCleanupWatcher") >= 3,
                "RunMultiplayer must arm the detached watcher for both supported game-process paths.");

            ValidateLauncherOrder(Path.Combine(
                AppContext.BaseDirectory,
                "run_mp_with_mod_from_game_root.bat"));
            ValidateLauncherOrder(Path.Combine(
                AppContext.BaseDirectory,
                "run_mp_with_mod.bat"));

            Console.WriteLine("Coop shader-cache mode-switch contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        finally
        {
            try
            {
                if (Directory.Exists(testRoot))
                    Directory.Delete(testRoot, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static void RunDetachedWatcherSurvivalTest(
        string scriptPath,
        string programDataRoot,
        string target)
    {
        if (Directory.Exists(target))
            Directory.Delete(target, recursive: true);

        string executablePath = Environment.ProcessPath ??
            throw new InvalidOperationException(
                "Could not resolve the contract-test executable path.");
        string watcherReadyPath = Path.Combine(
            programDataRoot,
            "detached-watcher-ready.txt");
        if (File.Exists(watcherReadyPath))
            File.Delete(watcherReadyPath);
        var startInfo = CreatePowerShellStartInfo(scriptPath);
        startInfo.ArgumentList.Add("-Phase");
        startInfo.ArgumentList.Add("RunMultiplayer");
        startInfo.ArgumentList.Add("-ProgramDataRoot");
        startInfo.ArgumentList.Add(programDataRoot);
        startInfo.ArgumentList.Add("-GameExecutable");
        startInfo.ArgumentList.Add(executablePath);
        startInfo.ArgumentList.Add("-GameArguments");
        startInfo.ArgumentList.Add(
            "--fake-game-delayed \"" + programDataRoot + "\" 3000");
        startInfo.ArgumentList.Add("-GameWorkingDirectory");
        startInfo.ArgumentList.Add(AppContext.BaseDirectory);
        startInfo.ArgumentList.Add("-ChildProcessStartTimeoutSeconds");
        startInfo.ArgumentList.Add("15");
        startInfo.ArgumentList.Add("-CleanupRetrySeconds");
        startInfo.ArgumentList.Add("5");
        startInfo.ArgumentList.Add("-ContractTestWatcherReadyPath");
        startInfo.ArgumentList.Add(watcherReadyPath);

        using Process wrapper = Process.Start(startInfo) ??
            throw new InvalidOperationException(
                "Could not start the shader-cache process wrapper.");
        WaitForCondition(
            () => File.Exists(Path.Combine(target, "shader_mapping.bin")),
            TimeSpan.FromSeconds(5),
            "The delayed fake game did not create its shader cache.");
        WaitForCondition(
            () => File.Exists(watcherReadyPath),
            TimeSpan.FromSeconds(10),
            "The detached watcher did not publish its readiness signal.");
        if (wrapper.HasExited)
        {
            string earlyOutput = wrapper.StandardOutput.ReadToEnd();
            string earlyError = wrapper.StandardError.ReadToEnd();
            throw new InvalidOperationException(
                "The primary wrapper exited before the survival test could terminate it. " +
                "ExitCode=" + wrapper.ExitCode +
                " Output=" + earlyOutput +
                " Error=" + earlyError);
        }

        wrapper.Kill(entireProcessTree: false);
        wrapper.WaitForExit();

        WaitForCondition(
            () => !Directory.Exists(target),
            TimeSpan.FromSeconds(20),
            "The detached watcher did not clear the cache after the primary wrapper was terminated.");
    }

    private static void RunWatcherScript(
        string scriptPath,
        string programDataRoot,
        string target)
    {
        string executablePath = Environment.ProcessPath ??
            throw new InvalidOperationException(
                "Could not resolve the contract-test executable path.");
        var fakeGameStartInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        fakeGameStartInfo.ArgumentList.Add("--fake-game-delayed");
        fakeGameStartInfo.ArgumentList.Add(programDataRoot);
        fakeGameStartInfo.ArgumentList.Add("750");

        using Process fakeGame = Process.Start(fakeGameStartInfo) ??
            throw new InvalidOperationException("Could not start the delayed fake game.");
        try
        {
            var startInfo = CreatePowerShellStartInfo(scriptPath);
            startInfo.ArgumentList.Add("-Phase");
            startInfo.ArgumentList.Add("WaitForMultiplayerExit");
            startInfo.ArgumentList.Add("-ProgramDataRoot");
            startInfo.ArgumentList.Add(programDataRoot);
            startInfo.ArgumentList.Add("-GameProcessId");
            startInfo.ArgumentList.Add(fakeGame.Id.ToString());
            startInfo.ArgumentList.Add("-GameProcessStartTimeUtcTicks");
            startInfo.ArgumentList.Add(
                fakeGame.StartTime.ToUniversalTime().Ticks.ToString());
            startInfo.ArgumentList.Add("-CleanupRetrySeconds");
            startInfo.ArgumentList.Add("5");

            RunPowerShell(startInfo, "Background shader-cache watcher");
        }
        finally
        {
            if (!fakeGame.HasExited)
                fakeGame.Kill(entireProcessTree: true);
        }

        AssertFalse(
            Directory.Exists(target),
            "The watcher must return only after the delayed fake game exits and its cache is removed.");
    }

    private static void RunMultiplayerScript(
        string scriptPath,
        string programDataRoot)
    {
        string executablePath = Environment.ProcessPath ??
            throw new InvalidOperationException(
                "Could not resolve the contract-test executable path.");
        var startInfo = CreatePowerShellStartInfo(scriptPath);
        startInfo.ArgumentList.Add("-Phase");
        startInfo.ArgumentList.Add("RunMultiplayer");
        startInfo.ArgumentList.Add("-ProgramDataRoot");
        startInfo.ArgumentList.Add(programDataRoot);
        startInfo.ArgumentList.Add("-GameExecutable");
        startInfo.ArgumentList.Add(executablePath);
        startInfo.ArgumentList.Add("-GameArguments");
        startInfo.ArgumentList.Add("--fake-game \"" + programDataRoot + "\"");
        startInfo.ArgumentList.Add("-GameWorkingDirectory");
        startInfo.ArgumentList.Add(AppContext.BaseDirectory);
        startInfo.ArgumentList.Add("-ChildProcessStartTimeoutSeconds");
        startInfo.ArgumentList.Add("0");

        RunPowerShell(startInfo, "Shader-cache process wrapper");
    }

    private static void RunScript(string scriptPath, string programDataRoot)
    {
        var startInfo = CreatePowerShellStartInfo(scriptPath);
        startInfo.ArgumentList.Add("-Phase");
        startInfo.ArgumentList.Add("ContractTest");
        startInfo.ArgumentList.Add("-ProgramDataRoot");
        startInfo.ArgumentList.Add(programDataRoot);

        RunPowerShell(startInfo, "Shader-cache cleanup script");
    }

    private static ProcessStartInfo CreatePowerShellStartInfo(string scriptPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        return startInfo;
    }

    private static void RunPowerShell(
        ProcessStartInfo startInfo,
        string operationName)
    {
        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start powershell.exe.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                operationName + " failed. ExitCode=" +
                process.ExitCode + " Output=" + output + " Error=" + error);
        }
    }

    private static void ValidateLauncherOrder(string launcherPath)
    {
        string launcher = File.ReadAllText(launcherPath);
        int wrapperPhase = launcher.IndexOf(
            "-Phase RunMultiplayer",
            StringComparison.OrdinalIgnoreCase);
        int gameLaunch = launcher.IndexOf(
            "-GameArguments \"/multiplayer",
            StringComparison.OrdinalIgnoreCase);

        AssertTrue(
            launcher.Contains(
                "Modules\\CoopSpectator\\CoopShaderCacheModeSwitch.ps1",
                StringComparison.OrdinalIgnoreCase),
            "Launcher must resolve the helper from the packaged CoopSpectator module: " +
            launcherPath);
        AssertTrue(
            wrapperPhase >= 0 && gameLaunch > wrapperPhase,
            "Launcher must delegate the complete multiplayer lifecycle to the cache wrapper: " +
            launcherPath);
        AssertFalse(
            launcher.Contains(
                "-Phase BeforeMultiplayer",
                StringComparison.OrdinalIgnoreCase) ||
            launcher.Contains(
                "-Phase AfterMultiplayer",
                StringComparison.OrdinalIgnoreCase),
            "Launcher must not run early standalone cleanup phases: " + launcherPath);
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(
                    value,
                    index,
                    StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static void WaitForCondition(
        Func<bool> condition,
        TimeSpan timeout,
        string failureMessage)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (condition())
                return;

            Thread.Sleep(50);
        }

        throw new InvalidOperationException(failureMessage);
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message)
    {
        AssertTrue(!condition, message);
    }
}
