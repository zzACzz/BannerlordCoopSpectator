using System; // Підключаємо базові типи .NET (Exception, StringComparison)
using Newtonsoft.Json; // Підключаємо Newtonsoft.Json для JSON серіалізації/десеріалізації

namespace CoopSpectator.Network.Messages // Оголошуємо простір імен для мережевих моделей/кодеків
{ // Починаємо блок простору імен
    /// <summary> // Документуємо клас
    /// Helper для `BattleStartMessage` ↔ JSON та формування протокольного рядка `BATTLE_START:{json}`. // Пояснюємо що саме робить кодек
    /// </summary> // Завершуємо XML-коментар
    internal static class BattleStartMessageCodec // Оголошуємо static, бо тут лише чисті функції без стану
    { // Починаємо блок класу
        internal static string BuildBattleStartMessage(BattleStartMessage message) // Формуємо повне TCP-повідомлення для відправки
        { // Починаємо блок методу
            string json = JsonConvert.SerializeObject(message); // Серіалізуємо DTO у JSON (в один рядок)
            return NetworkMessagePrefixes.BattleStart + json; // Додаємо префікс типу повідомлення, щоб receiver міг розпізнати
        } // Завершуємо блок методу

        internal static bool TryParseBattleStartMessage(string message, out BattleStartMessage battleStart) // Пробуємо розпізнати та розпарсити `BATTLE_START:{json}`
        { // Починаємо блок методу
            battleStart = null; // Ініціалізуємо out-параметр, щоб у випадку false він був передбачуваним

            if (string.IsNullOrEmpty(message)) // Перевіряємо що повідомлення не null і не порожнє
            { // Починаємо блок if
                return false; // Повертаємо false, бо парсити нічого
            } // Завершуємо блок if

            if (!message.StartsWith(NetworkMessagePrefixes.BattleStart, StringComparison.Ordinal)) // Перевіряємо префікс типу повідомлення
            { // Починаємо блок if
                return false; // Повертаємо false, бо це не `BATTLE_START:`
            } // Завершуємо блок if

            string json = message.Substring(NetworkMessagePrefixes.BattleStart.Length); // Відрізаємо префікс, залишаючи тільки JSON

            if (string.IsNullOrEmpty(json)) // Перевіряємо що JSON після префікса не порожній
            { // Починаємо блок if
                return false; // Повертаємо false, бо десеріалізувати нічого
            } // Завершуємо блок if

            try // Обгортаємо десеріалізацію, бо мережеві дані можуть бути некоректними
            { // Починаємо блок try
                battleStart = JsonConvert.DeserializeObject<BattleStartMessage>(json); // Десеріалізуємо JSON у DTO
                return battleStart != null; // Успіх тільки якщо DTO не null
            } // Завершуємо блок try
            catch (Exception) // Ловимо будь-які помилки JSON (несумісність, битий JSON, тощо)
            { // Починаємо блок catch
                battleStart = null; // Гарантуємо null у випадку помилки
                return false; // Повертаємо false, бо парсинг не вдався
            } // Завершуємо блок catch
        } // Завершуємо блок методу
    } // Завершуємо блок класу
} // Завершуємо блок простору імен

