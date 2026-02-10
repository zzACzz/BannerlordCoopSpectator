using System; // Підключаємо базові типи .NET (String, Int32)
using System.Collections.Generic; // Підключаємо List<string> для аргументів консольних команд
using CoopSpectator.Infrastructure; // Підключаємо логер для повідомлень
using TaleWorlds.Library; // Підключаємо CommandLineFunctionality для реєстрації команд у консолі Bannerlord

namespace CoopSpectator.Commands // Оголошуємо простір імен для консольних команд
{ // Починаємо блок простору імен
    /// <summary> // Документуємо клас
    /// Набір консольних команд для швидкого тестування TCP (host/join/send/status). // Пояснюємо для чого це
    /// </summary> // Завершуємо XML-коментар
    public static class CoopConsoleCommands // Оголошуємо статичний клас, бо команди повинні бути static
    { // Починаємо блок класу
        [CommandLineFunctionality.CommandLineArgumentFunction("host", "coop")] // Реєструємо команду `coop.host [port]`
        public static string Host(List<string> args) // Оголошуємо хост-команду яка стартує TCP сервер
        { // Починаємо блок методу
            CoopRuntime.Initialize(); // Гарантуємо що runtime створений, щоб NetworkManager існував

            int port = 7777; // Встановлюємо дефолтний порт (узгоджено з планом)

            if (args != null && args.Count > 0) // Якщо користувач передав аргумент, пробуємо прочитати порт
            { // Починаємо блок if
                int parsed; // Оголошуємо змінну для результату парсингу

                if (int.TryParse(args[0], out parsed)) // Парсимо перший аргумент як число
                { // Починаємо блок if
                    port = parsed; // Перезаписуємо порт якщо парсинг успішний
                } // Завершуємо блок if
            } // Завершуємо блок if

            try // Обгортаємо запуск сервера в try-catch, щоб не крашнути гру
            { // Починаємо блок try
                CoopRuntime.Network.StartServer(port); // Запускаємо TCP сервер
                return "Server started. Waiting for clients on port " + port + "."; // Повертаємо текст у консоль гри
            } // Завершуємо блок try
            catch (Exception ex) // Ловимо будь-які помилки запуску сервера
            { // Починаємо блок catch
                ModLogger.Error("Не вдалося запустити сервер.", ex); // Логуємо помилку
                return "ERROR: Failed to start server: " + ex.Message; // Повертаємо коротку причину у консоль
            } // Завершуємо блок catch
        } // Завершуємо блок методу

        [CommandLineFunctionality.CommandLineArgumentFunction("join", "coop")] // Реєструємо команду `coop.join <ip> [port]`
        public static string Join(List<string> args) // Оголошуємо join-команду яка підключає клієнта до хоста
        { // Починаємо блок методу
            CoopRuntime.Initialize(); // Гарантуємо що runtime створений

            if (args == null || args.Count == 0) // Перевіряємо що користувач передав IP
            { // Починаємо блок if
                return "Usage: coop.join <host_ip> [port]"; // Повертаємо підказку по використанню
            } // Завершуємо блок if

            string host = args[0]; // Зчитуємо IP/hostname з першого аргументу
            int port = 7777; // Встановлюємо дефолтний порт

            if (args.Count > 1) // Якщо є другий аргумент, пробуємо прочитати порт
            { // Починаємо блок if
                int parsed; // Оголошуємо змінну для результату парсингу

                if (int.TryParse(args[1], out parsed)) // Парсимо другий аргумент як порт
                { // Починаємо блок if
                    port = parsed; // Перезаписуємо порт якщо парсинг успішний
                } // Завершуємо блок if
            } // Завершуємо блок if

            try // Обгортаємо підключення в try-catch
            { // Починаємо блок try
                CoopRuntime.Network.ConnectToServer(host, port); // Підключаємося до сервера
                return "Connected to " + host + ":" + port + "."; // Повертаємо успішний результат у консоль
            } // Завершуємо блок try
            catch (Exception ex) // Ловимо будь-які помилки підключення
            { // Починаємо блок catch
                ModLogger.Error("Не вдалося підключитися до хоста.", ex); // Логуємо помилку
                return "ERROR: Failed to connect: " + ex.Message; // Повертаємо коротку причину у консоль
            } // Завершуємо блок catch
        } // Завершуємо блок методу

        [CommandLineFunctionality.CommandLineArgumentFunction("send", "coop")] // Реєструємо команду `coop.send <message...>`
        public static string Send(List<string> args) // Оголошуємо команду для відправки тестового повідомлення
        { // Починаємо блок методу
            if (CoopRuntime.Network == null) // Перевіряємо що NetworkManager існує
            { // Починаємо блок if
                return "Network is not initialized. Use coop.host or coop.join first."; // Повертаємо підказку
            } // Завершуємо блок if

            if (args == null || args.Count == 0) // Перевіряємо що користувач передав текст
            { // Починаємо блок if
                return "Usage: coop.send <message...>"; // Повертаємо підказку по використанню
            } // Завершуємо блок if

            string message = string.Join(" ", args.ToArray()); // Об'єднуємо всі аргументи в один рядок (щоб підтримати пробіли)

            if (CoopRuntime.Network.Role == Network.NetworkRole.Client) // Якщо ми клієнт, відправляємо на сервер
            { // Починаємо блок if
                CoopRuntime.Network.SendToServer(message); // Відправляємо повідомлення хосту
                return "Sent to server: " + message; // Повертаємо підтвердження
            } // Завершуємо блок if

            if (CoopRuntime.Network.Role == Network.NetworkRole.Server) // Якщо ми сервер, робимо broadcast всім клієнтам
            { // Починаємо блок if
                CoopRuntime.Network.BroadcastToClients(message); // Відправляємо повідомлення всім клієнтам
                return "Broadcasted to clients: " + message; // Повертаємо підтвердження
            } // Завершуємо блок if

            return "Network role is None. Use coop.host or coop.join first."; // Повертаємо підказку якщо мережа не запущена
        } // Завершуємо блок методу

        [CommandLineFunctionality.CommandLineArgumentFunction("status", "coop")] // Реєструємо команду `coop.status`
        public static string Status(List<string> args) // Оголошуємо команду для перегляду поточного стану мережі
        { // Починаємо блок методу
            if (CoopRuntime.Network == null) // Перевіряємо чи runtime ініціалізований
            { // Починаємо блок if
                return "Status: Network is not initialized."; // Повертаємо статус
            } // Завершуємо блок if

            return "Status: Role=" + CoopRuntime.Network.Role + ", IsRunning=" + CoopRuntime.Network.IsRunning + "."; // Повертаємо короткий опис стану
        } // Завершуємо блок методу
    } // Завершуємо блок класу
} // Завершуємо блок простору імен

