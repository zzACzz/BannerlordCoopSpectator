using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using CoopSpectator.Infrastructure;

internal static class SettlementMapEventTests
{
    public static void Run()
    {
        void Check(bool ok, string why) { if (!ok) throw new Exception("Settlement map event: " + why); }
        string root = Environment.GetEnvironmentVariable("COOPSPECTATOR_REPOSITORY_ROOT");
        string overlay = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapPrototypeSettlementOverlayVM.cs"));
        Check(overlay.Contains("_hostMapEventVisualType = entity.SettlementMapEventType") &&
            overlay.Contains("MapEventVisualType = visible ? _hostMapEventVisualType : 0") &&
            overlay.Contains("SetMapEventVisible(_mapEventVisible)"),
            "hidden event retains authoritative value and applies visibility on updates");
        string view = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapPrototypeMissionView.cs"));
        Check(view.Contains("_settlementOverlayViewModel?.SetMapEventsVisible(") &&
            view.Contains("TryGetMovementTargetState(out _, out _)"), "fresh coherent state gate is wired");
        Check(CoopCampaignMapMovementTargetCodec.IsFreshAndReady(1.99, 2, 4, 2, 4), "fresh coherent data accepted");
        Check(!CoopCampaignMapMovementTargetCodec.IsFreshAndReady(2, 2, 4, 2, 4), "stale boundary hides");
        Check(!CoopCampaignMapMovementTargetCodec.IsFreshAndReady(0, 2, 4, 2, 3), "unfinished dynamic data hides");
        Check(!CoopCampaignMapMovementTargetCodec.IsFreshAndReady(0, 2, 4, 1, 4), "unfinished catalog hides");
        for (int type = 0; type <= 5; type++)
        {
            var host = Program.CreateHostSnapshot(DateTime.UtcNow);
            var settlement = host.VisibleEntities[1];
            settlement.SettlementMapEventType = type;
            Check(settlement.Clone().SettlementMapEventType == type, "visible clone");
            Check(CoopCampaignMapPrototypeBridgeCodec.TryParse(CoopCampaignMapPrototypeBridgeCodec.Serialize(host),
                out var parsed, out _) && parsed.VisibleEntities[1].SettlementMapEventType == type, "full bridge");
            var state = new CoopCampaignMapPrototypeDynamicEntityState
            { EntityId = settlement.EntityId, IsVisible = true, SettlementMapEventType = type };
            var dynamic = new CoopCampaignMapPrototypeDynamicSnapshot
            {
                SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
                SessionId = host.SessionId, Revision = 1, UpdatedUtc = host.UpdatedUtc,
                Entities = new List<CoopCampaignMapPrototypeDynamicEntityState> { state }
            };
            Check(CoopCampaignMapPrototypeBridgeCodec.TryParseDynamic(CoopCampaignMapPrototypeBridgeCodec.SerializeDynamic(dynamic),
                out var result, out _) && result.Entities[0].SettlementMapEventType == type, "dynamic bridge");
            var fullLines = CoopCampaignMapPrototypeBridgeCodec.Serialize(host);
            var dynamicLines = CoopCampaignMapPrototypeBridgeCodec.SerializeDynamic(dynamic);
            foreach (string invalid in new[] { "-1", "6", "2147483648", "invalid", "" })
            {
                Check(!CoopCampaignMapPrototypeBridgeCodec.TryParse(ReplaceEvent(fullLines, invalid), out _, out _),
                    "invalid full event " + invalid);
                Check(!CoopCampaignMapPrototypeBridgeCodec.TryParseDynamic(ReplaceEvent(dynamicLines, invalid), out _, out _),
                    "invalid dynamic event " + invalid);
            }
            Check(!CoopCampaignMapPrototypeBridgeCodec.TryParse(
                fullLines.Where(line => !line.Contains(".SettlementMapEventType=")), out _, out _), "missing full event");
            Check(!CoopCampaignMapPrototypeBridgeCodec.TryParseDynamic(
                dynamicLines.Where(line => !line.Contains(".SettlementMapEventType=")), out _, out _), "missing dynamic event");
            var catalog = new CoopCampaignMapPrototypeCatalogEntityState
            { EntityId = state.EntityId, Kind = CoopCampaignMapPrototypeEntityKind.Settlement };
            Check(CoopCampaignMapReplicaStateProjection.Merge(catalog, state).SettlementMapEventType == type, "projection");
            for (int next = 0; next <= 5; next++)
            {
                var changed = state.Clone(); changed.SettlementMapEventType = next;
                Check(CoopCampaignMapPrototypeDynamicDeltaPolicy.BuildDelta(new[] { state }, new[] { changed }).Count ==
                    (next == type ? 0 : 1), "all transitions including clear");
            }
            var removed = CoopCampaignMapPrototypeDynamicDeltaPolicy.BuildDelta(new[] { state },
                Array.Empty<CoopCampaignMapPrototypeDynamicEntityState>());
            Check(removed.Count == 1 && removed[0].SettlementMapEventType == 0, "removal clear");
        }
        foreach (int bad in new[] { -1, 6, int.MaxValue })
            Check(!CoopCampaignMapPrototypeContract.IsValidDynamicEntity(new CoopCampaignMapPrototypeDynamicEntityState
            { EntityId = "settlement:town", IsVisible = true, SettlementMapEventType = bad }), "range");
        Check(!CoopCampaignMapPrototypeContract.IsValidDynamicEntity(new CoopCampaignMapPrototypeDynamicEntityState
        { EntityId = "party:enemy", IsVisible = true, SettlementMapEventType = 1 }), "party injection");
        Check(!CoopCampaignMapPrototypeContract.IsValidDynamicEntity(new CoopCampaignMapPrototypeDynamicEntityState
        { EntityId = "settlement:town", IsVisible = false, SettlementMapEventType = 1 }), "hidden injection");
        Console.WriteLine("Settlement map event checks passed.");
    }

    private static IEnumerable<string> ReplaceEvent(IEnumerable<string> lines, string value) =>
        lines.Select(line => line.Contains(".SettlementMapEventType=")
            ? line.Substring(0, line.IndexOf('=') + 1) + value : line);
}
