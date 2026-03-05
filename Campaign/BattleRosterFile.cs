// Файл: Campaign/BattleRosterFile.cs
// Призначення: варіант A — хост записує список troop ID з битви кампанії у файл;
// дедик (коли наш модуль на ньому завантажений) читає цей файл і обмежує вибір юнітів для клієнтів.
// Один і той самий шлях використовується на хоста (запис) і на дедику (читання) при запуску на одному ПК.

using System; // Exception, Environment, SpecialFolder
using System.Collections.Generic; // List<string>
using System.IO; // File, Path, Directory
using Newtonsoft.Json; // JsonConvert, серіалізація
using CoopSpectator.Infrastructure; // ModLogger

namespace CoopSpectator.Campaign
{
    /// <summary>
    /// DTO для збереження у файл: список StringId юнітів, які можна вибирати в MP-битві (з кампанії).
    /// </summary>
    public sealed class BattleRosterFileDto
    {
        public List<string> TroopIds { get; set; } = new List<string>();
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

                var dto = new BattleRosterFileDto { TroopIds = troopIds };
                string json = JsonConvert.SerializeObject(dto, Formatting.Indented);
                File.WriteAllText(path, json);
                ModLogger.Info("BattleRosterFile: wrote " + troopIds.Count + " troop IDs to " + path);
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
                if (dto?.TroopIds == null)
                    return new List<string>();
                ModLogger.Info("BattleRosterFile: read " + dto.TroopIds.Count + " troop IDs from " + path);
                return dto.TroopIds;
            }
            catch (Exception ex)
            {
                ModLogger.Error("BattleRosterFile: failed to read " + path, ex);
                return new List<string>();
            }
        }
    }
}
