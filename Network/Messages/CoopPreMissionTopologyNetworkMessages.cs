using System;
using System.Collections.Generic;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Network.Messages
{
    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopPreMissionTopologyContractMessage : GameNetworkMessage
    {
        public const int CurrentSchemaVersion = 2;
        public const int MaxWallRatioCount = 16;
        public const int MaxSiegeEngineCountPerSide = 32;

        private static readonly CompressionInfo.Integer SchemaVersionCompressionInfo =
            new CompressionInfo.Integer(0, 15, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer BattleIndexCompressionInfo =
            new CompressionInfo.Integer(-1, 15, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer SceneLevelCompressionInfo =
            new CompressionInfo.Integer(-1, 15, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer SceneEnumCompressionInfo =
            new CompressionInfo.Integer(-1, 255, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer CountCompressionInfo =
            new CompressionInfo.Integer(0, MaxSiegeEngineCountPerSide, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer WallRatioCompressionInfo =
            new CompressionInfo.Integer(0, 10000, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer SiegeEngineIndexCompressionInfo =
            new CompressionInfo.Integer(-1, 1023, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer SiegeEngineHealthCompressionInfo =
            new CompressionInfo.Integer(0, 100000000, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer MapPatchSceneIndexCompressionInfo =
            new CompressionInfo.Integer(-1, 32767, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer MapPatchNormalizedCompressionInfo =
            new CompressionInfo.Integer(0, 10000, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer MapPatchDirectionCompressionInfo =
            new CompressionInfo.Integer(-10000, 10000, maximumValueGiven: true);

        public CoopPreMissionTopologyContractMessage(
            int battleIndex,
            string battleId,
            string runtimeScene,
            string playerSide,
            int mapPatchSceneIndex,
            float mapPatchNormalizedX,
            float mapPatchNormalizedY,
            bool hasPatchEncounterDirection,
            float patchEncounterDirX,
            float patchEncounterDirY,
            string patchEncounterDirectionSource,
            BattleScenarioContextMessage scenarioContext,
            string contractHash)
        {
            SchemaVersion = CurrentSchemaVersion;
            BattleIndex = Clamp(battleIndex, -1, 15);
            BattleId = Normalize(battleId);
            RuntimeScene = Normalize(runtimeScene);
            PlayerSide = Normalize(playerSide);
            MapPatchSceneIndex = Clamp(mapPatchSceneIndex, -1, 32767);
            MapPatchNormalizedX = QuantizeNormalizedCoordinate(mapPatchNormalizedX) / 10000f;
            MapPatchNormalizedY = QuantizeNormalizedCoordinate(mapPatchNormalizedY) / 10000f;
            HasPatchEncounterDirection = hasPatchEncounterDirection;
            PatchEncounterDirX = QuantizeDirectionComponent(patchEncounterDirX) / 10000f;
            PatchEncounterDirY = QuantizeDirectionComponent(patchEncounterDirY) / 10000f;
            PatchEncounterDirectionSource = Normalize(patchEncounterDirectionSource);
            ScenarioContext = scenarioContext?.Clone();
            ContractHash = Normalize(contractHash);
        }

        public CoopPreMissionTopologyContractMessage()
        {
            SchemaVersion = CurrentSchemaVersion;
            BattleIndex = -1;
            BattleId = string.Empty;
            RuntimeScene = string.Empty;
            PlayerSide = string.Empty;
            MapPatchSceneIndex = -1;
            MapPatchNormalizedX = 0f;
            MapPatchNormalizedY = 0f;
            HasPatchEncounterDirection = false;
            PatchEncounterDirX = 0f;
            PatchEncounterDirY = 0f;
            PatchEncounterDirectionSource = string.Empty;
            ScenarioContext = null;
            ContractHash = string.Empty;
        }

        public int SchemaVersion { get; private set; }
        public int BattleIndex { get; private set; }
        public string BattleId { get; private set; }
        public string RuntimeScene { get; private set; }
        public string PlayerSide { get; private set; }
        public int MapPatchSceneIndex { get; private set; }
        public float MapPatchNormalizedX { get; private set; }
        public float MapPatchNormalizedY { get; private set; }
        public bool HasPatchEncounterDirection { get; private set; }
        public float PatchEncounterDirX { get; private set; }
        public float PatchEncounterDirY { get; private set; }
        public string PatchEncounterDirectionSource { get; private set; }
        public BattleScenarioContextMessage ScenarioContext { get; private set; }
        public string ContractHash { get; private set; }

        protected override bool OnRead()
        {
            bool bufferReadValid = true;
            SchemaVersion = ReadIntFromPacket(SchemaVersionCompressionInfo, ref bufferReadValid);
            BattleIndex = ReadIntFromPacket(BattleIndexCompressionInfo, ref bufferReadValid);
            BattleId = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty;
            RuntimeScene = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty;
            PlayerSide = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty;
            MapPatchSceneIndex = ReadIntFromPacket(MapPatchSceneIndexCompressionInfo, ref bufferReadValid);
            MapPatchNormalizedX =
                ReadIntFromPacket(MapPatchNormalizedCompressionInfo, ref bufferReadValid) / 10000f;
            MapPatchNormalizedY =
                ReadIntFromPacket(MapPatchNormalizedCompressionInfo, ref bufferReadValid) / 10000f;
            HasPatchEncounterDirection = ReadBoolFromPacket(ref bufferReadValid);
            PatchEncounterDirX =
                ReadIntFromPacket(MapPatchDirectionCompressionInfo, ref bufferReadValid) / 10000f;
            PatchEncounterDirY =
                ReadIntFromPacket(MapPatchDirectionCompressionInfo, ref bufferReadValid) / 10000f;
            PatchEncounterDirectionSource = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty;

            var scenarioContext = new BattleScenarioContextMessage
            {
                CampaignBattleType = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty,
                ScenarioKind = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty,
                IsSiegeBattle = ReadBoolFromPacket(ref bufferReadValid),
                Source = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty
            };

            bool hasSiegeContext = ReadBoolFromPacket(ref bufferReadValid);
            if (hasSiegeContext)
            {
                var siegeContext = new BattleSiegeContextMessage
                {
                    SiegeSubtype = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty,
                    MissionShell = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty,
                    SettlementId = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty,
                    SettlementKind = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty,
                    SettlementCultureId = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty,
                    SceneLocationId = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty,
                    CurrentSiegeState = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty,
                    WallLevel = ReadIntFromPacket(SceneLevelCompressionInfo, ref bufferReadValid),
                    HasAnySiegeTower = ReadBoolFromPacket(ref bufferReadValid),
                    HasMissionInitializerRecord = ReadBoolFromPacket(ref bufferReadValid),
                    MissionInitializerSource = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty,
                    MissionInitializerSceneName = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty,
                    MissionInitializerSceneLevels = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty,
                    MissionInitializerSceneUpgradeLevel = ReadIntFromPacket(SceneLevelCompressionInfo, ref bufferReadValid),
                    MissionInitializerPlayingInCampaignMode = ReadBoolFromPacket(ref bufferReadValid),
                    MissionInitializerSceneHasMapPatch = ReadBoolFromPacket(ref bufferReadValid),
                    MissionInitializerDecalAtlasGroup = ReadIntFromPacket(SceneEnumCompressionInfo, ref bufferReadValid),
                    MissionInitializerTerrainType = ReadIntFromPacket(SceneEnumCompressionInfo, ref bufferReadValid)
                };

                int wallRatioCount = Math.Min(
                    ReadIntFromPacket(CountCompressionInfo, ref bufferReadValid),
                    MaxWallRatioCount);
                for (int i = 0; i < wallRatioCount; i++)
                {
                    int scaledRatio = ReadIntFromPacket(WallRatioCompressionInfo, ref bufferReadValid);
                    siegeContext.WallHitPointRatios.Add(scaledRatio / 10000f);
                }

                ReadSiegeEngineSnapshots(
                    siegeContext.AttackerSiegeEngines,
                    ref bufferReadValid);
                ReadSiegeEngineSnapshots(
                    siegeContext.DefenderSiegeEngines,
                    ref bufferReadValid);
                ReadStringList(
                    siegeContext.AttackerSiegeEngineTypeIds,
                    ref bufferReadValid);
                ReadStringList(
                    siegeContext.DefenderSiegeEngineTypeIds,
                    ref bufferReadValid);
                scenarioContext.SiegeContext = siegeContext;
            }

            ScenarioContext = scenarioContext;
            ContractHash = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty;
            return bufferReadValid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket(Clamp(SchemaVersion, 0, 15), SchemaVersionCompressionInfo);
            WriteIntToPacket(Clamp(BattleIndex, -1, 15), BattleIndexCompressionInfo);
            WriteStringToPacket(BattleId ?? string.Empty);
            WriteStringToPacket(RuntimeScene ?? string.Empty);
            WriteStringToPacket(PlayerSide ?? string.Empty);
            WriteIntToPacket(
                Clamp(MapPatchSceneIndex, -1, 32767),
                MapPatchSceneIndexCompressionInfo);
            WriteIntToPacket(
                QuantizeNormalizedCoordinate(MapPatchNormalizedX),
                MapPatchNormalizedCompressionInfo);
            WriteIntToPacket(
                QuantizeNormalizedCoordinate(MapPatchNormalizedY),
                MapPatchNormalizedCompressionInfo);
            WriteBoolToPacket(HasPatchEncounterDirection);
            WriteIntToPacket(
                QuantizeDirectionComponent(PatchEncounterDirX),
                MapPatchDirectionCompressionInfo);
            WriteIntToPacket(
                QuantizeDirectionComponent(PatchEncounterDirY),
                MapPatchDirectionCompressionInfo);
            WriteStringToPacket(PatchEncounterDirectionSource ?? string.Empty);

            BattleScenarioContextMessage scenarioContext = ScenarioContext;
            WriteStringToPacket(scenarioContext?.CampaignBattleType ?? string.Empty);
            WriteStringToPacket(scenarioContext?.ScenarioKind ?? string.Empty);
            WriteBoolToPacket(scenarioContext?.IsSiegeBattle == true);
            WriteStringToPacket(scenarioContext?.Source ?? string.Empty);

            BattleSiegeContextMessage siegeContext = scenarioContext?.SiegeContext;
            WriteBoolToPacket(siegeContext != null);
            if (siegeContext != null)
            {
                WriteStringToPacket(siegeContext.SiegeSubtype ?? string.Empty);
                WriteStringToPacket(siegeContext.MissionShell ?? string.Empty);
                WriteStringToPacket(siegeContext.SettlementId ?? string.Empty);
                WriteStringToPacket(siegeContext.SettlementKind ?? string.Empty);
                WriteStringToPacket(siegeContext.SettlementCultureId ?? string.Empty);
                WriteStringToPacket(siegeContext.SceneLocationId ?? string.Empty);
                WriteStringToPacket(siegeContext.CurrentSiegeState ?? string.Empty);
                WriteIntToPacket(Clamp(siegeContext.WallLevel, -1, 15), SceneLevelCompressionInfo);
                WriteBoolToPacket(siegeContext.HasAnySiegeTower);
                WriteBoolToPacket(siegeContext.HasMissionInitializerRecord);
                WriteStringToPacket(siegeContext.MissionInitializerSource ?? string.Empty);
                WriteStringToPacket(siegeContext.MissionInitializerSceneName ?? string.Empty);
                WriteStringToPacket(siegeContext.MissionInitializerSceneLevels ?? string.Empty);
                WriteIntToPacket(
                    Clamp(siegeContext.MissionInitializerSceneUpgradeLevel, -1, 15),
                    SceneLevelCompressionInfo);
                WriteBoolToPacket(siegeContext.MissionInitializerPlayingInCampaignMode);
                WriteBoolToPacket(siegeContext.MissionInitializerSceneHasMapPatch);
                WriteIntToPacket(
                    Clamp(siegeContext.MissionInitializerDecalAtlasGroup, -1, 255),
                    SceneEnumCompressionInfo);
                WriteIntToPacket(
                    Clamp(siegeContext.MissionInitializerTerrainType, -1, 255),
                    SceneEnumCompressionInfo);

                int wallRatioCount = Math.Min(
                    siegeContext.WallHitPointRatios?.Count ?? 0,
                    MaxWallRatioCount);
                WriteIntToPacket(wallRatioCount, CountCompressionInfo);
                for (int i = 0; i < wallRatioCount; i++)
                {
                    float ratio = siegeContext.WallHitPointRatios[i];
                    int scaledRatio = float.IsNaN(ratio) || float.IsInfinity(ratio)
                        ? 10000
                        : Clamp((int)Math.Round(ratio * 10000f), 0, 10000);
                    WriteIntToPacket(scaledRatio, WallRatioCompressionInfo);
                }

                WriteSiegeEngineSnapshots(siegeContext.AttackerSiegeEngines);
                WriteSiegeEngineSnapshots(siegeContext.DefenderSiegeEngines);
                WriteStringList(siegeContext.AttackerSiegeEngineTypeIds);
                WriteStringList(siegeContext.DefenderSiegeEngineTypeIds);
            }

            WriteStringToPacket(ContractHash ?? string.Empty);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter()
        {
            return MultiplayerMessageFilter.Mission;
        }

        protected override string OnGetLogFormat()
        {
            return
                "CoopPreMissionTopologyContract" +
                " Schema=" + SchemaVersion +
                " BattleIndex=" + BattleIndex +
                " Scene=" + (RuntimeScene ?? string.Empty) +
                " MapPatchSceneIndex=" + MapPatchSceneIndex +
                " MapPatchNormalized=(" + MapPatchNormalizedX.ToString("0.####") + "," + MapPatchNormalizedY.ToString("0.####") + ")" +
                " HasPatchEncounterDirection=" + HasPatchEncounterDirection +
                " Siege=" + (ScenarioContext?.IsSiegeBattle == true) +
                " Shell=" + (ScenarioContext?.SiegeContext?.MissionShell ?? string.Empty) +
                " SceneLevels=" + (ScenarioContext?.SiegeContext?.MissionInitializerSceneLevels ?? string.Empty);
        }

        private static void ReadSiegeEngineSnapshots(
            List<BattleSiegeEngineSnapshotMessage> output,
            ref bool bufferReadValid)
        {
            int count = ReadIntFromPacket(CountCompressionInfo, ref bufferReadValid);
            for (int i = 0; i < count; i++)
            {
                output.Add(new BattleSiegeEngineSnapshotMessage
                {
                    EngineTypeId = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty,
                    Index = ReadIntFromPacket(SiegeEngineIndexCompressionInfo, ref bufferReadValid),
                    Health = ReadHealth(ref bufferReadValid),
                    InitialHealth = ReadHealth(ref bufferReadValid),
                    MaxHealth = ReadHealth(ref bufferReadValid)
                });
            }
        }

        private static void WriteSiegeEngineSnapshots(
            List<BattleSiegeEngineSnapshotMessage> snapshots)
        {
            int count = Math.Min(snapshots?.Count ?? 0, MaxSiegeEngineCountPerSide);
            WriteIntToPacket(count, CountCompressionInfo);
            for (int i = 0; i < count; i++)
            {
                BattleSiegeEngineSnapshotMessage snapshot = snapshots[i];
                WriteStringToPacket(snapshot?.EngineTypeId ?? string.Empty);
                WriteIntToPacket(
                    Clamp(snapshot?.Index ?? -1, -1, 1023),
                    SiegeEngineIndexCompressionInfo);
                WriteHealth(snapshot?.Health ?? 0f);
                WriteHealth(snapshot?.InitialHealth ?? 0f);
                WriteHealth(snapshot?.MaxHealth ?? 0f);
            }
        }

        private static void ReadStringList(
            List<string> output,
            ref bool bufferReadValid)
        {
            int count = ReadIntFromPacket(CountCompressionInfo, ref bufferReadValid);
            for (int i = 0; i < count; i++)
                output.Add(ReadStringFromPacket(ref bufferReadValid) ?? string.Empty);
        }

        private static void WriteStringList(List<string> values)
        {
            int count = Math.Min(values?.Count ?? 0, MaxSiegeEngineCountPerSide);
            WriteIntToPacket(count, CountCompressionInfo);
            for (int i = 0; i < count; i++)
                WriteStringToPacket(values[i] ?? string.Empty);
        }

        private static float ReadHealth(ref bool bufferReadValid)
        {
            int scaledHealth = ReadIntFromPacket(
                SiegeEngineHealthCompressionInfo,
                ref bufferReadValid);
            return scaledHealth / 100f;
        }

        private static void WriteHealth(float health)
        {
            int scaledHealth = float.IsNaN(health) || float.IsInfinity(health)
                ? 0
                : Clamp((int)Math.Round(Math.Max(0f, health) * 100f), 0, 100000000);
            WriteIntToPacket(scaledHealth, SiegeEngineHealthCompressionInfo);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;
            return value > max ? max : value;
        }

        private static int QuantizeNormalizedCoordinate(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0;
            return Clamp((int)Math.Round(value * 10000f), 0, 10000);
        }

        private static int QuantizeDirectionComponent(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0;
            return Clamp((int)Math.Round(value * 10000f), -10000, 10000);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
