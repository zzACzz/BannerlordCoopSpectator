using System;
using System.IO;
using CoopSpectator.Infrastructure;

internal static class VisualTickReadinessTests
{
    internal static void Run()
    {
        // Test the lifecycle in order: first load, resource reload, an options
        // screen, recovery, failed probing and teardown. Ready history must not
        // authorize a later unavailable native scene. Applies to either map.
        Expect(true, true, false, false, false, CoopCampaignMapVisualTickAction.AdvanceLoading);
        Expect(true, true, true, true, false, CoopCampaignMapVisualTickAction.UpdateVisuals);
        Expect(true, true, false, true, false, CoopCampaignMapVisualTickAction.AdvanceLoading);
        Expect(true, true, true, false, false, CoopCampaignMapVisualTickAction.ClearPending);
        Expect(true, true, true, true, false, CoopCampaignMapVisualTickAction.UpdateVisuals);
        Expect(true, false, true, true, false, CoopCampaignMapVisualTickAction.ClearPending);
        Expect(true, false, false, true, false, CoopCampaignMapVisualTickAction.ClearPending);
        Expect(true, true, true, true, false, CoopCampaignMapVisualTickAction.UpdateVisuals);
        Expect(true, true, true, true, true, CoopCampaignMapVisualTickAction.None);
        Expect(false, true, true, true, false, CoopCampaignMapVisualTickAction.None);

        for (int mask = 0; mask < 32; mask++)
        {
            var action = CoopCampaignMapVisualTickPolicy.Select(
                (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0,
                (mask & 8) != 0, (mask & 16) != 0);
            Check((action == CoopCampaignMapVisualTickAction.UpdateVisuals) == (mask == 15),
                "all current native prerequisites are mandatory: " + mask);
        }

        // Source integration check complements the detached decisions; it is
        // not evidence that native loading or the reported crash is repaired.
        string source = File.ReadAllText(Path.Combine("UI", "CoopCampaignMapPrototypeMissionView.cs"));
        int start = source.IndexOf("private void UpdateRenderReadiness(float dt)", StringComparison.Ordinal);
        int end = source.IndexOf("private void LogRenderState(", start, StringComparison.Ordinal);
        string probe = source.Substring(start, end - start);
        Check(probe.IndexOf("_mapSceneReadyToRender = false;", StringComparison.Ordinal) <
              probe.IndexOf("if (_sceneLayer == null", StringComparison.Ordinal), "revoke readiness before every early return");
        Check(probe.Contains("HasActiveReplicaView()") && probe.Contains("_sceneLayer.IsFinalized") &&
              probe.Contains("_mapScene.IsLoadingFinished()"), "current view/scene ownership and loading probes");
        int activeStart = source.IndexOf("private bool HasActiveReplicaView()", StringComparison.Ordinal);
        int activeEnd = source.IndexOf("private bool CanApplyReplicaEntityUpdates()", activeStart, StringComparison.Ordinal);
        string active = source.Substring(activeStart, activeEnd - activeStart);
        Check(!active.Contains("IsFocusLost") && active.Contains("_sceneLayer.IsActive") &&
              active.Contains("ReferenceEquals(ScreenManager.TopScreen, MissionScreen)"),
            "OS focus must not stop rendering in the owning active view");
        int tickStart = source.IndexOf("public override void OnMissionScreenTick(float dt)", StringComparison.Ordinal);
        int tickEnd = source.IndexOf("public override void OnMissionScreenFinalize()", tickStart, StringComparison.Ordinal);
        Check(!source.Substring(tickStart, tickEnd - tickStart).Contains("IsFocusLost"),
            "native window focus loss must not create a second background-frame rendering veto");
        int callbacks = source.IndexOf("private void OnClientCatalogDeltaChanged(", StringComparison.Ordinal);
        int load = source.IndexOf("private void TryLoadCampaignMap()", callbacks, StringComparison.Ordinal);
        string receive = source.Substring(callbacks, load - callbacks);
        Check(receive.Contains("CanApplyReplicaEntityUpdates()") &&
              receive.Contains("else _deferredEntityReconciliation = true;"),
            "background network updates are retained and deferred rather than applied or discarded");
        Check(source.Contains("(!_deferredEntityReconciliation &&") &&
              source.Contains("_appliedVisibleEntitiesRevision = Math.Max("),
            "resume reconciles latest stores even without a new visible-entity revision; no revision rollback");
        Console.WriteLine("Visual tick admission passed: lifecycle recovery and all 32 prerequisite combinations; native acceptance pending");
    }

    private static void Expect(bool scene, bool active, bool loaded, bool ready, bool failed,
        CoopCampaignMapVisualTickAction expected)
    {
        Check(CoopCampaignMapVisualTickPolicy.Select(scene, active, loaded, ready, failed) == expected,
            "lifecycle expected " + expected);
    }

    private static void Check(bool value, string message)
    { if (!value) throw new Exception("Visual tick readiness: " + message); }
}
