using System; // Підключаємо базові типи .NET (Exception)
using System.Collections.Generic; // Підключаємо List<> для списків поведінок місії
using System.IO; // Підключаємо File/Directory/Path для перевірки наявності сцени на диску
using System.Reflection; // Підключаємо reflection (MethodInfo/BindingFlags) для виклику MP API, яке може відрізнятися між reference DLL
using CoopSpectator.Infrastructure; // Підключаємо UiFeedback + логер для повідомлень
using TaleWorlds.CampaignSystem; // Підключаємо Campaign (щоб перевірити що ми в кампанії)
using TaleWorlds.CampaignSystem.Party; // Підключаємо MobileParty (fallback для вибору інфантерії)
using TaleWorlds.Core; // Підключаємо MissionInitializerRecord + MissionMode + AgentControllerType + FormationClass
using TaleWorlds.Library; // Підключаємо Vec2/Vec3/MatrixFrame
using TaleWorlds.MountAndBlade; // Підключаємо Mission/MissionState/GameNetwork/MissionLogic/Team/Formation/Orders
using TaleWorlds.ObjectSystem; // Підключаємо MBObjectManager для пошуку troop'ів за StringId

namespace CoopSpectator.Multiplayer // Окремий namespace для MP PoC/досліджень (Stage 3.2)
{ // Починаємо блок простору імен
    /// <summary> // Документуємо клас
    /// PoC для Stage 3.2: довести, що ми можемо ініціалізувати GameNetwork // Пояснюємо призначення
    /// і відкрити MP-like місію (multiplayer сцену) прямо з кампанії. // Пояснюємо “що саме перевіряємо”
    /// </summary> // Завершуємо XML-коментар
    public static class TestMpLaunchPoC // Статичний клас, бо нам не потрібен стан/екземпляри
    { // Починаємо блок класу
        private const int ServerPort = 7777; // Хардкод порту: за вимогою PoC
        private const string RequestedSceneName = "mp_sergeant_field"; // Сцена за вимогою PoC (може бути відсутня у цій версії гри)
        private const int TeamSize = 2; // Хардкод “2v2”: по 2 агенти на команду
        // ВАЖЛИВО: повний старт MP (`StartMultiplayer*`) вмикає мережеву реплікацію. // Пояснюємо причину
        // У SP-контексті без реальних peer'ів це призводило до крашу під час `SpawnAgent` (native crash). // Пояснюємо симптом
        // Для Stage 3.2 PoC нам достатньо довести, що MP сцену можна запустити з кампанії і побачити 2v2 AI. // Пояснюємо мету
        // NOTE: робимо це `readonly`, а не `const`, щоб компілятор не вирізав гілку // Пояснюємо чому
        // і не давав warning "Unreachable code" у релізній збірці. // Пояснюємо симптом
        private static readonly bool EnableFullMultiplayerSessionStart = true; // Вмикаємо повний старт, щоб mission була MP-like (Stage 3.2)
        private static IGameNetworkHandler _gameNetworkHandler; // Зберігаємо handler, щоб ініціалізувати GameNetwork один раз

