using System;
using System.IO;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;

internal static class PartyBodyTests
{
    private static int _assertions;

    internal static void Run()
    {
        foreach (CoopCampaignMapPrototypeEntityKind kind in new[] {
            CoopCampaignMapPrototypeEntityKind.MainParty,
            CoopCampaignMapPrototypeEntityKind.MobileParty,
            CoopCampaignMapPrototypeEntityKind.Settlement,
            (CoopCampaignMapPrototypeEntityKind)(-1),
            (CoopCampaignMapPrototypeEntityKind)100 })
        {
            for (int flags = 0; flags <= 63; flags++)
            {
                bool mobile = kind == CoopCampaignMapPrototypeEntityKind.MainParty ||
                    kind == CoopCampaignMapPrototypeEntityKind.MobileParty;
                bool inSettlement = (flags & 4) != 0;
                Check(CoopCampaignMapPartyBodyPolicy.ShouldRenderBody(kind, flags) ==
                    (mobile && !inSettlement), "Kind/flag body admission " + kind + "/" + flags);
            }
            foreach (int invalid in new[] { -1, int.MinValue, 64, int.MaxValue })
                Check(!CoopCampaignMapPartyBodyPolicy.ShouldRenderBody(kind, invalid),
                    "Invalid flags fail closed");
        }

        // The same logical anchor is retained across repeated entry/exit. Only
        // presentation changes; camera following must not depend on a mesh.
        var main = new CoopCampaignMapPrototypeEntityState {
            EntityId = "main", Kind = CoopCampaignMapPrototypeEntityKind.MainParty
        };
        for (int cycle = 0; cycle < 100; cycle++)
        {
            main.PartyNameplateFlags = 0;
            Check(CoopCampaignMapPartyBodyPolicy.ShouldRenderBody(main.Kind, main.PartyNameplateFlags), "Road body");
            main.PartyNameplateFlags = (int)CoopCampaignMapPartyNameplateFlags.InSettlement;
            Check(!CoopCampaignMapPartyBodyPolicy.ShouldRenderBody(main.Kind, main.PartyNameplateFlags), "Entry hides body");
            Check(main.EntityId == "main" && main.Kind == CoopCampaignMapPrototypeEntityKind.MainParty, "Anchor retained");
        }

        string root = FindRoot();
        string view = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapPrototypeMissionView.cs"));
        ValidateDeferredProjection(view);
        int gate = view.IndexOf("bool projectionCandidate =", StringComparison.Ordinal);
        int allocate = view.IndexOf("TryEnsurePartyVisual(replica, target, visualFrame)", gate, StringComparison.Ordinal);
        Check(gate >= 0 && allocate > gate && view.Substring(gate, allocate - gate).Contains(
            "CoopCampaignMapPartyBodyPolicy.ShouldRenderBody("), "Body gate precedes visual allocation");
        Check(view.Contains("replica.PartyVisual?.SetVisible(false)") &&
              view.Contains("_mainPartyRenderVisible = shouldRenderParty") &&
              view.Contains("shouldRenderParty && !hasPartyVisual"),
            "Existing mesh and both fallback paths share the hidden decision");
        int hover = view.IndexOf("private void UpdateReplicaInformation(float dt)", StringComparison.Ordinal);
        int hoverEnd = view.IndexOf("private static string BuildPartyVisualAttemptKey", hover, StringComparison.Ordinal);
        string hoverSource = view.Substring(hover, hoverEnd - hover);
        string pickingSource = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapPrototypeMissionView.Picking.cs"));
        Check(hoverSource.Contains("ResolveNativeHoveredReplica()") &&
              pickingSource.Contains("replica.Target.Kind, dynamicState.PartyNameplateFlags") &&
              hoverSource.Contains("IsPickTargetAdmitted(hoveredId)"),
            "Hidden body cannot become the current native tooltip target");
        int upsert = view.IndexOf("private void UpsertEntityReplica(", StringComparison.Ordinal);
        int upsertEnd = view.IndexOf("private void RemoveEntityReplica(", upsert, StringComparison.Ordinal);
        string upsertSource = view.Substring(upsert, upsertEnd - upsert);
        Check(upsertSource.Contains("entity.Kind, entity.PartyNameplateFlags") &&
              upsertSource.Contains("_hoveredReplicaId = null") &&
              upsertSource.Contains("_tooltipPresenter?.Clear()"),
            "Host update invalidates hidden hover independently of UI focus");
        string publisher = File.ReadAllText(Path.Combine(root, "Campaign", "CoopCampaignMapPrototypePublisherBehavior.cs"));
        Check(publisher.Contains("party.CurrentSettlement != null, party.IsDisorganized") &&
              publisher.Replace("\r\n", "\n").Contains(
                  "CoopCampaignMapPrototypeEntityKind.MainParty,\n                isVisible: true,"),
            "Host keeps main anchor and publishes settlement state");
        Console.WriteLine("Party body admission assertions passed: " + _assertions);
    }

