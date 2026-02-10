using System; // Підключаємо базові типи .NET (Exception)
using CoopSpectator.Infrastructure; // Підключаємо логер для діагностики
using CoopSpectator.Network; // Підключаємо NetworkRole для перевірки ролі (клієнт/сервер)
using CoopSpectator.Network.Messages; // Підключаємо кодек і DTO HostGameState
using TaleWorlds.CampaignSystem; // Підключаємо CampaignBehaviorBase та CampaignEvents для тіку в кампанії

namespace CoopSpectator.Campaign // Оголошуємо простір імен для campaign-логіки
{ // Починаємо блок простору імен
    /// <summary> // Документуємо клас
    /// Behavior клієнта: приймає `STATE:{json}` від хоста, зберігає останній стан і періодично відображає його в UI. // Пояснюємо призначення
    /// </summary> // Завершуємо XML-коментар
    public sealed class SpectatorStateReceiver : CampaignBehaviorBase // Наслідуємо CampaignBehaviorBase, щоб жити в контексті кампанії
    { // Починаємо блок класу
        private const float UiUpdateIntervalSeconds = 2.0f; // Інтервал оновлення UI (2 секунди), щоб не спамити повідомленнями
        private float _timeUntilNextUiUpdate; // Таймер (секунди) до наступного показу стану в UI

        private HostGameState _lastState; // Зберігаємо останній отриманий стан хоста
        private bool _hasState; // Прапорець, що ми вже отримали хоча б один STATE і можемо показувати дані

