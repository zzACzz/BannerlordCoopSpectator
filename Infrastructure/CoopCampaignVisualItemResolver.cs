using System;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Infrastructure
{
    // Some original multiplayer IDs are initialized metadata-only items. In
    // particular, r82's northern_round_shield has no ItemComponent despite
    // IsReady/IsInitialized=true. Never pass those objects to AgentVisuals.
    internal static class CoopCampaignVisualItemResolver
    {
        internal static bool TryResolve(MBObjectManager manager, string id, int slot, out ItemObject item)
        {
            return TryResolveCore(manager, id, candidate => IsUsable(candidate, slot), out item);
        }

        internal static bool TryResolveImage(MBObjectManager manager, string id, out ItemObject item)
        {
            return TryResolveCore(manager, id, candidate =>
                candidate != null && candidate.IsInitialized && candidate.IsReady && candidate.ItemComponent != null &&
                (candidate.WeaponComponent == null || candidate.Weapons != null && candidate.Weapons.Count > 0 && candidate.PrimaryWeapon != null) &&
                (candidate.HorseComponent == null || candidate.HorseComponent.Monster != null), out item);
        }

        private static bool TryResolveCore(MBObjectManager manager, string id, Func<ItemObject, bool> usable, out ItemObject item)
        {
            item = null;
            if (manager == null || string.IsNullOrWhiteSpace(id)) return false;
            ItemObject original = Find(manager, id);
            if (usable(original)) { item = original; return true; }
            if (!ExactCampaignRuntimeItemRegistry.TryResolvePreloadedMirrorItem(id, out string mirror, out _))
                return false;
            ItemObject replacement = Find(manager, mirror);
            if (!usable(replacement)) return false;
            item = replacement;
            return true;
        }

        internal static bool IsUsable(ItemObject item, int slot)
        {
            if (item == null || !item.IsInitialized || !item.IsReady || slot < 0 || slot > 11)
                return false;
            if (slot <= 4) return item.Weapons != null && item.Weapons.Count > 0 && item.PrimaryWeapon != null;
            if (slot == 10) return item.HorseComponent?.Monster != null;
            return item.ArmorComponent != null;
        }

        private static ItemObject Find(MBObjectManager manager, string id)
        {
            try { return manager.GetObject<ItemObject>(id); }
            catch { return null; }
        }
    }
}
