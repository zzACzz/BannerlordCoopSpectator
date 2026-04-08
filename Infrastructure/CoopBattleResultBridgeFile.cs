using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace CoopSpectator.Infrastructure
{
    public static class CoopBattleResultBridgeFile
    {
        private const string CoopSpectatorSubFolder = "CoopSpectator";
        private const string ResultFileName = "battle_result.json";

        public sealed class BattleResultSnapshot
        {
            public string BattleId { get; set; }
            public string BattleType { get; set; }
            public string MapScene { get; set; }
            public string Source { get; set; }
            public string WinnerSide { get; set; }
            public string PlayerSide { get; set; }
            public bool IsSynthetic { get; set; }
            public DateTime UpdatedUtc { get; set; }
            public List<BattleResultEntrySnapshot> Entries { get; set; } = new List<BattleResultEntrySnapshot>();
            public int DroppedCombatEventCount { get; set; }
            public List<BattleResultCombatEventSnapshot> CombatEvents { get; set; } = new List<BattleResultCombatEventSnapshot>();
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
        }

        public sealed class BattleResultCombatEventSnapshot
        {
            public string AttackerEntryId { get; set; }
            public string AttackerSideId { get; set; }
            public string AttackerPartyId { get; set; }
            public string AttackerCharacterId { get; set; }
            public string AttackerOriginalCharacterId { get; set; }
            public string WeaponItemId { get; set; }
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
            if (snapshot == null)
                return false;

            string path = GetResultFilePath();
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
                File.WriteAllText(path, json);
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
                if (!File.Exists(path))
                    return null;

                string json = File.ReadAllText(path);
                BattleResultSnapshot snapshot = JsonConvert.DeserializeObject<BattleResultSnapshot>(json);
                if (snapshot == null)
                    return null;

                if (logRead)
                {
                    ModLogger.Info(
                        "CoopBattleResultBridgeFile: read result from " + path +
                        " BattleId=" + (snapshot.BattleId ?? "null") +
                        " Entries=" + (snapshot.Entries?.Count ?? 0) +
                        " WinnerSide=" + (snapshot.WinnerSide ?? "none") + ".");
                }
                return snapshot;
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
    }
}
