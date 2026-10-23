using System;

namespace CoopSpectator.Infrastructure
{
    // Installed 1.4.8 DefaultSiegeEventModel resource admission, not campaign simulation.
    // Capture must supply the actual prepared resource. Never substitute a different one.
    public static class CoopCampaignMapSiegeResources
    {
        private static readonly string[] Ranged = {
            "mangonel_a_mapicon", "mangonel_b_mapicon",
            "mangonel_a_fire_mapicon", "mangonel_b_fire_mapicon",
            "ballista_a_mapicon", "ballista_b_mapicon",
            "ballista_a_fire_mapicon", "ballista_b_fire_mapicon",
            "trebuchet_a_mapicon", "trebuchet_b_mapicon"
        };
        private static readonly string[] Melee = {
            "batteringram_a_mapicon", "siegetower_5m_mapicon",
            "siegetower_9m_mapicon", "siegetower_12m_mapicon"
        };
        private static readonly string[] Projectiles = {
            "mangonel_mapicon_projectile", "mangonel_fire_mapicon_projectile",
            "ballista_mapicon_projectile", "ballista_fire_mapicon_projectile"
        };

        public static bool IsRanged(string prefab) => Contains(Ranged, prefab);
        public static bool IsMachine(string prefab) => IsRanged(prefab) || Contains(Melee, prefab);
        public static bool IsProjectile(string prefab) => Contains(Projectiles, prefab);

        public static bool AcceptsAnimation(string prefab, CoopCampaignMapSiegeAnimation sample)
        {
            if (!IsMachine(prefab)) return false;
            if (!IsRanged(prefab)) return sample == null;
            return sample != null && sample.IsValid() &&
                (sample.ClipName == prefab + "_reload" || sample.ClipName == prefab + "_fire");
        }

        private static bool Contains(string[] values, string value) =>
            Array.IndexOf(values, value) >= 0;
    }
}
