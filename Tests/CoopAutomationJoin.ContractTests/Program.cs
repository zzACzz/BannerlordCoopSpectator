using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.Automation;
using CoopSpectator.Multiplayer.Automation;

internal static class Program
{
    private const string ModuleHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string OtherHash = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    private static int Main()
    {
        try
        {
            ValidateRunIdRules();
            ValidateAcceptedRequest();
            ValidateRunTokenMismatch();
            ValidateModuleHashMismatch();
            ValidateExpiredRequest();
            ValidateExcessiveLifetime();
            ValidateExactServerSelection();
            ValidateOptionalServerFilters();
            ValidateMapIdIsNotUniqueMapId();
            ValidateNoServerMatch();
            ValidateAmbiguousServerMatch();
            ValidateTerminalStates();
            ValidateStrictAtomicStatusWrite();
            ValidateNetworkMainAssemblyResolution();
            ValidateLobbyStateAssemblyResolution();
            ValidatePlatformLoginContextAndInvocation();
            ValidateOneShotPlatformLoginControllerGuard();
            ValidateNetworkHandoffPatchOwnership();
            Console.WriteLine("Coop automation join contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void ValidateRunIdRules()
    {
        Assert(CoopAutomationJoinContract.IsValidRunId("M2B-20260831_001.test"), "A bounded ASCII RunId must be accepted.");
        Assert(!CoopAutomationJoinContract.IsValidRunId("../escape"), "Path traversal must not be accepted as a RunId.");
        Assert(!CoopAutomationJoinContract.IsValidRunId(" run"), "Whitespace must not be accepted in a RunId.");
        Assert(!CoopAutomationJoinContract.IsValidRunId(new string('a', 81)), "A RunId longer than 80 characters must be rejected.");
    }

    private static void ValidateAcceptedRequest()
    {
        DateTime now = DateTime.UtcNow;
        CoopAutomationJoinRequest request = ValidRequest(now);
        Assert(
            CoopAutomationJoinContract.TryValidateRequest(
                request,
                request.RunId,
                request.RunTokenSha256,
                ModuleHash,
                now,
                out string code,
                out string message),
            "A fully bound request must be accepted, but failed with " + code + ": " + message);
    }

    private static void ValidateRunTokenMismatch()
    {
        DateTime now = DateTime.UtcNow;
        CoopAutomationJoinRequest request = ValidRequest(now);
        bool accepted = CoopAutomationJoinContract.TryValidateRequest(
            request,
            request.RunId,
            OtherHash,
            ModuleHash,
            now,
            out string code,
            out _);
        Assert(!accepted && code == "RunTokenMismatch", "A different run-token hash must be rejected.");
    }

    private static void ValidateModuleHashMismatch()
    {
        DateTime now = DateTime.UtcNow;
        CoopAutomationJoinRequest request = ValidRequest(now);
        bool accepted = CoopAutomationJoinContract.TryValidateRequest(
            request,
            request.RunId,
            request.RunTokenSha256,
            OtherHash,
            now,
            out string code,
            out _);
        Assert(!accepted && code == "ClientModuleHashMismatch", "A different loaded module hash must be rejected.");
    }

    private static void ValidateExpiredRequest()
    {
        DateTime now = DateTime.UtcNow;
        CoopAutomationJoinRequest request = ValidRequest(now.AddMinutes(-5));
        request.ExpiresUtc = now.AddSeconds(-1);
        bool accepted = CoopAutomationJoinContract.TryValidateRequest(
            request,
            request.RunId,
            request.RunTokenSha256,
            ModuleHash,
            now,
            out string code,
            out _);
        Assert(!accepted && code == "RequestExpired", "An expired request must be rejected.");
    }

    private static void ValidateExcessiveLifetime()
    {
        DateTime now = DateTime.UtcNow;
        CoopAutomationJoinRequest request = ValidRequest(now);
        request.ExpiresUtc = request.CreatedUtc.AddMinutes(31);
        bool accepted = CoopAutomationJoinContract.TryValidateRequest(
            request,
            request.RunId,
            request.RunTokenSha256,
            ModuleHash,
            now,
            out string code,
            out _);
        Assert(!accepted && code == "LifetimeTooLong", "A request lifetime over 30 minutes must be rejected.");
    }

    private static void ValidateExactServerSelection()
    {
        CoopAutomationJoinRequest request = ValidRequest(DateTime.UtcNow);
        IReadOnlyList<CoopAutomationServerDescriptor> servers = new[]
        {
            Server("wrong-name", "AC_COOP_2", 7210),
            Server("wrong-port", "AC_COOP", 7777),
            Server("selected", "AC_COOP", 7210)
        };

        CoopAutomationServerSelection selection = CoopAutomationJoinContract.SelectExactServer(request, servers);
        Assert(
            selection.Status == CoopAutomationServerSelectionStatus.Selected &&
            selection.SelectedIndex == 2 &&
            selection.MatchingCount == 1,
            "Only the exact server name and port may be selected.");
    }

    private static void ValidateOptionalServerFilters()
    {
        CoopAutomationJoinRequest request = ValidRequest(DateTime.UtcNow);
        request.GameType = "TeamDeathmatch";
        request.UniqueMapId = ":ut[5]token:rev[8]revision";
        IReadOnlyList<CoopAutomationServerDescriptor> servers = new[]
        {
            Server(
                "wrong-game-type",
                "AC_COOP",
                7210,
                gameType: "Battle",
                map: "mp_tdm_map_001",
                uniqueMapId: ":ut[5]token:rev[8]revision"),
            Server(
                "wrong-unique-map-id",
                "AC_COOP",
                7210,
                gameType: "TeamDeathmatch",
                map: "mp_tdm_map_001",
                uniqueMapId: ":ut[5]other:rev[8]revision"),
            Server(
                "selected",
                "AC_COOP",
                7210,
                gameType: "TeamDeathmatch",
                map: "mp_tdm_map_001",
                uniqueMapId: ":ut[5]token:rev[8]revision")
        };

        CoopAutomationServerSelection selection = CoopAutomationJoinContract.SelectExactServer(request, servers);
        Assert(
            selection.Status == CoopAutomationServerSelectionStatus.Selected && selection.SelectedIndex == 2,
            "Declared game-type and unique-map filters must participate in exact selection.");
    }

    private static void ValidateMapIdIsNotUniqueMapId()
    {
        CoopAutomationJoinRequest request = ValidRequest(DateTime.UtcNow);
        request.GameType = "TeamDeathmatch";
        request.UniqueMapId = "mp_tdm_map_001";

        CoopAutomationServerSelection selection = CoopAutomationJoinContract.SelectExactServer(
            request,
            new[]
            {
                Server(
                    "native-server",
                    "AC_COOP",
                    7210,
                    gameType: "TeamDeathmatch",
                    map: "mp_tdm_map_001",
                    uniqueMapId: ":ut[5]token:rev[8]revision")
            });

        Assert(
            selection.Status == CoopAutomationServerSelectionStatus.None,
            "A native map name must not be treated as the serialized UniqueMapId value.");
    }

    private static void ValidateNoServerMatch()
    {
        CoopAutomationJoinRequest request = ValidRequest(DateTime.UtcNow);
        CoopAutomationServerSelection selection = CoopAutomationJoinContract.SelectExactServer(
            request,
            new[] { Server("other", "OTHER", 7210) });
        Assert(
            selection.Status == CoopAutomationServerSelectionStatus.None && selection.SelectedIndex == -1,
            "An absent exact server must remain a no-match result.");
    }

    private static void ValidateAmbiguousServerMatch()
    {
        CoopAutomationJoinRequest request = ValidRequest(DateTime.UtcNow);
        CoopAutomationServerSelection selection = CoopAutomationJoinContract.SelectExactServer(
            request,
            new[]
            {
                Server("one", "AC_COOP", 7210),
                Server("two", "AC_COOP", 7210)
            });
        Assert(
            selection.Status == CoopAutomationServerSelectionStatus.Ambiguous && selection.MatchingCount == 2,
            "Duplicate exact lobby matches must fail as ambiguous rather than picking the first server.");
    }

    private static void ValidateTerminalStates()
    {
        Assert(CoopAutomationJoinContract.IsTerminalState("Connected"), "Connected must be terminal.");
        Assert(CoopAutomationJoinContract.IsTerminalState("Failed"), "Failed must be terminal.");
        Assert(CoopAutomationJoinContract.IsTerminalState("Cancelled"), "Cancelled must be terminal.");
        Assert(!CoopAutomationJoinContract.IsTerminalState("JoinRequested"), "JoinRequested must remain non-terminal.");
    }

    private static void ValidateStrictAtomicStatusWrite()
    {
        string directory = Path.Combine(Path.GetTempPath(), "CoopAutomationJoin.ContractTests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "status.json");
        try
        {
            AtomicBridgeFileIO.WriteAllLinesStrictAtomic(path, new[] { "first" });
            AtomicBridgeFileIO.WriteAllLinesStrictAtomic(path, new[] { "second", "complete" });
            string[] lines = File.ReadAllLines(path);
            Assert(lines.SequenceEqual(new[] { "second", "complete" }), "Strict atomic replacement must expose only the complete replacement content.");
            Assert(!Directory.GetFiles(directory, "*.tmp").Any(), "Strict atomic replacement must not leave temporary files after success.");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static void ValidateNetworkMainAssemblyResolution()
    {
        AssemblyBuilder wrongAssembly = DefineNetworkMainAssembly("TaleWorlds.MountAndBlade.Multiplayer.ContractTest");
        Assert(
            !CoopLobbyAutomationDriver.TryResolveNetworkMainType(
                new Assembly[] { wrongAssembly },
                out _,
                out string wrongAssemblyFailure) &&
            wrongAssemblyFailure.Contains(CoopLobbyAutomationDriver.NetworkMainAssemblyName),
            "NetworkMain resolution must reject the historical multiplayer-assembly mismatch.");

        AssemblyBuilder coreAssembly = DefineNetworkMainAssembly(CoopLobbyAutomationDriver.NetworkMainAssemblyName);
        Assert(
            CoopLobbyAutomationDriver.TryResolveNetworkMainType(
                new Assembly[] { wrongAssembly, coreAssembly },
                out Type resolvedType,
                out string resolutionFailure) &&
            string.Equals(
                resolvedType?.FullName,
                CoopLobbyAutomationDriver.NetworkMainTypeName,
                StringComparison.Ordinal),
            "NetworkMain must resolve from TaleWorlds.MountAndBlade, but failed with: " + resolutionFailure);
    }

    private static void ValidateLobbyStateAssemblyResolution()
    {
        AssemblyBuilder wrongAssembly = DefineTypeAssembly(
            "TaleWorlds.MountAndBlade.ContractTest",
            CoopLobbyAutomationDriver.LobbyStateTypeName);
        Assert(
            !CoopLobbyAutomationDriver.TryResolveLobbyStateType(
                new Assembly[] { wrongAssembly },
                out _,
                out string wrongAssemblyFailure) &&
            wrongAssemblyFailure.Contains(CoopLobbyAutomationDriver.LobbyStateAssemblyName),
            "LobbyState resolution must reject a type from the wrong assembly.");

        AssemblyBuilder multiplayerAssembly = DefineTypeAssembly(
            CoopLobbyAutomationDriver.LobbyStateAssemblyName,
            CoopLobbyAutomationDriver.LobbyStateTypeName);
        Assert(
            CoopLobbyAutomationDriver.TryResolveLobbyStateType(
                new Assembly[] { wrongAssembly, multiplayerAssembly },
                out Type resolvedType,
                out string resolutionFailure) &&
            string.Equals(resolvedType?.FullName, CoopLobbyAutomationDriver.LobbyStateTypeName, StringComparison.Ordinal),
            "LobbyState must resolve from TaleWorlds.MountAndBlade.Multiplayer, but failed with: " + resolutionFailure);
    }

    private static void ValidatePlatformLoginContextAndInvocation()
    {
        var lobbyClient = new FakeLobbyClient { CurrentState = "Idle" };
        var loginCompletion = new TaskCompletionSource<bool>();
        var lobbyState = new FakeLobbyState(lobbyClient, loginCompletion.Task)
        {
            IsLoggingIn = false,
            HasMultiplayerPrivilege = null
        };
        FakeGame.Current = new FakeGame
        {
            GameStateManager = new FakeGameStateManager { ActiveState = lobbyState }
        };

        Assert(
            CoopLobbyAutomationDriver.TryGetPlatformLoginContext(
                typeof(FakeGame),
                typeof(FakeLobbyState),
                lobbyClient,
                out CoopPlatformLoginContext context,
                out string contextFailureCode,
                out string contextFailureMessage),
            "The exact active LobbyState context must be accepted, but failed with " +
            contextFailureCode + ": " + contextFailureMessage);
        Assert(
            ReferenceEquals(context.LobbyState, lobbyState) &&
            ReferenceEquals(context.LobbyClient, lobbyClient) &&
            context.LobbyClientState == "Idle" &&
            !context.IsLoggingIn &&
            !context.HasMultiplayerPrivilege.HasValue,
            "The platform login context must preserve exact object identity and nullable privilege state.");

        Assert(
            CoopLobbyAutomationDriver.TryStartPlatformLogin(
                context,
                out System.Threading.Tasks.Task loginTask,
                out string startFailureCode,
                out string startFailureMessage),
            "The valid stock TryLogin path must start, but failed with " + startFailureCode + ": " + startFailureMessage);
        Assert(
            ReferenceEquals(loginTask, loginCompletion.Task) && lobbyState.TryLoginCallCount == 1,
            "The driver must return the exact native login task and invoke TryLogin exactly once per start request.");

        lobbyState.IsLoggingIn = false;
        lobbyState.HasMultiplayerPrivilege = false;
        Assert(
            CoopLobbyAutomationDriver.TryGetPlatformLoginContext(
                typeof(FakeGame),
                typeof(FakeLobbyState),
                lobbyClient,
                out context,
                out _,
                out _) &&
            !CoopLobbyAutomationDriver.TryStartPlatformLogin(
                context,
                out _,
                out string privilegeFailureCode,
                out _) &&
            privilegeFailureCode == "PlatformLoginPrivilegeDenied" &&
            lobbyState.TryLoginCallCount == 1,
            "A known privilege denial must be terminal before another native login invocation.");

        lobbyState.HasMultiplayerPrivilege = true;
        lobbyState.IsLoggingIn = true;
        Assert(
            CoopLobbyAutomationDriver.TryGetPlatformLoginContext(
                typeof(FakeGame),
                typeof(FakeLobbyState),
                lobbyClient,
                out context,
                out _,
                out _) &&
            !CoopLobbyAutomationDriver.TryStartPlatformLogin(
                context,
                out _,
                out string activeFailureCode,
                out _) &&
            activeFailureCode == "PlatformLoginAlreadyActive" &&
            lobbyState.TryLoginCallCount == 1,
            "An already active native login must not be invoked again.");

        FakeGame.Current.GameStateManager.ActiveState = new FakeOtherState();
        Assert(
            !CoopLobbyAutomationDriver.TryGetPlatformLoginContext(
                typeof(FakeGame),
                typeof(FakeLobbyState),
                lobbyClient,
                out _,
                out string stateFailureCode,
                out _) &&
            stateFailureCode == "PlatformLoginLobbyStateNotActive",
            "A non-LobbyState active state must remain a pending precondition rather than a login target.");

        FakeGame.Current.GameStateManager.ActiveState = lobbyState;
        var differentClient = new FakeLobbyClient { CurrentState = "Idle" };
        Assert(
            !CoopLobbyAutomationDriver.TryGetPlatformLoginContext(
                typeof(FakeGame),
                typeof(FakeLobbyState),
                differentClient,
                out _,
                out string identityFailureCode,
                out _) &&
            identityFailureCode == "PlatformLoginLobbyClientMismatch",
            "The active LobbyState client must be reference-equal to NetworkMain.GameClient.");
    }

    private static void ValidateOneShotPlatformLoginControllerGuard()
    {
        string repositoryRoot = ResolveRepositoryRoot();
        string controllerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Multiplayer",
            "Automation",
            "CoopLobbyAutomationController.cs"));
        Assert(
            controllerSource.Contains("if (_platformLoginAttempted)", StringComparison.Ordinal) &&
            controllerSource.Contains("_platformLoginAttempted = true;", StringComparison.Ordinal) &&
            controllerSource.Contains("_platformLoginAttempted || _platformLoginTask != null", StringComparison.Ordinal),
            "The controller must gate retries and safe cancellation after the one native login attempt.");
        Assert(
            controllerSource.Contains("PlatformLoginFaulted", StringComparison.Ordinal) &&
            controllerSource.Contains("PlatformLoginCancelled", StringComparison.Ordinal) &&
            controllerSource.Contains("PlatformLoginPrivilegeDenied", StringComparison.Ordinal) &&
            controllerSource.Contains("PlatformLoginStillIdle", StringComparison.Ordinal),
            "The controller must preserve distinct native login terminal outcomes.");
        Assert(
            CoopAutomationJoinContract.CurrentSchemaVersion == 3 &&
            typeof(CoopAutomationJoinStatus).GetProperty("PlatformLoginAttempted") != null &&
            typeof(CoopAutomationJoinStatus).GetProperty("PlatformLoginTaskState") != null &&
            typeof(CoopAutomationJoinStatus).GetProperty("PlatformLoginOutcome") != null,
            "Schema 3 must publish explicit platform login evidence.");
    }

    private static void ValidateNetworkHandoffPatchOwnership()
    {
        string repositoryRoot = ResolveRepositoryRoot();
        string networkPatchSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Patches",
            "LocalJoinAddressPatch.cs"));
        string legacyLobbyPatchSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Patches",
            "LobbyCustomGameLocalJoinPatch.cs"));

        int targetIndex = networkPatchSource.IndexOf(
            "[HarmonyPatch(\"StartMultiplayerOnClient\")]",
            StringComparison.Ordinal);
        int redirectIndex = networkPatchSource.IndexOf(
            "HostSelfJoinRedirectState.TryConsumeLoopbackRewrite",
            targetIndex,
            StringComparison.Ordinal);
        int notificationIndex = networkPatchSource.IndexOf(
            "CoopLobbyAutomationController.NotifyStartMultiplayerHandoff",
            redirectIndex,
            StringComparison.Ordinal);
        Assert(
            targetIndex >= 0 && redirectIndex > targetIndex && notificationIndex > redirectIndex,
            "The authoritative GameNetwork handoff patch must notify automation after applying the final address rewrite.");
        Assert(
            !legacyLobbyPatchSource.Contains(
                "CoopLobbyAutomationController.NotifyStartMultiplayerHandoff",
                StringComparison.Ordinal),
            "The optional legacy lobby reflection patch must not duplicate the authoritative GameNetwork handoff notification.");
    }

