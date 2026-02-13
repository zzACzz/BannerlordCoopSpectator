namespace CoopSpectator.Infrastructure // Тримаємо спільні "прапорці стану" в Infrastructure, щоб ними могли користуватись і behaviors, і Harmony-патчі
{ // Починаємо блок простору імен
    /// <summary> // Документуємо клас
    /// Глобальний стан для клієнта: "є активне запрошення/вхід у бій" (battle pending). // Пояснюємо, навіщо потрібен прапорець
    /// Використовується, щоб тимчасово сильніше блокувати ввід/меню під час countdown. // Пояснюємо сценарій
    /// </summary> // Завершуємо XML-коментар
    public static class ClientBattleInvitationLock // Статичний клас, бо це простий глобальний прапорець
    { // Починаємо блок класу
        public static bool IsActive { get; private set; } // Публічно даємо лише читання; зміну робимо через Activate/Deactivate

        public static void Activate() // Вмикаємо lock (коли отримали BATTLE_START)
        { // Починаємо блок методу
            IsActive = true; // Ставимо прапорець в true
        } // Завершуємо блок методу

        public static void Deactivate() // Вимикаємо lock (коли countdown завершився або коли ми скидаємо стан)
        { // Починаємо блок методу
            IsActive = false; // Ставимо прапорець в false
        } // Завершуємо блок методу
    } // Завершуємо блок класу
} // Завершуємо блок простору імен

