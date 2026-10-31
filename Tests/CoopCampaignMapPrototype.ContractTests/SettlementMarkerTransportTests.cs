using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CoopSpectator.Infrastructure;

internal static class SettlementMarkerTransportTests
{
    private static int checks;

    internal static void Run()
    {
        var host = Program.CreateHostSnapshot(DateTime.UtcNow);
        var full = host.VisibleEntities.First(e => e.Kind == CoopCampaignMapPrototypeEntityKind.Settlement);
        full.SettlementEventFlags = 66;
        full.SettlementEventOrder = 23; // Production, then issue.
        full.QuestMarkerFlags = 24;
        full.SettlementQuestOrder = 37; // Story quest, then ordinary quest.
        full.SettlementProductionIconId = "grain";
        full.IsSettlementInRange = true;
        Check(CoopCampaignMapPrototypeContract.IsValidVisibleEntity(full), "valid full fixture");
        Check(full.Clone().SettlementEventOrder == 23 && full.Clone().SettlementQuestOrder == 37, "full clone");
        Check(host.Clone().VisibleEntities.First(e => e.EntityId == full.EntityId).SettlementQuestOrder == 37, "host deep clone");
        string[] lines = CoopCampaignMapPrototypeBridgeCodec.Serialize(host);
        Check(CoopCampaignMapPrototypeBridgeCodec.TryParse(lines, out var restored, out var reason), "full roundtrip: " + reason);
        var restoredEntity = restored.VisibleEntities.First(e => e.EntityId == full.EntityId);
        Check(restoredEntity.SettlementEventOrder == 23 && restoredEntity.SettlementQuestOrder == 37 &&
            restoredEntity.SettlementProductionIconId == "grain", "full bridge preserves order and production icon");

        var dynamicEntity = new CoopCampaignMapPrototypeDynamicEntityState {
            EntityId = full.EntityId, IsVisible = true, IsSettlementInRange = true,
            SettlementEventFlags = 66, SettlementEventOrder = 23,
            QuestMarkerFlags = 24, SettlementQuestOrder = 37
        };
        Check(CoopCampaignMapPrototypeContract.IsValidDynamicEntity(dynamicEntity), "valid dynamic fixture");
        var snapshot = new CoopCampaignMapPrototypeDynamicSnapshot {
            SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
            SessionId = host.SessionId, Revision = 1, UpdatedUtc = host.UpdatedUtc
        };
        snapshot.Entities.Add(dynamicEntity);
        string[] dynamicLines = CoopCampaignMapPrototypeBridgeCodec.SerializeDynamic(snapshot);
        Check(CoopCampaignMapPrototypeBridgeCodec.TryParseDynamic(dynamicLines, out var restoredDynamic, out reason), "dynamic roundtrip: " + reason);
        Check(restoredDynamic.Entities[0].SettlementEventOrder == 23 && restoredDynamic.Entities[0].SettlementQuestOrder == 37, "dynamic bridge orders");
        Check(snapshot.Clone().Entities[0].SettlementQuestOrder == 37, "dynamic snapshot clone");

        foreach (string field in new[] { "SettlementEventOrder", "SettlementQuestOrder" })
        {
            Check(!CoopCampaignMapPrototypeBridgeCodec.TryParse(lines.Where(x => !x.Contains("." + field + "=")).ToArray(), out _, out _), "full mandatory " + field);
            Check(!CoopCampaignMapPrototypeBridgeCodec.TryParseDynamic(dynamicLines.Where(x => !x.Contains("." + field + "=")).ToArray(), out _, out _), "dynamic mandatory " + field);
            foreach (string bad in new[] { "-1", "2147483647", "8", "9", "0", "not-an-integer" })
            {
                Check(!CoopCampaignMapPrototypeBridgeCodec.TryParse(lines.Select(x => ReplaceField(x, field, bad)).ToArray(), out _, out _), "full corrupt " + field + ": " + bad);
                Check(!CoopCampaignMapPrototypeBridgeCodec.TryParseDynamic(dynamicLines.Select(x => ReplaceField(x, field, bad)).ToArray(), out _, out _), "dynamic corrupt " + field + ": " + bad);
            }
        }
        // Full bridge parsing is syntactic; BridgeFile performs freshness/schema
        // admission separately. Exercise that actual boundary, not a new parser rule.
        Check(CoopCampaignMapPrototypeBridgeCodec.TryParse(lines.Select(x => x.StartsWith("SchemaVersion=") ? "SchemaVersion=26" : x).ToArray(), out var oldHost, out _) &&
            !CoopCampaignMapPrototypeContract.TryValidateHostSnapshot(oldHost, host.UpdatedUtc, TimeSpan.FromSeconds(3), out _), "old full bridge rejected at admission");
        Check(!CoopCampaignMapPrototypeBridgeCodec.TryParseDynamic(dynamicLines.Select(x => x.StartsWith("SchemaVersion=") ? "SchemaVersion=26" : x).ToArray(), out _, out _), "old dynamic bridge rejected");

        var reordered = dynamicEntity.Clone();
        reordered.SettlementEventOrder = 58;
        Check(!CoopCampaignMapPrototypeDynamicDeltaPolicy.AreEquivalent(dynamicEntity, reordered), "event-only order changes equality");
        var delta = CoopCampaignMapPrototypeDynamicDeltaPolicy.BuildDelta(new[] { dynamicEntity }, new[] { reordered });
        Check(delta.Count == 1 && delta[0].SettlementEventOrder == 58, "event-only order emits delta");
        reordered = dynamicEntity.Clone(); reordered.SettlementQuestOrder = 44;
        delta = CoopCampaignMapPrototypeDynamicDeltaPolicy.BuildDelta(new[] { dynamicEntity }, new[] { reordered });
        Check(delta.Count == 1 && delta[0].SettlementQuestOrder == 44, "quest-only order emits delta");
        Check(CoopCampaignMapPrototypeDynamicDeltaPolicy.BuildDelta(new[] { dynamicEntity }, new[] { dynamicEntity.Clone() }).Count == 0, "unchanged order emits nothing");

        var catalog = new CoopCampaignMapPrototypeCatalogEntityState {
            EntityId = full.EntityId, Kind = CoopCampaignMapPrototypeEntityKind.Settlement,
            SettlementProductionIconId = "grain"
        };
        var merged = CoopCampaignMapReplicaStateProjection.Merge(catalog, reordered);
        Check(merged != null && merged.SettlementEventOrder == 23 && merged.SettlementQuestOrder == 44 &&
            merged.SettlementProductionIconId == "grain", "catalog/dynamic merge");
        var cleared = dynamicEntity.Clone();
        cleared.SettlementEventOrder = cleared.SettlementQuestOrder = cleared.SettlementEventFlags = cleared.QuestMarkerFlags = 0;
        delta = CoopCampaignMapPrototypeDynamicDeltaPolicy.BuildDelta(new[] { dynamicEntity }, new[] { cleared });
        Check(delta.Count == 1 && delta[0].SettlementEventOrder == 0 && delta[0].SettlementQuestOrder == 0, "explicit clear emits delta");
        Check(CoopCampaignMapPrototypeContract.IsValidDynamicEntity(cleared), "empty lists valid");
        merged = CoopCampaignMapReplicaStateProjection.Merge(catalog, cleared);
        Check(merged != null && merged.SettlementEventFlags == 0 && merged.QuestMarkerFlags == 0, "clear reaches projection");
        delta = CoopCampaignMapPrototypeDynamicDeltaPolicy.BuildDelta(new[] { dynamicEntity }, Array.Empty<CoopCampaignMapPrototypeDynamicEntityState>());
        Check(delta.Count == 1 && !delta[0].IsVisible && !delta[0].IsSettlementInRange &&
            delta[0].SettlementEventOrder == 0 && delta[0].SettlementQuestOrder == 0 &&
            delta[0].SettlementEventFlags == 0 && delta[0].QuestMarkerFlags == 0, "removed entity cannot retransmit cached markers");
        Check(CoopCampaignMapReplicaStateProjection.Merge(catalog, delta[0]) == null, "removed entity absent from projection");

        var party = host.VisibleEntities.First(e => e.Kind == CoopCampaignMapPrototypeEntityKind.MainParty).Clone();
        party.QuestMarkerFlags = 24;
        Check(CoopCampaignMapPrototypeContract.IsValidVisibleEntity(party), "party quest flags remain unordered");
        party.SettlementQuestOrder = 37;
        Check(!CoopCampaignMapPrototypeContract.IsValidVisibleEntity(party), "party cannot carry settlement quest order");
        var dynamicParty = cleared.Clone(); dynamicParty.EntityId = party.EntityId; dynamicParty.QuestMarkerFlags = 24;
        Check(CoopCampaignMapPrototypeContract.IsValidDynamicEntity(dynamicParty), "dynamic party quest flags remain valid");
        dynamicParty.SettlementQuestOrder = 37;
        Check(!CoopCampaignMapPrototypeContract.IsValidDynamicEntity(dynamicParty), "dynamic party rejects settlement quest order");
        dynamicParty.SettlementQuestOrder = 0; dynamicParty.SettlementEventFlags = 64; dynamicParty.SettlementEventOrder = 7;
        Check(!CoopCampaignMapPrototypeContract.IsValidDynamicEntity(dynamicParty), "party rejects settlement events");
        Check(CoopCampaignMapPrototypeContract.ProtocolVersion == 72 && CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion == 67,
            "coherent incompatible format version bump");
        Check(CoopCampaignMapPrototypeContract.ProtocolCompressionMaximum == 127, "seven-bit protocol range includes version65");
        Check(CoopCampaignMapPrototypeContract.MaxDynamicBatchSize == 2, "bounded two-entry dynamic batch retained");
        SourceWiring();
        Console.WriteLine("Settlement marker transport assertions passed: " + checks);
    }

