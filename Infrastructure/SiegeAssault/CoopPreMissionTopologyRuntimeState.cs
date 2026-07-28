using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CoopSpectator.Network.Messages;

namespace CoopSpectator.Infrastructure
{
    public sealed class CoopPreMissionTopologyContract
    {
        public int SchemaVersion { get; set; }
        public int BattleIndex { get; set; } = -1;
        public string BattleId { get; set; }
        public string RuntimeScene { get; set; }
        public string PlayerSide { get; set; }
        public int MapPatchSceneIndex { get; set; } = -1;
        public float MapPatchNormalizedX { get; set; }
        public float MapPatchNormalizedY { get; set; }
        public bool HasPatchEncounterDirection { get; set; }
        public float PatchEncounterDirX { get; set; }
        public float PatchEncounterDirY { get; set; }
        public string PatchEncounterDirectionSource { get; set; }
        public BattleScenarioContextMessage ScenarioContext { get; set; }
        public string ContractHash { get; set; }
        public DateTime ReceivedUtc { get; set; }

        public CoopPreMissionTopologyContract Clone()
        {
            return new CoopPreMissionTopologyContract
            {
                SchemaVersion = SchemaVersion,
                BattleIndex = BattleIndex,
                BattleId = BattleId,
                RuntimeScene = RuntimeScene,
                PlayerSide = PlayerSide,
                MapPatchSceneIndex = MapPatchSceneIndex,
                MapPatchNormalizedX = MapPatchNormalizedX,
                MapPatchNormalizedY = MapPatchNormalizedY,
                HasPatchEncounterDirection = HasPatchEncounterDirection,
                PatchEncounterDirX = PatchEncounterDirX,
                PatchEncounterDirY = PatchEncounterDirY,
                PatchEncounterDirectionSource = PatchEncounterDirectionSource,
                ScenarioContext = ScenarioContext?.Clone(),
                ContractHash = ContractHash,
                ReceivedUtc = ReceivedUtc
            };
        }
    }

    public static class CoopPreMissionTopologyRuntimeState
    {
        private static readonly object Sync = new object();
        private static readonly TimeSpan ContractTtl = TimeSpan.FromMinutes(5);

        private static CoopPreMissionTopologyContract _receivedContract;
        private static CoopPreMissionTopologyContract _activeContract;

        public static CoopPreMissionTopologyContractMessage TryBuildServerMessage(
            int battleIndex,
            out string diagnostics)
        {
            BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                snapshot?.ScenarioContext;
            if (snapshot == null || scenarioContext == null)
            {
                diagnostics = "snapshot-or-scenario-context-missing";
                return null;
            }

            string runtimeScene = FirstNonEmpty(
                snapshot.MultiplayerScene,
                snapshot.MapScene,
                scenarioContext.SiegeContext?.MissionInitializerSceneName);
            if (string.IsNullOrWhiteSpace(runtimeScene))
            {
                diagnostics = "runtime-scene-missing";
                return null;
            }

            string playerSide = FirstNonEmpty(
                snapshot.PlayerSide,
                snapshot.Sides?
                    .FirstOrDefault(side => side != null && side.IsPlayerSide)?
                    .SideText,
                snapshot.Sides?
                    .FirstOrDefault(side => side != null && side.IsPlayerSide)?
                    .SideId);
            string contractHash = ComputeContractHash(
                CoopPreMissionTopologyContractMessage.CurrentSchemaVersion,
                battleIndex,
                snapshot.BattleId,
                runtimeScene,
                playerSide,
                snapshot.MapPatchSceneIndex,
                snapshot.MapPatchNormalizedX,
                snapshot.MapPatchNormalizedY,
                snapshot.HasPatchEncounterDirection,
                snapshot.PatchEncounterDirX,
                snapshot.PatchEncounterDirY,
                snapshot.PatchEncounterDirectionSource,
                scenarioContext);

            diagnostics =
                "Scene=" + runtimeScene +
                " BattleIndex=" + battleIndex +
                " BattleId=" + Normalize(snapshot.BattleId) +
                " MapPatchSceneIndex=" + snapshot.MapPatchSceneIndex +
                " MapPatchNormalized=(" + QuantizeNormalizedCoordinate(snapshot.MapPatchNormalizedX) + "," + QuantizeNormalizedCoordinate(snapshot.MapPatchNormalizedY) + ")" +
                " HasPatchEncounterDirection=" + snapshot.HasPatchEncounterDirection +
                " ScenarioKind=" + Normalize(scenarioContext.ScenarioKind) +
                " IsSiegeBattle=" + scenarioContext.IsSiegeBattle +
                " Shell=" + Normalize(scenarioContext.SiegeContext?.MissionShell) +
                " SceneLevels=" + Normalize(scenarioContext.SiegeContext?.MissionInitializerSceneLevels) +
                " Hash=" + contractHash;
            return new CoopPreMissionTopologyContractMessage(
                battleIndex,
                snapshot.BattleId,
                runtimeScene,
                playerSide,
                snapshot.MapPatchSceneIndex,
                snapshot.MapPatchNormalizedX,
                snapshot.MapPatchNormalizedY,
                snapshot.HasPatchEncounterDirection,
                snapshot.PatchEncounterDirX,
                snapshot.PatchEncounterDirY,
                snapshot.PatchEncounterDirectionSource,
                scenarioContext,
                contractHash);
        }

