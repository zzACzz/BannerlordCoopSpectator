using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Campaign
{
    // Current campaign objects only. This capture never creates items or changes
    // a live portrait/hero/equipment. Native item creation belongs to MP mission tick.
    internal static class CoopCampaignCraftedVisualCapture
    {
        private static TaleWorlds.CampaignSystem.Campaign _campaign;
        private static readonly Dictionary<string, Tuple<ItemObject, CoopCampaignCraftedVisualWeapon>> Cache =
            new Dictionary<string, Tuple<ItemObject, CoopCampaignCraftedVisualWeapon>>(StringComparer.Ordinal);
        internal static void Reset() { _campaign = null; Cache.Clear(); }
        internal static bool TryProjectEncounter(CoopCampaignMapEncounterPresentation encounter)
        {
            if (encounter == null || !TryProjectParties(encounter.Attackers.Concat(encounter.Defenders), out var definitions)) return false;
            encounter.VisualWeapons = definitions;
            return encounter.IsValid();
        }
        internal static bool TryProjectParties(IEnumerable<CoopCampaignMapEncounterParty> parties,
            out List<CoopCampaignCraftedVisualWeapon> capturedDefinitions)
        {
            capturedDefinitions = null;
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            if (parties == null || campaign == null) return false;
            if (!ReferenceEquals(_campaign, campaign)) { Reset(); _campaign = campaign; }
            var manager = Game.Current?.ObjectManager ?? MBObjectManager.Instance;
            if (manager == null) return false;
            var definitions = new Dictionary<string, CoopCampaignCraftedVisualWeapon>(StringComparer.Ordinal);
            Func<string, string> resolve = id => {
                var item = manager.GetObject<ItemObject>(id);
                if (!Cache.TryGetValue(id, out var cached) || !ReferenceEquals(cached.Item1, item))
                {
                    var source = BattleDetector.CaptureCraftedVisualDefinition(item);
                    CoopCampaignCraftedVisualWeapon definition = source == null ? null : new CoopCampaignCraftedVisualWeapon {
                        Key = source.Key, OriginalId = source.OriginalItemId, MirrorId = source.MirrorItemId,
                        Name = source.Name ?? "", TemplateId = source.CraftingTemplateId ?? "", CultureId = source.CultureId ?? "",
                        ModifierGroupId = source.ModifierGroupId ?? "", DesignHash = source.WeaponDesignHash ?? "",
                        Pieces = source.Pieces.Select(p => new CoopCampaignCraftedVisualPiece {
                            Id = p.PieceId, Type = p.PieceType, Scale = p.ScalePercentage }).ToList() };
                    if (definition != null && !definition.IsValid()) throw new InvalidOperationException("Invalid visual weapon definition.");
                    cached = Tuple.Create(item, definition); Cache[id] = cached;
                }
                if (cached.Item2 == null) return id;
                definitions[cached.Item2.Key] = cached.Item2;
                return cached.Item2.MirrorId;
            };
            foreach (var party in parties)
            {
                if (party == null) return false;
                if (party.PortraitCode.Length == 0) continue;
                string prepared = CoopConversationHudCodec.ResolvePortraitEquipment(party.PortraitCode, resolve, _ => true);
                if (prepared.Length == 0) return false;
                party.PortraitCode = prepared;
            }
            capturedDefinitions = definitions.Values.OrderBy(d => d.Key, StringComparer.Ordinal).Select(d => d.Clone()).ToList();
            return true;
        }
    }
}
