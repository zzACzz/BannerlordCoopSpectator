using System;
using System.Collections.Generic;
using System.IO;

namespace CoopSpectator.Infrastructure
{
    // Atomic full-list payload; outer transport must additionally bind session
    // and monotonic revision. This codec alone does not authenticate a sender.
    public static class CoopCampaignMapBattleMarkerCodec
    {
        private const int Magic = 0x314D4243; // CBM1
        public const int HeaderBytes = 12;
        public const int EntryBytes = 16;
        public const int MaximumBytes = HeaderBytes + EntryBytes * CoopCampaignMapBattleMarkerPolicy.MaximumEvents;

        public static bool TryEncode(int revision, IReadOnlyList<CoopCampaignMapBattleMarker> markers,
            out byte[] payload)
        {
            payload = null;
            if (revision < 0 || !CoopCampaignMapBattleMarkerPolicy.TrySnapshot(markers, out var snapshot)) return false;
            using (var stream = new MemoryStream(HeaderBytes + EntryBytes * snapshot.Length))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                writer.Write(revision);
                writer.Write(snapshot.Length);
                foreach (var marker in snapshot)
                {
                    writer.Write(marker.EventId);
                    writer.Write(marker.X); writer.Write(marker.Y); writer.Write(marker.Z);
                }
                writer.Flush();
                payload = stream.ToArray();
                return true;
            }
        }

        public static bool TryDecode(byte[] payload, int expectedRevision,
            out CoopCampaignMapBattleMarker[] markers)
        {
            markers = null;
            if (expectedRevision < 0 || payload == null || payload.Length < HeaderBytes || payload.Length > MaximumBytes)
                return false;
            using (var stream = new MemoryStream(payload, false))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadInt32() != Magic || reader.ReadInt32() != expectedRevision) return false;
                int count = reader.ReadInt32();
                if (count < 0 || count > CoopCampaignMapBattleMarkerPolicy.MaximumEvents ||
                    payload.Length != HeaderBytes + count * EntryBytes) return false;
                var result = new CoopCampaignMapBattleMarker[count];
                for (int i = 0; i < count; i++)
                    result[i] = new CoopCampaignMapBattleMarker(reader.ReadInt32(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                return CoopCampaignMapBattleMarkerPolicy.TrySnapshot(result, out markers);
            }
        }
    }
}
