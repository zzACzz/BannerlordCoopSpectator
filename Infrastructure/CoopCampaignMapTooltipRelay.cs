using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    internal enum CoopTooltipRelayKind : byte { Binding = 1, Requests = 2, Responses = 3 }
    internal sealed class CoopTooltipRelayItem
    {
        public CoopCampaignMapTooltipRequest Request;
        public long Sequence;
        public byte[] Response;
    }
    internal sealed class CoopTooltipRelayFrame
    {
        public CoopTooltipRelayKind Kind;
        public Guid Run;
        public CoopMissionProcessIdentity Host, Dedicated;
        public long Round, SentTimestamp;
        public List<CoopTooltipRelayItem> Items = new List<CoopTooltipRelayItem>();
    }

    // Separate launcher-owned local channel, independent of automatic missions.
    // No path, process identity or native provider is selected by an MP client.
    internal static class CoopCampaignMapTooltipRelay
    {
        public const string SessionEnvironment = "COOPSPECTATOR_MAP_TOOLTIP_SESSION";
        public const int MaxItems = 2;
        public const int MaxFrameBytes = 2 * CoopCampaignMapTooltipWireCodec.MaxEnvelopeBytes + 20000;
        private const int Magic = 0x43545231;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        internal static string DirectoryFor(Guid run)
        {
            if (run == Guid.Empty) throw new ArgumentException("Missing tooltip run.");
            return Path.Combine(Path.GetTempPath(), "CoopSpectator", "TooltipSessions", run.ToString("N"));
        }
        private static string FileFor(Guid run, CoopTooltipRelayKind kind)
        {
            string name;
            switch (kind)
            {
                case CoopTooltipRelayKind.Binding: name = "binding.bin"; break;
                case CoopTooltipRelayKind.Requests: name = "requests.bin"; break;
                case CoopTooltipRelayKind.Responses: name = "responses.bin"; break;
                default: throw new ArgumentException("Unknown tooltip relay kind.");
            }
            return Path.Combine(DirectoryFor(run), name);
        }
        internal static void Write(CoopTooltipRelayFrame frame) =>
            CoopCampaignMissionChannel.WriteAtomic(FileFor(frame.Run, frame.Kind), Encode(frame));

        internal static CoopTooltipRelayFrame Read(Guid run, CoopTooltipRelayKind kind)
        {
            try
            {
                var frame = Decode(CoopCampaignMissionChannel.ReadBounded(FileFor(run, kind),
                    kind == CoopTooltipRelayKind.Responses ? MaxFrameBytes : 20000));
                return frame.Run == run && frame.Kind == kind ? frame : null;
            }
            catch (IOException) { return null; }
            catch (InvalidDataException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
        }

        internal static bool MatchesBinding(CoopTooltipRelayFrame frame, CoopTooltipRelayFrame binding) =>
            frame != null && binding != null && binding.Kind == CoopTooltipRelayKind.Binding &&
            frame.Run == binding.Run && binding.Host != null && binding.Dedicated != null &&
            binding.Host.Matches(frame.Host) && binding.Dedicated.Matches(frame.Dedicated);

        internal static bool IsFresh(CoopTooltipRelayFrame frame, long now, long frequency) =>
            frame != null && frame.Kind != CoopTooltipRelayKind.Binding && frame.SentTimestamp > 0 &&
            frequency > 0 && now >= frame.SentTimestamp &&
            (now - frame.SentTimestamp) / (double)frequency < 2d;

        internal static byte[] Encode(CoopTooltipRelayFrame frame)
        {
            Validate(frame);
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8, true))
            {
                writer.Write(Magic); writer.Write((byte)frame.Kind); writer.Write(frame.Run.ToByteArray());
                Identity(writer, frame.Host); Identity(writer, frame.Dedicated);
                writer.Write(frame.Round); writer.Write(frame.SentTimestamp); writer.Write((byte)frame.Items.Count);
                foreach (var item in frame.Items)
                {
                    CoopCampaignMapTooltipWireCodec.TryEncode(new CoopCampaignMapTooltipWireMessage {
                        Kind = CoopCampaignMapTooltipMessageKind.Request, Request = item.Request }, out var request);
                    writer.Write(request.Length); writer.Write(request); writer.Write(item.Sequence);
                    byte[] response = item.Response ?? Array.Empty<byte>();
                    writer.Write(response.Length); writer.Write(response);
                }
                writer.Flush();
                if (stream.Length > MaxFrameBytes) throw new InvalidDataException("Tooltip relay size.");
                return stream.ToArray();
            }
        }

        internal static CoopTooltipRelayFrame Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 1 || bytes.Length > MaxFrameBytes) throw new InvalidDataException("Tooltip relay size.");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Utf8, true))
            {
                if (reader.ReadInt32() != Magic) throw new InvalidDataException("Tooltip relay schema.");
                var frame = new CoopTooltipRelayFrame { Kind = (CoopTooltipRelayKind)reader.ReadByte() };
                byte[] run = reader.ReadBytes(16);
                if (run.Length != 16) throw new EndOfStreamException();
                frame.Run = new Guid(run); frame.Host = Identity(reader); frame.Dedicated = Identity(reader);
                frame.Round = reader.ReadInt64(); frame.SentTimestamp = reader.ReadInt64();
                int count = reader.ReadByte();
                if (count > MaxItems) throw new InvalidDataException("Tooltip relay item count.");
                for (int i = 0; i < count; i++)
                {
                    byte[] requestBytes = Block(reader, CoopCampaignMapTooltipWireCodec.MaxControlBytes);
                    if (!CoopCampaignMapTooltipWireCodec.TryDecode(requestBytes, out var request) ||
                        request.Kind != CoopCampaignMapTooltipMessageKind.Request) throw new InvalidDataException("Tooltip relay request.");
                    var item = new CoopTooltipRelayItem { Request = request.Request, Sequence = reader.ReadInt64(),
                        Response = Block(reader, CoopCampaignMapTooltipWireCodec.MaxEnvelopeBytes) };
                    frame.Items.Add(item);
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Tooltip relay trailing bytes.");
                Validate(frame); return frame;
            }
        }

        private static void Validate(CoopTooltipRelayFrame frame)
        {
            if (frame == null || frame.Run == Guid.Empty || frame.Host == null || frame.Dedicated == null ||
                frame.Host.Matches(frame.Dedicated) || frame.SentTimestamp <= 0 || frame.Items == null || frame.Items.Count > MaxItems ||
                (frame.Kind != CoopTooltipRelayKind.Binding && frame.Kind != CoopTooltipRelayKind.Requests && frame.Kind != CoopTooltipRelayKind.Responses))
                throw new InvalidDataException("Tooltip relay frame.");
            ValidIdentity(frame.Host); ValidIdentity(frame.Dedicated);
            if (frame.Kind == CoopTooltipRelayKind.Binding)
            {
                if (frame.Round != 0 || frame.Items.Count != 0) throw new InvalidDataException("Tooltip binding shape.");
                return;
            }
            if (frame.Round <= 0 || frame.Items.Count < 1) throw new InvalidDataException("Tooltip round.");
            var leases = new HashSet<Guid>();
            foreach (var item in frame.Items)
            {
                if (item == null || item.Request == null || item.Sequence <= 0 || !leases.Add(item.Request.LeaseId) ||
                    !CoopCampaignMapTooltipWireCodec.TryEncode(new CoopCampaignMapTooltipWireMessage {
                        Kind = CoopCampaignMapTooltipMessageKind.Request, Request = item.Request }, out _)) throw new InvalidDataException("Tooltip item.");
                if (frame.Kind == CoopTooltipRelayKind.Requests)
                {
                    if (item.Response != null && item.Response.Length != 0) throw new InvalidDataException("Request contains result.");
                }
                else
                {
                    if (!CoopCampaignMapTooltipWireCodec.TryDecode(item.Response, out var response)) throw new InvalidDataException("Tooltip result.");
                    bool match = response.Kind == CoopCampaignMapTooltipMessageKind.Rows
                        ? response.Lease == item.Request.LeaseId && response.Payload.Sequence == item.Sequence &&
                            CoopCampaignMapTooltipPayloadCodec.MatchesInterest(response.Payload, item.Request.SessionId,
                                item.Request.EntityId, item.Request.Generation, item.Request.IsExtended, item.Sequence - 1)
                        : response.Kind == CoopCampaignMapTooltipMessageKind.Denied && SameRequest(response.Request, item.Request);
                    if (!match) throw new InvalidDataException("Tooltip result correlation.");
                }
            }
        }
        internal static bool SameRequest(CoopCampaignMapTooltipRequest left, CoopCampaignMapTooltipRequest right) =>
            left != null && right != null && left.LeaseId == right.LeaseId && left.SessionId == right.SessionId &&
            left.EntityId == right.EntityId && left.Generation == right.Generation && left.IsExtended == right.IsExtended;

        private static void ValidIdentity(CoopMissionProcessIdentity identity)
        {
            if (identity.Id <= 0 || identity.StartUtcTicks <= 0 || string.IsNullOrEmpty(identity.Path) ||
                identity.Path.Length > 2048 || !Path.IsPathRooted(identity.Path) || identity.Path.IndexOf('\0') >= 0 ||
                Utf8.GetByteCount(identity.Path) > 8192) throw new InvalidDataException("Tooltip process identity.");
        }
        private static void Identity(BinaryWriter writer, CoopMissionProcessIdentity identity)
        {
            writer.Write(identity.Id); writer.Write(identity.StartUtcTicks);
            byte[] path = Utf8.GetBytes(identity.Path); writer.Write(path.Length); writer.Write(path);
        }
        private static CoopMissionProcessIdentity Identity(BinaryReader reader) => new CoopMissionProcessIdentity {
            Id = reader.ReadInt32(), StartUtcTicks = reader.ReadInt64(), Path = Utf8.GetString(Block(reader, 8192)) };
        private static byte[] Block(BinaryReader reader, int limit)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > limit || count > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException("Tooltip block bound.");
            return reader.ReadBytes(count);
        }
    }
}
