using System;
using System.IO;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    public enum CoopCampaignMapTooltipMessageKind : byte { Hello = 1, Grant = 2, Request = 3, Rows = 4, Denied = 5, Revoked = 6 }

    // Semantic records only; not yet registered native network messages.
    public sealed class CoopCampaignMapTooltipWireMessage
    {
        public CoopCampaignMapTooltipMessageKind Kind { get; internal set; }
        public Guid Challenge { get; internal set; }
        public Guid Lease { get; internal set; }
        public string Session { get; internal set; }
        public CoopCampaignMapTooltipRequest Request { get; internal set; }
        public CoopCampaignMapTooltipPayload Payload { get; internal set; }
    }

    public static class CoopCampaignMapTooltipWireCodec
    {
        public const int Version = 1;
        public const int MaxControlBytes = 512;
        public const int MaxEnvelopeBytes = CoopCampaignMapTooltipPayloadCodec.MaxPayloadBytes + 26;
        private const int Magic = 0x43545731;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static bool TryEncode(CoopCampaignMapTooltipWireMessage value, out byte[] bytes)
        {
            bytes = null;
            if (!Valid(value)) return false;
            try
            {
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream, Utf8, true))
                {
                    writer.Write(Magic); writer.Write((byte)Version); writer.Write((byte)value.Kind);
                    switch (value.Kind)
                    {
                        case CoopCampaignMapTooltipMessageKind.Hello:
                            writer.Write(value.Challenge.ToByteArray()); break;
                        case CoopCampaignMapTooltipMessageKind.Revoked:
                            writer.Write(value.Lease.ToByteArray()); break;
                        case CoopCampaignMapTooltipMessageKind.Grant:
                            writer.Write(value.Challenge.ToByteArray()); writer.Write(value.Lease.ToByteArray());
                            Text(writer, value.Session); break;
                        case CoopCampaignMapTooltipMessageKind.Request:
                        case CoopCampaignMapTooltipMessageKind.Denied:
                            var request = value.Request;
                            writer.Write(request.LeaseId.ToByteArray()); Text(writer, request.SessionId);
                            Text(writer, request.EntityId); writer.Write(request.Generation); writer.Write(request.IsExtended);
                            break;
                        case CoopCampaignMapTooltipMessageKind.Rows:
                            if (!CoopCampaignMapTooltipPayloadCodec.TrySerialize(value.Payload, out byte[] rows)) return false;
                            writer.Write(value.Lease.ToByteArray()); writer.Write(rows.Length); writer.Write(rows);
                            break;
                    }
                    writer.Flush();
                    if (stream.Length > (value.Kind == CoopCampaignMapTooltipMessageKind.Rows ? MaxEnvelopeBytes : MaxControlBytes)) return false;
                    bytes = stream.ToArray(); return true;
                }
            }
            catch (ArgumentException) { return false; }
        }

        public static bool TryDecode(byte[] bytes, out CoopCampaignMapTooltipWireMessage value)
        {
            value = null;
            if (bytes == null || bytes.Length < 6 || bytes.Length > MaxEnvelopeBytes) return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Utf8, true))
                {
                    if (reader.ReadInt32() != Magic || reader.ReadByte() != Version) return false;
                    var result = new CoopCampaignMapTooltipWireMessage { Kind = (CoopCampaignMapTooltipMessageKind)reader.ReadByte() };
                    if (result.Kind != CoopCampaignMapTooltipMessageKind.Rows && bytes.Length > MaxControlBytes) return false;
                    switch (result.Kind)
                    {
                        case CoopCampaignMapTooltipMessageKind.Hello:
                            result.Challenge = ReadGuid(reader); break;
                        case CoopCampaignMapTooltipMessageKind.Revoked:
                            result.Lease = ReadGuid(reader); break;
                        case CoopCampaignMapTooltipMessageKind.Grant:
                            result.Challenge = ReadGuid(reader); result.Lease = ReadGuid(reader);
                            result.Session = Text(reader, 36); break;
                        case CoopCampaignMapTooltipMessageKind.Request:
                        case CoopCampaignMapTooltipMessageKind.Denied:
                            Guid lease = ReadGuid(reader);
                            string session = Text(reader, 36);
                            string entity = Text(reader, CoopCampaignMapPrototypeContract.MaxEntityIdCharacters);
                            long generation = reader.ReadInt64();
                            byte extended = reader.ReadByte();
                            if (extended > 1) return false;
                            result.Request = new CoopCampaignMapTooltipRequest(session, lease, entity, generation, extended == 1);
                            break;
                        case CoopCampaignMapTooltipMessageKind.Rows:
                            result.Lease = ReadGuid(reader);
                            int count = reader.ReadInt32();
                            if (count < 1 || count > CoopCampaignMapTooltipPayloadCodec.MaxPayloadBytes || count != stream.Length - stream.Position) return false;
                            if (!CoopCampaignMapTooltipPayloadCodec.TryDeserialize(reader.ReadBytes(count), out var payload)) return false;
                            result.Payload = payload; break;
                        default: return false;
                    }
                    if (stream.Position != stream.Length || !Valid(result)) return false;
                    value = result; return true;
                }
            }
            catch (InvalidDataException) { return false; }
            catch (IOException) { return false; }
            catch (ArgumentException) { return false; }
        }

        // Direction must also be enforced by the native network handler. A valid
        // Rows record is never accepted from a client, nor Hello from a server.
        public static bool IsClientMessage(CoopCampaignMapTooltipMessageKind kind) =>
            kind == CoopCampaignMapTooltipMessageKind.Hello || kind == CoopCampaignMapTooltipMessageKind.Request;

        public static bool IsServerMessage(CoopCampaignMapTooltipMessageKind kind) =>
            kind == CoopCampaignMapTooltipMessageKind.Grant || kind == CoopCampaignMapTooltipMessageKind.Rows ||
            kind == CoopCampaignMapTooltipMessageKind.Denied || kind == CoopCampaignMapTooltipMessageKind.Revoked;

        private static bool Valid(CoopCampaignMapTooltipWireMessage value)
        {
            if (value == null) return false;
            switch (value.Kind)
            {
                case CoopCampaignMapTooltipMessageKind.Hello:
                    return value.Challenge != Guid.Empty && value.Lease == Guid.Empty && value.Session == null && value.Request == null && value.Payload == null;
                case CoopCampaignMapTooltipMessageKind.Revoked:
                    return value.Challenge == Guid.Empty && value.Lease != Guid.Empty && value.Session == null && value.Request == null && value.Payload == null;
                case CoopCampaignMapTooltipMessageKind.Grant:
                    return value.Challenge != Guid.Empty && value.Lease != Guid.Empty && value.Request == null && value.Payload == null &&
                        CoopCampaignMapTooltipPayloadCodec.IsValidCorrelation(value.Session, "grant", 1, 1);
                case CoopCampaignMapTooltipMessageKind.Request:
                case CoopCampaignMapTooltipMessageKind.Denied:
                    return value.Challenge == Guid.Empty && value.Lease == Guid.Empty && value.Session == null && value.Payload == null &&
                        value.Request != null && value.Request.LeaseId != Guid.Empty &&
                        CoopCampaignMapTooltipPayloadCodec.IsValidCorrelation(value.Request.SessionId, value.Request.EntityId, value.Request.Generation, 1);
                case CoopCampaignMapTooltipMessageKind.Rows:
                    return value.Challenge == Guid.Empty && value.Lease != Guid.Empty && value.Session == null && value.Request == null &&
                        CoopCampaignMapTooltipPayloadCodec.IsValid(value.Payload);
                default: return false;
            }
        }

        private static Guid ReadGuid(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(16);
            if (bytes.Length != 16) throw new EndOfStreamException();
            return new Guid(bytes);
        }
        private static void Text(BinaryWriter writer, string value)
        {
            byte[] bytes = Utf8.GetBytes(value);
            writer.Write((ushort)bytes.Length); writer.Write(bytes);
        }
        private static string Text(BinaryReader reader, int maxCharacters)
        {
            int count = reader.ReadUInt16();
            if (count > maxCharacters * 4 || count > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException("Tooltip text bound.");
            string value = Utf8.GetString(reader.ReadBytes(count));
            if (value.Length > maxCharacters) throw new InvalidDataException("Tooltip character bound.");
            return value;
        }
    }

    // Client challenge is regenerated at every lifecycle reset. A delayed grant
    // from a previous map/connection cannot reinstall its old lease. Server
    // authentication/mission ownership are still mandatory in the caller.
    public sealed class CoopCampaignMapTooltipHandshake
    {
        public Guid Challenge { get; private set; } = Guid.NewGuid();
        private Guid _lease;
        private string _session;

        public void Reset() { Challenge = Guid.NewGuid(); _lease = Guid.Empty; _session = null; }

        public bool TryAcceptGrant(CoopCampaignMapTooltipWireMessage grant)
        {
            if (grant == null || grant.Kind != CoopCampaignMapTooltipMessageKind.Grant || grant.Challenge != Challenge ||
                grant.Lease == Guid.Empty || !CoopCampaignMapTooltipPayloadCodec.IsValidCorrelation(grant.Session, "grant", 1, 1)) return false;
            if (_lease != Guid.Empty) return _lease == grant.Lease && string.Equals(_session, grant.Session, StringComparison.Ordinal);
            _lease = grant.Lease; _session = grant.Session; return true;
        }
    }
}
