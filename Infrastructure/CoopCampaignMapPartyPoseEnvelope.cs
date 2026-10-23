using System;
using System.IO;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    // Local host-file transport only. Native networking sends individual CPE1 frames.
    public static class CoopCampaignMapPartyPoseBatchCodec
    {
        public const int MaximumCount = 64;
        public const int MaximumCharacters = MaximumCount *
            (((CoopCampaignMapPartyPoseEnvelopeCodec.MaximumBytes + 2) / 3) * 4 + 1);

        public static bool IsBound(CoopCampaignMapPartyPoseEnvelope[] values, string session,
            int revision, int catalogRevision, bool conversationActive)
        {
            if (values == null || values.Length > MaximumCount ||
                (conversationActive && values.Length != 0)) return false;
            var ids = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in values)
                if (value?.IsValid() != true || value.Sequence != revision ||
                    !string.Equals(value.SessionId, session, StringComparison.Ordinal) ||
                    value.Pose.AppearanceRevision != catalogRevision || !ids.Add(value.EntityId)) return false;
            return true;
        }

        public static bool TryEncode(CoopCampaignMapPartyPoseEnvelope[] values, out string encoded)
        {
            encoded = null;
            if (values == null || values.Length > MaximumCount) return false;
            var parts = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                if (!CoopCampaignMapPartyPoseEnvelopeCodec.TryEncode(values[i], out var bytes)) return false;
                parts[i] = Convert.ToBase64String(bytes);
            }
            encoded = string.Join(";", parts); return true;
        }

        public static bool TryDecode(string encoded, out CoopCampaignMapPartyPoseEnvelope[] values)
        {
            values = null;
            if (encoded == null || encoded.Length > MaximumCharacters) return false;
            if (encoded.Length == 0) { values = Array.Empty<CoopCampaignMapPartyPoseEnvelope>(); return true; }
            // Bound the element count before allocating the split array.
            int count = 1;
            foreach (char c in encoded) if (c == ';' && ++count > MaximumCount) return false;
            string[] parts = encoded.Split(';');
            var decoded = new CoopCampaignMapPartyPoseEnvelope[parts.Length];
            try
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i].Length > ((CoopCampaignMapPartyPoseEnvelopeCodec.MaximumBytes + 2) / 3) * 4) return false;
                    byte[] bytes = Convert.FromBase64String(parts[i]);
                    if (Convert.ToBase64String(bytes) != parts[i] ||
                        !CoopCampaignMapPartyPoseEnvelopeCodec.TryDecode(bytes, out decoded[i])) return false;
                }
            }
            catch (FormatException) { return false; }
            values = decoded; return true;
        }

        public static CoopCampaignMapPartyPoseEnvelope[] Clone(CoopCampaignMapPartyPoseEnvelope[] values)
        {
            if (values == null) return null;
            var result = new CoopCampaignMapPartyPoseEnvelope[values.Length];
            for (int i = 0; i < values.Length; i++) result[i] = values[i]?.Clone();
            return result;
        }
    }

    public sealed class CoopCampaignMapPartyPoseEnvelope
    {
        public string SessionId { get; set; }
        public string EntityId { get; set; }
        public int Sequence { get; set; }
        public CoopCampaignMapPartyPose Pose { get; set; }
        public bool IsValid() => Guid.TryParseExact(SessionId, "N", out var session) &&
            session != Guid.Empty && Sequence >= 0 &&
            IsEntityId(EntityId) && Pose?.IsValid() == true;
        private static bool IsEntityId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > CoopCampaignMapPrototypeContract.MaxEntityIdCharacters) return false;
            foreach (char c in value) if (char.IsControl(c)) return false;
            try { new UTF8Encoding(false, true).GetByteCount(value); return true; }
            catch (ArgumentException) { return false; }
        }
        public CoopCampaignMapPartyPoseEnvelope Clone() => new CoopCampaignMapPartyPoseEnvelope {
            SessionId = SessionId, EntityId = EntityId, Sequence = Sequence, Pose = Pose?.Clone() };

        public bool Matches(string session, string entity, int lastSequence, int appearanceRevision,
            bool disclosed, bool hasHuman, bool hasMount, bool hasCaravan) =>
            IsValid() && disclosed && Sequence > lastSequence &&
            string.Equals(SessionId, session, StringComparison.Ordinal) &&
            string.Equals(EntityId, entity, StringComparison.OrdinalIgnoreCase) &&
            Pose.MatchesAppearance(appearanceRevision, hasHuman, hasMount, hasCaravan);
    }

    public static class CoopCampaignMapPartyPoseEnvelopeCodec
    {
        private const int Magic = 0x43504531; // CPE1
        public const int MaximumBytes = 32 + 4 * CoopCampaignMapPrototypeContract.MaxEntityIdCharacters +
            CoopCampaignMapPartyPoseCodec.MaximumBytes;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        public static bool TryEncode(CoopCampaignMapPartyPoseEnvelope value, out byte[] bytes)
        {
            bytes = null;
            if (value?.IsValid() != true || !CoopCampaignMapPartyPoseCodec.TryEncode(value.Pose, out var pose)) return false;
            try
            {
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream, Utf8, true))
                {
                    writer.Write(Magic); writer.Write(Guid.ParseExact(value.SessionId, "N").ToByteArray());
                    writer.Write(value.Sequence);
                    var id = Utf8.GetBytes(value.EntityId); writer.Write(id.Length); writer.Write(id);
                    writer.Write(pose.Length); writer.Write(pose); writer.Flush();
                    if (stream.Length > MaximumBytes) return false;
                    bytes = stream.ToArray(); return true;
                }
            }
            catch (ArgumentException) { return false; }
        }
        public static bool TryDecode(byte[] bytes, out CoopCampaignMapPartyPoseEnvelope value)
        {
            value = null;
            if (bytes == null || bytes.Length < 74 || bytes.Length > MaximumBytes) return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Utf8, true))
                {
                    if (reader.ReadInt32() != Magic) return false;
                    string session = new Guid(reader.ReadBytes(16)).ToString("N");
                    int sequence = reader.ReadInt32();
                    int length = reader.ReadInt32();
                    if (length < 1 || length > 4 * CoopCampaignMapPrototypeContract.MaxEntityIdCharacters ||
                        length > stream.Length - stream.Position - 4) return false;
                    string entity = Utf8.GetString(reader.ReadBytes(length));
                    int poseLength = reader.ReadInt32();
                    if (poseLength < 1 || poseLength > CoopCampaignMapPartyPoseCodec.MaximumBytes ||
                        poseLength != stream.Length - stream.Position ||
                        !CoopCampaignMapPartyPoseCodec.TryDecode(reader.ReadBytes(poseLength), out var pose)) return false;
                    var decoded = new CoopCampaignMapPartyPoseEnvelope {
                        SessionId = session, EntityId = entity, Sequence = sequence, Pose = pose };
                    if (!decoded.IsValid()) return false;
                    value = decoded; return true;
                }
            }
            catch (ArgumentException) { return false; }
            catch (IOException) { return false; }
        }
    }
}
