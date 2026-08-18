using System;
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
            NormalizedTimeOfDay = ClampUnit(
                state?.NormalizedTimeOfDay ?? 0);
            SeasonTimeFactor = ClampUnit(state?.SeasonTimeFactor ?? 0);
            ServerTimeMilliseconds = ClampPositive(
                state?.ServerTimeMilliseconds ?? 0);
            VisibleEntitiesRevision = ClampPositive(
                state?.VisibleEntitiesRevision ?? 0);
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

        public int NormalizedTimeOfDay { get; private set; }

        public int SeasonTimeFactor { get; private set; }

        public int ServerTimeMilliseconds { get; private set; }

        public int VisibleEntitiesRevision { get; private set; }

        public CoopCampaignMapPrototypeCameraState Camera { get; private set; }

        public CoopCampaignMapPrototypeState ToState()
        {
            return new CoopCampaignMapPrototypeState
            {
                Revision = Revision,
                NormalizedX = NormalizedX,
                NormalizedY = NormalizedY,
                Heading = Heading,
                NormalizedTimeOfDay = NormalizedTimeOfDay,
                SeasonTimeFactor = SeasonTimeFactor,
                ServerTimeMilliseconds = ServerTimeMilliseconds,
                VisibleEntitiesRevision = VisibleEntitiesRevision,
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
            NormalizedTimeOfDay = ReadIntFromPacket(UnitCompression, ref valid);
            SeasonTimeFactor = ReadIntFromPacket(UnitCompression, ref valid);
            ServerTimeMilliseconds = ReadIntFromPacket(TimeCompression, ref valid);
            VisibleEntitiesRevision = ReadIntFromPacket(
                RevisionCompression,
                ref valid);
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
            WriteIntToPacket(ClampUnit(NormalizedTimeOfDay), UnitCompression);
            WriteIntToPacket(ClampUnit(SeasonTimeFactor), UnitCompression);
            WriteIntToPacket(
                ClampPositive(ServerTimeMilliseconds),
                TimeCompression);
            WriteIntToPacket(
                ClampPositive(VisibleEntitiesRevision),
                RevisionCompression);
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
                   " TimeOfDay=" + NormalizedTimeOfDay +
                   " Season=" + SeasonTimeFactor +
                   " ServerMs=" + ServerTimeMilliseconds +
                   " EntitiesRevision=" + VisibleEntitiesRevision +
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

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopCampaignMapPrototypeEntitySnapshotMessage :
        GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer ProtocolCompression =
            new CompressionInfo.Integer(0, 15, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer RevisionCompression =
            new CompressionInfo.Integer(0, int.MaxValue, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer CountCompression =
            new CompressionInfo.Integer(
                0,
                CoopCampaignMapPrototypeContract.MaxVisibleEntities,
                maximumValueGiven: true);

        public CoopCampaignMapPrototypeEntitySnapshotMessage(
            int revision,
            int entityCount)
        {
            ProtocolVersion = CoopCampaignMapPrototypeContract.ProtocolVersion;
            Revision = Math.Max(0, revision);
            EntityCount = Math.Max(
                0,
                Math.Min(
                    CoopCampaignMapPrototypeContract.MaxVisibleEntities,
                    entityCount));
        }

        public CoopCampaignMapPrototypeEntitySnapshotMessage()
        {
        }

        public int ProtocolVersion { get; private set; }

        public int Revision { get; private set; }

        public int EntityCount { get; private set; }

        protected override bool OnRead()
        {
            bool valid = true;
            ProtocolVersion = ReadIntFromPacket(ProtocolCompression, ref valid);
            Revision = ReadIntFromPacket(RevisionCompression, ref valid);
            EntityCount = ReadIntFromPacket(CountCompression, ref valid);
            return valid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket(ProtocolVersion, ProtocolCompression);
            WriteIntToPacket(Math.Max(0, Revision), RevisionCompression);
            WriteIntToPacket(
                Math.Max(
                    0,
                    Math.Min(
                        CoopCampaignMapPrototypeContract.MaxVisibleEntities,
                        EntityCount)),
                CountCompression);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() =>
            MultiplayerMessageFilter.Mission;

        protected override string OnGetLogFormat()
        {
            return "CoopCampaignMapPrototypeEntitySnapshot Revision=" +
                   Revision + " Count=" + EntityCount;
        }
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopCampaignMapPrototypeEntityStateMessage :
        GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer ProtocolCompression =
            new CompressionInfo.Integer(0, 15, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer RevisionCompression =
            new CompressionInfo.Integer(0, int.MaxValue, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer CountCompression =
            new CompressionInfo.Integer(
                0,
                CoopCampaignMapPrototypeContract.MaxVisibleEntities,
                maximumValueGiven: true);
        private static readonly CompressionInfo.Integer IndexCompression =
            new CompressionInfo.Integer(
                0,
                CoopCampaignMapPrototypeContract.MaxVisibleEntities - 1,
                maximumValueGiven: true);
        private static readonly CompressionInfo.Integer KindCompression =
            new CompressionInfo.Integer(0, 2, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer
            SettlementNameplateSizeCompression =
                new CompressionInfo.Integer(0, 3, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer UnitCompression =
            new CompressionInfo.Integer(
                0,
                CoopCampaignMapPrototypeContract.UnitScale,
                maximumValueGiven: true);
        private static readonly CompressionInfo.Integer PartySizeCompression =
            new CompressionInfo.Integer(
                0,
                CoopCampaignMapPrototypeContract.MaximumPartySize,
                maximumValueGiven: true);
        private static readonly CompressionInfo.UnsignedInteger ColorCompression =
            new CompressionInfo.UnsignedInteger(0u, 32);

        public CoopCampaignMapPrototypeEntityStateMessage(
            int revision,
            int index,
            int expectedCount,
            CoopCampaignMapPrototypeEntityState entity)
        {
            ProtocolVersion = CoopCampaignMapPrototypeContract.ProtocolVersion;
            Revision = Math.Max(0, revision);
            Index = Math.Max(
                0,
                Math.Min(
                    CoopCampaignMapPrototypeContract.MaxVisibleEntities - 1,
                    index));
            ExpectedCount = Math.Max(
                1,
                Math.Min(
                    CoopCampaignMapPrototypeContract.MaxVisibleEntities,
                    expectedCount));
            Entity = entity?.Clone();
        }

        public CoopCampaignMapPrototypeEntityStateMessage()
        {
        }

        public int ProtocolVersion { get; private set; }

        public int Revision { get; private set; }

        public int Index { get; private set; }

        public int ExpectedCount { get; private set; }

        public CoopCampaignMapPrototypeEntityState Entity { get; private set; }

        protected override bool OnRead()
        {
            bool valid = true;
            ProtocolVersion = ReadIntFromPacket(ProtocolCompression, ref valid);
            Revision = ReadIntFromPacket(RevisionCompression, ref valid);
            Index = ReadIntFromPacket(IndexCompression, ref valid);
            ExpectedCount = ReadIntFromPacket(CountCompression, ref valid);
            string entityId = ReadStringFromPacket(ref valid) ?? string.Empty;
            string displayName = ReadStringFromPacket(ref valid) ?? string.Empty;
            string bannerCode = ReadStringFromPacket(ref valid) ?? string.Empty;
            Entity = new CoopCampaignMapPrototypeEntityState
            {
                EntityId = entityId,
                DisplayName = displayName,
                BannerCode = bannerCode,
                Kind = (CoopCampaignMapPrototypeEntityKind)
                    ReadIntFromPacket(KindCompression, ref valid),
                SettlementNameplateSize =
                    (CoopCampaignMapPrototypeSettlementNameplateSize)
                        ReadIntFromPacket(
                            SettlementNameplateSizeCompression,
                            ref valid),
                NormalizedX = ReadIntFromPacket(UnitCompression, ref valid),
                NormalizedY = ReadIntFromPacket(UnitCompression, ref valid),
                Heading = ReadIntFromPacket(UnitCompression, ref valid),
                PartySize = ReadIntFromPacket(
                    PartySizeCompression,
                    ref valid),
                PrimaryColor = ReadUintFromPacket(
                    ColorCompression,
                    ref valid),
                SecondaryColor = ReadUintFromPacket(
                    ColorCompression,
                    ref valid)
            };
            return valid &&
                   ExpectedCount > 0 &&
                   Index < ExpectedCount &&
                   CoopCampaignMapPrototypeContract.IsValidVisibleEntity(Entity);
        }

        protected override void OnWrite()
        {
            CoopCampaignMapPrototypeEntityState safeEntity =
                Entity ?? new CoopCampaignMapPrototypeEntityState
                {
                    EntityId = "invalid",
                    DisplayName = "Invalid",
                    Kind = CoopCampaignMapPrototypeEntityKind.MobileParty
                };
            WriteIntToPacket(ProtocolVersion, ProtocolCompression);
            WriteIntToPacket(Math.Max(0, Revision), RevisionCompression);
            WriteIntToPacket(
                Math.Max(
                    0,
                    Math.Min(
                        CoopCampaignMapPrototypeContract.MaxVisibleEntities - 1,
                        Index)),
                IndexCompression);
            WriteIntToPacket(
                Math.Max(
                    1,
                    Math.Min(
                        CoopCampaignMapPrototypeContract.MaxVisibleEntities,
                        ExpectedCount)),
                CountCompression);
            WriteStringToPacket(
                CoopCampaignMapPrototypeContract.BoundEntityText(
                    safeEntity.EntityId,
                    CoopCampaignMapPrototypeContract.MaxEntityIdCharacters,
                    "invalid"));
            WriteStringToPacket(
                CoopCampaignMapPrototypeContract.BoundEntityText(
                    safeEntity.DisplayName,
                    CoopCampaignMapPrototypeContract.MaxEntityNameCharacters,
                    "Invalid"));
            WriteStringToPacket(
                CoopCampaignMapPrototypeContract.BoundEntityText(
                    safeEntity.BannerCode,
                    CoopCampaignMapPrototypeContract.MaxBannerCodeCharacters,
                    string.Empty));
            WriteIntToPacket((int)safeEntity.Kind, KindCompression);
            WriteIntToPacket(
                (int)safeEntity.SettlementNameplateSize,
                SettlementNameplateSizeCompression);
            WriteIntToPacket(ClampUnit(safeEntity.NormalizedX), UnitCompression);
            WriteIntToPacket(ClampUnit(safeEntity.NormalizedY), UnitCompression);
            WriteIntToPacket(ClampUnit(safeEntity.Heading), UnitCompression);
            WriteIntToPacket(
                Math.Max(
                    0,
                    Math.Min(
                        CoopCampaignMapPrototypeContract.MaximumPartySize,
                        safeEntity.PartySize)),
                PartySizeCompression);
            WriteUintToPacket(safeEntity.PrimaryColor, ColorCompression);
            WriteUintToPacket(safeEntity.SecondaryColor, ColorCompression);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() =>
            MultiplayerMessageFilter.Mission;

        protected override string OnGetLogFormat()
        {
            return "CoopCampaignMapPrototypeEntityState Revision=" +
                   Revision + " Index=" + Index + "/" + ExpectedCount +
                   " Entity=" + (Entity?.EntityId ?? "null");
        }

        private static int ClampUnit(int value)
        {
            return value < 0
                ? 0
                : value > CoopCampaignMapPrototypeContract.UnitScale
                    ? CoopCampaignMapPrototypeContract.UnitScale
                    : value;
        }
    }
}
