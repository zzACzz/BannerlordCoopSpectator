using System;
using System.IO;
using CoopSpectator.Infrastructure;

internal static class ReplicaInputFocusTests
{
    internal static void Run()
    {
        // Exhaust all admission combinations. Foreign focus, menus, dialogue,
        // inquiry, loading, inactive layers or another top screen must each veto.
        for (int mask = 0; mask < 256; mask++)
        {
            bool actual = CoopCampaignMapInputFocusPolicy.CanRestore(
                (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0,
                (mask & 16) != 0, (mask & 32) != 0, (mask & 64) != 0, (mask & 128) != 0);
            Check(actual == (mask == 135), "focus admission combination " + mask);
        }
        string view = File.ReadAllText(Path.Combine("UI", "CoopCampaignMapPrototypeMissionView.cs"));
        int restore = view.IndexOf("RestoreReplicaInputFocus();", StringComparison.Ordinal);
        Check(restore >= 0 && restore < view.IndexOf("HandleReplicaEscapeInput();", StringComparison.Ordinal) &&
            restore < view.IndexOf("TickCampaignMenu();", StringComparison.Ordinal), "restore before input and modal visibility decisions");
        int start = view.IndexOf("private void RestoreReplicaInputFocus()", StringComparison.Ordinal);
        int end = view.IndexOf("private void HandleReplicaEscapeInput()", start, StringComparison.Ordinal);
        string adapter = view.Substring(start, end - start);
        Check(adapter.Contains("ReferenceEquals(focused, MissionScreen.SceneLayer)") &&
            adapter.Contains("focused == null") && adapter.Contains("IsReplicaModalOpen()") &&
            adapter.Contains("ScreenManager.TrySetFocus(_replicaInfoLayer)") &&
            !adapter.Contains("TryLoseFocus") && !adapter.Contains("SetSuspendLayer"), "bounded recovery never dismisses or reactivates foreign layers");
        Check(view.Contains("MissionEscapeMenuView") && view.Contains("InformationManager.IsAnyInquiryActive()") &&
            view.Contains("layer.Name, \"MissionOptions\"") && view.Contains("ReferenceEquals(ScreenManager.TopScreen, MissionScreen)"),
            "native modal guards remain explicit");
        Console.WriteLine("Replica input focus admission passed: 256 combinations; native acceptance pending");
    }
    private static void Check(bool value, string message)
    { if (!value) throw new Exception("Replica focus: " + message); }
}
