using System;
using System.Collections.Generic;
using System.IO;
using CoopSpectator.Infrastructure;

internal static class ContentOwnerReadinessTests
{
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "WarhostMapOwner-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Native registry FolderPath can differ from game/Modules/Id.
            var baseMap = new KeyValuePair<string, string>("Sandbox", Path.Combine(root, "Game with spaces", "Sandbox"));
            var naval = new KeyValuePair<string, string>("NavalDLC", Path.Combine(root, "Platform DLC elsewhere"));
            var adapter = new KeyValuePair<string, string>(CoopCampaignContentProfile.NavalAdapterId, Path.Combine(root, "Private adapter"));
            var foreign = new KeyValuePair<string, string>("Overhaul", Path.Combine(root, "Another map"));
            MakeMap(baseMap.Value); MakeMap(naval.Value); MakeMap(adapter.Value); MakeMap(foreign.Value);
            var registry = ModulePathHelper.ReadActiveModulePaths(new[] {
                new ModuleMetadata { Id="Sandbox", FolderPath=baseMap.Value, IsActive=true },
                new ModuleMetadata { Id="NavalDLC", FolderPath=naval.Value, IsActive=true },
                new ModuleMetadata { Id="Overhaul", FolderPath=foreign.Value, IsActive=false } });
            Check(registry.Count == 2 && CoopCampaignMapResourceOwner.Resolve(registry) == "NavalDLC",
                "public registry metadata preserves order/real folders and skips inactive overrides");
            ExpectNotReady(() => ModulePathHelper.ReadActiveModulePaths(new object[] { new object() }));
            Check(CoopCampaignMapResourceOwner.Resolve(new[] { baseMap }) == "Sandbox", "base map owner");
            Check(CoopCampaignMapResourceOwner.Resolve(new[] { baseMap, naval }) == "NavalDLC", "official DLC owns the map by actual folder");
            Check(CoopCampaignMapResourceOwner.Resolve(new[] { baseMap, adapter }) == adapter.Key, "client resource adapter owns the map");
            Check(CoopCampaignMapResourceOwner.Resolve(new[] { baseMap, naval, foreign }) == "Overhaul", "later foreign override is exposed, never guessed Naval");
            Check(CoopCampaignMapResourceOwner.Resolve(new[] { naval, baseMap }) == "Sandbox", "registry ordering wins over module-name preference");
            Check(CoopCampaignMapResourceOwner.Resolve(Array.Empty<KeyValuePair<string,string>>()) == null, "empty initialization metadata has no owner");
            var missing = new KeyValuePair<string,string>("NavalDLC", Path.Combine(root, "Missing scene"));
            Directory.CreateDirectory(missing.Value);
            Check(CoopCampaignMapResourceOwner.Resolve(new[] { missing }) == null, "installed descriptor alone cannot imply map ownership");

            var cache = new CoopCampaignContentIdentityCache();
            object owner = new object();
            DateTime now = new DateTime(2026,10,29,0,0,0,DateTimeKind.Utc);
            int attempts = 0;
            Func<string> capture = () => {
                attempts++;
                string selected = CoopCampaignMapResourceOwner.Resolve(new[] { missing });
                if (selected == null) throw new IOException("Map registry is not ready.");
                return "naval-v1:" + new string('a',64);
            };
            ExpectNotReady(() => cache.GetOrCreate(owner, now, capture));
            MakeMap(missing.Value);
            ExpectNotReady(() => cache.GetOrCreate(owner, now.AddSeconds(1), capture));
            Check(attempts == 1, "failed readiness is throttled instead of hashing/scanning every tick");
            string ready = cache.GetOrCreate(owner, now.AddSeconds(2), capture);
            Check(CoopCampaignContentProfile.IsNaval(ready) && attempts == 2, "same game owner recovers from early unavailable metadata");
            Check(cache.GetOrCreate(owner, now.AddHours(1), () => { throw new Exception("Must not recapture successful identity"); }) == ready,
                "successful immutable identity stays cached");
            string baseIdentity = "base-v1:" + new string('b',64);
            Check(cache.GetOrCreate(new object(), now, () => baseIdentity) == baseIdentity, "new game/save owner revokes prior identity and retry delay");
            object third = new object();
            ExpectNotReady(() => cache.GetOrCreate(third, now, () => "invalid-profile"));
            Check(cache.GetOrCreate(third, now.AddSeconds(2), () => baseIdentity) == baseIdentity, "malformed identity is never cached as success");
            Console.WriteLine("PASS native-rule map ownership and content readiness: actual folders/order, base/Naval/adapter/foreign, bounded retry, same-owner recovery and save-owner reset.");
        }
        finally
        {
            // This fixture creates only ordinary directories/files beneath its
            // fixed GUID root. No module links or game paths are created here.
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    private static void MakeMap(string folder)
    {
        string scene = Path.Combine(folder,"SceneObj","Main_map");
        Directory.CreateDirectory(scene); File.WriteAllText(Path.Combine(scene,"scene.xscene"), "fixture");
    }
    private static void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
    private sealed class ModuleMetadata
    {
        public string Id { get; set; }
        public string FolderPath { get; set; }
        public bool IsActive { get; set; }
    }
    private static void ExpectNotReady(Action action)
    {
        try { action(); } catch (IOException) { return; }
        throw new InvalidOperationException("Unavailable content was admitted.");
    }
}
