using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    public sealed class CoopHeroCreationChunkedPayload
    {
        public CoopHeroCreationChunkedPayload(string payloadHash, int logicalByteCount, byte[][] chunks)
        {
            PayloadHash = payloadHash ?? string.Empty;
            LogicalByteCount = logicalByteCount;
            Chunks = chunks ?? Array.Empty<byte[]>();
        }

        public string PayloadHash { get; }
        public int LogicalByteCount { get; }
        public byte[][] Chunks { get; }
        public int ChunkCount => Chunks.Length;
    }

    public sealed class CoopHeroCreationChunkAccumulator
    {
        private readonly byte[][] _chunks;
        private readonly bool[] _received;
        private int _receivedCount;
        private int _wireByteCount;

        private CoopHeroCreationChunkAccumulator(
            int chunkCount,
            int logicalByteCount,
            string payloadHash,
            DateTime createdUtc)
        {
            ChunkCount = chunkCount;
            LogicalByteCount = logicalByteCount;
            PayloadHash = payloadHash;
            CreatedUtc = createdUtc;
            LastActivityUtc = createdUtc;
            _chunks = new byte[chunkCount][];
            _received = new bool[chunkCount];
        }

        public int ChunkCount { get; }
        public int LogicalByteCount { get; }
        public string PayloadHash { get; }
        public DateTime CreatedUtc { get; }
        public DateTime LastActivityUtc { get; private set; }
        public bool IsComplete => _receivedCount == ChunkCount;

        public static bool TryCreate(
            int chunkCount,
            int logicalByteCount,
            string payloadHash,
            DateTime createdUtc,
            out CoopHeroCreationChunkAccumulator accumulator,
            out string error)
        {
            accumulator = null;
            if (chunkCount <= 0 || chunkCount > CoopHeroCreationChunkCodec.MaxChunkCount)
                return Fail("chunk_count_invalid", out error);
            if (logicalByteCount <= 0 || logicalByteCount > CoopHeroCreationChunkCodec.MaxLogicalBytes)
                return Fail("logical_byte_count_invalid", out error);
            if (!CoopHeroCreationChunkCodec.IsSha256(payloadHash))
                return Fail("transport_hash_invalid", out error);

            accumulator = new CoopHeroCreationChunkAccumulator(
                chunkCount,
                logicalByteCount,
                payloadHash.ToLowerInvariant(),
                createdUtc);
            error = string.Empty;
            return true;
        }

        public bool Matches(int chunkCount, int logicalByteCount, string payloadHash)
        {
            return chunkCount == ChunkCount &&
                   logicalByteCount == LogicalByteCount &&
                   string.Equals(payloadHash, PayloadHash, StringComparison.OrdinalIgnoreCase);
        }

        public bool TryAccept(
            int chunkIndex,
            int chunkCount,
            byte[] payloadBytes,
            DateTime receivedUtc,
            out bool completed,
            out string error)
        {
            completed = false;
            if (chunkCount != ChunkCount)
                return Fail("chunk_count_conflict", out error);
            if (chunkIndex < 0 || chunkIndex >= ChunkCount)
                return Fail("chunk_index_invalid", out error);

            byte[] safePayload = payloadBytes ?? Array.Empty<byte>();
            if (safePayload.Length <= 0 || safePayload.Length > CoopHeroCreationChunkCodec.MaxChunkBytes)
                return Fail("chunk_size_invalid", out error);
            if (chunkIndex < ChunkCount - 1 && safePayload.Length != CoopHeroCreationChunkCodec.MaxChunkBytes)
                return Fail("non_terminal_chunk_size_invalid", out error);

            LastActivityUtc = receivedUtc;
            if (_received[chunkIndex])
            {
                if (!_chunks[chunkIndex].SequenceEqual(safePayload))
                    return Fail("duplicate_chunk_conflict", out error);
                completed = IsComplete;
                error = string.Empty;
                return true;
            }

            if (_wireByteCount + safePayload.Length > CoopHeroCreationChunkCodec.MaxWireBytes)
                return Fail("wire_payload_too_large", out error);

            byte[] stored = new byte[safePayload.Length];
            Buffer.BlockCopy(safePayload, 0, stored, 0, safePayload.Length);
            _chunks[chunkIndex] = stored;
            _received[chunkIndex] = true;
            _receivedCount++;
            _wireByteCount += stored.Length;
            completed = IsComplete;
            error = string.Empty;
            return true;
        }

        public bool TryComplete(out string payloadJson, out string error)
        {
            payloadJson = null;
            if (!IsComplete)
                return Fail("chunks_incomplete", out error);

            byte[] compressed = new byte[_wireByteCount];
            int offset = 0;
            for (int i = 0; i < _chunks.Length; i++)
            {
                byte[] chunk = _chunks[i];
                if (chunk == null || chunk.Length <= 0)
                    return Fail("chunk_missing", out error);
                Buffer.BlockCopy(chunk, 0, compressed, offset, chunk.Length);
                offset += chunk.Length;
            }

            return CoopHeroCreationChunkCodec.TryDecode(
                compressed,
                LogicalByteCount,
                PayloadHash,
                out payloadJson,
                out error);
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }

    public static class CoopHeroCreationChunkCodec
    {
        public const int MaxChunkBytes = 256;
        public const int MaxChunkCount = 2048;
        public const int MaxLogicalCharacters = 131072;
        public const int MaxLogicalBytes = 524288;
        public const int MaxWireBytes = MaxChunkBytes * MaxChunkCount;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static bool TryEncode(
            string payloadJson,
            out CoopHeroCreationChunkedPayload payload,
            out string error)
        {
            payload = null;
            string safePayload = payloadJson ?? string.Empty;
            if (safePayload.Length <= 0 || safePayload.Length > MaxLogicalCharacters)
                return Fail("logical_character_count_invalid", out error);

            byte[] logicalBytes;
            try { logicalBytes = StrictUtf8.GetBytes(safePayload); }
            catch (EncoderFallbackException) { return Fail("payload_utf8_invalid", out error); }
            if (logicalBytes.Length <= 0 || logicalBytes.Length > MaxLogicalBytes)
                return Fail("logical_payload_too_large", out error);

            byte[] compressed;
            try
            {
                using (MemoryStream output = new MemoryStream())
                {
                    using (GZipStream gzip = new GZipStream(output, CompressionMode.Compress, true))
                        gzip.Write(logicalBytes, 0, logicalBytes.Length);
                    compressed = output.ToArray();
                }
            }
            catch (Exception ex)
            {
                return Fail("payload_compression_failed:" + ex.GetType().Name, out error);
            }

            if (compressed.Length <= 0 || compressed.Length > MaxWireBytes)
                return Fail("wire_payload_too_large", out error);

            int chunkCount = (compressed.Length + MaxChunkBytes - 1) / MaxChunkBytes;
            if (chunkCount <= 0 || chunkCount > MaxChunkCount)
                return Fail("chunk_count_invalid", out error);

            byte[][] chunks = new byte[chunkCount][];
            for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                int chunkOffset = chunkIndex * MaxChunkBytes;
                int chunkLength = Math.Min(MaxChunkBytes, compressed.Length - chunkOffset);
                chunks[chunkIndex] = new byte[chunkLength];
                Buffer.BlockCopy(compressed, chunkOffset, chunks[chunkIndex], 0, chunkLength);
            }

            payload = new CoopHeroCreationChunkedPayload(
                CoopHeroCreationHash.ComputeSha256(safePayload),
                logicalBytes.Length,
                chunks);
            error = string.Empty;
            return true;
        }

        internal static bool TryDecode(
            byte[] compressed,
            int expectedLogicalByteCount,
            string expectedPayloadHash,
            out string payloadJson,
            out string error)
        {
            payloadJson = null;
            if (compressed == null || compressed.Length <= 0 || compressed.Length > MaxWireBytes)
                return Fail("wire_payload_size_invalid", out error);
            if (expectedLogicalByteCount <= 0 || expectedLogicalByteCount > MaxLogicalBytes)
                return Fail("logical_byte_count_invalid", out error);
            if (!IsSha256(expectedPayloadHash))
                return Fail("transport_hash_invalid", out error);

            byte[] logicalBytes;
            try
            {
                using (MemoryStream input = new MemoryStream(compressed, false))
                using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress, false))
                using (MemoryStream output = new MemoryStream(Math.Min(expectedLogicalByteCount, 8192)))
                {
                    byte[] buffer = new byte[8192];
                    while (true)
                    {
                        int bytesRead = gzip.Read(buffer, 0, buffer.Length);
                        if (bytesRead <= 0) break;
                        if (output.Length + bytesRead > MaxLogicalBytes ||
                            output.Length + bytesRead > expectedLogicalByteCount)
                            return Fail("decompressed_payload_too_large", out error);
                        output.Write(buffer, 0, bytesRead);
                    }
                    logicalBytes = output.ToArray();
                }
            }
            catch (Exception ex)
            {
                return Fail("payload_decompression_failed:" + ex.GetType().Name, out error);
            }

            if (logicalBytes.Length != expectedLogicalByteCount)
                return Fail("logical_byte_count_mismatch", out error);

            try { payloadJson = StrictUtf8.GetString(logicalBytes); }
            catch (DecoderFallbackException) { return Fail("payload_utf8_invalid", out error); }
            if (payloadJson.Length <= 0 || payloadJson.Length > MaxLogicalCharacters)
                return Fail("logical_character_count_invalid", out error);
            if (!string.Equals(
                    CoopHeroCreationHash.ComputeSha256(payloadJson),
                    expectedPayloadHash,
                    StringComparison.OrdinalIgnoreCase))
                return Fail("transport_hash_mismatch", out error);

            error = string.Empty;
            return true;
        }

        public static bool IsSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool isHex = (c >= '0' && c <= '9') ||
                             (c >= 'a' && c <= 'f') ||
                             (c >= 'A' && c <= 'F');
                if (!isHex) return false;
            }
            return true;
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
