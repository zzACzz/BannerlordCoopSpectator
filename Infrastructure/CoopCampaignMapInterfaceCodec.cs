using System;
using System.IO;

namespace CoopSpectator.Infrastructure
{
    // One atomic client presentation: menu and bottom bar cannot complete separately.
    public static class CoopCampaignMapInterfaceCodec
    {
        private const int Magic = 0x434D4931; // CMI1
        public static bool TryEncode(int revision, CoopCampaignMapMenuPresentation menu,
            CoopCampaignMapBarPresentation bar, out byte[] bytes)
        {
            bytes = null;
            if (!CoopCampaignMapMenuCodec.TrySerialize(revision, menu, out var menuBytes) ||
                !CoopCampaignMapBarCodec.TryEncode(revision, bar, out var barBytes) ||
                12L + menuBytes.Length + barBytes.Length > CoopCampaignMapConversationPayloadCodec.MaxPayloadBytes) return false;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic); writer.Write(menuBytes.Length); writer.Write(menuBytes);
                writer.Write(barBytes.Length); writer.Write(barBytes);
                bytes = stream.ToArray(); return true;
            }
        }

        public static bool TryDecode(byte[] bytes, int revision, out CoopCampaignMapMenuPresentation menu,
            out CoopCampaignMapBarPresentation bar)
        {
            menu = null; bar = null;
            if (revision <= 0 || bytes == null || bytes.Length < 38 ||
                bytes.Length > CoopCampaignMapConversationPayloadCodec.MaxPayloadBytes) return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream))
                {
                    if (reader.ReadInt32() != Magic) return false;
                    var menuBytes = ReadPart(reader);
                    var barBytes = ReadPart(reader);
                    if (stream.Position != stream.Length ||
                        !CoopCampaignMapMenuCodec.TryDeserialize(menuBytes, out var menuRevision, out var decodedMenu) ||
                        menuRevision != revision || !CoopCampaignMapBarCodec.TryDecode(barBytes, revision, out var decodedBar)) return false;
                    menu = decodedMenu; bar = decodedBar; return true;
                }
            }
            catch (IOException) { return false; }
            catch (InvalidDataException) { return false; }
            catch (ArgumentException) { return false; }
        }
        private static byte[] ReadPart(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 13 || count > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException();
            return reader.ReadBytes(count);
        }
    }

    public sealed class CoopCampaignMapInterfaceAssembler
    {
        private CoopCampaignMapCatalogChunkAccumulator _active;
        private int _latest, _completed;
        public bool TryAccept(int revision, int logicalBytes, int wireBytes, int count,
            CoopCampaignMapCatalogCompressionKind compression, string hash, int index, byte[] chunk,
            out bool completed, out CoopCampaignMapMenuPresentation menu, out CoopCampaignMapBarPresentation bar)
        {
            completed = false; menu = null; bar = null;
            if (revision <= 0 || revision < _latest || revision <= _completed ||
                !CoopCampaignMapConversationPayloadCodec.IsValidEnvelope(revision, logicalBytes, wireBytes,
                    count, compression, hash, index, chunk)) return false;
            if (_active == null || revision > _latest)
            {
                if (!CoopCampaignMapCatalogChunkAccumulator.TryCreate(1, revision, logicalBytes,
                    wireBytes, count, compression, hash, out var next, out _)) return false;
                _active = next; _latest = revision;
            }
            if (_active.LogicalByteCount != logicalBytes || _active.WireByteCount != wireBytes ||
                _active.ChunkCount != count || _active.CompressionKind != compression ||
                !string.Equals(_active.PayloadHash, hash, StringComparison.Ordinal) ||
                !_active.TryAccept(index, count, chunk, out _)) return false;
            if (!_active.IsComplete) return true;
            if (!_active.TryComplete(out var bytes, out _) ||
                !CoopCampaignMapInterfaceCodec.TryDecode(bytes, revision, out var decodedMenu, out var decodedBar))
            { _active = null; return false; }
            _completed = revision; _active = null;
            menu = decodedMenu; bar = decodedBar; completed = true; return true;
        }
        public void Reset() { _active = null; _latest = 0; _completed = 0; }
    }
}
