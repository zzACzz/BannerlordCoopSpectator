using System; // Підключаємо базові типи .NET (Math, Exception)
using CoopSpectator.Infrastructure; // Підключаємо UiFeedback + ClientBattleInvitationLock
using CoopSpectator.Network; // Підключаємо NetworkRole для перевірки ролі
using CoopSpectator.Network.Messages; // Підключаємо DTO + кодек для BATTLE_START
using TaleWorlds.CampaignSystem; // Підключаємо CampaignBehaviorBase + CampaignEvents

namespace CoopSpectator.Campaign // Campaign behaviors тут
{ // Begin namespace
    /// <summary> // Документуємо клас
    /// Клієнтський behavior: приймає `BATTLE_START:{json}`, // Пояснюємо, що саме слухаємо
    /// показує нотифікацію + короткий countdown, // Пояснюємо UI поведінку
    /// і тимчасово блокує campaign input сильніше (через ClientBattleInvitationLock). // Пояснюємо блокування вводу
    /// </summary> // End summary
    public sealed class ClientBattleNotification : CampaignBehaviorBase // Наслідуємо CampaignBehaviorBase для роботи в кампанії
    { // Begin class
        private const float CountdownSeconds = 5.0f; // Скільки секунд показуємо countdown після отримання BATTLE_START
        private const float UiUpdateStepSeconds = 1.0f; // Крок оновлення UI (раз на секунду)

        private bool _isActive; // Чи зараз активний countdown/notification
        private float _secondsRemaining; // Скільки секунд залишилось до завершення countdown
        private float _timeUntilNextUiUpdate; // Таймер до наступного показу повідомлення (щоб не показувати щотік)
        private int _lastShownWholeSeconds; // Останнє ціле значення секунд, яке ми вже показали (щоб не дублювати)

        private BattleStartMessage _lastBattleStart; // Зберігаємо останнє отримане BATTLE_START (для можливих майбутніх етапів)

