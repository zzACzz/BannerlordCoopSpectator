using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Infrastructure
{
    // Owned by one client map mission. No registered item is created/replaced.
    internal sealed class CoopDetachedPortraitResources
    {
        private readonly CoopCampaignCraftedVisualPreparation _preparation = new CoopCampaignCraftedVisualPreparation();
        private readonly Dictionary<string, ItemObject> _items = new Dictionary<string, ItemObject>(StringComparer.Ordinal);
        internal bool IsReady(CoopCampaignMapEncounterPresentation encounter) => _preparation.TryPrepare(encounter, null, 0);
        internal void Prepare(CoopCampaignMapEncounterPresentation encounter)
        {
            if (encounter == null || !encounter.IsValid()) return;
            Prepare(encounter.VisualWeapons, encounter.Attackers.Concat(encounter.Defenders).Select(p => p.PortraitCode));
        }
        internal bool IsReady(CoopCampaignSettlementMenuPresentation state) => state == null ||
            state.IsValid() && _preparation.TryPrepare(state.VisualWeapons, state.Characters.Concat(state.Parties).Select(p => p.PortraitCode), null, 0);
        internal void Prepare(CoopCampaignSettlementMenuPresentation state)
        {
            if (state == null || !state.IsValid()) return;
            Prepare(state.VisualWeapons, state.Characters.Concat(state.Parties).Select(p => p.PortraitCode));
        }
        private void Prepare(IReadOnlyList<CoopCampaignCraftedVisualWeapon> definitions, IEnumerable<string> portraits)
        {
            _preparation.TryPrepare(definitions, portraits, definition =>
            {
                if (_items.Count >= CoopCampaignCraftedVisualWeapon.MaximumDefinitions ||
                    !ExactCampaignRuntimeItemRegistry.TryCreateDetachedPortraitWeapon(definition, out var item)) return false;
                _items.Add(definition.MirrorId, item);
                return true;
            }, 4);
        }

        internal bool TryBuild(string portrait, out Equipment equipment)
        {
            equipment = null;
            if (!CoopConversationHudCodec.IsPortraitCode(portrait) || string.IsNullOrEmpty(portrait)) return false;
            var manager = Game.Current?.ObjectManager ?? MBObjectManager.Instance;
            if (manager == null) return false;
            var fields = portrait.Split(new[] { "@---@" }, StringSplitOptions.None);
            var slots = fields[1].Split('+');
            var owned = new Equipment();
            for (int i = 0; i < 12; i++)
            {
                var parts = slots[i + 1].Split('-');
                if (parts[1] == "@null") continue;
                ItemObject item;
                if (parts[1].StartsWith("cs_crafted_", StringComparison.Ordinal))
                {
                    // Never fall back to a same-name registered item from an old save.
                    if (!_items.TryGetValue(parts[1], out item) || item.Id.InternalValue != 0) return false;
                }
                else
                {
                    if (!CoopCampaignVisualItemResolver.TryResolve(manager, parts[1], i, out item)) return false;
                }
                var modifier = parts[2] == "@null" ? null : manager.GetObject<ItemModifier>(parts[2]);
                if (parts[2] != "@null" && modifier == null) return false;
                owned[i] = new EquipmentElement(item, modifier);
            }
            equipment = owned; return true;
        }
    }
}