        public static bool TryAccept(
            CoopPreMissionTopologyContractMessage message,
            out string diagnostics)
        {
            if (message == null)
            {
                diagnostics = "message-null";
                return false;
            }

            if (message.SchemaVersion != CoopPreMissionTopologyContractMessage.CurrentSchemaVersion)
            {
                diagnostics =
                    "schema-mismatch Received=" + message.SchemaVersion +
                    " Expected=" + CoopPreMissionTopologyContractMessage.CurrentSchemaVersion;
                return false;
            }

            if (string.IsNullOrWhiteSpace(message.RuntimeScene) ||
                message.ScenarioContext == null)
            {
                diagnostics = "required-fields-missing";
                return false;
            }

            string expectedHash = ComputeContractHash(
                message.SchemaVersion,
                message.BattleIndex,
                message.BattleId,
                message.RuntimeScene,
                message.PlayerSide,
                message.MapPatchSceneIndex,
                message.MapPatchNormalizedX,
                message.MapPatchNormalizedY,
                message.HasPatchEncounterDirection,
                message.PatchEncounterDirX,
                message.PatchEncounterDirY,
                message.PatchEncounterDirectionSource,
                message.ScenarioContext);
            if (!string.Equals(
                    expectedHash,
                    Normalize(message.ContractHash),
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostics =
                    "hash-mismatch Received=" + Normalize(message.ContractHash) +
                    " Computed=" + expectedHash;
                return false;
            }

            var accepted = new CoopPreMissionTopologyContract
            {
                SchemaVersion = message.SchemaVersion,
                BattleIndex = message.BattleIndex,
                BattleId = Normalize(message.BattleId),
                RuntimeScene = Normalize(message.RuntimeScene),
                PlayerSide = Normalize(message.PlayerSide),
                MapPatchSceneIndex = message.MapPatchSceneIndex,
                MapPatchNormalizedX = message.MapPatchNormalizedX,
                MapPatchNormalizedY = message.MapPatchNormalizedY,
                HasPatchEncounterDirection = message.HasPatchEncounterDirection,
                PatchEncounterDirX = message.PatchEncounterDirX,
                PatchEncounterDirY = message.PatchEncounterDirY,
                PatchEncounterDirectionSource = Normalize(message.PatchEncounterDirectionSource),
                ScenarioContext = message.ScenarioContext.Clone(),
                ContractHash = expectedHash,
                ReceivedUtc = DateTime.UtcNow
            };

            lock (Sync)
            {
                _receivedContract = accepted;
            }

            diagnostics =
                "Accepted=True Scene=" + accepted.RuntimeScene +
                " BattleIndex=" + accepted.BattleIndex +
                " BattleId=" + accepted.BattleId +
                " MapPatchSceneIndex=" + accepted.MapPatchSceneIndex +
                " HasPatchEncounterDirection=" + accepted.HasPatchEncounterDirection +
                " ScenarioKind=" + Normalize(accepted.ScenarioContext?.ScenarioKind) +
                " IsSiegeBattle=" + (accepted.ScenarioContext?.IsSiegeBattle == true) +
                " Shell=" + Normalize(accepted.ScenarioContext?.SiegeContext?.MissionShell) +
                " SceneLevels=" + Normalize(accepted.ScenarioContext?.SiegeContext?.MissionInitializerSceneLevels) +
                " Hash=" + accepted.ContractHash;
            return true;
        }

        public static bool TryActivateForMissionLoad(
            string runtimeScene,
            int battleIndex,
            out CoopPreMissionTopologyContract contract,
            out string diagnostics)
        {
            string normalizedScene = Normalize(runtimeScene);
            lock (Sync)
            {
                contract = _receivedContract?.Clone();
                if (contract == null)
                {
                    diagnostics = "contract-not-received";
                    return false;
                }

                if (contract.ReceivedUtc == DateTime.MinValue ||
                    DateTime.UtcNow - contract.ReceivedUtc > ContractTtl)
                {
                    diagnostics = "contract-expired";
                    return false;
                }

                if (!string.Equals(
                        normalizedScene,
                        Normalize(contract.RuntimeScene),
                        StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics =
                        "scene-mismatch Requested=" + normalizedScene +
                        " Contract=" + Normalize(contract.RuntimeScene);
                    return false;
                }

                if (battleIndex >= 0 &&
                    contract.BattleIndex >= 0 &&
                    battleIndex != contract.BattleIndex)
                {
                    diagnostics =
                        "battle-index-mismatch Requested=" + battleIndex +
                        " Contract=" + contract.BattleIndex;
                    return false;
                }

                _activeContract = contract.Clone();
            }

            diagnostics =
                "Activated=True Scene=" + contract.RuntimeScene +
                " BattleIndex=" + contract.BattleIndex +
                " ScenarioKind=" + Normalize(contract.ScenarioContext?.ScenarioKind) +
                " IsSiegeBattle=" + (contract.ScenarioContext?.IsSiegeBattle == true) +
                " Shell=" + Normalize(contract.ScenarioContext?.SiegeContext?.MissionShell) +
                " SceneLevels=" + Normalize(contract.ScenarioContext?.SiegeContext?.MissionInitializerSceneLevels) +
                " Hash=" + contract.ContractHash;
            return true;
        }

        public static BattleScenarioContextMessage GetActiveScenarioContext()
        {
            lock (Sync)
            {
                return _activeContract?.ScenarioContext?.Clone();
            }
        }

        public static string GetActivePlayerSide()
        {
            lock (Sync)
            {
                return _activeContract?.PlayerSide ?? string.Empty;
            }
        }

        public static bool TryGetActive(
            string runtimeScene,
            out CoopPreMissionTopologyContract contract,
            out string diagnostics)
        {
            string normalizedScene = Normalize(runtimeScene);
            lock (Sync)
            {
                contract = _activeContract?.Clone();
                if (contract == null)
                {
                    diagnostics = "active-contract-missing";
                    return false;
                }

                if (contract.ReceivedUtc == DateTime.MinValue ||
                    DateTime.UtcNow - contract.ReceivedUtc > ContractTtl)
                {
                    diagnostics = "active-contract-expired";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(normalizedScene) &&
                    !string.Equals(
                        normalizedScene,
                        Normalize(contract.RuntimeScene),
                        StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics =
                        "active-scene-mismatch Requested=" + normalizedScene +
                        " Contract=" + Normalize(contract.RuntimeScene);
                    return false;
                }
            }

            diagnostics =
                "Active=True Scene=" + contract.RuntimeScene +
                " BattleIndex=" + contract.BattleIndex +
                " Hash=" + contract.ContractHash;
            return true;
        }

        public static bool TryValidateFullSnapshot(
            BattleSnapshotMessage snapshot,
            out string diagnostics)
        {
            if (snapshot == null)
            {
                diagnostics = "snapshot-null";
                return false;
            }

            CoopPreMissionTopologyContract active;
            lock (Sync)
            {
                active = _activeContract?.Clone();
            }

            if (active == null)
            {
                diagnostics = "active-contract-missing";
                return false;
            }

            string runtimeScene = FirstNonEmpty(
                snapshot.MultiplayerScene,
                snapshot.MapScene,
                snapshot.ScenarioContext?.SiegeContext?.MissionInitializerSceneName);
            string playerSide = FirstNonEmpty(
                snapshot.PlayerSide,
                snapshot.Sides?
                    .FirstOrDefault(side => side != null && side.IsPlayerSide)?
                    .SideText,
                snapshot.Sides?
                    .FirstOrDefault(side => side != null && side.IsPlayerSide)?
                    .SideId);
            string fullSnapshotHash = ComputeContractHash(
                active.SchemaVersion,
                active.BattleIndex,
                snapshot.BattleId,
                runtimeScene,
                playerSide,
                snapshot.MapPatchSceneIndex,
                snapshot.MapPatchNormalizedX,
                snapshot.MapPatchNormalizedY,
                snapshot.HasPatchEncounterDirection,
                snapshot.PatchEncounterDirX,
                snapshot.PatchEncounterDirY,
                snapshot.PatchEncounterDirectionSource,
                snapshot.ScenarioContext);
            bool matches = string.Equals(
                fullSnapshotHash,
                active.ContractHash,
                StringComparison.OrdinalIgnoreCase);
            diagnostics =
                "Matches=" + matches +
                " ActiveHash=" + active.ContractHash +
                " FullSnapshotHash=" + fullSnapshotHash +
                " ActiveScene=" + active.RuntimeScene +
                " FullSnapshotScene=" + runtimeScene +
                " ActiveBattleId=" + active.BattleId +
                " FullSnapshotBattleId=" + Normalize(snapshot.BattleId);
            return matches;
        }

        public static void Clear(string source)
        {
            CoopPreMissionTopologyContract previousReceived;
            CoopPreMissionTopologyContract previousActive;
            lock (Sync)
            {
                previousReceived = _receivedContract;
                previousActive = _activeContract;
                _receivedContract = null;
                _activeContract = null;
            }

            if (previousReceived != null || previousActive != null)
            {
                ModLogger.Info(
                    "CoopPreMissionTopologyRuntimeState: cleared. " +
                    "Source=" + Normalize(source) +
                    " ReceivedScene=" + Normalize(previousReceived?.RuntimeScene) +
                    " ActiveScene=" + Normalize(previousActive?.RuntimeScene) + ".");
            }
        }

        public static string ComputeContractHash(
            int schemaVersion,
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
            BattleScenarioContextMessage scenarioContext)
        {
            var builder = new StringBuilder(2048);
            Append(builder, schemaVersion);
            Append(builder, battleIndex);
            Append(builder, battleId);
            Append(builder, runtimeScene);
            Append(builder, playerSide);
            Append(builder, Clamp(mapPatchSceneIndex, -1, 32767));
            Append(builder, QuantizeNormalizedCoordinate(mapPatchNormalizedX));
            Append(builder, QuantizeNormalizedCoordinate(mapPatchNormalizedY));
            Append(builder, hasPatchEncounterDirection);
            Append(builder, QuantizeDirectionComponent(patchEncounterDirX));
            Append(builder, QuantizeDirectionComponent(patchEncounterDirY));
            Append(builder, patchEncounterDirectionSource);
            Append(builder, scenarioContext?.CampaignBattleType);
            Append(builder, scenarioContext?.ScenarioKind);
            Append(builder, scenarioContext?.IsSiegeBattle == true);
            Append(builder, scenarioContext?.Source);

            BattleSiegeContextMessage siege = scenarioContext?.SiegeContext;
            Append(builder, siege != null);
            if (siege != null)
            {
                Append(builder, siege.SiegeSubtype);
                Append(builder, siege.MissionShell);
                Append(builder, siege.SettlementId);
                Append(builder, siege.SettlementKind);
                Append(builder, siege.SettlementCultureId);
                Append(builder, siege.SceneLocationId);
                Append(builder, siege.CurrentSiegeState);
                Append(builder, siege.WallLevel);
                Append(builder, siege.HasAnySiegeTower);
                Append(builder, siege.HasMissionInitializerRecord);
                Append(builder, siege.MissionInitializerSource);
                Append(builder, siege.MissionInitializerSceneName);
                Append(builder, siege.MissionInitializerSceneLevels);
                Append(builder, siege.MissionInitializerSceneUpgradeLevel);
                Append(builder, siege.MissionInitializerPlayingInCampaignMode);
                Append(builder, siege.MissionInitializerSceneHasMapPatch);
                Append(builder, siege.MissionInitializerDecalAtlasGroup);
                Append(builder, siege.MissionInitializerTerrainType);
                AppendFloatList(builder, siege.WallHitPointRatios);
                AppendSiegeEngines(builder, siege.AttackerSiegeEngines);
                AppendSiegeEngines(builder, siege.DefenderSiegeEngines);
                AppendStringList(builder, siege.AttackerSiegeEngineTypeIds);
                AppendStringList(builder, siege.DefenderSiegeEngineTypeIds);
            }

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(
                    Encoding.UTF8.GetBytes(builder.ToString()));
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }

        private static void AppendSiegeEngines(
            StringBuilder builder,
            System.Collections.Generic.List<BattleSiegeEngineSnapshotMessage> engines)
        {
            int count = Math.Min(
                engines?.Count ?? 0,
                CoopPreMissionTopologyContractMessage.MaxSiegeEngineCountPerSide);
            Append(builder, count);
            for (int i = 0; i < count; i++)
            {
                BattleSiegeEngineSnapshotMessage engine = engines[i];
                Append(builder, engine?.EngineTypeId);
                Append(builder, engine?.Index ?? -1);
                Append(builder, QuantizeHealth(engine?.Health ?? 0f));
                Append(builder, QuantizeHealth(engine?.InitialHealth ?? 0f));
                Append(builder, QuantizeHealth(engine?.MaxHealth ?? 0f));
            }
        }

        private static void AppendFloatList(
            StringBuilder builder,
            System.Collections.Generic.List<float> values)
        {
            int count = Math.Min(
                values?.Count ?? 0,
                CoopPreMissionTopologyContractMessage.MaxWallRatioCount);
            Append(builder, count);
            for (int i = 0; i < count; i++)
            {
                float value = values[i];
                int quantized = float.IsNaN(value) || float.IsInfinity(value)
                    ? 10000
                    : Clamp((int)Math.Round(value * 10000f), 0, 10000);
                Append(builder, quantized);
            }
        }

        private static void AppendStringList(
            StringBuilder builder,
            System.Collections.Generic.List<string> values)
        {
            int count = Math.Min(
                values?.Count ?? 0,
                CoopPreMissionTopologyContractMessage.MaxSiegeEngineCountPerSide);
            Append(builder, count);
            for (int i = 0; i < count; i++)
                Append(builder, values[i]);
        }

        private static int QuantizeHealth(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0;
            return Clamp(
                (int)Math.Round(Math.Max(0f, value) * 100f),
                0,
                100000000);
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

        private static void Append(StringBuilder builder, object value)
        {
            string text;
            if (value is bool boolValue)
                text = boolValue ? "1" : "0";
            else if (value is IFormattable formattable)
                text = formattable.ToString(null, CultureInfo.InvariantCulture);
            else
                text = Normalize(value?.ToString());

            builder.Append(text.Length);
            builder.Append(':');
            builder.Append(text);
            builder.Append('|');
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
                return string.Empty;

            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                    return values[i].Trim();
            }

            return string.Empty;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;
            return value > max ? max : value;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