        public static string Launch() // Запускаємо PoC: мережа + місія + спавн 2v2 AI
        { // Починаємо блок методу
            // 0) Базові перевірки контексту. // Пояснюємо етап
            // Важливо: тут "Campaign" може конфліктувати з нашим namespace `CoopSpectator.Campaign`. // Пояснюємо чому full name
            // Тому ми явно звертаємось до типу `TaleWorlds.CampaignSystem.Campaign`. // Пояснюємо як уникнути конфлікту
            if (TaleWorlds.CampaignSystem.Campaign.Current == null) // Якщо кампанія не активна, ми не хочемо запускати PoC з меню/іншого режиму
            { // Починаємо блок if
                return "ERROR: Campaign is not running. Load a campaign first."; // Повертаємо зрозумілу помилку в консоль
            } // Завершуємо блок if

            if (Mission.Current != null) // Якщо вже є активна місія — не запускаємо другу, щоб не зламати стан гри
            { // Починаємо блок if
                return "ERROR: Mission is already running. Leave current mission first."; // Пояснюємо користувачу що робити
            } // Завершуємо блок if

            // 1) Ініціалізуємо GameNetwork server-side. // Пояснюємо етап
            string step = "unknown"; // Тримаємо назву кроку, щоб у помилці було зрозуміло, де саме впало

            try // Обгортаємо в try/catch, бо GameNetwork може кинути виняток в SP контексті
            { // Починаємо блок try
                // 1.0) Обов’язково ініціалізуємо GameNetwork з нашим handler'ом. // Пояснюємо навіщо
                // Без handler'а частина внутрішніх полів GameNetwork може бути null, // Пояснюємо причину NRE
                // і PreStartMultiplayerOnServer/InitializeServerSide можуть падати з NullReferenceException. // Пояснюємо симптом
                step = "GameNetwork.Initialize(handler)"; // Ставимо поточний крок
                EnsureGameNetworkInitialized(); // Викликаємо helper, який робить Initialize один раз

                // 1.1) Ініціалізація compression info (часто потрібна перед мережею). // Пояснюємо чому робимо це явно
                step = "GameNetwork.InitializeCompressionInfos()"; // Оновлюємо крок
                TryInvokeGameNetworkStaticVoidMethodByName( // Викликаємо reflection helper (на випадок якщо reference DLL не має методу)
                    methodName: "InitializeCompressionInfos", // Назва методу
                    args: null); // Без аргументів

                // PreStart готує мультиплеєр для запуску на сервері. // Пояснюємо навіщо цей виклик
                step = "GameNetwork.PreStartMultiplayerOnServer()"; // Оновлюємо крок
                GameNetwork.PreStartMultiplayerOnServer(); // Готуємо внутрішні структури до старту MP сесії

                // Вимога PoC: викликати InitializeServerSide(7777). // Пояснюємо що це основний доказ “MP init працює”
                // Важливо: наші compile-time reference DLL можуть бути "урізані" і не містити цього методу. // Пояснюємо проблему
                // Тому викликаємо його reflection'ом по імені, щоб PoC працював з game DLL на рантаймі. // Пояснюємо рішення
                step = "GameNetwork.InitializeServerSide(7777)"; // Оновлюємо крок
                InvokeGameNetworkStaticVoidMethodByName( // Викликаємо helper, який робить reflection Invoke
                    methodName: "InitializeServerSide", // Ім'я потрібного методу
                    args: new object[] { ServerPort }); // Аргументи: порт 7777

                if (EnableFullMultiplayerSessionStart) // Якщо ми свідомо дозволили повний MP старт (для подальших експериментів)
                { // Begin if
                    // Додатково пробуємо "StartMultiplayer" шлях, щоб активувати multiplayer state. // Пояснюємо для стабільності
                    // У різних версіях Bannerlord порядок може відрізнятися; // Пояснюємо чому best-effort
                    // але PoC має максимально підвищити шанс, що місія відкриється як MP-like. // Пояснюємо мету
                    try // Внутрішній try: якщо вже “стартовано” — просто ігноруємо
                    { // Begin nested try
                        step = "GameNetwork.StartMultiplayerOnServer(7777)"; // Оновлюємо крок
                        GameNetwork.StartMultiplayerOnServer(ServerPort); // Стартуємо MP як сервер (може бути no-op якщо вже стартовано)
                    } // End nested try
                    catch (Exception ex) // Якщо метод падає через стан/порт — не валимо PoC одразу
                    { // Begin nested catch
                        ModLogger.Info("StartMultiplayerOnServer failed (ignored): " + ex.Message); // Логуємо причину для дебагу
                    } // End nested catch

                    try // Другий best-effort виклик: “загальний старт мультиплеєра”
                    { // Begin nested try
                        // StartMultiplayer також може бути відсутній у reference DLL, // Пояснюємо чому reflection знову
                        // тому викликаємо його через reflection. // Пояснюємо підхід
                        step = "GameNetwork.StartMultiplayer()"; // Оновлюємо крок
                        InvokeGameNetworkStaticVoidMethodByName( // Викликаємо helper
                            methodName: "StartMultiplayer", // Ім'я методу без параметрів
                            args: null); // Аргументів немає
                    } // End nested try
                    catch (Exception ex) // Якщо не можна стартувати MP повністю в SP — не валимо PoC
                    { // Begin nested catch
                        ModLogger.Info("StartMultiplayer failed (ignored): " + ex.Message); // Логуємо для діагностики
                    } // End nested catch
                } // End if
                else // Якщо повний MP старт вимкнено (дефолт для стабільного PoC)
                { // Begin else
                    ModLogger.Info( // Логуємо для дебагу
                        "MP test: skipping StartMultiplayer* to avoid crash; " + // Пояснюємо що пропускаємо
                        "running mission offline."); // Пояснюємо наслідок
                } // End else

                // ВАЖЛИВО: у повноцінному MP-пайплайні у сервера зазвичай є server-peer (host), // Пояснюємо контекст
                // і без нього деякі MP-операції (CreateAgent/AssignAgent) можуть падати нативно. // Пояснюємо симптом
                // Тому ми створюємо "server peer" штучно перед запуском місії. // Пояснюємо рішення
                step = "GameNetwork.AddNewPlayerOnServer(serverPeer=true)"; // Оновлюємо крок
                EnsureServerPeerExists(); // Створюємо server peer best-effort, щоб MP місія не крашилась на spawn

                // Показуємо короткий статус GameNetwork, щоб одразу бачити результат ініціалізації. // Пояснюємо навіщо
                UiFeedback.ShowMessageDeferred( // Використовуємо deferred, щоб повідомлення не загубилось у transition
                    "MP init: IsServer=" + GameNetwork.IsServer + // Показуємо чи гра вважає нас сервером
                    ", IsMultiplayer=" + GameNetwork.IsMultiplayer + // Показуємо чи активний multiplayer mode
                    ", IsSessionActive=" + GameNetwork.IsSessionActive + // Показуємо чи активна сесія
                    ", PeerCount=" + GameNetwork.NetworkPeerCount + // Показуємо кількість peer'ів
                    "."); // Завершуємо текст
            } // Завершуємо блок try
            catch (Exception ex) // Якщо мережевий init не вдався
            { // Починаємо блок catch
                ModLogger.Error("Failed to initialize GameNetwork (" + step + ").", ex); // Логуємо повну помилку + крок
                return "ERROR: GameNetwork init failed at " + step + ": " + ex.Message; // Повертаємо коротку помилку в консоль
            } // Завершуємо блок catch

            // 2) Відкриваємо місію через MissionState.OpenNew. // Пояснюємо етап
            try // Обгортаємо відкриття місії в try/catch
            { // Починаємо блок try
                // Важливо: у v1.3.14 сцени `mp_sergeant_field` може не бути. // Пояснюємо причину крашу/зависання
                // Тому перед запуском вибираємо першу доступну MP сцену (fallback). // Пояснюємо рішення
                string sceneName = ResolveSceneNameForPoC(); // Обираємо сцену: requested або fallback, якщо requested відсутня

                if (string.IsNullOrEmpty(sceneName)) // Якщо не змогли знайти жодної валідної MP сцени
                { // Begin if
                    return "ERROR: No suitable MP scene found. " + // Пояснюємо помилку
                           "Requested '" + RequestedSceneName + "' missing."; // Повертаємо причину в консоль
                } // End if

                MissionInitializerRecord record = new MissionInitializerRecord(sceneName); // Створюємо record з реальною назвою сцени (scene id)
                record.PlayingInCampaignMode = false; // Критично: це НЕ campaign mission, а “MP-like” контекст
                record.DoNotUseLoadingScreen = false; // Дозволяємо loading screen, щоб сцена гарантовано встигла завантажитись

                // Відкриваємо нову місію. // Пояснюємо виклик
                MissionState.OpenNew( // Статичний helper Bannerlord для старту місій
                    "coop_test_mp_launch", // Ім'я місії (для логів/діагностики)
                    record, // Параметри ініціалізації сцени/режиму
                    CreateMissionBehaviors, // Делегат який повертає список MissionBehavior'ів
                    addDefaultMissionBehaviors: true, // Просимо гру додати дефолтні behaviors (камера, базова логіка, тощо)
                    needsMemoryCleanup: true); // Просимо гру робити memory cleanup при переході (безпечніше для PoC)

                return "OK: Launching MP test mission on scene '" + // Пояснюємо успіх
                       sceneName + "'."; // Повертаємо фактичну сцену (requested або fallback)
            } // Завершуємо блок try
            catch (Exception ex) // Якщо місію не вдалося відкрити
            { // Починаємо блок catch
                ModLogger.Error("Failed to open test MP mission from campaign.", ex); // Логуємо повну причину
                return "ERROR: MissionState.OpenNew failed: " + ex.Message; // Повертаємо коротку помилку
            } // Завершуємо блок catch
        } // Завершуємо блок методу

