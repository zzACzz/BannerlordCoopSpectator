using System; // Підключаємо базові типи .NET (Exception)
using System.Collections.Generic; // Підключаємо List<> для списків у DTO
using CoopSpectator.Infrastructure; // Підключаємо логер і UI feedback
using CoopSpectator.Network; // Підключаємо NetworkRole для перевірки ролі
using CoopSpectator.Network.Messages; // Підключаємо DTO + кодек для BATTLE_START:{json}
using TaleWorlds.CampaignSystem; // Підключаємо Campaign (для доступу до поточної кампанії)
using TaleWorlds.CampaignSystem.Party; // Підключаємо MobileParty (партія гравця в кампанії)
using TaleWorlds.MountAndBlade; // Підключаємо Mission (детекція входу в місію/битву)

namespace CoopSpectator.Campaign // Тримаємо battle/campaign логіку в одному namespace
{ // Починаємо блок простору імен
    /// <summary> // Документуємо клас
    /// Детектор старту битви/місії у хоста: // Пояснюємо, що ми детектимо
    /// коли `Mission.Current` переходить з null → не-null, // Описуємо основний сигнал
    /// ми відправляємо клієнтам `BATTLE_START:{json}`. // Описуємо результат
    /// </summary> // Завершуємо XML-коментар
    public sealed class BattleDetector // Оголошуємо sealed сервіс-клас (стан + простий tick)
    { // Починаємо блок класу
        private bool _wasInMissionLastTick; // Пам'ятаємо стан попереднього тіку: чи вже була активна місія
        private bool _hasSentBattleStartForThisMission; // Прапорець, щоб не відправляти BATTLE_START багато разів за одну місію

        public void Tick() // Метод, який треба викликати кожен кадр з головного потоку (наприклад з SubModule.OnApplicationTick)
        { // Починаємо блок методу
            if (!ShouldSendBattleStart()) // Перевіряємо умови: ми маємо бути хостом (Server) і мережа має бути запущена
            { // Починаємо блок if
                ResetIfMissionEnded(); // Навіть якщо ми не сервер — тримаємо внутрішній стан консистентним
                return; // Виходимо, бо клієнт не має розсилати battle повідомлення
            } // Завершуємо блок if

            bool isInMissionNow = Mission.Current != null; // Визначаємо чи зараз є активна місія (битва/сцена)

            if (!isInMissionNow) // Якщо місії немає, значить ми не в битві (або вже вийшли з неї)
            { // Починаємо блок if
                _wasInMissionLastTick = false; // Оновлюємо стан "було в місії" на false
                _hasSentBattleStartForThisMission = false; // Скидаємо прапорець, щоб наступна місія могла знову надіслати BATTLE_START
                return; // Виходимо, бо поки що нема старту битви для відправки
            } // Завершуємо блок if

            if (_wasInMissionLastTick) // Якщо ми вже були в місії на попередньому тіку, значить "старт" вже минув
            { // Починаємо блок if
                return; // Виходимо, щоб не відправляти повторно
            } // Завершуємо блок if

            _wasInMissionLastTick = true; // Фіксуємо, що ми щойно увійшли в місію
            TrySendBattleStart(); // Пробуємо сформувати DTO і відправити клієнтам
        } // Завершуємо блок методу

        private static bool ShouldSendBattleStart() // Перевіряємо чи поточний інстанс гри має право надсилати BATTLE_START
        { // Починаємо блок методу
            if (CoopRuntime.Network == null) // Перевіряємо що NetworkManager ініціалізований
            { // Починаємо блок if
                return false; // Повертаємо false, бо без мережі немає куди надсилати
            } // Завершуємо блок if

            if (!CoopRuntime.Network.IsRunning) // Перевіряємо що мережа реально запущена (сервер активний)
            { // Починаємо блок if
                return false; // Повертаємо false, бо сервер ще не стартував
            } // Завершуємо блок if

            if (CoopRuntime.Network.Role != NetworkRole.Server) // Перевіряємо що ми саме хост/сервер
            { // Починаємо блок if
                return false; // Повертаємо false, бо клієнт не має розсилати battle події
            } // Завершуємо блок if

            return true; // Повертаємо true, бо ми сервер і можемо надсилати BATTLE_START
        } // Завершуємо блок методу

