// Файл: Campaign/BattleRosterFile.cs
// Призначення: варіант A — хост записує список troop ID з битви кампанії у файл;
// дедик (коли наш модуль на ньому завантажений) читає цей файл і обмежує вибір юнітів для клієнтів.
// Один і той самий шлях використовується на хоста (запис) і на дедику (читання) при запуску на одному ПК.

using System; // Exception, Environment, SpecialFolder
using System.Collections.Generic; // List<string>
using System.Linq;
using System.IO; // File, Path, Directory
using Newtonsoft.Json; // JsonConvert, серіалізація
using CoopSpectator.Infrastructure; // ModLogger
using CoopSpectator.Network.Messages;

namespace CoopSpectator.Campaign
{
    /// <summary>
    /// DTO для збереження у файл: список StringId юнітів, які можна вибирати в MP-битві (з кампанії).
    /// </summary>
    public sealed class BattleRosterFileDto
    {
        public List<string> TroopIds { get; set; } = new List<string>();
        public BattleSnapshotMessage Snapshot { get; set; }
    }

    /// <summary>
    /// Шлях до файлу roster і методи запису/читання. Шлях: Documents\Mount and Blade II Bannerlord\CoopSpectator\battle_roster.json
    /// </summary>
    public static class BattleRosterFileHelper
    {
        private const string CoopSpectatorSubFolder = "CoopSpectator";
        private const string RosterFileName = "battle_roster.json";

        /// <summary>Повертає повний шлях до файлу battle_roster.json (спільний для хост і дедик на одному ПК).</summary>
        public static string GetRosterFilePath()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string folder = Path.Combine(docs, "Mount and Blade II Bannerlord", CoopSpectatorSubFolder);
            return Path.Combine(folder, RosterFileName);
        }

        /// <summary>Записує список troop ID у файл. Створює папку, якщо її немає. Повертає true при успіху.</summary>
        public static bool WriteRoster(List<string> troopIds)
        {
            return WriteRoster(troopIds, null);
        }

        public static bool WriteRoster(List<string> troopIds, BattleSnapshotMessage snapshot)
        {
            if (troopIds == null)
            {
                ModLogger.Info("BattleRosterFile: WriteRoster skipped (null list).");
                return false;
            }

            string path = GetRosterFilePath();
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var dto = new BattleRosterFileDto
                {
                    TroopIds = troopIds,
                    Snapshot = snapshot
                };
                string json = JsonConvert.SerializeObject(dto, Formatting.Indented);
                File.WriteAllText(path, json);
                ModLogger.Info("BattleRosterFile: wrote " + troopIds.Count + " troop IDs to " + path + " (snapshot sides=" + (snapshot?.Sides?.Count ?? 0) + ").");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error("BattleRosterFile: failed to write " + path, ex);
                return false;
            }
        }

        /// <summary>Читає список troop ID з файлу. Повертає порожній список, якщо файлу немає або помилка.</summary>
        public static List<string> ReadRoster()
        {
            string path = GetRosterFilePath();
            try
            {
                if (!File.Exists(path))
                {
                    ModLogger.Info("BattleRosterFile: no file at " + path + ", using empty roster.");
                    return new List<string>();
                }

                string json = File.ReadAllText(path);
                var dto = JsonConvert.DeserializeObject<BattleRosterFileDto>(json);
                if (dto == null)
                    return new List<string>();

                List<string> troopIds = dto.TroopIds != null && dto.TroopIds.Count > 0
                    ? dto.TroopIds
                    : FlattenTroopIds(dto.Snapshot);
                ModLogger.Info("BattleRosterFile: read " + troopIds.Count + " troop IDs from " + path + " (snapshot sides=" + (dto.Snapshot?.Sides?.Count ?? 0) + ").");
                return troopIds;
            }
            catch (Exception ex)
            {
                ModLogger.Error("BattleRosterFile: failed to read " + path, ex);
                return new List<string>();
            }
        }

        public static BattleSnapshotMessage ReadSnapshot()
        {
            string path = GetRosterFilePath();
            try
            {
                if (!File.Exists(path))
                    return null;

                string json = File.ReadAllText(path);
                var dto = JsonConvert.DeserializeObject<BattleRosterFileDto>(json);
                BattleSnapshotMessage snapshot = dto?.Snapshot;
                if (snapshot?.Sides == null || snapshot.Sides.Count == 0)
                    return null;

                BattleSnapshotRuntimeState.SetCurrent(snapshot, "battle-roster-file");
                ModLogger.Info("BattleRosterFile: read snapshot with " + snapshot.Sides.Count + " sides from " + path);
                return snapshot;
            }
            catch (Exception ex)
            {
                ModLogger.Error("BattleRosterFile: failed to read snapshot from " + path, ex);
                return null;
            }
        }

        private static List<string> FlattenTroopIds(BattleSnapshotMessage snapshot)
        {
            if (snapshot?.Sides == null || snapshot.Sides.Count == 0)
                return new List<string>();

            return snapshot.Sides
                .Where(side => side?.Troops != null)
                .SelectMany(side => side.Troops)
                .Select(troop => troop?.CharacterId)
                .Where(characterId => !string.IsNullOrWhiteSpace(characterId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
