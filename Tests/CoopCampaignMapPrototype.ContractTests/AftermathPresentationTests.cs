using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopSpectator.Infrastructure;

internal static class AftermathPresentationTests
{
    internal static void Run()
    {
        ValidateAnchoredDynamicFreshness();
        ValidateIndependentRowIdentity();
        ValidateInspectionTransport();
        ValidateLoadoutTarget();
        ValidateTroopTarget();
        ValidateRejectionCodes();
        ValidateCompactLargeScreens();
        ValidateCraftedEquipmentProjection();
        for (int mode = 0; mode < 3; mode++)
        {
            Check(CoopCampaignAftermathComparisonPolicy.IsValidSelection(mode, 0, 0, 0), "nonweapon comparison mode admitted");
            Check(CoopCampaignAftermathComparisonPolicy.IsValidSelection(mode, 1, 2, 3), "alternate usage and fourth comparison admitted");
        }
        Check(!CoopCampaignAftermathComparisonPolicy.IsValidSelection(3, 0, 0, 0) &&
            !CoopCampaignAftermathComparisonPolicy.IsValidSelection(-1, 0, 0, 0) &&
            !CoopCampaignAftermathComparisonPolicy.IsValidSelection(1, 1, 1, 0) &&
            !CoopCampaignAftermathComparisonPolicy.IsValidSelection(1, 1, 0, 0) &&
            !CoopCampaignAftermathComparisonPolicy.IsValidSelection(1, -1, 2, 0) &&
            !CoopCampaignAftermathComparisonPolicy.IsValidSelection(1, 0, 2, 4), "invalid comparison selections reject");
        Check(CoopCampaignAftermathComparisonPolicy.Priority(true, true, true) == 0 &&
            CoopCampaignAftermathComparisonPolicy.Priority(false, true, true) == 1 &&
            CoopCampaignAftermathComparisonPolicy.Priority(false, false, true) == 2 &&
            CoopCampaignAftermathComparisonPolicy.Priority(false, false, false) == 3, "native comparison precedence preserved");
        var root = CoopCampaignPresentationValue.Node();
        root.Properties.Add("TextColor", new CoopCampaignPresentationValue {
            Kind = CoopCampaignPresentationValueKind.Color, Number = 0xff123456u });
        root.Properties.Add("DefinitionLabel", new CoopCampaignPresentationValue {
            Kind = CoopCampaignPresentationValueKind.Text, Text = "Weight" });
        root.Properties.Add("ValueLabel", new CoopCampaignPresentationValue {
            Kind = CoopCampaignPresentationValueKind.Text, Text = "0.68" });
        root.Properties.Add("TitleLbl", new CoopCampaignPresentationValue { Kind = CoopCampaignPresentationValueKind.Text, Text = "Prisoners Полонені" });
        root.Properties.Add("MainPartyPrisoners", new CoopCampaignPresentationValue {
            Kind = CoopCampaignPresentationValueKind.List, Items = new List<CoopCampaignPresentationValue> { CoopCampaignPresentationValue.Node() } });
        var state = new CoopCampaignAftermathPresentation { Kind = CoopCampaignAftermathKind.Prisoners,
            WindowId = "0123456789abcdef0123456789abcdef", Root = root };
        Check(CoopCampaignAftermathCodec.TryEncode(state, out var bytes) &&
            CoopCampaignAftermathCodec.TryDecode(bytes, out var copy) && copy.Root.Properties["TitleLbl"].Text == "Prisoners Полонені",
            "localized prepared presentation roundtrip");
        copy = state.Clone(); copy.Root.Properties["TitleLbl"].Text = "Changed";
        Check(copy.Root.Properties["TextColor"].Kind == CoopCampaignPresentationValueKind.Color &&
            copy.Root.Properties["TextColor"].Number == 0xff123456u &&
            copy.Root.Properties["DefinitionLabel"].Text == "Weight" && copy.Root.Properties["ValueLabel"].Text == "0.68",
            "prepared tooltip labels and typed color survive transport");
        root.Properties["TextColor"].Number = (double)uint.MaxValue + 1;
        Check(!state.IsValid(), "out-of-range typed color rejected");
        root.Properties["TextColor"].Number = .5;
        Check(!state.IsValid(), "fractional typed color rejected");
        root.Properties["TextColor"].Number = 0xff123456u;
        Check(root.Properties["TitleLbl"].Text != "Changed", "nested clone isolates live screen");
        var menu = new CoopCampaignMapMenuPresentation { MenuId = "aftermath:1:" + state.WindowId, Aftermath = state };
        Check(CoopCampaignMapInterfaceCodec.TryEncode(3, menu, null, out var envelope) &&
            CoopCampaignMapInterfaceCodec.TryDecode(envelope, 3, out var decoded, out var bar) &&
            decoded.Aftermath.Kind == CoopCampaignAftermathKind.Prisoners && bar == null, "existing atomic network envelope");
        menu.IsInSiegeMode = true;
        Check(!menu.IsValid(), "aftermath cannot alias siege controls"); menu.IsInSiegeMode = false;
        menu.MenuId = "old-window"; Check(!menu.IsValid(), "screen identity binds kind and owner");
        for (int i = 0; i < bytes.Length; i++)
            Check(!CoopCampaignAftermathCodec.TryDecode(new ArraySegment<byte>(bytes, 0, i).ToArray(), out _), "truncation rejects");
        var extra = new byte[bytes.Length + 1]; Array.Copy(bytes, extra, bytes.Length);
        Check(!CoopCampaignAftermathCodec.TryDecode(extra, out _), "trailing bytes reject");
        var invalid = (byte[])bytes.Clone(); invalid[4] = 3;
        Check(!CoopCampaignAftermathCodec.TryDecode(invalid, out _), "unknown screen mode rejects");
        root.Properties.Add("Unsafe.Path", new CoopCampaignPresentationValue());
        Check(!state.IsValid(), "binding traversal injection rejected"); root.Properties.Remove("Unsafe.Path");
        root.Properties.Add("Cycle", root); Check(!state.IsValid(), "cycle rejected"); root.Properties.Remove("Cycle");
        root.Properties.Add("Bad", new CoopCampaignPresentationValue { Kind = CoopCampaignPresentationValueKind.Integer, Number = .5 });
        Check(!state.IsValid(), "fractional integer rejects"); root.Properties.Remove("Bad");
        var list = root.Properties["MainPartyPrisoners"].Items;
        for (int i = list.Count; i <= CoopCampaignAftermathCodec.MaximumItems; i++) list.Add(new CoopCampaignPresentationValue());
        Check(!state.IsValid(), "oversize screen fails rather than silently truncating prisoners");
        Check(CoopCampaignMapMenuSendPolicy.ShouldPreempt("aftermath:1:a", "aftermath:2:b", false), "prisoner-to-loot transition preempts old transfer");
        Console.WriteLine("Aftermath detached presentation, bounded transport and screen-identity checks passed.");
    }
    private static void ValidateRejectionCodes()
    {
        var root = CoopCampaignPresentationValue.Node();
        var state = new CoopCampaignAftermathPresentation { Kind = CoopCampaignAftermathKind.Prisoners,
            WindowId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", Root = root };
        Check(CoopCampaignAftermathCodec.TryEncode(state, out _, out var reason) && reason == null, "valid tree has no rejection code");
        root.Properties.Add("Sensitive.Value", new CoopCampaignPresentationValue());
        Check(!CoopCampaignAftermathCodec.TryEncode(state, out _, out reason) && reason == "property-name", "fixed code omits rejected binding contents");
        root.Properties.Clear(); root.Properties.Add("Value", new CoopCampaignPresentationValue { Kind = CoopCampaignPresentationValueKind.Double, Number = double.NaN });
        Check(!CoopCampaignAftermathCodec.TryEncode(state, out _, out reason) && reason == "number", "nonfinite number distinguished");
        root.Properties.Clear(); root.Properties.Add("Loop", root);
        Check(!CoopCampaignAftermathCodec.TryEncode(state, out _, out reason) && reason == "cycle", "cycle distinguished");
        root.Properties.Clear(); root.Properties.Add("Value", new CoopCampaignPresentationValue { Kind = CoopCampaignPresentationValueKind.Text, Text = new string('x', 32769) });
        Check(!CoopCampaignAftermathCodec.TryEncode(state, out _, out reason) && reason == "text-limit", "individual text limit distinguished");
        root.Properties.Clear();
        // Unique ordinary-sized strings still enforce the unchanged wire bound.
        for (int i = 0; i < 100; i++) root.Properties.Add("Field" + i, new CoopCampaignPresentationValue {
            Kind = CoopCampaignPresentationValueKind.Text, Text = new string((char)(0x100 + i), 10000) });
        Check(!CoopCampaignAftermathCodec.TryEncode(state, out _, out reason) && reason == "byte-limit", "total byte limit distinguished");
        string repo = Environment.GetEnvironmentVariable("COOPSPECTATOR_REPOSITORY_ROOT");
        string capture = File.ReadAllText(Path.Combine(repo, "Campaign", "CoopCampaignAftermathCapture.cs"));
        Check(capture.Contains("COOPSPECTATOR_AFTERMATH_CAPTURE_DIAGNOSTICS") && capture.Contains("if (CaptureDiagnostics) reason +="), "host rejection detail requires explicit opt-in");
    }
    private static void ValidateCraftedEquipmentProjection()
    {
        string original = string.Concat(Enumerable.Range(0, 12).Select(slot => "+" + slot + "-" +
            (slot == 0 ? "crafted_item_0" : slot == 5 ? "campaign_helmet" : "@null") + "-@null"));
        foreach (string mirror in new[] { "cs_crafted_first_save", "cs_crafted_other_save" })
        {
            var root = CoopCampaignPresentationValue.Node();
            root.Properties.Add("EquipmentCode", new CoopCampaignPresentationValue {
                Kind = CoopCampaignPresentationValueKind.Text, Text = original });
            var image = CoopCampaignPresentationValue.Node();
            image.Properties.Add("Id", new CoopCampaignPresentationValue { Kind = CoopCampaignPresentationValueKind.Text, Text = "crafted_item_0" });
            image.Properties.Add("TextureProviderName", new CoopCampaignPresentationValue {
                Kind = CoopCampaignPresentationValueKind.Text, Text = "ItemImageTextureProvider" });
            root.Properties.Add("WeaponImage", image);
            var portrait = CoopCampaignPresentationValue.Node();
            portrait.Properties.Add("TextureProviderName", new CoopCampaignPresentationValue {
                Kind = CoopCampaignPresentationValueKind.Text, Text = "CharacterImageTextureProvider" });
            portrait.Properties.Add("Id", new CoopCampaignPresentationValue { Kind = CoopCampaignPresentationValueKind.Text,
                Text = "@---@" + original + "@---@body@---@0@---@0@---@0@---@1@---@2@---@0@---@" });
            root.Properties.Add("HeroRowPortrait", portrait);
            CoopCampaignAftermathEquipmentProjection.ApplyCraftedAliases(root,
                new Dictionary<string, string> { { "crafted_item_0", mirror } });
            string resolved = root.Properties["EquipmentCode"].Text;
            Check(resolved.Contains("+0-" + mirror + "-@null") && resolved.Contains("+5-campaign_helmet-@null") &&
                resolved.Split('+').Length == 13 && image.Properties["Id"].Text == mirror,
                "crafted weapon fingerprint resolves without stripping armor or any equipment slot");
            Check(CoopConversationHudCodec.IsPortraitCode(portrait.Properties["Id"].Text) &&
                portrait.Properties["Id"].Text.Contains("+0-" + mirror + "-@null") && portrait.Properties["Id"].Text.Contains("@---@body@---@"),
                "hero row portrait resolves the same exact crafted identity while retaining body and colors");
            Check(CoopConversationHudCodec.ResolveEquipmentCode(resolved, id => id, _ => true) == resolved,
                "projected complete equipment survives strict client parser");
        }
        Check(original.Contains("crafted_item_0") && !original.Contains("cs_crafted"),
            "same save-local original ID is not changed or globally remapped");
    }

    private static void ValidateCompactLargeScreens()
    {
        // Large siege aftermath: the former encoding repeats each long binding
        // name/text per troop. Keep every row/value; do not raise transport limits.
        foreach (var kind in new[] { CoopCampaignAftermathKind.Prisoners, CoopCampaignAftermathKind.Loot })
        {
            var root = CoopCampaignPresentationValue.Node();
            var rows = new CoopCampaignPresentationValue { Kind = CoopCampaignPresentationValueKind.List,
                Items = new List<CoopCampaignPresentationValue>() };
            root.Properties.Add("Rows", rows);
            for (int row = 0; row < 600; row++)
            {
                var node = CoopCampaignPresentationValue.Node();
                node.Properties.Add("Name", new CoopCampaignPresentationValue {
                    Kind = CoopCampaignPresentationValueKind.Text, Text = "Troop " + row });
                for (int field = 0; field < 50; field++) node.Properties.Add("RepeatedPreparedProperty" + field,
                    new CoopCampaignPresentationValue { Kind = CoopCampaignPresentationValueKind.Text,
                        Text = "Prepared localized value — повний опис " + field });
                rows.Items.Add(node);
            }
            var state = new CoopCampaignAftermathPresentation { Kind = kind,
                WindowId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", Root = root };
            Check(CoopCampaignAftermathCodec.TryEncode(state, out var encoded) && encoded.Length < CoopCampaignAftermathCodec.MaximumBytes / 2,
                kind + " large complete screen fits existing wire budget through references");
            Check(CoopCampaignAftermathCodec.TryDecode(encoded, out var decoded), kind + " compact screen decodes");
            var decodedRows = decoded.Root.Properties["Rows"].Items;
            Check(decodedRows.Count == 600 && decodedRows[599].Properties["Name"].Text == "Troop 599" &&
                decodedRows[599].Properties["RepeatedPreparedProperty49"].Text == "Prepared localized value — повний опис 49",
                kind + " final row and every property retained without truncation");
            // Roundtrip must be byte-canonical; malformed references cannot read
            // a later entry, carry a table between messages or duplicate literals.
            Check(CoopCampaignAftermathCodec.TryEncode(decoded, out var again) && encoded.SequenceEqual(again),
                kind + " deterministic string table roundtrip");
            byte[] forward = (byte[])encoded.Clone();
            // CAF3 header: magic4 + kind1 + id-length4 + id32 + root-kind1 + count4.
            Array.Copy(BitConverter.GetBytes(0), 0, forward, 46, 4);
            Check(!CoopCampaignAftermathCodec.TryDecode(forward, out _), kind + " undefined/forward string reference rejected");
        }
        var invalid = new byte[] { 0x33, 0x46, 0x41, 0x43 };
        Check(!CoopCampaignAftermathCodec.TryDecode(invalid, out _), "incomplete compact frame rejected");
    }
    private static void ValidateInspectionTransport()
    {
        var target = new CoopCampaignAftermathInspectionTarget { WindowId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            RowId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", HeroId = "cccccccccccccccccccccccccccccccc", EquipmentMode = 1 };
        string id = target.Encode();
        Check(id.Length <= CoopCampaignMapPrototypeContract.MaxEntityIdCharacters &&
            CoopCampaignAftermathInspectionTarget.TryParse(id, out var parsed) && parsed.RowId == target.RowId && parsed.HeroId == target.HeroId,
            "compact independent inspection correlation roundtrip");
        Check(!CoopCampaignAftermathInspectionTarget.TryParse(id + ":x", out _) &&
            !CoopCampaignAftermathInspectionTarget.TryParse(id.Replace(":1:", ":01:"), out _), "noncanonical inspection correlation rejects");
        var root = CoopCampaignPresentationValue.Node();
        var item = CoopCampaignPresentationValue.Node(); var hero = CoopCampaignPresentationValue.Node();
        item.Properties.Add("ReplicaRowId", new CoopCampaignPresentationValue { Kind = CoopCampaignPresentationValueKind.Text, Text = target.RowId });
        hero.Properties.Add("ReplicaRowId", new CoopCampaignPresentationValue { Kind = CoopCampaignPresentationValueKind.Text, Text = target.HeroId });
        root.Properties.Add("LeftItemListVM", new CoopCampaignPresentationValue { Kind = CoopCampaignPresentationValueKind.List,
            Items = new List<CoopCampaignPresentationValue> { item } });
        var selector = CoopCampaignPresentationValue.Node();
        selector.Properties.Add("ItemList", new CoopCampaignPresentationValue { Kind = CoopCampaignPresentationValueKind.List,
            Items = new List<CoopCampaignPresentationValue> { hero } }); root.Properties.Add("CharacterList", selector);
        var card = new CoopCampaignAftermathPresentation { Kind = CoopCampaignAftermathKind.Loot, WindowId = target.WindowId, Root = root };
        Check(CoopCampaignAftermathInspectionTarget.IsDisclosed(card, id), "current screen discloses item and hero");
        var payload = new CoopCampaignMapTooltipPayload { SessionId = "dddddddddddddddddddddddddddddddd", EntityId = id,
            RequestGeneration = 1, Sequence = 1, Kind = CoopCampaignMapTooltipKind.AftermathItem, AftermathCard = card };
        Check(CoopCampaignMapTooltipPayloadCodec.TrySerialize(payload, out var bytes) &&
            CoopCampaignMapTooltipPayloadCodec.TryDeserialize(bytes, out var copy) && copy.AftermathCard.WindowId == card.WindowId,
            "full prepared card survives existing per-peer tooltip envelope");
        payload.AftermathCard.WindowId = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        Check(!CoopCampaignMapTooltipPayloadCodec.IsValid(payload), "response for different window rejects");
        payload.AftermathCard.WindowId = target.WindowId;
        root.Properties["LeftItemListVM"].Items.Clear();
        Check(!CoopCampaignAftermathInspectionTarget.IsDisclosed(card, id), "host removal revokes network target");
        target.RowId = null; target.EquipmentSlot = 5;
        string gear = target.Encode();
        Check(gear.StartsWith("gear:") && gear.Length <= 96 &&
            CoopCampaignAftermathInspectionTarget.TryParse(gear, out var equipped) && equipped.EquipmentSlot == 5 && equipped.HeroId == target.HeroId,
            "equipment target resolves hero slot without borrowing host-selected row identity");
        Check(CoopCampaignAftermathInspectionTarget.IsDisclosed(card, gear), "equipment hover works with empty loot lists");
        foreach (string invalid in new[] { gear.Replace(":5:", ":12:"), gear.Replace(":5:", ":05:"),
            gear.Replace(":5:", ":-1:"), gear + ":x" })
            Check(!CoopCampaignAftermathInspectionTarget.TryParse(invalid, out _), "invalid or noncanonical equipment slot rejects");
        payload.EntityId = gear;
        Check(CoopCampaignMapTooltipPayloadCodec.TrySerialize(payload, out var gearBytes) &&
            CoopCampaignMapTooltipPayloadCodec.TryDeserialize(gearBytes, out var gearCopy) && gearCopy.EntityId == gear,
            "equipment card uses existing bounded tooltip envelope");
        selector.Properties["ItemList"].Items.Clear();
        Check(!CoopCampaignAftermathInspectionTarget.IsDisclosed(card, gear), "removed hero revokes equipment interest");
    }
    private static void ValidateLoadoutTarget()
    {
        var target = new CoopCampaignAftermathLoadoutTarget { WindowId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            HeroId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", EquipmentMode = 1 };
        string id = target.Encode();
        Check(id.Length <= 96 && CoopCampaignAftermathLoadoutTarget.TryParse(id, out var copy) &&
            copy.HeroId == target.HeroId && copy.EquipmentMode == 1, "hero-only correlation roundtrip");
        foreach (string invalid in new[] { id + ":extra", id.Replace(":1", ":01"), id.Replace(":1", ":3"), id.ToUpperInvariant(), "" })
            Check(!CoopCampaignAftermathLoadoutTarget.TryParse(invalid, out _), "noncanonical loadout rejects");
        var row = CoopCampaignPresentationValue.Node();
        row.Properties.Add("ReplicaRowId", new CoopCampaignPresentationValue { Kind = CoopCampaignPresentationValueKind.Text, Text = target.HeroId });
        var list = new CoopCampaignPresentationValue { Kind = CoopCampaignPresentationValueKind.List,
            Items = new List<CoopCampaignPresentationValue> { row } };
        var selector = CoopCampaignPresentationValue.Node(); selector.Properties.Add("ItemList", list);
        var root = CoopCampaignPresentationValue.Node(); root.Properties.Add("CharacterList", selector);
        var state = new CoopCampaignAftermathPresentation { Kind = CoopCampaignAftermathKind.Loot, WindowId = target.WindowId, Root = root };
        Check(CoopCampaignAftermathLoadoutTarget.IsDisclosed(state, id), "hero preview works with empty loot lists");
        root.Properties.Add("HeroRowId", new CoopCampaignPresentationValue { Kind = CoopCampaignPresentationValueKind.Text, Text = target.HeroId });
        root.Properties.Add("EquipmentMode", new CoopCampaignPresentationValue { Kind = CoopCampaignPresentationValueKind.Integer, Number = 1 });
        root.Properties.Add("MainCharacter", CoopCampaignPresentationValue.Node());
        root.Properties.Add("ReplicaLoadoutVersion", new CoopCampaignPresentationValue { Kind = CoopCampaignPresentationValueKind.Text, Text = "equipment-v1" });
        var response = new CoopCampaignMapTooltipPayload { SessionId = "dddddddddddddddddddddddddddddddd", EntityId = id,
            RequestGeneration = 1, Sequence = 1, Kind = CoopCampaignMapTooltipKind.AftermathLoadout, AftermathCard = state };
        Check(CoopCampaignMapTooltipPayloadCodec.TrySerialize(response, out var encoded) &&
            CoopCampaignMapTooltipPayloadCodec.TryDeserialize(encoded, out var decoded) && decoded.Kind == response.Kind &&
            target.MatchesPresentation(decoded.AftermathCard), "hero visual response roundtrip with exact correlation");
        root.Properties["HeroRowId"].Text = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        Check(!CoopCampaignMapTooltipPayloadCodec.IsValid(response), "different hero visual rejects");
        root.Properties["HeroRowId"].Text = target.HeroId;
        root.Properties["EquipmentMode"].Number = 0;
        Check(!CoopCampaignMapTooltipPayloadCodec.IsValid(response), "different equipment mode rejects");
        root.Properties["EquipmentMode"].Number = 1; response.IsExtended = true;
        Check(!CoopCampaignMapTooltipPayloadCodec.IsValid(response), "loadout cannot alias extended item tooltip");
        response.IsExtended = false; response.Kind = CoopCampaignMapTooltipKind.AftermathItem;
        Check(!CoopCampaignMapTooltipPayloadCodec.IsValid(response), "item kind cannot impersonate loadout response");
        list.Items.Add(row);
        Check(!CoopCampaignAftermathLoadoutTarget.IsDisclosed(state, id), "ambiguous duplicate hero rejects");
        list.Items.Clear();
        Check(!CoopCampaignAftermathLoadoutTarget.IsDisclosed(state, id), "removed hero revokes preview");
        list.Items.Add(row); state.WindowId = "cccccccccccccccccccccccccccccccc";
        Check(!CoopCampaignAftermathLoadoutTarget.IsDisclosed(state, id), "replaced window revokes preview");
        state.WindowId = target.WindowId; state.Kind = CoopCampaignAftermathKind.Prisoners;
        Check(!CoopCampaignAftermathLoadoutTarget.IsDisclosed(state, id), "prisoner window cannot impersonate loadout");
    }
    private static void ValidateTroopTarget()
    {
        var target = new CoopCampaignAftermathTroopTarget { WindowId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", RowId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" };
        string id = target.Encode();
        Check(id.Length <= 96 && CoopCampaignAftermathTroopTarget.TryParse(id, out var parsed) && parsed.RowId == target.RowId,
            "independent troop target roundtrip");
        foreach (string invalid in new[] { id + ":extra", id.ToUpperInvariant(), id.Replace("troop:", "loot:"), string.Empty })
            Check(!CoopCampaignAftermathTroopTarget.TryParse(invalid, out _), "malformed troop target rejects");
        var row = CoopCampaignPresentationValue.Node();
        row.Properties.Add("ReplicaRowId", new CoopCampaignPresentationValue { Kind = CoopCampaignPresentationValueKind.Text, Text = target.RowId });
        row.Properties.Add("Number", new CoopCampaignPresentationValue { Kind = CoopCampaignPresentationValueKind.Integer, Number = 2 });
        var root = CoopCampaignPresentationValue.Node();
        var list = new CoopCampaignPresentationValue { Kind = CoopCampaignPresentationValueKind.List, Items = new List<CoopCampaignPresentationValue> { row } };
        root.Properties.Add("OtherPartyPrisoners", list);
        var state = new CoopCampaignAftermathPresentation { Kind = CoopCampaignAftermathKind.Prisoners, WindowId = target.WindowId, Root = root };
        Check(CoopCampaignAftermathTroopTarget.IsDisclosed(state, id), "current positive prisoner row admitted");
        var request = new CoopCampaignMapTooltipRequest("dddddddddddddddddddddddddddddddd", Guid.NewGuid(), id, 1, false);
        var menu = new CoopCampaignMapMenuPresentation { Aftermath = state };
        Check(CoopCampaignMapTooltipTargetAdmission.IsAftermathAdmitted(request.SessionId, request, menu),
            "disclosed troop target admitted through owned current session");
        Check(!CoopCampaignMapTooltipTargetAdmission.IsAftermathAdmitted("eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee", request, menu),
            "different publisher session cannot admit prisoner selection");
        request = new CoopCampaignMapTooltipRequest(request.SessionId, request.LeaseId, id, 1, true);
        Check(!CoopCampaignMapTooltipTargetAdmission.IsAftermathAdmitted(request.SessionId, request, menu),
            "troop preview cannot impersonate extended item tooltip");
        request = new CoopCampaignMapTooltipRequest(request.SessionId, request.LeaseId, id, 1, false);
        var preview = CoopCampaignPresentationValue.Node();
        preview.Properties.Add("SelectedCharacter", CoopCampaignPresentationValue.Node());
        preview.Properties.Add("CurrentCharacter", row);
        var response = new CoopCampaignMapTooltipPayload { SessionId = request.SessionId, EntityId = id,
            RequestGeneration = 1, Sequence = 1, Kind = CoopCampaignMapTooltipKind.AftermathTroop,
            AftermathCard = new CoopCampaignAftermathPresentation { Kind = CoopCampaignAftermathKind.Prisoners,
                WindowId = target.WindowId, Root = preview } };
        Check(CoopCampaignMapTooltipPayloadCodec.TrySerialize(response, out var encoded) &&
            CoopCampaignMapTooltipPayloadCodec.TryDeserialize(encoded, out var decoded) && target.MatchesPresentation(decoded.AftermathCard),
            "full troop preview roundtrip binds exact row and window");
        row.Properties["ReplicaRowId"].Text = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        Check(!CoopCampaignMapTooltipPayloadCodec.IsValid(response), "another row cannot impersonate selected troop");
        row.Properties["ReplicaRowId"].Text = target.RowId;
        response.AftermathCard.Kind = CoopCampaignAftermathKind.Loot;
        Check(!CoopCampaignMapTooltipPayloadCodec.IsValid(response), "loot payload cannot impersonate prisoner preview");
        response.AftermathCard.Kind = CoopCampaignAftermathKind.Prisoners;
        root.Properties.Add("MainPartyTroops", list);
        Check(!CoopCampaignAftermathTroopTarget.IsDisclosed(state, id), "same identity in two rosters rejects");
        root.Properties.Remove("MainPartyTroops"); row.Properties["Number"].Number = 0;
        Check(!CoopCampaignAftermathTroopTarget.IsDisclosed(state, id), "zero-count row revokes selection");
        row.Properties["Number"].Number = 2; list.Items.Clear();
        Check(!CoopCampaignAftermathTroopTarget.IsDisclosed(state, id), "removed row cannot reuse old target");
        list.Items.Add(row); state.WindowId = "cccccccccccccccccccccccccccccccc";
        Check(!CoopCampaignAftermathTroopTarget.IsDisclosed(state, id), "replacement screen rejects old prisoner interest");
        state.WindowId = target.WindowId; state.Kind = CoopCampaignAftermathKind.Loot;
        Check(!CoopCampaignAftermathTroopTarget.IsDisclosed(state, id), "Loot cannot impersonate prisoner screen");
    }
    private static void ValidateIndependentRowIdentity()
    {
        var registry = new CoopCampaignAftermathRowRegistry();
        var first = new object(); var second = new object();
        registry.Begin("window-a");
        string a = registry.Observe(first), b = registry.Observe(second); registry.Complete();
        Check(a != b, "equal-looking rows must have distinct reference identities");
        registry.Begin("window-a");
        Check(registry.Observe(second) == b && registry.Observe(first) == a, "reordering preserves row identity");
        registry.Complete();
        Check(registry.TryResolve("window-a", a, out var resolved) && ReferenceEquals(first, resolved), "exact current row resolves");
        Check(!registry.TryResolve("window-b", a, out _), "other window rejected");
        registry.Begin("window-a"); registry.Observe(second); registry.Complete();
        Check(!registry.TryResolve("window-a", a, out _), "host transfer/removal revokes old row");
        registry.Begin("window-a");
        Check(registry.Observe(first) != a, "removed/reintroduced row cannot revive stale inspection"); registry.Complete();
        registry.Begin("window-b"); registry.Observe(first); registry.Complete();
        Check(!registry.TryResolve("window-a", a, out _), "screen replacement revokes previous window");
        registry.Clear(); Check(!registry.TryResolve("window-b", b, out _), "teardown revokes registry");
    }
    private static void ValidateAnchoredDynamicFreshness()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CoopAftermathFreshnessTests", Guid.NewGuid().ToString("N"));
        string prior = CoopSpectator.Infrastructure.Automation.CoopAutomationRuntimeBridge.TestCoopFolderPath;
        Directory.CreateDirectory(directory);
        CoopSpectator.Infrastructure.Automation.CoopAutomationRuntimeBridge.TestCoopFolderPath = directory;
        try
        {
            DateTime now = DateTime.UtcNow;
            var dynamic = new CoopCampaignMapPrototypeDynamicSnapshot {
                SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
                SessionId = "0123456789abcdef0123456789abcdef", Revision = 7,
                UpdatedUtc = now.AddMinutes(-5) };
            Check(CoopCampaignMapPrototypeBridgeFile.TryWriteDynamic(dynamic, out var reason), "write retained pre-battle dynamic: " + reason);
            Check(!CoopCampaignMapPrototypeBridgeFile.TryReadFreshDynamic(now, TimeSpan.FromSeconds(2), out _, out reason)
                && reason == "stale", "new map mission rejects stale pre-battle dynamic");
            dynamic.UpdatedUtc = now;
            Check(CoopCampaignMapPrototypeBridgeFile.TryWriteDynamic(dynamic, out reason), "renew retained dynamic publication: " + reason);
            Check(CoopCampaignMapPrototypeBridgeFile.TryReadFreshDynamic(now, TimeSpan.FromSeconds(2), out var received, out reason)
                && received.SessionId == dynamic.SessionId && received.Revision == 7 && received.Entities.Count == 0,
                "renewal admits unchanged identity and data after battle: " + reason);
            Check(!CoopCampaignMapPrototypeBridgeFile.TryReadFreshDynamic(now.AddSeconds(3), TimeSpan.FromSeconds(2), out _, out reason)
                && reason == "stale", "renewal does not disable expiry after host stops publishing");
        }
        finally
        {
            CoopSpectator.Infrastructure.Automation.CoopAutomationRuntimeBridge.TestCoopFolderPath = prior;
            Directory.Delete(directory, true);
        }
    }
    private static void Check(bool value, string message)
    { if (!value) throw new Exception("Aftermath: " + message); }
}
