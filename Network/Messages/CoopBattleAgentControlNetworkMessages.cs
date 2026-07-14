using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Network.Messages
{
    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromClient)]
    public sealed class CoopBattleAgentControlRequestMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer RequestKindCompression =
            new CompressionInfo.Integer(0, 1, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer AgentIndexCompression =
            new CompressionInfo.Integer(-1, 4095, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer RequestIdCompression =
            new CompressionInfo.Integer(0, int.MaxValue, maximumValueGiven: true);

        public CoopBattleAgentControlRequestMessage(
            CoopBattleAgentControlRequestKind requestKind,
            int expectedAgentIndex,
            int requestId)
        {
            RequestKind = requestKind;
            ExpectedAgentIndex = expectedAgentIndex;
            RequestId = requestId;
        }

        public CoopBattleAgentControlRequestMessage()
        {
        }

        public CoopBattleAgentControlRequestKind RequestKind { get; private set; }
        public int ExpectedAgentIndex { get; private set; }
        public int RequestId { get; private set; }

        protected override bool OnRead()
        {
            bool valid = true;
            RequestKind = (CoopBattleAgentControlRequestKind)ReadIntFromPacket(RequestKindCompression, ref valid);
            ExpectedAgentIndex = ReadIntFromPacket(AgentIndexCompression, ref valid);
            RequestId = ReadIntFromPacket(RequestIdCompression, ref valid);
            return valid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket((int)RequestKind, RequestKindCompression);
            WriteIntToPacket(ExpectedAgentIndex, AgentIndexCompression);
            WriteIntToPacket(RequestId, RequestIdCompression);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() => MultiplayerMessageFilter.Mission;

        protected override string OnGetLogFormat()
        {
            return "CoopBattleAgentControlRequest Kind=" + RequestKind +
                   " ExpectedAgentIndex=" + ExpectedAgentIndex +
                   " RequestId=" + RequestId;
        }
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopBattleAgentControlStateMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer ModeCompression =
            new CompressionInfo.Integer(0, 1, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer AgentIndexCompression =
            new CompressionInfo.Integer(-1, 4095, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer TeamIndexCompression =
            new CompressionInfo.Integer(-1, 64, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer FormationIndexCompression =
            new CompressionInfo.Integer(-1, 15, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer BattleSideCompression =
            new CompressionInfo.Integer(-1, 1, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer RevisionCompression =
            new CompressionInfo.Integer(0, int.MaxValue, maximumValueGiven: true);

        public CoopBattleAgentControlStateMessage(CoopBattleAgentControlState state)
        {
            Mode = state.Mode;
            AgentIndex = state.AgentIndex;
            EntryId = state.EntryId ?? string.Empty;
            Side = state.Side;
            TeamIndex = state.TeamIndex;
            FormationIndex = state.FormationIndex;
            Revision = state.Revision;
            AcknowledgedRequestId = state.LastRequestId;
        }

        public CoopBattleAgentControlStateMessage()
        {
            EntryId = string.Empty;
        }

        public CoopBattleAgentControlMode Mode { get; private set; }
        public int AgentIndex { get; private set; }
        public string EntryId { get; private set; }
        public BattleSideEnum Side { get; private set; }
        public int TeamIndex { get; private set; }
        public int FormationIndex { get; private set; }
        public int Revision { get; private set; }
        public int AcknowledgedRequestId { get; private set; }

        protected override bool OnRead()
        {
            bool valid = true;
            Mode = (CoopBattleAgentControlMode)ReadIntFromPacket(ModeCompression, ref valid);
            AgentIndex = ReadIntFromPacket(AgentIndexCompression, ref valid);
            EntryId = ReadStringFromPacket(ref valid) ?? string.Empty;
            Side = (BattleSideEnum)ReadIntFromPacket(BattleSideCompression, ref valid);
            TeamIndex = ReadIntFromPacket(TeamIndexCompression, ref valid);
            FormationIndex = ReadIntFromPacket(FormationIndexCompression, ref valid);
            Revision = ReadIntFromPacket(RevisionCompression, ref valid);
            AcknowledgedRequestId = ReadIntFromPacket(RevisionCompression, ref valid);
            return valid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket((int)Mode, ModeCompression);
            WriteIntToPacket(AgentIndex, AgentIndexCompression);
            WriteStringToPacket(EntryId ?? string.Empty);
            WriteIntToPacket((int)Side, BattleSideCompression);
            WriteIntToPacket(TeamIndex, TeamIndexCompression);
            WriteIntToPacket(FormationIndex, FormationIndexCompression);
            WriteIntToPacket(Revision, RevisionCompression);
            WriteIntToPacket(AcknowledgedRequestId, RevisionCompression);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() => MultiplayerMessageFilter.Mission;

        protected override string OnGetLogFormat()
        {
            return "CoopBattleAgentControlState Mode=" + Mode +
                   " AgentIndex=" + AgentIndex +
                   " Revision=" + Revision +
                   " AckRequestId=" + AcknowledgedRequestId;
        }
    }
}
