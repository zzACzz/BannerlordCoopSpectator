using System;
using System.IO;
using CoopSpectator.Infrastructure;

internal static class TooltipTargetTests
{
    private static int _count;
    internal static void Run()
    {
        var session = Guid.NewGuid().ToString("N");
        var lease = Guid.NewGuid();
        foreach (var kind in new[] { CoopCampaignMapPrototypeEntityKind.MainParty, CoopCampaignMapPrototypeEntityKind.MobileParty,
            CoopCampaignMapPrototypeEntityKind.Settlement, (CoopCampaignMapPrototypeEntityKind)99 })
        {
            string id = kind == CoopCampaignMapPrototypeEntityKind.Settlement ? "settlement:town" : "party:host";
            var request = new CoopCampaignMapTooltipRequest(session, lease, id, 1, false);
            var catalog = new CoopCampaignMapPrototypeCatalogEntityState { EntityId = id, Kind = kind };
            var dynamicState = new CoopCampaignMapPrototypeDynamicEntityState { EntityId = id };
            for (int flags = -1; flags <= 64; flags++)
            {
                dynamicState.PartyNameplateFlags = flags;
                foreach (bool visible in new[] { false, true })
                {
                    dynamicState.IsVisible = visible;
                    bool main = kind == CoopCampaignMapPrototypeEntityKind.MainParty;
                    bool mobile = main || kind == CoopCampaignMapPrototypeEntityKind.MobileParty;
                    bool pickable = mobile && flags >= 0 && flags <= 63 && (flags & 4) == 0 &&
                        (main || (flags & 32) == 0);
                    bool expected = visible && (kind == CoopCampaignMapPrototypeEntityKind.Settlement || pickable);
                    Check(CoopCampaignMapTooltipTargetAdmission.IsAdmitted(session, request, catalog, dynamicState) == expected, "kind/body/visibility matrix");
                }
            }
            dynamicState.IsVisible = true; dynamicState.PartyNameplateFlags = 0;
            Check(!CoopCampaignMapTooltipTargetAdmission.IsAdmitted(Guid.NewGuid().ToString("N"), request, catalog, dynamicState), "wrong publication session");
            catalog.EntityId = id.ToUpperInvariant();
            Check(!CoopCampaignMapTooltipTargetAdmission.IsAdmitted(session, request, catalog, dynamicState), "case alias catalog rejected");
            catalog.EntityId = id; dynamicState.EntityId = "party:other";
            Check(!CoopCampaignMapTooltipTargetAdmission.IsAdmitted(session, request, catalog, dynamicState), "dynamic mismatch");
        }
        var settlement = new CoopCampaignMapPrototypeCatalogEntityState { EntityId = "party:host", Kind = CoopCampaignMapPrototypeEntityKind.Settlement };
        var state = new CoopCampaignMapPrototypeDynamicEntityState { EntityId = "party:host", IsVisible = true };
        var valid = new CoopCampaignMapTooltipRequest(session, lease, "party:host", 1, false);
        Check(!CoopCampaignMapTooltipTargetAdmission.IsAdmitted(session, valid, settlement, state), "kind/prefix mismatch");
        settlement.Kind = CoopCampaignMapPrototypeEntityKind.MainParty;
        state.IsPartyInformationHidden = true;
        Check(CoopCampaignMapTooltipTargetAdmission.IsAdmitted(session, valid, settlement, state), "unknown detail still permits native question-mark tooltip");
        Check(!CoopCampaignMapTooltipTargetAdmission.IsAdmitted(session, null, settlement, state) &&
            !CoopCampaignMapTooltipTargetAdmission.IsAdmitted(session, valid, null, state) &&
            !CoopCampaignMapTooltipTargetAdmission.IsAdmitted(session, valid, settlement, null), "missing proof");
        Check(!CoopCampaignMapTooltipTargetAdmission.IsAdmitted(session,
            new CoopCampaignMapTooltipRequest(session, Guid.Empty, "party:host", 1, false), settlement, state), "missing lease");

        string root = Environment.GetEnvironmentVariable("COOPSPECTATOR_REPOSITORY_ROOT");
        string publisher = File.ReadAllText(Path.Combine(root, "Campaign", "CoopCampaignMapPrototypePublisherBehavior.cs"));
        string capture = File.ReadAllText(Path.Combine(root, "Campaign", "CoopCampaignMapTooltipCapture.cs"));
        Check(capture.Contains("(party.IsMainParty || !party.IsInRaftState)"),
            "Host rechecks current native raft state rather than trusting last published flags");
        int method = publisher.IndexOf("internal static bool TryCapturePublishedTooltip", StringComparison.Ordinal);
        int lookup = publisher.IndexOf("foreach (Settlement settlement in Settlement.All)", method, StringComparison.Ordinal);
        Check(method >= 0 && publisher.IndexOf("CoopCampaignMapTooltipTargetAdmission.IsAdmitted", method, StringComparison.Ordinal) < lookup,
            "admission precedes native collection lookup");
        Check(publisher.Contains("_tooltipOwnerThread != System.Threading.Thread.CurrentThread.ManagedThreadId") &&
            publisher.Contains("ReferenceEquals(publisher._lastPublishedCampaign") && publisher.Contains("CoopCampaignMapTooltipCapture.IsMapContext()"), "host context guards");
        int publish = publisher.IndexOf("private void PublishTick", StringComparison.Ordinal);
        Check(publisher.IndexOf("_lastPublishedCampaign = null", publish, StringComparison.Ordinal) <
            publisher.IndexOf("if (!TryCaptureSnapshot", publish, StringComparison.Ordinal), "failed capture invalidates prior proof");
        Check(publisher.IndexOf("_lastPublishedTimestamp =", publish, StringComparison.Ordinal) >
            publisher.IndexOf("CoopCampaignMapPrototypeBridgeFile.TryWrite(snapshot", publish, StringComparison.Ordinal), "successful publication precedes stamp");
        Check(publisher.Contains("if (++matches > 1) return false") && publisher.Contains("/ (double)System.Diagnostics.Stopwatch.Frequency >= 2d"),
            "ambiguous identity and stale publication rejected");
        Console.WriteLine("Tooltip target-admission assertions passed: " + _count);
    }
    private static void Check(bool value, string name)
    { _count++; if (!value) throw new InvalidOperationException("Tooltip target: " + name); }
}
