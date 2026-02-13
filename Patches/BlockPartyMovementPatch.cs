using System; // Use Environment.TickCount for cooldown timing
using CoopSpectator.Infrastructure; // Підключаємо логер для діагностики
using CoopSpectator.Network; // Підключаємо NetworkRole для перевірки ролі (клієнт/сервер)
using HarmonyLib; // Підключаємо Harmony для патчингу методів гри
using TaleWorlds.CampaignSystem; // Підключаємо NavigationType, CampaignVec2 та інші базові типи кампанії
using TaleWorlds.CampaignSystem.Party; // Підключаємо MobileParty, щоб патчити його методи
using TaleWorlds.CampaignSystem.Settlements; // Підключаємо Settlement для методів руху до поселень
using TaleWorlds.CampaignSystem.MapEvents; // Підключаємо IInteractablePoint для руху до інтерактивних точок на мапі

namespace CoopSpectator.Patches // Оголошуємо простір імен для Harmony-патчів
{ // Починаємо блок простору імен
    /// <summary> // Документуємо клас
    /// Блокує будь-які накази руху для клієнта у spectator mode, щоб клієнт не міг керувати партією на мапі кампанії. // Пояснюємо призначення
    /// </summary> // Завершуємо XML-коментар
    internal static class BlockPartyMovementPatch // Оголошуємо internal static клас, бо Harmony шукає патчі по атрибутам, а екземпляр не потрібен
    { // Починаємо блок класу
        private static uint _lastUiTickSeconds; // Last time we showed UI feedback (unsigned seconds, wrap-safe)
        private const uint UiCooldownSeconds = 2u; // Cooldown between UI messages to prevent spam

        [HarmonyPatch(typeof(MobileParty))] // Вказуємо тип, методи якого ми будемо патчити
        [HarmonyPatch("SetMoveGoToPoint")] // Патчимо рух до довільної точки (клік по землі)
        private static class SetMoveGoToPointPatch // Вкладений патч-клас для SetMoveGoToPoint
        { // Починаємо блок класу
            private static bool Prefix() // Prefix виконується перед оригінальним методом; false = пропустити оригінал
            { // Починаємо блок методу
                if (!ShouldBlockMovement()) // Перевіряємо чи треба блокувати рух (тільки для клієнта)
                { // Починаємо блок if
                    return true; // Дозволяємо оригінальний метод, якщо ми не клієнт
                } // Завершуємо блок if

                ThrottledNotify("SetMoveGoToPoint"); // Show reliable UI feedback (throttled)
                return false; // Блокуємо оригінальний метод, щоб клієнт не міг змінювати маршрут партії
            } // Завершуємо блок методу
        } // Завершуємо блок класу

        [HarmonyPatch(typeof(MobileParty))] // Вказуємо тип для патчу
        [HarmonyPatch("SetMoveGoToSettlement")] // Патчимо рух до поселення (клік по місту/селу/замку)
        private static class SetMoveGoToSettlementPatch // Вкладений патч-клас для SetMoveGoToSettlement
        { // Починаємо блок класу
            private static bool Prefix() // Prefix: false блокує оригінальний виклик
            { // Починаємо блок методу
                if (!ShouldBlockMovement()) // Якщо не клієнт — не блокуємо
                { // Починаємо блок if
                    return true; // Дозволяємо оригінал
                } // Завершуємо блок if

                ThrottledNotify("SetMoveGoToSettlement"); // Show reliable UI feedback (throttled)
                return false; // Блокуємо рух до settlement
            } // Завершуємо блок методу
        } // Завершуємо блок класу

        [HarmonyPatch(typeof(MobileParty))] // Вказуємо тип для патчу
        [HarmonyPatch("SetMoveEngageParty")] // Патчимо рух/атака до іншого загону (клік по загону)
        private static class SetMoveEngagePartyPatch // Вкладений патч-клас для SetMoveEngageParty
        { // Починаємо блок класу
            private static bool Prefix() // Prefix: false блокує оригінальний виклик
            { // Починаємо блок методу
                if (!ShouldBlockMovement()) // Якщо не клієнт — не блокуємо
                { // Починаємо блок if
                    return true; // Дозволяємо оригінал
                } // Завершуємо блок if

                ThrottledNotify("SetMoveEngageParty"); // Show reliable UI feedback (throttled)
                return false; // Блокуємо рух до загону
            } // Завершуємо блок методу
        } // Завершуємо блок класу

        [HarmonyPatch(typeof(MobileParty))] // Вказуємо тип для патчу
        [HarmonyPatch("SetMoveGoToInteractablePoint")] // Патчимо рух до інтерактивної точки на мапі (наприклад, “точка взаємодії”)
        private static class SetMoveGoToInteractablePointPatch // Вкладений патч-клас для SetMoveGoToInteractablePoint
        { // Починаємо блок класу
            private static bool Prefix() // Prefix: false блокує оригінальний виклик
            { // Починаємо блок методу
                if (!ShouldBlockMovement()) // Якщо не клієнт — не блокуємо
                { // Починаємо блок if
                    return true; // Дозволяємо оригінал
                } // Завершуємо блок if

                ThrottledNotify("SetMoveGoToInteractablePoint"); // Show reliable UI feedback (throttled)
                return false; // Блокуємо рух
            } // Завершуємо блок методу
        } // Завершуємо блок класу

        private static void ThrottledNotify(string blockedMethod) // Show message with cooldown to avoid spam
        { // Begin method
            uint nowSeconds = unchecked((uint)Environment.TickCount) / 1000u; // Unsigned seconds avoid negative values after TickCount overflow

            if (nowSeconds - _lastUiTickSeconds < UiCooldownSeconds) // If cooldown has not elapsed
            { // Begin if
                return; // Skip showing/logging again
            } // End if

            _lastUiTickSeconds = nowSeconds; // Update last UI feedback time

            ModLogger.Info("Blocked party movement for client (" + (blockedMethod ?? "?") + ")."); // Log once per cooldown

            UiFeedback.ShowMessageDeferred("Spectator: party movement is disabled."); // Defer UI message so it reliably appears
        } // End method

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

