using System; // Підключаємо базові типи .NET (Type, Environment)
using CoopSpectator.Infrastructure; // Підключаємо UI-хелпер та логер
using CoopSpectator.Network; // Підключаємо NetworkRole для перевірки ролі (клієнт/сервер)
using HarmonyLib; // Підключаємо Harmony для патчингу методів гри
using TaleWorlds.CampaignSystem.GameMenus; // Підключаємо GameMenuManager, який керує переходами між campaign-меню

namespace CoopSpectator.Patches // Оголошуємо простір імен для Harmony-патчів
{ // Починаємо блок простору імен
    /// <summary> // Документуємо клас
    /// Блокує переходи в settlement-меню (town/castle/village/і т.п.) для клієнта, патчачи GameMenuManager.SetNextMenu. // Пояснюємо призначення
    /// </summary> // Завершуємо XML-коментар
    internal static class BlockSettlementMenusPatch // Оголошуємо internal static, бо це набір патчів без інстансів
    { // Починаємо блок класу
        private static uint _lastUiTickSeconds; // Store last UI notify time as unsigned seconds (wrap-safe)
        private const uint UiCooldownSeconds = 2u; // Cooldown in seconds between notifications

        [HarmonyPatch(typeof(GameMenuManager))] // Вказуємо тип, який керує переходами між ігровими меню кампанії
        [HarmonyPatch("SetNextMenu")] // Патчимо метод, який виставляє наступне меню по string Id
        [HarmonyPatch(new Type[] { typeof(string) })] // Явно задаємо сигнатуру, щоб Harmony точно знайшов потрібний метод
        private static class SetNextMenuPatch // Вкладений клас патчу за стандартом Harmony
        { // Починаємо блок класу
            // ВАЖЛИВО: параметр має називатись як у оригіналі (`name`), // Пояснюємо причину
            // інакше Harmony не зможе замапити аргумент і патч не застосовується. // Пояснюємо помилку "Parameter not found"
            private static bool Prefix(string name) // Prefix: false = пропустити оригінал (тобто не дозволити перехід у меню)
            { // Починаємо блок методу
                if (!ShouldBlockMenus()) // Якщо ми не клієнт у мережевому режимі, нічого не блокуємо
                { // Починаємо блок if
                    return true; // Дозволяємо оригінальний SetNextMenu
                } // Завершуємо блок if

                // Якщо зараз активний countdown запрошення в бій, блокуємо будь-які переходи в меню, // Пояснюємо "hard lock"
                // щоб клієнт не міг відкривати екрани під час підготовки до битви. // Пояснюємо навіщо
                if (ClientBattleInvitationLock.IsActive) // Перевіряємо глобальний battle lock
                { // Починаємо блок if
                    ThrottledNotify("BATTLE_LOCK:" + (name ?? "null")); // Показуємо повідомлення з cooldown
                    return false; // Блокуємо будь-які меню переходи під час lock
                } // Завершуємо блок if

                if (!IsSettlementMenuId(name)) // Якщо це не settlement-меню, дозволяємо перехід (наприклад, encounter/лоадінг/тощо)
                { // Починаємо блок if
                    return true; // Дозволяємо оригінал, бо це не “місто/село/замок”
                } // Завершуємо блок if

                ThrottledNotify(name); // Показуємо коротке повідомлення гравцю з cooldown
                return false; // Блокуємо відкриття settlement-меню для клієнта
            } // Завершуємо блок методу
        } // Завершуємо блок класу

        private static bool ShouldBlockMenus() // Визначаємо чи ми маємо блокувати settlement-меню прямо зараз
        { // Починаємо блок методу
            if (CoopRuntime.Network == null) // Перевіряємо що NetworkManager існує
            { // Починаємо блок if
                return false; // Не блокуємо, бо мережа не ініціалізована
            } // Завершуємо блок if

            if (!CoopRuntime.Network.IsRunning) // Перевіряємо що мережа запущена (інакше це звичайний сингл)
            { // Починаємо блок if
                return false; // Не блокуємо, бо spectator режим не активний
            } // Завершуємо блок if

            if (CoopRuntime.Network.Role != NetworkRole.Client) // Перевіряємо що ми клієнт, а не хост
            { // Починаємо блок if
                return false; // Не блокуємо, бо хост має мати доступ до меню
            } // Завершуємо блок if

            return true; // Повертаємо true, бо ми клієнт у spectator mode
        } // Завершуємо блок методу

        private static bool IsSettlementMenuId(string menuId) // Перевіряємо чи menuId схожий на меню міста/села/замку
        { // Починаємо блок методу
            if (string.IsNullOrEmpty(menuId)) // Перевіряємо null/порожнє, щоб не падати на ToLowerInvariant
            { // Починаємо блок if
                return false; // Порожній Id не вважаємо settlement-меню
            } // Завершуємо блок if

            string id = menuId.ToLowerInvariant(); // Нормалізуємо для простих contains-перевірок без залежності від регістру

            if (id.Contains("town")) return true; // Блокуємо всі меню з "town" (типово: town, town_keep, town_arena, ...)
            if (id.Contains("castle")) return true; // Блокуємо меню замків
            if (id.Contains("village")) return true; // Блокуємо меню сіл
            if (id.Contains("settlement")) return true; // Блокуємо загальні settlement меню
            if (id.Contains("keep")) return true; // Блокуємо keep (часто частина town/castle меню)
            if (id.Contains("tavern")) return true; // Блокуємо таверни (підменю міста)
            if (id.Contains("market")) return true; // Блокуємо ринок (підменю міста)
            if (id.Contains("arena")) return true; // Блокуємо арену (підменю міста)

            return false; // Якщо жоден ключ не співпав, вважаємо що це не settlement-меню
        } // Завершуємо блок методу

        private static void ThrottledNotify(string menuId) // Показуємо повідомлення з cooldown, щоб не спамити при багаторазових викликах
        { // Починаємо блок методу
            uint nowSeconds = unchecked((uint)Environment.TickCount) / 1000u; // Unsigned seconds to avoid negative values after TickCount overflow

            if (nowSeconds - _lastUiTickSeconds < UiCooldownSeconds) // If cooldown has not elapsed, skip
            { // Починаємо блок if
                return; // Виходимо, щоб не спамити повідомленнями
            } // Завершуємо блок if

            _lastUiTickSeconds = nowSeconds; // Оновлюємо час останнього повідомлення
            ModLogger.Info("Заблоковано SetNextMenu для клієнта: " + (menuId ?? "null")); // Логуємо Id меню для дебагу

            UiFeedback.ShowMessageDeferred("Spectator: settlement menus are disabled."); // Defer UI message to avoid being lost during transitions
        } // Завершуємо блок методу
    } // Завершуємо блок класу
} // Завершуємо блок простору імен

