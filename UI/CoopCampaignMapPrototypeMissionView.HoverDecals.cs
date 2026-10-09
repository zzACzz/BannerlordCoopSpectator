using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace CoopSpectator.UI
{
    public sealed partial class CoopCampaignMapPrototypeMissionView
    {
        private GameEntity _circleHoverEntity, _fortificationHoverEntity;
        private Decal _circleHoverDecal, _fortificationHoverDecal;
        private bool _hoverDecalFailed;
        private string _hoverDecalEncoded;
        private CoopCampaignMapHoverDecal _hoverDecalDescriptor;

        private void UpdateNativeHoverDecal()
        {
            if (_hoverDecalFailed || !_mapSceneReadyToRender || !CanUseReplicaMapInput() ||
                _hoveredReplicaId == null || string.Equals(_hoveredReplicaId, _movementSuppressHoverId, StringComparison.Ordinal) ||
                !IsPickTargetAdmitted(_hoveredReplicaId) ||
                !_entityReplicas.TryGetValue(_hoveredReplicaId, out EntityReplica replica))
            { HideNativeHoverDecals(); return; }
            try
            {
                string encoded = replica.Target.HoverDecal;
                if (_hoverDecalEncoded != encoded)
                {
                    _hoverDecalEncoded = encoded;
                    if (!CoopCampaignMapHoverDecal.TryDecode(encoded, out _hoverDecalDescriptor))
                        _hoverDecalDescriptor = null;
                }
                var descriptor = _hoverDecalDescriptor;
                if (descriptor == null) { HideNativeHoverDecals(); return; }
                bool fortification = descriptor.Fortification;
                EnsureNativeHoverDecal(fortification);
                GameEntity entity = fortification ? _fortificationHoverEntity : _circleHoverEntity;
                Decal decal = fortification ? _fortificationHoverDecal : _circleHoverDecal;
                float[] f = descriptor.Frame;
                MatrixFrame frame = MatrixFrame.Identity;
                frame.rotation.s = new Vec3(f[0], f[1], f[2]);
                frame.rotation.f = new Vec3(f[3], f[4], f[5]);
                frame.rotation.u = new Vec3(f[6], f[7], f[8]);
                frame.origin = new Vec3(f[9], f[10], f[11]);
                if (!fortification)
                {
                    frame.origin += replica.WorldPosition;
                    bool atSea = replica.Target.Kind != CoopCampaignMapPrototypeEntityKind.Settlement &&
                        (_dynamicById[_hoveredReplicaId].PartyNameplateFlags &
                         (int)CoopCampaignMapPartyNameplateFlags.AtSea) != 0;
                    if (atSea)
                    {
                        Vec3 scale = Vec3.One * 2.5f;
                        frame.Scale(in scale);
                    }
                    else frame.origin.z = _mapScene.GetTerrainHeight(frame.origin.AsVec2);
                    decal.SetVectorArgument(0.166f, 1f, 0.83f, 0f);
                }
                decal.SetFactor1Linear(descriptor.Color);
                entity.SetGlobalFrame(in frame, true);
                entity.SetVisibilityExcludeParents(true);
                (fortification ? _circleHoverEntity : _fortificationHoverEntity)?.SetVisibilityExcludeParents(false);
            }
            catch (Exception ex)
            {
                _hoverDecalFailed = true;
                HideNativeHoverDecals();
                ModLogger.Info("Campaign map hover decal disabled: " + ex.GetType().Name);
            }
        }

        private void EnsureNativeHoverDecal(bool fortification)
        {
            if ((fortification ? _fortificationHoverEntity : _circleHoverEntity) != null) return;
            Material material = Material.GetFromResource(fortification ? "decal_city_circle_a" : "map_circle_decal");
            if (material == null) throw new InvalidOperationException("Native hover material unavailable.");
            GameEntity entity = GameEntity.CreateEmpty(_mapScene, true, true, true);
            if (entity == null) throw new InvalidOperationException("Native hover entity unavailable.");
            // Retain ownership before any remaining native call can fail.
            if (fortification) _fortificationHoverEntity = entity; else _circleHoverEntity = entity;
            entity.SetVisibilityExcludeParents(false);
            Decal decal = Decal.CreateDecal(null);
            if (decal == null) throw new InvalidOperationException("Native hover decal unavailable.");
            if (fortification) _fortificationHoverDecal = decal; else _circleHoverDecal = decal;
            decal.SetMaterial(material);
            _mapScene.AddDecalInstance(decal, "editor_set", false);
            entity.AddComponent(decal);
            entity.SetReadyToRender(true);
        }

        private void HideNativeHoverDecals()
        {
            _circleHoverEntity?.SetVisibilityExcludeParents(false);
            _fortificationHoverEntity?.SetVisibilityExcludeParents(false);
        }

        private void ClearNativeHoverDecals()
        {
            RemoveMarker(_circleHoverEntity);
            RemoveMarker(_fortificationHoverEntity);
            _circleHoverEntity = _fortificationHoverEntity = null;
            _circleHoverDecal = _fortificationHoverDecal = null;
            _hoverDecalEncoded = null;
            _hoverDecalDescriptor = null;
            _hoverDecalFailed = false;
        }
    }
}
