using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Network.Messages
{
    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopCampaignMapPrototypeStateMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer ProtocolCompression =
            new CompressionInfo.Integer(0, CoopCampaignMapPrototypeContract.ProtocolCompressionMaximum, maximumValueGiven: true);
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
            SessionId = state?.SessionId ?? string.Empty;
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
            CatalogRevision = ClampPositive(state?.CatalogRevision ?? 0);
            DynamicRevision = ClampPositive(state?.DynamicRevision ?? 0);
            MovementTarget = CoopCampaignMapMovementTargetCodec.TryDecode(state?.MovementTarget, out _)
                ? state.MovementTarget : string.Empty;
            Camera = CoopCampaignMapPrototypeContract.IsValidCameraState(
                state?.Camera)
                ? state?.Camera?.Clone()
                : null;
        }

        public CoopCampaignMapPrototypeStateMessage()
        {
        }

        public int ProtocolVersion { get; private set; }
        public string SessionId { get; private set; } = string.Empty;

        public int Revision { get; private set; }

        public int NormalizedX { get; private set; }

        public int NormalizedY { get; private set; }

        public int Heading { get; private set; }

        public int NormalizedTimeOfDay { get; private set; }

        public int SeasonTimeFactor { get; private set; }

        public int ServerTimeMilliseconds { get; private set; }

        public int VisibleEntitiesRevision { get; private set; }

        public int CatalogRevision { get; private set; }

        public int DynamicRevision { get; private set; }

        public CoopCampaignMapPrototypeCameraState Camera { get; private set; }

        public string MovementTarget { get; private set; } = string.Empty;

        public CoopCampaignMapPrototypeState ToState()
        {
            return new CoopCampaignMapPrototypeState
            {
                SessionId = SessionId,
                Revision = Revision,
                NormalizedX = NormalizedX,
                NormalizedY = NormalizedY,
                Heading = Heading,
                NormalizedTimeOfDay = NormalizedTimeOfDay,
                SeasonTimeFactor = SeasonTimeFactor,
                ServerTimeMilliseconds = ServerTimeMilliseconds,
                VisibleEntitiesRevision = VisibleEntitiesRevision,
                CatalogRevision = CatalogRevision,
                DynamicRevision = DynamicRevision,
                Camera = Camera?.Clone(),
                MovementTarget = MovementTarget
            };
        }

        protected override bool OnRead()
        {
            bool valid = true;
            ProtocolVersion = ReadIntFromPacket(ProtocolCompression, ref valid);
            if (!valid || ProtocolVersion != CoopCampaignMapPrototypeContract.ProtocolVersion) return false;
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
            CatalogRevision = ReadIntFromPacket(RevisionCompression, ref valid);
            DynamicRevision = ReadIntFromPacket(RevisionCompression, ref valid);
            bool hasCamera = ReadBoolFromPacket(ref valid);
            Camera = hasCamera
                ? ReadCamera(ref valid)
                : null;
            MovementTarget = ReadStringFromPacket(ref valid);
            SessionId = ReadStringFromPacket(ref valid);
            valid &= Guid.TryParseExact(SessionId, "N", out var session) && session != Guid.Empty;
            valid &= CoopCampaignMapMovementTargetCodec.TryDecode(MovementTarget, out _);
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
            WriteIntToPacket(ClampPositive(CatalogRevision), RevisionCompression);
            WriteIntToPacket(ClampPositive(DynamicRevision), RevisionCompression);
            bool hasCamera =
                CoopCampaignMapPrototypeContract.IsValidCameraState(Camera) &&
                Camera != null;
            WriteBoolToPacket(hasCamera);
            if (hasCamera)
                WriteCamera(Camera);
            WriteStringToPacket(MovementTarget);
            WriteStringToPacket(SessionId);
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
                   " CatalogRevision=" + CatalogRevision +
                   " DynamicRevision=" + DynamicRevision +
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
            new CompressionInfo.Integer(0, CoopCampaignMapPrototypeContract.ProtocolCompressionMaximum, maximumValueGiven: true);
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
            if (!valid || ProtocolVersion != CoopCampaignMapPrototypeContract.ProtocolVersion) return false;
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
            new CompressionInfo.Integer(0, CoopCampaignMapPrototypeContract.ProtocolCompressionMaximum, maximumValueGiven: true);
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
        private static readonly CompressionInfo.Integer PartyVisualKindCompression =
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
        private static readonly CompressionInfo.Integer VisualRaceCompression =
            new CompressionInfo.Integer(
                0,
                CoopCampaignMapPrototypeContract.MaximumVisualRace,
                maximumValueGiven: true);
        private static readonly CompressionInfo.Integer SkeletonTypeCompression =
            new CompressionInfo.Integer(
                0,
                CoopCampaignMapPrototypeContract.MaximumSkeletonType,
                maximumValueGiven: true);
        private static readonly CompressionInfo.Integer WieldedIndexCompression =
            new CompressionInfo.Integer(
                0,
                CoopCampaignMapPrototypeContract.EquipmentSlotCount,
                maximumValueGiven: true);
        private static readonly CompressionInfo.Integer QuestMarkerCompression =
            new CompressionInfo.Integer(
                0,
                CoopCampaignMapPrototypeContract.MaxQuestMarkerFlags,
                maximumValueGiven: true);
        private static readonly CompressionInfo.Integer SettlementEventCompression =
            new CompressionInfo.Integer(
                0,
                CoopCampaignMapPrototypeContract.MaxSettlementEventFlags,
                maximumValueGiven: true);
        private static readonly CompressionInfo.Integer PartyFlagsCompression =
            new CompressionInfo.Integer(0, CoopCampaignMapPrototypeContract.MaxPartyNameplateFlags, true);
        private static readonly CompressionInfo.Integer SettlementMapEventTypeCompression = new CompressionInfo.Integer(0, 5, true);
        private static readonly CompressionInfo.Integer SettlementRelationCompression =
            new CompressionInfo.Integer(0, CoopCampaignMapSettlementRelationPolicy.Maximum, true);
        private static readonly CompressionInfo.Integer EventOrderCompression =
            new CompressionInfo.Integer(0, CoopCampaignMapSettlementMarkerOrder.MaxEventOrder, true);
        private static readonly CompressionInfo.Integer QuestOrderCompression =
            new CompressionInfo.Integer(0, CoopCampaignMapSettlementMarkerOrder.MaxQuestOrder, true);

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
            if (!valid || ProtocolVersion != CoopCampaignMapPrototypeContract.ProtocolVersion) return false;
            Revision = ReadIntFromPacket(RevisionCompression, ref valid);
            Index = ReadIntFromPacket(IndexCompression, ref valid);
            ExpectedCount = ReadIntFromPacket(CountCompression, ref valid);
            string entityId = ReadStringFromPacket(ref valid) ?? string.Empty;
            string displayName = ReadStringFromPacket(ref valid) ?? string.Empty;
            string nameplateName = ReadStringFromPacket(ref valid) ?? string.Empty;
            string bannerCode = ReadStringFromPacket(ref valid) ?? string.Empty;
            string visualCharacterId =
                ReadStringFromPacket(ref valid) ?? string.Empty;
            string cultureId = ReadStringFromPacket(ref valid) ?? string.Empty;
            string settlementProductionIconId =
                ReadStringFromPacket(ref valid) ?? string.Empty;
            Entity = new CoopCampaignMapPrototypeEntityState
            {
                EntityId = entityId,
                DisplayName = displayName,
                PartyNameplateNameText = nameplateName,
                BannerCode = bannerCode,
                VisualCharacterId = visualCharacterId,
                CultureId = cultureId,
                Kind = (CoopCampaignMapPrototypeEntityKind)
                    ReadIntFromPacket(KindCompression, ref valid),
                SettlementNameplateSize =
                    (CoopCampaignMapPrototypeSettlementNameplateSize)
                        ReadIntFromPacket(
                            SettlementNameplateSizeCompression,
                            ref valid),
                PartyVisualKind =
                    (CoopCampaignMapPrototypePartyVisualKind)
                        ReadIntFromPacket(
                            PartyVisualKindCompression,
                            ref valid),
                QuestMarkerFlags = ReadIntFromPacket(
                    QuestMarkerCompression,
                    ref valid),
                SettlementEventFlags = ReadIntFromPacket(
                    SettlementEventCompression,
                    ref valid),
                SettlementProductionIconId = settlementProductionIconId,
                SettlementEventOrder = ReadIntFromPacket(EventOrderCompression, ref valid),
                SettlementQuestOrder = ReadIntFromPacket(QuestOrderCompression, ref valid),
                IsSettlementTracked = ReadBoolFromPacket(ref valid),
                SettlementRelation = ReadIntFromPacket(SettlementRelationCompression, ref valid),
                SettlementMapEventType = ReadIntFromPacket(SettlementMapEventTypeCompression, ref valid),
                IsSettlementInRange = ReadBoolFromPacket(ref valid),
                NormalizedX = ReadIntFromPacket(UnitCompression, ref valid),
                NormalizedY = ReadIntFromPacket(UnitCompression, ref valid),
                Heading = ReadIntFromPacket(UnitCompression, ref valid),
                PartySize = ReadIntFromPacket(
                    PartySizeCompression,
                    ref valid),
                IsPartyInformationHidden = ReadBoolFromPacket(ref valid),
                PartyNameplateCountText = ReadStringFromPacket(ref valid) ?? string.Empty,
                PartyNameplateExtraInfoText = ReadStringFromPacket(ref valid) ?? string.Empty,
                PartyMovementSpeedText = ReadStringFromPacket(ref valid) ?? string.Empty,
                PartyNameplateFlags = ReadIntFromPacket(PartyFlagsCompression, ref valid),
                PrimaryColor = ReadUintFromPacket(
                    ColorCompression,
                    ref valid),
                SecondaryColor = ReadUintFromPacket(
                    ColorCompression,
                    ref valid),
                NameplateColor = ReadUintFromPacket(
                    ColorCompression,
                    ref valid),
                HumanVisual = ReadAgentVisual(ref valid),
                MountVisual = ReadAgentVisual(ref valid),
                CaravanMountVisual = ReadAgentVisual(ref valid)
            };
            Entity.SettlementLevelFlags = ReadIntFromPacket(UnitCompression, ref valid);
            Entity.SettlementWallStates = ReadStringFromPacket(ref valid) ?? string.Empty;
            Entity.SettlementPreparationState = ReadIntFromPacket(UnitCompression, ref valid);
            Entity.SettlementRaidState = ReadIntFromPacket(UnitCompression, ref valid);
            Entity.HoverDecal = ReadStringFromPacket(ref valid) ?? string.Empty;
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
                    Kind = CoopCampaignMapPrototypeEntityKind.MobileParty,
                    BannerCode = string.Empty,
                    VisualCharacterId = string.Empty,
                    CultureId = string.Empty,
                    PartyVisualKind =
                        CoopCampaignMapPrototypePartyVisualKind.None
                };
            if (!string.IsNullOrEmpty(safeEntity.SettlementPartyBadges))
                throw new InvalidOperationException("Settlement badges require chunked catalog transport.");
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
            WriteStringToPacket(CoopCampaignMapPrototypeContract.BoundEntityText(
                safeEntity.PartyNameplateNameText,
                CoopCampaignMapPrototypeContract.MaxPartyNameplateNameCharacters, string.Empty));
            WriteStringToPacket(
                CoopCampaignMapPrototypeContract.BoundEntityText(
                    safeEntity.BannerCode,
                    CoopCampaignMapPrototypeContract.MaxBannerCodeCharacters,
                    string.Empty));
            WriteStringToPacket(
                CoopCampaignMapPrototypeContract.BoundEntityText(
                    safeEntity.VisualCharacterId,
                    CoopCampaignMapPrototypeContract.MaxVisualCharacterIdCharacters,
                    string.Empty));
            WriteStringToPacket(
                CoopCampaignMapPrototypeContract.BoundEntityText(
                    safeEntity.CultureId,
                    CoopCampaignMapPrototypeContract.MaxCultureIdCharacters,
                    string.Empty));
            WriteStringToPacket(
                CoopCampaignMapPrototypeContract.BoundEntityText(
                    safeEntity.SettlementProductionIconId,
                    CoopCampaignMapPrototypeContract.MaxProductionIconIdCharacters,
                    string.Empty));
            WriteIntToPacket((int)safeEntity.Kind, KindCompression);
            WriteIntToPacket(
                (int)safeEntity.SettlementNameplateSize,
                SettlementNameplateSizeCompression);
            WriteIntToPacket(
                (int)safeEntity.PartyVisualKind,
                PartyVisualKindCompression);
            WriteIntToPacket(
                ClampFlags(
                    safeEntity.QuestMarkerFlags,
                    CoopCampaignMapPrototypeContract.MaxQuestMarkerFlags),
                QuestMarkerCompression);
            WriteIntToPacket(
                ClampFlags(
                    safeEntity.SettlementEventFlags,
                    CoopCampaignMapPrototypeContract.MaxSettlementEventFlags),
                SettlementEventCompression);
            WriteIntToPacket(safeEntity.SettlementEventOrder, EventOrderCompression);
            WriteIntToPacket(safeEntity.SettlementQuestOrder, QuestOrderCompression);
            WriteBoolToPacket(safeEntity.IsSettlementTracked);
            WriteIntToPacket(safeEntity.SettlementRelation, SettlementRelationCompression);
            WriteIntToPacket(safeEntity.SettlementMapEventType, SettlementMapEventTypeCompression);
            WriteBoolToPacket(safeEntity.IsSettlementInRange);
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
            WriteBoolToPacket(safeEntity.IsPartyInformationHidden);
            WriteStringToPacket(safeEntity.PartyNameplateCountText ?? string.Empty);
            WriteStringToPacket(safeEntity.PartyNameplateExtraInfoText ?? string.Empty);
            WriteStringToPacket(safeEntity.PartyMovementSpeedText ?? string.Empty);
            WriteIntToPacket(safeEntity.PartyNameplateFlags, PartyFlagsCompression);
            WriteUintToPacket(safeEntity.PrimaryColor, ColorCompression);
            WriteUintToPacket(safeEntity.SecondaryColor, ColorCompression);
            WriteUintToPacket(safeEntity.NameplateColor, ColorCompression);
            WriteAgentVisual(safeEntity.HumanVisual);
            WriteAgentVisual(safeEntity.MountVisual);
            WriteAgentVisual(safeEntity.CaravanMountVisual);
            WriteIntToPacket(safeEntity.SettlementLevelFlags, UnitCompression);
            WriteStringToPacket(safeEntity.SettlementWallStates ?? string.Empty);
            WriteIntToPacket(safeEntity.SettlementPreparationState, UnitCompression);
            WriteIntToPacket(safeEntity.SettlementRaidState, UnitCompression);
            WriteStringToPacket(safeEntity.HoverDecal ?? string.Empty);
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

        private static int ClampFlags(int value, int maximum)
        {
            return value < 0 ? 0 : value > maximum ? maximum : value;
        }

        private CoopCampaignMapPrototypeAgentVisualState ReadAgentVisual(
            ref bool valid)
        {
            if (!ReadBoolFromPacket(ref valid))
                return null;

            string bodyProperties =
                ReadStringFromPacket(ref valid) ?? string.Empty;
            bool isFemale = ReadBoolFromPacket(ref valid);
            int race = ReadIntFromPacket(VisualRaceCompression, ref valid);
            int skeletonType =
                ReadIntFromPacket(SkeletonTypeCompression, ref valid);
            int rightWieldedItemIndex =
                ReadIntFromPacket(WieldedIndexCompression, ref valid) - 1;
            int leftWieldedItemIndex =
                ReadIntFromPacket(WieldedIndexCompression, ref valid) - 1;
            string mountCreationKey =
                ReadStringFromPacket(ref valid) ?? string.Empty;
            bool hasBanner = ReadBoolFromPacket(ref valid);
            bool addColorRandomness = ReadBoolFromPacket(ref valid);
            var itemIds = new string[
                CoopCampaignMapPrototypeContract.EquipmentSlotCount];
            for (int slot = 0; slot < itemIds.Length; slot++)
                itemIds[slot] = ReadStringFromPacket(ref valid) ?? string.Empty;

            return new CoopCampaignMapPrototypeAgentVisualState
            {
                BodyProperties = bodyProperties,
                IsFemale = isFemale,
                Race = race,
                SkeletonType = skeletonType,
                RightWieldedItemIndex = rightWieldedItemIndex,
                LeftWieldedItemIndex = leftWieldedItemIndex,
                MountCreationKey = mountCreationKey,
                HasBanner = hasBanner,
                AddColorRandomness = addColorRandomness,
                EquipmentItemIds = itemIds
            };
        }

        private void WriteAgentVisual(
            CoopCampaignMapPrototypeAgentVisualState visual)
        {
            bool isValid = visual != null &&
                           CoopCampaignMapPrototypeContract.IsValidAgentVisualState(
                               visual,
                               requireBodyProperties: false);
            WriteBoolToPacket(isValid);
            if (!isValid)
                return;

            WriteStringToPacket(
                CoopCampaignMapPrototypeContract.BoundEntityText(
                    visual.BodyProperties,
                    CoopCampaignMapPrototypeContract.MaxBodyPropertiesCharacters,
                    string.Empty));
            WriteBoolToPacket(visual.IsFemale);
            WriteIntToPacket(
                Math.Max(
                    0,
                    Math.Min(
                        CoopCampaignMapPrototypeContract.MaximumVisualRace,
                        visual.Race)),
                VisualRaceCompression);
            WriteIntToPacket(
                Math.Max(
                    0,
                    Math.Min(
                        CoopCampaignMapPrototypeContract.MaximumSkeletonType,
                        visual.SkeletonType)),
                SkeletonTypeCompression);
            WriteIntToPacket(
                ClampWieldedIndex(visual.RightWieldedItemIndex) + 1,
                WieldedIndexCompression);
            WriteIntToPacket(
                ClampWieldedIndex(visual.LeftWieldedItemIndex) + 1,
                WieldedIndexCompression);
            WriteStringToPacket(
                CoopCampaignMapPrototypeContract.BoundEntityText(
                    visual.MountCreationKey,
                    CoopCampaignMapPrototypeContract.MaxMountCreationKeyCharacters,
                    string.Empty));
            WriteBoolToPacket(visual.HasBanner);
            WriteBoolToPacket(visual.AddColorRandomness);
            for (int slot = 0;
                 slot < CoopCampaignMapPrototypeContract.EquipmentSlotCount;
                 slot++)
            {
                WriteStringToPacket(
                    CoopCampaignMapPrototypeContract.BoundEntityText(
                        visual.EquipmentItemIds[slot],
                        CoopCampaignMapPrototypeContract.MaxVisualItemIdCharacters,
                        string.Empty));
            }
        }

        private static int ClampWieldedIndex(int value)
        {
            return value < -1
                ? -1
                : value >= CoopCampaignMapPrototypeContract.EquipmentSlotCount
                    ? CoopCampaignMapPrototypeContract.EquipmentSlotCount - 1
                    : value;
        }
    }
}
