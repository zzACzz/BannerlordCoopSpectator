using System;
using System.IO;
using CoopSpectator.Infrastructure;

internal static class LobbyLifetimeTests
{
    internal static void Run()
    {
        for (int mask = 0; mask < 8; mask++)
        {
            bool server = (mask & 1) != 0;
            bool map = (mask & 2) != 0;
            bool playing = (mask & 4) != 0;
            Check(CoopCampaignMapLobbyLifetimePolicy.ShouldRunNativeTick(server, map, playing)
                == (mask != 7), "only the server's playing replica map is excluded");
        }
        // No deadline, peer-count, or mutable static policy state: an expired
        // match timer and a disconnect/rejoin cannot acquire map end authority.
        for (int tick = 0; tick < 10000; tick++)
            Check(!CoopCampaignMapLobbyLifetimePolicy.ShouldRunNativeTick(true, true, true),
                "long-lived map remains owned by campaign lifecycle");
        Check(CoopCampaignMapLobbyLifetimePolicy.ShouldRunNativeTick(true, true, false),
            "explicit Ending after long map still executes native tick");
        Check(CoopCampaignMapLobbyLifetimePolicy.ShouldRunNativeTick(true, false, true),
            "next battle's playing tick is unaffected");

        string patch = File.ReadAllText(Path.Combine("GameMode", "CoopCampaignMapLobbyLifetimePatch.cs"));
        Check(patch.Contains("__instance?.Mission?.GetMissionBehavior<") &&
              patch.Contains("MissionMultiplayerCoopCampaignMapPrototype>() != null") &&
              patch.Contains("GameNetwork.IsServer") &&
              patch.Contains("MissionLobbyComponent.MultiplayerGameState.Playing"),
              "runtime adapter binds exact map behavior on invoked lobby and server/Playing gates");
        Check(!patch.Contains("OnUdpNetworkHandlerTick") && !patch.Contains("SetStateEndingAsServer") &&
              !patch.Contains("finalizer:") && !patch.Contains("ModLogger."),
              "do not replace native ending, swallow exceptions, or log on tick");
        string mode = File.ReadAllText(Path.Combine("GameMode", "MissionMultiplayerCoopCampaignMapPrototypeMode.cs"));
        int guard = mode.IndexOf("CoopCampaignMapLobbyLifetimePatch.EnsureApplied();", StringComparison.Ordinal);
        Check(guard >= 0 && guard < mode.IndexOf("MissionState.OpenNew", StringComparison.Ordinal) &&
              mode.Substring(0, guard).Contains("if (GameNetwork.IsServer)"),
              "guard binds before the map can be opened on server");
        Console.WriteLine("Lobby lifetime contract checks passed: 10013.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
