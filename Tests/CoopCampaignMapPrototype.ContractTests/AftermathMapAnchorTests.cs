using System;
using System.IO;
using CoopSpectator.Campaign;
using CoopSpectator.Infrastructure;

internal static class AftermathMapAnchorTests
{
    private const string Session = "0123456789abcdef0123456789abcdef";
    internal static void Run()
    {
        // Every route reaches the same PartyState/InventoryState publication:
        // the fix must not depend on a field-battle-only completion event.
        ValidateRetainedBundle("shared aftermath route");
        ValidateBridgePublication();
        ValidateRevocation();
        string source = File.ReadAllText(Path.Combine("Campaign", "CoopCampaignMapPrototypePublisherBehavior.cs"));
        int remembered = source.IndexOf("_aftermathAnchor.Remember(", StringComparison.Ordinal);
        Check(remembered > source.IndexOf("if (!CoopCampaignMapPrototypeBridgeFile.TryWrite(snapshot, out reason))", StringComparison.Ordinal),
            "capture alone must not remember an unpublished frame");
        Check(source.Contains("_aftermathPublicationCatalog ?? _cachedCatalog") &&
            source.Contains("_aftermathPublicationDynamic ?? _cachedDynamic") &&
            source.Contains("if (aftermathPublication) _catalogDirty = _dynamicDirty = true;") &&
            source.Contains("if (!aftermathPublication) _catalogDirty = false;") &&
            source.Contains("if (!aftermathPublication) _dynamicDirty = false;"),
            "publish retained dependencies and require live restoration even after partial write");
        Check(!source.Contains("_cachedDynamic.UpdatedUtc = snapshot.UpdatedUtc") &&
            !source.Contains("_cachedDynamic.Revision != _aftermathAnchor"),
            "aftermath neither mutates live cache nor compares it with the older retained bundle");
        Console.WriteLine("Aftermath map anchor passed: cache drift, bridge roundtrip, isolation, revocation; native battle matrix pending");
    }