        private static string ResolveSceneNameForPoC() // Обираємо сцену, яка точно існує в поточній інсталяції гри
        { // Begin helper
            // 1) Спершу пробуємо сцену, яку попросили в PoC. // Пояснюємо пріоритет
            string requestedPath; // Сюди запишемо шлях, якщо знайдемо сцену

            if (TryFindSceneXscenePath(RequestedSceneName, out requestedPath)) // Перевіряємо, чи існує requested сцена на диску
            { // Begin if
                UiFeedback.ShowMessageDeferred( // Показуємо гравцю що scene знайдена
                    "MP test: using requested scene '" + // Текст повідомлення
                    RequestedSceneName + "'."); // Показуємо саме requested ім'я
                return RequestedSceneName; // Повертаємо requested сцену як “реальну”
            } // End if

            // 2) Якщо requested сцена відсутня — беремо fallback зі списку. // Пояснюємо навіщо
            // Важливо: список підібраний з ванільних MP сцен (зазвичай у Native). // Пояснюємо вибір
            string[] fallbacks = new string[] // Список fallback сцен (порядок = пріоритет)
            { // Begin array
                "mp_skirmish_spawn_test", // Тестова сцена зі spawnpoint'ами (часто найстабільніша)
                "mp_compact", // Маленька карта (швидко вантажиться)
                "mp_tdm_map_001", // Поширена TDM карта
                "mp_skirmish_map_002f", // Поширена Skirmish карта
            }; // End array

            for (int i = 0; i < fallbacks.Length; i++) // Перебираємо fallback сцени
            { // Begin for
                string fallbackName = fallbacks[i]; // Беремо конкретний fallback
                string fallbackPath; // Сюди запишемо шлях до fallback, якщо знайдемо

                if (!TryFindSceneXscenePath(fallbackName, out fallbackPath)) // Якщо fallback сцени немає — пробуємо наступну
                { // Begin if
                    continue; // Йдемо до наступного кандидата
                } // End if

                UiFeedback.ShowMessageDeferred( // Показуємо, що requested немає, але ми знайшли заміну
                    "MP test: scene '" + RequestedSceneName + // Пояснюємо що requested відсутня
                    "' not found. Using fallback '" + // Пояснюємо що беремо fallback
                    fallbackName + "'."); // Показуємо назву fallback сцени
                return fallbackName; // Повертаємо перший валідний fallback
            } // End for

            // 3) Якщо нічого не знайшли — повертаємо null, щоб НЕ запускати місію (і не крашити гру). // Пояснюємо guard
            UiFeedback.ShowMessageDeferred( // Показуємо повідомлення користувачу
                "MP test: no MP scene found on disk. Aborting."); // Пояснюємо що ми зупинили запуск
            return null; // Caller поверне ERROR в консоль
        } // End helper

        private static bool TryFindSceneXscenePath( // Шукаємо файл `scene.xscene` для заданого sceneName у будь-якому модулі
            string sceneName, // Ім'я сцени (наприклад "mp_compact")
            out string xscenePath) // Вихідний параметр: повний шлях до `scene.xscene`, якщо знайдено
        { // Begin helper
            xscenePath = null; // Дефолт: не знайдено

            if (string.IsNullOrEmpty(sceneName)) // Захист від null/порожнього імені
            { // Begin if
                return false; // Нічого шукати
            } // End if

            string modulesDir; // Тут буде шлях до папки `Modules`

            if (!TryGetModulesDirectory(out modulesDir)) // Якщо не змогли визначити папку `Modules`
            { // Begin if
                ModLogger.Info( // Логуємо для дебагу
                    "MP test: failed to resolve Modules directory " + // Частина повідомлення
                    "(cannot verify scene existence)."); // Пояснюємо наслідок
                return false; // Не можемо гарантувати, що сцена існує
            } // End if

            try // Обгортаємо в try, бо Directory.GetDirectories може кинути виняток
            { // Begin try
                string[] moduleDirs = Directory.GetDirectories(modulesDir); // Беремо всі модулі як папки в `Modules`

                for (int i = 0; i < moduleDirs.Length; i++) // Перебираємо всі модулі
                { // Begin for
                    string moduleDir = moduleDirs[i]; // Папка конкретного модуля

                    // Стандартний шлях до сцени в модулі: Modules/<Mod>/SceneObj/<Scene>/scene.xscene // Пояснюємо шаблон
                    string candidatePath = Path.Combine( // Склеюємо шлях без ручних слешів
                        moduleDir, // Корінь конкретного модуля
                        "SceneObj", // Папка зі сценами
                        sceneName, // Папка конкретної сцени
                        "scene.xscene"); // Файл опису сцени

                    if (!File.Exists(candidatePath)) // Якщо файлу не існує — пробуємо наступний модуль
                    { // Begin if
                        continue; // Йдемо далі
                    } // End if

                    xscenePath = candidatePath; // Записуємо знайдений шлях
                    return true; // Сцена знайдена
                } // End for
            } // End try
            catch (Exception ex) // Якщо виникла помилка доступу/IO
            { // Begin catch
                ModLogger.Info( // Логуємо, але не крашимось
                    "MP test: error while scanning Modules for scene '" + // Пояснюємо контекст
                    sceneName + "': " + ex.Message); // Пишемо текст помилки
            } // End catch

            return false; // Не знайшли сцену у жодному модулі
        } // End helper

