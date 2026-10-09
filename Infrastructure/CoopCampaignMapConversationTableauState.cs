using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    // Detached presentation data only. No campaign objects, scene paths or commands.
    public sealed class CoopCampaignMapConversationTableauState
    {
        public string AtmosphereName { get; set; } = string.Empty;
        public bool IsRaining { get; set; }
        public bool IsSnowing { get; set; }
        public CoopCampaignMapConversationMissionScene MissionScene { get; set; }
        // s, f, u, origin; four consecutive xyz vectors in native frame order.
        public float[] CameraFrame { get; set; }
        public float BaseCameraHorizontalFov { get; set; }
        public List<CoopCampaignMapConversationActorState> Actors { get; set; } =
            new List<CoopCampaignMapConversationActorState>();

        public CoopCampaignMapConversationTableauState Clone()
        {
            var copy = (CoopCampaignMapConversationTableauState)MemberwiseClone();
            copy.CameraFrame = CameraFrame == null ? null : (float[])CameraFrame.Clone();
            copy.MissionScene = MissionScene?.Clone();
            copy.Actors = Actors == null ? null : new List<CoopCampaignMapConversationActorState>();
            if (Actors != null)
                foreach (var actor in Actors) copy.Actors.Add(actor?.Clone());
            return copy;
        }
    }

    public sealed class CoopCampaignMapConversationActorState
    {
        public CoopCampaignMapPrototypeAgentVisualState Visual { get; set; }
        public float[] Frame { get; set; }
        public float Scale { get; set; } = 1f;
        public uint ClothColor1 { get; set; }
        public uint ClothColor2 { get; set; }
        public string BannerCode { get; set; } = string.Empty;
        public string ActionSetName { get; set; } = string.Empty;
        public string InitialActionName { get; set; } = string.Empty;

        public CoopCampaignMapConversationActorState Clone()
        {
            var copy = (CoopCampaignMapConversationActorState)MemberwiseClone();
            copy.Visual = Visual?.Clone();
            copy.Frame = Frame == null ? null : (float[])Frame.Clone();
            return copy;
        }
    }

    // Revision-bound visual transfer. Limits are checked before the shared catalog
    // accumulator allocates or decompresses anything. Mission reset must call Reset.
    public sealed class CoopCampaignMapConversationTableauAssembler
    {
        private CoopCampaignMapCatalogChunkAccumulator _active;
        private int _latestRevision = -1;
        private int _completedRevision = -1;

        public bool TryAccept(int revision, int logicalBytes, int wireBytes, int chunkCount,
            CoopCampaignMapCatalogCompressionKind compression, string hash, int chunkIndex,
            byte[] chunk, out CoopCampaignMapConversationTableauState completed)
        {
            completed = null;
            int maximumBytes = CoopCampaignMapConversationTableauCodec.MaxPayloadBytes;
            if (revision < 0 || revision <= _completedRevision || revision < _latestRevision ||
                logicalBytes < 1 || logicalBytes > maximumBytes || wireBytes < 1 || wireBytes > maximumBytes ||
                chunkCount < 1 || chunkCount > maximumBytes / CoopCampaignMapCatalogChunkCodec.MaxChunkBytes ||
                chunkIndex < 0 || chunkIndex >= chunkCount || chunk == null ||
                chunk.Length != (chunkIndex == chunkCount - 1
                    ? wireBytes - (chunkCount - 1) * CoopCampaignMapCatalogChunkCodec.MaxChunkBytes
                    : CoopCampaignMapCatalogChunkCodec.MaxChunkBytes))
                return false;

            if (_active == null || revision > _latestRevision)
            {
                if (!CoopCampaignMapCatalogChunkAccumulator.TryCreate(1, revision, logicalBytes,
                    wireBytes, chunkCount, compression, hash, out var candidate, out _))
                    return false;
                _active = candidate;
                _latestRevision = revision;
            }
            if (_active.LogicalByteCount != logicalBytes || _active.WireByteCount != wireBytes ||
                _active.ChunkCount != chunkCount || _active.CompressionKind != compression ||
                !string.Equals(_active.PayloadHash, hash, StringComparison.Ordinal))
                return false;
            if (!_active.TryAccept(chunkIndex, chunkCount, chunk, out _)) return false;
            if (!_active.IsComplete) return true;
            if (!_active.TryComplete(out byte[] bytes, out _) ||
                !CoopCampaignMapConversationTableauCodec.TryDeserialize(bytes, out completed))
            {
                _active = null;
                return false;
            }
            _completedRevision = revision;
            _active = null;
            return true;
        }

        // Close/invalidation is also a revision barrier: delayed open chunks cannot
        // resurrect a scene after the authoritative dialogue has closed.
        public void DiscardThrough(int revision)
        {
            if (revision < 0 || revision < _latestRevision) return;
            _latestRevision = revision;
            _completedRevision = revision;
            _active = null;
        }

        public void Reset()
        {
            _active = null;
            _latestRevision = -1;
            _completedRevision = -1;
        }
    }

    public static class CoopCampaignMapConversationTableauCodec
    {
        public const int SchemaVersion = 2;
        public const int MaxActors = 3;
        public const int MaxPayloadBytes = 65536;
        public const int MaxIdentifierCharacters = 128;
        public const string SceneName = "scn_conversation_tableau";
        private const int Magic = 0x43565431;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static bool IsValid(CoopCampaignMapConversationTableauState state)
        {
            if (state == null || !IsIdentifier(state.AtmosphereName, false) ||
                (state.MissionScene == null
                    ? !state.AtmosphereName.StartsWith("conv_", StringComparison.Ordinal)
                    : !CoopCampaignMapConversationMissionScene.IsValid(state.MissionScene)) ||
                !IsFrame(state.CameraFrame) || !InRange(state.BaseCameraHorizontalFov, 0.1f, 3f) ||
                state.Actors == null || state.Actors.Count < 1 || state.Actors.Count > MaxActors)
                return false;
            foreach (var actor in state.Actors)
            {
                if (actor == null || actor.Visual == null ||
                    !CoopCampaignMapPrototypeContract.IsValidAgentVisualState(actor.Visual, true) ||
                    !IsFrame(actor.Frame) || !InRange(actor.Scale, 0.1f, 4f) ||
                    !IsIdentifier(actor.ActionSetName, false) ||
                    !IsIdentifier(actor.InitialActionName, false) ||
                    !actor.InitialActionName.StartsWith("act_", StringComparison.Ordinal) ||
                    !IsBanner(actor.BannerCode) ||
                    actor.Visual.HasBanner != (actor.BannerCode.Length != 0) ||
                    !string.IsNullOrEmpty(actor.Visual.MountCreationKey))
                    return false;
                foreach (string item in actor.Visual.EquipmentItemIds)
                    if (!IsIdentifier(item, true)) return false;
            }
            return true;
        }

        public static bool TrySerialize(CoopCampaignMapConversationTableauState state, out byte[] bytes)
        {
            bytes = null;
            if (!IsValid(state)) return false;
            try
            {
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream, Utf8, true))
                {
                    writer.Write(Magic);
                    writer.Write(SchemaVersion);
                    CoopCampaignMapConversationMissionScene.Write(writer, state.MissionScene);
                    WriteText(writer, state.AtmosphereName);
                    writer.Write(state.IsRaining);
                    writer.Write(state.IsSnowing);
                    WriteFrame(writer, state.CameraFrame);
                    writer.Write(state.BaseCameraHorizontalFov);
                    writer.Write(state.Actors.Count);
                    foreach (var actor in state.Actors)
                    {
                        WriteFrame(writer, actor.Frame);
                        writer.Write(actor.Scale);
                        writer.Write(actor.ClothColor1);
                        writer.Write(actor.ClothColor2);
                        WriteText(writer, actor.BannerCode);
                        WriteText(writer, actor.ActionSetName);
                        WriteText(writer, actor.InitialActionName);
                        var visual = actor.Visual;
                        WriteText(writer, visual.BodyProperties);
                        writer.Write(visual.IsFemale);
                        writer.Write(visual.Race);
                        writer.Write(visual.SkeletonType);
                        writer.Write(visual.RightWieldedItemIndex);
                        writer.Write(visual.LeftWieldedItemIndex);
                        writer.Write(visual.HasBanner);
                        writer.Write(visual.AddColorRandomness);
                        foreach (string item in visual.EquipmentItemIds) WriteText(writer, item);
                    }
                    writer.Flush();
                    if (stream.Length > MaxPayloadBytes) return false;
                    bytes = stream.ToArray();
                    return true;
                }
            }
            catch (EncoderFallbackException) { return false; }
        }

        public static bool TryDeserialize(byte[] bytes, out CoopCampaignMapConversationTableauState state)
        {
            state = null;
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxPayloadBytes) return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Utf8, true))
                {
                    if (reader.ReadInt32() != Magic || reader.ReadInt32() != SchemaVersion) return false;
                    var decoded = new CoopCampaignMapConversationTableauState
                    {
                        MissionScene = CoopCampaignMapConversationMissionScene.Read(reader),
                        AtmosphereName = ReadText(reader, MaxIdentifierCharacters),
                        IsRaining = ReadBoolean(reader),
                        IsSnowing = ReadBoolean(reader),
                        CameraFrame = ReadFrame(reader),
                        BaseCameraHorizontalFov = reader.ReadSingle()
                    };
                    int count = reader.ReadInt32();
                    if (count < 1 || count > MaxActors) return false;
                    for (int index = 0; index < count; index++)
                    {
                        var actor = new CoopCampaignMapConversationActorState
                        {
                            Frame = ReadFrame(reader),
                            Scale = reader.ReadSingle(),
                            ClothColor1 = reader.ReadUInt32(),
                            ClothColor2 = reader.ReadUInt32(),
                            BannerCode = ReadText(reader, CoopCampaignMapPrototypeContract.MaxBannerCodeCharacters),
                            ActionSetName = ReadText(reader, MaxIdentifierCharacters),
                            InitialActionName = ReadText(reader, MaxIdentifierCharacters),
                            Visual = new CoopCampaignMapPrototypeAgentVisualState
                            {
                                BodyProperties = ReadText(reader, CoopCampaignMapPrototypeContract.MaxBodyPropertiesCharacters),
                                IsFemale = ReadBoolean(reader),
                                Race = reader.ReadInt32(),
                                SkeletonType = reader.ReadInt32(),
                                RightWieldedItemIndex = reader.ReadInt32(),
                                LeftWieldedItemIndex = reader.ReadInt32(),
                                HasBanner = ReadBoolean(reader),
                                AddColorRandomness = ReadBoolean(reader),
                                MountCreationKey = string.Empty
                            }
                        };
                        for (int slot = 0; slot < actor.Visual.EquipmentItemIds.Length; slot++)
                            actor.Visual.EquipmentItemIds[slot] = ReadText(reader, CoopCampaignMapPrototypeContract.MaxVisualItemIdCharacters);
                        decoded.Actors.Add(actor);
                    }
                    if (stream.Position != stream.Length || !IsValid(decoded)) return false;
                    state = decoded;
                    return true;
                }
            }
            catch (InvalidDataException) { return false; }
            catch (IOException) { return false; }
            catch (ArgumentException) { return false; }
        }

        public static bool Equivalent(CoopCampaignMapConversationTableauState left, CoopCampaignMapConversationTableauState right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (!TrySerialize(left, out byte[] a) || !TrySerialize(right, out byte[] b) || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static bool IsIdentifier(string value, bool allowEmpty)
        {
            if (value == null || value.Length > MaxIdentifierCharacters || (!allowEmpty && value.Length == 0)) return false;
            foreach (char c in value)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                    !(c >= '0' && c <= '9') && c != '_') return false;
            return true;
        }

        private static bool IsBanner(string value)
        {
            if (value == null || value.Length > CoopCampaignMapPrototypeContract.MaxBannerCodeCharacters) return false;
            foreach (char c in value) if ((c < '0' || c > '9') && c != '.' && c != '-') return false;
            return true;
        }

        private static bool InRange(float value, float min, float max) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;

        internal static bool IsFrame(float[] frame)
        {
            if (frame == null || frame.Length != 12) return false;
            for (int i = 0; i < 12; i++) if (!InRange(frame[i], i < 9 ? -1.02f : -10000f, i < 9 ? 1.02f : 10000f)) return false;
            // Native frame axes are right-handed and unit length. Scale is separate.
            for (int axis = 0; axis < 3; axis++)
            {
                double norm = 0;
                for (int i = 0; i < 3; i++) norm += frame[axis * 3 + i] * frame[axis * 3 + i];
                if (Math.Abs(norm - 1d) > 0.04d) return false;
                for (int other = axis + 1; other < 3; other++)
                {
                    double dot = 0;
                    for (int i = 0; i < 3; i++) dot += frame[axis * 3 + i] * frame[other * 3 + i];
                    if (Math.Abs(dot) > 0.02d) return false;
                }
            }
            double determinant = frame[0] * (frame[4] * frame[8] - frame[5] * frame[7]) -
                frame[1] * (frame[3] * frame[8] - frame[5] * frame[6]) +
                frame[2] * (frame[3] * frame[7] - frame[4] * frame[6]);
            return determinant > 0.94d && determinant < 1.06d;
        }

        internal static void WriteFrame(BinaryWriter writer, float[] frame)
        { foreach (float value in frame) writer.Write(value); }
        internal static float[] ReadFrame(BinaryReader reader)
        {
            var frame = new float[12];
            for (int i = 0; i < frame.Length; i++) frame[i] = reader.ReadSingle();
            return frame;
        }
        internal static bool ReadBoolean(BinaryReader reader)
        {
            byte value = reader.ReadByte();
            if (value > 1) throw new InvalidDataException("Non-canonical boolean.");
            return value != 0;
        }
        internal static void WriteText(BinaryWriter writer, string text)
        {
            byte[] bytes = Utf8.GetBytes(text);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }
        internal static string ReadText(BinaryReader reader, int maxCharacters)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > maxCharacters * 4 || count > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("String size.");
            string value = Utf8.GetString(reader.ReadBytes(count));
            if (value.Length > maxCharacters) throw new InvalidDataException("String length.");
            return value;
        }
    }
}
