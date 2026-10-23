using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CoopSpectator.Infrastructure;

internal static class ConversationHudTests
{
    private static int _count;
    internal static void Run()
    {
        var hud = Fixture();
        Check(CoopConversationHudCodec.IsValid(hud), "valid host projection");
        Check(CoopConversationHudCodec.TrySerialize(hud, out var bytes) &&
            CoopConversationHudCodec.TryDeserialize(bytes, out var copy) && CoopConversationHudCodec.Equivalent(hud, copy), "HUD round trip");
        var clone = hud.Clone(); clone.AttackerLeader.QuestMarkers[0] = 8;
        Check(hud.AttackerLeader.QuestMarkers[0] == 1 && !CoopConversationHudCodec.Equivalent(hud, clone), "deep clone markers");
        clone = hud.Clone(); clone.DefenderParties[0].HealthyAmount++;
        Check(!CoopConversationHudCodec.Equivalent(hud, clone), "count change detected");
        clone = hud.Clone(); clone.AttackerPower = 0; clone.DefenderPower = 1;
        clone.InitialAttackerPower = 0; clone.InitialDefenderPower = 1;
        Check(CoopConversationHudCodec.TrySerialize(clone, out var hidden) &&
            CoopConversationHudCodec.TryDeserialize(hidden, out var hiddenCopy) && hiddenCopy.AttackerPower == 0 && hiddenCopy.DefenderPower == 1,
            "native hidden strength retained exactly");
        clone = hud.Clone(); clone.AttackerLeader.PortraitCode = string.Empty;
        Check(CoopConversationHudCodec.IsValid(clone), "hidden portrait remains empty");
        clone = hud.Clone(); clone.IsAggressive = false;
        Check(!CoopConversationHudCodec.IsValid(clone), "hidden stale aggressive fields rejected");
        Check(CoopConversationHudCodec.IsValid(new CoopCampaignMapConversationHud()), "empty presentation");
        Check(CoopConversationHudCodec.TrySerialize(null, out var empty) &&
            CoopConversationHudCodec.TryDeserialize(empty, out var emptyCopy) && emptyCopy == null, "optional HUD unavailable");
        clone = hud.Clone(); clone.IsBannerEnabled = false;
        Check(!CoopConversationHudCodec.IsValid(clone), "hidden stale banner rejected");
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d, 1e13 })
        {
            clone = hud.Clone(); clone.AttackerPower = bad;
            Check(!CoopConversationHudCodec.IsValid(clone), "unsafe power rejected");
        }
        clone = hud.Clone(); clone.AttackerColor = "../../texture";
        Check(!CoopConversationHudCodec.IsValid(clone), "color format");
        clone = hud.Clone(); clone.AttackerLeader.QuestMarkers.Add(3);
        Check(!CoopConversationHudCodec.IsValid(clone), "unknown marker");
        clone = hud.Clone(); clone.AttackerLeader.HealthyAmount = -1;
        Check(!CoopConversationHudCodec.IsValid(clone), "negative party count");
        clone = hud.Clone(); clone.AttackerLeader.PortraitCode = "hero_campaign_id";
        Check(!CoopConversationHudCodec.IsValid(clone), "no campaign lookup id");
        clone = hud.Clone(); clone.AttackerLeader.PortraitCode = Portrait().Replace("+0-", "+12-");
        Check(!CoopConversationHudCodec.IsValid(clone), "equipment slot overrun rejected");
        clone = hud.Clone(); clone.AttackerLeader.PortraitCode = Portrait().Replace("+1-", "+0-");
        Check(!CoopConversationHudCodec.IsValid(clone), "duplicate slot rejected");
        string portrait = Portrait().Replace("+0-@null-@null", "+0-campaign_sword-fine");
        Check(CoopConversationHudCodec.ResolvePortraitEquipment(portrait, item => item, modifier => modifier == "fine") == portrait,
            "existing item retains exact portrait");
        Check(CoopConversationHudCodec.ResolvePortraitEquipment(portrait, item => "cs_mirror_" + item, modifier => true) ==
            portrait.Replace("+0-campaign_sword-", "+0-cs_mirror_campaign_sword-"), "mirror changes only equipment id");
        Check(CoopConversationHudCodec.ResolvePortraitEquipment(portrait, item => null, modifier => true) == string.Empty,
            "unavailable item never yields partial portrait");
        Check(CoopConversationHudCodec.ResolvePortraitEquipment(portrait, item => item, modifier => false) == string.Empty,
            "unavailable modifier preserves safe fallback");
        Check(CoopConversationHudCodec.ResolvePortraitEquipment(portrait, item => "../invalid", modifier => true) == string.Empty,
            "invalid local resolution rejected");
        Check(CoopConversationHudCodec.ResolvePortraitEquipment(string.Empty, item => throw new Exception(), modifier => false) == string.Empty,
            "hidden portrait performs no lookup");
        for (int i = 0; i < bytes.Length; i++)
            Check(!CoopConversationHudCodec.TryDeserialize(bytes.Take(i).ToArray(), out _), "truncated " + i);
        Check(!CoopConversationHudCodec.TryDeserialize(bytes.Concat(new byte[] { 0 }).ToArray(), out _), "trailing bytes");
        var malformed = (byte[])bytes.Clone(); malformed[4] = 2;
        Check(!CoopConversationHudCodec.TryDeserialize(malformed, out _), "canonical bool");
        var random = new Random(211);
        for (int i = 0; i < 1000; i++)
        {
            malformed = (byte[])bytes.Clone(); malformed[random.Next(malformed.Length)] = (byte)random.Next(256);
            bool accepted = CoopConversationHudCodec.TryDeserialize(malformed, out var fuzz);
            Check(!accepted || CoopConversationHudCodec.IsValid(fuzz), "decoder never throws or emits invalid state");
        }
        var state = new CoopCampaignMapConversationState { IsActive = true, Revision = 2, DialogText = "Visible", Hud = hud };
        Check(CoopCampaignMapConversationPayloadCodec.TrySerialize(state, out var full) &&
            CoopCampaignMapConversationPayloadCodec.TryDeserialize(full, out var decoded) &&
            CoopCampaignMapPrototypeContract.AreConversationContentsEquivalent(state, decoded), "atomic HUD/text payload");
        malformed = (byte[])full.Clone(); malformed[0] = 0x31;
        Check(!CoopCampaignMapConversationPayloadCodec.TryDeserialize(malformed, out _), "old payload magic rejected");
        var stateCopy = state.Clone(); stateCopy.Hud.AttackerPower++;
        Check(!CoopCampaignMapPrototypeContract.AreConversationContentsEquivalent(state, stateCopy), "HUD-only revision");
        var close = CoopCampaignMapPrototypeContract.CreateInactiveConversation(3); close.Hud = hud;
        Check(!CoopCampaignMapPrototypeContract.TryValidateConversationState(close, out _), "close cannot retain HUD");
        var host = new CoopCampaignMapPrototypeHostSnapshot
        {
            SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
            SessionId = "0123456789abcdef0123456789abcdef", Revision = 1, IsActive = true,
            UpdatedUtc = DateTime.UtcNow, Conversation = state
        };
        var lines = CoopCampaignMapPrototypeBridgeCodec.Serialize(host);
        Check(CoopCampaignMapPrototypeBridgeCodec.TryParse(lines, out var bridge, out _) &&
            CoopConversationHudCodec.Equivalent(hud, bridge.Conversation.Hud), "bridge HUD round trip");
        Check(!CoopCampaignMapPrototypeBridgeCodec.TryParse(lines.Where(l => !l.StartsWith("ConversationHud=")).ToArray(), out _, out _), "required HUD field");
        Check(!CoopCampaignMapPrototypeBridgeCodec.TryParse(lines.Select(l => l.StartsWith("ConversationHud=") ? "ConversationHud=!" : l).ToArray(), out _, out _), "corrupt HUD field");
        clone = hud.Clone();
        while (clone.AttackerParties.Count < CoopConversationHudCodec.MaxPartiesPerSide) clone.AttackerParties.Add(hud.AttackerLeader.Clone());
        Check(CoopConversationHudCodec.TrySerialize(clone, out _), "maximum party list");
        clone.AttackerParties.Add(hud.AttackerLeader.Clone());
        Check(!CoopConversationHudCodec.IsValid(clone), "party allocation cap");
        Wiring();
        Console.WriteLine("Conversation HUD assertions passed: " + _count);
    }
    internal static string Portrait() => "@---@" + string.Concat(Enumerable.Range(0, 12).Select(i => "+" + i + "-@null-@null")) +
        "@---@<BodyProperties version=\"4\" age=\"30\" weight=\"0.5\" build=\"0.5\" key=\"0000000000000000000000000000000000000000000000000000000000000000\" />@---@0@---@1@---@0@---@4294967295@---@4294967295@---@0@---@";
    private static CoopCampaignMapConversationHud Fixture()
    {
        var hud = new CoopCampaignMapConversationHud
        {
            IsAggressive = true, AttackerLeader = new CoopConversationHudParty { PortraitCode = Portrait(), HealthyAmount = 29 },
            DefenderLeader = new CoopConversationHudParty { HealthyAmount = 11 },
            AttackerPower = 150.125, DefenderPower = 22.75, InitialAttackerPower = 150.125, InitialDefenderPower = 22.75,
            AttackerColor = "#123456FF", DefenderColor = "#FEDCBAFF", IsBannerEnabled = true,
            BannerCode = "11.2.3.4.5", FactionHint = "Clan visible to host", BannerNineGrid = true
        };
        hud.AttackerLeader.QuestMarkers.Add(1);
        hud.DefenderParties.Add(new CoopConversationHudParty { HealthyAmount = 4 });
        return hud;
    }
    private static void Wiring()
    {
        string root = Environment.GetEnvironmentVariable("COOPSPECTATOR_REPOSITORY_ROOT");
        string capture = File.ReadAllText(Path.Combine(root, "Campaign", "CoopCampaignMapConversationCapture.cs"));
        string vm = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapConversationHudVM.cs"));
        Check(capture.Contains("dialog.IsAggressive") && capture.Contains("power.AttackerBattlePowerValue") &&
            !capture.Contains("CalculateCurrentStrength") && !capture.Contains("GetQuestsRelatedToParty"), "host presentation authority");
        Check(!vm.Contains("Campaign.Current") && !vm.Contains("MobileParty") && !vm.Contains("SendMessage") &&
            vm.Contains("ExecuteShowPartyTooltip() { }") && vm.Contains("Quests.Clear()"), "detached read-only finalized UI");
        Check(vm.Contains("CoopDebugConfig.VerboseDiagnostics &&") &&
            vm.Contains("Interlocked.Increment(ref _portraitFailureDiagnostics) <= 32") &&
            !vm.Contains("ex.Message"), "portrait failure diagnostics opt-in bounded without sensitive payloads");
        var items = XDocument.Load(Path.Combine(root, "Module", "CoopSpectator", "ModuleData", "coopspectator_campaign_mirror_items.xml")).Root.Elements().ToArray();
        const string dragonId = "cs_mirror_dragon_banner_a60398b5";
        var dragon = items.Single(e => (string)e.Attribute("id") == dragonId);
        Check(items.Select(e => (string)e.Attribute("id")).Distinct(StringComparer.Ordinal).Count() == items.Length,
            "mirror catalog has unique identifiers");
        Check(items.SkipWhile(e => e != dragon).Skip(1).All(e => (string)e.Attribute("Type") == "Goods") && (string)dragon.Attribute("multiplayer_item") == "true" &&
            (string)dragon.Attribute("Type") == "Banner" && (string)dragon.Attribute("mesh") == "dragon_banner_weapon" &&
            (string)dragon.Attribute("prefab") == "dragon_banner_weapon", "append-only exact StoryMode banner resource");
        Check((string)dragon.Element("ItemComponent").Element("Banner").Attribute("effect") == "IncreasedMeleeDamageAgainstMountedTroops" &&
            (string)dragon.Element("Flags").Attribute("CanBePickedUpFromCorpse") == "false", "banner semantics retained");
        string bannerPortrait = Portrait().Replace("+4-@null-@null", "+4-dragon_banner-@null");
        Check(CoopConversationHudCodec.ResolvePortraitEquipment(bannerPortrait,
            item => item == "dragon_banner" ? dragonId : null, modifier => true) ==
            bannerPortrait.Replace("+4-dragon_banner-", "+4-" + dragonId + "-"), "banner slot retained in complete portrait");
        Check(CoopConversationHudCodec.ResolvePortraitEquipment(bannerPortrait, item => null, modifier => true) == string.Empty,
            "missing banner still rejects complete portrait safely");
    }
    private static void Check(bool ok, string label)
    { _count++; if (!ok) throw new InvalidOperationException("Conversation HUD: " + label); }
}
