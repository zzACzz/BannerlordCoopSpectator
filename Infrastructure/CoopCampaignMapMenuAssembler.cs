using System;

namespace CoopSpectator.Infrastructure
{
    // One bounded in-flight menu; a newer revision supersedes unfinished data.
    // A completed null menu is an explicit close, distinguished by 'completed'.
    public sealed class CoopCampaignMapMenuAssembler
    {
        private CoopCampaignMapCatalogChunkAccumulator _active;
        private int _latestRevision;
        private int _completedRevision;

        public bool TryAccept(int revision, int logicalBytes, int wireBytes, int count,
            CoopCampaignMapCatalogCompressionKind compression, string hash, int index, byte[] chunk,
            out bool completed, out CoopCampaignMapMenuPresentation menu)
        {
            completed = false; menu = null;
            if (revision <= 0 || revision < _latestRevision || revision <= _completedRevision ||
                logicalBytes > CoopCampaignMapMenuCodec.MaxPayloadBytes ||
                !CoopCampaignMapConversationPayloadCodec.IsValidEnvelope(revision, logicalBytes,
                    wireBytes, count, compression, hash, index, chunk)) return false;
            if (_active == null || revision > _latestRevision)
            {
                if (!CoopCampaignMapCatalogChunkAccumulator.TryCreate(1, revision, logicalBytes,
                    wireBytes, count, compression, hash, out var next, out _)) return false;
                _active = next; _latestRevision = revision;
            }
            if (_active.LogicalByteCount != logicalBytes || _active.WireByteCount != wireBytes ||
                _active.ChunkCount != count || _active.CompressionKind != compression ||
                !string.Equals(_active.PayloadHash, hash, StringComparison.Ordinal) ||
                !_active.TryAccept(index, count, chunk, out _)) return false;
            if (!_active.IsComplete) return true;
            if (!_active.TryComplete(out var bytes, out _) ||
                !CoopCampaignMapMenuCodec.TryDeserialize(bytes, out var decodedRevision, out var decoded) ||
                decodedRevision != revision)
            { _active = null; return false; }
            _completedRevision = revision; _active = null;
            completed = true; menu = decoded; return true;
        }

        public void Reset()
        { _active = null; _latestRevision = 0; _completedRevision = 0; }
    }
}
