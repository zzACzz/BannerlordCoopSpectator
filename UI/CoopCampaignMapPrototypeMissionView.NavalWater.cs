using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace CoopSpectator.UI
{
    public sealed partial class CoopCampaignMapPrototypeMissionView
    {
        private readonly CoopNavalMapWaterPreparation _navalWater = new CoopNavalMapWaterPreparation();
        private bool _navalWaterRequired;

        private bool TryPrepareNavalWater()
        {
            if (!_navalWaterRequired || _navalWater.Attempted || _mapScene == null) return true;
            try
            {
                if (_navalWater.Ensure(_mapScene, CoopNavalClientModuleAdmission.IsPrepared, _mapSceneReadyToRender,
                    HasCanonicalWaterShader, InitializeCanonicalWater))
                    ModLogger.Info("Naval water initialized for canonical native content and validated shader.");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error("Canonical Naval water preparation failed; releasing the owned map scene.", ex);
                ReleaseSceneLayer();
                InformationManager.ShowInquiry(new InquiryData("Campaign map unavailable",
                    "The map's water resources could not be prepared. Reconnect to reload the map.",
                    true, false, "OK", string.Empty, null, null));
                return false;
            }
        }

        private bool HasCanonicalWaterShader()
        {
            if (!CoopNavalClientModuleAdmission.IsPrepared) return false;
            Shader shader = Shader.GetFromResource("water_simulation_macro");
            return shader != null && shader.GetMaterialShaderFlagMask("grad_fold_clear", false) != 0;
        }

        private void InitializeCanonicalWater()
        {
            // r96/r98 prove why the alias-only calls were unsafe: native Naval
            // feature admission was absent, skipping raw program assignments.
            // Both canonical admission and the stock shader-definition branch
            // are now required before the official wrapper's three operations.
            _mapScene.CheckResources(checkInvisibleEntities: false);
            _mapScene.EnsureWaterWakeRenderer();
            _mapScene.SetWaterWakeWorldSize(128f, 0.994f);
            _mapScene.SetWaterWakeCameraOffset(8f);
        }

        private void ReleaseCanonicalWater()
        {
            if (_mapScene == null || _sceneLayer == null) return;
            try
            {
                _navalWater.Retire(_mapScene, ClearCanonicalWaterView, DeleteCanonicalWater);
            }
            catch (Exception ex)
            {
                ModLogger.Error("Canonical Naval water retirement failed; continuing owned scene release.", ex);
            }
        }

        private void ClearCanonicalWaterView()
        {
            // Match MapScreen.ClearGPUMemory before the flowmap entities are
            // removed. Final Scene.ClearAll destroys the wake but omits the
            // public Delete wrapper's assigned-material slot7 clear/rebind.
            _sceneLayer.ClearRuntimeGPUMemory(true);
        }

        private void DeleteCanonicalWater()
        {
            _mapScene.DeleteWaterWakeRenderer();
        }
    }
}