        private static bool TryGetModulesDirectory( // Визначаємо шлях до `.../Mount & Blade II Bannerlord/Modules`
            out string modulesDir) // Вихідний параметр: шлях до `Modules`
        { // Begin helper
            modulesDir = null; // Дефолт: ще не визначено

            try // Обгортаємо визначення шляхів у try, щоб не падати на Path.GetFullPath
            { // Begin try
                // Зазвичай BaseDirectory = .../bin/Win64_Shipping_Client/ // Пояснюємо очікування
                string baseDir = AppDomain.CurrentDomain.BaseDirectory; // Беремо базову папку процесу

                if (string.IsNullOrEmpty(baseDir)) // Захист від дивних випадків
                { // Begin if
                    return false; // Не можемо визначити шлях
                } // End if

                // 1) Типовий шлях: baseDir/../../ = корінь гри. // Пояснюємо варіант №1
                string root1 = Path.GetFullPath( // Нормалізуємо шлях
                    Path.Combine(baseDir, "..", "..")); // Піднімаємось з bin/Win64_Shipping_Client до кореня гри
                string modules1 = Path.Combine(root1, "Modules"); // Додаємо папку Modules

                if (Directory.Exists(modules1)) // Якщо папка існує — це наш шлях
                { // Begin if
                    modulesDir = modules1; // Записуємо результат
                    return true; // Успіх
                } // End if

                // 2) Інколи baseDir може бути .../bin/, тоді ../ = корінь. // Пояснюємо варіант №2
                string root2 = Path.GetFullPath( // Нормалізуємо
                    Path.Combine(baseDir, "..")); // Піднімаємось на 1 рівень
                string modules2 = Path.Combine(root2, "Modules"); // Додаємо Modules

                if (Directory.Exists(modules2)) // Перевіряємо існування
                { // Begin if
                    modulesDir = modules2; // Записуємо
                    return true; // Успіх
                } // End if

                // 3) Рідкісний випадок: baseDir вже корінь гри. // Пояснюємо варіант №3
                string modules3 = Path.Combine(baseDir, "Modules"); // Додаємо Modules прямо до baseDir

                if (Directory.Exists(modules3)) // Якщо папка існує
                { // Begin if
                    modulesDir = modules3; // Записуємо
                    return true; // Успіх
                } // End if
            } // End try
            catch (Exception ex) // Якщо щось пішло не так з IO/Path
            { // Begin catch
                ModLogger.Info( // Логуємо для дебагу
                    "MP test: failed to resolve Modules directory: " + // Пояснюємо що впало
                    ex.Message); // Пишемо текст помилки
            } // End catch

            return false; // Не змогли визначити папку Modules
        } // End helper

        private static IEnumerable<MissionBehavior> CreateMissionBehaviors(Mission mission) // Формуємо список behaviors для місії
        { // Починаємо блок методу
            // Ми додаємо лише наш MissionLogic, який заспавнить 2v2 AI. // Пояснюємо мінімалізм PoC
            // Дефолтні behaviors додає сама гра (addDefaultMissionBehaviors=true). // Пояснюємо розподіл відповідальності
            var behaviors = new List<MissionBehavior>(); // Створюємо список behaviors
            behaviors.Add(new TestMpLaunch2v2AiLogic()); // Додаємо наш custom MissionLogic
            return behaviors; // Повертаємо список в MissionState.OpenNew
        } // Завершуємо блок методу

        /// <summary> // Документуємо MissionLogic
        /// MissionLogic, який після старту місії спавнить 2v2 AI інфантерію // Пояснюємо роль
        /// і налаштовує AI так, щоб ми одразу бачили бій (без використання Formation). // Пояснюємо очікуваний результат
        /// </summary> // Завершуємо XML-коментар
        private sealed class TestMpLaunch2v2AiLogic : MissionLogic // Наслідуємо MissionLogic, бо нам потрібен AfterStart()
        { // Починаємо блок класу
            private bool _hasSpawned; // Guard: щоб не спавнити агентів кілька разів

