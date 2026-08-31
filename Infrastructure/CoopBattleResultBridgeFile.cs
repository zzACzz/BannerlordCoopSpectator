using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using CoopSpectator.Network.Messages;
using CoopSpectator.Infrastructure.Automation;

namespace CoopSpectator.Infrastructure
{
    public static class CoopBattleResultBridgeFile
    {
        private const string CoopSpectatorSubFolder = "CoopSpectator";
        private const string ResultFileName = "battle_result.json";
        private static readonly CoopBattleResultReadCache<BattleResultSnapshot> ReadCache =
            new CoopBattleResultReadCache<BattleResultSnapshot>();

        public sealed class BattleResultSnapshot
        {
            public string BattleId { get; set; }
            public string BattleInstanceId { get; set; }
            public int CampaignBindingVersion { get; set; }
            public string CampaignId { get; set; }
            public string ResultId { get; set; }
            public string BattleType { get; set; }
            public string MapScene { get; set; }
            public string Source { get; set; }
            public string WinnerSide { get; set; }
            public string PlayerSide { get; set; }
            public string BattleStage { get; set; }
            public string CompletionReason { get; set; }
            public bool DefenderPushedBack { get; set; }
            public bool IsFinalStage { get; set; }
            public int RoutedDefenderCount { get; set; }
            public bool IsSynthetic { get; set; }
            public DateTime UpdatedUtc { get; set; }
            public List<BattleResultEntrySnapshot> Entries { get; set; } = new List<BattleResultEntrySnapshot>();
            public int DroppedCombatEventCount { get; set; }
            public List<BattleResultCombatEventSnapshot> CombatEvents { get; set; } = new List<BattleResultCombatEventSnapshot>();
            public List<BattleResultCasualtyEventSnapshot> CasualtyEvents { get; set; } = new List<BattleResultCasualtyEventSnapshot>();
            public List<string> FrozenCaptainEntryIds { get; set; } = new List<string>();
            public List<FrozenCaptainCombatGroupSnapshotMessage> FrozenCaptainCombatGroups { get; set; } = new List<FrozenCaptainCombatGroupSnapshotMessage>();
            public List<BattleSiegeEngineSnapshotMessage> AttackerSiegeEngines { get; set; } = new List<BattleSiegeEngineSnapshotMessage>();
            public List<BattleSiegeEngineSnapshotMessage> DefenderSiegeEngines { get; set; } = new List<BattleSiegeEngineSnapshotMessage>();
        }

        public sealed class BattleResultCasualtyEventSnapshot
        {
            public string CasualtyEventId { get; set; }
            public string SpawnInstanceId { get; set; }
            public string Outcome { get; set; }
            public string VictimEntryId { get; set; }
            public string VictimSideId { get; set; }
            public string VictimPartyId { get; set; }
            public string VictimCharacterId { get; set; }
            public string VictimOriginalCharacterId { get; set; }
            public string VictimHeroId { get; set; }
            public bool VictimIsHero { get; set; }
            public string AttackerEntryId { get; set; }
            public string AttackerPartyId { get; set; }
            public string DamageType { get; set; }
            public string WeaponFlags { get; set; }
            public float MissionTime { get; set; }
        }

        public sealed class BattleResultEntrySnapshot
        {
            public string EntryId { get; set; }
            public string SideId { get; set; }
            public string PartyId { get; set; }
            public string CharacterId { get; set; }
            public string OriginalCharacterId { get; set; }
            public string SpawnTemplateId { get; set; }
            public string TroopName { get; set; }
            public string HeroId { get; set; }
            public string HeroRole { get; set; }
            public bool IsHero { get; set; }
            public int SnapshotCount { get; set; }
            public int SnapshotWoundedCount { get; set; }
            public int MaterializedSpawnCount { get; set; }
            public int ActiveCount { get; set; }
            public int RemovedCount { get; set; }
            public int KilledCount { get; set; }
            public int UnconsciousCount { get; set; }
            public int RoutedCount { get; set; }
            public int OtherRemovedCount { get; set; }
            public int ScoreHitCount { get; set; }
            public int HitsTakenCount { get; set; }
            public int FatalHitCount { get; set; }
            public int KillsInflictedCount { get; set; }
            public int UnconsciousInflictedCount { get; set; }
            public int RoutedInflictedCount { get; set; }
            public float DamageDealt { get; set; }
            public float DamageTaken { get; set; }
            public List<CaptainPerkEffectSnapshotMessage> GlobalCaptainPerkEffects { get; set; } = new List<CaptainPerkEffectSnapshotMessage>();
        }

        public sealed class BattleResultCombatEventSnapshot
        {
            public string AttackerEntryId { get; set; }
            public string AttackerSideId { get; set; }
            public string AttackerPartyId { get; set; }
            public string AttackerCharacterId { get; set; }
            public string AttackerOriginalCharacterId { get; set; }
            public string WeaponItemId { get; set; }
            public string CampaignWeaponItemId { get; set; }
            public string CaptainHeroId { get; set; }
            public string CaptainCharacterId { get; set; }
            public string CaptainOriginalCharacterId { get; set; }
            public string CommanderHeroId { get; set; }
            public string VictimEntryId { get; set; }
            public string VictimSideId { get; set; }
            public string VictimPartyId { get; set; }
            public string VictimCharacterId { get; set; }
            public string VictimOriginalCharacterId { get; set; }
            public string WeaponSkillHint { get; set; }
            public string WeaponClassHint { get; set; }
            public bool IsBlocked { get; set; }
            public bool IsSiegeEngineHit { get; set; }
            public bool IsTeamKill { get; set; }
            public bool IsFatal { get; set; }
            public bool IsAttackerMounted { get; set; }
            public bool IsAttackerUnderCommand { get; set; }
            public bool IsHorseCharge { get; set; }
            public bool IsSneakAttack { get; set; }
            public float Damage { get; set; }
            public float HitDistance { get; set; }
            public float MovementSpeedBonus { get; set; }
            public float HitPointRatio { get; set; }
            public float ShotDifficulty { get; set; }
            public float MissionTime { get; set; }
        }