        public override void RegisterEvents() // Реєструємо події кампанії
        { // Починаємо блок методу
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick); // Підписуємось на tick кампанії, щоб мати dt і таймер UI
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched); // Підписуємось на старт сесії, щоб підключити мережеві хендлери коли кампанія готова
        } // Завершуємо блок методу

        public override void SyncData(IDataStore dataStore) // Синхронізація/збереження даних (для нашого моду не потрібна)
        { // Починаємо блок методу
        } // Завершуємо блок методу

        private void OnSessionLaunched(CampaignGameStarter campaignGameStarter) // Викликається коли кампанія стартувала і session запущена
        { // Починаємо блок методу
            SubscribeToNetwork(); // Підписуємось на мережеві повідомлення, коли кампанія готова
        } // Завершуємо блок методу

        private void OnTick(float dt) // Tick кампанії, dt — час (секунди) між тиками
        { // Починаємо блок методу
            if (!ShouldReceive()) // Якщо ми не клієнт або мережа не запущена, нічого не робимо
            { // Починаємо блок if
                return; // Виходимо, бо немає сенсу показувати spectator-стан
            } // Завершуємо блок if

            if (!_hasState) // Якщо ми ще не отримали жодного STATE, поки не показуємо UI
            { // Починаємо блок if
                return; // Виходимо до наступного тіку
            } // Завершуємо блок if

            _timeUntilNextUiUpdate -= dt; // Зменшуємо таймер до оновлення UI

            if (_timeUntilNextUiUpdate > 0f) // Якщо ще не час оновлювати UI, нічого не робимо
            { // Починаємо блок if
                return; // Виходимо до наступного тіку
            } // Завершуємо блок if

            _timeUntilNextUiUpdate = UiUpdateIntervalSeconds; // Скидаємо таймер, щоб наступне оновлення було через 2 секунди

            try // Захищаємо UI-логіку, щоб вона не крашила гру
            { // Починаємо блок try
                string text = BuildUiText(_lastState); // Формуємо короткий рядок для відображення в UI
                GameUi.ShowMessage(text); // Показуємо повідомлення через спільний helper, щоб не дублювати UI-код
            } // Завершуємо блок try
            catch (Exception ex) // Ловимо будь-які винятки, щоб мод не ламав кампанію
            { // Починаємо блок catch
                ModLogger.Error("Помилка під час відображення spectator STATE.", ex); // Логуємо помилку
            } // Завершуємо блок catch
        } // Завершуємо блок методу

        private void SubscribeToNetwork() // Підписуємось на MessageReceived, щоб отримувати STATE від TCP
        { // Починаємо блок методу
            if (CoopRuntime.Network == null) // Перевіряємо що NetworkManager існує
            { // Починаємо блок if
                return; // Виходимо, бо підписатися неможливо
            } // Завершуємо блок if

            CoopRuntime.Network.MessageReceived -= OnNetworkMessageReceived; // Спершу відписуємось, щоб не отримати дублікати при повторних викликах
            CoopRuntime.Network.MessageReceived += OnNetworkMessageReceived; // Підписуємось на подію мережі (вона вже викликається в головному потоці)
        } // Завершуємо блок методу

        private void OnNetworkMessageReceived(string message) // Callback мережі, який викликається в головному потоці
        { // Починаємо блок методу
            if (!ShouldReceive()) // Перевіряємо роль/стан мережі, щоб не обробляти зайве
            { // Починаємо блок if
                return; // Виходимо, бо ми не клієнт або мережа вимкнена
            } // Завершуємо блок if

            HostGameState state; // Оголошуємо змінну для результату парсингу STATE

            if (!HostGameStateCodec.TryParseStateMessage(message, out state)) // Пробуємо розпізнати STATE:{json} і десеріалізувати
            { // Починаємо блок if
                return; // Виходимо, бо це не STATE повідомлення або JSON некоректний
            } // Завершуємо блок if

            _lastState = state; // Зберігаємо останній стан, щоб UI показував актуальне значення
            _hasState = true; // Встановлюємо прапорець, що дані вже є
        } // Завершуємо блок методу

        private static bool ShouldReceive() // Перевіряємо чи поточний екземпляр гри має право приймати STATE (тільки клієнт)
        { // Починаємо блок методу
            if (CoopRuntime.Network == null) // Перевіряємо що NetworkManager існує
            { // Починаємо блок if
                return false; // Повертаємо false, бо без мережі нічого приймати
            } // Завершуємо блок if

            if (!CoopRuntime.Network.IsRunning) // Перевіряємо що мережа запущена
            { // Починаємо блок if
                return false; // Повертаємо false, бо без активного підключення не буде повідомлень
            } // Завершуємо блок if

            if (CoopRuntime.Network.Role != NetworkRole.Client) // Перевіряємо що ми клієнт, а не сервер
            { // Починаємо блок if
                return false; // Повертаємо false, бо сервер не має показувати spectator-стан сам собі
            } // Завершуємо блок if

            return true; // Повертаємо true, бо ми клієнт і можемо приймати STATE
        } // Завершуємо блок методу

        private static string BuildUiText(HostGameState state) // Формуємо короткий текст для UI з останнього стану
        { // Починаємо блок методу
            if (state == null) // Перевіряємо null, щоб не зловити NullReferenceException
            { // Починаємо блок if
                return "HOST: (немає даних)"; // Повертаємо fallback-текст
            } // Завершуємо блок if

            float x = state.Position != null ? state.Position.X : 0f; // Беремо X координату або 0 якщо Position null
            float y = state.Position != null ? state.Position.Y : 0f; // Беремо Y координату або 0 якщо Position null
            string action = string.IsNullOrEmpty(state.CurrentAction) ? "?" : state.CurrentAction; // Беремо дію або "?" якщо значення відсутнє
            string battle = state.InBattle ? "YES" : "NO"; // Перетворюємо bool в короткий текст для UI

            return "HOST: pos=(" + x.ToString("0.00") + "," + y.ToString("0.00") + "), action=" + action + ", army=" + state.ArmySize + ", inBattle=" + battle; // Повертаємо компактний рядок статусу
        } // Завершуємо блок методу
    } // Завершуємо блок класу
} // Завершуємо блок простору імен