        private void ResetIfMissionEnded() // Helper: тримаємо внутрішній стан коректним, коли місія завершилась
        { // Починаємо блок методу
            if (Mission.Current != null) // Якщо місія активна, ми нічого не скидаємо
            { // Починаємо блок if
                return; // Виходимо, бо ми все ще в місії
            } // Завершуємо блок if

            _wasInMissionLastTick = false; // Скидаємо "було в місії" — ми вже не в місії
            _hasSentBattleStartForThisMission = false; // Дозволяємо наступній місії знову відправити BATTLE_START
        } // Завершуємо блок методу

        private void TrySendBattleStart() // Формуємо повідомлення та надсилаємо клієнтам (best-effort)
        { // Починаємо блок методу
            if (_hasSentBattleStartForThisMission) // Перевіряємо guard-прапорець
            { // Починаємо блок if
                return; // Виходимо, бо вже відправили для цієї місії
            } // Завершуємо блок if

            _hasSentBattleStartForThisMission = true; // Ставимо прапорець одразу, щоб навіть при винятку не спамити повторними відправками

            try // Захищаємо збір даних від винятків, щоб мод не крашив гру
            { // Починаємо блок try
                BattleStartMessage payload = BuildBattleStartPayload(); // Будуємо DTO з даними про битву/місію
                string wireMessage = BattleStartMessageCodec.BuildBattleStartMessage(payload); // Серіалізуємо DTO в `BATTLE_START:{json}`
                CoopRuntime.Network.BroadcastToClients(wireMessage); // Відправляємо повідомлення всім підключеним клієнтам

                UiFeedback.ShowMessageDeferred("Host: BATTLE_START sent to clients."); // Даємо короткий UI-індикатор для дебагу/перевірки
                ModLogger.Info("BATTLE_START broadcasted."); // Логуємо факт відправки в лог гри
            } // Завершуємо блок try
            catch (Exception ex) // Ловимо будь-які винятки (API changes/null, тощо)
            { // Починаємо блок catch
                ModLogger.Error("Failed to send BATTLE_START.", ex); // Логуємо помилку, щоб її можна було діагностувати
            } // Завершуємо блок catch
        } // Завершуємо блок методу

        private static BattleStartMessage BuildBattleStartPayload() // Будуємо мінімальні дані про старт битви/місії
        { // Починаємо блок методу
            BattleStartMessage message = new BattleStartMessage(); // Створюємо DTO

            // 1) Map scene name (best-effort) // Пояснюємо блок
            message.MapScene = TryGetMapSceneNameSafe(); // Пишемо назву сцени мапи (або "unknown")

            // 2) Map position (campaign world) // Пояснюємо блок
            float x = 0f; // Дефолтне значення X
            float y = 0f; // Дефолтне значення Y

            try // Пробуємо взяти позицію партії хоста
            { // Починаємо блок try
                var pos = MobileParty.MainParty.GetPosition2D; // Беремо позицію партії гравця на мапі (Vec2)
                x = pos.x; // Записуємо X
                y = pos.y; // Записуємо Y
            } // Завершуємо блок try
            catch (Exception) // Якщо ми не в кампанії або API недоступний — лишаємо 0/0
            { // Починаємо блок catch
            } // Завершуємо блок catch

            message.MapX = x; // Записуємо X у DTO
            message.MapY = y; // Записуємо Y у DTO

            // 3) Player side (best-effort) // Пояснюємо блок
            message.PlayerSide = TryGetPlayerSideTextSafe(); // Пишемо "Attacker/Defender/Unknown" як текст

            // 4) Troops list (party roster) // Пояснюємо блок
            message.Troops = BuildPartyTroopStacksSafe(); // Збираємо стеки військ з ростера партії
            message.ArmySize = TryGetArmySizeSafe(); // Записуємо сумарну кількість людей (для UI і тестів)

            return message; // Повертаємо сформований DTO
        } // Завершуємо блок методу

