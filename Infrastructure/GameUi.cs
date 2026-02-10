using System; // Підключаємо базові типи .NET (Exception)
using TaleWorlds.Core; // Підключаємо InformationMessage для UI повідомлень
using TaleWorlds.Library; // Підключаємо InformationManager для показу повідомлень у грі

namespace CoopSpectator.Infrastructure // Оголошуємо простір імен для інфраструктури
{ // Починаємо блок простору імен
    /// <summary> // Документуємо клас
    /// Спільні UI-хелпери для показу повідомлень в Bannerlord (в одному місці, щоб не дублювати try/catch). // Пояснюємо навіщо клас
    /// </summary> // Завершуємо XML-коментар
    public static class GameUi // Оголошуємо статичний клас, бо це набір утиліт без стану
    { // Починаємо блок класу
        public static void ShowMessage(string text) // Показуємо текстове повідомлення гравцю
        { // Починаємо блок методу
            if (string.IsNullOrEmpty(text)) // Перевіряємо що текст не порожній, щоб не показувати "порожні" повідомлення
            { // Починаємо блок if
                return; // Виходимо, бо нічого показувати
            } // Завершуємо блок if

            try // Обгортаємо виклик UI, бо в деякі моменти InformationManager може бути недоступний
            { // Починаємо блок try
                InformationManager.DisplayMessage(new InformationMessage(text)); // Показуємо повідомлення в лівому нижньому куті
            } // Завершуємо блок try
            catch (Exception ex) // Ловимо винятки, щоб мод не міг крашнути гру через UI
            { // Починаємо блок catch
                ModLogger.Error("Не вдалося показати InformationMessage.", ex); // Логуємо помилку для діагностики
            } // Завершуємо блок catch
        } // Завершуємо блок методу
    } // Завершуємо блок класу
} // Завершуємо блок простору імен