        public override void RegisterEvents() // Реєструємо події кампанії
        { // Begin method
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick); // Tick для countdown таймера
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched); // Коли кампанія готова — підписуємось на мережу
        } // End method

        public override void SyncData(IDataStore dataStore) // Persist не потрібен для цього behavior
        { // Begin method
        } // End method

        private void OnSessionLaunched(CampaignGameStarter starter) // Викликається коли session запущена
        { // Begin method
            SubscribeToNetwork(); // Підписуємось на Network.MessageReceived
        } // End method

        private void SubscribeToNetwork() // Підписуємось на мережеві повідомлення
        { // Begin method
            if (CoopRuntime.Network == null) // Якщо NetworkManager відсутній — нічого не робимо
            { // Begin if
                return; // Вихід
            } // End if

            CoopRuntime.Network.MessageReceived -= OnNetworkMessageReceived; // Прибираємо можливий попередній підпис, щоб не було дублікатів
            CoopRuntime.Network.MessageReceived += OnNetworkMessageReceived; // Додаємо наш handler (викликається в головному потоці)
        } // End method

        private void OnNetworkMessageReceived(string message) // Обробляємо вхідні мережеві повідомлення
        { // Begin method
            if (!ShouldReceive()) // Якщо ми не клієнт або мережа не активна — ігноруємо
            { // Begin if
                return; // Вихід
            } // End if

            BattleStartMessage battleStart; // Оголошуємо змінну для DTO

            if (!BattleStartMessageCodec.TryParseBattleStartMessage(message, out battleStart)) // Пробуємо розпізнати BATTLE_START і розпарсити JSON
            { // Begin if
                return; // Це не BATTLE_START — ігноруємо
            } // End if

            _lastBattleStart = battleStart; // Зберігаємо останнє battle start повідомлення
            StartCountdown(); // Запускаємо countdown + тимчасове блокування вводу
        } // End method

        private static bool ShouldReceive() // Перевіряємо чи цей інстанс має право обробляти BATTLE_START
        { // Begin method
            if (CoopRuntime.Network == null) // Якщо NetworkManager не створений
            { // Begin if
                return false; // Нема куди/звідки отримувати
            } // End if

            if (!CoopRuntime.Network.IsRunning) // Якщо мережа не запущена
            { // Begin if
                return false; // Нічого не приймаємо
            } // End if

            if (CoopRuntime.Network.Role != NetworkRole.Client) // Якщо ми не клієнт
            { // Begin if
                return false; // Сервер не показує client notification
            } // End if

            return true; // Ми клієнт і можемо приймати BATTLE_START
        } // End method

        private void StartCountdown() // Стартуємо countdown і lock вводу
        { // Begin method
            _isActive = true; // Вмикаємо режим countdown
            _secondsRemaining = CountdownSeconds; // Ставимо початкову тривалість countdown
            _timeUntilNextUiUpdate = 0f; // Дозволяємо показати перше повідомлення одразу
            _lastShownWholeSeconds = -1; // Скидаємо “останнє показане значення”, щоб перший показ точно відбувся

            ClientBattleInvitationLock.Activate(); // Вмикаємо глобальний lock, щоб Harmony-патчі могли жорсткіше блокувати ввід/меню

            // Показуємо перше повідомлення з коротким summary. // Пояснюємо навіщо
            string summary = BuildSummaryText(_lastBattleStart); // Формуємо текст
            UiFeedback.ShowMessageDeferred(summary); // Показуємо на наступних тіках, щоб не загубилось під час UI transition

            ModLogger.Info("Client received BATTLE_START. Countdown started."); // Логуємо для дебагу
        } // End method

        private void OnTick(float dt) // Tick кампанії
        { // Begin method
            if (!_isActive) // Якщо countdown не активний — нічого не робимо
            { // Begin if
                return; // Вихід
            } // End if

            if (!ShouldReceive()) // Якщо клієнт відключився/змінив роль — скидаємо стан
            { // Begin if
                StopCountdown(); // Вимикаємо countdown і lock
                return; // Вихід
            } // End if

            // Оновлюємо час. // Пояснюємо крок
            _secondsRemaining -= dt; // Віднімаємо dt
            _timeUntilNextUiUpdate -= dt; // Віднімаємо dt для UI таймера

            if (_secondsRemaining <= 0f) // Якщо countdown завершився
            { // Begin if
                StopCountdown(); // Вимикаємо lock і показуємо фінальне повідомлення
                return; // Вихід
            } // End if

            if (_timeUntilNextUiUpdate > 0f) // Якщо ще не час показувати наступний UI-рядок
            { // Begin if
                return; // Вихід
            } // End if

            _timeUntilNextUiUpdate = UiUpdateStepSeconds; // Ставимо наступний показ через 1 секунду

            int wholeSecondsLeft = (int)Math.Ceiling(_secondsRemaining); // Округлюємо вгору, щоб показувати 5..4..3..2..1

            if (wholeSecondsLeft == _lastShownWholeSeconds) // Якщо це значення вже показували — не дублюємо
            { // Begin if
                return; // Вихід
            } // End if

            _lastShownWholeSeconds = wholeSecondsLeft; // Запам’ятовуємо, що ми це вже показали

            UiFeedback.ShowMessageDeferred("Battle invitation: starting in " + wholeSecondsLeft + "s..."); // Показуємо countdown повідомлення
        } // End method

        private void StopCountdown() // Зупиняємо countdown і знімаємо lock
        { // Begin method
            _isActive = false; // Вимикаємо режим countdown
            _secondsRemaining = 0f; // Скидаємо залишок
            _timeUntilNextUiUpdate = 0f; // Скидаємо таймер

            ClientBattleInvitationLock.Deactivate(); // Знімаємо глобальний lock (щоб не ламати UX надовго без BATTLE_END)

            UiFeedback.ShowMessageDeferred("Battle on host has started. (Next: mission join)"); // Фінальне повідомлення після countdown
            ModLogger.Info("Client battle countdown finished. Lock released."); // Логуємо для дебагу
        } // End method

        private static string BuildSummaryText(BattleStartMessage message) // Робимо короткий summary для першого повідомлення
        { // Begin method
            if (message == null) // Захист від null
            { // Begin if
                return "Battle invitation received. Starting soon..."; // Fallback
            } // End if

            string side = string.IsNullOrEmpty(message.PlayerSide) ? "Unknown" : message.PlayerSide; // Нормалізуємо сторону
            string scene = string.IsNullOrEmpty(message.MapScene) ? "unknown" : message.MapScene; // Нормалізуємо сцену
            int troopStacks = message.Troops != null ? message.Troops.Count : 0; // Кількість стеків у списку

            return "Battle invitation received: side=" + side + ", scene=" + scene + ", stacks=" + troopStacks + ". Starting in " + ((int)CountdownSeconds) + "s..."; // Повертаємо компактний рядок
        } // End method
    } // End class
} // End namespace

