using System;
using System.IO;
using CoopSpectator.Infrastructure;

internal static class ConversationMissionSceneTests
{
    private static int _checks;

    internal static void Run()
    {
        foreach (string name in new[] { "conversation_scene_green_valley", "conversation_scene_forest",
            "conversation_scene_steppe", "conversation_scene_desert" })
        {
            var state = Fixture();
            state.MissionScene.SceneName = name;
            Check(CoopCampaignMapConversationTableauCodec.TrySerialize(state, out var bytes), "serialize " + name);
            Check(CoopCampaignMapConversationTableauCodec.TryDeserialize(bytes, out var copy), "deserialize " + name);
            Check(CoopCampaignMapConversationTableauCodec.Equivalent(state, copy), "complete round trip " + name);
            Check(CoopCampaignMapConversationTableauCodec.Equivalent(state, state.Clone()), "clone " + name);
            copy.MissionScene.TimeOfDay++;
            Check(state.MissionScene.TimeOfDay == 9 && !CoopCampaignMapConversationTableauCodec.Equivalent(state, copy), "detached climate");
            for (int length = 0; length < bytes.Length; length++)
            {
                var prefix = new byte[length];
                Buffer.BlockCopy(bytes, 0, prefix, 0, length);
                Check(!CoopCampaignMapConversationTableauCodec.TryDeserialize(prefix, out _), "truncated scene payload");
            }
            var old = (byte[])bytes.Clone();
            Buffer.BlockCopy(BitConverter.GetBytes(1), 0, old, 4, 4);
            Check(!CoopCampaignMapConversationTableauCodec.TryDeserialize(old, out _), "old tableau schema rejected");
            old = (byte[])bytes.Clone(); old[8] = 2;
            Check(!CoopCampaignMapConversationTableauCodec.TryDeserialize(old, out _), "canonical presence flag");
        }
        foreach (string name in new[] { "", "../conversation_scene_forest", "Main_map", "battle_terrain_029",
            "hideout_forest", "conversation_scene_forest/scene", "CONVERSATION_SCENE_FOREST", "conversation_scene_future" })
        {
            var state = Fixture(); state.MissionScene.SceneName = name;
            Check(!CoopCampaignMapConversationTableauCodec.TrySerialize(state, out _), "scene identity rejected");
        }
        foreach (var property in typeof(CoopCampaignMapConversationMissionScene).GetProperties())
        {
            if (property.PropertyType != typeof(float)) continue;
            foreach (float value in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity, float.MaxValue, float.MinValue })
            {
                var state = Fixture(); property.SetValue(state.MissionScene, value);
                Check(!CoopCampaignMapConversationTableauCodec.TrySerialize(state, out _), "invalid " + property.Name);
            }
        }
        var map = ConversationTableauTests.Fixture();
        Check(map.MissionScene == null && CoopCampaignMapConversationTableauCodec.IsValid(map), "ordinary map unchanged");
        map.AtmosphereName = "semi_cloudy_09";
        Check(!CoopCampaignMapConversationTableauCodec.IsValid(map), "ordinary map still requires conv atmosphere");
        var mission = Fixture();
        mission.MissionScene.CameraNear = mission.MissionScene.CameraFar = 10;
        Check(!CoopCampaignMapConversationTableauCodec.IsValid(mission), "ordered clipping planes");
        mission = Fixture(); mission.AtmosphereName = "../atmosphere";
        Check(!CoopCampaignMapConversationTableauCodec.IsValid(mission), "no atmosphere paths");

