using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    internal sealed class CoopCampaignMissionAdmission
    {
        private readonly string _session;
        private readonly CoopMissionProcessIdentity _self;
        private readonly string _epoch, _campaign;
        private readonly CoopMissionProcessIdentity _host;
        private CoopCampaignMissionEnvelope _last;
        private string _lastDigest;
        public CoopCampaignMissionAdmission(string session, CoopMissionProcessIdentity self,
            string epoch = null, string campaign = null, CoopMissionProcessIdentity host = null)
        { _session = session; _self = self?.Copy() ?? throw new ArgumentNullException(nameof(self));
            _epoch = epoch; _campaign = campaign; _host = host?.Copy(); }
        public CoopCampaignMissionEnvelope Admit(CoopCampaignMissionEnvelope value, long now, long frequency,
            Func<CoopMissionProcessIdentity, bool> hostAlive)
        {
            if (value == null || value.Intent.Session != _session || !CoopCampaignMissionChannel.IsFresh(value, now, frequency) ||
                !_self.Matches(value.Dedicated) || !hostAlive(value.Host) ||
                (_epoch != null && value.Epoch != _epoch) || (_campaign != null && value.CampaignId != _campaign) ||
                (_host != null && !_host.Matches(value.Host))) return null;
            if (_last != null && (!_last.Host.Matches(value.Host) || _last.CampaignId != value.CampaignId || _last.Epoch != value.Epoch))
                throw new InvalidDataException("Session identity changed.");
            if (_last != null && value.Pulse < _last.Pulse) return null;
            byte[] encoded = CoopCampaignMissionChannel.Encode(value);
            string digest = CoopCampaignMissionChannel.Digest(encoded);
            if (_last != null && value.Pulse == _last.Pulse && digest != _lastDigest)
                throw new InvalidDataException("Pulse reused.");
            _last = CoopCampaignMissionChannel.Decode(encoded); // Immutable admission history, detached from caller.
            _lastDigest = digest;
            var admitted = CoopCampaignMissionChannel.Decode(encoded);
            admitted.Intent.Fresh = true;
            return admitted;
        }
    }

    internal sealed class CoopMissionProcessIdentity
    {
        public int Id;
        public long StartUtcTicks;
        public string Path;
        internal CoopMissionProcessIdentity Copy() => new CoopMissionProcessIdentity { Id = Id, StartUtcTicks = StartUtcTicks, Path = Path };
        public static CoopMissionProcessIdentity Capture(Process process) => new CoopMissionProcessIdentity
        { Id = process.Id, StartUtcTicks = process.StartTime.ToUniversalTime().Ticks, Path = process.MainModule.FileName };
        public bool Matches(CoopMissionProcessIdentity other) => other != null && Id == other.Id &&
            StartUtcTicks == other.StartUtcTicks && string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase);
        public bool IsAlive()
        {
            try { using (var process = Process.GetProcessById(Id)) return !process.HasExited && Matches(Capture(process)); }
            catch { return false; }
        }
    }

    internal sealed class CoopCampaignMissionEnvelope
    {
        public CoopCampaignMissionIntent Intent;
        public CoopMissionProcessIdentity Host, Dedicated;
        public long Pulse, SentTimestamp;
        public string CampaignId, Epoch, BattleId, BattleInstanceId, Scene, GameType, RosterSha256;
    }

    // Local, inherited session only. No directory enumeration, arbitrary path input,
    // credentials, engine references or retry sleeps. Control writes never fall back
    // to an in-place overwrite. Process identity is re-admitted by the receiver.
    internal static class CoopCampaignMissionChannel
    {
        public const string SessionEnvironment = "COOPSPECTATOR_CAMPAIGN_MISSION_SESSION";
        public const int MaxEnvelopeBytes = 16384;
        public const int MaxRosterBytes = 16 * 1024 * 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        public static string HostSession { get; set; }
        public static string ActiveSession => HostSession ?? Environment.GetEnvironmentVariable(SessionEnvironment);
        public static string DirectoryFor(string session)
        {
            if (!Guid.TryParseExact(session, "N", out _)) throw new ArgumentException("Invalid lifecycle session.");
            return System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CoopSpectator", "MissionSessions", session);
        }
        // Preserve the campaign/client's existing shared roster path. Only the
        // dedicated process reads a pinned, session-local active-stage roster.
        public static string RosterPath => HostSession != null || string.IsNullOrEmpty(ActiveSession) ? null :
            System.IO.Path.Combine(DirectoryFor(ActiveSession), "battle_roster.json");
        // Published only after the authoritative result file was successfully written.
        // The dedicated adapter correlates the exact mission reference and identity.
        public static Action<object, string, string, string> ResultPublished { get; set; }
        public static void Write(CoopCampaignMissionEnvelope value)
        {
            byte[] bytes = Encode(value);
            string path = System.IO.Path.Combine(DirectoryFor(value.Intent.Session), "intent.bin");
            WriteAtomic(path, bytes);
        }
        public static void WriteAtomic(string path, byte[] bytes)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static CoopCampaignMissionEnvelope Read(string session)
        {
            try
            {
                var result = Decode(ReadBounded(System.IO.Path.Combine(DirectoryFor(session), "intent.bin"), MaxEnvelopeBytes));
                return result.Intent.Session == session ? result : null;
            }
            catch (IOException) { return null; }
            catch (InvalidDataException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
        }
        public static byte[] ReadBounded(string path, int limit)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                if (stream.Length < 1 || stream.Length > limit) throw new InvalidDataException("Size bound.");
                byte[] bytes = new byte[(int)stream.Length];
                int position = 0;
                while (position < bytes.Length)
                {
                    int read = stream.Read(bytes, position, bytes.Length - position);
                    if (read == 0) throw new EndOfStreamException();
                    position += read;
                }
                if (stream.ReadByte() != -1) throw new InvalidDataException("File changed.");
                return bytes;
            }
        }
        public static string Digest(byte[] bytes)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
        }
        public static bool IsFresh(CoopCampaignMissionEnvelope value, long now, long frequency)
        {
            if (value == null || value.Pulse <= 0 || frequency <= 0 || value.SentTimestamp <= 0 || now < value.SentTimestamp) return false;
            return (now - value.SentTimestamp) / (double)frequency <= 15;
        }
        internal static byte[] Encode(CoopCampaignMissionEnvelope value)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8, true))
            {
                writer.Write(0x434D4C02);
                var intent = value.Intent;
                Text(writer, intent.Session); writer.Write(intent.Revision); writer.Write((int)intent.Kind);
                Text(writer, intent.BattleKey); writer.Write(intent.HostOnGlobalMap);
                writer.Write(intent.AftermathApplied); Text(writer, intent.AppliedBattleKey); writer.Write(intent.FollowupStagePending);
                Identity(writer, value.Host); Identity(writer, value.Dedicated);
                writer.Write(value.Pulse); writer.Write(value.SentTimestamp);
                Text(writer, value.CampaignId); Text(writer, value.Epoch); Text(writer, value.BattleId); Text(writer, value.BattleInstanceId);
                Text(writer, value.Scene); Text(writer, value.GameType); Text(writer, value.RosterSha256);
                writer.Flush();
                if (stream.Length > MaxEnvelopeBytes) throw new InvalidDataException("Envelope bound.");
                byte[] bytes = stream.ToArray();
                Decode(bytes); // Writers and readers enforce the same contract.
                return bytes;
            }
        }
        internal static CoopCampaignMissionEnvelope Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length > MaxEnvelopeBytes) throw new InvalidDataException("Envelope bound.");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Utf8))
            {
                if (reader.ReadInt32() != 0x434D4C02) throw new InvalidDataException("Schema.");
                var intent = new CoopCampaignMissionIntent {
                    Session = Text(reader), Revision = reader.ReadInt64(), Kind = (CoopCampaignMissionIntentKind)reader.ReadInt32(),
                    BattleKey = Text(reader), HostOnGlobalMap = Boolean(reader), AftermathApplied = Boolean(reader),
                    AppliedBattleKey = Text(reader), FollowupStagePending = Boolean(reader) };
                var result = new CoopCampaignMissionEnvelope {
                    Intent = intent, Host = Identity(reader), Dedicated = Identity(reader),
                    Pulse = reader.ReadInt64(), SentTimestamp = reader.ReadInt64(),
                    CampaignId = Text(reader), Epoch = Text(reader), BattleId = Text(reader), BattleInstanceId = Text(reader),
                    Scene = Text(reader), GameType = Text(reader), RosterSha256 = Text(reader) };
                if (stream.Position != stream.Length || !Guid.TryParseExact(intent.Session, "N", out _) || intent.Revision <= 0 ||
                    !Enum.IsDefined(typeof(CoopCampaignMissionIntentKind), intent.Kind) || result.Pulse <= 0 || result.SentTimestamp <= 0 ||
                    string.IsNullOrEmpty(result.CampaignId) || !Guid.TryParseExact(result.Epoch, "N", out var epoch) || epoch == Guid.Empty ||
                    result.Epoch != epoch.ToString("N") || intent.BattleKey.Length > 256 || intent.AppliedBattleKey.Length > 256)
                    throw new InvalidDataException("Invalid intent.");
                if (intent.Kind == CoopCampaignMissionIntentKind.Battle && (string.IsNullOrEmpty(result.BattleId) ||
                    string.IsNullOrEmpty(result.BattleInstanceId) || !IsToken(result.Scene) || !IsToken(result.GameType) ||
                    !IsDigest(result.RosterSha256) || intent.BattleKey != result.RosterSha256))
                    throw new InvalidDataException("Invalid battle binding.");
                return result;
            }
        }
        public static bool IsToken(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 128) return false;
            foreach (char c in text) if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                !(c >= '0' && c <= '9') && c != '_') return false;
            return true;
        }
        private static bool IsDigest(string text)
        {
            if (text == null || text.Length != 64) return false;
            foreach (char c in text) if (!(c >= 'A' && c <= 'F') && !(c >= '0' && c <= '9')) return false;
            return true;
        }
        private static void Text(BinaryWriter writer, string text)
        {
            byte[] bytes = Utf8.GetBytes(text ?? "");
            if (bytes.Length > 2048) throw new InvalidDataException("String bound.");
            writer.Write((ushort)bytes.Length); writer.Write(bytes);
        }
        private static string Text(BinaryReader reader)
        {
            int length = reader.ReadUInt16();
            if (length > 2048) throw new InvalidDataException("String bound.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return Utf8.GetString(bytes);
        }
        private static bool Boolean(BinaryReader reader)
        {
            byte value = reader.ReadByte();
            if (value > 1) throw new InvalidDataException("Boolean.");
            return value != 0;
        }
        private static void Identity(BinaryWriter writer, CoopMissionProcessIdentity value)
        { writer.Write(value.Id); writer.Write(value.StartUtcTicks); Text(writer, value.Path); }
        private static CoopMissionProcessIdentity Identity(BinaryReader reader)
        {
            var value = new CoopMissionProcessIdentity { Id = reader.ReadInt32(), StartUtcTicks = reader.ReadInt64(), Path = Text(reader) };
            if (value.Id <= 0 || value.StartUtcTicks <= 0 || !System.IO.Path.IsPathRooted(value.Path))
                throw new InvalidDataException("Process identity.");
            return value;
        }
    }
}
