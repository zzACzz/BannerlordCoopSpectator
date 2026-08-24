using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    public enum CoopCampaignMapCatalogCompressionKind
    {
        None = 0,
        Gzip = 1
    }

    public sealed class CoopCampaignMapCatalogChunkedPayload
    {
        internal CoopCampaignMapCatalogChunkedPayload(
            int logicalByteCount,
            CoopCampaignMapCatalogCompressionKind compressionKind,
            string payloadHash,
            byte[][] chunks)
        {
            LogicalByteCount = logicalByteCount;
            CompressionKind = compressionKind;
            PayloadHash = payloadHash ?? string.Empty;
            Chunks = chunks ?? Array.Empty<byte[]>();
            int wireByteCount = 0;
            for (int index = 0; index < Chunks.Length; index++)
                wireByteCount += Chunks[index]?.Length ?? 0;
            WireByteCount = wireByteCount;
        }

        public int LogicalByteCount { get; }

        public int WireByteCount { get; }

        public int ChunkCount => Chunks.Length;

        public CoopCampaignMapCatalogCompressionKind CompressionKind { get; }

        public string PayloadHash { get; }

        public byte[][] Chunks { get; }
    }

    public static class CoopCampaignMapCatalogChunkCodec
    {
        public const int MaxTransferId = 1048575;
        public const int MaxChunkBytes = 256;
        public const int MaxChunkCount = 8191;
        public const int MaxWireBytes = MaxChunkBytes * MaxChunkCount;

        public static bool TryEncode(
            byte[] logicalBytes,
            out CoopCampaignMapCatalogChunkedPayload payload,
            out string reason)
        {
            payload = null;
            reason = null;
            if (logicalBytes == null ||
                logicalBytes.Length <= 0 ||
                logicalBytes.Length >
                    CoopCampaignMapPrototypeContract.MaxCatalogLogicalBytes)
            {
                reason = "logical-size";
                return false;
            }

            byte[] wireBytes = TryCompress(logicalBytes, out bool compressed);
            if (wireBytes.Length <= 0 || wireBytes.Length > MaxWireBytes)
            {
                reason = "wire-size";
                return false;
            }

            int chunkCount =
                (wireBytes.Length + MaxChunkBytes - 1) / MaxChunkBytes;
            if (chunkCount <= 0 || chunkCount > MaxChunkCount)
            {
                reason = "chunk-count";
                return false;
            }

            var chunks = new byte[chunkCount][];
            for (int chunkIndex = 0;
                 chunkIndex < chunkCount;
                 chunkIndex++)
            {
                int offset = chunkIndex * MaxChunkBytes;
                int length = Math.Min(
                    MaxChunkBytes,
                    wireBytes.Length - offset);
                chunks[chunkIndex] = new byte[length];
                Buffer.BlockCopy(
                    wireBytes,
                    offset,
                    chunks[chunkIndex],
                    0,
                    length);
            }

            payload = new CoopCampaignMapCatalogChunkedPayload(
                logicalBytes.Length,
                compressed
                    ? CoopCampaignMapCatalogCompressionKind.Gzip
                    : CoopCampaignMapCatalogCompressionKind.None,
                ComputeSha256(wireBytes),
                chunks);
            return true;
        }

        public static bool IsValidHash(string value)
        {
            if (value == null || value.Length != 64)
                return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool hexadecimal =
                    character >= '0' && character <= '9' ||
                    character >= 'a' && character <= 'f' ||
                    character >= 'A' && character <= 'F';
                if (!hexadecimal)
                    return false;
            }
            return true;
        }

        internal static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(bytes ?? Array.Empty<byte>());
                var builder = new StringBuilder(hash.Length * 2);
                for (int index = 0; index < hash.Length; index++)
                    builder.Append(hash[index].ToString("x2"));
                return builder.ToString();
            }
        }

        private static byte[] TryCompress(
            byte[] logicalBytes,
            out bool compressed)
        {
            compressed = false;
            try
            {
                using (var output = new MemoryStream())
                {
                    using (var gzip = new GZipStream(
                               output,
                               CompressionLevel.Optimal,
                               true))
                    {
                        gzip.Write(logicalBytes, 0, logicalBytes.Length);
                    }

                    byte[] compressedBytes = output.ToArray();
                    if (compressedBytes.Length > 0 &&
                        compressedBytes.Length < logicalBytes.Length)
                    {
                        compressed = true;
                        return compressedBytes;
                    }
                }
            }
            catch
            {
                // Raw transport is the safe fallback.
            }
            return logicalBytes;
        }
    }

    public static class CoopCampaignMapCatalogTransferPolicy
    {
        public static bool ShouldStartPreparedTransfer(
            int activeTransferId,
            int activeRevision,
            bool activeCompleted,
            int preparedTransferId,
            int preparedRevision)
        {
            if (preparedTransferId <= 0 ||
                preparedTransferId >
                    CoopCampaignMapCatalogChunkCodec.MaxTransferId ||
                preparedRevision < 0)
            {
                return false;
            }

            if (activeTransferId <= 0)
                return true;
            if (!activeCompleted)
                return false;
            return activeTransferId != preparedTransferId ||
                   activeRevision != preparedRevision;
        }
    }

    public sealed class CoopCampaignMapCatalogChunkAccumulator
    {
        private readonly byte[][] _chunks;
        private readonly bool[] _received;

        private CoopCampaignMapCatalogChunkAccumulator(
            int transferId,
            int revision,
            int logicalByteCount,
            int wireByteCount,
            int chunkCount,
            CoopCampaignMapCatalogCompressionKind compressionKind,
            string payloadHash)
        {
            TransferId = transferId;
            Revision = revision;
            LogicalByteCount = logicalByteCount;
            WireByteCount = wireByteCount;
            ChunkCount = chunkCount;
            CompressionKind = compressionKind;
            PayloadHash = payloadHash.ToLowerInvariant();
            _chunks = new byte[chunkCount][];
            _received = new bool[chunkCount];
            HighestContiguousChunkIndex = -1;
        }

        public int TransferId { get; }

        public int Revision { get; }

        public int LogicalByteCount { get; }

        public int WireByteCount { get; }

        public int ChunkCount { get; }

        public CoopCampaignMapCatalogCompressionKind CompressionKind { get; }

        public string PayloadHash { get; }

        public int ReceivedChunkCount { get; private set; }

        public int HighestContiguousChunkIndex { get; private set; }

        public bool IsComplete => ReceivedChunkCount == ChunkCount;

        public static bool TryCreate(
            int transferId,
            int revision,
            int logicalByteCount,
            int wireByteCount,
            int chunkCount,
            CoopCampaignMapCatalogCompressionKind compressionKind,
            string payloadHash,
            out CoopCampaignMapCatalogChunkAccumulator accumulator,
            out string reason)
        {
            accumulator = null;
            reason = null;
            int expectedChunkCount = wireByteCount <= 0
                ? 0
                : (wireByteCount +
                   CoopCampaignMapCatalogChunkCodec.MaxChunkBytes - 1) /
                  CoopCampaignMapCatalogChunkCodec.MaxChunkBytes;
            if (transferId <= 0 ||
                transferId > CoopCampaignMapCatalogChunkCodec.MaxTransferId ||
                revision < 0 ||
                logicalByteCount <= 0 ||
                logicalByteCount >
                    CoopCampaignMapPrototypeContract.MaxCatalogLogicalBytes ||
                wireByteCount <= 0 ||
                wireByteCount >
                    CoopCampaignMapCatalogChunkCodec.MaxWireBytes ||
                chunkCount <= 0 ||
                chunkCount >
                    CoopCampaignMapCatalogChunkCodec.MaxChunkCount ||
                chunkCount != expectedChunkCount ||
                !Enum.IsDefined(
                    typeof(CoopCampaignMapCatalogCompressionKind),
                    compressionKind) ||
                !CoopCampaignMapCatalogChunkCodec.IsValidHash(payloadHash))
            {
                reason = "manifest";
                return false;
            }

            accumulator = new CoopCampaignMapCatalogChunkAccumulator(
                transferId,
                revision,
                logicalByteCount,
                wireByteCount,
                chunkCount,
                compressionKind,
                payloadHash);
            return true;
        }

        public bool TryAccept(
            int chunkIndex,
            int chunkCount,
            byte[] payloadBytes,
            out string reason)
        {
            reason = null;
            int expectedLength = chunkIndex == ChunkCount - 1
                ? WireByteCount -
                  (ChunkCount - 1) *
                  CoopCampaignMapCatalogChunkCodec.MaxChunkBytes
                : CoopCampaignMapCatalogChunkCodec.MaxChunkBytes;
            if (chunkCount != ChunkCount ||
                chunkIndex < 0 ||
                chunkIndex >= ChunkCount ||
                payloadBytes == null ||
                payloadBytes.Length != expectedLength)
            {
                reason = "chunk";
                return false;
            }

            if (_received[chunkIndex])
            {
                if (!BytesEqual(_chunks[chunkIndex], payloadBytes))
                {
                    reason = "conflicting-duplicate";
                    return false;
                }
                return true;
            }

            byte[] copy = new byte[payloadBytes.Length];
            Buffer.BlockCopy(
                payloadBytes,
                0,
                copy,
                0,
                payloadBytes.Length);
            _chunks[chunkIndex] = copy;
            _received[chunkIndex] = true;
            ReceivedChunkCount++;
            while (HighestContiguousChunkIndex + 1 < ChunkCount &&
                   _received[HighestContiguousChunkIndex + 1])
            {
                HighestContiguousChunkIndex++;
            }
            return true;
        }

        public bool TryComplete(out byte[] logicalBytes, out string reason)
        {
            logicalBytes = Array.Empty<byte>();
            reason = null;
            if (!IsComplete)
            {
                reason = "missing-chunks";
                return false;
            }

            var wireBytes = new byte[WireByteCount];
            int offset = 0;
            for (int index = 0; index < _chunks.Length; index++)
            {
                byte[] chunk = _chunks[index];
                if (chunk == null || offset + chunk.Length > wireBytes.Length)
                {
                    reason = "wire-size";
                    return false;
                }
                Buffer.BlockCopy(chunk, 0, wireBytes, offset, chunk.Length);
                offset += chunk.Length;
            }
            if (offset != wireBytes.Length)
            {
                reason = "wire-size";
                return false;
            }

            string actualHash =
                CoopCampaignMapCatalogChunkCodec.ComputeSha256(wireBytes);
            if (!string.Equals(
                    actualHash,
                    PayloadHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                reason = "hash";
                return false;
            }

            if (CompressionKind ==
                CoopCampaignMapCatalogCompressionKind.None)
            {
                if (wireBytes.Length != LogicalByteCount)
                {
                    reason = "logical-size";
                    return false;
                }
                logicalBytes = wireBytes;
                return true;
            }

            try
            {
                using (var input = new MemoryStream(wireBytes, false))
                using (var gzip = new GZipStream(
                           input,
                           CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[8192];
                    int bytesRead;
                    while ((bytesRead = gzip.Read(
                               buffer,
                               0,
                               buffer.Length)) > 0)
                    {
                        if (output.Length + bytesRead >
                            CoopCampaignMapPrototypeContract
                                .MaxCatalogLogicalBytes)
                        {
                            reason = "logical-size";
                            return false;
                        }
                        output.Write(buffer, 0, bytesRead);
                    }
                    if (output.Length != LogicalByteCount)
                    {
                        reason = "logical-size";
                        return false;
                    }
                    logicalBytes = output.ToArray();
                    return true;
                }
            }
            catch
            {
                logicalBytes = Array.Empty<byte>();
                reason = "decompression";
                return false;
            }
        }

        public bool HasChunk(int chunkIndex)
        {
            return chunkIndex >= 0 &&
                   chunkIndex < _received.Length &&
                   _received[chunkIndex];
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (int index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                    return false;
            }
            return true;
        }
    }
}
