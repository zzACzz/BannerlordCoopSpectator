using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    // Current native simulation scoreboard presentation, never simulation inputs.
    public sealed class CoopCampaignAutoBattlePresentation
    {
        public string WindowId;
        public CoopCampaignPresentationValue Root;
        public bool IsValid() => CoopCampaignAutoBattleCodec.TryEncode(this, out _);
        public CoopCampaignAutoBattlePresentation Clone()
        {
            if (!CoopCampaignAutoBattleCodec.TryEncode(this, out var bytes) || !CoopCampaignAutoBattleCodec.TryDecode(bytes, out var copy))
                throw new ArgumentException("Invalid auto-battle presentation.");
            return copy;
        }
        internal bool HasVisibleSimulationRoot() => Root?.Kind == CoopCampaignPresentationValueKind.Node &&
            Root.Properties != null && Flag("IsSimulation") && Flag("ShowScoreboard") &&
            Root.Properties.TryGetValue("IsOver", out var ended) && ended?.Kind == CoopCampaignPresentationValueKind.Boolean &&
            Node("Attackers") && Node("Defenders");
        private bool Flag(string key) => Root.Properties.TryGetValue(key, out var value) &&
            value?.Kind == CoopCampaignPresentationValueKind.Boolean && value.Number == 1;
        private bool Node(string key) => Root.Properties.TryGetValue(key, out var value) && value?.Kind == CoopCampaignPresentationValueKind.Node;
    }
    public static class CoopCampaignAutoBattleCodec
    {
        private const int Magic = 0x43415331; // CAS1, bounded shared prepared-value encoding.
        public const int MaximumBytes = CoopCampaignAftermathCodec.MaximumBytes;
        public const int MinimumBytes = 25;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        public static bool TryEncode(CoopCampaignAutoBattlePresentation source, out byte[] bytes)
        {
            bytes = null;
            if (source == null || !Guid.TryParseExact(source.WindowId, "N", out var id) || id == Guid.Empty ||
                source.WindowId != id.ToString("N") || !source.HasVisibleSimulationRoot()) return false;
            try
            {
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream, Utf8, true))
                {
                    writer.Write(Magic); writer.Write(id.ToByteArray());
                    int budget = CoopCampaignAftermathCodec.MaximumValues;
                    CoopCampaignAftermathCodec.Write(writer, source.Root, 0, ref budget,
                        new HashSet<CoopCampaignPresentationValue>(), new Dictionary<string, int>(StringComparer.Ordinal));
                    writer.Flush(); if (stream.Length > MaximumBytes) return false;
                    bytes = stream.ToArray(); return true;
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is InvalidDataException || ex is OverflowException) { return false; }
        }
        public static bool TryDecode(byte[] bytes, out CoopCampaignAutoBattlePresentation result)
        {
            result = null;
            if (bytes == null || bytes.Length < MinimumBytes || bytes.Length > MaximumBytes) return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Utf8, true))
                {
                    if (reader.ReadInt32() != Magic) return false;
                    var id = new Guid(reader.ReadBytes(16)); if (id == Guid.Empty) return false;
                    int budget = CoopCampaignAftermathCodec.MaximumValues;
                    var root = CoopCampaignAftermathCodec.Read(reader, 0, ref budget, new List<string>(), new HashSet<string>(StringComparer.Ordinal));
                    var copy = new CoopCampaignAutoBattlePresentation { WindowId = id.ToString("N"), Root = root };
                    if (stream.Position != stream.Length || !copy.HasVisibleSimulationRoot()) return false;
                    result = copy; return true;
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is InvalidDataException || ex is OverflowException) { return false; }
        }
    }
}
