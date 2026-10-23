using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.UI
{
    public sealed partial class CoopCampaignMapPrototypeMissionView
    {
        private sealed class SiegeMachineVisual
        {
            internal GameEntity Root;
            internal GameEntity Animated;
            internal string Prefab, Clip;
            internal long InstanceId;
        }
        private readonly Dictionary<long, SiegeMachineVisual> _siegeMachines = new Dictionary<long, SiegeMachineVisual>();
        private CoopCampaignMapSiegeMachines _appliedSiegeMachines;

        private void ApplySiegeMachines(CoopCampaignMapSiegeMachines source)
        {
            if (source != null && ReferenceEquals(source, _appliedSiegeMachines)) return;
            if (_mapScene == null || source == null || !source.TrySnapshot(out var snapshot))
            { ClearSiegeMachines(); return; }
            var retained = new HashSet<long>();
            foreach (var item in snapshot.Items)
            {
                retained.Add(item.Key);
                if (_siegeMachines.TryGetValue(item.Key, out var visual) &&
                    (visual.Prefab != item.Prefab || visual.InstanceId != item.InstanceId))
                {
                    visual.Root.Remove(113); _siegeMachines.Remove(item.Key); visual = null;
                }
                if (visual == null)
                {
                    if (!GameEntity.PrefabExists(item.Prefab)) throw new InvalidOperationException("Siege prefab unavailable");
                    // No gameplay callbacks or physics in the detached presentation scene.
                    var entity = GameEntity.Instantiate(_mapScene, item.Prefab, false, false);
                    if (entity == null) throw new InvalidOperationException("Siege prefab creation failed");
                    visual = new SiegeMachineVisual { Root = entity, Prefab = item.Prefab, InstanceId = item.InstanceId };
                    _siegeMachines.Add(item.Key, visual); // Own before any further native work.
                    entity.SetVisibilityExcludeParents(false);
                    if (item.IsRanged)
                    {
                        var children = new List<WeakGameEntity>();
                        entity.WeakEntity.GetChildrenRecursive(ref children);
                        foreach (var child in children)
                            if (child.HasTag("siege_machine_mapicon_skeleton") && child.Skeleton != null)
                            {
                                if (visual.Animated != null) throw new InvalidOperationException("Ambiguous siege skeleton");
                                visual.Animated = GameEntity.CreateFromWeakEntity(child);
                            }
                        if (visual.Animated == null) throw new InvalidOperationException("Siege skeleton unavailable");
                    }
                }
                var a = item.Frame;
                var frame = MatrixFrame.Identity;
                frame.rotation.s = new Vec3(a[0], a[1], a[2]);
                frame.rotation.f = new Vec3(a[3], a[4], a[5]);
                frame.rotation.u = new Vec3(a[6], a[7], a[8]);
                frame.origin = new Vec3(a[9], a[10], a[11]);
                // Captured world frame already includes authored prefab scale/offset.
                visual.Root.SetGlobalFrame(frame);
                if (item.Animation != null)
                {
                    var skeleton = visual.Animated.Skeleton;
                    if (skeleton == null || !skeleton.IsValid) throw new InvalidOperationException("Siege skeleton expired");
                    if (visual.Clip != item.Animation.ClipName)
                    {
                        skeleton.SetAnimationAtChannel(item.Animation.ClipName, 0, 0f, 0f, item.Animation.Progress);
                        visual.Clip = item.Animation.ClipName;
                    }
                    // Campaign manually drives these phases. Do not let local wall time
                    // advance a paused host or run a clip beyond the admitted sample.
                    skeleton.SetAnimationSpeedAtChannel(0, 0f);
                    skeleton.SetAnimationParameterAtChannel(0, item.Animation.Progress);
                    skeleton.TickAnimationsAndForceUpdate(0f, MatrixFrame.Identity, false);
                }
                visual.Root.SetVisibilityExcludeParents(true);
            }
            foreach (long key in new List<long>(_siegeMachines.Keys))
                if (!retained.Contains(key)) { _siegeMachines[key].Root.Remove(113); _siegeMachines.Remove(key); }
            _appliedSiegeMachines = source;
        }

        private void ClearSiegeMachines()
        {
            _appliedSiegeMachines = null;
            foreach (var visual in _siegeMachines.Values) visual.Root?.Remove(113);
            _siegeMachines.Clear();
        }
    }
}
