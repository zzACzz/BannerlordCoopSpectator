using System; // Підключаємо базові типи .NET (Exception)
using Newtonsoft.Json; // Підключаємо Newtonsoft.Json для серіалізації/десеріалізації

namespace CoopSpectator.Network.Messages // Оголошуємо простір імен для мережевих моделей/кодеків
{ // Починаємо блок простору імен
    /// <summary> // Документуємо клас
    /// Helper для перетворення HostGameState ↔ JSON та формування повідомлень протоколу `STATE:{json}`. // Пояснюємо призначення
    /// </summary> // Завершуємо XML-коментар
    internal static class HostGameStateCodec // Оголошуємо static клас, бо це набір чистих функцій без стану
    { // Починаємо блок класу
        internal static string BuildStateMessage(HostGameState state) // Формуємо повне текстове повідомлення для відправки по TCP
        { // Починаємо блок методу
            string json = JsonConvert.SerializeObject(state); // Серіалізуємо модель у JSON (компактно, в один рядок)
            return NetworkMessagePrefixes.State + json; // Додаємо префікс типу повідомлення, щоб receiver знав як його обробити
        } // Завершуємо блок методу

        internal static bool TryParseStateMessage(string message, out HostGameState state) // Парсимо вхідний текст і, якщо це STATE, повертаємо десеріалізований стан
        { // Починаємо блок методу
            state = null; // Ініціалізуємо out-параметр, щоб у випадку false він був передбачуваним

            if (string.IsNullOrEmpty(message)) // Перевіряємо що повідомлення не null і не порожнє
            { // Починаємо блок if
                return false; // Повертаємо false, бо парсити нічого
            } // Завершуємо блок if

            if (!message.StartsWith(NetworkMessagePrefixes.State, StringComparison.Ordinal)) // Перевіряємо тип повідомлення по префіксу STATE:
            { // Починаємо блок if
                return false; // Повертаємо false, бо це не STATE повідомлення
            } // Завершуємо блок if

            string json = message.Substring(NetworkMessagePrefixes.State.Length); // Відрізаємо префікс, залишаючи тільки JSON

            if (string.IsNullOrEmpty(json)) // Перевіряємо що після префіксу залишився хоч якийсь JSON
            { // Починаємо блок if
                return false; // Повертаємо false, бо десеріалізувати нічого
            } // Завершуємо блок if

            try // Обгортаємо десеріалізацію в try-catch, бо мережеві дані можуть бути некоректними
            { // Починаємо блок try
                state = JsonConvert.DeserializeObject<HostGameState>(json); // Десеріалізуємо JSON у модель стану
                return state != null; // Повертаємо true тільки якщо отримали не-null модель
            } // Завершуємо блок try
            catch (Exception) // Ловимо будь-які винятки десеріалізації і трактуємо як "не вдалося"
            { // Починаємо блок catch
                state = null; // Гарантуємо що state null у випадку помилки
                return false; // Повертаємо false, бо JSON був некоректний/несумісний
            } // Завершуємо блок catch
        } // Завершуємо блок методу
    } // Завершуємо блок класу
} // Завершуємо блок простору імен

