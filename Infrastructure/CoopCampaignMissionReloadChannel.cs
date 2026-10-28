using System;
using System.IO;

namespace CoopSpectator.Infrastructure
{
    internal sealed class CoopCampaignReloadFrame
    {
        public bool Acknowledgement;
        public CoopCampaignMissionEnvelope Binding;
        internal CoopCampaignReloadRequest Request => new CoopCampaignReloadRequest {
            Ordinal = Binding.Intent.Revision, Epoch = Binding.Epoch, CampaignId = Binding.CampaignId };
        internal bool Matches(CoopCampaignReloadFrame other) => other != null && Request.Same(other.Request) &&
            Binding.Intent.Session == other.Binding.Intent.Session && Binding.Host.Matches(other.Binding.Host) &&
            Binding.Dedicated.Matches(other.Binding.Dedicated);
    }

    // Only fixed files inside the inherited process-pair directory. A normal
    // envelope cannot change epoch: a separately admitted reset/ack is required.
    internal static class CoopCampaignMissionReloadChannel
    {
        internal const int MaximumBytes = CoopCampaignMissionChannel.MaxEnvelopeBytes + 9;
        internal static byte[] Encode(CoopCampaignReloadFrame frame)
        {
            if (frame?.Binding == null || frame.Binding.Intent == null || !frame.Request.IsValid ||
                frame.Binding.Intent.Kind != CoopCampaignMissionIntentKind.Hold ||
                frame.Binding.Intent.HostOnGlobalMap || frame.Binding.Intent.AftermathApplied || frame.Binding.Intent.FollowupStagePending ||
                !string.IsNullOrEmpty(frame.Binding.Intent.BattleKey) || !string.IsNullOrEmpty(frame.Binding.Intent.AppliedBattleKey) ||
                !string.IsNullOrEmpty(frame.Binding.BattleId) || !string.IsNullOrEmpty(frame.Binding.BattleInstanceId) ||
                !string.IsNullOrEmpty(frame.Binding.RosterSha256) || !string.IsNullOrEmpty(frame.Binding.Scene) ||
                !string.IsNullOrEmpty(frame.Binding.GameType)) throw new InvalidDataException("Reload frame contains battle state.");
            byte[] binding = CoopCampaignMissionChannel.Encode(frame.Binding);
            using (var output = new MemoryStream()) using (var writer = new BinaryWriter(output))
            {
                writer.Write(0x43524C01); writer.Write(frame.Acknowledgement); writer.Write(binding.Length); writer.Write(binding);
                return output.ToArray();
            }
        }
        internal static CoopCampaignReloadFrame Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 10 || bytes.Length > MaximumBytes) throw new InvalidDataException("Reload size.");
            using (var input = new MemoryStream(bytes, false)) using (var reader = new BinaryReader(input))
            {
                if (reader.ReadInt32() != 0x43524C01) throw new InvalidDataException("Reload schema.");
                byte acknowledgement = reader.ReadByte(); int length = reader.ReadInt32();
                if (acknowledgement > 1 || length <= 0 || length != input.Length - input.Position)
                    throw new InvalidDataException("Reload framing.");
                var frame = new CoopCampaignReloadFrame { Acknowledgement = acknowledgement == 1,
                    Binding = CoopCampaignMissionChannel.Decode(reader.ReadBytes(length)) };
                Encode(frame); return frame;
            }
        }
        internal static void Write(CoopCampaignReloadFrame frame) => CoopCampaignMissionChannel.WriteAtomic(
            Path.Combine(CoopCampaignMissionChannel.DirectoryFor(frame.Binding.Intent.Session),
                frame.Acknowledgement ? "reload_ack.bin" : "reload_request.bin"), Encode(frame));
        internal static CoopCampaignReloadFrame Read(string session, bool acknowledgement)
        {
            try
            {
                var frame = Decode(CoopCampaignMissionChannel.ReadBounded(Path.Combine(CoopCampaignMissionChannel.DirectoryFor(session),
                    acknowledgement ? "reload_ack.bin" : "reload_request.bin"), MaximumBytes));
                return frame.Binding.Intent.Session == session && frame.Acknowledgement == acknowledgement ? frame : null;
            }
            catch (IOException) { return null; }
            catch (InvalidDataException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
        }
    }
}