            public override void AfterStart() // Викликається після того, як місія/сцена вже завантажена і готова
            { // Починаємо блок методу
                base.AfterStart(); // Викликаємо базову реалізацію (на всяк випадок)

                if (_hasSpawned) // Якщо вже спавнили — виходимо
                { // Begin if
                    return; // Не робимо нічого вдруге
                } // End if

                _hasSpawned = true; // Позначаємо що ми вже виконали спавн

                Mission mission = Mission.Current; // Беремо поточну місію (гарантовано не null всередині AfterStart)

                if (mission == null) // Додатковий захист від несподіваного null
                { // Begin if
                    return; // Вихід (краще нічого не робити, ніж крашнути)
                } // End if

                // 1) Перемикаємо місію в режим Battle, щоб AI/симуляція поводились як бій. // Пояснюємо навіщо
                try // Обгортаємо, бо SetMissionMode може кинути виняток у деяких контекстах
                { // Begin try
                    mission.SetMissionMode(MissionMode.Battle, true); // Встановлюємо режим Battle і force = true
                } // End try
                catch (Exception ex) // Якщо не вдалось — просто логуємо, але продовжуємо PoC
                { // Begin catch
                    ModLogger.Info("SetMissionMode(Battle) failed (ignored): " + ex.Message); // Логуємо для діагностики
                } // End catch

                // 2) Готуємо команди (Attacker/Defender). // Пояснюємо етап
                Team attackerTeam = mission.Teams.Attacker; // Беремо attacker team (може бути null, якщо ще не створена)
                Team defenderTeam = mission.Teams.Defender; // Беремо defender team (може бути null)

                if (attackerTeam == null) // Якщо attacker team ще не існує — створюємо
                { // Begin if
                    attackerTeam = mission.Teams.Add( // Додаємо команду на стороні Attacker
                        BattleSideEnum.Attacker, // Сторона бою
                        0xFFCC2222u, // Колір 1 (червоний) у форматі uint
                        0xFF661111u, // Колір 2 (темніший червоний) у форматі uint
                        banner: null, // Банер не потрібен для PoC
                        isPlayerGeneral: false, // Ми не хочемо робити гравця генералом (це AI vs AI)
                        isPlayerSergeant: false, // І точно не сержантом у цьому PoC
                        // ВАЖЛИВО: НЕ просимо гру автоматично налаштовувати відносини під час Add(), // Пояснюємо причину
                        // бо після швидкого EndMultiplayer/TerminateServerSide це інколи провокує native crash. // Пояснюємо симптом
                        // Відносини ми виставимо вручну нижче через SetIsEnemyOf(). // Пояснюємо рішення
                        isSettingRelations: false); // Не налаштовуємо relations автоматично (manual нижче)
                } // End if

                if (defenderTeam == null) // Якщо defender team ще не існує — створюємо
                { // Begin if
                    defenderTeam = mission.Teams.Add( // Додаємо команду на стороні Defender
                        BattleSideEnum.Defender, // Сторона бою
                        0xFF2255CCu, // Колір 1 (синій) у форматі uint
                        0xFF112266u, // Колір 2 (темніший синій)
                        banner: null, // Банер не потрібен
                        isPlayerGeneral: false, // Гравець не генерал
                        isPlayerSergeant: false, // Гравець не сержант
                        // Див. пояснення вище: relations виставимо вручну, щоб уникнути крашу. // Пояснюємо повторно
                        isSettingRelations: false); // Не налаштовуємо relations автоматично (manual нижче)
                } // End if

                // На всякий випадок явно робимо команди ворогами. // Пояснюємо навіщо
                attackerTeam.SetIsEnemyOf(defenderTeam, true); // Attacker ворожий до Defender
                defenderTeam.SetIsEnemyOf(attackerTeam, true); // Defender ворожий до Attacker

                // 3) Підготовка spawn frame behavior для пошуку spawnpoint'ів у MP сцені. // Пояснюємо етап
                var spawnFrameBehavior = new FFASpawnFrameBehavior(); // Створюємо spawn frame behavior (використовує теги "spawnpoint")
                spawnFrameBehavior.Initialize(); // Ініціалізуємо (збирає spawn points зі сцени)

                // Беремо базові точки спавну для кожної команди. // Пояснюємо етап
                MatrixFrame attackerSpawn = spawnFrameBehavior.GetSpawnFrame(attackerTeam, hasMount: false, isInitialSpawn: true); // Frame для attacker
                MatrixFrame defenderSpawn = spawnFrameBehavior.GetSpawnFrame(defenderTeam, hasMount: false, isInitialSpawn: true); // Frame для defender

                // 4) Обираємо “інфантерію” (BasicCharacterObject) для PoC. // Пояснюємо етап
                BasicCharacterObject attackerTroop = TryPickInfantryTroopSafe(); // Підбираємо інфантерію best-effort
                BasicCharacterObject defenderTroop = attackerTroop; // Для PoC використовуємо той самий troop (2v2 інфантерія)

                // Якщо не вдалося знайти жодного troop'а — зупиняємось. // Пояснюємо guard
                if (attackerTroop == null) // Перевіряємо null
                { // Begin if
                    UiFeedback.ShowMessageDeferred("MP test: failed to pick infantry troop."); // Показуємо повідомлення в UI
                    return; // Вихід
                } // End if

                // 5) Рахуємо базові позиції так, щоб обидві команди були близько одна до одної. // Пояснюємо навіщо
                // Це важливо, бо без Formation AI може не "бігти через всю карту". // Пояснюємо причину
                Vec3 attackerBase = attackerSpawn.origin; // Беремо базову позицію attacker зі spawnpoint
                Vec2 dirToDef2 = new Vec2( // Беремо напрямок у площині X/Y від attacker до defender spawn
                    defenderSpawn.origin.x - attackerSpawn.origin.x, // ΔX
                    defenderSpawn.origin.y - attackerSpawn.origin.y); // ΔY

                if (dirToDef2.LengthSquared < 0.001f) // Якщо напрямок майже нульовий (spawnpoints співпали)
                { // Begin if
                    dirToDef2 = new Vec2(1f, 0f); // Ставимо дефолтний напрямок по X
                } // End if

                dirToDef2.Normalize(); // Нормалізуємо, щоб отримати unit vector

                Vec2 right2 = new Vec2(-dirToDef2.y, dirToDef2.x); // Перпендикуляр вправо (для розведення агентів в сторони)
                Vec3 defenderBase = attackerBase + new Vec3( // Ставимо defender базу на ~10м попереду attacker
                    dirToDef2.x * 10f, // Зсув по X
                    dirToDef2.y * 10f, // Зсув по Y
                    0f); // Z не змінюємо (беремо з attackerBase)

                // 6) Спавнимо 2 агенти на attacker стороні БЕЗ формацій. // Пояснюємо етап
                // Причина: у dump.dmp краш відбувався в Agent.Formation.set(...) під час SpawnAgent. // Пояснюємо причину
                var attackerAgents = new List<Agent>(); // Зберігаємо attacker агентів, щоб потім призначити цілі
                var defenderAgents = new List<Agent>(); // Зберігаємо defender агентів

                for (int i = 0; i < TeamSize; i++) // Робимо рівно 2 агенти
                { // Begin for
                    float lateral = (i - ((TeamSize - 1) * 0.5f)) * 2.0f; // -2м і +2м для 2 агентів (щоб не спавнитись в одній точці)
                    Vec3 pos = attackerBase + new Vec3( // Рахуємо позицію attacker агента
                        right2.x * lateral, // Зсув вправо/вліво по X
                        right2.y * lateral, // Зсув вправо/вліво по Y
                        0f); // Z не змінюємо

                    Agent agent = SpawnAiInfantry( // Викликаємо helper спавну, який повертає Agent
                        mission, // Поточна місія
                        attackerTeam, // Команда attacker
                        attackerTroop, // Troop для attacker
                        pos, // Позиція спавну
                        lookAt: defenderBase); // Дивимось у бік defender бази

                    if (agent != null) // Якщо агент успішно заспавнився
                    { // Begin if
                        attackerAgents.Add(agent); // Додаємо у список attacker'ів
                    } // End if
                } // End for

                // 7) Спавнимо 2 агенти на defender стороні. // Пояснюємо етап
                for (int i = 0; i < TeamSize; i++) // Робимо рівно 2 агенти
                { // Begin for
                    float lateral = (i - ((TeamSize - 1) * 0.5f)) * 2.0f; // Такий самий розкид по ширині
                    Vec3 pos = defenderBase + new Vec3( // Рахуємо позицію defender агента
                        right2.x * lateral, // Зсув вправо/вліво по X
                        right2.y * lateral, // Зсув вправо/вліво по Y
                        0f); // Z не змінюємо

                    Agent agent = SpawnAiInfantry( // Спавнимо defender агента
                        mission, // Поточна місія
                        defenderTeam, // Команда defender
                        defenderTroop, // Troop defender
                        pos, // Позиція спавну
                        lookAt: attackerBase); // Дивимось у бік attacker бази

                    if (agent != null) // Якщо агент успішно заспавнився
                    { // Begin if
                        defenderAgents.Add(agent); // Додаємо у список defender'ів
                    } // End if
                } // End for

                // 8) Призначаємо AI цілі, щоб вони гарантовано почали бій без Formation. // Пояснюємо етап
                if (attackerAgents.Count > 0 && defenderAgents.Count > 0) // Перевіряємо що обидві сторони мають агентів
                { // Begin if
                    for (int i = 0; i < attackerAgents.Count; i++) // Для кожного attacker агента
                    { // Begin for
                        Agent a = attackerAgents[i]; // Беремо attacker агента
                        Agent d = defenderAgents[i % defenderAgents.Count]; // Підбираємо defender агента (паруємо по індексу)
                        a.SetTargetAgent(d); // Кажемо AI: атакуй цього агента
                        a.SetAutomaticTargetSelection(true); // Дозволяємо авто-вибір цілей, якщо target загине
                        a.ForceAiBehaviorSelection(); // Просимо AI одразу перерахувати поведінку (щоб не чекати)
                    } // End for

                    for (int i = 0; i < defenderAgents.Count; i++) // Для кожного defender агента
                    { // Begin for
                        Agent d = defenderAgents[i]; // Беремо defender агента
                        Agent a = attackerAgents[i % attackerAgents.Count]; // Підбираємо attacker агента
                        d.SetTargetAgent(a); // Кажемо AI: атакуй цього агента
                        d.SetAutomaticTargetSelection(true); // Дозволяємо авто-таргетинг
                        d.ForceAiBehaviorSelection(); // Перераховуємо поведінку
                    } // End for
                } // End if

                UiFeedback.ShowMessageDeferred( // Показуємо підтвердження успіху
                    "MP test: spawned 2v2 AI infantry (no Formation). Fighting..."); // Текст повідомлення
            } // Завершуємо блок методу

