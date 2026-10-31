using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace CoopSpectator.UI
{
    public sealed partial class CoopCampaignMapPrototypeMissionView
    {
        private sealed class CustomSettlementRoot
        {
            internal GameEntity Root;
            internal Vec3 Position;
        }

        // Prefab-authored visibility has no fortification levels. It never grants
        // level0 admission to an unrelated town, castle, village or hideout.
        private readonly CoopCampaignMapSettlementGeometryAdmission<GameEntity> _customSettlementGeometry =
            new CoopCampaignMapSettlementGeometryAdmission<GameEntity>(
                (root, visible) => root.SetVisibilityExcludeParents(visible));
        private readonly Dictionary<string, CustomSettlementRoot> _customSettlementRoots =
            new Dictionary<string, CustomSettlementRoot>(StringComparer.Ordinal);
        private readonly HashSet<string> _customSettlementAttempts = new HashSet<string>(StringComparer.Ordinal);

        private void UpdateCustomSettlementVisual(CoopCampaignMapPrototypeEntityState visible, Vec3 position)
        {
            _catalogById.TryGetValue(visible.EntityId, out var catalog);
            bool admitted = CoopCampaignMapCustomSettlementPolicy.IsAdmitted(visible, catalog);
            if (!admitted || !CanApplyReplicaEntityUpdates())
            {
                _customSettlementGeometry.Apply(visible.EntityId, false);
                return;
            }
            if (!_customSettlementRoots.TryGetValue(visible.EntityId, out var binding))
            {
                // Resource existence is checked once per owned scene, not per
                // frame. A missing prefab cannot trigger unlimited native retries.
                if (!_customSettlementAttempts.Add(visible.EntityId)) return;
                string prefab = CoopCampaignMapCustomSettlementPolicy.TrainingFieldPrefab;
                GameEntity root = null;
                try
                {
                    if (!GameEntity.PrefabExists(prefab))
                    {
                        ModLogger.Info("Campaign custom settlement prefab is unavailable: " + prefab);
                        return;
                    }
                    root = _mapScene.GetFirstEntityWithName(prefab);
                    if (root == null)
                        root = GameEntity.Instantiate(_mapScene, prefab, callScriptCallbacks: false, createPhysics: false);
                    if (root == null) throw new InvalidOperationException("Custom settlement creation returned null.");
                    root.SetVisibilityExcludeParents(false);
                    // Retain ownership before any subsequent native operation.
                    binding = new CustomSettlementRoot { Root = root, Position = position };
                    _customSettlementRoots.Add(visible.EntityId, binding);
                    // Stock MapScene.AddNewEntityToMapScene changes only position.
                    // Keep the prefab's -2.525 rotation, scale and all children.
                    root.SetLocalPosition(position);
                    root.SetReadyToRender(true);
                    root.SetEntityEnvMapVisibility(false);
                    root.CheckResources(true, false);
                    _customSettlementGeometry.Register(prefab, root);
                    RegisterSettlementPickGeometry(root);
                    ModLogger.Info("Campaign custom settlement visual created: " + prefab);
                }
                catch (Exception ex)
                {
                    _customSettlementRoots.Remove(visible.EntityId);
                    _customSettlementGeometry.Apply(visible.EntityId, false);
                    root?.SetVisibilityExcludeParents(false);
                    ModLogger.Error("Campaign custom settlement creation failed: " + prefab, ex);
                    return;
                }
            }
            else if (binding.Position != position)
            {
                binding.Root.SetLocalPosition(position);
                binding.Position = position;
            }
            _customSettlementGeometry.Apply(visible.EntityId, true);
        }

        private void ClearCustomSettlementVisuals()
        {
            // The owning scene/layer clears the native entities afterwards.
            _customSettlementGeometry.Clear();
            _customSettlementRoots.Clear();
            _customSettlementAttempts.Clear();
        }
    }
}
