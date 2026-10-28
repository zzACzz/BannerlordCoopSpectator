using System;
using System.Runtime.CompilerServices;
using CoopSpectator.Patches;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade.View.Tableaus.Thumbnails;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new Exception(label); }
    private static int Main()
    {
        try
        {
            MirrorItemPoseTests.Run();
            StoryBannerResourceTests.Run();
            SiegeFlightCallSiteTests.Run();
            SiegeFlightObservationTests.Run();
            SiegeFlightCleanupTests.Run();
            Environment.SetEnvironmentVariable("COOPSPECTATOR_PORTRAIT_POSE_OBSERVER", "1");
            CoopPortraitPoseObservationPatch.Apply();
            CoopReplicaPortraitPosePatch.Apply();
            var cache = new CharacterThumbnailCache();
            TaleWorlds.MountAndBlade.View.Tableaus.ThumbnailCacheManager.Current.Register(cache);
            var data = new CharacterThumbnailCreationData { RenderId = "hero", CharacterCode = new CharacterCode { IsHero = true } };
            cache.Create(data);
            Check(cache.Selections == 2, "Native selector calls preserved");
            Check(!CoopPortraitPoseObservationPatch.TryGet(cache, "hero", out _), "No evidence before completion");
            Check(((IThumbnailCache)cache).OnThumbnailRenderCompleted("hero", new Texture()), "Native completion result preserved");
            Check(CoopPortraitPoseObservationPatch.TryGet(cache, "hero", out int pose) && pose == 0,
                "Actual camera pose captured, discarded second pose ignored");
            Check(CoopPortraitPoseObservationPatch.TryGetCurrent("hero", out pose) && pose == 0,
                "Current registered cache resolves exact key");
            Check(!CoopPortraitPoseObservationPatch.TryGetCurrent("hidden-or-other-size", out _),
                "Unknown exact key cannot disclose another portrait");
            var ambiguous = new CharacterThumbnailCache();
            TaleWorlds.MountAndBlade.View.Tableaus.ThumbnailCacheManager.Current.Register(ambiguous);
            Check(!CoopPortraitPoseObservationPatch.TryGetCurrent("hero", out _), "Ambiguous caches rejected");
            TaleWorlds.MountAndBlade.View.Tableaus.ThumbnailCacheManager.Current.Unregister(ambiguous);
            cache.Hit = true; cache.Create(data);
            Check(cache.Selections == 2 && CoopPortraitPoseObservationPatch.TryGet(cache, "hero", out pose) && pose == 0,
                "Cache hit retains evidence without extra selector calls");
            cache.Evict("hero");
            Check(!CoopPortraitPoseObservationPatch.TryGet(cache, "hero", out _), "Native eviction clears completed evidence");
            cache.Hit = false; cache.Create(data);
            ((IThumbnailCache)cache).Clear(false);
            ((IThumbnailCache)cache).OnThumbnailRenderCompleted("hero", new Texture());
            Check(!CoopPortraitPoseObservationPatch.TryGet(cache, "hero", out _), "Clear rejects late completion");
            cache.Throw = true;
            bool threw = false;
            try { cache.Create(data); } catch (InvalidOperationException) { threw = true; }
            Check(threw && !CoopPortraitPoseObservationPatch.TryGet(cache, "hero", out _), "Native exception preserved, failed request invalidated");
            cache.Throw = false; cache.Create(data);
            ((IThumbnailCache)cache).OnThumbnailRenderCompleted("hero", null);
            Check(!CoopPortraitPoseObservationPatch.TryGet(cache, "hero", out _), "Null texture is not evidence");
            TaleWorlds.MountAndBlade.GameNetwork.IsMultiplayer = true;
            cache.Create(data);
            ((IThumbnailCache)cache).OnThumbnailRenderCompleted("hero", new Texture());
            Check(!CoopPortraitPoseObservationPatch.TryGet(cache, "hero", out _), "Multiplayer capture excluded");
            TaleWorlds.MountAndBlade.GameNetwork.IsMultiplayer = false;
            cache.Create(data);
            ((IThumbnailCache)cache).OnThumbnailRenderCompleted("hero", new Texture());
            Check(CoopPortraitPoseObservationPatch.TryGet(cache, "hero", out _), "Capture recovers after failed requests");
            ((IThumbnailCache)cache).Destroy();
            Check(!CoopPortraitPoseObservationPatch.TryGet(cache, "hero", out _), "Destroy removes evidence");
            var other = new OtherCache();
            ((IThumbnailCache)other).Clear(false);
            Check(((IThumbnailCache)other).OnThumbnailRenderCompleted("hero", new Texture()), "Other generic cache keeps native result");
            Check(!CoopPortraitPoseObservationPatch.TryGet(other, "hero", out _), "Other cache excluded");
            var replicaCache = new CharacterThumbnailCache();
            var hero = new CharacterCode { IsHero = true };
            var replica = new CoopSpectator.Infrastructure.CoopReplicaPortraitCreationData(hero, null, null, false, 6);
            var differentPose = new CoopSpectator.Infrastructure.CoopReplicaPortraitCreationData(hero, null, null, false, 7);
            var differentSize = new CoopSpectator.Infrastructure.CoopReplicaPortraitCreationData(hero, null, null, true, 6);
            Check(replica.RenderId != differentPose.RenderId && replica.RenderId != differentSize.RenderId,
                "Replica cache identity separates host poses and native sizes");
            TaleWorlds.MountAndBlade.GameNetwork.IsMultiplayer = true;
            replicaCache.Create(replica);
            Check(replicaCache.CameraPose == "lord_6" && replicaCache.DiscardedPose == "lord_1" && replicaCache.Selections == 2,
                "Replica overrides effective pose only, preserving selector calls and discarded result");
            replicaCache.Create(data);
            Check(replicaCache.CameraPose == "lord_2", "Ordinary multiplayer thumbnail unchanged");
            replicaCache.Throw = true;
            try { replicaCache.Create(differentPose); } catch (InvalidOperationException) { }
            replicaCache.Throw = false;
            replicaCache.Create(data);
            Check(replicaCache.CameraPose == "lord_6", "Exception restores replica request scope");
            TaleWorlds.MountAndBlade.GameNetwork.IsMultiplayer = false;
            replicaCache.Create(replica);
            Check(replicaCache.CameraPose == "lord_0", "Campaign selector never overridden by replica request");
            bool invalidPose = false;
            try { new CoopSpectator.Infrastructure.CoopReplicaPortraitCreationData(hero, null, null, false, 8); }
            catch (ArgumentOutOfRangeException) { invalidPose = true; }
            Check(invalidPose, "Invalid replica pose rejected before rendering");
            TaleWorlds.MountAndBlade.GameNetwork.IsMultiplayer = true;
            var nestedCache = new CharacterThumbnailCache();
            string nestedPose = null;
            nestedCache.BeforeSelect = () => { nestedCache.Create(differentPose); nestedPose = nestedCache.CameraPose; };
            nestedCache.Create(replica);
            Check(nestedPose == "lord_7" && nestedCache.CameraPose == "lord_6" && nestedCache.Selections == 4,
                "Nested replica request restores outer pose and preserves all native calls");
            nestedCache.BeforeSelect = () => { nestedCache.Create(data); nestedPose = nestedCache.CameraPose; };
            nestedCache.Create(replica);
            Check(nestedPose == "lord_4" && nestedCache.CameraPose == "lord_6",
                "Nested ordinary request cannot inherit outer replica pose");
            nestedCache.BeforeSelect = () =>
            {
                nestedCache.Throw = true;
                try { nestedCache.Create(differentPose); } catch (InvalidOperationException) { }
                finally { nestedCache.Throw = false; }
            };
            nestedCache.Create(replica);
            Check(nestedCache.CameraPose == "lord_6", "Caught nested failure restores outer replica scope");
            var threadCache = new CharacterThumbnailCache();
            nestedCache.BeforeSelect = () => System.Threading.Tasks.Task.Run(() => threadCache.Create(data)).GetAwaiter().GetResult();
            nestedCache.Create(replica);
            Check(threadCache.CameraPose == "lord_0", "Other thread cannot inherit replica scope");
            nestedCache.Hit = true;
            int selectionsBeforeHit = nestedCache.Selections;
            nestedCache.Create(differentPose);
            Check(nestedCache.Selections == selectionsBeforeHit, "Replica cache hit does not invoke selector");
            nestedCache.Hit = false;
            nestedCache.Create(data);
            Check(nestedCache.CameraPose == "lord_" + (selectionsBeforeHit % 8), "Cache hit leaves no override scope");
            Console.WriteLine("Portrait observer patched-stub checks passed: " + _checks);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

namespace CoopSpectator.Infrastructure
{
    public static class ExperimentalFeatures
    {
        public static bool EnableCampaignMapPrototype = true;
        public static bool EnableCampaignMapSiegeFlights => EnableCampaignMapPrototype &&
            Environment.GetEnvironmentVariable("COOPSPECTATOR_SIEGE_FLIGHT_OBSERVER") != "0";
    }
    public static class ModLogger
    {
        public static void Info(string text) { Console.WriteLine(text); }
        public static void Error(string text, Exception ex) { throw new Exception(text, ex); }
    }
}
namespace TaleWorlds.CampaignSystem { public class Campaign { public static Campaign Current = new Campaign(); } }
namespace TaleWorlds.MountAndBlade { public static class GameNetwork { public static bool IsMultiplayer; } }
namespace TaleWorlds.Core { public class CharacterCode { public bool IsHero; } }
namespace TaleWorlds.Engine { public class Texture { } }
namespace TaleWorlds.MountAndBlade.View.Tableaus
{
    public class ThumbnailCacheManager
    {
        public static ThumbnailCacheManager Current = new ThumbnailCacheManager();
        private readonly System.Collections.Generic.List<Thumbnails.IThumbnailCache> _thumbnailCaches =
            new System.Collections.Generic.List<Thumbnails.IThumbnailCache>();
        public void Register(Thumbnails.IThumbnailCache cache) { _thumbnailCaches.Add(cache); }
        public void Unregister(Thumbnails.IThumbnailCache cache) { _thumbnailCaches.Remove(cache); }
    }
}
namespace TaleWorlds.MountAndBlade.View.Tableaus.Thumbnails
{
    public class CharacterThumbnailCreationData
    {
        public CharacterCode CharacterCode; public string RenderId;
        public CharacterThumbnailCreationData() { }
        public CharacterThumbnailCreationData(CharacterCode character, Action<Texture> completed, Action cancelled, bool big)
        { CharacterCode = character; RenderId = "normalized-code" + (big ? "1" : "0"); }
    }
    public interface IThumbnailCache
    {
        bool OnThumbnailRenderCompleted(string renderId, Texture renderTarget);
        void Clear(bool releaseImmediately);
        void Destroy();
    }
    public class Cache<T> : IThumbnailCache
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        bool IThumbnailCache.OnThumbnailRenderCompleted(string renderId, Texture renderTarget) => true;
        [MethodImpl(MethodImplOptions.NoInlining)] void IThumbnailCache.Clear(bool releaseImmediately) { }
        [MethodImpl(MethodImplOptions.NoInlining)] void IThumbnailCache.Destroy() { }
        [MethodImpl(MethodImplOptions.NoInlining)] protected virtual void OnRequestCancelled(string key) { }
        public void Evict(string key) { OnRequestCancelled(key); }
    }
    public class OtherCache : Cache<object> { }
    public class CharacterThumbnailCache : Cache<CharacterThumbnailCreationData>
    {
        public int Selections;
        public string CameraPose, DiscardedPose;
        public bool Hit, Throw;
        public Action BeforeSelect;
        public void Create(CharacterThumbnailCreationData data) { OnCreateTexture(data); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void OnCreateTexture(CharacterThumbnailCreationData data)
        {
            if (Hit) return;
            CreateCharacterBaseEntity(data.CharacterCode);
            GetPoseParamsFromCharacterCode(data.CharacterCode, out DiscardedPose, out _);
            if (Throw) throw new InvalidOperationException("native failure");
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void CreateCharacterBaseEntity(CharacterCode code)
        {
            var nested = BeforeSelect; BeforeSelect = null; nested?.Invoke();
            GetPoseParamsFromCharacterCode(code, out CameraPose, out _);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GetPoseParamsFromCharacterCode(CharacterCode code, out string poseName, out bool hasHorse)
        { poseName = "lord_" + (Selections++ % 8); hasHorse = false; }
    }
}