        private static string TryGetMapSceneNameSafe() // Best-effort: намагаємось отримати назву сцени карти кампанії
        { // Починаємо блок методу
            try // Обгортаємо в try/catch, бо MapSceneWrapper може бути недоступний у деяких контекстах
            { // Починаємо блок try
                if (TaleWorlds.CampaignSystem.Campaign.Current == null) // Якщо кампанія не створена (наприклад, ми в меню) — повертаємо "unknown"
                { // Починаємо блок if
                    return "unknown"; // Повертаємо fallback
                } // Завершуємо блок if

                object mapScene = TaleWorlds.CampaignSystem.Campaign.Current.MapSceneWrapper; // Беремо map-scene wrapper як object (щоб не залежати від конкретного інтерфейсу)

                if (mapScene == null) // Якщо wrapper відсутній — повертаємо fallback
                { // Починаємо блок if
                    return "unknown"; // Fallback
                } // Завершуємо блок if

                Type mapSceneType = mapScene.GetType(); // Отримуємо runtime-тип wrapper'а

                // 1) Пробуємо метод GetMapSceneName() (як у старих прикладах) // Пояснюємо першу спробу
                var getNameMethod = mapSceneType.GetMethod("GetMapSceneName", Type.EmptyTypes); // Шукаємо метод без параметрів

                if (getNameMethod != null) // Якщо метод знайдено — пробуємо викликати
                { // Починаємо блок if
                    object value = getNameMethod.Invoke(mapScene, null); // Викликаємо метод reflection'ом
                    string text = value != null ? value.ToString() : null; // Перетворюємо результат в string

                    if (!string.IsNullOrEmpty(text)) // Якщо рядок валідний — повертаємо його
                    { // Починаємо блок if
                        return text; // Повертаємо назву сцени
                    } // Завершуємо блок if
                } // Завершуємо блок if

                // 2) Пробуємо властивості з типових назв: SceneName / Name / MapSceneName // Пояснюємо другу спробу
                var nameProperty = // Оголошуємо змінну з першим знайденим property
                    mapSceneType.GetProperty("SceneName") ?? // Варіант 1
                    mapSceneType.GetProperty("Name") ?? // Варіант 2
                    mapSceneType.GetProperty("MapSceneName"); // Варіант 3

                if (nameProperty != null) // Якщо property знайдено — читаємо значення
                { // Починаємо блок if
                    object value = nameProperty.GetValue(mapScene, null); // Читаємо значення property
                    string text = value != null ? value.ToString() : null; // Перетворюємо в string

                    if (!string.IsNullOrEmpty(text)) // Якщо рядок валідний — повертаємо його
                    { // Починаємо блок if
                        return text; // Повертаємо назву сцени
                    } // Завершуємо блок if
                } // Завершуємо блок if

                return mapScene.ToString(); // Останній fallback — ToString() типу wrapper'а
            } // Завершуємо блок try
            catch (Exception) // Якщо API/контекст не підходить — повертаємо "unknown"
            { // Починаємо блок catch
                return "unknown"; // Повертаємо fallback
            } // Завершуємо блок catch
        } // Завершуємо блок методу

        private static int TryGetArmySizeSafe() // Best-effort: намагаємось отримати загальний розмір партії
        { // Починаємо блок методу
            try // Захищаємо доступ до MobileParty на випадок, якщо ми не в кампанії
            { // Починаємо блок try
                return MobileParty.MainParty.MemberRoster.TotalManCount; // Повертаємо загальну кількість людей у ростері
            } // Завершуємо блок try
            catch (Exception) // Якщо щось не доступно — повертаємо 0
            { // Починаємо блок catch
                return 0; // Fallback
            } // Завершуємо блок catch
        } // Завершуємо блок методу

