using System;
using System.IO;
using CoopSpectator.Infrastructure;

internal static class ConversationMissionSceneTests
{
    private static int _checks;

    internal static void Run()
    {
        Check(CoopCampaignMapConversationMissionScene.TryResolveAtmosphereName(null, "semi_cloudy_09", out var selected) &&
            selected == "semi_cloudy_09", "interpolated-only native atmosphere");
        Check(CoopCampaignMapConversationMissionScene.TryResolveAtmosphereName("conv_day", "semi_cloudy_09", out selected) &&
            selected == "conv_day", "explicit atmosphere remains primary");
        foreach (string invalid in new[] { null, "", "../preset", "a/b", "a\\b", "white space", new string('x', 129) })
        {
            Check(!CoopCampaignMapConversationMissionScene.TryResolveAtmosphereName(null, invalid, out selected) &&
                selected == null, "invalid interpolated atmosphere rejected");
            if (!string.IsNullOrEmpty(invalid))
                Check(!CoopCampaignMapConversationMissionScene.TryResolveAtmosphereName(invalid, "semi_cloudy_09", out selected),
                    "invalid primary not silently replaced");
        }
        foreach (string name in new[] { "conversation_scene_green_valley", "conversation_scene_forest",
            "conversation_scene_steppe", "conversation_scene_desert" })
        {
            var state = Fixture();
            state.MissionScene.SceneName = name;
            Check(CoopCampaignMapConversationTableauCodec.TrySerialize(state, out var bytes), "serialize " + name);
            Check(CoopCampaignMapConversationTableauCodec.TryDeserialize(bytes, out var copy), "deserialize " + name);
            Check(CoopCampaignMapConversationTableauCodec.Equivalent(state, copy), "complete round trip " + name);
            foreach (var property in typeof(CoopCampaignMapConversationMissionScene).GetProperties())
                Check(Equals(property.GetValue(state.MissionScene), property.GetValue(copy.MissionScene)),
                    "every scene field round trips: " + property.Name);
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
            Buffer.BlockCopy(BitConverter.GetBytes(3), 0, old, 4, 4);
            Check(!CoopCampaignMapConversationTableauCodec.TryDeserialize(old, out _), "old tableau schema rejected");
            Buffer.BlockCopy(BitConverter.GetBytes(4), 0, old, 4, 4);
            Check(!CoopCampaignMapConversationTableauCodec.TryDeserialize(old, out _), "three-actor tableau schema rejected");
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
        mission = Fixture(); mission.MissionScene.SunDirectionZ = 0;
        Check(!CoopCampaignMapConversationTableauCodec.IsValid(mission), "zero sun direction rejected");
        mission = Fixture(); mission.MissionScene.AtmosphereTimeOfDay = -1;
        Check(!CoopCampaignMapConversationTableauCodec.IsValid(mission), "negative preset time rejected");
        mission = Fixture(); var lightingClone = mission.Clone();
        lightingClone.MissionScene.IsInsideStorm = false;
        Check(mission.MissionScene.IsInsideStorm &&
            !CoopCampaignMapConversationTableauCodec.Equivalent(mission, lightingClone), "lighting detached and changes resource identity");
        foreach (float time in new[] { 0f, 2.23680329f, 12f, 23.99f, 24f })
        {
            mission = Fixture(); mission.MissionScene.AtmosphereTimeOfDay = time;
            Check(CoopCampaignMapConversationTableauCodec.TrySerialize(mission, out var payload) &&
                CoopCampaignMapConversationTableauCodec.TryDeserialize(payload, out var decoded) &&
                decoded.MissionScene.AtmosphereTimeOfDay == time && decoded.MissionScene.TimeOfDay == 9,
                "initializer selection time independent of live scene time");
        }
        mission = Fixture(); mission.MissionScene.SunDirectionX = 1;
        Check(!CoopCampaignMapConversationTableauCodec.IsValid(mission), "nonunit light direction rejected");
        mission = Fixture(); mission.MissionScene.IsInsideStorm = false;
        Check(CoopCampaignMapConversationTableauCodec.TrySerialize(mission, out var calmBytes) &&
            CoopCampaignMapConversationTableauCodec.TryDeserialize(calmBytes, out var calm) &&
            !calm.MissionScene.IsInsideStorm && calm.MissionScene.Rain > 0,
            "rain does not imply storm selection");
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                CoopCampaignMapConversationMissionScene.Write(writer, mission.MissionScene);
            byte[] noncanonicalStorm = stream.ToArray();
            noncanonicalStorm[noncanonicalStorm.Length - 13] = 2;
            bool rejected = false;
            using (var reader = new BinaryReader(new MemoryStream(noncanonicalStorm)))
                try { CoopCampaignMapConversationMissionScene.Read(reader); }
                catch (InvalidDataException) { rejected = true; }
            Check(rejected, "noncanonical storm flag rejected at decoder");
        }

        string view = File.ReadAllText("UI/CoopCampaignMapConversationTableauView.cs");
        Check(view.Contains("_missionScene?.SceneName ?? CoopCampaignMapConversationTableauCodec.SceneName") &&
            view.Contains("ResolveSceneOwner(sceneName)") && view.Contains("_scene.Read(sceneName, owner"), "actual scene resource selected");
        Check(view.Contains("SetFovVertical(_missionScene.CameraVerticalFov") &&
            view.Contains("_missionScene.CameraNear, _missionScene.CameraFar"), "native vertical FOV and clip range");
        Check(view.Contains("InitPhysicsWorld = false") && !view.Contains("SpawnAgent(") &&
            !view.Contains("SpawnCharacter(") && !view.Contains("MissionState.OpenNew("), "no physical agent or mission opening");
        Check(view.Contains("_missionScene = null;") && view.Contains("else ApplyMissionClimate();"), "lifetime and climate branch");
        Check(view.Contains("if (_missionScene != null) _view.SetSceneUsesShadows(true);") &&
            view.Contains("SetFocusedShadowmap(_missionScene == null"),
            "standalone view shadows admitted without changing map focused-shadow profile");
        string capture = File.ReadAllText("Campaign/CoopCampaignMapMissionConversationVisualCapture.cs");
        Check(view.Contains("_sceneProbeEnabled = _verboseAttempt &&") &&
            view.Contains("COOPSPECTATOR_MISSION_SCENE_LOADING_DIAGNOSTICS") &&
            view.Contains("_sceneProbeSample >= 3 || _sceneProbeProcessSamples >= 12"),
            "native loading probe double gated and bounded per scene/process");
        Check(view.Contains("bool terrain = _scene.ContainsTerrain;") &&
            view.Contains("bool loaded = _scene.IsLoadingFinished();") &&
            !view.Contains("_scene.ForceLoadResources(") && !view.Contains("_scene.CheckResources("),
            "loading probe observes without forced scene resource preparation");
        Check(capture.Contains("CoopCampaignMapMissionConversationContext.TryGetActiveView") &&
            capture.Contains("visuals.GetVisible()") && capture.Contains("visuals.GetGlobalFrame()") &&
            capture.Contains("view.MissionScreen.CombatCamera"), "capture reads admitted visible native presentation");
        Check(!capture.Contains("ForceUpdate") && !capture.Contains("SetAction(") &&
            !capture.Contains("TickAnimations") && !capture.Contains("SpawnAgent(") &&
            !capture.Contains("SetVisible("), "host presentation never mutated to obtain a sample");
        Check(!capture.Contains("CAPTURE_DIAGNOSTICS") && !capture.Contains("ModLogger."),
            "temporary host capture probe removed");
        Check(capture.Contains("record.AtmosphereOnCampaign") && !capture.Contains("GetAtmosphereModel(") &&
            capture.Contains("AtmosphereTimeOfDay = atmosphere.TimeInfo.TimeOfDay") &&
            capture.Contains("IsInsideStorm = atmosphere.NauticalInfo.IsInsideStorm == 1") &&
            capture.Contains("Vec3 sunDirection = mission.Scene.GetSunDirection();"),
            "native selection inputs and effective direction, not weather lighting guesses");
        Check(capture.Contains("atmosphere.NauticalInfo.CanUseLowAltitudeAtmosphere != 0") &&
            capture.Contains("atmosphere.NauticalInfo.IsInsideStorm != 1"),
            "alternate native selector and noncanonical storm rejected");
        Check(view.Contains("_scene.SetPhotoAtmosphereViaTod(_missionScene.AtmosphereTimeOfDay, _missionScene.IsInsideStorm)") &&
            view.Contains("_scene.SetSunDirection(ref sunDirection)") &&
            !view.Contains("_scene.SetEnvironmentMultiplier(") && !view.Contains("_scene.SetSun(ref sun") &&
            !view.Contains("_scene.SetFog(") && !view.Contains("_scene.SetMinExposure(") &&
            !view.Contains("_scene.SetMaxExposure(") && !view.Contains("_scene.SetMiddleGray("),
            "owned photo preset is not overwritten by calculated weather lighting");
        // Match the branch structurally across LF/CRLF worktrees.
        string normalizedView = view.Replace("\r\n", "\n");
        Check(normalizedView.Contains("if (_missionScene == null)\n            {\n                _scene.SetAtmosphereWithName(state.AtmosphereName);"),
            "named atmosphere remains confined to the original map tableau");
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
        conversation.Tableau.Actors.Add(conversation.Tableau.Actors[0].Clone());
        Check(conversation.Tableau.Actors.Count == 4 &&
            CoopCampaignMapConversationTableauCodec.TrySerialize(conversation.Tableau, out var fourBytes) &&
            CoopCampaignMapConversationTableauCodec.TryDeserialize(fourBytes, out var fourCopy) &&
            CoopCampaignMapConversationTableauCodec.Equivalent(conversation.Tableau, fourCopy),
            "leader, host and two guards round trip");
        var oversized = conversation.Tableau.Clone();
        oversized.Actors.Add(oversized.Actors[0].Clone());
        Check(!CoopCampaignMapConversationTableauCodec.IsValid(oversized), "fifth actor rejected");
        var mapFour = ConversationTableauTests.Fixture();
        mapFour.Actors.Add(mapFour.Actors[0].Clone());
        Check(!CoopCampaignMapConversationTableauCodec.IsValid(mapFour), "map retains three actor limit");
        var pose = ConversationPoseTests.Fixture(conversation);
        Check(!CoopCampaignMapConversationPoseCodec.Matches(pose, conversation), "mission motion requires frames");
        pose.CameraFrame = (float[])conversation.Tableau.CameraFrame.Clone();
        for (int i = 0; i < pose.Actors.Count; i++)
        {
            pose.Actors[i].Frame = (float[])conversation.Tableau.Actors[i].Frame.Clone();
            pose.Actors[i].LookDirection = new[] { 0.6f, 0f, -0.8f };
            pose.Actors[i].ActionName = "act_" + new string('a', 124);
        }
        Check(CoopCampaignMapConversationPoseCodec.Matches(pose, conversation), "complete frame binding");
        var host = new CoopCampaignMapPrototypeHostSnapshot
        {
            SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
            SessionId = "0123456789abcdef0123456789abcdef", Revision = 10, IsActive = true,
            UpdatedUtc = DateTime.UtcNow, Conversation = conversation, ConversationPose = pose
        };
        Check(CoopCampaignMapPrototypeBridgeCodec.TryParse(
            CoopCampaignMapPrototypeBridgeCodec.Serialize(host), out var bridged, out _) &&
            CoopCampaignMapConversationPoseCodec.Matches(bridged.ConversationPose, conversation),
            "maximum four-actor motion crosses host bridge without stale encoded-size cap");
        Check(CoopCampaignMapConversationPoseCodec.TrySerialize(pose, out var bytes) && bytes.Length == 901 &&
            bytes.Length == CoopCampaignMapConversationPoseCodec.MaxPayloadBytes, "maximum four-actor framed pose fits exact bound");
        var fifthPose = pose.Clone(); fifthPose.Actors.Add(fifthPose.Actors[0].Clone());
        Check(!CoopCampaignMapConversationPoseCodec.IsValid(fifthPose), "fifth pose actor rejected");
        Check(CoopCampaignMapConversationPoseCodec.TryDeserialize(bytes, out var copy) &&
            CoopCampaignMapConversationPoseCodec.Matches(copy, conversation), "framed pose round trip");
        Check(CoopCampaignMapConversationPoseCodec.TrySerialize(copy, out var roundtrip) &&
            Convert.ToBase64String(roundtrip) == Convert.ToBase64String(bytes), "all frame coordinates retained");
        var clone = pose.Clone(); clone.CameraFrame[9]++; clone.Actors[0].Frame[10]++;
        clone.Actors[0].LookDirection[0] = 1f;
        Check(pose.Actors[0].LookDirection[0] == 0.6f, "deep gaze clone");
        foreach (var direction in new float[][] { null, new float[2], new float[3],
            new[] { float.NaN, 0f, 1f }, new[] { float.PositiveInfinity, 0f, 1f },
            new[] { 2f, 0f, 0f }, new[] { 0.5f, 0f, 0f } })
        {
            var invalidGaze = pose.Clone(); invalidGaze.Actors[0].LookDirection = direction;
            Check(!CoopCampaignMapConversationPoseCodec.IsValid(invalidGaze), "invalid mission gaze rejected");
        }
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
        bytes[4] = 2;
        Check(!CoopCampaignMapConversationPoseCodec.TryDeserialize(bytes, out _), "three-actor pose schema rejected");
        bytes[4] = 3;
        Check(!CoopCampaignMapConversationPoseCodec.TryDeserialize(bytes, out _), "pre-gaze pose schema rejected");
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
            WindX = -2, WindY = 1,
            AtmosphereTimeOfDay = 2.23680329f, IsInsideStorm = true,
            SunDirectionX = 0, SunDirectionY = 0, SunDirectionZ = 1
        };
        return state;
    }

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException("Conversation mission scene: " + message);
    }
}