    private static void SourceWiring()
    {
        int readers = 0;
        foreach (string name in new[] { "Prototype", "Replica", "Conversation", "Tooltip" })
        {
            string wire = File.ReadAllText(Path.Combine("Network", "Messages", "CoopCampaignMap" + name + "NetworkMessages.cs"));
            var reads = Regex.Matches(wire, @"(?:ProtocolVersion|int protocol) = ReadIntFromPacket\(\s*(?:ProtocolCompression|Protocol),\s*ref valid\);");
            foreach (Match read in reads)
            {
                string variable = read.Value.StartsWith("int protocol") ? "protocol" : "ProtocolVersion";
                Check(wire.Substring(read.Index + read.Length).TrimStart().StartsWith(
                    "if (!valid || " + variable + " != CoopCampaignMapPrototypeContract.ProtocolVersion) return false;"),
                    name + " rejects incompatible protocol before parsing payload");
                readers++;
            }
            Check(Regex.Matches(wire, @"CompressionInfo.Integer\(0, CoopCampaignMapPrototypeContract.ProtocolCompressionMaximum, (?:maximumValueGiven: )?true\)").Count == reads.Count,
                name + " every protocol reader/writer uses shared seven-bit range");
            if (name != "Prototype" && name != "Replica") continue;
            foreach (string field in new[] { "SettlementEventOrder", "SettlementQuestOrder" })
            {
                string compression = field == "SettlementEventOrder" ? "EventOrderCompression" : "QuestOrderCompression";
                Check(wire.Contains(field + " = ReadIntFromPacket(" + compression + ", ref valid)"), name + " reads " + field);
                Check(Regex.IsMatch(wire, @"WriteIntToPacket\((?:safeEntity\." + field + @"|entity\?\." + field + @" \?\? 0), " + compression + @"\)"), name + " writes " + field);
            }
        }
        Check(readers == 15, "all native map protocol readers audited");
        string overlay = File.ReadAllText(Path.Combine("UI", "CoopCampaignMapPrototypeSettlementOverlayVM.cs"));
        Check(overlay.Contains("_eventOrder == eventOrder && _questOrder == questOrder") &&
            overlay.Contains("_inRange == inRange") && overlay.Contains("string.Equals(_productionIcon, icon, StringComparison.Ordinal)"),
            "unchanged orders/range/icon retain existing UI lists");
        // Wire audit: unchanged bounded strings; new fixed fields add 21+15 bits
        // per entity, and protocol range is now seven rather than five bits.
        int extraBits = CoopCampaignMapPrototypeContract.MaxDynamicBatchSize * (21 + 15) + 2;
        Check(extraBits == 74 && (extraBits + 7) / 8 == 10, "maximum incremental batch payload is ten bytes");
    }

    private static string ReplaceField(string line, string field, string value)
    {
        int marker = line.IndexOf("." + field + "=", StringComparison.Ordinal);
        return marker < 0 ? line : line.Substring(0, marker + field.Length + 2) + value;
    }

    private static void Check(bool condition, string label)
    {
        checks++;
        if (!condition) throw new InvalidOperationException("Settlement marker transport: " + label);
    }
}