    private static void ValidateRetainedBundle(string scenario)
    {
        object campaign = new object();
        var anchor = new CoopCampaignAftermathMapAnchor();
        DateTime before = new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc);
        var frame = Frame(before);
        var catalog = Catalog(before);
        var dynamic = Dynamic(before);
        Check(anchor.Remember(campaign, frame, catalog, dynamic), scenario + " coherent publication retained");
        // Dialogue advances both live caches; a long battle then ends directly
        // in Prisoners, without an intervening non-dialogue map publication.
        frame.DynamicRevision = dynamic.Revision = 42;
        frame.CatalogRevision = catalog.Revision = 43;
        frame.NormalizedX = 99;
        catalog.Entities[0].DisplayName = "new live catalog";
        dynamic.Entities[0].NormalizedX = 99;
        DateTime now = before.AddMinutes(20);
        Check(anchor.TryAcquire(campaign, Session, now, out var retained, out var retainedCatalog, out var retainedDynamic),
            scenario + " Prisoners admission survives newer live caches");
        Check(retained.DynamicRevision == 6 && retainedDynamic.Revision == 6 &&
            retained.CatalogRevision == 5 && retainedCatalog.Revision == 5 &&
            retained.NormalizedX == 12 && retainedDynamic.Entities[0].NormalizedX == 12 &&
            retainedCatalog.Entities[0].DisplayName == "retained town", scenario + " exact bundle frozen");
        Check(retained.UpdatedUtc == now && retainedCatalog.UpdatedUtc == now && retainedDynamic.UpdatedUtc == now &&
            dynamic.UpdatedUtc == before && catalog.UpdatedUtc == before, scenario + " only publication receipt renewed");
        // Consumer inspection cannot corrupt Loot or the next acquisition.
        retained.NormalizedX = 123;
        retainedCatalog.Entities[0].DisplayName = "consumer edit";
        retainedDynamic.Entities.Clear();
        Check(anchor.TryAcquire(campaign, Session, now.AddSeconds(1), out retained, out retainedCatalog, out retainedDynamic) &&
            retained.NormalizedX == 12 && retainedDynamic.Entities.Count == 1 &&
            retainedCatalog.Entities[0].DisplayName == "retained town", scenario + " Loot is isolated from Prisoners");
        Check(anchor.Remember(campaign, frame, catalog, dynamic) &&
            anchor.TryAcquire(campaign, Session, now.AddMinutes(1), out retained, out retainedCatalog, out retainedDynamic) &&
            retainedDynamic.Revision == 42 && retainedCatalog.Revision == 43 && retained.NormalizedX == 99,
            scenario + " next successful map publication replaces bundle for repeated battle");
    }

    private static void ValidateRevocation()
    {
        object campaign = new object();
        DateTime now = DateTime.UtcNow;
        var anchor = new CoopCampaignAftermathMapAnchor();
        Check(!anchor.TryAcquire(campaign, Session, now, out _, out _, out _), "no anchor at cold start");
        Check(anchor.Remember(campaign, Frame(now), Catalog(now), Dynamic(now)), "owner fixture");
        Check(!anchor.TryAcquire(new object(), Session, now, out _, out _, out _) &&
            !anchor.TryAcquire(campaign, Session, now, out _, out _, out _), "save reload revokes old campaign, including switch back");
        anchor.Remember(campaign, Frame(now), Catalog(now), Dynamic(now));
        Check(!anchor.TryAcquire(campaign, "other-session", now, out _, out _, out _) &&
            !anchor.TryAcquire(campaign, Session, now, out _, out _, out _), "session mismatch revokes");
        for (int mismatch = 0; mismatch < 6; mismatch++)
        {
            var frame = Frame(now); var catalog = Catalog(now); var dynamic = Dynamic(now);
            if (mismatch == 0) catalog.Revision++;
            if (mismatch == 1) dynamic.Revision++;
            if (mismatch == 2) catalog.SessionId = "foreign";
            if (mismatch == 3) dynamic.SessionId = "foreign";
            if (mismatch == 4) catalog.SchemaVersion++;
            if (mismatch == 5) dynamic.SchemaVersion++;
            Check(!anchor.Remember(campaign, frame, catalog, dynamic) &&
                !anchor.TryAcquire(campaign, Session, now, out _, out _, out _), "incoherent part fails closed " + mismatch);
        }
        anchor.Remember(campaign, Frame(now), Catalog(now), Dynamic(now)); anchor.Reset();
        Check(!anchor.TryAcquire(campaign, Session, now, out _, out _, out _), "teardown revokes");
    }

    private static void ValidateBridgePublication()
    {
        string directory = Path.Combine(Path.GetTempPath(), "CoopAftermathBundleTests", Guid.NewGuid().ToString("N"));
        string previous = CoopSpectator.Infrastructure.Automation.CoopAutomationRuntimeBridge.TestCoopFolderPath;
        Directory.CreateDirectory(directory);
        CoopSpectator.Infrastructure.Automation.CoopAutomationRuntimeBridge.TestCoopFolderPath = directory;
        try
        {
            DateTime before = DateTime.UtcNow.AddMinutes(-20), now = DateTime.UtcNow;
            object owner = new object(); var anchor = new CoopCampaignAftermathMapAnchor();
            var frame = Frame(before); var catalog = Catalog(before); var dynamic = Dynamic(before);
            catalog.Entities.Clear(); dynamic.Entities.Clear();
            Check(anchor.Remember(owner, frame, catalog, dynamic), "bridge anchor");
            // The files initially contain a newer dialogue's dependencies.
            catalog.Revision = 43; dynamic.Revision = 42; catalog.UpdatedUtc = dynamic.UpdatedUtc = now;
            Check(CoopCampaignMapPrototypeBridgeFile.TryWriteCatalog(catalog, out _) &&
                CoopCampaignMapPrototypeBridgeFile.TryWriteDynamic(dynamic, out _), "write newer live dependencies");
            Check(anchor.TryAcquire(owner, Session, now, out var retained, out var retainedCatalog, out var retainedDynamic),
                "obtain all retained dependencies after long battle");
            retained.Revision = 50;
            Check(CoopCampaignMapPrototypeBridgeFile.TryWriteCatalog(retainedCatalog, out _) &&
                CoopCampaignMapPrototypeBridgeFile.TryWriteDynamic(retainedDynamic, out _) &&
                CoopCampaignMapPrototypeBridgeFile.TryWrite(retained, out _), "write retained dependency pair before frame");
            Check(CoopCampaignMapPrototypeBridgeFile.TryReadFresh(now, TimeSpan.FromSeconds(2), out var received, out var reason),
                "retained host frame is fresh: " + reason);
            Check(CoopCampaignMapPrototypeBridgeFile.TryReadFreshCatalog(now, TimeSpan.Zero, out var readCatalog, out _) &&
                CoopCampaignMapPrototypeBridgeFile.TryReadFreshDynamic(now, TimeSpan.FromSeconds(2), out var readDynamic, out _) &&
                received.SessionId == readCatalog.SessionId && received.SessionId == readDynamic.SessionId &&
                received.CatalogRevision == readCatalog.Revision && received.DynamicRevision == readDynamic.Revision,
                "fresh server admission sees exact retained revisions, not newer caches");
            Check(!CoopCampaignMapPrototypeBridgeFile.TryReadFreshDynamic(now.AddSeconds(3), TimeSpan.FromSeconds(2), out _, out _),
                "host silence still expires retained publication");
            frame.UpdatedUtc = now; frame.Revision = 51; frame.CatalogRevision = catalog.Revision; frame.DynamicRevision = dynamic.Revision;
            Check(CoopCampaignMapPrototypeBridgeFile.TryWriteCatalog(catalog, out _) &&
                CoopCampaignMapPrototypeBridgeFile.TryWriteDynamic(dynamic, out _) &&
                CoopCampaignMapPrototypeBridgeFile.TryWrite(frame, out _) &&
                CoopCampaignMapPrototypeBridgeFile.TryReadFresh(now, TimeSpan.FromSeconds(2), out received, out _) &&
                CoopCampaignMapPrototypeBridgeFile.TryReadFreshDynamic(now, TimeSpan.FromSeconds(2), out readDynamic, out _) &&
                received.DynamicRevision == 42 && readDynamic.Revision == 42, "normal map restores live dependencies");
        }
        finally
        {
            CoopSpectator.Infrastructure.Automation.CoopAutomationRuntimeBridge.TestCoopFolderPath = previous;
            Directory.Delete(directory, true);
        }
    }

    private static CoopCampaignMapPrototypeHostSnapshot Frame(DateTime now) => new CoopCampaignMapPrototypeHostSnapshot {
        ContentProfileId = "base-v1:" + new string('a',64),
        SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion, SessionId = Session,
        IsActive = true, Revision = 7, CatalogRevision = 5, DynamicRevision = 6, NormalizedX = 12, UpdatedUtc = now };
    private static CoopCampaignMapPrototypeCatalogSnapshot Catalog(DateTime now) => new CoopCampaignMapPrototypeCatalogSnapshot {
        SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion, SessionId = Session,
        Revision = 5, UpdatedUtc = now, Entities = { new CoopCampaignMapPrototypeCatalogEntityState {
            EntityId = "town", DisplayName = "retained town" } } };
    private static CoopCampaignMapPrototypeDynamicSnapshot Dynamic(DateTime now) => new CoopCampaignMapPrototypeDynamicSnapshot {
        SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion, SessionId = Session,
        Revision = 6, UpdatedUtc = now, Entities = { new CoopCampaignMapPrototypeDynamicEntityState {
            EntityId = "town", NormalizedX = 12 } } };
    private static void Check(bool value, string message)
    { if (!value) throw new Exception("Aftermath map anchor: " + message); }
}
