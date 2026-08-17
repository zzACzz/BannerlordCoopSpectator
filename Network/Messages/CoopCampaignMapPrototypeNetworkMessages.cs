using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Network.Messages
{
    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopCampaignMapPrototypeStateMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer ProtocolCompression =
            new CompressionInfo.Integer(0, 15, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer RevisionCompression =
            new CompressionInfo.Integer(0, int.MaxValue, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer UnitCompression =
            new CompressionInfo.Integer(
                0,
                CoopCampaignMapPrototypeContract.UnitScale,
                maximumValueGiven: true);
        private static readonly CompressionInfo.Integer TimeCompression =
            new CompressionInfo.Integer(0, int.MaxValue, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer WorldCoordinateCompression =
            new CompressionInfo.Integer(
                0,
                CoopCampaignMapPrototypeContract.WorldCoordinateQuantizedMaximum,
                maximumValueGiven: true);

        public CoopCampaignMapPrototypeStateMessage(
            CoopCampaignMapPrototypeState state)
        {
            ProtocolVersion = CoopCampaignMapPrototypeContract.ProtocolVersion;
            Revision = ClampPositive(state?.Revision ?? 0);
            NormalizedX = ClampUnit(state?.NormalizedX ?? 0);
            NormalizedY = ClampUnit(state?.NormalizedY ?? 0);
            Heading = ClampUnit(state?.Heading ?? 0);
            ServerTimeMilliseconds = ClampPositive(
                state?.ServerTimeMilliseconds ?? 0);
            Camera = CoopCampaignMapPrototypeContract.IsValidCameraState(
                state?.Camera)
                ? state?.Camera?.Clone()
                : null;
        }

        public CoopCampaignMapPrototypeStateMessage()
        {
        }

        public int ProtocolVersion { get; private set; }

        public int Revision { get; private set; }

        public int NormalizedX { get; private set; }

        public int NormalizedY { get; private set; }

        public int Heading { get; private set; }

        public int ServerTimeMilliseconds { get; private set; }

        public CoopCampaignMapPrototypeCameraState Camera { get; private set; }

        public CoopCampaignMapPrototypeState ToState()
        {
            return new CoopCampaignMapPrototypeState
            {
                Revision = Revision,
                NormalizedX = NormalizedX,
                NormalizedY = NormalizedY,
                Heading = Heading,
                ServerTimeMilliseconds = ServerTimeMilliseconds,
                Camera = Camera?.Clone()
            };
        }

        protected override bool OnRead()
        {
            bool valid = true;
            ProtocolVersion = ReadIntFromPacket(ProtocolCompression, ref valid);
            Revision = ReadIntFromPacket(RevisionCompression, ref valid);
            NormalizedX = ReadIntFromPacket(UnitCompression, ref valid);
            NormalizedY = ReadIntFromPacket(UnitCompression, ref valid);
            Heading = ReadIntFromPacket(UnitCompression, ref valid);
            ServerTimeMilliseconds = ReadIntFromPacket(TimeCompression, ref valid);
            bool hasCamera = ReadBoolFromPacket(ref valid);
            Camera = hasCamera
                ? ReadCamera(ref valid)
                : null;
            return valid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket(ProtocolVersion, ProtocolCompression);
            WriteIntToPacket(ClampPositive(Revision), RevisionCompression);
            WriteIntToPacket(ClampUnit(NormalizedX), UnitCompression);
            WriteIntToPacket(ClampUnit(NormalizedY), UnitCompression);
            WriteIntToPacket(ClampUnit(Heading), UnitCompression);
            WriteIntToPacket(
                ClampPositive(ServerTimeMilliseconds),
                TimeCompression);
            bool hasCamera =
                CoopCampaignMapPrototypeContract.IsValidCameraState(Camera) &&
                Camera != null;
            WriteBoolToPacket(hasCamera);
            if (hasCamera)
                WriteCamera(Camera);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() =>
            MultiplayerMessageFilter.Mission;

        protected override string OnGetLogFormat()
        {
            return "CoopCampaignMapPrototypeState Revision=" + Revision +
                   " X=" + NormalizedX +
                   " Y=" + NormalizedY +
                   " Heading=" + Heading +
                   " ServerMs=" + ServerTimeMilliseconds +
                   " Camera=" + (Camera != null);
        }

        private static CoopCampaignMapPrototypeCameraState ReadCamera(
            ref bool valid)
        {
            return new CoopCampaignMapPrototypeCameraState
            {
                OriginX = ReadIntFromPacket(WorldCoordinateCompression, ref valid),
                OriginY = ReadIntFromPacket(WorldCoordinateCompression, ref valid),
                OriginZ = ReadIntFromPacket(WorldCoordinateCompression, ref valid),
                DirectionX = ReadIntFromPacket(UnitCompression, ref valid),
                DirectionY = ReadIntFromPacket(UnitCompression, ref valid),
                DirectionZ = ReadIntFromPacket(UnitCompression, ref valid),
                UpX = ReadIntFromPacket(UnitCompression, ref valid),
                UpY = ReadIntFromPacket(UnitCompression, ref valid),
                UpZ = ReadIntFromPacket(UnitCompression, ref valid),
                VerticalFov = ReadIntFromPacket(UnitCompression, ref valid)
            };
        }

        private static void WriteCamera(
            CoopCampaignMapPrototypeCameraState camera)
        {
            WriteIntToPacket(camera.OriginX, WorldCoordinateCompression);
            WriteIntToPacket(camera.OriginY, WorldCoordinateCompression);
            WriteIntToPacket(camera.OriginZ, WorldCoordinateCompression);
            WriteIntToPacket(camera.DirectionX, UnitCompression);
            WriteIntToPacket(camera.DirectionY, UnitCompression);
            WriteIntToPacket(camera.DirectionZ, UnitCompression);
            WriteIntToPacket(camera.UpX, UnitCompression);
            WriteIntToPacket(camera.UpY, UnitCompression);
            WriteIntToPacket(camera.UpZ, UnitCompression);
            WriteIntToPacket(camera.VerticalFov, UnitCompression);
        }

        private static int ClampUnit(int value)
        {
            return value < 0
                ? 0
                : value > CoopCampaignMapPrototypeContract.UnitScale
                    ? CoopCampaignMapPrototypeContract.UnitScale
                    : value;
        }

        private static int ClampPositive(int value)
        {
            return value < 0 ? 0 : value;
        }
    }
}
