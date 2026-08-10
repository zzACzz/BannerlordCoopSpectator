using CoopSpectator.Infrastructure.Hideout;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Network.Messages
{
    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromClient)]
    public sealed class CoopHideoutAmbushClientCommandMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer CommandCompression =
            new CompressionInfo.Integer(0, 1, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer RevisionCompression =
            new CompressionInfo.Integer(0, int.MaxValue, maximumValueGiven: true);

        public CoopHideoutAmbushClientCommandMessage(
            string battleInstanceId,
            int revision,
            CoopHideoutAmbushClientCommandKind commandKind)
        {
            BattleInstanceId = CoopHideoutAmbushContract.Bound(
                battleInstanceId,
                CoopHideoutAmbushContract.MaximumBattleInstanceIdCharacters);
            Revision = revision;
            CommandKind = commandKind;
        }

        public CoopHideoutAmbushClientCommandMessage()
        {
            BattleInstanceId = string.Empty;
        }

        public string BattleInstanceId { get; private set; }

        public int Revision { get; private set; }

        public CoopHideoutAmbushClientCommandKind CommandKind { get; private set; }

        protected override bool OnRead()
        {
            bool valid = true;
            BattleInstanceId = ReadStringFromPacket(ref valid) ?? string.Empty;
            Revision = ReadIntFromPacket(RevisionCompression, ref valid);
            CommandKind = (CoopHideoutAmbushClientCommandKind)
                ReadIntFromPacket(CommandCompression, ref valid);
            return valid &&
                   BattleInstanceId.Length <=
                   CoopHideoutAmbushContract.MaximumBattleInstanceIdCharacters;
        }

        protected override void OnWrite()
        {
            WriteStringToPacket(CoopHideoutAmbushContract.Bound(
                BattleInstanceId,
                CoopHideoutAmbushContract.MaximumBattleInstanceIdCharacters));
            WriteIntToPacket(Revision, RevisionCompression);
            WriteIntToPacket((int)CommandKind, CommandCompression);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() =>
            MultiplayerMessageFilter.Mission;

        protected override string OnGetLogFormat()
        {
            return "CoopHideoutAmbushClientCommand Battle=" + BattleInstanceId +
                   " Revision=" + Revision +
                   " Kind=" + CommandKind;
        }
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopHideoutAmbushStateMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer ProtocolCompression =
            new CompressionInfo.Integer(0, 15, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer RevisionCompression =
            new CompressionInfo.Integer(0, int.MaxValue, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer PhaseCompression =
            new CompressionInfo.Integer(0, 6, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer AgentIndexCompression =
            new CompressionInfo.Integer(-1, 4095, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer SuspicionCompression =
            new CompressionInfo.Integer(0, 1000, maximumValueGiven: true);

        public CoopHideoutAmbushStateMessage(CoopHideoutAmbushState state)
        {
            ProtocolVersion = CoopHideoutAmbushContract.ProtocolVersion;
            BattleInstanceId = CoopHideoutAmbushContract.Bound(
                state?.BattleInstanceId,
                CoopHideoutAmbushContract.MaximumBattleInstanceIdCharacters);
            Revision = state?.Revision ?? 0;
            Phase = state?.Phase ?? CoopHideoutAmbushPhase.WaitingForMaterialization;
            GuardAgentIndex = state?.GuardAgentIndex ?? -1;
            ObservedAgentIndex = state?.ObservedAgentIndex ?? -1;
            SuspicionPermille = state?.SuspicionPermille ?? 0;
            IsAlarmed = state?.IsAlarmed ?? false;
            HasGlobalAlarm = state?.HasGlobalAlarm ?? false;
            IsUsePointAvailable = state?.IsUsePointAvailable ?? false;
            Reason = CoopHideoutAmbushContract.Bound(
                state?.Reason,
                CoopHideoutAmbushContract.MaximumReasonCharacters);
        }

        public CoopHideoutAmbushStateMessage()
        {
            BattleInstanceId = string.Empty;
            Reason = string.Empty;
        }

        public int ProtocolVersion { get; private set; }

        public string BattleInstanceId { get; private set; }

        public int Revision { get; private set; }

        public CoopHideoutAmbushPhase Phase { get; private set; }

        public int GuardAgentIndex { get; private set; }

        public int ObservedAgentIndex { get; private set; }

        public int SuspicionPermille { get; private set; }

        public bool IsAlarmed { get; private set; }

        public bool HasGlobalAlarm { get; private set; }

        public bool IsUsePointAvailable { get; private set; }

        public string Reason { get; private set; }

        public CoopHideoutAmbushState ToState()
        {
            return new CoopHideoutAmbushState
            {
                BattleInstanceId = BattleInstanceId,
                Revision = Revision,
                Phase = Phase,
                GuardAgentIndex = GuardAgentIndex,
                ObservedAgentIndex = ObservedAgentIndex,
                SuspicionPermille = SuspicionPermille,
                IsAlarmed = IsAlarmed,
                HasGlobalAlarm = HasGlobalAlarm,
                IsUsePointAvailable = IsUsePointAvailable,
                Reason = Reason
            };
        }

        protected override bool OnRead()
        {
            bool valid = true;
            ProtocolVersion = ReadIntFromPacket(ProtocolCompression, ref valid);
            BattleInstanceId = ReadStringFromPacket(ref valid) ?? string.Empty;
            Revision = ReadIntFromPacket(RevisionCompression, ref valid);
            Phase = (CoopHideoutAmbushPhase)
                ReadIntFromPacket(PhaseCompression, ref valid);
            GuardAgentIndex = ReadIntFromPacket(AgentIndexCompression, ref valid);
            ObservedAgentIndex = ReadIntFromPacket(AgentIndexCompression, ref valid);
            SuspicionPermille = ReadIntFromPacket(SuspicionCompression, ref valid);
            IsAlarmed = ReadBoolFromPacket(ref valid);
            HasGlobalAlarm = ReadBoolFromPacket(ref valid);
            IsUsePointAvailable = ReadBoolFromPacket(ref valid);
            Reason = ReadStringFromPacket(ref valid) ?? string.Empty;
            return valid &&
                   BattleInstanceId.Length <=
                   CoopHideoutAmbushContract.MaximumBattleInstanceIdCharacters &&
                   Reason.Length <= CoopHideoutAmbushContract.MaximumReasonCharacters;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket(ProtocolVersion, ProtocolCompression);
            WriteStringToPacket(CoopHideoutAmbushContract.Bound(
                BattleInstanceId,
                CoopHideoutAmbushContract.MaximumBattleInstanceIdCharacters));
            WriteIntToPacket(Revision, RevisionCompression);
            WriteIntToPacket((int)Phase, PhaseCompression);
            WriteIntToPacket(GuardAgentIndex, AgentIndexCompression);
            WriteIntToPacket(ObservedAgentIndex, AgentIndexCompression);
            WriteIntToPacket(SuspicionPermille, SuspicionCompression);
            WriteBoolToPacket(IsAlarmed);
            WriteBoolToPacket(HasGlobalAlarm);
            WriteBoolToPacket(IsUsePointAvailable);
            WriteStringToPacket(CoopHideoutAmbushContract.Bound(
                Reason,
                CoopHideoutAmbushContract.MaximumReasonCharacters));
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() =>
            MultiplayerMessageFilter.Mission;

        protected override string OnGetLogFormat()
        {
            return "CoopHideoutAmbushState Battle=" + BattleInstanceId +
                   " Revision=" + Revision +
                   " Phase=" + Phase +
                   " Guard=" + GuardAgentIndex +
                   " Suspicion=" + SuspicionPermille;
        }
    }
}
