using System; // Підключаємо базові типи .NET (Exception)
using CoopSpectator.Infrastructure; // Підключаємо логер для діагностики
using CoopSpectator.Network; // Підключаємо NetworkRole/NetworkManager через CoopRuntime
using CoopSpectator.Network.Messages; // Підключаємо DTO та кодек для STATE:{json}
using TaleWorlds.CampaignSystem; // Підключаємо CampaignBehaviorBase, MobileParty, CampaignEvents
using TaleWorlds.CampaignSystem.Party; // Підключаємо MobileParty (в Bannerlord API він знаходиться в цьому namespace)
using TaleWorlds.MountAndBlade; // Підключаємо Mission для визначення чи йде битва

namespace CoopSpectator.Campaign // Оголошуємо простір імен для campaign-логіки
{ // Починаємо блок простору імен
    /// <summary> // Документуємо клас
    /// Behavior хоста: періодично формує та відправляє клієнтам стан кампанії (STATE:{json}). // Пояснюємо призначення
    /// </summary> // Завершуємо XML-коментар
    public sealed class HostStateBroadcaster : CampaignBehaviorBase // Наслідуємо CampaignBehaviorBase, щоб підписуватись на події кампанії
    { // Починаємо блок класу
        private const float BroadcastIntervalSeconds = 2.0f; // Інтервал між повідомленнями стану (2 секунди, щоб не спамити мережею)
        private float _timeUntilNextBroadcast; // Таймер (секунди) до наступної відправки STATE (накопичується в OnTick)

        public override void RegisterEvents() // Реєструємо події кампанії
        { // Починаємо блок методу
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick); // Підписуємось на частий tick кампанії, щоб мати таймер у секундах
        } // Завершуємо блок методу

        public override void SyncData(IDataStore dataStore) // Синхронізація/збереження даних (для нашого моду не потрібна)
        { // Починаємо блок методу
        } // Завершуємо блок методу

        private void OnTick(float dt) // Викликається регулярно під час кампанії, dt — час (секунди) між тиками
        { // Починаємо блок методу
            if (!ShouldBroadcast()) // Перевіряємо чи ми в режимі хоста/сервера і чи є підключення
            { // Починаємо блок if
                return; // Виходимо, бо у spectator mode клієнт не має нічого відправляти
            } // Завершуємо блок if

            _timeUntilNextBroadcast -= dt; // Зменшуємо таймер на пройдений час, щоб відраховувати 2 секунди

            if (_timeUntilNextBroadcast > 0f) // Якщо ще не настав час відправки, нічого не робимо
            { // Починаємо блок if
                return; // Виходимо до наступного тіку
            } // Завершуємо блок if

            _timeUntilNextBroadcast = BroadcastIntervalSeconds; // Скидаємо таймер на інтервал, щоб наступна відправка була через 2 секунди

            try // Обгортаємо збір стану та відправку, щоб помилка не крашнула гру
            { // Починаємо блок try
                HostGameState state = BuildHostGameState(); // Формуємо мінімальний стан хоста для клієнтів
                string message = HostGameStateCodec.BuildStateMessage(state); // Серіалізуємо стан у повідомлення протоколу STATE:{json}
                CoopRuntime.Network.BroadcastToClients(message); // Відправляємо повідомлення всім підключеним TCP клієнтам
            } // Завершуємо блок try
            catch (Exception ex) // Ловимо будь-які винятки (null, API зміни, тощо)
            { // Починаємо блок catch
                ModLogger.Error("Помилка під час Broadcast STATE.", ex); // Логуємо помилку для подальшого дебагу
            } // Завершуємо блок catch
        } // Завершуємо блок методу

        private static bool ShouldBroadcast() // Перевіряємо чи поточний екземпляр гри має право відправляти стан (тільки хост)
        { // Починаємо блок методу
            if (CoopRuntime.Network == null) // Перевіряємо що NetworkManager ініціалізований
            { // Починаємо блок if
                return false; // Повертаємо false, бо без NetworkManager немає куди відправляти
            } // Завершуємо блок if

            if (!CoopRuntime.Network.IsRunning) // Перевіряємо що мережа реально запущена (сервер/клієнт активний)
            { // Починаємо блок if
                return false; // Повертаємо false, бо поки сервер не запущено, немає сенсу відправляти
            } // Завершуємо блок if

            if (CoopRuntime.Network.Role != NetworkRole.Server) // Перевіряємо що ми саме сервер (хост), а не клієнт
            { // Починаємо блок if
                return false; // Повертаємо false, бо клієнти не мають бути авторитетними
            } // Завершуємо блок if

            return true; // Повертаємо true, бо ми хост і можемо відправляти STATE
        } // Завершуємо блок методу

        private static HostGameState BuildHostGameState() // Формуємо модель стану хоста з API кампанії
        { // Починаємо блок методу
            HostGameState state = new HostGameState(); // Створюємо новий DTO стану для серіалізації

            state.Position = new HostPosition2D(); // Створюємо DTO позиції, щоб не тягнути Vec2 у мережевий протокол
            var pos = MobileParty.MainParty.GetPosition2D; // Отримуємо позицію партії хоста у вигляді Vec2 (в Bannerlord 1.3.14 це публічна властивість)
            state.Position.X = pos.x; // Записуємо X координату партії хоста на мапі (у Vec2 поля називаються x/y)
            state.Position.Y = pos.y; // Записуємо Y координату партії хоста на мапі (у Vec2 поля називаються x/y)

            state.ArmySize = MobileParty.MainParty.MemberRoster.TotalManCount; // Записуємо розмір армії (корисно для відображення та тестів)
            state.InBattle = Mission.Current != null; // Визначаємо чи зараз є активна місія (битва) у хоста
            state.CurrentAction = GetCurrentActionText(); // Визначаємо короткий текст стану (TRAVELING/IDLE/IN_SETTLEMENT/IN_BATTLE)

            state.TimeOfDay = 0f; // Тимчасово ставимо 0, бо час доби не критичний для MVP і залежить від точного API CampaignTime

            return state; // Повертаємо сформований DTO для серіалізації
        } // Завершуємо блок методу

        private static string GetCurrentActionText() // Визначаємо просту "дію" хоста для клієнтського UI
        { // Починаємо блок методу
            if (Mission.Current != null) // Якщо є активна місія, ми в битві
            { // Починаємо блок if
                return "IN_BATTLE"; // Повертаємо статус "в битві"
            } // Завершуємо блок if

            if (MobileParty.MainParty.CurrentSettlement != null) // Якщо партія всередині поселення, ми у місті/замку/селі
            { // Починаємо блок if
                return "IN_SETTLEMENT"; // Повертаємо статус "в поселення"
            } // Завершуємо блок if

            if (MobileParty.MainParty.IsMoving) // Якщо партія рухається, ми подорожуємо
            { // Починаємо блок if
                return "TRAVELING"; // Повертаємо статус "рух"
            } // Завершуємо блок if

            return "IDLE"; // Якщо жодна умова не спрацювала, вважаємо що ми стоїмо/очікуємо
        } // Завершуємо блок методу
    } // Завершуємо блок класу
} // Завершуємо блок простору імен

