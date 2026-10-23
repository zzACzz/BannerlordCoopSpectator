using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using CoopSpectator.Patches;
using TaleWorlds.ObjectSystem;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Tableaus.Thumbnails;

internal static class MirrorItemPoseTests
{
    internal static void Run()
    {
        CoopMirrorItemPosePatch.Apply();
        var cache = new ItemThumbnailCache();
        foreach (string id in new[] { "iron", "hardwood", "charcoal", "ironIngot1", "ironIngot2", "ironIngot3", "ironIngot4", "ironIngot5", "ironIngot6" })
        {
            string mirror = "cs_mirror_" + id;
            CoopSpectator.Infrastructure.ExactCampaignRuntimeItemRegistry.Ids[mirror] = id;
            var item = new MBObjectBase { StringId = mirror };
            GameNetwork.IsMultiplayer = true;
            if (cache.Select(item) != "craftmat" || item.StringId != mirror) throw new Exception("Mirror pose or identity: " + id);
            GameNetwork.IsMultiplayer = false;
            if (cache.Select(item) != "goods") throw new Exception("Campaign affected");
        }
        GameNetwork.IsMultiplayer = true;
        foreach (string id in new[] { "unknown", "cs_mirror_unknown", "dragon_banner_center", "hardwood" })
        {
            string expected = id == "hardwood" ? "craftmat" : id == "dragon_banner_center" ? "dragon" : "goods";
            if (cache.Select(new MBObjectBase { StringId = id }) != expected) throw new Exception("Ordinary item changed");
        }
        CoopSpectator.Infrastructure.ExactCampaignRuntimeItemRegistry.Ids["cs_mirror_dragon"] = "dragon_banner_center";
        if (cache.Select(new MBObjectBase { StringId = "cs_mirror_dragon" }) != "goods") throw new Exception("Non-material admitted");
        var transpile = typeof(CoopMirrorItemPosePatch).GetMethod("Transpile", BindingFlags.NonPublic | BindingFlags.Static);
        bool rejected = false;
        try { transpile.Invoke(null, new object[] { new[] { new CodeInstruction(OpCodes.Ret) } }); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { rejected = true; }
        if (!rejected) throw new Exception("Changed native shape accepted");
        GameNetwork.IsMultiplayer = false;
        Console.WriteLine("Mirror pose: nine materials, identity preservation, campaign isolation, ordinary IDs and changed-shape rejection passed (substitute native method).");
    }
}
namespace TaleWorlds.ObjectSystem
{
    public class MBObjectBase { public string StringId { get; set; } }
}
namespace CoopSpectator.Infrastructure
{
    public static class ExactCampaignRuntimeItemRegistry
    {
        public static readonly Dictionary<string, string> Ids = new Dictionary<string, string>();
        public static bool TryResolveOriginalItemIdByMirrorId(string id, out string original) => Ids.TryGetValue(id, out original);
    }
}
namespace TaleWorlds.MountAndBlade.View.Tableaus.Thumbnails
{
    public class ItemThumbnailCache
    {
        public string Select(MBObjectBase item) => GetItemPoseAndCamera(item);
        [MethodImpl(MethodImplOptions.NoInlining)]
        private string GetItemPoseAndCamera(MBObjectBase item)
        {
            if (item.StringId == "iron" || item.StringId == "hardwood" || item.StringId == "charcoal" ||
                item.StringId == "ironIngot1" || item.StringId == "ironIngot2" || item.StringId == "ironIngot3" ||
                item.StringId == "ironIngot4" || item.StringId == "ironIngot5" || item.StringId == "ironIngot6") return "craftmat";
            return item.StringId == "dragon_banner_center" ? "dragon" : "goods";
        }
    }
}
