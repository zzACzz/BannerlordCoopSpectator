using System;
using System.IO;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    public sealed class CoopCampaignMapTooltipChunk
    {
        public Guid Lease;
        public long Generation, Sequence;
        public int LogicalBytes, WireBytes, Count, Index;
        public CoopCampaignMapCatalogCompressionKind Compression;
        public string Hash;
        public byte[] Bytes;
    }
    public static class CoopCampaignMapTooltipChunks
    {
        public const int MaxPacketBytes = 512;
        public const int MaxBytes = CoopCampaignMapTooltipWireCodec.MaxEnvelopeBytes;
        public const int MaxCount = (MaxBytes + 255) / 256;
        private const int Magic = 0x43544331;

        public static CoopCampaignMapTooltipChunk Create(Guid lease, long generation, long sequence,
            CoopCampaignMapCatalogChunkedPayload payload, int index) => new CoopCampaignMapTooltipChunk {
                Lease = lease, Generation = generation, Sequence = sequence, LogicalBytes = payload.LogicalByteCount,
                WireBytes = payload.WireByteCount, Count = payload.ChunkCount, Index = index,
                Compression = payload.CompressionKind, Hash = payload.PayloadHash, Bytes = payload.Chunks[index] };

        public static bool IsValid(CoopCampaignMapTooltipChunk value)
        {
            if (value == null || value.Lease == Guid.Empty || value.Generation <= 0 || value.Sequence <= 0 ||
                value.LogicalBytes < 1 || value.LogicalBytes > MaxBytes || value.WireBytes < 1 || value.WireBytes > MaxBytes ||
                value.Count < 1 || value.Count > MaxCount || value.Count != (value.WireBytes + 255) / 256 ||
                value.Index < 0 || value.Index >= value.Count || value.Bytes == null ||
                value.Bytes.Length != (value.Index == value.Count - 1 ? value.WireBytes - (value.Count - 1) * 256 : 256) ||
                !CoopCampaignMapCatalogChunkCodec.IsValidHash(value.Hash)) return false;
            return value.Compression == CoopCampaignMapCatalogCompressionKind.Gzip ||
                (value.Compression == CoopCampaignMapCatalogCompressionKind.None && value.LogicalBytes == value.WireBytes);
        }
        public static bool TryEncode(CoopCampaignMapTooltipChunk value, out byte[] bytes)
        {
            bytes = null;
            if (!IsValid(value)) return false;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                writer.Write(Magic); writer.Write(value.Lease.ToByteArray()); writer.Write(value.Generation); writer.Write(value.Sequence);
                writer.Write(value.LogicalBytes); writer.Write(value.WireBytes); writer.Write(value.Count); writer.Write(value.Index);
                writer.Write((byte)value.Compression); writer.Write(Encoding.ASCII.GetBytes(value.Hash));
                writer.Write((ushort)value.Bytes.Length); writer.Write(value.Bytes); writer.Flush();
                if (stream.Length > MaxPacketBytes) return false;
                bytes = stream.ToArray(); return true;
            }
        }
        public static bool TryDecode(byte[] bytes, out CoopCampaignMapTooltipChunk value)
        {
            value = null;
            if (bytes == null || bytes.Length < 1 || bytes.Length > MaxPacketBytes) return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Encoding.ASCII, true))
                {
                    if (reader.ReadInt32() != Magic) return false;
                    byte[] lease = reader.ReadBytes(16);
                    if (lease.Length != 16) return false;
                    var decoded = new CoopCampaignMapTooltipChunk { Lease = new Guid(lease), Generation = reader.ReadInt64(), Sequence = reader.ReadInt64(),
                        LogicalBytes = reader.ReadInt32(), WireBytes = reader.ReadInt32(), Count = reader.ReadInt32(), Index = reader.ReadInt32(),
                        Compression = (CoopCampaignMapCatalogCompressionKind)reader.ReadByte(), Hash = Encoding.ASCII.GetString(reader.ReadBytes(64)) };
                    int count = reader.ReadUInt16();
                    if (count < 1 || count > 256 || count != stream.Length - stream.Position) return false;
                    decoded.Bytes = reader.ReadBytes(count);
                    if (!IsValid(decoded)) return false;
                    value = decoded; return true;
                }
            }
            catch (IOException) { return false; }
            catch (ArgumentException) { return false; }
        }
    }

    public sealed class CoopCampaignMapTooltipAssembler
    {
        private CoopCampaignMapCatalogChunkAccumulator _accumulator;
        private Guid _lease;
        private long _generation, _sequence;
        public void Clear() { _accumulator = null; _lease = Guid.Empty; _generation = _sequence = 0; }

        // Check pending interest BEFORE allocating a chunk table or decompressing.
        public bool TryAccept(CoopCampaignMapTooltipChunk chunk, CoopCampaignMapTooltipInterest interest,
            bool disclosed, long now, out CoopCampaignMapTooltipPayload payload)
        {
            payload = null;
            var pending = interest?.PendingRequest;
            if (pending == null || !interest.IsCurrentPending(pending.LeaseId, pending.Generation, disclosed, now)) { Clear(); return false; }
            // A delayed chunk for another lease/generation must not destroy the
            // current valid assembly or allocate a new one.
            if (!CoopCampaignMapTooltipChunks.IsValid(chunk) || chunk.Lease != pending.LeaseId || chunk.Generation != pending.Generation) return false;
            if (_accumulator == null || _lease != chunk.Lease || _generation != chunk.Generation)
            {
                Clear();
                if (!CoopCampaignMapCatalogChunkAccumulator.TryCreate(1, 1, chunk.LogicalBytes, chunk.WireBytes,
                    chunk.Count, chunk.Compression, chunk.Hash, out _accumulator, out _)) return false;
                _lease = chunk.Lease; _generation = chunk.Generation; _sequence = chunk.Sequence;
            }
            if (_sequence != chunk.Sequence || _accumulator.LogicalByteCount != chunk.LogicalBytes ||
                _accumulator.WireByteCount != chunk.WireBytes || _accumulator.ChunkCount != chunk.Count ||
                _accumulator.CompressionKind != chunk.Compression || !string.Equals(_accumulator.PayloadHash, chunk.Hash, StringComparison.OrdinalIgnoreCase) ||
                !_accumulator.TryAccept(chunk.Index, chunk.Count, chunk.Bytes, out _)) { Clear(); return false; }
            if (!_accumulator.IsComplete) return false;
            bool complete = _accumulator.TryComplete(out var bytes, out _) &&
                CoopCampaignMapTooltipWireCodec.TryDecode(bytes, out var response) && response.Kind == CoopCampaignMapTooltipMessageKind.Rows &&
                response.Lease == _lease && response.Payload.Sequence == _sequence && interest.TryAccept(_lease, response.Payload, disclosed, now);
            Clear();
            return complete && interest.TryGetDisplay(disclosed, now, out payload);
        }
    }
}
