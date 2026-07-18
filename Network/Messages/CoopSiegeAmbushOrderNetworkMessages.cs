using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Network.Messages
{
    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromClient)]
    public sealed class CoopSiegeAmbushDestroySiegeWeaponsOrderMessage : GameNetworkMessage
    {
        public const int MaxFormationMask = 0xFF;

        private static readonly CompressionInfo.Integer BattleSideCompression =
            new CompressionInfo.Integer(-1, 1, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer FormationMaskCompression =
            new CompressionInfo.Integer(0, MaxFormationMask, maximumValueGiven: true);

        public CoopSiegeAmbushDestroySiegeWeaponsOrderMessage(
            BattleSideEnum requestedSide,
            int formationMask)
        {
            RequestedSide = requestedSide;
            FormationMask = formationMask & MaxFormationMask;
        }

        public CoopSiegeAmbushDestroySiegeWeaponsOrderMessage()
        {
            RequestedSide = BattleSideEnum.None;
        }

        public BattleSideEnum RequestedSide { get; private set; }

        public int FormationMask { get; private set; }

        protected override bool OnRead()
        {
            bool valid = true;
            RequestedSide =
                (BattleSideEnum)ReadIntFromPacket(BattleSideCompression, ref valid);
            FormationMask = ReadIntFromPacket(FormationMaskCompression, ref valid);
            return valid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket((int)RequestedSide, BattleSideCompression);
            WriteIntToPacket(
                FormationMask & MaxFormationMask,
                FormationMaskCompression);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter()
        {
            return MultiplayerMessageFilter.Mission;
        }

        protected override string OnGetLogFormat()
        {
            return "CoopSiegeAmbushDestroySiegeWeaponsOrder Side=" +
                   RequestedSide +
                   " FormationMask=" +
                   FormationMask;
        }
    }
}
