using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using CoopSpectator.Network.Messages;

namespace CoopSpectator.Infrastructure.Automation
{
    // Observation DTOs are evidence only; BattleSnapshotMessage remains the scenario authority.
    public sealed class CoopAutomationSmokeSlot
    {
        public string ItemId { get; set; }
        public string ModifierId { get; set; }
        public int? Amount { get; set; }
    }

    public sealed class CoopAutomationSmokeAgent
    {
        public int AgentIndex { get; set; }
        public string EntryId { get; set; }
        public string SideId { get; set; }
        public string Side { get; set; }
        public int TeamIndex { get; set; }
        public string Formation { get; set; }
        public bool FormationTeamMatches { get; set; }
        public string OriginalCharacterId { get; set; }
        public string HeroId { get; set; }
        public bool IsHero { get; set; }
        public string NativeCharacterId { get; set; }
        public string ContractNativeCharacterId { get; set; }
        public bool NativeOriginAndLedgerMatch { get; set; }
        public bool ExactContractValid { get; set; }
        public bool PreSpawnEquipmentInjected { get; set; }
        public bool Active { get; set; }
        public bool Mounted { get; set; }
        public int MountAgentIndex { get; set; } = -1;
        public bool ReciprocalMountLink { get; set; }
        public string MountHorseId { get; set; }
        public string MountHarnessId { get; set; }
        public Dictionary<string, CoopAutomationSmokeSlot> Equipment { get; set; } =
            new Dictionary<string, CoopAutomationSmokeSlot>(StringComparer.Ordinal);
    }

    public sealed class CoopAutomationSmokeObservation
    {
        public string CampaignId { get; set; }
        public string BattleId { get; set; }
        public string BattleInstanceId { get; set; }
        public string Stage { get; set; }
        public string Scene { get; set; }
        public string MissionShell { get; set; }
        public string ScenarioKind { get; set; }
        public string CampaignBattleType { get; set; }
        public bool IsSiegeBattle { get; set; }
        public string Phase { get; set; }
        public bool NativeMaterializationComplete { get; set; }
        public int ConnectedClientCount { get; set; }
        public int ActiveMountCount { get; set; }
        public int ResultEntryCount { get; set; }
        public bool ResultGuardWasClear { get; set; }
        public List<string> Controllers { get; set; } = new List<string>();
        public List<string> Violations { get; set; } = new List<string>();
        public List<CoopAutomationSmokeAgent> Agents { get; set; } = new List<CoopAutomationSmokeAgent>();
    }

    public sealed class CoopAutomationSmokeFixture
    {
        public string RosterJson { get; internal set; }
        public BattleSnapshotMessage Snapshot { get; internal set; }
        internal JObject RawSnapshot { get; set; }
    }

    public sealed class CoopAutomationDedicatedSpawnSmokeEvidence
    {
        public string Profile { get; set; } = CoopAutomationSpawnSmokeContract.Profile;
        public string FixtureId { get; set; } = CoopAutomationSpawnSmokeContract.FixtureId;
        public string PayloadSha256 { get; set; } = CoopAutomationSpawnSmokeContract.PayloadSha256;
        public string OracleSha256 { get; set; } = CoopAutomationSpawnSmokeContract.OracleSha256;
        public string AuthoritativeSource { get; set; } = "CoopMissionSpawnLogic.TryCaptureAutomationSpawnSmokeEvidence";
        public int StartMissionRequests { get; set; }
        public int EndMissionRequests { get; set; }
        public bool InitialStateWasClean { get; set; }
        public bool MissionDisposed { get; set; }
        public bool ProtectedResultUnchanged { get; set; }
        public string PhaseBeforeEnd { get; set; }
        public int ResultAttempts { get; set; }
        public int SuppressedResults { get; set; }
        public int ResultEntriesAtAttempt { get; set; }
        public CoopAutomationSmokeObservation Observation { get; set; }
    }