            private static Agent SpawnAiInfantry( // Helper для спавну одного AI агента (повертаємо Agent)
                Mission mission, // Поточна місія (куди спавнимо)
                Team team, // Команда агента
                BasicCharacterObject troop, // Тип юніта/персонажа
                Vec3 position, // Позиція спавну (вже порахована caller'ом)
                Vec3 lookAt) // Точка, куди агент має “дивитись” при спавні
            { // Begin helper
                // Створюємо "origin" агента: звідки він взявся (потрібно для частини AI/логіки). // Пояснюємо навіщо
                var origin = new BasicBattleAgentOrigin(troop); // Створюємо простий origin на базі troop'а

                // Готуємо напрямок (Vec2) в бік ворога. // Пояснюємо етап
                Vec3 toEnemy3 = lookAt - position; // Вектор від нашої позиції до ворога
                Vec2 toEnemy2 = new Vec2(toEnemy3.x, toEnemy3.y); // Перетворюємо вектор у 2D (на площині X/Y)

                if (toEnemy2.LengthSquared < 0.001f) // Якщо напрямок майже нульовий (дуже близько) — ставимо дефолт
                { // Begin if
                    toEnemy2 = new Vec2(1f, 0f); // Дефолт: дивимось по X
                } // End if

                toEnemy2.Normalize(); // Нормалізуємо напрямок, бо InitialDirection очікує unit vector

                // Будуємо AgentBuildData — опис того, як створити агента. // Пояснюємо етап
                AgentBuildData buildData = new AgentBuildData(troop); // Створюємо build data на основі troop'а
                buildData.Team(team); // Прив'язуємо агента до команди
                buildData.Controller(AgentControllerType.AI); // Робимо агента керованим AI
                buildData.TroopOrigin(origin); // Встановлюємо troop origin (потрібно для частини логіки/статистики)
                buildData.InitialPosition(in position); // Встановлюємо стартову позицію (in згідно API в 1.3.14)
                buildData.InitialDirection(in toEnemy2); // Встановлюємо стартовий напрямок (in згідно API в 1.3.14)
                buildData.SpawnsIntoOwnFormation(false); // НЕ призначаємо формацію автоматично (у dump був краш на Formation.set)
                buildData.SpawnsUsingOwnTroopClass(false); // Також вимикаємо авто-вибір формації по troop class (щоб не було implicit Formation.set)

                // Спавнимо агента в місію. // Пояснюємо етап
                Agent agent = mission.SpawnAgent(buildData, spawnFromAgentVisuals: false); // Spawn agent і отримуємо інстанс

                if (agent != null) // Якщо агент створений
                { // Begin if
                    agent.SetFiringOrder(FiringOrder.RangedWeaponUsageOrderEnum.FireAtWill); // Дозволяємо стріляти, якщо є дальня зброя
                    agent.SetAutomaticTargetSelection(true); // Вмикаємо авто-вибір цілі для AI
                } // End if

                return agent; // Повертаємо агента, щоб caller міг призначити цілі
            } // End helper

            private static BasicCharacterObject TryPickInfantryTroopSafe() // Best-effort: шукаємо будь-який infantry troop
            { // Begin helper
                try // Обгортаємо пошук, бо object manager може бути не готовий у дивних контекстах
                { // Begin try
                    // 1) Спроба №1: взяти хардкодну інфантерію з Native. // Пояснюємо підхід
                    // Якщо StringId не існує — GetObject поверне null. // Пояснюємо поведінку
                    BasicCharacterObject hardcoded = MBObjectManager.Instance.GetObject<BasicCharacterObject>("imperial_infantryman"); // Найчастіший ID у ванілі

                    if (hardcoded != null && !hardcoded.IsMounted) // Перевіряємо що troop існує і він не верховий
                    { // Begin if
                        return hardcoded; // Повертаємо знайдений troop
                    } // End if

                    // 2) Спроба №2: знайти інфантерію в ростері партії гравця. // Пояснюємо fallback
                    foreach (var element in MobileParty.MainParty.MemberRoster.GetTroopRoster()) // Проходимо по ростеру партії
                    { // Begin foreach
                        if (element.Character == null) // Якщо Character відсутній — пропускаємо
                        { // Begin if
                            continue; // Пропуск
                        } // End if

                        if (element.Number <= 0) // Якщо таких юнітів 0 — пропускаємо
                        { // Begin if
                            continue; // Пропуск
                        } // End if

                        // Беремо як BasicCharacterObject (CharacterObject наслідує BasicCharacterObject). // Пояснюємо cast
                        BasicCharacterObject candidate = element.Character; // Кандидат troop

                        if (candidate == null) // Захист від несподіваного null
                        { // Begin if
                            continue; // Пропуск
                        } // End if

                        if (candidate.IsMounted) // Якщо це кавалерія — пропускаємо (нам потрібна інфантерія)
                        { // Begin if
                            continue; // Пропуск
                        } // End if

                        if (candidate.IsHero) // Якщо це герой — пропускаємо (для PoC краще звичайні юніти)
                        { // Begin if
                            continue; // Пропуск
                        } // End if

                        return candidate; // Повертаємо перший валідний infantry troop
                    } // End foreach

                    // 3) Спроба №3: останній fallback — головний герой. // Пояснюємо fallback
                    return Hero.MainHero != null ? Hero.MainHero.CharacterObject : null; // Повертаємо героя або null
                } // End try
                catch (Exception ex) // Якщо щось пішло не так — логуємо і повертаємо null
                { // Begin catch
                    ModLogger.Error("Failed to pick infantry troop for MP test mission.", ex); // Логуємо проблему
                    return null; // Повертаємо null (caller покаже повідомлення)
                } // End catch
            } // End helper
        } // End MissionLogic

