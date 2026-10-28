using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure;

internal static class CraftedVisualResourceTests
{
    private static void Check(bool value, string text) { if (!value) throw new Exception("Crafted visuals: " + text); }
    private static CoopCampaignCraftedVisualWeapon Mace()
    {
        // Actual r60 weapon. This pins compatibility with the existing battle
        // fingerprint rather than proving only a new encoder against itself.
        return new CoopCampaignCraftedVisualWeapon {
            Key = "cw_b2834eadf9bb7620", OriginalId = "crafted_item_0", MirrorId = "cs_crafted_b2834eadf9bb7620",
            Name = "Crafted Two Handed Mace", TemplateId = "TwoHandedMace", CultureId = "empire",
            ModifierGroupId = "", DesignHash = "crafted_item_0",
            Pieces = new List<CoopCampaignCraftedVisualPiece> {
                new CoopCampaignCraftedVisualPiece { Id = "mace_head_35", Type = "Blade", Scale = 106 },
                new CoopCampaignCraftedVisualPiece { Id = "spear_handle_1", Type = "Handle", Scale = 100 },
                new CoopCampaignCraftedVisualPiece { Id = "default_polearm_pommel", Type = "Pommel", Scale = 100 } } };
    }
    private static string Portrait(string item) => "@---@" + string.Concat(Enumerable.Range(0, 12)
        .Select(i => "+" + i + "-" + (i == 0 ? item : "@null") + "-@null")) + "@---@body@---@0@---@0@---@0@---@1@---@2@---@0@---@";
    private static CoopCampaignMapEncounterPresentation Encounter(CoopCampaignCraftedVisualWeapon weapon)
    {
        var frame = new CoopCampaignMapEncounterPresentation();
        frame.Attackers.Add(new CoopCampaignMapEncounterParty { PortraitCode = Portrait(weapon.MirrorId) });
        frame.VisualWeapons.Add(weapon); return frame;
    }
    internal static void Run()
    {
        var weapon = Mace();
        Check(weapon.IsValid() && weapon.CalculateKey() == "cw_b2834eadf9bb7620", "exact installed battle fingerprint retained");
        var encounter = Encounter(weapon);
        var menu = new CoopCampaignMapMenuPresentation { MenuId = "before-battle", IsEncounterMenu = true, Encounter = encounter };
        Check(CoopCampaignMapMenuCodec.TrySerialize(1, menu, out var bytes), "complete dependency encodes");
        Check(CoopCampaignMapMenuCodec.TryDeserialize(bytes, out var revision, out var decoded) && revision == 1 &&
            decoded.Encounter.VisualWeapons[0].SameDefinition(weapon), "portrait and exact complete dependency travel atomically");
        Check(CoopCampaignMapMenuCodec.TrySerialize(1, decoded, out var again) && bytes.SequenceEqual(again), "canonical dependency roundtrip");
        foreach (int length in Enumerable.Range(0, bytes.Length))
            Check(!CoopCampaignMapMenuCodec.TryDeserialize(bytes.Take(length).ToArray(), out _, out _), "truncated dependency rejected");
        byte[] extra = new byte[bytes.Length + 1]; Array.Copy(bytes, extra, bytes.Length);
        Check(!CoopCampaignMapMenuCodec.TryDeserialize(extra, out _, out _), "trailing dependency data rejected");
        encounter.VisualWeapons.Clear();
        Check(!encounter.IsValid(), "crafted alias cannot reference an undeclared resource");
        encounter.VisualWeapons.Add(weapon);
        var unused = Mace(); unused.OriginalId = "crafted_item_9"; unused.Key = unused.CalculateKey(); unused.MirrorId = "cs_crafted_" + unused.Key.Substring(3);
        encounter.VisualWeapons.Add(unused);
        Check(!encounter.IsValid(), "undisclosed extra definition cannot trigger native creation");
        encounter.VisualWeapons.Remove(unused);
        weapon.Pieces[0].Scale++;
        Check(!weapon.IsValid() && !encounter.IsValid(), "modified design with stale fingerprint rejected");
        weapon.Pieces[0].Scale--;
        weapon.Pieces.Add(weapon.Pieces[0].Clone());
        Check(!weapon.IsValid(), "duplicate native piece role rejected"); weapon.Pieces.RemoveAt(3);
        weapon.Pieces[0].Scale = -1; Check(!weapon.IsValid(), "invalid scale rejected"); weapon.Pieces[0].Scale = 106;
        weapon.MirrorId = "arbitrary_registered_item"; Check(!weapon.IsValid(), "arbitrary native object identity rejected");
        weapon.MirrorId = "cs_crafted_b2834eadf9bb7620";

        var gate = new CoopCampaignCraftedVisualPreparation(); int calls = 0;
        Check(!gate.TryPrepare(encounter, _ => { calls++; return true; }, 0) && calls == 0,
            "readiness is not inferred before actual resource preparation");
        Check(!gate.TryPrepare(encounter, _ => { calls++; return false; }, 1) && calls == 1,
            "native unavailable does not acknowledge dependency");
        Check(gate.TryPrepare(encounter, _ => { calls++; return true; }, 1) && calls == 2,
            "cold-join dependency readiness follows explicit successful preparation");
        Check(gate.TryPrepare(encounter, _ => { calls++; return false; }, 1) && calls == 2,
            "repeated frame never recreates already prepared item");
        var changed = weapon.Clone(); changed.Name = changed.Name.ToUpperInvariant();
        Check(changed.IsValid(), "legacy case-insensitive fingerprint remains compatible");
        Check(!gate.TryPrepare(Encounter(changed), _ => { calls++; return true; }, 1) && calls == 2,
            "same key cannot replace a distinct definition still owned by a view");
        changed = weapon.Clone(); changed.Pieces[0].Scale++; changed.Key = changed.CalculateKey(); changed.MirrorId = "cs_crafted_" + changed.Key.Substring(3);
        Check(gate.TryPrepare(Encounter(changed), _ => { calls++; return true; }, 1) && calls == 3,
            "same original ID after save rewind requires its new exact design");
        Check(gate.TryPrepare(null, null, 0), "closed menu has no visual work to adopt");
        var normal = new CoopCampaignMapEncounterPresentation();
        normal.Attackers.Add(new CoopCampaignMapEncounterParty { PortraitCode = Portrait("ordinary_item") });
        Check(gate.TryPrepare(normal, _ => { calls++; return false; }, 0) && calls == 3,
            "ordinary portraits require no crafted-resource work");
        Console.WriteLine("Crafted visual dependencies: actual battle fingerprint, bounded atomic transport and cold-join readiness passed; native preparation pending.");
    }
}
