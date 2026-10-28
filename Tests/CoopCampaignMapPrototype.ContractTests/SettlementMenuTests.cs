using System;
using System.Linq;
using CoopSpectator.Infrastructure;

internal static class SettlementMenuTests
{
    private static void Check(bool value, string label) { if (!value) throw new Exception("Settlement overlay: " + label); }
    internal static void Run()
    {
        foreach (bool fortification in new[] { false, true })
        {
            var panel = new CoopCampaignSettlementMenuPresentation();
            panel.Labels["SettlementNameLbl"] = fortification ? "Замок" : "Avalyps";
            panel.Labels["MilitasLbl"] = "41"; panel.Labels["ProsperityLbl"] = "346";
            panel.Numbers["ProsperityChangeAmount"] = 1; panel.Numbers["MilitiaChangeAmount"] = -3;
            panel.Flags["IsFortification"] = fortification; panel.Flags["IsInitializationOver"] = true;
            panel.Characters.Add(new CoopCampaignMapEncounterParty { NameText = "Losys" });
            panel.Characters.Add(new CoopCampaignMapEncounterParty { NameText = "Joron" });
            panel.Parties.Add(new CoopCampaignMapEncounterParty { NameText = "Guest party", PartySizeLabel = "29" });
            var menu = new CoopCampaignMapMenuPresentation { MenuId = "settlement", SettlementOverlay = panel };
            Check(menu.IsValid() && CoopCampaignMapMenuCodec.TrySerialize(1, menu, out _), "valid village/fortification panel");
            CoopCampaignMapMenuCodec.TrySerialize(1, menu, out var bytes);
            Check(CoopCampaignMapMenuCodec.TryDeserialize(bytes, out _, out var decoded) &&
                decoded.SettlementOverlay.Labels["SettlementNameLbl"] == panel.Labels["SettlementNameLbl"] &&
                decoded.SettlementOverlay.Numbers["MilitiaChangeAmount"] == -3 &&
                decoded.SettlementOverlay.Characters.Select(p => p.NameText).SequenceEqual(new[] { "Losys", "Joron" }) &&
                decoded.SettlementOverlay.Parties[0].PartySizeLabel == "29", "labels/counts/deltas/order travel atomically");
            foreach (int length in Enumerable.Range(0, bytes.Length))
                Check(!CoopCampaignMapMenuCodec.TryDeserialize(bytes.Take(length).ToArray(), out _, out _), "truncated overlay rejected");
            Check(!CoopCampaignMapMenuCodec.TryDeserialize(bytes.Concat(new byte[] { 0 }).ToArray(), out _, out _), "trailing payload rejected");
            var copy = menu.Clone(); panel.Labels["MilitasLbl"] = "42"; panel.Characters[0].NameText = "Changed";
            Check(copy.SettlementOverlay.Labels["MilitasLbl"] == "41" && copy.SettlementOverlay.Characters[0].NameText == "Losys", "owned deep snapshot");
            menu.IsEncounterMenu = true; Check(!menu.IsValid(), "settlement cannot impersonate encounter"); menu.IsEncounterMenu = false;
            panel.Labels.Add("UntrustedProperty", "injected"); Check(!menu.IsValid(), "unknown binding rejected"); panel.Labels.Remove("UntrustedProperty");
            panel.Flags.Remove("IsCrimeEnabled"); Check(!menu.IsValid(), "missing visibility binding rejected"); panel.Flags["IsCrimeEnabled"] = false;
            panel.Characters[0].TooltipId = "encounter:" + new string('a', 32);
            Check(!menu.IsValid(), "settlement cannot reuse unrelated encounter hover capability"); panel.Characters[0].TooltipId = "";
            panel.Numbers["WallsLevel"] = -1; Check(!menu.IsValid(), "invalid wall visual state rejected"); panel.Numbers["WallsLevel"] = 0;
            panel.Characters.Clear(); panel.Parties.Clear();
            Check(menu.IsValid(), "empty settlement lists explicitly clear old portraits");
            CoopCampaignMapMenuCodec.TrySerialize(2, new CoopCampaignMapMenuPresentation { MenuId = "left-settlement" }, out var left);
            Check(CoopCampaignMapMenuCodec.TryDeserialize(left, out _, out var closed) && closed.SettlementOverlay == null,
                "leaving settlement revokes panel");
        }
        ValidateHints();
        Console.WriteLine("Settlement menu bounded presentation assertions passed.");
    }
    private static void ValidateHints()
    {
        string session = Guid.NewGuid().ToString("N");
        var context = new object(); var overlay = new object(); var native = new object();
        var identity = new CoopCampaignSettlementHintIdentity();
        var sources = new System.Collections.Generic.Dictionary<string, object> { ["MilitasHint"] = native, ["Character_0"] = new object() };
        Check(identity.TryUpdate(context, overlay, session, "village", sources, out var tokens), "hint owner lease");
        string token = tokens["MilitasHint"], portrait = tokens["Character_0"];
        Check(identity.TryUpdate(context, overlay, session, "village", sources, out var same) && same["MilitasHint"] == token,
            "stable source retains capability during number updates");
        var panel = new CoopCampaignSettlementMenuPresentation(); panel.Flags["IsInitializationOver"] = true;
        panel.Characters.Add(new CoopCampaignMapEncounterParty { NameText = "Notable" }); panel.HintTokens = tokens;
        var menu = new CoopCampaignMapMenuPresentation { MenuId = "village", SettlementOverlay = panel };
        var request = new CoopCampaignMapTooltipRequest(session, Guid.NewGuid(), token, 1, false);
        Check(CoopCampaignMapTooltipTargetAdmission.IsSettlementHintAdmitted(session, request, menu), "published current hint admitted");
        Check(!CoopCampaignMapTooltipTargetAdmission.IsSettlementHintAdmitted(Guid.NewGuid().ToString("N"), request, menu), "session mismatch rejected");
        var unleased = new CoopCampaignMapTooltipRequest(session, Guid.Empty, token, 1, false);
        Check(!CoopCampaignMapTooltipTargetAdmission.IsSettlementHintAdmitted(session, unleased, menu), "no peer lease rejected");
        Check(CoopCampaignMapMenuCodec.TrySerialize(1, menu, out var bytes) && CoopCampaignMapMenuCodec.TryDeserialize(bytes, out _, out var decoded) &&
            decoded.SettlementOverlay.HintTokens["Character_0"] == portrait, "ordered hint namespace travels with exact panel");
        var snapshot = panel.Clone(); panel.HintTokens.Remove("MilitasHint");
        Check(snapshot.HintTokens.ContainsKey("MilitasHint") && !CoopCampaignMapTooltipTargetAdmission.ContainsSettlementHint(menu, token), "owned snapshot and publication revocation");
        panel.HintTokens["MilitasHint"] = portrait; Check(!panel.IsValid(), "duplicate token across row/stat rejected"); panel.HintTokens.Remove("MilitasHint");
        panel.HintTokens["Character_01"] = token; Check(!panel.IsValid(), "noncanonical slot rejected"); panel.HintTokens.Remove("Character_01");
        panel.HintTokens["Character_1"] = token; Check(!panel.IsValid(), "out of disclosed list rejected"); panel.HintTokens.Remove("Character_1");
        panel.HintTokens["WallsHint"] = token; Check(!panel.IsValid(), "village cannot request hidden walls"); panel.HintTokens.Remove("WallsHint");
        panel.Flags["IsInitializationOver"] = false; Check(!panel.IsValid(), "uninitialized panel cannot disclose interests"); panel.Flags["IsInitializationOver"] = true;
        panel.HintTokens["Character_0"] = "encounter:" + new string('a',32); Check(!panel.IsValid(), "encounter capability cannot impersonate settlement UI");
        Check(identity.TryResolve(context, overlay, session, "village", token, out var key, out var resolved) && key == "MilitasHint" && ReferenceEquals(resolved,native), "exact source resolve");
        sources["MilitasHint"] = new object(); identity.TryUpdate(context,overlay,session,"village",sources,out _);
        Check(!identity.TryResolve(context,overlay,session,"village",token,out _,out _), "replaced provider revokes old interest");
        foreach (string change in new[] { "context", "overlay", "session", "menu" })
        {
            identity.TryUpdate(context,overlay,session,"village",sources,out tokens); token=tokens["MilitasHint"];
            identity.TryUpdate(change=="context"?new object():context,change=="overlay"?new object():overlay,
                change=="session"?Guid.NewGuid().ToString("N"):session,change=="menu"?"town":"village",sources,out _);
            Check(!identity.TryResolve(context,overlay,session,"village",token,out _,out _), change+" revokes old owner");
        }
        identity.TryUpdate(context,overlay,session,"village",sources,out tokens); token=tokens["MilitasHint"];
        sources.Clear(); identity.TryUpdate(context,overlay,session,"village",sources,out _);
        Check(!identity.TryResolve(context,overlay,session,"village",token,out _,out _), "hidden/removed provider revoked");
        sources["MilitasHint"]=null;
        Check(!identity.TryUpdate(context,overlay,session,"village",sources,out _), "invalid capture clears owner");
        foreach (var kind in new[] { CoopCampaignMapTooltipKind.SettlementInfo, CoopCampaignMapTooltipKind.Hero, CoopCampaignMapTooltipKind.SettlementControl })
        {
            var payload = new CoopCampaignMapTooltipPayload { SessionId=session, EntityId="settlementui:"+Guid.NewGuid().ToString("N"),
                RequestGeneration=1,Sequence=1,Kind=kind,Mode=kind==CoopCampaignMapTooltipKind.Hero?2:0 };
            if (kind==CoopCampaignMapTooltipKind.SettlementControl) payload.ControlHint=new CoopCampaignMapControlHint { Text="Walls: 2" };
            else payload.Rows.Add(new CoopCampaignMapTooltipRow { Definition="Militia",Value="41" });
            Check(CoopCampaignMapTooltipPayloadCodec.TrySerialize(payload,out bytes) && CoopCampaignMapTooltipPayloadCodec.TryDeserialize(bytes,out var copy) &&
                copy.Kind==kind && copy.Mode==payload.Mode, "settlement native hint/provider roundtrip "+kind);
            payload.EntityId="barhint:"+Guid.NewGuid().ToString("N"); Check(!CoopCampaignMapTooltipPayloadCodec.IsValid(payload), "provider namespace fenced "+kind);
        }
    }
}
