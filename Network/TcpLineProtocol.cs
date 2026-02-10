using System.Text; // Підключаємо типи для роботи з текстом (Encoding, StringBuilder)

namespace CoopSpectator.Network // Оголошуємо простір імен для мережевого шару
{ // Починаємо блок простору імен
    /// <summary> // Документуємо клас
    /// Простий протокол поверх TCP: одне повідомлення = один рядок, розділений символом '\n'. // Пояснюємо формат
    /// </summary> // Завершуємо XML-коментар
    internal static class TcpLineProtocol // Оголошуємо internal, бо це деталь реалізації мережевого шару
    { // Починаємо блок класу
        internal static readonly Encoding Utf8 = new UTF8Encoding(false); // Використовуємо UTF-8 без BOM, щоб коректно передавати будь-який текст
        internal const char Delimiter = '\n'; // Визначаємо роздільник повідомлень у потоці TCP

        internal static string SanitizeForSingleLine(string message) // Оголошуємо метод для нормалізації повідомлення перед відправкою
        { // Починаємо блок методу
            if (message == null) // Перевіряємо null, щоб не отримати виняток при Replace
            { // Починаємо блок if
                return string.Empty; // Повертаємо порожній рядок як безпечний дефолт
            } // Завершуємо блок if

            string withoutCarriageReturn = message.Replace("\r", " "); // Замінюємо '\r' пробілом, щоб не ламати протокол рядків
            string withoutNewLine = withoutCarriageReturn.Replace("\n", " "); // Замінюємо '\n' пробілом, щоб повідомлення не розбивалося на кілька
            return withoutNewLine; // Повертаємо очищений рядок, який гарантовано не містить переносів
        } // Завершуємо блок методу

        internal static byte[] EncodeLine(string message) // Оголошуємо метод кодування одного рядка в байти
        { // Починаємо блок методу
            string sanitized = SanitizeForSingleLine(message); // Очищаємо повідомлення від переносів рядка
            string line = sanitized + Delimiter; // Додаємо роздільник в кінець, щоб receiver міг відокремити повідомлення
            return Utf8.GetBytes(line); // Кодуємо рядок в UTF-8 байти для відправки в NetworkStream
        } // Завершуємо блок методу

        internal static bool TryExtractLine(StringBuilder buffer, out string line) // Оголошуємо метод витягування одного повного рядка з буфера
        { // Починаємо блок методу
            line = null; // Ініціалізуємо out-параметр, щоб у випадку false значення було передбачуваним

            if (buffer == null) // Перевіряємо буфер на null для безпечної роботи
            { // Починаємо блок if
                return false; // Повертаємо false, бо немає з чого витягувати рядки
            } // Завершуємо блок if

            for (int i = 0; i < buffer.Length; i++) // Проходимо по буферу, щоб знайти перший роздільник '\n'
            { // Починаємо блок for
                if (buffer[i] != Delimiter) // Якщо поточний символ не є роздільником, продовжуємо пошук
                { // Починаємо блок if
                    continue; // Переходимо до наступного символу
                } // Завершуємо блок if

                string rawLine = buffer.ToString(0, i); // Беремо підрядок від початку буфера до символу '\n' (сам '\n' не включаємо)
                buffer.Remove(0, i + 1); // Видаляємо з буфера вже оброблену частину разом з '\n'
                line = rawLine.TrimEnd('\r'); // Прибираємо можливий '\r' в кінці (якщо відправник використовував CRLF)
                return true; // Повертаємо true, бо ми успішно витягнули один повний рядок
            } // Завершуємо блок for

            return false; // Повертаємо false, бо повного рядка (з '\n') у буфері ще немає
        } // Завершуємо блок методу
    } // Завершуємо блок класу
} // Завершуємо блок простору імен

