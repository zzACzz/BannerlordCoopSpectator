using System;
using CoopSpectator.Infrastructure;

internal static class SiegeAnimationTests
{
    internal static void Run()
    {
        var sample = new CoopCampaignMapSiegeAnimation { ClipName = "siege_test_clip", Progress = 1, Speed = 0 };
        Check(sample.TrySnapshot(out var copy), "Completed paused clip is valid");
        copy.ClipName = "changed"; copy.Progress = 0; copy.Speed = 2;
        Check(sample.ClipName == "siege_test_clip" && sample.Progress == 1 && sample.Speed == 0,
            "Snapshot owns values");
        foreach (var name in new[] { null, "", "../clip", "clip/path", "clip\\path", "clip\0", "clip name", "clip\n", new string('a', 129) })
        {
            sample.ClipName = name;
            Check(!sample.TrySnapshot(out copy) && copy == null, "Invalid name fails atomically");
        }
        sample.ClipName = new string('a', 128);
        Check(sample.IsValid(), "Maximum name admitted");
        foreach (float phase in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -0.01f, 1.01f })
        {
            sample.Progress = phase;
            Check(!sample.IsValid(), "Invalid phase rejected");
        }
        sample.Progress = 0;
        foreach (float speed in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -0.01f, 16.01f })
        {
            sample.Speed = speed;
            Check(!sample.IsValid(), "Invalid speed rejected");
        }
        sample.Speed = 16;
        Check(sample.IsValid(), "Boundary phase and speed admitted");
        foreach (string prefab in new[] { "mangonel_a_mapicon", "mangonel_b_mapicon",
            "mangonel_a_fire_mapicon", "mangonel_b_fire_mapicon", "ballista_a_mapicon",
            "ballista_b_mapicon", "ballista_a_fire_mapicon", "ballista_b_fire_mapicon",
            "trebuchet_a_mapicon", "trebuchet_b_mapicon" })
        {
            Check(CoopCampaignMapSiegeResources.IsRanged(prefab), "Known ranged resource");
            foreach (string suffix in new[] { "_reload", "_fire" })
            {
                sample.ClipName = prefab + suffix;
                Check(CoopCampaignMapSiegeResources.AcceptsAnimation(prefab, sample), "Matching clip admitted");
            }
            sample.ClipName = "unrelated_clip";
            Check(!CoopCampaignMapSiegeResources.AcceptsAnimation(prefab, sample), "Unrelated clip rejected");
            Check(!CoopCampaignMapSiegeResources.AcceptsAnimation(prefab, null), "Ranged pose required");
        }
        foreach (string prefab in new[] { "batteringram_a_mapicon", "siegetower_5m_mapicon",
            "siegetower_9m_mapicon", "siegetower_12m_mapicon" })
            Check(CoopCampaignMapSiegeResources.AcceptsAnimation(prefab, null) &&
                !CoopCampaignMapSiegeResources.AcceptsAnimation(prefab, sample), "Melee has no ranged clip");
        foreach (string prefab in new[] { "mangonel_mapicon_projectile", "mangonel_fire_mapicon_projectile",
            "ballista_mapicon_projectile", "ballista_fire_mapicon_projectile" })
            Check(CoopCampaignMapSiegeResources.IsProjectile(prefab) &&
                !CoopCampaignMapSiegeResources.IsMachine(prefab), "Projectile not machine");
        foreach (string prefab in new[] { null, "", "../ballista_a_mapicon", "BALLISTA_A_MAPICON", "custom_engine" })
            Check(!CoopCampaignMapSiegeResources.IsMachine(prefab) &&
                !CoopCampaignMapSiegeResources.IsProjectile(prefab) &&
                !CoopCampaignMapSiegeResources.AcceptsAnimation(prefab, null), "Unknown resource fails closed");
        Console.WriteLine("Siege animation sample checks passed (validation only, no native acceptance).");
    }

    private static void Check(bool value, string message)
    { if (!value) throw new Exception("Siege animation: " + message); }
}
