namespace CoopSpectator.Network.Messages // Оголошуємо простір імен для мережевих моделей
{ // Починаємо блок простору імен
    /// <summary> // Документуємо клас
    /// Стисла модель стану хоста на карті кампанії, яку ми будемо серіалізувати в JSON і відправляти клієнтам. // Пояснюємо призначення
    /// </summary> // Завершуємо XML-коментар
    public sealed class HostGameState // Оголошуємо sealed, бо це проста DTO-модель і не потребує наслідування
    { // Починаємо блок класу
        public HostPosition2D Position { get; set; } // Зберігаємо позицію партії хоста на 2D мапі кампанії
        public string CurrentAction { get; set; } // Зберігаємо поточну "дію" хоста (наприклад TRAVELING/IDLE/IN_SETTLEMENT/IN_BATTLE)
        public int ArmySize { get; set; } // Зберігаємо розмір армії (для простого відображення/тесту)
        public float TimeOfDay { get; set; } // Зберігаємо час доби (опціонально; може бути корисно для UI)
        public bool InBattle { get; set; } // Зберігаємо чи хост зараз у битві (для переходу до battle integration)
    } // Завершуємо блок класу

    /// <summary> // Документуємо клас
    /// Проста 2D позиція (X/Y), щоб не тягнути ігрові типи Vec2 у мережеві DTO. // Пояснюємо навіщо окремий тип
    /// </summary> // Завершуємо XML-коментар
    public sealed class HostPosition2D // Оголошуємо sealed DTO для координат
    { // Починаємо блок класу
        public float X { get; set; } // Зберігаємо координату X на мапі
        public float Y { get; set; } // Зберігаємо координату Y на мапі
    } // Завершуємо блок класу
} // Завершуємо блок простору імен