        public static string GetResultFilePath()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string folder = Path.Combine(docs, "Mount and Blade II Bannerlord", CoopSpectatorSubFolder);
            return Path.Combine(folder, ResultFileName);
        }

        public static bool WriteResult(BattleResultSnapshot snapshot)
        {
            return WriteResult(snapshot, out _);
        }

        public static bool WriteResult(BattleResultSnapshot snapshot, out bool suppressed)
        {
            suppressed = false;
            if (snapshot == null)
                return false;

            CoopAutomationResultPublicationDecision automationDecision =
                CoopAutomationRuntimeBridge.ResolveResultPublicationDecision(
                    out CoopAutomationRuntimeConfiguration automationConfiguration,
                    out string automationFailureCode,
                    out string automationFailureMessage);
            if (automationDecision != CoopAutomationResultPublicationDecision.Publish)
            {
                suppressed = automationDecision == CoopAutomationResultPublicationDecision.Suppress;
                try
                {
                    CoopAutomationRuntimeBridge.WriteResultPublicationStatus(
                        automationConfiguration,
                        automationDecision,
                        snapshot.BattleId,
                        snapshot.Source,
                        automationFailureCode,
                        automationFailureMessage);
                }
                catch (Exception ex)
                {
                    ModLogger.Error(
                        "CoopBattleResultBridgeFile: failed to write automation result-publication status.",
                        ex);
                    return false;
                }

                if (suppressed)
                {
                    ModLogger.Info(
                        "CoopBattleResultBridgeFile: canonical result publication suppressed by validated automation policy. " +
                        "RunId=" + automationConfiguration.RunId +
                        " BattleId=" + (snapshot.BattleId ?? "null") + ".");
                    return true;
                }

                ModLogger.Error(
                    "CoopBattleResultBridgeFile: canonical result publication rejected because automation configuration is invalid. " +
                    "Failure=" + (automationFailureCode ?? "unknown") + ": " +
                    (automationFailureMessage ?? "unknown"),
                    null);
                return false;
            }

            string path = GetResultFilePath();
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
                File.WriteAllText(path, json);
                if (TryGetFileStamp(path, out CoopBattleResultFileStamp writtenStamp))
                    ReadCache.TryStore(writtenStamp, writtenStamp, snapshot);
                else
                    ReadCache.Invalidate();

                ModLogger.Info(
                    "CoopBattleResultBridgeFile: wrote result to " + path +
                    " BattleId=" + (snapshot.BattleId ?? "null") +
                    " Entries=" + (snapshot.Entries?.Count ?? 0) +
                    " WinnerSide=" + (snapshot.WinnerSide ?? "none") + ".");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error("CoopBattleResultBridgeFile: failed to write " + path, ex);
                return false;
            }
        }

        public static BattleResultSnapshot ReadResult(bool logRead = true)
        {
            string path = GetResultFilePath();
            try
            {
                if (!TryGetFileStamp(path, out CoopBattleResultFileStamp beforeRead))
                {
                    ReadCache.Invalidate();
                    return null;
                }

                if (ReadCache.TryGet(beforeRead, out BattleResultSnapshot cachedSnapshot))
                {
                    LogReadResult(path, cachedSnapshot, logRead);
                    return cachedSnapshot;
                }

                string json = File.ReadAllText(path);
                if (!TryGetFileStamp(path, out CoopBattleResultFileStamp afterRead) ||
                    !CoopBattleResultReadCacheContract.IsStable(beforeRead, afterRead))
                {
                    return null;
                }

                BattleResultSnapshot snapshot = JsonConvert.DeserializeObject<BattleResultSnapshot>(json);
                if (snapshot == null)
                    return null;

                ReadCache.TryStore(beforeRead, afterRead, snapshot);
                LogReadResult(path, snapshot, logRead);
                return snapshot;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (Exception ex)
            {
                ModLogger.Error("CoopBattleResultBridgeFile: failed to read " + path, ex);
                return null;
            }
        }

        public static void ClearResult(string source)
        {
            string path = GetResultFilePath();
            ReadCache.Invalidate();
            try
            {
                if (!File.Exists(path))
                    return;

                File.Delete(path);
                ModLogger.Info(
                    "CoopBattleResultBridgeFile: cleared result file. " +
                    "Source=" + (source ?? "unknown") +
                    " Path=" + path);
            }
            catch (Exception ex)
            {
                ModLogger.Error("CoopBattleResultBridgeFile: failed to clear " + path, ex);
            }
        }

        private static bool TryGetFileStamp(string path, out CoopBattleResultFileStamp stamp)
        {
            stamp = default(CoopBattleResultFileStamp);
            try
            {
                var fileInfo = new FileInfo(path);
                fileInfo.Refresh();
                if (!fileInfo.Exists)
                    return false;

                stamp = new CoopBattleResultFileStamp(
                    fileInfo.FullName,
                    fileInfo.Length,
                    fileInfo.LastWriteTimeUtc.Ticks);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void LogReadResult(
            string path,
            BattleResultSnapshot snapshot,
            bool logRead)
        {
            if (!logRead || snapshot == null)
                return;

            ModLogger.Info(
                "CoopBattleResultBridgeFile: read result from " + path +
                " BattleId=" + (snapshot.BattleId ?? "null") +
                " Entries=" + (snapshot.Entries?.Count ?? 0) +
                " WinnerSide=" + (snapshot.WinnerSide ?? "none") + ".");
        }
    }
}
