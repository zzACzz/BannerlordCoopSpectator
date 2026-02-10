using CoopSpectator.Infrastructure; // Підключаємо логер для діагностики
using CoopSpectator.Network; // Підключаємо NetworkRole для перевірки ролі (клієнт/сервер)
using HarmonyLib; // Підключаємо Harmony для патчингу методів гри
using TaleWorlds.CampaignSystem.Party; // Підключаємо MobileParty, щоб вказати тип для HarmonyPatch

namespace CoopSpectator.Patches // Оголошуємо простір імен для Harmony-патчів
{ // Починаємо блок простору імен
    /// <summary> // Документуємо клас
    /// Блокує будь-які накази руху для клієнта у spectator mode, щоб клієнт не міг керувати партією на мапі кампанії. // Пояснюємо призначення
    /// </summary> // Завершуємо XML-коментар
    internal static class BlockPartyMovementPatch // Оголошуємо internal static клас, бо Harmony шукає патчі по атрибутам, а екземпляр не потрібен
    { // Починаємо блок класу
        [HarmonyPatch(typeof(MobileParty))] // Вказуємо тип, методи якого ми будемо патчити
        [HarmonyPatch("SetMoveGoToPoint")] // Патчимо конкретний метод, який викликається при кліку на мапі для руху до точки
        private static class SetMoveGoToPointPatch // Оголошуємо вкладений клас патчу, як рекомендує стандарт Harmony
        { // Починаємо блок класу
            private static bool Prefix() // Prefix виконується перед оригінальним методом; false = пропустити оригінал
            { // Починаємо блок методу
                if (!ShouldBlockMovement()) // Перевіряємо чи треба блокувати рух (тільки для клієнта)
                { // Починаємо блок if
                    return true; // Дозволяємо оригінальний метод, якщо ми не клієнт
                } // Завершуємо блок if

                ModLogger.Info("Заблоковано рух партії на клієнті (SetMoveGoToPoint)."); // Логуємо факт блокування для дебагу
                return false; // Блокуємо оригінальний метод, щоб клієнт не міг змінювати маршрут партії
            } // Завершуємо блок методу
        } // Завершуємо блок класу

        private static bool ShouldBlockMovement() // Визначаємо чи треба блокувати наказ руху прямо зараз
        { // Починаємо блок методу
            if (CoopRuntime.Network == null) // Перевіряємо що NetworkManager існує
            { // Починаємо блок if
                return false; // Не блокуємо, бо мод не ініціалізував мережу
            } // Завершуємо блок if

            if (!CoopRuntime.Network.IsRunning) // Перевіряємо що мережа запущена (інакше це звичайний сингл)
            { // Починаємо блок if
                return false; // Не блокуємо, бо без підключення/хосту це не spectator режим
            } // Завершуємо блок if

            if (CoopRuntime.Network.Role != NetworkRole.Client) // Перевіряємо що ми саме клієнт
            { // Починаємо блок if
                return false; // Не блокуємо, бо хост має мати повний контроль
            } // Завершуємо блок if

            return true; // Блокуємо рух, бо клієнт у spectator mode не повинен керувати партією
        } // Завершуємо блок методу
    } // Завершуємо блок класу
} // Завершуємо блок простору імен

