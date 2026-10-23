using System;
using System.IO;

namespace CoopSpectator.Infrastructure
{
    // Detached land-conversation scene projection. It never names arbitrary
    // module paths or enables physics, mission behaviors, or campaign objects.
    public sealed class CoopCampaignMapConversationMissionScene
    {
        public string SceneName { get; set; } = string.Empty;
        public float CameraVerticalFov { get; set; }
        public float CameraNear { get; set; }
        public float CameraFar { get; set; }
        public float TimeOfDay { get; set; }
        public float AtmosphereTimeOfDay { get; set; }
        public bool IsInsideStorm { get; set; }
        public float SunDirectionX { get; set; }
        public float SunDirectionY { get; set; }
        public float SunDirectionZ { get; set; }
        public float Winter { get; set; }
        public float Dryness { get; set; }
        public float Rain { get; set; }
        public float Snow { get; set; }
        public float Temperature { get; set; }
        public float Humidity { get; set; }
        public float WindX { get; set; }
        public float WindY { get; set; }

        public CoopCampaignMapConversationMissionScene Clone() =>
            (CoopCampaignMapConversationMissionScene)MemberwiseClone();

        public static bool IsAllowedScene(string name) =>
            name == "conversation_scene_green_valley" || name == "conversation_scene_forest" ||
            name == "conversation_scene_steppe" || name == "conversation_scene_desert";

        // The installed weather model fills InterpolatedAtmosphereName without
        // AtmosphereName. AtmosphereInfo.IsValid checks only the latter, so it is
        // not an admission rule for a detached campaign-weather preset.
        public static bool TryResolveAtmosphereName(string named, string interpolated, out string name)
        {
            name = string.IsNullOrEmpty(named) ? interpolated : named;
            if (string.IsNullOrEmpty(name) || name.Length > CoopCampaignMapConversationTableauCodec.MaxIdentifierCharacters)
            { name = null; return false; }
            foreach (char c in name)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                    !(c >= '0' && c <= '9') && c != '_')
                { name = null; return false; }
            return true;
        }

        public static bool IsValid(CoopCampaignMapConversationMissionScene scene) =>
            scene != null && IsAllowedScene(scene.SceneName) &&
            Range(scene.CameraVerticalFov, 0.05f, 3f) && Range(scene.CameraNear, 0.01f, 10f) &&
            Range(scene.CameraFar, 10f, 20000f) && scene.CameraFar > scene.CameraNear &&
            Range(scene.TimeOfDay, 0f, 24f) && Range(scene.AtmosphereTimeOfDay, 0f, 24f) &&
            Range(scene.SunDirectionX, -1f, 1f) && Range(scene.SunDirectionY, -1f, 1f) &&
            Range(scene.SunDirectionZ, -1f, 1f) &&
            Range(scene.SunDirectionX * scene.SunDirectionX + scene.SunDirectionY * scene.SunDirectionY +
                scene.SunDirectionZ * scene.SunDirectionZ, 0.99f, 1.01f) && Range(scene.Winter, 0f, 1f) &&
            Range(scene.Dryness, 0f, 1f) && Range(scene.Rain, 0f, 1f) && Range(scene.Snow, 0f, 1f) &&
            Range(scene.Temperature, -100f, 100f) && Range(scene.Humidity, 0f, 100f) &&
            Range(scene.WindX, -100f, 100f) && Range(scene.WindY, -100f, 100f);

        internal static void Write(BinaryWriter writer, CoopCampaignMapConversationMissionScene scene)
        {
            writer.Write(scene != null);
            if (scene == null) return;
            CoopCampaignMapConversationTableauCodec.WriteText(writer, scene.SceneName);
            writer.Write(scene.CameraVerticalFov); writer.Write(scene.CameraNear); writer.Write(scene.CameraFar);
            writer.Write(scene.TimeOfDay); writer.Write(scene.Winter); writer.Write(scene.Dryness);
            writer.Write(scene.Rain); writer.Write(scene.Snow); writer.Write(scene.Temperature);
            writer.Write(scene.Humidity); writer.Write(scene.WindX); writer.Write(scene.WindY);
            writer.Write(scene.AtmosphereTimeOfDay); writer.Write(scene.IsInsideStorm);
            writer.Write(scene.SunDirectionX); writer.Write(scene.SunDirectionY); writer.Write(scene.SunDirectionZ);
        }

        internal static CoopCampaignMapConversationMissionScene Read(BinaryReader reader)
        {
            if (!CoopCampaignMapConversationTableauCodec.ReadBoolean(reader)) return null;
            var scene = new CoopCampaignMapConversationMissionScene
            {
                SceneName = CoopCampaignMapConversationTableauCodec.ReadText(reader, 64),
                CameraVerticalFov = reader.ReadSingle(), CameraNear = reader.ReadSingle(), CameraFar = reader.ReadSingle(),
                TimeOfDay = reader.ReadSingle(), Winter = reader.ReadSingle(), Dryness = reader.ReadSingle(),
                Rain = reader.ReadSingle(), Snow = reader.ReadSingle(), Temperature = reader.ReadSingle(),
                Humidity = reader.ReadSingle(), WindX = reader.ReadSingle(), WindY = reader.ReadSingle(),
                AtmosphereTimeOfDay = reader.ReadSingle(), IsInsideStorm = CoopCampaignMapConversationTableauCodec.ReadBoolean(reader),
                SunDirectionX = reader.ReadSingle(), SunDirectionY = reader.ReadSingle(), SunDirectionZ = reader.ReadSingle()
            };
            if (!IsValid(scene)) throw new InvalidDataException("Invalid conversation mission scene.");
            return scene;
        }

        private static bool Range(float value, float min, float max) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
    }
}
