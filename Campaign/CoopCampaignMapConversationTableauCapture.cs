using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.Infrastructure;
using SandBox.View.Map;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View;

namespace CoopSpectator.Campaign
{
    // Host-only reader of an already initialized native presentation. It does not
    // create a tableau, enumerate campaign rosters, tick visuals or select replies.
    // Called on the campaign tick; native data never crosses the process boundary.
    internal static class CoopCampaignMapConversationTableauCapture
    {
        private static readonly FieldInfo Initialized = Field("_initialized");
        private static readonly FieldInfo Enabled = Field("_isEnabled");
        private static readonly FieldInfo Visuals = Field("_agentVisuals");
        private static readonly FieldInfo Camera = Field("_continuousRenderCamera");
        private static readonly FieldInfo BaseFov = Field("_baseCameraFOV");
        private static readonly FieldInfo Atmosphere = Field("_cachedAtmosphereName");
        private static readonly FieldInfo Data = Field("_data");

        internal static bool TryCapture(MapConversationView view,
            out CoopCampaignMapConversationTableauState state,
            out CoopCampaignMapConversationPose pose)
        {
            state = null;
            pose = null;
            if (TaleWorlds.CampaignSystem.Campaign.Current == null ||
                view == null || !view.IsConversationActive ||
                Initialized == null || Enabled == null || Visuals == null ||
                Camera == null || BaseFov == null || Atmosphere == null || Data == null)
                return false;
            try
            {
                MapConversationTableau tableau = view.ConversationMission?.ConversationTableau;
                if (tableau == null || !(bool)Initialized.GetValue(tableau) || !(bool)Enabled.GetValue(tableau))
                    return false;
                var actors = Visuals.GetValue(tableau) as List<AgentVisuals>;
                var camera = Camera.GetValue(tableau) as TaleWorlds.Engine.Camera;
                var data = Data.GetValue(tableau) as MapConversationTableauData;
                if (actors == null || actors.Count < 1 || actors.Count > CoopCampaignMapConversationTableauCodec.MaxMapActors ||
                    camera == null || data == null)
                    return false;
                var captured = new CoopCampaignMapConversationTableauState
                {
                    AtmosphereName = Atmosphere.GetValue(tableau) as string,
                    IsRaining = data.IsRaining,
                    IsSnowing = data.IsSnowing,
                    CameraFrame = DetachFrame(camera.Frame),
                    BaseCameraHorizontalFov = (float)BaseFov.GetValue(tableau)
                };
                var motion = new CoopCampaignMapConversationPose();
                bool motionValid = true;
                foreach (AgentVisuals actor in actors)
                {
                    CoopCampaignMapConversationActorState detached = CaptureActor(actor);
                    if (detached == null) return false;
                    captured.Actors.Add(detached);
                    var actorPose = CapturePose(actor);
                    if (actorPose == null) motionValid = false;
                    else motion.Actors.Add(actorPose);
                }
                // A capture cannot outlive a native view/tableau replacement.
                if (!ReferenceEquals(view.ConversationMission?.ConversationTableau, tableau) ||
                    !view.IsConversationActive || !(bool)Initialized.GetValue(tableau) ||
                    !CoopCampaignMapConversationTableauCodec.IsValid(captured))
                    return false;
                state = captured;
                motion.TableauHash = CoopCampaignMapConversationPoseCodec.Hash(captured);
                if (motionValid && CoopCampaignMapConversationPoseCodec.IsValid(motion)) pose = motion;
                return true;
            }
            catch (Exception)
            {
                // Optional presentation capture: no hot-path diagnostics or mutation.
                return false;
            }
        }

        private static CoopCampaignMapConversationActorState CaptureActor(AgentVisuals actor)
        {
            if (actor == null) return null;
            AgentVisualsData data = actor.GetCopyAgentVisualsData();
            Equipment equipment = actor.GetEquipment();
            if (data == null || equipment == null || !data.ActionSetData.IsValid) return null;
            var visual = new CoopCampaignMapPrototypeAgentVisualState
            {
                BodyProperties = actor.GetBodyProperties().ToString(),
                IsFemale = actor.GetIsFemale(),
                Race = data.RaceData,
                SkeletonType = (int)data.SkeletonTypeData,
                RightWieldedItemIndex = data.RightWieldedItemIndexData,
                LeftWieldedItemIndex = data.LeftWieldedItemIndexData,
                MountCreationKey = string.Empty,
                HasBanner = data.BannerData != null,
                AddColorRandomness = data.AddColorRandomnessData
            };
            for (int slot = 0; slot < visual.EquipmentItemIds.Length; slot++)
            {
                EquipmentElement element = equipment[(EquipmentIndex)slot];
                visual.EquipmentItemIds[slot] = (element.CosmeticItem ?? element.Item)?.StringId ?? string.Empty;
            }
            return new CoopCampaignMapConversationActorState
            {
                Visual = visual,
                Frame = DetachFrame(actor.GetFrame()),
                // ScaleData == 0 requests native body-derived scale; capture the
                // already resolved visible actor instead of replacing it with 1.
                Scale = actor.GetScale(),
                ClothColor1 = data.ClothColor1Data,
                ClothColor2 = data.ClothColor2Data,
                BannerCode = data.BannerData?.Serialize() ?? string.Empty,
                ActionSetName = data.ActionSetData.GetName(),
                InitialActionName = data.ActionCodeData.GetName()
            };
        }

        private static CoopCampaignMapConversationActorPose CapturePose(AgentVisuals actor)
        {
            // A missing/unsupported pose must not invalidate the visible tableau.
            // Same read-only body channel used by installed AgentVisuals refresh.
            try
            {
                var skeleton = actor.GetVisuals()?.GetSkeleton();
                return skeleton == null ? null : new CoopCampaignMapConversationActorPose
                {
                    ActionName = skeleton.GetActionAtChannel(0).GetName(),
                    Progress = skeleton.GetAnimationParameterAtChannel(0),
                    Speed = skeleton.GetAnimationSpeedAtChannel(0)
                };
            }
            catch (Exception) { return null; }
        }

        private static float[] DetachFrame(MatrixFrame frame) => new[]
        {
            frame.rotation.s.x, frame.rotation.s.y, frame.rotation.s.z,
            frame.rotation.f.x, frame.rotation.f.y, frame.rotation.f.z,
            frame.rotation.u.x, frame.rotation.u.y, frame.rotation.u.z,
            frame.origin.x, frame.origin.y, frame.origin.z
        };

        private static FieldInfo Field(string name) => typeof(MapConversationTableau).GetField(
            name, BindingFlags.Instance | BindingFlags.NonPublic);
    }
}
