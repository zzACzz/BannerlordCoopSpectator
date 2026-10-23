using System;

namespace CoopSpectator.Infrastructure
{
    // One mission-owned, bounded pending list. Reset at every session boundary.
    public sealed class CoopCampaignMapBattleMarkerAssembler
    {
        private CoopCampaignMapCatalogChunkAccumulator _pending;
        private int _latest;
        private int _completed;

        // A newer admitted envelope revokes the old presentation even if its
        // final hash/decoding later fails. Only a successful latest list reveals it.
        public bool HasCompletedLatest => _completed > 0 && _completed == _latest;

        public bool TryAccept(int revision, int logicalBytes, int wireBytes, int count,
            CoopCampaignMapCatalogCompressionKind compression, string hash, int index, byte[] chunk,
            out bool completed, out CoopCampaignMapBattleMarker[] markers)
        {
            completed = false; markers = null;
            if (revision <= 0 || revision < _latest || revision <= _completed ||
                logicalBytes < CoopCampaignMapBattleMarkerCodec.HeaderBytes ||
                logicalBytes > CoopCampaignMapBattleMarkerCodec.MaximumBytes ||
                wireBytes > CoopCampaignMapBattleMarkerCodec.MaximumBytes ||
                !CoopCampaignMapConversationPayloadCodec.IsValidEnvelope(revision, logicalBytes,
                    wireBytes, count, compression, hash, index, chunk)) return false;
            if (_pending == null || revision > _latest)
            {
                if (!CoopCampaignMapCatalogChunkAccumulator.TryCreate(1, revision, logicalBytes,
                    wireBytes, count, compression, hash, out var next, out _)) return false;
                _pending = next; _latest = revision;
            }
            if (_pending.LogicalByteCount != logicalBytes || _pending.WireByteCount != wireBytes ||
                _pending.ChunkCount != count || _pending.CompressionKind != compression ||
                !string.Equals(_pending.PayloadHash, hash, StringComparison.Ordinal) ||
                !_pending.TryAccept(index, count, chunk, out _)) return false;
            if (!_pending.IsComplete) return true;
            if (!_pending.TryComplete(out var bytes, out _) ||
                !CoopCampaignMapBattleMarkerCodec.TryDecode(bytes, revision, out var decoded))
            { _pending = null; return false; }
            _completed = revision; _pending = null;
            completed = true; markers = decoded;
            return true;
        }

        public void Reset() { _pending = null; _latest = 0; _completed = 0; }
    }
}
