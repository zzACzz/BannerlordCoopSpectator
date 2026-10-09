using System;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace CoopSpectator.UI
{
    public sealed partial class CoopCampaignMapPrototypeMissionView
    {
        private readonly GameEntity[] _movementEntities = new GameEntity[4];
        private readonly Decal[] _movementDecals = new Decal[4];
        private bool _movementFailed;
        private int _movementRevision = -1;
        private CoopCampaignMapMovementTargetPresentation _movementRecipe;
        private double _movementHostElapsed;
        private string _movementSuppressHoverId;

        private void UpdateNativeMovementTargets()
        {
            if (_movementFailed || !_mapSceneReadyToRender || !CanUseReplicaMapInput() ||
                !CoopCampaignMapPrototypeNetworkController.TryGetMovementTargetState(out var state, out double age))
            { HideNativeMovementTargets(); return; }
            try
            {
                if (_movementRevision != state.Revision)
                {
                    _movementRevision = state.Revision;
                    if (!CoopCampaignMapMovementTargetCodec.TryDecode(state.MovementTarget, out _movementRecipe))
                        _movementRecipe = null;
                    if (_movementRecipe != null)
                        _movementHostElapsed = _movementRecipe.AnimationElapsedSeconds;
                }
                var recipe = _movementRecipe;
                if (recipe == null) { HideNativeMovementTargets(); return; }
                CoopCampaignMapPrototypeCatalogEntityState catalog = null;
                CoopCampaignMapPrototypeDynamicEntityState dynamicState = null;
                if (recipe.EntityId != null)
                {
                    _catalogById.TryGetValue(recipe.EntityId, out catalog);
                    _dynamicById.TryGetValue(recipe.EntityId, out dynamicState);
                }
                if (!CoopCampaignMapMovementTargetCodec.IsAdmitted(recipe, catalog, dynamicState))
                { HideNativeMovementTargets(); return; }
                recipe.AnimationElapsedSeconds = _movementHostElapsed + age;
                if (recipe.Kind == CoopCampaignMapMovementTargetKind.Party)
                {
                    if (!_entityReplicas.TryGetValue(recipe.EntityId, out var replica) || !replica.HasWorldPosition)
                    { HideNativeMovementTargets(); return; }
                    float[] offset = recipe.VisualOriginOffset;
                    recipe.Origin[0] = replica.WorldPosition.x + offset[0];
                    recipe.Origin[1] = replica.WorldPosition.y + offset[1];
                    recipe.Origin[2] = replica.WorldPosition.z + offset[2];
                }
                if (!CoopCampaignMapMovementTargetLayout.TryBuild(recipe, out var layout))
                { HideNativeMovementTargets(); return; }
                int activeMask = 0;
                for (int i = 0; i < layout.Length; i++)
                {
                    var item = layout[i];
                    int slot = item.Material == CoopCampaignMapMovementDecalMaterial.Fortification ? 2 :
                        item.Material == CoopCampaignMapMovementDecalMaterial.Wind ? 3 : i;
                    EnsureNativeMovementTarget(slot);
                    float[] f = item.Frame, uv = item.TextureCoordinates;
                    var frame = new MatrixFrame(new Mat3(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8]),
                        new Vec3(f[9], f[10], f[11]));
                    _movementDecals[slot].SetFactor1Linear(item.Color);
                    _movementDecals[slot].SetVectorArgument(uv[0], uv[1], uv[2], uv[3]);
                    _movementEntities[slot].SetGlobalFrame(in frame, true);
                    activeMask |= 1 << slot;
                }
                for (int i = 0; i < 4; i++)
                    _movementEntities[i]?.SetVisibilityExcludeParents((activeMask & (1 << i)) != 0);
                _movementSuppressHoverId = recipe.EntityId;
            }
            catch (Exception ex)
            {
                _movementFailed = true;
                HideNativeMovementTargets();
                ModLogger.Info("Campaign map movement decals disabled: " + ex.GetType().Name);
            }
        }

        private void EnsureNativeMovementTarget(int slot)
        {
            if (_movementEntities[slot] != null) return;
            string resource = slot == 2 ? "decal_city_circle_a" : slot == 3 ? "decal_map_circle_wind" : "map_circle_decal";
            Material material = Material.GetFromResource(resource);
            if (material == null) throw new InvalidOperationException("Movement material unavailable");
            GameEntity entity = GameEntity.CreateEmpty(_mapScene, true, true, true);
            if (entity == null) throw new InvalidOperationException("Movement entity unavailable");
            _movementEntities[slot] = entity;
            entity.SetVisibilityExcludeParents(false);
            Decal decal = Decal.CreateDecal(null);
            if (decal == null) throw new InvalidOperationException("Movement decal unavailable");
            _movementDecals[slot] = decal;
            decal.SetMaterial(material);
            _mapScene.AddDecalInstance(decal, "editor_set", false);
            entity.AddComponent(decal);
            entity.SetReadyToRender(true);
        }

        private void HideNativeMovementTargets()
        {
            _movementSuppressHoverId = null;
            for (int i = 0; i < 4; i++)
            {
                try { _movementEntities[i]?.SetVisibilityExcludeParents(false); }
                catch { _movementFailed = true; }
            }
        }

        private void ClearNativeMovementTargets()
        {
            HideNativeMovementTargets();
            for (int i = 0; i < 4; i++)
            {
                RemoveMarker(_movementEntities[i]);
                _movementEntities[i] = null;
                _movementDecals[i] = null;
            }
            _movementRecipe = null;
            _movementRevision = -1;
            _movementFailed = false;
        }
    }
}
