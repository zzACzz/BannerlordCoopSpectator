using System; // Підключаємо базові типи .NET (Type)
using CoopSpectator.Infrastructure; // Підключаємо логер для діагностики
using CoopSpectator.Network; // Підключаємо NetworkRole для перевірки ролі (клієнт/сервер)
using HarmonyLib; // Підключаємо Harmony для патчингу методів гри
using TaleWorlds.CampaignSystem; // Підключаємо Hero (параметр методу OnSettlementEntered)
using TaleWorlds.CampaignSystem.CampaignBehaviors; // Підключаємо PlayerTownVisitCampaignBehavior (клас, який відкриває town menus)
using TaleWorlds.CampaignSystem.Party; // Підключаємо MobileParty (параметр методу OnSettlementEntered)
using TaleWorlds.CampaignSystem.Settlements; // Підключаємо Settlement (параметр методу OnSettlementEntered)
using System.Threading; // Підключаємо типи для часу/потоків (Environment.TickCount)

namespace CoopSpectator.Patches // Оголошуємо простір імен для Harmony-патчів
{ // Починаємо блок простору імен
    /// <summary> // Документуємо клас
    /// Блокує відкриття town menu/відвідування поселень для клієнта, щоб у spectator mode клієнт не міг взаємодіяти з містами. // Пояснюємо призначення
    /// </summary> // Завершуємо XML-коментар
    internal static class BlockTownMenuPatch // Оголошуємо internal static, бо це набір патчів без стану екземпляра
    { // Починаємо блок класу
        private static uint _lastUiTickSeconds; // Store last UI notify time as unsigned seconds (wrap-safe)
        private const uint UiCooldownSeconds = 2u; // Cooldown in seconds between notifications

        [HarmonyPatch(typeof(PlayerTownVisitCampaignBehavior))] // Вказуємо клас, метод якого ми патчимо
        [HarmonyPatch("OnSettlementEntered")] // Патчимо приватний метод, який викликається коли партія гравця входить у поселення (і гра готує/відкриває town menu)
        [HarmonyPatch(new Type[] { typeof(MobileParty), typeof(Settlement), typeof(Hero) })] // Явно задаємо сигнатуру, щоб Harmony знайшов правильний overload
        private static class OnSettlementEnteredPatch // Оголошуємо вкладений клас патчу за стандартом Harmony
        { // Починаємо блок класу
            private static bool Prefix(MobileParty party, Settlement settlement, Hero hero) // Prefix виконується перед оригінальним методом; false = пропустити оригінал
            { // Починаємо блок методу
                if (!ShouldBlockTownInteraction()) // Перевіряємо чи треба блокувати (тільки для клієнта)
                { // Починаємо блок if
                    return true; // Дозволяємо оригінальний метод, якщо ми не клієнт
                } // Завершуємо блок if

                ThrottledLogBlock(settlement); // Логуємо блокування з cooldown, щоб було видно що патч працює
                return false; // Блокуємо оригінальний метод, щоб town menu не відкривалось для клієнта
            } // Завершуємо блок методу
        } // Завершуємо блок класу

        private static bool ShouldBlockTownInteraction() // Визначаємо чи треба блокувати вхід/меню поселення прямо зараз
        { // Починаємо блок методу
            if (CoopRuntime.Network == null) // Перевіряємо що NetworkManager існує
            { // Починаємо блок if
                return false; // Не блокуємо, бо мод не ініціалізував мережу
            } // Завершуємо блок if

            if (!CoopRuntime.Network.IsRunning) // Перевіряємо що мережа запущена (інакше це звичайний сингл)
            { // Починаємо блок if
                return false; // Не блокуємо, бо без мережі це не spectator режим
            } // Завершуємо блок if

            if (CoopRuntime.Network.Role != NetworkRole.Client) // Перевіряємо що ми саме клієнт
            { // Починаємо блок if
                return false; // Не блокуємо, бо хост має мати повний контроль
            } // Завершуємо блок if

            return true; // Блокуємо, бо клієнт у spectator mode не повинен заходити в поселення/відкривати town menu
        } // Завершуємо блок методу

        private static void ThrottledLogBlock(Settlement settlement) // Логуємо блокування з cooldown, щоб не спамити в лог кожен виклик
        { // Починаємо блок методу
            uint nowSeconds = unchecked((uint)Environment.TickCount) / 1000u; // Unsigned seconds to avoid negative values after TickCount overflow

            if (nowSeconds - _lastUiTickSeconds < UiCooldownSeconds) // If cooldown has not elapsed, skip
            { // Починаємо блок if
                return; // Виходимо, щоб не спамити
            } // Завершуємо блок if

            _lastUiTickSeconds = nowSeconds; // Update last UI notify time

            string name = settlement != null ? settlement.Name.ToString() : "unknown"; // Отримуємо назву поселення для логу (або unknown якщо null)
            ModLogger.Info("Заблоковано вхід у поселення/меню для клієнта: " + name + "."); // Логуємо блокування для дебагу

            UiFeedback.ShowMessageDeferred("Spectator: entering settlements is disabled."); // Defer UI message to avoid being lost during transitions
        } // Завершуємо блок методу
    } // Завершуємо блок класу
} // Завершуємо блок простору імен

