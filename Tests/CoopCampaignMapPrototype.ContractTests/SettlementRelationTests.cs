using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopSpectator.Infrastructure;

internal static class SettlementRelationTests
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException("Settlement relation: " + message);
    }

    public static void Run()
    {
        for (int mask = 0; mask < 16; mask++)
        {
            bool owner = (mask & 1) != 0, war = (mask & 2) != 0,
                same = (mask & 4) != 0, allied = (mask & 8) != 0;
            int expected = 0;
            if (owner)
            {
                if (war) expected = 2;
                else if (same) expected = 1;
                else if (allied) expected = 3;
            }
            Check(CoopCampaignMapSettlementRelationPolicy.Resolve(owner, war, same, allied) == expected,
                "installed precedence truth table " + mask);
        }

        for (int relation = 0; relation <= 3; relation++)
        {
            var host = Program.CreateHostSnapshot(DateTime.UtcNow);
            var settlement = host.VisibleEntities[1];
            settlement.SettlementRelation = relation;
            Check(CoopCampaignMapPrototypeContract.IsValidVisibleEntity(settlement), "visible admission");
            Check(settlement.Clone().SettlementRelation == relation, "visible clone");
            var lines = CoopCampaignMapPrototypeBridgeCodec.Serialize(host);
            Check(CoopCampaignMapPrototypeBridgeCodec.TryParse(lines, out var parsed, out var reason) &&
                parsed.VisibleEntities[1].SettlementRelation == relation, "full bridge " + reason);

            var state = new CoopCampaignMapPrototypeDynamicEntityState {
                EntityId = settlement.EntityId, IsVisible = true, SettlementRelation = relation
            };
            Check(CoopCampaignMapPrototypeContract.IsValidDynamicEntity(state), "dynamic admission");
            Check(state.Clone().SettlementRelation == relation, "dynamic clone");
            var snapshot = new CoopCampaignMapPrototypeDynamicSnapshot {
                SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
                SessionId = host.SessionId, Revision = 1, UpdatedUtc = host.UpdatedUtc,
                Entities = new List<CoopCampaignMapPrototypeDynamicEntityState> { state }
            };
            var dynamicLines = CoopCampaignMapPrototypeBridgeCodec.SerializeDynamic(snapshot);
            Check(CoopCampaignMapPrototypeBridgeCodec.TryParseDynamic(dynamicLines, out var roundtrip, out reason) &&
                roundtrip.Entities[0].SettlementRelation == relation, "dynamic bridge " + reason);
            var catalog = new CoopCampaignMapPrototypeCatalogEntityState {
                EntityId = settlement.EntityId, Kind = CoopCampaignMapPrototypeEntityKind.Settlement
            };
            Check(CoopCampaignMapReplicaStateProjection.Merge(catalog, state).SettlementRelation == relation,
                "projection consumes dynamic relation");
            for (int next = 0; next <= 3; next++)
            {
                var changed = state.Clone(); changed.SettlementRelation = next;
                var delta = CoopCampaignMapPrototypeDynamicDeltaPolicy.BuildDelta(new[] { state }, new[] { changed });
                Check(delta.Count == (next == relation ? 0 : 1), "relation-only transition " + relation + "->" + next);
                if (delta.Count > 0)
                    Check(CoopCampaignMapReplicaStateProjection.Merge(catalog, delta[0]).SettlementRelation == next,
                        "new relation replaces prior presentation, including peace");
            }
            var removed = CoopCampaignMapPrototypeDynamicDeltaPolicy.BuildDelta(new[] { state },
                Array.Empty<CoopCampaignMapPrototypeDynamicEntityState>());
            Check(removed.Count == 1 && !removed[0].IsVisible && removed[0].SettlementRelation == 0,
                "removal clears relation");
            Check(CoopCampaignMapPrototypeContract.IsValidDynamicEntity(removed[0]) &&
                CoopCampaignMapReplicaStateProjection.Merge(catalog, removed[0]) == null, "hidden cannot render");
            var current = new Dictionary<string, CoopCampaignMapPrototypeDynamicEntityState> { [state.EntityId] = state };
            Check(CoopCampaignMapReplicaStateProjection.BuildVisible(new[] { catalog }, current)[0].SettlementRelation == relation,
                "late full reconciliation uses current dynamic state");
            current[state.EntityId] = removed[0];
            Check(CoopCampaignMapReplicaStateProjection.BuildVisible(new[] { catalog }, current).Count == 0,
                "queued catalog cannot revive removed relation");

            foreach (string invalid in new[] { "-1", "4", "2147483647", "2147483648", "invalid", "" })
            {
                Check(!CoopCampaignMapPrototypeBridgeCodec.TryParse(ReplaceRelation(lines, invalid), out _, out _),
                    "invalid full bridge " + invalid);
                Check(!CoopCampaignMapPrototypeBridgeCodec.TryParseDynamic(ReplaceRelation(dynamicLines, invalid), out _, out _),
                    "invalid dynamic bridge " + invalid);
            }
            Check(!CoopCampaignMapPrototypeBridgeCodec.TryParse(WithoutRelation(lines), out _, out _), "missing full relation");
            Check(!CoopCampaignMapPrototypeBridgeCodec.TryParseDynamic(WithoutRelation(dynamicLines), out _, out _), "missing dynamic relation");
            Check(!CoopCampaignMapPrototypeBridgeCodec.TryParseDynamic(
                dynamicLines.Select(line => line.StartsWith("SchemaVersion=") ? "SchemaVersion=24" : line), out _, out _),
                "old bridge rejected");
        }

        var party = Program.CreateHostSnapshot(DateTime.UtcNow).VisibleEntities[0];
        party.SettlementRelation = 2;
        Check(!CoopCampaignMapPrototypeContract.IsValidVisibleEntity(party), "party cannot carry settlement relation");
        foreach (int invalid in new[] { -1, 4, int.MinValue, int.MaxValue })
        {
            var state = new CoopCampaignMapPrototypeDynamicEntityState {
                EntityId = "settlement:town", IsVisible = true, SettlementRelation = invalid
            };
            Check(!CoopCampaignMapPrototypeContract.IsValidDynamicEntity(state), "invalid relation range");
            Check(CoopCampaignMapReplicaStateProjection.Merge(new CoopCampaignMapPrototypeCatalogEntityState {
                EntityId = state.EntityId, Kind = CoopCampaignMapPrototypeEntityKind.Settlement
            }, state) == null, "projection refuses invalid relation");
        }
        Check(!CoopCampaignMapPrototypeContract.IsValidDynamicEntity(new CoopCampaignMapPrototypeDynamicEntityState {
            EntityId = "party:enemy", IsVisible = true, SettlementRelation = 2
        }), "party dynamic injection");
        Check(!CoopCampaignMapPrototypeContract.IsValidDynamicEntity(new CoopCampaignMapPrototypeDynamicEntityState {
            EntityId = "settlement:town", IsVisible = false, SettlementRelation = 2
        }), "hidden relation injection");

        var root = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CoopSpectator.csproj"))) root = root.Parent;
        Check(root != null, "repository root");
        foreach (string file in new[] { "CoopCampaignMapPrototypeNetworkMessages.cs", "CoopCampaignMapReplicaNetworkMessages.cs" })
        {
            string wire = File.ReadAllText(Path.Combine(root.FullName, "Network/Messages", file));
            Check(wire.Contains("SettlementRelation = ReadIntFromPacket(SettlementRelationCompression, ref valid)") &&
                wire.Contains("CoopCampaignMapSettlementRelationPolicy.Maximum") &&
                (wire.Contains("WriteIntToPacket(safeEntity.SettlementRelation, SettlementRelationCompression)") ||
                 wire.Contains("WriteIntToPacket(entity?.SettlementRelation ?? 0, SettlementRelationCompression)")),
                "native wire source wiring (not actual packet proof): " + file);
        }
        string publisher = File.ReadAllText(Path.Combine(root.FullName, "Campaign/CoopCampaignMapPrototypePublisherBehavior.cs"));
        Check(publisher.Contains("entity.SettlementRelation = CaptureSettlementRelation(settlement)") &&
            publisher.Contains("SettlementRelation = entity.SettlementRelation") &&
            publisher.Contains("hidden.SettlementRelation = 0"), "publisher capture/dynamic/clear wiring");
        string ui = File.ReadAllText(Path.Combine(root.FullName, "UI/CoopCampaignMapPrototypeSettlementOverlayVM.cs"));
        Check(!ui.Contains("public int Relation => 0") && ui.Contains("Relation = entity.SettlementRelation") &&
            ui.Contains("SetField(ref _relation, value, nameof(Relation))"), "stock property update notification");
        Console.WriteLine("Settlement relation contracts passed: " + _checks + " assertions.");
    }

    private static IEnumerable<string> ReplaceRelation(IEnumerable<string> lines, string value) =>
        lines.Select(line => line.Contains(".SettlementRelation=") ? line.Substring(0, line.IndexOf('=') + 1) + value : line);
    private static IEnumerable<string> WithoutRelation(IEnumerable<string> lines) =>
        lines.Where(line => !line.Contains(".SettlementRelation="));
}