    private static AssemblyBuilder DefineNetworkMainAssembly(string assemblyName)
    {
        return DefineTypeAssembly(assemblyName, CoopLobbyAutomationDriver.NetworkMainTypeName);
    }

    private static AssemblyBuilder DefineTypeAssembly(string assemblyName, string typeName)
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName), AssemblyBuilderAccess.Run);
        ModuleBuilder module = assembly.DefineDynamicModule(assemblyName + ".dll");
        module.DefineType(typeName, TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed).CreateType();
        return assembly;
    }

    private static string ResolveRepositoryRoot()
    {
        string configured = Environment.GetEnvironmentVariable("COOPSPECTATOR_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        DirectoryInfo directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CoopSpectator.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be resolved from the contract-test output path.");
    }

    private static CoopAutomationJoinRequest ValidRequest(DateTime now)
    {
        string tokenHash = CoopAutomationJoinContract.ComputeSha256Hex("0123456789abcdef0123456789abcdef");
        return new CoopAutomationJoinRequest
        {
            SchemaVersion = CoopAutomationJoinContract.CurrentSchemaVersion,
            ProtocolMajorVersion = CoopAutomationRunContract.CurrentProtocolMajorVersion,
            ProtocolMinorVersion = CoopAutomationRunContract.CurrentProtocolMinorVersion,
            RunId = "M2B-automation-join-test",
            Sequence = 1,
            CommandId = Guid.NewGuid().ToString("D"),
            SourceRoleType = "Runner",
            SourceRoleInstanceId = "runner-01",
            TargetRoleType = "MultiplayerClient",
            TargetRoleInstanceId = "multiplayer-client-01",
            CreatedUtc = now,
            ExpiresUtc = now.AddMinutes(10),
            RunTokenSha256 = tokenHash,
            ExpectedClientModuleSha256 = ModuleHash,
            ServerName = "AC_COOP",
            ServerPort = 7210,
            GameType = string.Empty,
            UniqueMapId = string.Empty,
            RequireLocalHostOwnership = true,
            PasswordProvided = false,
            RequestedBy = "contract-test"
        };
    }

    private static CoopAutomationServerDescriptor Server(
        string id,
        string name,
        int port,
        string gameType = "TeamDeathmatch",
        string map = "mp_tdm_map_001",
        string uniqueMapId = ":ut[5]token:rev[8]revision")
    {
        return new CoopAutomationServerDescriptor
        {
            Id = id,
            ServerName = name,
            Address = "203.0.113.1",
            Port = port,
            GameType = gameType,
            Map = map,
            UniqueMapId = uniqueMapId,
            PasswordProtected = false
        };
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class FakeLobbyClient
    {
        public string CurrentState { get; set; }
    }

    private sealed class FakeLobbyState
    {
        private readonly System.Threading.Tasks.Task _loginTask;

        public FakeLobbyState(FakeLobbyClient lobbyClient, System.Threading.Tasks.Task loginTask)
        {
            LobbyClient = lobbyClient;
            _loginTask = loginTask;
        }

        public FakeLobbyClient LobbyClient { get; }
        public bool IsLoggingIn { get; set; }
        public bool? HasMultiplayerPrivilege { get; set; }
        public int TryLoginCallCount { get; private set; }

        public System.Threading.Tasks.Task TryLogin()
        {
            TryLoginCallCount++;
            IsLoggingIn = true;
            return _loginTask;
        }
    }

    private sealed class FakeOtherState
    {
    }

    private sealed class FakeGameStateManager
    {
        public object ActiveState { get; set; }
    }

    private sealed class FakeGame
    {
        public static FakeGame Current { get; set; }
        public FakeGameStateManager GameStateManager { get; set; }
    }
}
