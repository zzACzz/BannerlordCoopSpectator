using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade.View;
using TaleWorlds.MountAndBlade.View.Tableaus.Thumbnails;

namespace CoopSpectator.UI
{
    internal sealed partial class CoopCampaignMapPrototypePartyVisual
    {
        private GameEntity _tentRoot;
        private ClothSimulatorComponent _tentFlagCloth;

        private static bool TryCreateTent(Scene scene, CoopCampaignMapPrototypeEntityState state,
            MatrixFrame initialFrame, out CoopCampaignMapPrototypePartyVisual visual)
        {
            visual = null;
            if (!CoopCampaignMapSiegeTentCodec.IsValidForEntity(state.SiegeTentPresentation,
                    state.Kind, state.PartyVisualKind, state.HumanVisual != null ||
                    state.MountVisual != null || state.CaravanMountVisual != null) ||
                !CoopCampaignMapSiegeTentCodec.TryDecodeText(state.SiegeTentPresentation, out var tent)) return false;
            var candidate = new CoopCampaignMapPrototypePartyVisual();
            try
            {
                candidate._tentRoot = GameEntity.CreateEmpty(scene, true, true, true);
                candidate._tentRoot.SetVisibilityExcludeParents(false);
                GameEntity child = GameEntity.CreateEmpty(scene, true, true, true);
                // Attach immediately so any failure is cleaned up with the owned root.
                candidate._tentRoot.AddChild(child, false);
                MetaMesh mesh = MetaMesh.GetCopy(CoopCampaignMapSiegeTent.TentMesh, true, true);
                if (mesh == null) return FailCandidate(candidate, out visual);
                child.AddMultiMesh(mesh, true);
                MatrixFrame local = TentFrame(tent.TentFrame);
                child.SetFrame(ref local, true);
                if (tent.FlagFrame != null)
                {
                    MetaMesh flag = MetaMesh.GetCopy(CoopCampaignMapSiegeTent.FlagMesh, true, true);
                    if (flag == null) return FailCandidate(candidate, out visual);
                    flag.Frame = TentFrame(tent.FlagFrame);
                    child.AddMultiMesh(flag, true);
                    if (child.ClothSimulatorComponentCount != 1)
                        return FailCandidate(candidate, out visual);
                    candidate._tentFlagCloth = child.GetClothSimulator(0);
                    var banner = new Banner(tent.BannerCode);
                    for (int i = 0; i < flag.MeshCount; i++)
                    {
                        Mesh part = flag.GetMeshAtIndex(i);
                        if (part.HasTag("dont_use_tableau")) continue;
                        Material material = part.GetMaterial().CreateCopy();
                        part.SetMaterial(material);
                        var debug = BannerDebugInfo.CreateManual("CoopMapSiegeTent");
                        BannerVisualExtensions.GetTableauTextureLarge(banner, debug, texture => {
                            // A late texture callback must not mutate released native resources.
                            if (candidate._tentRoot == null || texture == null) return;
                            material.SetTexture(TaleWorlds.Engine.Material.MBTextureType.DiffuseMap, texture);
                            ulong mask = (uint)material.GetShader().GetMaterialShaderFlagMask("use_tableau_blending", true);
                            material.SetShaderFlags(material.GetShaderFlags() | mask);
                        });
                    }
                }
                child.SetVisibilityExcludeParents(true);
                return CompleteCandidate(candidate, initialFrame, out visual);
            }
            catch { return FailCandidate(candidate, out visual); }
        }

        private void ResetTent()
        {
            GameEntity root = _tentRoot;
            _tentFlagCloth = null;
            _tentRoot = null;
            try { root?.Remove(103); } catch { }
        }

        private static MatrixFrame TentFrame(float[] frame) => new MatrixFrame(
            new Mat3(new Vec3(frame[0], frame[1], frame[2]),
                new Vec3(frame[3], frame[4], frame[5]), new Vec3(frame[6], frame[7], frame[8])),
            new Vec3(frame[9], frame[10], frame[11]));
    }
}
