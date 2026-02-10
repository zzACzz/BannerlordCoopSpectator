using System; // Підключаємо базові типи .NET (Exception)
using CoopSpectator.Infrastructure; // Підключаємо інфраструктуру (логер, dispatcher)
using TaleWorlds.Core; // Підключаємо базові типи Bannerlord (InformationMessage)
using TaleWorlds.Library; // Підключаємо утиліти Bannerlord (InformationManager)
using TaleWorlds.MountAndBlade; // Підключаємо базовий API модів (MBSubModuleBase)

namespace CoopSpectator // Використовуємо кореневий namespace моду (має співпасти з SubModule.xml)
{ // Починаємо блок простору імен
    /// <summary> // Документуємо клас
    /// Точка входу моду для Bannerlord (завантажується через SubModule.xml). // Пояснюємо призначення
    /// </summary> // Завершуємо XML-коментар
    public sealed class SubModule : MBSubModuleBase // Наслідуємо MBSubModuleBase, щоб отримати callbacks від гри
    { // Починаємо блок класу
        private bool _hasShownLoadedMessage; // Зберігаємо прапорець, щоб показати "mod loaded" лише один раз (і не спамити при перезапусках гри/сесій)

        protected override void OnSubModuleLoad() // Викликається коли мод завантажується (до старту гри/кампанії)
        { // Починаємо блок методу
            base.OnSubModuleLoad(); // Викликаємо базову реалізацію, щоб не ламати внутрішню логіку гри

            CoopRuntime.Initialize(); // Ініціалізуємо глобальний runtime (NetworkManager, тощо)

            if (CoopRuntime.Network != null) // Перевіряємо що NetworkManager створився успішно
            { // Починаємо блок if
                CoopRuntime.Network.MessageReceived += OnNetworkMessageReceived; // Підписуємось на події мережі, щоб бачити тестові повідомлення
            } // Завершуємо блок if

            _hasShownLoadedMessage = false; // Скидаємо прапорець, бо OnSubModuleLoad викликається до старту гри, а UI може бути ще не готовий
            ModLogger.Info("SubModule завантажено."); // Логуємо завантаження для дебагу
        } // Завершуємо блок методу

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject) // Викликається коли стартує гра/кампанія (UI вже зазвичай ініціалізований)
        { // Починаємо блок методу
            base.OnGameStart(game, gameStarterObject); // Викликаємо базову реалізацію, щоб не ламати ініціалізацію гри

            if (_hasShownLoadedMessage) // Перевіряємо прапорець, щоб не показувати повідомлення повторно
            { // Починаємо блок if
                return; // Виходимо, бо повідомлення вже було показано
            } // Завершуємо блок if

            _hasShownLoadedMessage = true; // Встановлюємо прапорець, щоб повторний OnGameStart не спамив повідомленнями
            ShowMessage("CoopSpectator mod loaded! (v0.1.0)"); // Показуємо коротке повідомлення в UI, коли гра вже стартувала і UI готовий
        } // Завершуємо блок методу

        protected override void OnSubModuleUnloaded() // Викликається коли мод вивантажується (закриття гри / перезавантаження модів)
        { // Починаємо блок методу
            try // Обгортаємо cleanup в try-catch, щоб не крашнути гру при вивантаженні
            { // Починаємо блок try
                if (CoopRuntime.Network != null) // Перевіряємо що NetworkManager існує
                { // Починаємо блок if
                    CoopRuntime.Network.MessageReceived -= OnNetworkMessageReceived; // Відписуємось від подій, щоб уникнути memory leak
                } // Завершуємо блок if

                CoopRuntime.Shutdown(); // Коректно зупиняємо мережу і звільняємо ресурси
            } // Завершуємо блок try
            catch (Exception ex) // Ловимо будь-які винятки, щоб unload не падав
            { // Починаємо блок catch
                ModLogger.Error("Помилка під час OnSubModuleUnloaded.", ex); // Логуємо помилку для дебагу
            } // Завершуємо блок catch
            finally // Гарантуємо виклик базового методу
            { // Починаємо блок finally
                base.OnSubModuleUnloaded(); // Викликаємо базову реалізацію
            } // Завершуємо блок finally
        } // Завершуємо блок методу

        protected override void OnApplicationTick(float dt) // Викликається кожен кадр на рівні додатку (коли гра запущена)
        { // Починаємо блок методу
            base.OnApplicationTick(dt); // Викликаємо базову реалізацію

            MainThreadDispatcher.ExecutePending(); // Виконуємо дії, які були поставлені в чергу з мережевого потоку
        } // Завершуємо блок методу

        private void OnNetworkMessageReceived(string message) // Обробляємо мережеві повідомлення (викликається в головному потоці)
        { // Починаємо блок методу
            if (string.IsNullOrEmpty(message)) // Перевіряємо що повідомлення не порожнє
            { // Починаємо блок if
                return; // Виходимо, бо нічого показувати
            } // Завершуємо блок if

            ShowMessage("NET: " + message); // Показуємо повідомлення в UI для швидкого тесту networking
            ModLogger.Info("Отримано мережеве повідомлення: " + message); // Логуємо повідомлення в лог гри
        } // Завершуємо блок методу

        private static void ShowMessage(string text) // Оголошуємо helper для показу повідомлень в UI Bannerlord
        { // Починаємо блок методу
            try // Захищаємо UI виклик, щоб не крашнути якщо InformationManager недоступний у певний момент
            { // Починаємо блок try
                InformationManager.DisplayMessage(new InformationMessage(text)); // Показуємо повідомлення гравцю в лівому нижньому куті
            } // Завершуємо блок try
            catch (Exception ex) // Ловимо виняток як fallback
            { // Починаємо блок catch
                ModLogger.Error("Не вдалося показати InformationMessage.", ex); // Логуємо помилку, щоб знати що саме сталося
            } // Завершуємо блок catch
        } // Завершуємо блок методу
    } // Завершуємо блок класу
} // Завершуємо блок простору імен

