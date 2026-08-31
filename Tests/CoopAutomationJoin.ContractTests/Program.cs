using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.Automation;

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
            ValidateNoServerMatch();
            ValidateAmbiguousServerMatch();
            ValidateTerminalStates();
            ValidateStrictAtomicStatusWrite();
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
        request.UniqueMapId = "battle_terrain_a";
        IReadOnlyList<CoopAutomationServerDescriptor> servers = new[]
        {
            Server("wrong-game-type", "AC_COOP", 7210, "Battle", "battle_terrain_a"),
            Server("wrong-map", "AC_COOP", 7210, "TeamDeathmatch", "battle_terrain_b"),
            Server("selected", "AC_COOP", 7210, "TeamDeathmatch", "battle_terrain_a")
        };

        CoopAutomationServerSelection selection = CoopAutomationJoinContract.SelectExactServer(request, servers);
        Assert(
            selection.Status == CoopAutomationServerSelectionStatus.Selected && selection.SelectedIndex == 2,
            "Declared game-type and unique-map filters must participate in exact selection.");
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
        string uniqueMapId = "battle_terrain_a")
    {
        return new CoopAutomationServerDescriptor
        {
            Id = id,
            ServerName = name,
            Address = "203.0.113.1",
            Port = port,
            GameType = gameType,
            Map = uniqueMapId,
            UniqueMapId = uniqueMapId,
            PasswordProtected = false
        };
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
