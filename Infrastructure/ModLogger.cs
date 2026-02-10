using System; // Підключаємо базові типи .NET (Exception)
using System.Diagnostics; // Підключаємо System.Diagnostics як fallback логування (на випадок якщо API гри недоступне)
using TWDebug = TaleWorlds.Library.Debug; // Створюємо alias щоб явно викликати Debug.Print з TaleWorlds, а не з System.Diagnostics

namespace CoopSpectator.Infrastructure // Оголошуємо простір імен для інфраструктурних утиліт
{ // Починаємо блок простору імен
    /// <summary> // Документуємо клас
    /// Простий логер для моду з префіксом, щоб легше фільтрувати повідомлення у логах гри. // Пояснюємо навіщо існує
    /// </summary> // Завершуємо XML-коментар
    public static class ModLogger // Оголошуємо статичний клас, бо логер не потребує стану екземпляра
    { // Починаємо блок класу
        private const string Prefix = "[CoopSpectator]"; // Визначаємо префікс, який буде доданий до кожного повідомлення

        public static void Info(string message) // Оголошуємо метод для інформаційних повідомлень
        { // Починаємо блок методу
            Print("INFO", message, null); // Друкуємо повідомлення з рівнем INFO
        } // Завершуємо блок методу

        public static void Warn(string message) // Оголошуємо метод для попереджень
        { // Починаємо блок методу
            Print("WARN", message, null); // Друкуємо повідомлення з рівнем WARN
        } // Завершуємо блок методу

        public static void Error(string message, Exception exception) // Оголошуємо метод для помилок з винятком
        { // Починаємо блок методу
            Print("ERROR", message, exception); // Друкуємо повідомлення з рівнем ERROR і деталями винятку
        } // Завершуємо блок методу

        private static void Print(string level, string message, Exception exception) // Оголошуємо спільний метод друку, щоб не дублювати форматування
        { // Починаємо блок методу
            string safeMessage = message ?? string.Empty; // Гарантуємо що message не null, щоб не отримати NullReferenceException при конкатенації
            string exceptionText = exception != null ? (" | " + exception) : string.Empty; // Додаємо текст винятку тільки якщо він є
            string line = $"{Prefix} {level}: {safeMessage}{exceptionText}"; // Формуємо фінальний рядок для логу

            try // Пробуємо використати логер гри, бо це зручніше для Bannerlord
            { // Починаємо блок try
                TWDebug.Print(line); // Друкуємо у лог гри через TaleWorlds.Library.Debug.Print (найбільш сумісний overload)
            } // Завершуємо блок try
            catch (Exception) // Якщо API гри недоступне (наприклад, під час тестів поза грою), падаємо назад на System.Diagnostics
            { // Починаємо блок catch
                Debug.WriteLine(line); // Пишемо у стандартний debug output, щоб не втратити інформацію
            } // Завершуємо блок catch
        } // Завершуємо блок методу
    } // Завершуємо блок класу
} // Завершуємо блок простору імен

