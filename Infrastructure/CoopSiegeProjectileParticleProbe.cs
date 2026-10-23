using System;
using System.Collections.Generic;
using System.Threading;
using TaleWorlds.Engine;

namespace CoopSpectator.Infrastructure
{
    // Explicit diagnostic only. No state mutation, native object retention or per-frame logging.
    internal sealed class CoopSiegeProjectileParticleProbe
    {
        private readonly int _ownerThread = Thread.CurrentThread.ManagedThreadId;
        private readonly bool _enabled = ExperimentalFeatures.EnableVerboseDiagnostics &&
            Environment.GetEnvironmentVariable("COOPSPECTATOR_SIEGE_PROJECTILE_DIAGNOSTICS") == "1";
        private readonly HashSet<string> _seen = new HashSet<string>();

        internal void Observe(string role, GameEntity root, string prefab, bool flying)
        {
            if (!_enabled || Thread.CurrentThread.ManagedThreadId != _ownerThread || root == null ||
                !CoopCampaignMapSiegeResources.IsProjectile(prefab) || _seen.Count >= 16) return;
            string key = role + ":" + prefab + ":" + flying;
            if (!_seen.Add(key)) return;
            try
            {
                int children = root.ChildCount;
                if (children < 1 || children > 16) { ModLogger.Info("SiegeProjectileProbe: incompatible child count=" + children); return; }
                var child = GameEntity.CreateFromWeakEntity(root.WeakEntity.GetChild(0));
                if (child == null) return;
                int rootParticles = root.GetComponentCount(GameEntity.ComponentType.ParticleSystemInstanced);
                int flightParticles = child.GetComponentCount(GameEntity.ComponentType.ParticleSystemInstanced);
                ModLogger.Info("SiegeProjectileProbe: role=" + role + " prefab=" + prefab +
                    " flying=" + flying + " children=" + children + " rootParticles=" + rootParticles +
                    " flightParticles=" + flightParticles + " flightChildren=" + child.ChildCount);
            }
            catch (Exception ex) { ModLogger.Info("SiegeProjectileProbe: bounded read failed " + ex.GetType().Name); }
        }
    }
}