    public sealed class CoopAutomationSmokeLifecycle
    {
        public int StartRequests { get; private set; }
        public int EndRequests { get; private set; }
        public bool TryClaimStart(bool nativeIdle, bool missionExists, out string failure)
        {
            failure = StartRequests != 0 ? "DuplicateStartMission" :
                missionExists ? "UnexpectedExistingMission" : !nativeIdle ? "NativeCommandBusy" : string.Empty;
            if (failure.Length != 0) return false;
            StartRequests = 1; // Claim BEFORE dispatch, including exceptions and uncertain native outcomes.
            return true;
        }
        public bool TryClaimEnd(bool nativeIdle, bool missionMatches, string phase, out string failure)
        {
            failure = EndRequests != 0 ? "DuplicateEndMission" : StartRequests != 1 || !missionMatches
                ? "AbortMissionMismatch" : phase != CoopAutomationSpawnSmokeContract.Stage
                ? "AbortPhaseInvalid" : !nativeIdle ? "NativeCommandBusy" : string.Empty;
            if (failure.Length != 0) return false;
            EndRequests = 1;
            return true;
        }
    }
    public static class CoopAutomationSpawnSmokeContract
    {
        public const string Profile = "FieldDedicatedSpawnSmokeV1";
        public const string FixtureId = "field-current-sanitized-v1";
        public const string Scene = "battle_terrain_029";
        public const string GameType = "CoopBattle";
        public const string MissionShell = "MultiplayerBattle";
        public const string CampaignId = "fixture-campaign-001";
        public const string BattleId = "fixture-battle-001";
        public const string BattleInstanceId = "fixture-battle-instance-001";
        public const string Stage = "PreBattleHold";
        public const string FixtureRelativeRoot = "payloads/field-current";
        public const string PayloadFile = "battle_roster.sanitized.json";
        public const int PayloadLength = 259744;
        public const string PayloadSha256 = "B47D7AF7FA057C36CA8EF759A6D597C00007158A22E3A556AC57A1299579D49D";
        public const string MetadataSha256 = "06169055E66E4DC0719AF3A8CB5A5E082CAF487DD18B1E4D18317B5C80973950";
        public const string OracleSha256 = "D9F593D17BEA35A8D8717867C6F7AE79BC721C2FA3A9FFD90F474A001FD023DA";
        public static readonly string[] Slots =
            { "Item0", "Item1", "Item2", "Item3", "Head", "Body", "Leg", "Gloves", "Cape", "Horse", "HorseHarness" };

        public static bool IsFirstFieldScenario(string scenario, string battleType, bool siege)
        {
            return scenario == "FieldBattle" && battleType == "FieldBattle" && !siege;
        }

