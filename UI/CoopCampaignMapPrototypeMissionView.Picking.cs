using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace CoopSpectator.UI
{
    public sealed partial class CoopCampaignMapPrototypeMissionView
    {
        private readonly CoopCampaignMapPickResolver _pickResolver = new CoopCampaignMapPickResolver();
        private readonly Dictionary<UIntPtr, GameEntity> _pickEntities = new Dictionary<UIntPtr, GameEntity>();
        private readonly UIntPtr[] _pickHits = new UIntPtr[CoopCampaignMapPickResolver.MaximumHits];
        private readonly Intersection[] _pickIntersections = new Intersection[CoopCampaignMapPickResolver.MaximumHits];
        private bool _pickingFailed;

        private void RegisterSettlementPickGeometry(GameEntity root)
        {
            var children = new List<GameEntity>();
            root.GetChildrenRecursive(ref children);
            if (children.Count > 4096) throw new InvalidOperationException("Settlement pick geometry limit.");
            string id = "settlement:" + root.Name;
            RegisterPickEntity(root, id, false);
            foreach (GameEntity child in children) RegisterPickEntity(child, id, false);
        }

        private void RegisterPickEntity(GameEntity entity, string id, bool mobile)
        {
            if (entity == null) return;
            _pickResolver.Register(entity.Pointer, id, mobile);
            _pickEntities[entity.Pointer] = entity;
        }

        private void UpdatePartyPickGeometry(EntityReplica replica, MatrixFrame frame, bool visible)
        {
            if (_pickingFailed) return;
            try
            {
                if (!visible)
                {
                    RemovePartyPickGeometry(replica);
                    return;
                }
                if (replica.PickBody == null)
                {
                    replica.PickBody = GameEntity.CreateEmpty(_mapScene, true, true, true);
                    if (replica.PickBody == null) throw new InvalidOperationException("Party pick body unavailable.");
                    // Installed MobilePartyVisual.InitializePartyCollider: unscaled radius 0.5,
                    // Moveable | OnlyCollideWithRaycast. No rigid-body simulation or battle scene.
                    replica.PickBody.AddSphereAsBody(Vec3.Zero, 0.5f,
                        BodyFlags.Moveable | BodyFlags.OnlyCollideWithRaycast);
                    RegisterPickEntity(replica.PickBody, replica.Target.EntityId, true);
                }
                if (!replica.HasPickFrame || !replica.PickFrame.NearlyEquals(frame, 0.00001f))
                {
                    replica.PickBody.SetFrame(ref frame, true);
                    replica.PickFrame = frame;
                    replica.HasPickFrame = true;
                }
            }
            catch (Exception ex) { DisableNativePicking(ex); }
        }

        private void RemovePartyPickGeometry(EntityReplica replica)
        {
            GameEntity body = replica.PickBody;
            replica.PickBody = null;
            replica.HasPickFrame = false;
            if (body == null) return;
            _pickResolver.Remove(body.Pointer);
            _pickEntities.Remove(body.Pointer);
            RemoveMarker(body);
        }

        private bool IsPickTargetAdmitted(string id)
        {
            return _entityReplicas.TryGetValue(id, out EntityReplica replica) &&
                replica.Target != null && _dynamicById.TryGetValue(id, out var dynamicState) &&
                dynamicState.IsVisible && (replica.Target.Kind == CoopCampaignMapPrototypeEntityKind.Settlement ||
                    CoopCampaignMapPartyBodyPolicy.ShouldRenderBody(replica.Target.Kind, dynamicState.PartyNameplateFlags));
        }

        private bool IsPickGeometryVisible(UIntPtr handle) =>
            _pickEntities.TryGetValue(handle, out GameEntity entity) && entity.IsVisibleIncludeParents();

        private string ResolveNativeHoveredReplica()
        {
            if (_pickingFailed || _mapScene == null || _sceneLayer?.SceneView == null) return null;
            try
            {
                Vec3 near = Vec3.Zero, far = Vec3.Zero;
                _sceneLayer.SceneView.TranslateMouse(ref near, ref far);
                Vec3 direction = far - near;
                float length = direction.Normalize();
                if (!IsFinite(length) || length <= 0f) return null;
                var ray = new Ray(near, direction, length);
                int count = _mapScene.SelectEntitiesCollidedWith(ref ray, _pickIntersections, _pickHits);
                return _pickResolver.Resolve(_pickHits, count, IsPickTargetAdmitted, IsPickGeometryVisible);
            }
            catch (Exception ex) { DisableNativePicking(ex); return null; }
            finally
            {
                Array.Clear(_pickHits, 0, _pickHits.Length);
                Array.Clear(_pickIntersections, 0, _pickIntersections.Length);
            }
        }

        private void DisableNativePicking(Exception ex)
        {
            if (_pickingFailed) return;
            _pickingFailed = true;
            _hoveredReplicaId = null;
            ModLogger.Info("Campaign map native picking disabled: " + ex.GetType().Name);
        }

        private void ClearNativePicking()
        {
            // Remove owned bodies before destroying their scene. Static scene entities
            // are only referenced, never destroyed by the picking adapter.
            foreach (EntityReplica replica in _entityReplicas.Values) RemovePartyPickGeometry(replica);
            _pickResolver.Clear();
            _pickEntities.Clear();
            Array.Clear(_pickHits, 0, _pickHits.Length);
            Array.Clear(_pickIntersections, 0, _pickIntersections.Length);
            _pickingFailed = false;
        }
    }
}
