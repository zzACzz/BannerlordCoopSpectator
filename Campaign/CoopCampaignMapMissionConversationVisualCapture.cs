using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;

namespace CoopSpectator.Campaign
{
    // Reads only an admitted live host presentation. Never updates host camera,
    // bones, equipment, visibility or scene; no mission/campaign constructors.
    internal static class CoopCampaignMapMissionConversationVisualCapture
    {
        private static readonly PropertyInfo Initializer = typeof(Mission).GetProperty(
            "InitializerRecord", BindingFlags.Instance | BindingFlags.NonPublic);
        private static Mission _mission;
        private static List<Agent> _actors;
        private static CoopCampaignMapConversationTableauState _descriptor;

        internal static void Reset() { _mission = null; _actors = null; _descriptor = null; }

        internal static bool TryCapture(MissionView view, out CoopCampaignMapConversationTableauState state,
            out CoopCampaignMapConversationPose pose)
        {
            state = null; pose = null;
            try
            {
                Mission mission = Mission.Current;
                if (view == null || !CoopCampaignMapMissionConversationContext.TryGetActiveView(mission,
                        TaleWorlds.CampaignSystem.Campaign.Current?.ConversationManager?.Handler, out var currentView) ||
                    !ReferenceEquals(view, currentView) || mission.Scene == null ||
                    !string.IsNullOrEmpty(mission.SceneLevels) || Initializer == null ||
                    !(Initializer.GetValue(mission) is MissionInitializerRecord record) ||
                    record.NeedsRandomTerrain || record.SceneHasMapPatch || !record.AtmosphereOnCampaign.IsValid)
                { Reset(); return false; }
                Camera camera = view.MissionScreen.CombatCamera;
                if (camera == null || mission.Agents.Count > 32) { Reset(); return false; }
                var atmosphere = record.AtmosphereOnCampaign;
                Vec2 wind = mission.Scene.GetGlobalWindStrengthVector();
                var captured = new CoopCampaignMapConversationTableauState
                {
                    AtmosphereName = atmosphere.AtmosphereName,
                    IsRaining = mission.Scene.GetRainDensity() > 0f,
                    IsSnowing = mission.Scene.GetSnowDensity() > 0f,
                    CameraFrame = Frame(camera.Frame),
                    BaseCameraHorizontalFov = camera.GetFovHorizontal(),
                    MissionScene = new CoopCampaignMapConversationMissionScene
                    {
                        SceneName = mission.SceneName, CameraVerticalFov = camera.GetFovVertical(),
                        CameraNear = camera.Near, CameraFar = camera.Far, TimeOfDay = mission.Scene.TimeOfDay,
                        Winter = mission.Scene.GetWinterTimeFactor(), Dryness = atmosphere.TimeInfo.DrynessFactor,
                        Rain = mission.Scene.GetRainDensity(), Snow = mission.Scene.GetSnowDensity(),
                        Temperature = atmosphere.AreaInfo.Temperature, Humidity = atmosphere.AreaInfo.Humidity,
                        WindX = wind.x, WindY = wind.y
                    }
                };
                var visible = new List<Agent>();
                foreach (Agent actor in mission.Agents)
                {
                    if (actor == null || !actor.IsActive()) continue;
                    var visuals = actor.AgentVisuals;
                    if (visuals == null || !visuals.IsValid() || !visuals.GetVisible()) continue;
                    // Do not silently omit visible mounts/extra actors and call
                    // that a complete scene. Their descriptors need separate work.
                    if (!actor.IsHuman || actor.MountAgent != null || actor.Character == null ||
                        actor.SpawnEquipment == null || actor.BodyPropertiesValue.Age < 18f ||
                        visible.Count >= CoopCampaignMapConversationTableauCodec.MaxActors)
                    { Reset(); return false; }
                    visible.Add(actor);
                }
                visible.Sort((left, right) => left.Index.CompareTo(right.Index));
                var motion = new CoopCampaignMapConversationPose { CameraFrame = (float[])captured.CameraFrame.Clone() };
                foreach (Agent actor in visible)
                {
                    var visuals = actor.AgentVisuals;
                    Skeleton skeleton = visuals.GetSkeleton();
                    if (skeleton == null || !actor.ActionSet.IsValid) { Reset(); return false; }
                    MatrixFrame worldFrame = visuals.GetGlobalFrame();
                    worldFrame.rotation.MakeUnit(); // Detached value; native scale is separate.
                    var appearance = new CoopCampaignMapPrototypeAgentVisualState
                    {
                        BodyProperties = actor.BodyPropertiesValue.ToString(), IsFemale = actor.IsFemale,
                        Race = actor.Character.Race, SkeletonType = actor.IsFemale ? 1 : 0,
                        RightWieldedItemIndex = (int)actor.GetPrimaryWieldedItemIndex(),
                        LeftWieldedItemIndex = (int)actor.GetOffhandWieldedItemIndex(),
                        MountCreationKey = string.Empty,
                        HasBanner = actor.Origin?.Banner != null, AddColorRandomness = false
                    };
                    for (int slot = 0; slot < appearance.EquipmentItemIds.Length; slot++)
                    {
                        EquipmentElement item = actor.SpawnEquipment[(EquipmentIndex)slot];
                        appearance.EquipmentItemIds[slot] = (item.CosmeticItem ?? item.Item)?.StringId ?? string.Empty;
                    }
                    string action = skeleton.GetActionAtChannel(0).GetName();
                    captured.Actors.Add(new CoopCampaignMapConversationActorState
                    {
                        Visual = appearance, Frame = Frame(worldFrame), Scale = actor.AgentScale,
                        ClothColor1 = actor.ClothingColor1, ClothColor2 = actor.ClothingColor2,
                        BannerCode = actor.Origin?.Banner?.Serialize() ?? string.Empty,
                        ActionSetName = actor.ActionSet.GetName(), InitialActionName = action
                    });
                    motion.Actors.Add(new CoopCampaignMapConversationActorPose
                    {
                        ActionName = action, Progress = skeleton.GetAnimationParameterAtChannel(0),
                        Speed = skeleton.GetAnimationSpeedAtChannel(0), Frame = Frame(worldFrame)
                    });
                }
                if (!ReferenceEquals(Mission.Current, mission) || mission.MissionEnded ||
                    !CoopCampaignMapConversationTableauCodec.IsValid(captured)) { Reset(); return false; }

                // Resource identity is stable across camera/body movement and
                // action changes. Those samples use the separate pose stream.
                bool sameActors = ReferenceEquals(_mission, mission) && _actors?.Count == visible.Count;
                for (int i = 0; sameActors && i < visible.Count; i++)
                    sameActors = ReferenceEquals(_actors[i], visible[i]);
                if (sameActors && _descriptor != null)
                {
                    var resources = captured.Clone();
                    resources.CameraFrame = (float[])_descriptor.CameraFrame.Clone();
                    for (int i = 0; i < resources.Actors.Count; i++)
                    {
                        resources.Actors[i].Frame = (float[])_descriptor.Actors[i].Frame.Clone();
                        resources.Actors[i].InitialActionName = _descriptor.Actors[i].InitialActionName;
                    }
                    if (CoopCampaignMapConversationTableauCodec.Equivalent(resources, _descriptor)) captured = _descriptor.Clone();
                }
                motion.TableauHash = CoopCampaignMapConversationPoseCodec.Hash(captured);
                if (!CoopCampaignMapConversationPoseCodec.IsValid(motion)) { Reset(); return false; }
                _mission = mission; _actors = visible; _descriptor = captured.Clone();
                state = captured; pose = motion;
                return true;
            }
            catch (Exception) { Reset(); return false; }
        }

        private static float[] Frame(MatrixFrame frame) => new[]
        {
            frame.rotation.s.x, frame.rotation.s.y, frame.rotation.s.z,
            frame.rotation.f.x, frame.rotation.f.y, frame.rotation.f.z,
            frame.rotation.u.x, frame.rotation.u.y, frame.rotation.u.z,
            frame.origin.x, frame.origin.y, frame.origin.z
        };
    }
}