        public static bool TryResolveContainedPath(string root, string relative, out string path, out string failure)
        {
            path = null;
            failure = "FixturePathEscapesRoot";
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root) ||
                string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(":"))
                return false;
            try
            {
                string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string candidate = Path.GetFullPath(Path.Combine(fullRoot, relative));
                if (!candidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return false;
                // A lexical prefix is insufficient when any existing ancestor is a junction/symlink.
                for (string current = candidate; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                {
                    if ((File.Exists(current) || Directory.Exists(current)) &&
                        (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    {
                        failure = "FixtureReparsePointRejected";
                        return false;
                    }
                }
                path = candidate;
                failure = string.Empty;
                return true;
            }
            catch { return false; }
        }

        public static bool TryLoadFixture(string runRoot, string relativeRoot, string fixtureId,
            out CoopAutomationSmokeFixture fixture, out string failure)
        {
            fixture = null;
            failure = "FixtureIdMismatch";
            if (fixtureId != FixtureId) return false;
            failure = "FixtureRootMismatch";
            if (relativeRoot != FixtureRelativeRoot) return false;
            try
            {
                if (!TryReadPinned(runRoot, relativeRoot + "/" + PayloadFile, PayloadLength, PayloadSha256,
                        out byte[] bytes, out failure) ||
                    !TryReadPinned(runRoot, relativeRoot + "/fixture.sanitized.metadata.json", null, MetadataSha256,
                        out byte[] metadataBytes, out failure) ||
                    !TryReadPinned(runRoot, relativeRoot + "/fixture.oracle.json", null, OracleSha256,
                        out byte[] oracleBytes, out failure))
                    return false;
                var utf8 = new UTF8Encoding(false, true);
                string json = utf8.GetString(bytes);
                JObject metadata = JObject.Parse(utf8.GetString(metadataBytes));
                JObject oracle = JObject.Parse(utf8.GetString(oracleBytes));
                JObject raw = (JObject)JObject.Parse(json)["Snapshot"];
                if (metadata.Value<string>("FixtureId") != FixtureId || oracle.Value<string>("FixtureId") != FixtureId ||
                    oracle.Value<bool>("RecorderQualificationUsedAsOracle") ||
                    oracle.SelectToken("Expected.PositiveStackCount")?.Value<int>() != 47 ||
                    oracle.SelectToken("Expected.PositiveUnitCount")?.Value<int>() != 74 ||
                    oracle.SelectToken("Expected.MountedStackCount")?.Value<int>() != 17 ||
                    oracle.SelectToken("Expected.HeroStackCount")?.Value<int>() != 4)
                {
                    failure = "FixtureOracleMismatch";
                    return false;
                }
                BattleSnapshotMessage snapshot = raw?.ToObject<BattleSnapshotMessage>();
                if (snapshot == null || snapshot.CampaignId != CampaignId || snapshot.BattleId != BattleId ||
                    snapshot.BattleInstanceId != BattleInstanceId || snapshot.MultiplayerScene != Scene ||
                    !IsFirstFieldScenario(snapshot.ScenarioContext?.ScenarioKind,
                        snapshot.ScenarioContext?.CampaignBattleType, snapshot.ScenarioContext?.IsSiegeBattle ?? true))
                {
                    failure = "FixtureScenarioIdentityMismatch";
                    return false;
                }
                fixture = new CoopAutomationSmokeFixture { RosterJson = json, Snapshot = snapshot, RawSnapshot = raw };
                failure = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                failure = "FixtureReadFailed:" + ex.GetType().Name;
                return false;
            }
        }

        private static bool TryReadPinned(string root, string relative, int? length, string hash,
            out byte[] bytes, out string failure)
        {
            bytes = null;
            if (!TryResolveContainedPath(root, relative, out string path, out failure)) return false;
            if (!File.Exists(path)) { failure = "FixtureFileMissing"; return false; }
            // Validate bounds before allocation, then hash the SAME bytes used for parsing.
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > 1024 * 1024 || stream.Length <= 0 ||
                    (length.HasValue && stream.Length != length.Value))
                { failure = "PayloadLengthMismatch"; return false; }
                bytes = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read == 0) { failure = "PayloadTruncated"; return false; }
                    offset += read;
                }
            }
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
                if (actual != hash) { failure = "PayloadHashMismatch"; return false; }
            }
            failure = string.Empty;
            return true;
        }

        public static bool TryValidateObservation(CoopAutomationSmokeFixture fixture,
            CoopAutomationSmokeObservation observed, out string failure)
        {
            failure = "ObservationMissing";
            if (fixture == null || observed == null) return false;
            if (observed.CampaignId != CampaignId || observed.BattleId != BattleId ||
                observed.BattleInstanceId != BattleInstanceId || observed.Stage != Stage ||
                observed.Scene != Scene || observed.MissionShell != MissionShell ||
                !IsFirstFieldScenario(observed.ScenarioKind, observed.CampaignBattleType, observed.IsSiegeBattle))
            { failure = "MissionIdentityMismatch"; return false; }
            if (observed.Phase != Stage || observed.ConnectedClientCount != 0)
            { failure = "PreBattleBoundaryInvalid"; return false; }
            if (!observed.NativeMaterializationComplete)
            { failure = "NativeMaterializationIncomplete"; return false; }
            if (observed.Violations == null || observed.Violations.Count != 0)
            { failure = "FatalInvariantViolation"; return false; }
            foreach (string controller in new[] { "MissionMultiplayerCoopBattle", "CoopMissionSpawnLogic",
                "CoopMissionNetworkBridge", "MissionLobbyComponent", "MissionAgentSpawnLogic",
                "BannerBearerLogic" })
                if (observed.Controllers == null || !observed.Controllers.Contains(controller))
                { failure = "ControllerMissing:" + controller; return false; }
            if (observed.Agents == null || observed.Agents.Count != 74 || observed.Agents.Any(a => a == null) ||
                observed.Agents.Select(a => a.AgentIndex).Distinct().Count() != 74 ||
                observed.ActiveMountCount != 21 || observed.ResultEntryCount != 47 || !observed.ResultGuardWasClear)
            { failure = "InitialUniverseMismatch"; return false; }
            var entries = fixture.RawSnapshot["Sides"].Children()
                .SelectMany(side => side["Troops"].Children()).ToDictionary(t => (string)t["EntryId"], StringComparer.Ordinal);
            var mounts = new HashSet<int>();
            var teams = new Dictionary<string, int>(StringComparer.Ordinal);
            var orientations = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var group in observed.Agents.GroupBy(a => a.EntryId))
            {
                if (group.Key == null || !entries.TryGetValue(group.Key, out JToken expected))
                { failure = "UnexpectedEntry"; return false; }
                if (group.Count() != (int)expected["Count"] - (int)expected["WoundedCount"])
                { failure = "EntryMultiplicityMismatch:" + group.Key; return false; }
                foreach (CoopAutomationSmokeAgent agent in group)
                {
                    string sideId = (string)expected["SideId"];
                    if (!agent.Active || !agent.NativeOriginAndLedgerMatch || !agent.ExactContractValid ||
                        !agent.PreSpawnEquipmentInjected || agent.AgentIndex < 0)
                    { failure = "NativeEntryEvidenceInvalid:" + group.Key; return false; }
                    if (agent.SideId != sideId || (agent.Side != "Attacker" && agent.Side != "Defender") ||
                        agent.TeamIndex < 0 || !agent.FormationTeamMatches ||
                        agent.Formation != (string)expected["CampaignFormationClass"])
                    { failure = "TeamOrFormationMismatch:" + group.Key; return false; }
                    if ((teams.TryGetValue(sideId, out int team) && team != agent.TeamIndex) ||
                        (orientations.TryGetValue(sideId, out string side) && side != agent.Side))
                    { failure = "SideOrientationMismatch:" + group.Key; return false; }
                    teams[sideId] = agent.TeamIndex;
                    orientations[sideId] = agent.Side;
                    if (agent.OriginalCharacterId != (string)expected["OriginalCharacterId"] ||
                        agent.IsHero != (bool)expected["IsHero"] ||
                        (agent.HeroId ?? "") != ((string)expected["HeroId"] ?? "") ||
                        string.IsNullOrEmpty(agent.NativeCharacterId) ||
                        agent.NativeCharacterId != agent.ContractNativeCharacterId)
                    { failure = "CharacterOrHeroMismatch:" + group.Key; return false; }
                    foreach (string slot in Slots)
                    {
                        if (agent.Equipment == null || !agent.Equipment.TryGetValue(slot, out CoopAutomationSmokeSlot actual) ||
                            actual == null || (actual.ItemId ?? "") != ((string)expected["Combat" + slot + "Id"] ?? "") ||
                            (actual.ModifierId ?? "") != ((string)expected["Combat" + slot + "ModifierId"] ?? ""))
                        { failure = "EquipmentMismatch:" + group.Key + ":" + slot; return false; }
                        int? amount = expected["Combat" + slot + "Amount"]?.Value<int?>();
                        if (amount.HasValue && actual.Amount != amount)
                        { failure = "EquipmentAmountMismatch:" + group.Key + ":" + slot; return false; }
                    }
                    if (agent.Mounted != (bool)expected["IsMounted"] ||
                        (agent.Mounted && (agent.MountAgentIndex < 0 || !agent.ReciprocalMountLink ||
                         !mounts.Add(agent.MountAgentIndex) ||
                         agent.MountHorseId != (string)expected["CombatHorseId"] ||
                         agent.MountHarnessId != (string)expected["CombatHorseHarnessId"])) ||
                        (!agent.Mounted && agent.MountAgentIndex != -1))
                    { failure = "MountLinkMismatch:" + group.Key; return false; }
                }
            }
            if (observed.Agents.Select(a => a.EntryId).Distinct().Count() != 47 || mounts.Count != 21 ||
                observed.Agents.Count(a => a.IsHero) != 4 || teams.Count != 2 ||
                teams.Values.Distinct().Count() != 2 || orientations.Values.Distinct().Count() != 2)
            { failure = "ArmyCompositionMismatch"; return false; }
            failure = string.Empty;
            return true;
        }
    }
}