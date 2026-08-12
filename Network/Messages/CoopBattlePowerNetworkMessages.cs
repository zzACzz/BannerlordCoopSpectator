using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Network.Messages
{
    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopBattlePowerStateMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer ProtocolCompression =
            new CompressionInfo.Integer(0, 15, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer RevisionCompression =
            new CompressionInfo.Integer(0, int.MaxValue, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer PowerCompression =
            new CompressionInfo.Integer(0, int.MaxValue, maximumValueGiven: true);

        public CoopBattlePowerStateMessage(CoopBattlePowerState state)
        {
            ProtocolVersion = CoopBattlePowerContract.ProtocolVersion;
            BattleInstanceId = CoopBattlePowerContract.BoundBattleInstanceId(
                state?.BattleInstanceId);
            Revision = state?.Revision ?? 0;
            IsAvailable = state?.IsAvailable ?? false;
            InitialAttackerPower = ClampPower(state?.InitialAttackerPower ?? 0);
            CurrentAttackerPower = ClampPower(state?.CurrentAttackerPower ?? 0);
            InitialDefenderPower = ClampPower(state?.InitialDefenderPower ?? 0);
            CurrentDefenderPower = ClampPower(state?.CurrentDefenderPower ?? 0);
        }

        public CoopBattlePowerStateMessage()
        {
            BattleInstanceId = string.Empty;
        }

        public int ProtocolVersion { get; private set; }

        public string BattleInstanceId { get; private set; }

        public int Revision { get; private set; }

        public bool IsAvailable { get; private set; }

        public int InitialAttackerPower { get; private set; }

        public int CurrentAttackerPower { get; private set; }

        public int InitialDefenderPower { get; private set; }

        public int CurrentDefenderPower { get; private set; }

        public CoopBattlePowerState ToState()
        {
            return new CoopBattlePowerState
            {
                BattleInstanceId = BattleInstanceId,
                Revision = Revision,
                IsAvailable = IsAvailable,
                InitialAttackerPower = InitialAttackerPower,
                CurrentAttackerPower = CurrentAttackerPower,
                InitialDefenderPower = InitialDefenderPower,
                CurrentDefenderPower = CurrentDefenderPower
            };
        }

        protected override bool OnRead()
        {
            bool valid = true;
            ProtocolVersion = ReadIntFromPacket(ProtocolCompression, ref valid);
            BattleInstanceId = ReadStringFromPacket(ref valid) ?? string.Empty;
            Revision = ReadIntFromPacket(RevisionCompression, ref valid);
            IsAvailable = ReadBoolFromPacket(ref valid);
            InitialAttackerPower = ReadIntFromPacket(PowerCompression, ref valid);
            CurrentAttackerPower = ReadIntFromPacket(PowerCompression, ref valid);
            InitialDefenderPower = ReadIntFromPacket(PowerCompression, ref valid);
            CurrentDefenderPower = ReadIntFromPacket(PowerCompression, ref valid);
            return valid &&
                   BattleInstanceId.Length <=
                   CoopBattlePowerContract.MaximumBattleInstanceIdCharacters;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket(ProtocolVersion, ProtocolCompression);
            WriteStringToPacket(CoopBattlePowerContract.BoundBattleInstanceId(
                BattleInstanceId));
            WriteIntToPacket(Revision, RevisionCompression);
            WriteBoolToPacket(IsAvailable);
            WriteIntToPacket(ClampPower(InitialAttackerPower), PowerCompression);
            WriteIntToPacket(ClampPower(CurrentAttackerPower), PowerCompression);
            WriteIntToPacket(ClampPower(InitialDefenderPower), PowerCompression);
            WriteIntToPacket(ClampPower(CurrentDefenderPower), PowerCompression);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() =>
            MultiplayerMessageFilter.Mission;

        protected override string OnGetLogFormat()
        {
            return "CoopBattlePowerState Battle=" + BattleInstanceId +
                   " Revision=" + Revision +
                   " Available=" + IsAvailable +
                   " Attacker=" + CurrentAttackerPower + "/" + InitialAttackerPower +
                   " Defender=" + CurrentDefenderPower + "/" + InitialDefenderPower;
        }

        private static int ClampPower(int value)
        {
            return value < 0 ? 0 : value;
        }
    }
}