        private static void InvokeGameNetworkStaticVoidMethodByName( // Helper: викликаємо static void метод GameNetwork по імені
            string methodName, // Ім'я методу (наприклад "InitializeServerSide")
            object[] args) // Масив аргументів (або null, якщо метод без параметрів)
        { // Begin helper
            // Беремо тип GameNetwork. // Пояснюємо крок
            Type gameNetworkType = typeof(GameNetwork); // GameNetwork — статичний клас в TaleWorlds.MountAndBlade

            // Шукаємо метод по імені серед public/non-public static методів. // Пояснюємо параметри пошуку
            MethodInfo method = gameNetworkType.GetMethod( // Використовуємо reflection GetMethod
                methodName, // Ім'я методу
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static); // Шукаємо static методи незалежно від access modifier

            if (method == null) // Якщо метод не знайдено
            { // Begin if
                // Кидаємо виняток, щоб caller міг показати помилку/зупинити PoC. // Пояснюємо чому throw
                throw new MissingMethodException( // Викидаємо виняток з поясненням
                    gameNetworkType.FullName, // Додаємо повне ім'я типу
                    methodName); // Додаємо ім'я відсутнього методу
            } // End if

            // Викликаємо метод. // Пояснюємо крок
            try // Обгортаємо, щоб розгорнути TargetInvocationException
            { // Begin try
                method.Invoke( // Викликаємо через reflection
                    obj: null, // null, бо метод static
                    parameters: args); // Передаємо аргументи (або null)
            } // End try
            catch (TargetInvocationException tie) // Reflection загортає винятки в TargetInvocationException
            { // Begin catch
                // Кидаємо внутрішній виняток, щоб у нас був реальний stack trace та повідомлення. // Пояснюємо чому unwrap
                throw tie.InnerException ?? tie; // Якщо InnerException є — кидаємо його
            } // End catch
        } // End helper

        private static object InvokeGameNetworkStaticMethodByName( // Helper: викликаємо static метод GameNetwork по імені (з return value)
            string methodName, // Ім'я методу (наприклад "AddNewPlayerOnServer")
            object[] args) // Масив аргументів (або null)
        { // Begin helper
            Type gameNetworkType = typeof(GameNetwork); // Тип GameNetwork (статичний клас)

            MethodInfo method = gameNetworkType.GetMethod( // Шукаємо метод reflection'ом
                methodName, // Ім'я методу
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static); // Public/NonPublic static

            if (method == null) // Якщо метод не знайдено
            { // Begin if
                throw new MissingMethodException( // Кидаємо виняток, щоб caller знав що саме відсутнє
                    gameNetworkType.FullName, // Повне ім'я типу
                    methodName); // Ім'я методу
            } // End if

            try // Обгортаємо, щоб unwrap'нути TargetInvocationException
            { // Begin try
                return method.Invoke( // Викликаємо метод
                    obj: null, // static → obj=null
                    parameters: args); // Аргументи
            } // End try
            catch (TargetInvocationException tie) // Якщо реальний виняток загорнуто
            { // Begin catch
                throw tie.InnerException ?? tie; // Кидаємо InnerException, щоб бачити реальний stack trace
            } // End catch
        } // End helper

        private static void TryInvokeGameNetworkStaticVoidMethodByName( // Best-effort: виклик методу, який може бути відсутній
            string methodName, // Ім'я методу (наприклад "InitializeCompressionInfos")
            object[] args) // Аргументи (або null)
        { // Begin helper
            try // Пробуємо викликати
            { // Begin try
                InvokeGameNetworkStaticVoidMethodByName(methodName, args); // Викликаємо основний helper
            } // End try
            catch (MissingMethodException ex) // Якщо методу немає — просто логуємо і продовжуємо
            { // Begin catch
                ModLogger.Info("GameNetwork method missing (ignored): " + methodName + " | " + ex.Message); // Лог для дебагу
            } // End catch
            catch (Exception ex) // Якщо метод існує, але падає в цьому контексті — теж ігноруємо (best-effort)
            { // Begin catch
                ModLogger.Info("GameNetwork method failed (ignored): " + methodName + " | " + ex.Message); // Логуємо причину, але не валимо PoC
            } // End catch
        } // End helper

