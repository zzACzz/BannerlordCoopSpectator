using System;
using System.IO;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    public static class CoopCampaignMapConversationPayloadCodec
    {
        public const int MaxPayloadBytes = 1048576;
        public const int MaxChunks = MaxPayloadBytes / CoopCampaignMapCatalogChunkCodec.MaxChunkBytes;
        private const int Magic = 0x43445032;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static bool TrySerialize(CoopCampaignMapConversationState state, out byte[] payload)
        {
            payload = null;
            if (!CoopCampaignMapPrototypeContract.TryValidateConversationState(state, out _)) return false;
            try
            {
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream, Utf8, true))
                {
                    writer.Write(Magic);
                    writer.Write(state.Revision);
                    writer.Write(state.IsActive);
                    CoopCampaignMapConversationTableauCodec.WriteText(writer, state.SpeakerName);
                    CoopCampaignMapConversationTableauCodec.WriteText(writer, state.DialogText);
                    CoopCampaignMapConversationTableauCodec.WriteText(writer, state.GoldText);
                    CoopCampaignMapConversationTableauCodec.WriteText(writer, state.ContinueText);
                    writer.Write(state.Options.Count);
                    foreach (var option in state.Options)
                    {
                        CoopCampaignMapConversationTableauCodec.WriteText(writer, option.Text);
                        writer.Write(option.IsEnabled);
                        writer.Write(option.IsSpecial);
                    }
                    if (!CoopConversationHudCodec.TrySerialize(state.Hud, out var hud)) return false;
                    writer.Write(hud.Length);
                    writer.Write(hud);
                    byte[] tableau = Array.Empty<byte>();
                    if (state.Tableau != null &&
                        !CoopCampaignMapConversationTableauCodec.TrySerialize(state.Tableau, out tableau)) return false;
                    writer.Write(tableau.Length);
                    writer.Write(tableau);
                    writer.Flush();
                    if (stream.Length > MaxPayloadBytes) return false;
                    payload = stream.ToArray();
                    return true;
                }
            }
            catch (ArgumentException) { return false; }
        }

        public static bool TryDeserialize(byte[] payload, out CoopCampaignMapConversationState state)
        {
            state = null;
            if (payload == null || payload.Length < 1 || payload.Length > MaxPayloadBytes) return false;
            try
            {
                using (var stream = new MemoryStream(payload, false))
                using (var reader = new BinaryReader(stream, Utf8, true))
                {
                    if (reader.ReadInt32() != Magic) return false;
                    var decoded = new CoopCampaignMapConversationState
                    {
                        Revision = reader.ReadInt32(),
                        IsActive = CoopCampaignMapConversationTableauCodec.ReadBoolean(reader),
                        SpeakerName = CoopCampaignMapConversationTableauCodec.ReadText(reader, CoopCampaignMapPrototypeContract.MaxConversationSpeakerCharacters),
                        DialogText = CoopCampaignMapConversationTableauCodec.ReadText(reader, CoopCampaignMapPrototypeContract.MaxConversationDialogCharacters),
                        GoldText = CoopCampaignMapConversationTableauCodec.ReadText(reader, CoopCampaignMapPrototypeContract.MaxConversationGoldCharacters),
                        ContinueText = CoopCampaignMapConversationTableauCodec.ReadText(reader, CoopCampaignMapPrototypeContract.MaxConversationContinueCharacters)
                    };
                    int count = reader.ReadInt32();
                    if (count < 0 || count > CoopCampaignMapPrototypeContract.MaxConversationOptions) return false;
                    for (int i = 0; i < count; i++) decoded.Options.Add(new CoopCampaignMapConversationOptionState
                    {
                        Text = CoopCampaignMapConversationTableauCodec.ReadText(reader, CoopCampaignMapPrototypeContract.MaxConversationOptionCharacters),
                        IsEnabled = CoopCampaignMapConversationTableauCodec.ReadBoolean(reader),
                        IsSpecial = CoopCampaignMapConversationTableauCodec.ReadBoolean(reader)
                    });
                    int hudSize = reader.ReadInt32();
                    if (hudSize < 5 || hudSize > CoopConversationHudCodec.MaxPayloadBytes ||
                        hudSize > stream.Length - stream.Position ||
                        !CoopConversationHudCodec.TryDeserialize(reader.ReadBytes(hudSize), out var hud)) return false;
                    decoded.Hud = hud;
                    int size = reader.ReadInt32();
                    if (size < 0 || size > CoopCampaignMapConversationTableauCodec.MaxPayloadBytes ||
                        size != stream.Length - stream.Position) return false;
                    if (size > 0)
                    {
                        if (!CoopCampaignMapConversationTableauCodec.TryDeserialize(reader.ReadBytes(size), out var tableau)) return false;
                        decoded.Tableau = tableau;
                    }
                    if (!CoopCampaignMapPrototypeContract.TryValidateConversationState(decoded, out _)) return false;
                    state = decoded;
                    return true;
                }
            }
            catch (InvalidDataException) { return false; }
            catch (IOException) { return false; }
            catch (ArgumentException) { return false; }
        }

        public static bool IsValidEnvelope(int revision, int logicalBytes, int wireBytes, int count,
            CoopCampaignMapCatalogCompressionKind compression, string hash, int index, byte[] chunk)
        {
            return revision >= 0 && logicalBytes > 0 && logicalBytes <= MaxPayloadBytes &&
                wireBytes > 0 && wireBytes <= MaxPayloadBytes && count > 0 && count <= MaxChunks &&
                count == (wireBytes + 255) / 256 && index >= 0 && index < count &&
                (compression == CoopCampaignMapCatalogCompressionKind.None || compression == CoopCampaignMapCatalogCompressionKind.Gzip) &&
                CoopCampaignMapCatalogChunkCodec.IsValidHash(hash) && chunk != null &&
                chunk.Length == (index == count - 1 ? wireBytes - (count - 1) * 256 : 256);
        }
    }

    public sealed class CoopCampaignMapConversationPayloadAssembler
    {
        private CoopCampaignMapCatalogChunkAccumulator _active;
        private int _latestRevision = -1;
        private int _completedRevision = -1;

        public bool TryAccept(int revision, int logicalBytes, int wireBytes, int count,
            CoopCampaignMapCatalogCompressionKind compression, string hash, int index, byte[] chunk,
            out CoopCampaignMapConversationState state)
        {
            state = null;
            if (revision < _latestRevision || revision <= _completedRevision ||
                !CoopCampaignMapConversationPayloadCodec.IsValidEnvelope(revision, logicalBytes,
                    wireBytes, count, compression, hash, index, chunk)) return false;
            if (_active == null || revision > _latestRevision)
            {
                if (!CoopCampaignMapCatalogChunkAccumulator.TryCreate(1, revision, logicalBytes,
                    wireBytes, count, compression, hash, out var accumulator, out _)) return false;
                _active = accumulator;
                _latestRevision = revision;
            }
            if (_active.LogicalByteCount != logicalBytes || _active.WireByteCount != wireBytes ||
                _active.ChunkCount != count || _active.CompressionKind != compression ||
                !string.Equals(_active.PayloadHash, hash, StringComparison.Ordinal) ||
                !_active.TryAccept(index, count, chunk, out _)) return false;
            if (!_active.IsComplete) return true;
            if (!_active.TryComplete(out var bytes, out _) ||
                !CoopCampaignMapConversationPayloadCodec.TryDeserialize(bytes, out var decoded) ||
                decoded.Revision != revision)
            {
                _active = null;
                return false;
            }
            _completedRevision = revision;
            _active = null;
            state = decoded;
            return true;
        }

        public void Reset()
        {
            _active = null;
            _latestRevision = -1;
            _completedRevision = -1;
        }
    }
}
