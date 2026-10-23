using System;
using System.IO;

namespace CoopSpectator.Infrastructure
{
    public static class CoopCampaignMapPartyPoseCodec
    {
        private const int Magic = 0x434D5032; // CMP2 includes campaign clock scale
        public const int MaximumBytes = 24 + 3 * CoopCampaignMapPartyAnimationCodec.MaximumBytes;

        public static bool TryEncode(CoopCampaignMapPartyPose pose, out byte[] bytes)
        {
            bytes = null;
            if (pose == null || !pose.IsValid()) return false;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic); writer.Write(pose.AppearanceRevision);
                writer.Write(pose.TimeScale);
                foreach (var sample in new[] { pose.Human, pose.Mount, pose.Caravan })
                {
                    if (sample == null) { writer.Write(0); continue; }
                    if (!CoopCampaignMapPartyAnimationCodec.TryEncode(sample, out var encoded)) return false;
                    writer.Write(encoded.Length); writer.Write(encoded);
                }
                writer.Flush(); bytes = stream.ToArray(); return true;
            }
        }

        public static bool TryDecode(byte[] bytes, out CoopCampaignMapPartyPose pose)
        {
            pose = null;
            if (bytes == null || bytes.Length < 41 || bytes.Length > MaximumBytes) return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream))
                {
                    if (reader.ReadInt32() != Magic) return false;
                    int revision = reader.ReadInt32();
                    float timeScale = reader.ReadSingle();
                    var samples = new CoopCampaignMapPartyAnimation[3];
                    for (int i = 0; i < samples.Length; i++)
                    {
                        int length = reader.ReadInt32();
                        if (length == 0) continue;
                        if (length < 0 || length > CoopCampaignMapPartyAnimationCodec.MaximumBytes ||
                            length > stream.Length - stream.Position ||
                            !CoopCampaignMapPartyAnimationCodec.TryDecode(reader.ReadBytes(length), out samples[i])) return false;
                    }
                    var value = new CoopCampaignMapPartyPose { AppearanceRevision = revision,
                        TimeScale = timeScale,
                        Human = samples[0], Mount = samples[1], Caravan = samples[2] };
                    if (stream.Position != stream.Length || !value.IsValid()) return false;
                    pose = value; return true;
                }
            }
            catch (IOException) { return false; }
            catch (ArgumentException) { return false; }
        }
    }
}
