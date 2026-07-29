using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Network.Messages
{
    public enum CoopSiegeAssaultFormationOrderKind
    {
        AttackGate = 0,
        AssaultWalls = 1,
        UseSiegeMachines = 2,
        OccupyArcherPositions = 3,
        OccupyAttackerBarricades = 4
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromClient)]
    public sealed class CoopSiegeAssaultFormationOrderMessage : GameNetworkMessage
    {
        public const int MaxFormationMask = 0xFF;

        private static readonly CompressionInfo.Integer BattleSideCompression =
            new CompressionInfo.Integer(-1, 1, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer FormationMaskCompression =
            new CompressionInfo.Integer(0, MaxFormationMask, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer OrderKindCompression =
            new CompressionInfo.Integer(
                (int)CoopSiegeAssaultFormationOrderKind.AttackGate,
                (int)CoopSiegeAssaultFormationOrderKind.OccupyAttackerBarricades,
                maximumValueGiven: true);

        public CoopSiegeAssaultFormationOrderMessage(
            BattleSideEnum requestedSide,
            int formationMask,
            CoopSiegeAssaultFormationOrderKind orderKind)
        {
            RequestedSide = requestedSide;
            FormationMask = formationMask & MaxFormationMask;
            OrderKind = orderKind;
        }

        public CoopSiegeAssaultFormationOrderMessage()
        {
            RequestedSide = BattleSideEnum.None;
            OrderKind = CoopSiegeAssaultFormationOrderKind.AttackGate;
        }

        public BattleSideEnum RequestedSide { get; private set; }

        public int FormationMask { get; private set; }

        public CoopSiegeAssaultFormationOrderKind OrderKind { get; private set; }

        protected override bool OnRead()
        {
            bool valid = true;
            RequestedSide =
                (BattleSideEnum)ReadIntFromPacket(BattleSideCompression, ref valid);
            FormationMask = ReadIntFromPacket(FormationMaskCompression, ref valid);
            OrderKind =
                (CoopSiegeAssaultFormationOrderKind)ReadIntFromPacket(
                    OrderKindCompression,
                    ref valid);
            return valid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket((int)RequestedSide, BattleSideCompression);
            WriteIntToPacket(
                FormationMask & MaxFormationMask,
                FormationMaskCompression);
            WriteIntToPacket((int)OrderKind, OrderKindCompression);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter()
        {
            return MultiplayerMessageFilter.Mission;
        }

        protected override string OnGetLogFormat()
        {
            return "CoopSiegeAssaultFormationOrder Side=" +
                   RequestedSide +
                   " FormationMask=" +
                   FormationMask +
                   " Order=" +
                   OrderKind;
        }
    }
}