        private static List<TroopStackInfo> BuildPartyTroopStacksSafe() // Будуємо список стеків військ з партії (best-effort)
        { // Починаємо блок методу
            List<TroopStackInfo> troops = new List<TroopStackInfo>(); // Створюємо список результатів

            try // Захищаємо доступ до ростеру, щоб не крашнути гру
            { // Починаємо блок try
                foreach (var element in MobileParty.MainParty.MemberRoster.GetTroopRoster()) // Проходимо по елементах ростеру партії
                { // Починаємо блок foreach
                    if (element.Character == null) // Перевіряємо що є Character (тип юнита)
                    { // Починаємо блок if
                        continue; // Пропускаємо некоректні елементи
                    } // Завершуємо блок if

                    TroopStackInfo stack = new TroopStackInfo(); // Створюємо DTO стеку
                    stack.CharacterId = element.Character.StringId; // Записуємо StringId юнита (стабільний ключ)
                    stack.TroopName = element.Character.Name != null ? element.Character.Name.ToString() : element.Character.StringId; // Беремо ім'я або fallback на id
                    stack.Tier = element.Character.Tier; // Записуємо tier
                    stack.IsMounted = element.Character.IsMounted; // Записуємо чи верховий
                    stack.IsHero = element.Character.IsHero; // Записуємо чи герой
                    stack.Count = element.Number; // Записуємо кількість у стеку

                    // WoundedNumber може бути відсутній в деяких версіях; // Пояснюємо чому try/catch
                    // тому беремо його best-effort через try/catch. // Пояснюємо підхід
                    int wounded = 0; // Дефолт

                    try // Пробуємо взяти wounded count
                    { // Починаємо блок try
                        wounded = element.WoundedNumber; // Беремо кількість поранених у стеку (якщо API має цю властивість)
                    } // Завершуємо блок try
                    catch (Exception) // Якщо властивості нема — лишаємо 0
                    { // Починаємо блок catch
                    } // Завершуємо блок catch

                    stack.WoundedCount = wounded; // Записуємо wounded count

                    troops.Add(stack); // Додаємо стек до списку
                } // Завершуємо блок foreach
            } // Завершуємо блок try
            catch (Exception ex) // Якщо ростер недоступний — лог і повертаємо те, що є
            { // Починаємо блок catch
                ModLogger.Error("Failed to build troop roster for BATTLE_START.", ex); // Логуємо помилку
            } // Завершуємо блок catch

            return troops; // Повертаємо список (може бути порожнім — це ок для MVP)
        } // Завершуємо блок методу

        private static string TryGetPlayerSideTextSafe() // Best-effort: намагаємось визначити сторону гравця в поточному encounter
        { // Починаємо блок методу
            // Ми не хардкодимо PlayerEncounter тип напряму, // Пояснюємо чому reflection
            // щоб зменшити ризик несумісності API між версіями гри. // Пояснюємо мету
            try // Обгортаємо в try/catch, бо reflection може впасти
            { // Починаємо блок try
                var campaignAssembly = typeof(TaleWorlds.CampaignSystem.Campaign).Assembly; // Беремо збірку TaleWorlds.CampaignSystem, де зазвичай лежать encounter типи

                // Найчастіші повні імена для PlayerEncounter в Bannerlord. // Пояснюємо список кандидатів
                Type typeA = campaignAssembly.GetType("TaleWorlds.CampaignSystem.Encounters.PlayerEncounter"); // Варіант 1: через namespace Encounters
                Type typeB = campaignAssembly.GetType("TaleWorlds.CampaignSystem.PlayerEncounter"); // Варіант 2: прямо в CampaignSystem
                Type playerEncounterType = typeA ?? typeB; // Беремо перший знайдений варіант

                if (playerEncounterType == null) // Якщо тип не знайдений — повертаємо Unknown
                { // Починаємо блок if
                    return "Unknown"; // Fallback
                } // Завершуємо блок if

                // Очікуємо static property Battle. // Пояснюємо очікувану структуру
                var battleProperty = playerEncounterType.GetProperty("Battle"); // Беремо property "Battle"

                if (battleProperty == null) // Якщо property немає — повертаємо Unknown
                { // Починаємо блок if
                    return "Unknown"; // Fallback
                } // Завершуємо блок if

                object battle = battleProperty.GetValue(null, null); // Читаємо static property (екземпляр не потрібен)

                if (battle == null) // Якщо battle null — значить encounter не в стані battle або ще не створений
                { // Починаємо блок if
                    return "Unknown"; // Fallback
                } // Завершуємо блок if

                // Очікуємо property PlayerSide на battle-об'єкті. // Пояснюємо очікування
                var playerSideProperty = battle.GetType().GetProperty("PlayerSide"); // Беремо property "PlayerSide"

                if (playerSideProperty == null) // Якщо немає — повертаємо Unknown
                { // Починаємо блок if
                    return "Unknown"; // Fallback
                } // Завершуємо блок if

                object sideValue = playerSideProperty.GetValue(battle, null); // Читаємо значення PlayerSide (зазвичай enum)

                return sideValue != null ? sideValue.ToString() : "Unknown"; // Повертаємо enum.ToString() або Unknown
            } // Завершуємо блок try
            catch (Exception) // Якщо reflection не вдався — не ламаємо мод, а просто повертаємо Unknown
            { // Починаємо блок catch
                return "Unknown"; // Fallback
            } // Завершуємо блок catch
        } // Завершуємо блок методу
    } // Завершуємо блок класу
} // Завершуємо блок простору імен

