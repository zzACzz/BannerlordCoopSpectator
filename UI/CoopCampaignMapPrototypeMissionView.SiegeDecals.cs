using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace CoopSpectator.UI
{
    public sealed partial class CoopCampaignMapPrototypeMissionView
    {
        private sealed class SiegeDecalVisual
        {
            public GameEntity Entity;
            public Decal Decal;
        }
        private readonly Dictionary<long, SiegeDecalVisual> _siegeDecals = new Dictionary<long, SiegeDecalVisual>();
        private CoopCampaignMapSiegeDecals _appliedSiegeDecals;

        private void ApplySiegeDecals(CoopCampaignMapSiegeDecals source)
        {
            // Network presentation instances are replaced, never mutated after admission.
            // Clear resets this identity so a suspended view can recreate the same sample.
            if (source != null && ReferenceEquals(source, _appliedSiegeDecals)) return;
            if (_mapScene == null || source == null || !source.TrySnapshot(out var snapshot))
            { ClearSiegeDecals(); return; }
            var retained = new HashSet<long>();
            foreach (var item in snapshot.Items)
            {
                long key = ((long)item.Group << 32) | (uint)item.Index;
                retained.Add(key);
                if (!_siegeDecals.TryGetValue(key, out var visual))
                {
                    visual = new SiegeDecalVisual { Entity = GameEntity.CreateEmpty(_mapScene, true, true, true) };
                    // Record ownership before further native work, so error cleanup can remove it.
                    _siegeDecals.Add(key, visual);
                    visual.Decal = Decal.CreateDecal(null);
                    visual.Decal.SetMaterial(Material.GetFromResource(item.MaterialName));
                    visual.Entity.AddComponent(visual.Decal);
                    _mapScene.AddDecalInstance(visual.Decal, "editor_set", true);
                }
                var a = item.Frame;
                var frame = MatrixFrame.Identity;
                frame.rotation.s = new Vec3(a[0], a[1], a[2]);
                frame.rotation.f = new Vec3(a[3], a[4], a[5]);
                frame.rotation.u = new Vec3(a[6], a[7], a[8]);
                frame.origin = new Vec3(a[9], a[10], a[11]);
                visual.Entity.SetGlobalFrame(frame);
                visual.Decal.SetFactor1(item.Color);
                visual.Entity.SetVisibilityExcludeParents(true);
            }
            foreach (long key in new List<long>(_siegeDecals.Keys))
                if (!retained.Contains(key))
                { _siegeDecals[key].Entity.Remove(112); _siegeDecals.Remove(key); }
            _appliedSiegeDecals = source;
        }

        private void ClearSiegeDecals()
        {
            _appliedSiegeDecals = null;
            foreach (var visual in _siegeDecals.Values) visual.Entity?.Remove(112);
            _siegeDecals.Clear();
        }
    }
}
