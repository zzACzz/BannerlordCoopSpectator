using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace CoopSpectator.UI
{
    public sealed partial class CoopCampaignMapPrototypeMissionView
    {
        private sealed class SiegeProjectileVisual
        {
            internal GameEntity Root;
            internal string Prefab;
        }
        private readonly Dictionary<long, SiegeProjectileVisual> _siegeProjectiles =
            new Dictionary<long, SiegeProjectileVisual>();
        private CoopCampaignMapSiegeProjectiles _appliedSiegeProjectiles;
        private readonly CoopSiegeProjectileParticleProbe _projectileParticleProbe = new CoopSiegeProjectileParticleProbe();

        private void ApplySiegeProjectiles(CoopCampaignMapSiegeProjectiles source)
        {
            if (source != null && ReferenceEquals(source, _appliedSiegeProjectiles)) return;
            if (_mapScene == null || source == null || !source.TrySnapshot(out var snapshot))
            { ClearSiegeProjectiles(); return; }
            var retained = new HashSet<long>();
            foreach (var item in snapshot.Items)
            {
                retained.Add(item.Key);
                if (_siegeProjectiles.TryGetValue(item.Key, out var visual) && visual.Prefab != item.Prefab)
                {
                    visual.Root.Remove(114); _siegeProjectiles.Remove(item.Key); visual = null;
                }
                if (visual == null)
                {
                    if (!GameEntity.PrefabExists(item.Prefab)) throw new InvalidOperationException("Siege projectile prefab unavailable");
                    var root = GameEntity.Instantiate(_mapScene, item.Prefab, false, false);
                    if (root == null) throw new InvalidOperationException("Siege projectile creation failed");
                    visual = new SiegeProjectileVisual { Root = root, Prefab = item.Prefab };
                    _siegeProjectiles.Add(item.Key, visual);
                    root.SetVisibilityExcludeParents(false);
                }
                if (visual.Root.ChildCount < 1) throw new InvalidOperationException("Siege projectile flight child unavailable");
                var a = item.Frame;
                var frame = MatrixFrame.Identity;
                frame.rotation.s = new Vec3(a[0], a[1], a[2]);
                frame.rotation.f = new Vec3(a[3], a[4], a[5]);
                frame.rotation.u = new Vec3(a[6], a[7], a[8]);
                frame.origin = new Vec3(a[9], a[10], a[11]);
                visual.Root.SetGlobalFrame(frame);
                visual.Root.WeakEntity.GetChild(0).SetVisibilityExcludeParents(item.FlightChildVisible);
                visual.Root.SetVisibilityExcludeParents(true);
                _projectileParticleProbe.Observe("replica", visual.Root, item.Prefab, item.FlightChildVisible);
            }
            foreach (long key in new List<long>(_siegeProjectiles.Keys))
                if (!retained.Contains(key)) { _siegeProjectiles[key].Root.Remove(114); _siegeProjectiles.Remove(key); }
            _appliedSiegeProjectiles = source;
        }

        private void ClearSiegeProjectiles()
        {
            _appliedSiegeProjectiles = null;
            foreach (var visual in _siegeProjectiles.Values) visual.Root?.Remove(114);
            _siegeProjectiles.Clear();
        }
    }
}
