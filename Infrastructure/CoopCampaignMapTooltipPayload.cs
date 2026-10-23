using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    // Detached presentation only. No campaign objects, delegates, commands or
    // client-chosen provider types may cross this boundary.
    public enum CoopCampaignMapTooltipKind
    {
        Settlement = 1,
        MobileParty = 2,
        Army = 3,
        MapEvent = 4,
        SiegeEvent = 5,
        PlunderItem = 6,
        BarRundown = 7,
        BarInfo = 8,
        BarControl = 9
    }

    public sealed class CoopCampaignMapTooltipRow
    {
        public string Definition { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public int TextHeight { get; set; }
        public int PropertyModifier { get; set; }
        // Preserve native float channels, including transparent default color.
        public float Red { get; set; }
        public float Green { get; set; }
        public float Blue { get; set; }
        public float Alpha { get; set; }
    }

    public sealed class CoopCampaignMapTooltipPayload
    {
        public string SessionId { get; set; }
        public string EntityId { get; set; }
        public long RequestGeneration { get; set; }
        public long Sequence { get; set; }
        public bool IsExtended { get; set; }
        public CoopCampaignMapTooltipKind Kind { get; set; }
        public int Mode { get; set; }
        public List<CoopCampaignMapTooltipRow> Rows { get; set; } = new List<CoopCampaignMapTooltipRow>();
        public CoopCampaignMapRundownPresentation Rundown { get; set; }
        public CoopCampaignMapControlHint ControlHint { get; set; }
    }

    public static class CoopCampaignMapTooltipPayloadCodec
    {
        public const int Version = 3;
        public const int MaxPayloadBytes = 1048576;
        public const int MaxRows = 512;
        public const int MaxTextCharacters = 4096;
        public const int MaxTextBytes = MaxTextCharacters * 4;
        public const int MaxPropertyModifier = 0x3fff;
        private const int Magic = 0x43545431;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        // Correlation only, NOT authorization. The caller must separately admit
        // peer, map lifetime, current host visibility and response freshness.
        public static bool MatchesInterest(CoopCampaignMapTooltipPayload payload,
            string sessionId, string entityId, long generation, bool extended,
            long lastSequence)
        {
            return IsValid(payload) && generation > 0 && lastSequence >= 0 &&
                string.Equals(payload.SessionId, sessionId, StringComparison.Ordinal) &&
                string.Equals(payload.EntityId, entityId, StringComparison.Ordinal) &&
                payload.RequestGeneration == generation && payload.IsExtended == extended &&
                payload.Sequence > lastSequence;
        }

        public static bool IsValid(CoopCampaignMapTooltipPayload payload)
        {
            return TryMeasure(payload, out _);
        }

        public static bool IsValidCorrelation(string sessionId, string entityId, long generation, long sequence)
        {
            if (sessionId == null || (sessionId.Length != 32 && sessionId.Length != 36) ||
                !Guid.TryParseExact(sessionId, sessionId.Length == 32 ? "N" : "D", out var session) ||
                session == Guid.Empty || !ValidText(entityId, CoopCampaignMapPrototypeContract.MaxEntityIdCharacters, false) ||
                string.IsNullOrWhiteSpace(entityId) || generation <= 0 || sequence <= 0) return false;
            try { Utf8.GetByteCount(entityId); return true; }
            catch (ArgumentException) { return false; }
        }

        public static bool TrySerialize(CoopCampaignMapTooltipPayload payload, out byte[] bytes)
        {
            bytes = null;
            if (!TryMeasure(payload, out int size)) return false;
            try
            {
                using (var stream = new MemoryStream(size))
                using (var writer = new BinaryWriter(stream, Utf8, true))
                {
                    writer.Write(Magic);
                    writer.Write(Version);
                    WriteText(writer, payload.SessionId);
                    WriteText(writer, payload.EntityId);
                    writer.Write(payload.RequestGeneration);
                    writer.Write(payload.Sequence);
                    writer.Write(payload.IsExtended);
                    writer.Write((int)payload.Kind);
                    writer.Write(payload.Mode);
                    writer.Write(payload.Rows.Count);
                    foreach (var row in payload.Rows)
                    {
                        WriteText(writer, row.Definition);
                        WriteText(writer, row.Value);
                        writer.Write(row.TextHeight);
                        writer.Write(row.PropertyModifier);
                        writer.Write(row.Red);
                        writer.Write(row.Green);
                        writer.Write(row.Blue);
                        writer.Write(row.Alpha);
                    }
                    if (payload.Kind == CoopCampaignMapTooltipKind.BarRundown)
                    {
                        if (!CoopCampaignMapRundownCodec.TryEncode(payload.Rundown, out var rundownBytes)) return false;
                        writer.Write(rundownBytes.Length); writer.Write(rundownBytes);
                    }
                    if (payload.Kind == CoopCampaignMapTooltipKind.BarControl)
                    {
                        if (!CoopCampaignMapControlHintCodec.TryEncode(payload.ControlHint, out var controlBytes)) return false;
                        writer.Write(controlBytes.Length); writer.Write(controlBytes);
                    }
                    writer.Flush();
                    if (stream.Length != size) return false;
                    bytes = stream.ToArray();
                    return true;
                }
            }
            catch (ArgumentException) { return false; }
        }

        public static bool TryDeserialize(byte[] bytes, out CoopCampaignMapTooltipPayload payload)
        {
            payload = null;
            if (bytes == null || bytes.Length < 1 || bytes.Length > MaxPayloadBytes) return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Utf8, true))
                {
                    if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version) return false;
                    var decoded = new CoopCampaignMapTooltipPayload {
                        SessionId = ReadText(reader, 36),
                        EntityId = ReadText(reader, CoopCampaignMapPrototypeContract.MaxEntityIdCharacters),
                        RequestGeneration = reader.ReadInt64(),
                        Sequence = reader.ReadInt64()
                    };
                    byte extended = reader.ReadByte();
                    if (extended > 1) return false;
                    decoded.IsExtended = extended == 1;
                    decoded.Kind = (CoopCampaignMapTooltipKind)reader.ReadInt32();
                    decoded.Mode = reader.ReadInt32();
                    int count = reader.ReadInt32();
                    // Each row requires two length prefixes and six 32-bit fields.
                    if (count < (decoded.Kind == CoopCampaignMapTooltipKind.BarRundown || decoded.Kind == CoopCampaignMapTooltipKind.BarControl ? 0 : 1) || count > MaxRows || count > (stream.Length - stream.Position) / 32) return false;
                    for (int i = 0; i < count; i++)
                    {
                        decoded.Rows.Add(new CoopCampaignMapTooltipRow {
                            Definition = ReadText(reader, MaxTextCharacters),
                            Value = ReadText(reader, MaxTextCharacters),
                            TextHeight = reader.ReadInt32(),
                            PropertyModifier = reader.ReadInt32(),
                            Red = reader.ReadSingle(), Green = reader.ReadSingle(),
                            Blue = reader.ReadSingle(), Alpha = reader.ReadSingle()
                        });
                    }
                    if (decoded.Kind == CoopCampaignMapTooltipKind.BarRundown)
                    {
                        int length = reader.ReadInt32();
                        if (length < 0 || length > MaxPayloadBytes || length != stream.Length - stream.Position ||
                            !CoopCampaignMapRundownCodec.TryDecode(reader.ReadBytes(length), out var rundown)) return false;
                        decoded.Rundown = rundown;
                    }
                    if (decoded.Kind == CoopCampaignMapTooltipKind.BarControl)
                    {
                        int length = reader.ReadInt32();
                        if (length < 0 || length > MaxPayloadBytes || length != stream.Length - stream.Position ||
                            !CoopCampaignMapControlHintCodec.TryDecode(reader.ReadBytes(length), out var control)) return false;
                        decoded.ControlHint = control;
                    }
                    if (stream.Position != stream.Length || !IsValid(decoded)) return false;
                    payload = decoded;
                    return true;
                }
            }
            catch (InvalidDataException) { return false; }
            catch (IOException) { return false; }
            catch (ArgumentException) { return false; }
        }

        private static bool TryMeasure(CoopCampaignMapTooltipPayload payload, out int size)
        {
            size = 0;
            if (payload == null || !IsValidCorrelation(payload.SessionId, payload.EntityId, payload.RequestGeneration, payload.Sequence) ||
                (int)payload.Kind < 1 || (int)payload.Kind > 9 || payload.Mode < 0 || payload.Mode > 4 ||
                payload.Rows == null || payload.Rows.Count > MaxRows) return false;
            try
            {
                long measured = 45L + Utf8.GetByteCount(payload.SessionId) + Utf8.GetByteCount(payload.EntityId);
                if (payload.Kind != CoopCampaignMapTooltipKind.BarControl && payload.ControlHint != null) return false;
                if (payload.Kind == CoopCampaignMapTooltipKind.BarControl)
                {
                    if (payload.Rows.Count != 0 || payload.Mode != 0 || payload.Rundown != null ||
                        !CoopCampaignMapBarHintIdentity<object>.IsToken(payload.EntityId) ||
                        !CoopCampaignMapControlHintCodec.TryEncode(payload.ControlHint, out var controlBytes)) return false;
                    measured += 4L + controlBytes.Length;
                    if (measured > MaxPayloadBytes) return false;
                }
                else if (payload.Kind == CoopCampaignMapTooltipKind.BarRundown)
                {
                    if (payload.Rows.Count != 0 || payload.Mode != 0 ||
                        !CoopCampaignMapBarHintIdentity<object>.IsToken(payload.EntityId) ||
                        payload.Rundown == null || !payload.Rundown.IsActive ||
                        payload.Rundown.IsExtended != payload.IsExtended ||
                        !CoopCampaignMapRundownCodec.TryEncode(payload.Rundown, out var rundownBytes)) return false;
                    measured += 4L + rundownBytes.Length;
                    if (measured > MaxPayloadBytes) return false;
                }
                else if (payload.Rundown != null || payload.Rows.Count < 1) return false;
                if (payload.Kind == CoopCampaignMapTooltipKind.BarInfo &&
                    (payload.Mode != 0 || !CoopCampaignMapBarHintIdentity<object>.IsToken(payload.EntityId))) return false;
                foreach (var row in payload.Rows)
                {
                    if (row == null || !ValidText(row.Definition, MaxTextCharacters, true) ||
                        !ValidText(row.Value, MaxTextCharacters, true) || row.TextHeight < -1 || row.TextHeight > 4096 ||
                        row.PropertyModifier < 0 || row.PropertyModifier > MaxPropertyModifier ||
                        !ColorChannel(row.Red) || !ColorChannel(row.Green) ||
                        !ColorChannel(row.Blue) || !ColorChannel(row.Alpha)) return false;
                    measured += 32L + Utf8.GetByteCount(row.Definition) + Utf8.GetByteCount(row.Value);
                    if (measured > MaxPayloadBytes) return false;
                }
                size = (int)measured;
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        private static bool ColorChannel(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= 1f;
        }

        private static bool ValidText(string value, int maximum, bool multiline)
        {
            if (value == null || value.Length > maximum) return false;
            foreach (char ch in value)
                if (char.IsControl(ch) && !(multiline && (ch == '\n' || ch == '\r' || ch == '\t'))) return false;
            return true;
        }

        private static void WriteText(BinaryWriter writer, string value)
        {
            byte[] bytes = Utf8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadText(BinaryReader reader, int maxCharacters)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > maxCharacters * 4 ||
                length > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException();
            string result = Utf8.GetString(reader.ReadBytes(length));
            if (result.Length > maxCharacters) throw new InvalidDataException();
            return result;
        }
    }
}
