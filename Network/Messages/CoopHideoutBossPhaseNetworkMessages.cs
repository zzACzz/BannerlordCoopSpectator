using CoopSpectator.Infrastructure.Hideout;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Network.Messages
{
    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromClient)]
    public sealed class CoopHideoutBossPhaseClientCommandMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer CommandCompression =
            new CompressionInfo.Integer(0, 2, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer RevisionCompression =
            new CompressionInfo.Integer(0, int.MaxValue, maximumValueGiven: true);

        public CoopHideoutBossPhaseClientCommandMessage(
            string battleInstanceId,
            int revision,
            CoopHideoutBossClientCommandKind commandKind)
        {
            BattleInstanceId = CoopHideoutBossPhaseContract.Bound(
                battleInstanceId,
                CoopHideoutBossPhaseContract.MaximumBattleInstanceIdCharacters);
            Revision = revision;
            CommandKind = commandKind;
        }

        public CoopHideoutBossPhaseClientCommandMessage()
        {
            BattleInstanceId = string.Empty;
        }

        public string BattleInstanceId { get; private set; }
        public int Revision { get; private set; }
        public CoopHideoutBossClientCommandKind CommandKind { get; private set; }

        protected override bool OnRead()
        {
            bool valid = true;
            BattleInstanceId = ReadStringFromPacket(ref valid) ?? string.Empty;
            Revision = ReadIntFromPacket(RevisionCompression, ref valid);
            CommandKind = (CoopHideoutBossClientCommandKind)ReadIntFromPacket(CommandCompression, ref valid);
            return valid &&
                   BattleInstanceId.Length <= CoopHideoutBossPhaseContract.MaximumBattleInstanceIdCharacters;
        }

        protected override void OnWrite()
        {
            WriteStringToPacket(CoopHideoutBossPhaseContract.Bound(
                BattleInstanceId,
                CoopHideoutBossPhaseContract.MaximumBattleInstanceIdCharacters));
            WriteIntToPacket(Revision, RevisionCompression);
            WriteIntToPacket((int)CommandKind, CommandCompression);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() => MultiplayerMessageFilter.Mission;

        protected override string OnGetLogFormat()
        {
            return "CoopHideoutBossPhaseClientCommand Battle=" + BattleInstanceId +
                   " Revision=" + Revision +
                   " Kind=" + CommandKind;
        }
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopHideoutBossPhaseStateMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer ProtocolCompression =
            new CompressionInfo.Integer(0, 15, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer RevisionCompression =
            new CompressionInfo.Integer(0, int.MaxValue, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer PhaseCompression =
            new CompressionInfo.Integer(0, 6, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer ChoiceCompression =
            new CompressionInfo.Integer(0, 2, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer PeerIndexCompression =
            new CompressionInfo.Integer(-1, 4095, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer AgentIndexCompression =
            new CompressionInfo.Integer(-1, 4095, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer DurationCompression =
            new CompressionInfo.Integer(0, 60000, maximumValueGiven: true);

        public CoopHideoutBossPhaseStateMessage(
            CoopHideoutBossPhaseSession session,
            int phaseDurationMilliseconds)
        {
            ProtocolVersion = CoopHideoutBossPhaseContract.ProtocolVersion;
            BattleInstanceId = CoopHideoutBossPhaseContract.Bound(
                session?.BattleInstanceId,
                CoopHideoutBossPhaseContract.MaximumBattleInstanceIdCharacters);
            Revision = session?.Revision ?? 0;
            Phase = session?.Phase ?? CoopHideoutBossPhase.InitialAssault;
            Choice = session?.Choice ?? CoopHideoutBossChoice.None;
            HostPeerIndex = session?.HostPeerIndex ?? -1;
            HostAgentIndex = session?.HostAgentIndex ?? -1;
            BossAgentIndex = session?.BossAgentIndex ?? -1;
            PhaseDurationMilliseconds = phaseDurationMilliseconds;
            Reason = CoopHideoutBossPhaseContract.Bound(
                session?.Reason,
                CoopHideoutBossPhaseContract.MaximumReasonCharacters);
        }

        public CoopHideoutBossPhaseStateMessage()
        {
            BattleInstanceId = string.Empty;
            Reason = string.Empty;
        }

        public int ProtocolVersion { get; private set; }
        public string BattleInstanceId { get; private set; }
        public int Revision { get; private set; }
        public CoopHideoutBossPhase Phase { get; private set; }
        public CoopHideoutBossChoice Choice { get; private set; }
        public int HostPeerIndex { get; private set; }
        public int HostAgentIndex { get; private set; }
        public int BossAgentIndex { get; private set; }
        public int PhaseDurationMilliseconds { get; private set; }
        public string Reason { get; private set; }

        public CoopHideoutBossPhaseSession ToSession()
        {
            return new CoopHideoutBossPhaseSession
            {
                BattleInstanceId = BattleInstanceId,
                Revision = Revision,
                Phase = Phase,
                Choice = Choice,
                HostPeerIndex = HostPeerIndex,
                HostAgentIndex = HostAgentIndex,
                BossAgentIndex = BossAgentIndex,
                Reason = Reason
            };
        }

        protected override bool OnRead()
        {
            bool valid = true;
            ProtocolVersion = ReadIntFromPacket(ProtocolCompression, ref valid);
            BattleInstanceId = ReadStringFromPacket(ref valid) ?? string.Empty;
            Revision = ReadIntFromPacket(RevisionCompression, ref valid);
            Phase = (CoopHideoutBossPhase)ReadIntFromPacket(PhaseCompression, ref valid);
            Choice = (CoopHideoutBossChoice)ReadIntFromPacket(ChoiceCompression, ref valid);
            HostPeerIndex = ReadIntFromPacket(PeerIndexCompression, ref valid);
            HostAgentIndex = ReadIntFromPacket(AgentIndexCompression, ref valid);
            BossAgentIndex = ReadIntFromPacket(AgentIndexCompression, ref valid);
            PhaseDurationMilliseconds = ReadIntFromPacket(DurationCompression, ref valid);
            Reason = ReadStringFromPacket(ref valid) ?? string.Empty;
            return valid &&
                   BattleInstanceId.Length <= CoopHideoutBossPhaseContract.MaximumBattleInstanceIdCharacters &&
                   Reason.Length <= CoopHideoutBossPhaseContract.MaximumReasonCharacters;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket(ProtocolVersion, ProtocolCompression);
            WriteStringToPacket(CoopHideoutBossPhaseContract.Bound(
                BattleInstanceId,
                CoopHideoutBossPhaseContract.MaximumBattleInstanceIdCharacters));
            WriteIntToPacket(Revision, RevisionCompression);
            WriteIntToPacket((int)Phase, PhaseCompression);
            WriteIntToPacket((int)Choice, ChoiceCompression);
            WriteIntToPacket(HostPeerIndex, PeerIndexCompression);
            WriteIntToPacket(HostAgentIndex, AgentIndexCompression);
            WriteIntToPacket(BossAgentIndex, AgentIndexCompression);
            WriteIntToPacket(PhaseDurationMilliseconds, DurationCompression);
            WriteStringToPacket(CoopHideoutBossPhaseContract.Bound(
                Reason,
                CoopHideoutBossPhaseContract.MaximumReasonCharacters));
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() => MultiplayerMessageFilter.Mission;

        protected override string OnGetLogFormat()
        {
            return "CoopHideoutBossPhaseState Battle=" + BattleInstanceId +
                   " Revision=" + Revision +
                   " Phase=" + Phase +
                   " Choice=" + Choice +
                   " HostPeer=" + HostPeerIndex +
                   " HostAgent=" + HostAgentIndex +
                   " BossAgent=" + BossAgentIndex;
        }
    }
}