    private static void ValidateDeferredProjection(string view)
    {
        var catalog = new List<CoopCampaignMapPrototypeCatalogEntityState> {
            new CoopCampaignMapPrototypeCatalogEntityState {
                EntityId = "main", Kind = CoopCampaignMapPrototypeEntityKind.MainParty,
                DisplayName = "Before"
            }
        };
        var dynamicById = new Dictionary<string, CoopCampaignMapPrototypeDynamicEntityState>(StringComparer.OrdinalIgnoreCase) {
            ["main"] = new CoopCampaignMapPrototypeDynamicEntityState {
                EntityId = "main", IsVisible = true, PartyNameplateFlags = 4
            }
        };
        // Catalog completion queues an inside-settlement snapshot. The exit
        // dynamic update then arrives before the view drains its pending work.
        var stale = CoopCampaignMapReplicaStateProjection.BuildVisible(catalog, dynamicById);
        dynamicById["main"].PartyNameplateFlags = 0;
        var exit = CoopCampaignMapReplicaStateProjection.BuildVisible(catalog, dynamicById);
        Check(stale[0].PartyNameplateFlags == 4 && exit[0].PartyNameplateFlags == 0,
            "Deferred projection uses latest exit state, not the queued clone");
        Check(CoopCampaignMapPartyBodyPolicy.ShouldRenderBody(exit[0].Kind, exit[0].PartyNameplateFlags),
            "Body admitted after catalog-then-exit ordering");
        dynamicById["main"].PartyNameplateFlags = 4;
        Check(CoopCampaignMapReplicaStateProjection.BuildVisible(catalog, dynamicById)[0].PartyNameplateFlags == 4,
            "Repeated entry supersedes queued outside state");
        dynamicById["main"].IsVisible = false;
        Check(CoopCampaignMapReplicaStateProjection.BuildVisible(catalog, dynamicById).Count == 0,
            "Queued snapshot cannot resurrect a newly hidden party");
        dynamicById["main"].IsVisible = true;
        catalog[0].DisplayName = "After";
        Check(CoopCampaignMapReplicaStateProjection.BuildVisible(catalog, dynamicById)[0].DisplayName == "After",
            "Latest catalog also supersedes queued presentation");
        catalog.Add(new CoopCampaignMapPrototypeCatalogEntityState { EntityId = "new", Kind = CoopCampaignMapPrototypeEntityKind.MobileParty });
        dynamicById["new"] = new CoopCampaignMapPrototypeDynamicEntityState { EntityId = "new", IsVisible = true };
        Check(CoopCampaignMapReplicaStateProjection.BuildVisible(catalog, dynamicById).Count == 2,
            "Entity admitted after queueing is retained by full reconciliation");
        catalog.RemoveAt(0);
        Check(CoopCampaignMapReplicaStateProjection.BuildVisible(catalog, dynamicById).Count == 1,
            "Catalog removal cannot be undone by pending work");
        dynamicById["new"].EntityId = "mismatch";
        Check(CoopCampaignMapReplicaStateProjection.BuildVisible(catalog, dynamicById).Count == 0,
            "Mismatched identity fails closed");
        Check(CoopCampaignMapReplicaStateProjection.BuildVisible(null, dynamicById).Count == 0 &&
              CoopCampaignMapReplicaStateProjection.BuildVisible(catalog, null).Count == 0,
            "Missing authoritative input fails closed");
        int start = view.IndexOf("private void ApplyPendingVisibleEntities()", StringComparison.Ordinal);
        int end = view.IndexOf("private void ApplyDynamicEntityUpdate(", start, StringComparison.Ordinal);
        string drain = view.Substring(start, end - start);
        Check(drain.Contains("CoopCampaignMapReplicaStateProjection.BuildVisible(") &&
              drain.Contains("_catalogById.Values, _dynamicById") &&
              !drain.Contains("Synchronize(_pendingVisibleEntities)"),
            "Deferred view drain projects latest admitted stores for bodies and overlays");
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "CoopSpectator.csproj")))
            directory = directory.Parent;
        if (directory == null) throw new InvalidOperationException("Repository root not found.");
        return directory.FullName;
    }

    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException("Party body: " + message);
    }
}
