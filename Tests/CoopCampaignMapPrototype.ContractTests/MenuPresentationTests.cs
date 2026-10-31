using System;
using System.Linq;
using System.IO;
using System.Xml.Linq;
using CoopSpectator.Infrastructure;

internal static class MenuPresentationTests
{
    private static int _checks;
    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new Exception("Menu presentation: " + label); }
    internal static void Run()
    {
        TestEncounterSnapshot();
        PlunderIdentityTests.Run();
        EncounterIdentityTests.Run();
        var menu = new CoopCampaignMapMenuPresentation { MenuId = "encounter", Context = "Host text" };
        menu.Options.Add(new CoopCampaignMapMenuOption { Text = "Attack", IsEnabled = true });
        menu.Options.Add(new CoopCampaignMapMenuOption { Text = "Retreat", IsEnabled = false, Hint = "Unavailable" });
        menu.ProgressItems.Add(new CoopCampaignMapMenuProgress { Progress = 37, Text = "Raid", ProgressText = "2 hours" });
        menu.PlunderItems.Add(new CoopCampaignMapMenuPlunder { ItemId = "grain", Amount = 5 });
        Check(menu.TrySnapshot(out var snapshot), "valid capture");
        Check(snapshot.Options[0].Text == "Attack" && snapshot.Options[1].Text == "Retreat", "host ordering");
        Check(!snapshot.Options[1].IsEnabled && snapshot.Options[1].Hint == "Unavailable", "host disabled appearance");
        menu.Options[0].Text = "Changed"; menu.ProgressItems[0].Progress = 99;
        Check(snapshot.Options[0].Text == "Attack" && snapshot.ProgressItems[0].Progress == 37, "deep detached capture");
        menu.PlunderItems[0].Amount = 7;
        Check(snapshot.PlunderItems[0].Amount == 5, "detached plunder count");
        menu.PlunderItems[0].ItemId = "../grain";
        Check(!menu.IsValid(), "plunder identifier is not a file path");
        menu.PlunderItems[0].ItemId = "grain";
        menu.Options[0].BattleSize = -1;
        Check(menu.TrySnapshot(out var noBattle), "native no-battle sentinel accepted");
        Check(CoopCampaignMapMenuCodec.TrySerialize(9, noBattle, out var noBattleBytes) &&
            CoopCampaignMapMenuCodec.TryDeserialize(noBattleBytes, out _, out var noBattleDecoded) &&
            noBattleDecoded.Options[0].BattleSize == -1, "no-battle sentinel preserved on wire");
        menu.Options[0].BattleSize = -2;
        Check(!menu.IsValid(), "invalid negative battle size rejected");
        menu.Options[0].BattleSize = 0;
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f, 101f })
        {
            menu.ProgressItems[0].Progress = invalid;
            Check(!menu.TrySnapshot(out var rejected) && rejected == null, "invalid progress has no partial result");
        }
        foreach (float valid in new[] { 0f, 1f, 50f, 100f })
        { menu.ProgressItems[0].Progress = valid; Check(menu.IsValid(), "native percentage range"); }
        menu.Options.Add(null); Check(!menu.IsValid(), "null option"); menu.Options.RemoveAt(2);
        menu.Context = new string('x', CoopCampaignMapMenuPresentation.MaxTextCharacters + 1);
        Check(!menu.IsValid(), "bounded text"); menu.Context = "";
        while (menu.Options.Count < CoopCampaignMapMenuPresentation.MaxOptions)
            menu.Options.Add(new CoopCampaignMapMenuOption());
        Check(menu.IsValid(), "maximum options retained"); menu.Options.Add(new CoopCampaignMapMenuOption());
        Check(!menu.IsValid(), "no silent option truncation");
        Check(typeof(CoopCampaignMapMenuOption).GetMethod("ExecuteAction") == null, "no native action binding");
        Check(CoopCampaignMapMenuCodec.TrySerialize(1, snapshot, out var bytes), "encode presentation");
        Check(CoopCampaignMapMenuCodec.TryDeserialize(bytes, out var revision, out var decoded) && revision == 1 &&
            decoded.Options[1].Text == "Retreat" && !decoded.Options[1].IsEnabled &&
            decoded.Options[1].Hint == "Unavailable" && decoded.ProgressItems[0].Progress == 37 &&
            decoded.PlunderItems[0].ItemId == "grain" && decoded.PlunderItems[0].Amount == 5, "wire roundtrip");
        for (int length = 0; length < bytes.Length; length++)
        {
            var truncated = new byte[length]; Array.Copy(bytes, truncated, length);
            Check(!CoopCampaignMapMenuCodec.TryDeserialize(truncated, out revision, out decoded) &&
                revision == 0 && decoded == null, "truncation publishes nothing");
        }
        var trailing = new byte[bytes.Length + 1]; Array.Copy(bytes, trailing, bytes.Length);
        Check(!CoopCampaignMapMenuCodec.TryDeserialize(trailing, out _, out _), "trailing bytes rejected");
        var malformed = (byte[])bytes.Clone(); malformed[12] = 2;
        Check(!CoopCampaignMapMenuCodec.TryDeserialize(malformed, out _, out _), "noncanonical boolean rejected");
        Check(CoopCampaignMapMenuCodec.TrySerialize(2, null, out var close) &&
            CoopCampaignMapMenuCodec.TryDeserialize(close, out revision, out decoded) && revision == 2 && decoded == null,
            "explicit close roundtrip");
        Check(!CoopCampaignMapMenuCodec.TrySerialize(0, snapshot, out _), "revision required");
        snapshot.Context = "Рейд — 村 🛡";
        Check(CoopCampaignMapMenuCodec.TrySerialize(3, snapshot, out bytes) &&
            CoopCampaignMapMenuCodec.TryDeserialize(bytes, out _, out decoded) && decoded.Context == snapshot.Context,
            "unicode exact text");
        snapshot.Context = "\ud800";
        Check(!CoopCampaignMapMenuCodec.TrySerialize(3, snapshot, out _), "invalid unicode rejected");
        snapshot.Context = "Host encounter";
        var host = new CoopCampaignMapPrototypeHostSnapshot
        {
            SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
            SessionId = "0123456789abcdef0123456789abcdef", Revision = 7,
            IsActive = true, UpdatedUtc = DateTime.UtcNow, Menu = snapshot
        };
        var lines = CoopCampaignMapPrototypeBridgeCodec.Serialize(host);
        Check(CoopCampaignMapPrototypeBridgeCodec.TryParse(lines, out var received, out _) &&
            received.Menu.Context == snapshot.Context && received.Menu.ProgressItems[0].Progress == 37 &&
            !received.Menu.Options[1].IsEnabled && received.Menu.Options[1].Hint == "Unavailable",
            "host bridge carries menu presentation");
        Check(!CoopCampaignMapPrototypeBridgeCodec.TryParse(lines.Where(s => !s.StartsWith("MenuPresentation=")).ToArray(),
            out _, out _), "bridge menu field required");
        Check(!CoopCampaignMapPrototypeBridgeCodec.TryParse(lines.Select(s => s.StartsWith("Revision=") ? "Revision=8" : s).ToArray(),
            out _, out _), "menu bound to host revision");
        host.Menu = null;
        Check(CoopCampaignMapPrototypeBridgeCodec.TryParse(CoopCampaignMapPrototypeBridgeCodec.Serialize(host),
            out received, out _) && received.Menu == null, "host bridge carries close");
        TestChunks(snapshot);
        Check(!CoopCampaignMapMenuSendPolicy.ShouldPreempt("raid", "raid", false), "progress cannot starve in-flight menu");
        Check(CoopCampaignMapMenuSendPolicy.ShouldPreempt("raid", null, false), "close preempts immediately");
        Check(CoopCampaignMapMenuSendPolicy.ShouldPreempt("raid", "encounter", false), "different window preempts");
        Check(CoopCampaignMapMenuSendPolicy.ShouldPreempt("raid", "raid", true), "session boundary preempts");
        var root = Environment.GetEnvironmentVariable("COOPSPECTATOR_REPOSITORY_ROOT");
        var catalog = XDocument.Load(Path.Combine(root, "Module", "CoopSpectator", "ModuleData",
            "coopspectator_campaign_mirror_items.xml")).Root.Elements("Item").ToArray();
        // Independent native DefaultItems table: includes goods absent from campaign XML.
        foreach (string row in new[] {
            "grain|merchandise_grain|10|10|true",
            "meat|merchandise_meat|30|10|true",
            "planks|bd_planks_a|180|10|false",
            "felt|merchandise_hides_b|230|10|false",
            "hides|merchandise_hides_b|50|10|false",
            "tools|bd_pickaxe_b|250|10|false",
            "iron|iron_ore|50|10|false",
            "hardwood|hardwood|25|10|false",
            "charcoal|charcoal|50|5|false",
            "ironIngot1|crude_iron|20|0.5|false",
            "ironIngot2|wrought_iron|30|0.5|false",
            "ironIngot3|iron_a|60|0.5|false",
            "ironIngot4|steel|100|0.5|false",
            "ironIngot5|fine_steel|160|0.5|false",
            "ironIngot6|thamaskene_steel|260|0.5|false",
            "trash|iron_ore|1|1|false"
        })
        {
            var fields = row.Split('|');
            string original = fields[0].ToLowerInvariant();
            uint hash = 2166136261;
            unchecked { foreach (char c in original) hash = (hash ^ c) * 16777619; }
            string id = "cs_mirror_" + original + "_" + hash.ToString("x8");
            var matches = catalog.Where(x => (string)x.Attribute("id") == id).ToArray();
            Check(matches.Length == 1, "one preloaded native good: " + original);
            var item = matches[0];
            string category = original.StartsWith("ironingot") ? "iron" :
                original == "charcoal" ? "hardwood" : original == "trash" ? "unassigned" : original;
            Check((string)item.Attribute("item_category") == category,
                "native registered category id: " + original);
            Check((string)item.Attribute("mesh") == fields[1] &&
                (string)item.Attribute("value") == fields[2] &&
                (string)item.Attribute("weight") == fields[3] &&
                (string)item.Attribute("IsFood") == fields[4], "native good properties: " + original);
            Check((string)item.Attribute("Type") == "Goods" &&
                (string)item.Attribute("multiplayer_item") == "true" &&
                (string)item.Element("ItemComponent")?.Element("Trade")?.Attribute("morale_bonus") == "0" &&
                (string)item.Element("Flags")?.Attribute("Civilian") == "true",
                "preloaded trade component: " + original);
        }
        var prefab = XDocument.Load(Path.Combine(root, "Module", "CoopSpectator", "GUI", "Prefabs", "CoopCampaignMenu.xml"));
        string plunderVm = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapMenuVM.cs"));
        Check(plunderVm.Contains("_tooltipHover?.Invoke(_tooltipId, true)") &&
            plunderVm.Contains("_tooltipHover?.Invoke(_tooltipId, false)"), "plunder hover forwards only opaque identity");
        Check(!plunderVm.Contains("InformationManager.ShowTooltip") && !plunderVm.Contains("RefreshItemTooltip"),
            "replica plunder never invokes campaign tooltip provider");
        string plunderMenuView = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapPrototypeMissionView.Menu.cs"));
        Check(plunderMenuView.Contains("_campaignMenuVm?.ClearTooltipHover()") &&
            plunderMenuView.Contains("new CoopCampaignMapMenuVM(OnPlunderTooltipHover)"), "menu suspension clears hover and callback is wired");
        var sliders = prefab.Descendants("SliderWidget").ToArray();
        Check(sliders.Length == 1 && (string)sliders[0].Attribute("Locked") == "false" &&
            (string)sliders[0].Attribute("ValueFloat") == "@Progress", "host progress binding remains writable");
        Check((string)sliders[0].Attribute("IsEnabled") == "false" &&
            (string)sliders[0].Attribute("DoNotAcceptEvents") == "true" &&
            (string)sliders[0].Attribute("UpdateValueOnScroll") == "false",
            "progress rejects local pointer keyboard and scroll input without locking host updates");
        string view = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapPrototypeMissionView.cs"));
        Check(view.Contains("!CanUseReplicaMapInput() || _replicaInfoLayer.HitTest() || IsPointerOverCampaignMenu()") &&
            view.Contains("_replicaInfoLayer.HitTest() || IsPointerOverCampaignMenu())"), "menu shields map picking and camera input");
        var infoPrefab = XDocument.Load(Path.Combine(root, "Module", "CoopSpectator", "GUI", "Prefabs", "CoopCampaignMapReplicaInfo.xml"));
        var followButton = infoPrefab.Descendants("ButtonWidget").Single(x =>
            (string)x.Attribute("Command.Click") == "ExecuteFollowHost");
        var cameraPanel = followButton.Parent.Parent;
        Check((string)cameraPanel.Attribute("HorizontalAlignment") == "Right" &&
            (string)cameraPanel.Attribute("MarginRight") == "20" &&
            (string)cameraPanel.Attribute("VerticalAlignment") == "Bottom",
            "camera controls reserve the left side for campaign menus (layout wiring)");
        string menuVm = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapMenuVM.cs"));
        string encounterVm = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapEncounterVM.cs"));
        Check(encounterVm.Contains("public string EncyclopediaCursorEffect => \"Default\";"),
            "shared encounter portrait supplies a valid non-link cursor, never an empty enum name (source wiring)");
        Check(encounterVm.Contains("CoopConversationHudImageVM(copy.AttackerBanner, true, true)") &&
            encounterVm.Contains("CoopConversationHudImageVM(copy.DefenderBanner, true, true)"),
            "encounter side banners retain native nine-grid presentation (source wiring)");
        foreach (string action in new[] { "ExecuteOpenEncyclopedia", "ExecuteSetAsContextMenuItem" })
            Check(encounterVm.Contains("public void " + action + "() { }"), "encounter action is read-only (source wiring): " + action);
        Check(encounterVm.Contains("_hover?.Invoke(_tooltipId, true)") &&
            encounterVm.Contains("_hover?.Invoke(_tooltipId, false)") &&
            !encounterVm.Contains("InformationManager.ShowTooltip"),
            "encounter hover emits opaque interest, never native campaign provider in MP (source wiring)");
        string menuView = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapPrototypeMissionView.Menu.cs"));
        Check(menuView.Contains("SetInputRestrictions(true, InputUsageMask.Mouse)") &&
            menuView.Contains("IsFocusLayer = false") &&
            !menuView.Contains("InputUsageMask.All"), "menu admits native hover without claiming keyboard focus (source wiring)");
        Check(menuVm.Contains("public void ExecuteAction() { }") &&
            menuVm.Contains("public void ExecuteLink(string link) { }"),
            "mouse admission does not grant campaign command authority (source wiring)");
        Check(menuView.Contains("_campaignMenuLayer.LoadMovie(\"EncounterOverlay\", _encounterVm)") &&
            menuView.Contains("_campaignMenuLayer.ReleaseMovie(_encounterMovie)"),
            "encounter shares menu suspension and releases its movie (source wiring)");
        string capture = File.ReadAllText(Path.Combine(root, "Campaign", "CoopCampaignMapMenuCapture.cs"));
        Check(capture.Contains("captured.Encounter = CaptureEncounter(context, captured.MenuId)") &&
            capture.Contains("?.GetValue(view) as EncounterMenuOverlayVM") &&
            !capture.Contains("new EncounterMenuOverlayVM("), "capture reads existing native overlay (source wiring)");
        Check(menuVm.Contains("CoopCampaignVisualItemResolver.TryResolveImage(objects, _itemId, out var item)"),
            "plunder image uses component-aware preloaded resource admission (source wiring)");
        Console.WriteLine("Menu presentation checks passed: " + _checks);
    }

    private static void TestEncounterSnapshot()
    {
        foreach (var mode in new[] { "field", "village", "siege", "naval" })
        {
            var source = new CoopCampaignMapEncounterPresentation
            {
                TitleText = mode, IsSiege = mode == "siege", IsNaval = mode == "naval",
                AttackerCountLabel = "19", DefenderCountLabel = "70 (3)",
                AttackerPower = 0.25, DefenderPower = 13.5,
                InitialAttackerPower = 1, InitialDefenderPower = 14,
                AttackerFood = "2 days", DefenderMorale = "67", DefenderWallHitPoints = "1200",
                AttackerShipCount = mode == "naval" ? 3 : 0
            };
            source.Attackers.Add(new CoopCampaignMapEncounterParty { NameText = "Host", IsPlayer = true,
                PortraitCode = ConversationHudTests.Portrait(), PortraitPose = 7 });
            source.Defenders.Add(new CoopCampaignMapEncounterParty { NameText = "Defenders", IsEnemy = true });
            source.Attackers[0].QuestMarkers.Add(4);
            Check(source.TrySnapshot(out var copy), mode + " encounter capture valid");
            source.Attackers[0].NameText = "Changed"; source.Attackers[0].QuestMarkers.Clear();
            Check(copy.Attackers[0].NameText == "Host" && copy.Attackers[0].QuestMarkers.Single() == 4,
                mode + " nested party snapshot detached");
            Check(copy.IsSiege == (mode == "siege") && copy.IsNaval == (mode == "naval") &&
                copy.DefenderCountLabel == "70 (3)" && copy.DefenderWallHitPoints == "1200" &&
                copy.AttackerPower == 0.25 && copy.InitialDefenderPower == 14,
                mode + " display values preserved, not recomputed");
            var envelope = new CoopCampaignMapMenuPresentation { MenuId = mode, IsEncounterMenu = true, Encounter = copy };
            envelope.Options.Add(new CoopCampaignMapMenuOption { Text = "Attack" });
            Check(CoopCampaignMapMenuCodec.TrySerialize(29, envelope, out var wire), mode + " encounter envelope serialized");
            Check(CoopCampaignMapMenuCodec.TryDeserialize(wire, out var revision, out var decoded) && revision == 29 &&
                decoded.Options[0].Text == "Attack" && decoded.Encounter.Attackers[0].QuestMarkers.Single() == 4 &&
                decoded.Encounter.Attackers[0].PortraitPose == 7 && decoded.Encounter.Defenders[0].PortraitPose == -1 &&
                decoded.Encounter.DefenderCountLabel == "70 (3)" && decoded.Encounter.IsNaval == copy.IsNaval &&
                decoded.Encounter.IsSiege == copy.IsSiege && decoded.Encounter.AttackerShipCount == copy.AttackerShipCount &&
                decoded.Encounter.DefenderWallHitPoints == copy.DefenderWallHitPoints,
                mode + " menu and encounter roundtrip together");
            var detached = envelope.Clone();
            envelope.Encounter.Attackers[0].NameText = "mutated";
            Check(detached.Encounter.Attackers[0].NameText == "Host", mode + " menu clone detaches encounter");
            for (int length = 0; length < wire.Length; length++)
            {
                var partial = wire.Take(length).ToArray();
                Check(!CoopCampaignMapMenuCodec.TryDeserialize(partial, out revision, out decoded) &&
                    revision == 0 && decoded == null, mode + " truncated encounter cannot publish partial menu");
            }
            var oldVersion = (byte[])wire.Clone(); oldVersion[0] = 0x33;
            Check(!CoopCampaignMapMenuCodec.TryDeserialize(oldVersion, out _, out _), "previous menu format rejected");
            envelope.IsEncounterMenu = false;
            Check(!envelope.IsValid(), "encounter cannot leak into unrelated menu");
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -1, 1e13 })
            {
                source.AttackerPower = invalid;
                Check(!source.TrySnapshot(out var rejected) && rejected == null, mode + " invalid power rejects whole snapshot");
            }
        }
        var bounded = new CoopCampaignMapEncounterPresentation();
        for (int i = 0; i < CoopCampaignMapEncounterPresentation.MaxPartiesPerSide; i++)
            bounded.Attackers.Add(new CoopCampaignMapEncounterParty());
        Check(bounded.IsValid(), "full bounded party list retained");
        bounded.Attackers.Add(new CoopCampaignMapEncounterParty());
        Check(!bounded.IsValid(), "oversized encounter rejected, not truncated");
        bounded.Attackers.Clear(); bounded.Defenders.Add(null);
        Check(!bounded.IsValid(), "null encounter party rejected");
        bounded.Defenders.Clear(); bounded.AttackerBanner = "../file";
        Check(!bounded.IsValid(), "banner cannot be a path");
        bounded.AttackerBanner = ""; bounded.DefenderColor = "invalid";
        Check(!bounded.IsValid(), "invalid encounter color rejected");
        bounded.DefenderColor = "#FFFFFFFF"; bounded.DefenderShipCount = -1;
        Check(!bounded.IsValid(), "negative ships rejected");
        var party = new CoopCampaignMapEncounterParty { PortraitCode = "arbitrary-provider" };
        var poseParty = new CoopCampaignMapEncounterParty { PortraitCode = ConversationHudTests.Portrait() };
        for (int pose = -1; pose <= 7; pose++)
        {
            poseParty.PortraitPose = pose;
            Check(poseParty.IsValid() && poseParty.Clone().PortraitPose == pose, "finite pose set preserved");
        }
        foreach (int invalid in new[] { -2, 8, int.MinValue, int.MaxValue })
        {
            poseParty.PortraitPose = invalid;
            Check(!poseParty.IsValid(), "invalid pose rejected");
        }
        poseParty.PortraitCode = ""; poseParty.PortraitPose = 0;
        Check(!poseParty.IsValid(), "hidden portrait cannot retain a pose");
        Check(!party.IsValid(), "portrait structure validated");
        party.PortraitCode = "";
        foreach (int relation in new[] { -101, -100, 0, 100, 101 })
        { party.Relation = relation; Check(party.IsValid(), "native relation including no-hero/player sentinels"); }
        foreach (int relation in new[] { -102, 102 })
        { party.Relation = relation; Check(!party.IsValid(), "invalid relation rejected"); }
        party.Relation = -101; party.QuestMarkers.Add(999);
        Check(!party.IsValid(), "unsupported quest marker rejected");
    }

    private static bool Accept(CoopCampaignMapMenuAssembler assembler, int revision,
        CoopCampaignMapCatalogChunkedPayload payload, int index, out bool complete,
        out CoopCampaignMapMenuPresentation menu) => assembler.TryAccept(revision,
            payload.LogicalByteCount, payload.WireByteCount, payload.ChunkCount,
            payload.CompressionKind, payload.PayloadHash, index, payload.Chunks[index], out complete, out menu);

    private static void TestChunks(CoopCampaignMapMenuPresentation menu)
    {
        // Deterministic varied text forces multiple compressed packets.
        var random = new Random(81);
        menu.Context = new string(Enumerable.Range(0, 8192).Select(_ => (char)random.Next(32, 127)).ToArray());
        Check(CoopCampaignMapMenuCodec.TrySerialize(10, menu, out var bytes), "chunk fixture encode");
        Check(CoopCampaignMapCatalogChunkCodec.TryEncode(bytes, out var payload, out _) && payload.ChunkCount > 1,
            "multi-packet fixture");
        var assembler = new CoopCampaignMapMenuAssembler();
        bool complete; CoopCampaignMapMenuPresentation decoded;
        for (int index = payload.ChunkCount - 1; index >= 0; index--)
        {
            Check(Accept(assembler, 10, payload, index, out complete, out decoded), "out-of-order packet accepted");
            Check(complete == (index == 0), "no partial publication");
            if (complete) Check(decoded.Context == menu.Context, "assembled exact text");
        }
        Check(!Accept(assembler, 10, payload, 0, out _, out _), "completed replay rejected");
        assembler.Reset();
        Check(Accept(assembler, 10, payload, 0, out complete, out _) && !complete, "partial old menu");
        Check(CoopCampaignMapMenuCodec.TrySerialize(11, null, out var closeBytes), "close encode");
        Check(CoopCampaignMapCatalogChunkCodec.TryEncode(closeBytes, out var close, out _), "close packets");
        for (int i = 0; i < close.ChunkCount; i++)
        {
            Check(Accept(assembler, 11, close, i, out complete, out decoded), "close supersedes old menu");
            if (i == close.ChunkCount - 1) Check(complete && decoded == null, "completed close distinguished from partial");
        }
        Check(!Accept(assembler, 10, payload, 1, out _, out _), "late open cannot undo close");
        assembler.Reset();
        bool accepted = true;
        for (int i = 0; i < payload.ChunkCount; i++) accepted = Accept(assembler, 12, payload, i, out complete, out decoded);
        Check(!accepted, "inner revision must match envelope");
        assembler.Reset();
        var corrupt = (byte[])payload.Chunks[0].Clone(); corrupt[0] ^= 1;
        accepted = assembler.TryAccept(10, payload.LogicalByteCount, payload.WireByteCount,
            payload.ChunkCount, payload.CompressionKind, payload.PayloadHash, 0, corrupt, out complete, out decoded);
        for (int i = 1; i < payload.ChunkCount; i++)
            accepted = Accept(assembler, 10, payload, i, out complete, out decoded);
        Check(!accepted && !complete && decoded == null, "corrupt payload never publishes");
        assembler.Reset();
        Check(!assembler.TryAccept(10, CoopCampaignMapMenuCodec.MaxPayloadBytes + 1,
            payload.WireByteCount, payload.ChunkCount, payload.CompressionKind, payload.PayloadHash,
            0, payload.Chunks[0], out complete, out decoded) && !complete && decoded == null,
            "oversized declaration rejected before accumulation");
        Check(!assembler.TryAccept(10, payload.LogicalByteCount, payload.WireByteCount,
            payload.ChunkCount, payload.CompressionKind, payload.PayloadHash, payload.ChunkCount,
            payload.Chunks[0], out _, out _), "out-of-range index rejected");
        Check(Accept(assembler, 10, payload, 0, out complete, out _) && !complete,
            "valid first packet after rejected envelope");
        Check(!assembler.TryAccept(10, payload.LogicalByteCount, payload.WireByteCount,
            payload.ChunkCount, payload.CompressionKind, new string('0', payload.PayloadHash.Length),
            1, payload.Chunks[1], out _, out _), "same revision cannot change digest");
        for (int i = 1; i < payload.ChunkCount; i++)
            accepted = Accept(assembler, 10, payload, i, out complete, out decoded);
        Check(accepted && complete && decoded.Context == menu.Context,
            "rejected conflicting packet does not poison valid transfer");
    }
}
