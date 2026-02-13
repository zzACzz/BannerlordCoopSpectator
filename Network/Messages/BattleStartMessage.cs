using System.Collections.Generic; // Підключаємо List<> для списків у DTO

namespace CoopSpectator.Network.Messages // Оголошуємо простір імен для мережевих моделей
{ // Починаємо блок простору імен
    /// <summary> // Документуємо клас
    /// DTO для повідомлення `BATTLE_START:{json}` — мінімальні дані, // Пояснюємо призначення
    /// щоб клієнт міг показати запрошення в бій і підготувати вибір військ. // Додаємо контекст майбутніх етапів
    /// </summary> // Завершуємо XML-коментар
    public sealed class BattleStartMessage // Оголошуємо sealed DTO-клас без наслідування
    { // Починаємо блок класу
        public string MapScene { get; set; } // Назва сцени/карти (поки що: з кампанії; потім може бути сцена битви)
        public float MapX { get; set; } // X координата місця (на мапі кампанії) де стартує бій
        public float MapY { get; set; } // Y координата місця (на мапі кампанії) де стартує бій
        public string PlayerSide { get; set; } // Сторона гравця ("Attacker"/"Defender"/"Unknown") як текст
        public int ArmySize { get; set; } // Загальна кількість бійців у партії хоста на момент старту
        public List<TroopStackInfo> Troops { get; set; } // Список стеків військ (тип юнита + кількість)
    } // Завершуємо блок класу

    /// <summary> // Документуємо клас
    /// Інформація про один "стек" військ одного типу (агрегація замість списку з 1000 елементів). // Пояснюємо чому так
    /// </summary> // Завершуємо XML-коментар
    public sealed class TroopStackInfo // Оголошуємо sealed DTO-клас для одного стеку військ
    { // Починаємо блок класу
        public string CharacterId { get; set; } // Унікальний StringId персонажа/юнита (для майбутнього спавну)
        public string TroopName { get; set; } // Людська назва юнита (для UI клієнта)
        public int Tier { get; set; } // Тір (рівень) юнита (для фільтрації/балансу)
        public bool IsMounted { get; set; } // Чи юнит верховий (на коні)
        public bool IsHero { get; set; } // Чи це герой (унікальний персонаж; важливо для правил відбору)
        public int Count { get; set; } // Скільки таких юнитів у партії
        public int WoundedCount { get; set; } // Скільки з них поранені (на майбутнє: щоб не пропонувати wounded)
    } // Завершуємо блок класу
} // Завершуємо блок простору імен