        string view = File.ReadAllText("UI/CoopCampaignMapConversationTableauView.cs");
        Check(view.Contains("_missionScene?.SceneName ?? CoopCampaignMapConversationTableauCodec.SceneName") &&
            view.Contains("ResolveSceneOwner(sceneName)") && view.Contains("_scene.Read(sceneName, owner"), "actual scene resource selected");
        Check(view.Contains("SetFovVertical(_missionScene.CameraVerticalFov") &&
            view.Contains("_missionScene.CameraNear, _missionScene.CameraFar"), "native vertical FOV and clip range");
        Check(view.Contains("InitPhysicsWorld = false") && !view.Contains("SpawnAgent(") &&
            !view.Contains("SpawnCharacter(") && !view.Contains("MissionState.OpenNew("), "no physical agent or mission opening");
        Check(view.Contains("_missionScene = null;") && view.Contains("else ApplyMissionClimate();"), "lifetime and climate branch");
        string capture = File.ReadAllText("Campaign/CoopCampaignMapMissionConversationVisualCapture.cs");
        Check(capture.Contains("CoopCampaignMapMissionConversationContext.TryGetActiveView") &&
            capture.Contains("visuals.GetVisible()") && capture.Contains("visuals.GetGlobalFrame()") &&
            capture.Contains("view.MissionScreen.CombatCamera"), "capture reads admitted visible native presentation");
        Check(!capture.Contains("ForceUpdate") && !capture.Contains("SetAction(") &&
            !capture.Contains("TickAnimations") && !capture.Contains("SpawnAgent(") &&
            !capture.Contains("SetVisible("), "host presentation never mutated to obtain a sample");
        Check(capture.Contains("resources.CameraFrame = (float[])_descriptor.CameraFrame.Clone()") &&
            capture.Contains("InitialActionName = _descriptor.Actors[i].InitialActionName") &&
            capture.Contains("motion.TableauHash = CoopCampaignMapConversationPoseCodec.Hash(captured)"),
            "movement/action samples keep immutable descriptor identity");
        ValidateMotion();
        Console.WriteLine("Conversation mission scene checks passed: " + _checks);
    }

    private static void ValidateMotion()
    {
        var conversation = new CoopCampaignMapConversationState { IsActive = true, Revision = 4, Tableau = Fixture() };
        var pose = ConversationPoseTests.Fixture(conversation);
        Check(!CoopCampaignMapConversationPoseCodec.Matches(pose, conversation), "mission motion requires frames");
        pose.CameraFrame = (float[])conversation.Tableau.CameraFrame.Clone();
        for (int i = 0; i < pose.Actors.Count; i++)
        {
            pose.Actors[i].Frame = (float[])conversation.Tableau.Actors[i].Frame.Clone();
            pose.Actors[i].ActionName = "act_" + new string('a', 124);
        }
        Check(CoopCampaignMapConversationPoseCodec.Matches(pose, conversation), "complete frame binding");
        Check(CoopCampaignMapConversationPoseCodec.TrySerialize(pose, out var bytes) && bytes.Length == 665 &&
            bytes.Length <= CoopCampaignMapConversationPoseCodec.MaxPayloadBytes, "maximum framed pose bounded below 768");
        Check(CoopCampaignMapConversationPoseCodec.TryDeserialize(bytes, out var copy) &&
            CoopCampaignMapConversationPoseCodec.Matches(copy, conversation), "framed pose round trip");
        Check(CoopCampaignMapConversationPoseCodec.TrySerialize(copy, out var roundtrip) &&
            Convert.ToBase64String(roundtrip) == Convert.ToBase64String(bytes), "all frame coordinates retained");
        var clone = pose.Clone(); clone.CameraFrame[9]++; clone.Actors[0].Frame[10]++;
        Check(clone.CameraFrame[9] != pose.CameraFrame[9] && clone.Actors[0].Frame[10] != pose.Actors[0].Frame[10], "deep frame clone");
        var receiver = new CoopCampaignMapConversationPoseReceiver(); receiver.Bind(conversation);
        Check(receiver.TryAccept(pose, out _), "mission receiver admits matching frames");
        pose.Sequence++; pose.Actors[0].Frame = null;
        Check(!receiver.TryAccept(pose, out _), "partial frame sample rejected");
        pose = copy.Clone(); pose.Sequence++; pose.CameraFrame[0] = float.NaN;
        Check(!receiver.TryAccept(pose, out _), "nonfinite camera rejected");
        pose = copy.Clone(); pose.Sequence++; pose.Actors[1].Frame[4] = 0;
        Check(!receiver.TryAccept(pose, out _), "degenerate actor frame rejected");
        var map = new CoopCampaignMapConversationState { IsActive = true, Revision = 5, Tableau = ConversationTableauTests.Fixture() };
        receiver.Bind(map);
        pose = copy.Clone(); pose.ConversationRevision = 5; pose.TableauHash = CoopCampaignMapConversationPoseCodec.Hash(map.Tableau);
        Check(!receiver.TryAccept(pose, out _) && !CoopCampaignMapConversationPoseCodec.Matches(pose, map), "map cannot accept mission frame format");
        for (int length = 0; length < bytes.Length; length++)
        {
            var prefix = new byte[length]; Buffer.BlockCopy(bytes, 0, prefix, 0, length);
            Check(!CoopCampaignMapConversationPoseCodec.TryDeserialize(prefix, out _), "truncated framed pose");
        }
        bytes[4] = 1;
        Check(!CoopCampaignMapConversationPoseCodec.TryDeserialize(bytes, out _), "old pose schema rejected");
    }

    private static CoopCampaignMapConversationTableauState Fixture()
    {
        var state = ConversationTableauTests.Fixture();
        state.AtmosphereName = "semi_cloudy_09";
        state.MissionScene = new CoopCampaignMapConversationMissionScene
        {
            SceneName = "conversation_scene_green_valley", CameraVerticalFov = 1.1f,
            CameraNear = 0.065f, CameraFar = 12500, TimeOfDay = 9, Winter = 0.25f,
            Dryness = 0.75f, Rain = 0.2f, Snow = 0.1f, Temperature = 25, Humidity = 30,
            WindX = -2, WindY = 1
        };
        return state;
    }

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException("Conversation mission scene: " + message);
    }
}