        private static void EnsureServerPeerExists() // Створюємо server peer (host) якщо його ще немає
        { // Begin helper
            try // Best-effort: не валимо PoC, якщо peer створити не вийшло
            { // Begin try
                if (GameNetwork.MyPeer != null) // Якщо MyPeer вже існує — нічого не робимо
                { // Begin if
                    return; // Вихід
                } // End if

                // PlayerId тип лежить у TaleWorlds.PlayerServices, але ми не референсимо його compile-time. // Пояснюємо чому reflection
                Type playerIdType = Type.GetType( // Пробуємо отримати тип через assembly-qualified name
                    "TaleWorlds.PlayerServices.PlayerId, TaleWorlds.PlayerServices"); // Повне ім'я типу + збірка

                if (playerIdType == null) // Якщо Type.GetType не спрацював (assembly ще не завантажений)
                { // Begin if
                    Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies(); // Беремо всі завантажені assembly

                    for (int i = 0; i < loaded.Length; i++) // Перебираємо assembly
                    { // Begin for
                        Assembly asm = loaded[i]; // Поточна assembly
                        string name = asm.GetName().Name; // Ім'я assembly без версії

                        if (!string.Equals(name, "TaleWorlds.PlayerServices", StringComparison.Ordinal)) // Шукаємо PlayerServices
                        { // Begin if
                            continue; // Це не та збірка
                        } // End if

                        playerIdType = asm.GetType("TaleWorlds.PlayerServices.PlayerId"); // Пробуємо взяти тип з assembly
                        break; // Виходимо з циклу (знайшли або ні)
                    } // End for
                } // End if

                if (playerIdType == null) // Якщо ми так і не змогли знайти PlayerId
                { // Begin if
                    ModLogger.Info("MP test: PlayerId type not found; cannot create server peer."); // Логуємо причину
                    return; // Вихід (не валимо PoC)
                } // End if

                // Беремо PlayerId.Empty як стабільний placeholder id. // Пояснюємо вибір
                PropertyInfo emptyProp = playerIdType.GetProperty( // Отримуємо property Empty
                    "Empty", // Назва property
                    BindingFlags.Public | BindingFlags.Static); // Static public property

                if (emptyProp == null) // Якщо property не знайдена
                { // Begin if
                    ModLogger.Info("MP test: PlayerId.Empty not found; cannot create server peer."); // Логуємо
                    return; // Вихід
                } // End if

                object playerIdEmpty = emptyProp.GetValue(null, null); // Отримуємо значення Empty (boxed struct)

                // Створюємо PlayerConnectionInfo через reflection, передаючи PlayerId.Empty. // Пояснюємо як формуємо дані гравця
                object pci = Activator.CreateInstance( // Створюємо інстанс
                    typeof(PlayerConnectionInfo), // Тип PlayerConnectionInfo з TaleWorlds.MountAndBlade
                    args: new object[] { playerIdEmpty }); // Єдиний параметр ctor: PlayerId

                // Заповнюємо мінімальні поля: ім'я та session key. // Пояснюємо навіщо
                typeof(PlayerConnectionInfo).GetProperty("Name").SetValue(pci, "CoopHost", null); // Ім'я хоста
                typeof(PlayerConnectionInfo).GetProperty("SessionKey").SetValue(pci, 0, null); // SessionKey=0 як placeholder

                // Викликаємо AddNewPlayerOnServer(..., serverPeer=true, isAdmin=true). // Пояснюємо основний крок
                object communicator = InvokeGameNetworkStaticMethodByName( // Викликаємо метод через reflection
                    methodName: "AddNewPlayerOnServer", // Назва методу
                    args: new object[] { pci, true, true }); // Аргументи: PlayerConnectionInfo, serverPeer, isAdmin

                // Повернене значення реалізує ICommunicator; у нас це має бути NetworkCommunicator. // Пояснюємо cast
                NetworkCommunicator peer = communicator as NetworkCommunicator; // Cast (якщо не той тип — буде null)

                if (peer == null) // Якщо cast не вдався
                { // Begin if
                    ModLogger.Info("MP test: AddNewPlayerOnServer returned non-NetworkCommunicator."); // Логуємо
                    return; // Вихід
                } // End if

                // Додаємо peer у список GameNetwork (на випадок якщо AddNewPlayerOnServer не зробив цього автоматично). // Пояснюємо safety
                TryInvokeGameNetworkStaticVoidMethodByName( // Best-effort: не валимо PoC, якщо вже додано
                    methodName: "AddNetworkPeer", // Додаємо peer
                    args: new object[] { peer }); // Аргумент: peer

                // Явно виставляємо MyPeer, щоб GameNetwork точно мав "свого" peer'а. // Пояснюємо навіщо
                PropertyInfo myPeerProp = typeof(GameNetwork).GetProperty( // Беремо property MyPeer
                    "MyPeer", // Ім'я
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static); // Static property

                if (myPeerProp != null && myPeerProp.CanWrite) // Якщо property існує і має setter
                { // Begin if
                    myPeerProp.SetValue(null, peer, null); // Встановлюємо MyPeer = peer
                } // End if

                UiFeedback.ShowMessageDeferred( // Показуємо короткий результат
                    "MP init: created server peer. PeerCount=" + GameNetwork.NetworkPeerCount + "."); // Текст повідомлення
            } // End try
            catch (Exception ex) // Якщо щось пішло не так — логуємо, але не валимо PoC
            { // Begin catch
                ModLogger.Info("MP test: failed to create server peer (ignored): " + ex.Message); // Логуємо
            } // End catch
        } // End helper

        private static void EnsureGameNetworkInitialized() // Гарантуємо, що GameNetwork.Initialize(handler) викликано один раз
        { // Begin helper
            if (_gameNetworkHandler != null) // Якщо вже ініціалізували — виходимо
            { // Begin if
                return; // Нічого не робимо вдруге
            } // End if

            _gameNetworkHandler = new CoopGameNetworkHandler(); // Створюємо наш мінімальний handler

            // Викликаємо GameNetwork.Initialize(handler) через reflection, // Пояснюємо навіщо reflection
            // щоб не залежати від того, що reference DLL містить цей метод. // Пояснюємо мету
            InvokeGameNetworkStaticVoidMethodByName( // Викликаємо
                methodName: "Initialize", // Ім'я методу
                args: new object[] { _gameNetworkHandler }); // Передаємо handler як аргумент
        } // End helper

        private sealed class CoopGameNetworkHandler : IGameNetworkHandler // Мінімальний handler для GameNetwork у SP контексті
        { // Begin class
            public void OnInitialize() // Викликається, коли GameNetwork ініціалізується
            { // Begin method
                ModLogger.Info("GameNetwork handler: OnInitialize()"); // Логуємо для дебагу
            } // End method

            public void OnStartMultiplayer() // Викликається при старті мультиплеєра
            { // Begin method
                ModLogger.Info("GameNetwork handler: OnStartMultiplayer()"); // Логуємо
            } // End method

            public void OnEndMultiplayer() // Викликається при завершенні мультиплеєра
            { // Begin method
                ModLogger.Info("GameNetwork handler: OnEndMultiplayer()"); // Логуємо
            } // End method

            public void OnStartReplay() // Не використовуємо в PoC
            { // Begin method
                ModLogger.Info("GameNetwork handler: OnStartReplay()"); // Логуємо
            } // End method

            public void OnEndReplay() // Не використовуємо в PoC
            { // Begin method
                ModLogger.Info("GameNetwork handler: OnEndReplay()"); // Логуємо
            } // End method

            public void OnDisconnectedFromServer() // Коли клієнт від'єднався від сервера
            { // Begin method
                ModLogger.Info("GameNetwork handler: OnDisconnectedFromServer()"); // Логуємо
            } // End method

            public void OnPlayerConnectedToServer(NetworkCommunicator peer) // Коли peer підключився до сервера
            { // Begin method
                ModLogger.Info("GameNetwork handler: OnPlayerConnectedToServer(peer=" + (peer != null ? peer.ToString() : "null") + ")"); // Логуємо
            } // End method

            public void OnNewPlayerConnect(PlayerConnectionInfo playerConnectionInfo, NetworkCommunicator networkPeer) // Новий гравець підключився
            { // Begin method
                ModLogger.Info("GameNetwork handler: OnNewPlayerConnect(peer=" + (networkPeer != null ? networkPeer.ToString() : "null") + ")"); // Логуємо
            } // End method

            public void OnPlayerDisconnectedFromServer(NetworkCommunicator peer) // Коли peer від'єднався від сервера
            { // Begin method
                ModLogger.Info("GameNetwork handler: OnPlayerDisconnectedFromServer(peer=" + (peer != null ? peer.ToString() : "null") + ")"); // Логуємо
            } // End method

            public void OnHandleConsoleCommand(string command) // Передача консольних команд (не використовуємо)
            { // Begin method
                // Не логуємо кожну команду, щоб не спамити. // Пояснюємо
            } // End method
        } // End class
    } // End class
} // End namespace

