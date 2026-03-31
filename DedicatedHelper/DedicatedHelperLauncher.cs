using System; // Exception, Environment, IntPtr
using System.Diagnostics; // Process, ProcessStartInfo
using System.IO; // Path, File, Directory
using System.Management; // ManagementObjectSearcher, Win32_Process
using System.Threading; // Thread.Sleep для очікування дочірнього процесу
using CoopSpectator.Infrastructure; // ModLogger, UiFeedback, CoopGameModeIds

namespace CoopSpectator.DedicatedHelper // Запуск Dedicated Helper (офіційний дедик-сервер) з кампанії
{
    /// <summary>
    /// Запуск процесу Mount &amp; Blade II Dedicated Server з кампанії.
    /// Токен: Documents\Mount &amp; Blade II Bannerlord\Tokens (або /dedicatedcustomserverauthtoken).
    /// </summary>
    public static class DedicatedHelperLauncher
    {
        private const int DefaultPort = 7210; // Офіційний UDP-порт дедик-сервера за замовчуванням
        private const string DedicatedServerFolderName = "Mount & Blade II Dedicated Server";
        private const string ServerBinRelativePath = @"bin\Win64_Shipping_Server";
        /// <summary>Єдиний exe для custom dedicated server у стандартній інсталі: DedicatedCustomServer.Starter.exe (core exe в цій папці немає).</summary>
        private static readonly string[] ExeCandidates = new[] { "DedicatedCustomServer.Starter.exe" };
        /// <summary>Папка Tokens у Documents — варіант з "and" (гра часто створює саме цю).</summary>
        private const string TokensSubFolder = @"Mount and Blade II Bannerlord\Tokens";
        /// <summary>Варіант з "&" — іноді з’являється в OneDrive/провіднику; dedicated може очікувати той чи інший.</summary>
        private const string TokensSubFolderAmpersand = @"Mount & Blade II Bannerlord\Tokens";
        /// <summary>Ім'я файлу токена, який гра створює після customserver.gettoken.</summary>
        private const string OfficialTokenFileName = "DedicatedCustomServerAuthToken.txt";
        /// <summary>Ім'я стартового конфігу для автоматичного start_game (файл у Dedicated Server Modules\Native).</summary>
        private const string StartupConfigFileName = "ds_config_coop_start.txt";
        /// <summary>Пароль адміна для Dashboard (localhost:7210), щоб увійти в Terminal і дослідити API консольних команд.</summary>
        private const string DashboardAdminPassword = "coopforever";
        /// <summary>Пароль для HTTP-логіну в web panel (DedicatedServerCommands / WebPanelAuth).</summary>
        public static string GetDashboardAdminPassword() => DashboardAdminPassword;
        /// <summary>Якщо false — не передаємо конфіг при запуску (як Steam: "Command file is null"), щоб сервер не від'єднувався від Diamond. start_game тоді вводять вручну в консолі сервера.</summary>
        private const bool UseStartupConfig = false;
        /// <summary>Явний список модулів для vanilla-like dedicated launch. Без цього сервер може піднятися без Multiplayer/DedicatedCustomServerHelper і від'єднатися від custom battle server manager.</summary>
        private const string ModulesArg = "_MODULES_*Native*Multiplayer*DedicatedCustomServerHelper*_MODULES_";
        /// <summary>
        /// Exact campaign-scene bootstrap module set for modded dedicated flow.
        /// `Sandbox` and `SandBoxCore` are included deliberately so the dedicated
        /// runtime can see staged `sp_battle_scenes.xml` and `battle_terrain_*`
        /// scene assets during exact-scene experiments.
        /// </summary>
        private const string ExactCampaignSceneModulesArg = "_MODULES_*Native*SandBoxCore*Sandbox*Multiplayer*CoopSpectatorDedicated*_MODULES_";
        /// <summary>ТЗ C2: офіційний хвіст — блок _MODULES_ та /dedicatedcustomserver ... /playerhosteddedicatedserver для guard/normalization.</summary>
        private const string OfficialModulesTail = "_MODULES_*Native*Multiplayer*_MODULES_";
        private const string PlayerHostedSuffix = "/playerhosteddedicatedserver";
        private const string DedicatedCustomServerPrefix = " /dedicatedcustomserver ";
        /// <summary>true = не передаємо наш _MODULES_/конфіг (Starter сам додає свої args — тоді AliveMessage ок). Якщо false — передаємо повний набір (раніше давало Disconnected).</summary>
        private const bool SteamLikeLaunch = true;
        /// <summary>Тимчасовий debug для ТЗ "дослідити завантаження кастомного модуля". true = додати _MODULES_ з CoopSpectatorDedicated до safe args (очікується Disconnected: Starter перезапише). Не для production.</summary>
        private const bool DebugTryAddCoopSpectatorDedicatedToModules = false;
        /// <summary>Окремий debug launch для modded dedicated (ТЗ 1): Starter з явним exact-bootstrap _MODULES_, без SteamLikeLaunch. Перевірка: чи взагалі завантажується модуль і чи підхоплюються staged official exact-scene modules. Не для production.</summary>
        private const bool DebugModdedDedicatedLaunch = false;
        /// <summary>ТЗ B1: 0=PlainOfficialArgs, 1=ModdedMixedArgs, 2=ModdedOnly, 3=ModdedOnlyWithToken, -1=вимкнено (звичайний flow). Якщо 0..3 — запуск з відповідним preset і лог для таблиці.</summary>
        private const int LaunchPresetB1 = -1;
        /// <summary>ТЗ C1: еталонний робочий modded launch — ModdedOfficialNoTokenArg. Exact-bootstrap _MODULES_, /LogOutputPath, official tail /dedicatedcustomserver... Без /dedicatedcustomserverauthtoken (токен лише з оточення/файлів).</summary>
        private const bool ModdedOfficialNoTokenArg = false;
        /// <summary>ТЗ C3: production-like запуск з кастомним server module. true = coop.dedicated_start використовує modded official flow з exact-bootstrap `_MODULES_` (Sandbox/SandBoxCore/Multiplayer/CoopSpectatorDedicated), без token arg, токен з Documents.</summary>
        private const bool UseModdedDedicatedOfficialFlow = true;
        /// <summary>При SteamLikeLaunch: true = додати тільки токен+порт. ВІДОМО ЛАМАЄ manager-конект (Disconnected). Залишати false.</summary>
        private const bool AddTokenAndPortOnly = false;
        /// <summary>Передавати /port (ізоляція: не ламає manager).</summary>
        private const bool AddPortOnly = true;
        /// <summary>НЕ вмикати: передача токена аргументом ламає manager (Disconnected). Токен лише з Documents.</summary>
        private const bool AddTokenOnly = false;
        /// <summary>Конфіг + /dedicatedcustomserverconfigfile — авто start_game (ізоляція: не ламає). Разом з AddPortOnly працює.</summary>
        private const bool AddConfigFileOnly = true;
        /// <summary>У modded official flow: true = писати і передавати startup config (mp_tdm_map_001, ZZZ_COOP_TEST_7210, coopforever), щоб сервер був у Custom Server List з нашим ім'ям/паролем/сценою. Без token arg.</summary>
        private const bool UseStartupConfigInModdedOfficialFlow = true;
        /// <summary>Для контрольного тесту: ім'я сервера в списку (однозначно наше).</summary>
        private const string TestListedServerName = "ZZZ_COOP_TEST_7210";
        /// <summary>Для контрольного тесту: сцена TDM (listed flow safer за mp_skirmish_spawn_test).</summary>
        private const string TestListedScene = "mp_tdm_map_001";
        /// <summary>Етап 3.3: true = у listed-test конфігу використовувати GameType TdmClone (і add_map_to_usable_maps ... TdmClone), щоб на дедику працювали наші mission behaviors з логуванням. На дедику потрібно CleanModuleLoadOnly = false.</summary>
        private const bool UseTdmCloneForListedTest = ExperimentalFeatures.EnableTdmCloneExperiment;

        /// <summary>Процес дедик-сервера, запущеного з моду (для відправки команд через stdin, якщо сервер їх читає).</summary>
        private static Process _dedicatedProcess;

        /// <summary>Відправити рядок у stdin процесу дедик-сервера (якщо він був запущений з моду і ще працює). Використовується для start_mission / end_mission. Повертає true, якщо рядок записано.</summary>
        public static bool TrySendConsoleLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            Process p = _dedicatedProcess;
            if (p == null || p.HasExited) return false;
            try
            {
                LogDedicatedProcessInfo(p, "TrySendConsoleLine (sending stdin)");
                if (p.StandardInput == null)
                {
                    ModLogger.Info("DedicatedHelper: StandardInput is null for PID=" + p.Id + " (process not started with RedirectStandardInput?).");
                    return false;
                }
                p.StandardInput.WriteLine(line.Trim());
                p.StandardInput.Flush();
                ModLogger.Info("DedicatedHelper: sent to stdin: \"" + line.Trim() + "\"");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedHelper: TrySendConsoleLine failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>Логує PID, ProcessName і (якщо є) MainModule.FileName процесу — для зіставлення з консоллю в Task Manager.</summary>
        private static void LogDedicatedProcessInfo(Process p, string context)
        {
            if (p == null) return;
            try
            {
                string name = p.ProcessName ?? "";
                string mainModule = "";
                try { mainModule = p.MainModule != null && p.MainModule.FileName != null ? p.MainModule.FileName : ""; } catch (Exception) { mainModule = "(no access)"; }
                ModLogger.Info("DedicatedHelper [" + context + "] PID=" + p.Id + " ProcessName=" + name + " MainModule.FileName=" + mainModule);
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedHelper [LogDedicatedProcessInfo] PID=" + p.Id + " error: " + ex.Message);
            }
        }

        /// <summary>Чи є живий процес дедик-сервера, запущений з моду (для пріоритету stdin у SendCommand).</summary>
        public static bool HasDedicatedProcess()
        {
            Process p = _dedicatedProcess;
            return p != null && !p.HasExited;
        }

        /// <summary>Повертає шлях до папки Tokens для показу користувачу (перший існуючий кандидат або MyDocuments).</summary>
        public static string GetTokensFolderPath()
        {
            foreach (string folder in GetTokenFolderCandidates())
            {
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    return folder;
            }
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return string.IsNullOrEmpty(docs) ? null : Path.Combine(docs, TokensSubFolder);
        }

        /// <summary>Кандидати для папки Tokens: обидва варіанти назви (and / &) у MyDocuments і OneDrive.</summary>
        private static System.Collections.Generic.IEnumerable<string> GetTokenFolderCandidates()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrEmpty(docs))
            {
                yield return Path.Combine(docs, TokensSubFolder);
                yield return Path.Combine(docs, TokensSubFolderAmpersand);
            }
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(user))
            {
                yield return Path.Combine(user, "OneDrive", "Documents", TokensSubFolder);
                yield return Path.Combine(user, "OneDrive", "Documents", TokensSubFolderAmpersand);
                yield return Path.Combine(user, "OneDrive", "Документи", TokensSubFolder);
                yield return Path.Combine(user, "OneDrive", "Документи", TokensSubFolderAmpersand);
            }
        }

        /// <summary>Шукає токен у всіх кандидатах папок. Повертає true і заповнює folderWhereFound, якщо передано.</summary>
        public static bool TryReadTokenFromFolder(out string tokenContent, out string folderWhereFound)
        {
            tokenContent = null;
            folderWhereFound = null;
            foreach (string folder in GetTokenFolderCandidates())
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
                if (TryReadTokenFromFolderInternal(folder, out tokenContent))
                {
                    folderWhereFound = folder;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Шукає токен у всіх кандидатах папок (перегруз без folderWhereFound).</summary>
        public static bool TryReadTokenFromFolder(out string tokenContent)
        {
            string folder;
            return TryReadTokenFromFolder(out tokenContent, out folder);
        }

        private static bool TryReadTokenFromFolderInternal(string folder, out string tokenContent)
        {
            tokenContent = null;
            try
            {
                string officialPath = Path.Combine(folder, OfficialTokenFileName);
                if (File.Exists(officialPath))
                {
                    tokenContent = File.ReadAllText(officialPath).Trim();
                    if (!string.IsNullOrEmpty(tokenContent))
                    {
                        ModLogger.Info("DedicatedHelper: token read from " + OfficialTokenFileName);
                        return true;
                    }
                }
                string[] files = Directory.GetFiles(folder);
                for (int i = 0; i < files.Length; i++)
                {
                    string path = files[i];
                    if (path == null) continue;
                    string name = Path.GetFileName(path);
                    if (string.IsNullOrEmpty(name) || name.StartsWith(".", StringComparison.Ordinal)) continue;
                    try
                    {
                        tokenContent = File.ReadAllText(path).Trim();
                        if (!string.IsNullOrEmpty(tokenContent))
                        {
                            ModLogger.Info("DedicatedHelper: token read from " + name);
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Info("DedicatedHelper: skip file " + name + " — " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedHelper: failed to read folder " + folder + " — " + ex.Message);
            }
            return false;
        }

        /// <summary>Повертає "іншу" папку Tokens (and ↔ &), якщо така є в тому ж базовому шляху.</summary>
        private static string GetOtherTokenFolderPath(string tokenFolderPath)
        {
            if (string.IsNullOrEmpty(tokenFolderPath)) return null;
            string other = tokenFolderPath.IndexOf("Mount and Blade II Bannerlord", StringComparison.OrdinalIgnoreCase) >= 0
                ? tokenFolderPath.Replace("Mount and Blade II Bannerlord", "Mount & Blade II Bannerlord")
                : tokenFolderPath.Replace("Mount & Blade II Bannerlord", "Mount and Blade II Bannerlord");
            return other == tokenFolderPath ? null : other;
        }

        /// <summary>Token doctor: якщо токен знайдено лише в одній папці, а інша (and/&) існує без токена — підказати скопіювати.</summary>
        public static void LogTokenDoctorMessage(string tokenFoundInFolder)
        {
            if (string.IsNullOrEmpty(tokenFoundInFolder)) return;
            string other = GetOtherTokenFolderPath(tokenFoundInFolder);
            if (string.IsNullOrEmpty(other) || !Directory.Exists(other)) return;
            string officialInOther = Path.Combine(other, OfficialTokenFileName);
            if (File.Exists(officialInOther)) return;
            ModLogger.Info("DedicatedHelper [Token doctor]: Token found in \"" + tokenFoundInFolder + "\". Dedicated server may expect \"" + other + "\" — copy DedicatedCustomServerAuthToken.txt there if server fails to auth.");
        }

        /// <summary>Відкриває папку Tokens у провіднику (для UX при відсутності токена). Викликає Token doctor якщо токен знайдено в одній папці.</summary>
        public static void OpenTokensFolderInExplorer()
        {
            string folder = GetTokensFolderPath();
            if (string.IsNullOrEmpty(folder)) return;
            string token;
            string folderWhere;
            if (TryReadTokenFromFolder(out token, out folderWhere) && !string.IsNullOrEmpty(folderWhere))
                LogTokenDoctorMessage(folderWhere);
            try
            {
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "\"" + folder + "\"", UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ModLogger.Error("DedicatedHelper: failed to open Tokens folder.", ex);
            }
        }

        /// <summary>Шукає exe дедик-сервера: поряд з грою (Steam common) або через змінну середовища. Перебирає кандидатів (core exe, потім Starter).</summary>
        public static string TryFindDedicatedServerExePath()
        {
            string binDir = null;

            // 1) Змінна середовища (корінь Dedicated Server або повний шлях до exe)
            string envPath = Environment.GetEnvironmentVariable("BANNERLORD_DEDICATED_SERVER_PATH");
            if (!string.IsNullOrWhiteSpace(envPath))
            {
                if (File.Exists(envPath)) return envPath;
                binDir = Path.Combine(envPath, ServerBinRelativePath);
            }

            // 2) Поряд з грою: ...\Steam\steamapps\common\Mount & Blade II Bannerlord -> ...\Mount & Blade II Dedicated Server
            if (string.IsNullOrEmpty(binDir))
            {
                string gameRoot = TryGetGameRootFromProcess();
                if (!string.IsNullOrEmpty(gameRoot))
                {
                    string parent = Path.GetDirectoryName(gameRoot);
                    if (!string.IsNullOrEmpty(parent))
                        binDir = Path.Combine(parent, DedicatedServerFolderName, ServerBinRelativePath);
                }
            }

            if (string.IsNullOrEmpty(binDir) || !Directory.Exists(binDir)) return null;

            for (int i = 0; i < ExeCandidates.Length; i++)
            {
                string exe = Path.Combine(binDir, ExeCandidates[i]);
                if (File.Exists(exe)) return exe;
            }
            return null;
        }

        private static string TryGetGameRootFromProcess()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrEmpty(baseDir)) return null;
                // Типовий шлях: ...\bin\Win64_Shipping_Client -> корінь гри на 2 рівні вгору
                string root = Path.GetFullPath(Path.Combine(baseDir, "..", ".."));
                if (Directory.Exists(root)) return root;
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedHelper: could not resolve game root: " + ex.Message);
            }
            return null;
        }

        /// <summary>Корінь інсталяції Dedicated Server (exe лежить у bin\Win64_Shipping_Server\).</summary>
        private static string GetDedicatedServerRootFromExe(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return null;
            try
            {
                string dir = Path.GetDirectoryName(exePath);  // ...\bin\Win64_Shipping_Server
                if (string.IsNullOrEmpty(dir)) return null;
                dir = Path.GetDirectoryName(dir);             // ...\bin
                if (string.IsNullOrEmpty(dir)) return null;
                dir = Path.GetDirectoryName(dir);             // корінь Dedicated Server
                return Directory.Exists(dir) ? dir : null;
            }
            catch { return null; }
        }

        /// <summary>Записує стартовий конфіг (start_game, ServerName) у Dedicated Server Modules\Native. Повертає ім'я файлу для /dedicatedcustomserverconfigfile або null.</summary>
        private static string TryWriteStartupConfig(string exePath)
        {
            string root = GetDedicatedServerRootFromExe(exePath);
            if (string.IsNullOrEmpty(root)) return null;
            string nativeDir = Path.Combine(root, "Modules", "Native");
            if (!Directory.Exists(nativeDir)) return null;
            string configPath = Path.Combine(nativeDir, StartupConfigFileName);
            try
            {
                // GameType у dedicated server config ПОВИНЕН бути одним з офіційних режимів:
                // Captain, TeamDeathmatch, Skirmish, FreeForAll, Duel або Siege.
                // (див. офіційну документацію hosting_server / GameType).
                // Наш кастомний режим TdmClone реєструється в модулі, але як GameType у конфігу
                // він не підтримується → "Cannot find game type: TdmClone".
                // Тому тут завжди ставимо валідний офіційний режим, наприклад TeamDeathmatch.
                string gameTypeId = "TeamDeathmatch";
                string content = "AdminPassword " + DashboardAdminPassword + Environment.NewLine
                    + "ServerName Coop Spectator" + Environment.NewLine
                    + "GameType " + gameTypeId + Environment.NewLine
                    + "start_game" + Environment.NewLine;
                File.WriteAllText(configPath, content);
                ModLogger.Info("DedicatedHelper: wrote startup config to " + configPath + " [config GameTypeId=" + gameTypeId + "]");
                return StartupConfigFileName;
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedHelper: could not write config: " + ex.Message);
                return null;
            }
        }

        /// <summary>Записує стартовий конфіг для контрольного тесту listed flow: scene=mp_tdm_map_001, ServerName=ZZZ_COOP_TEST_7210, AdminPassword=coopforever, явний Map + add_map_to_usable_maps + start_game. UseTdmCloneForListedTest=true → GameType TdmClone (для Етапу 3.3 з нашими mission behaviors). Файл ds_config_coop_listed_test.txt у Modules\Native.</summary>
        private static string TryWriteStartupConfigForListedTest(string exePath)
        {
            string root = GetDedicatedServerRootFromExe(exePath);
            if (string.IsNullOrEmpty(root)) return null;
            string nativeDir = Path.Combine(root, "Modules", "Native");
            if (!Directory.Exists(nativeDir)) return null;
            const string testConfigFileName = "ds_config_coop_listed_test.txt";
            string configPath = Path.Combine(nativeDir, testConfigFileName);
            try
            {
                string gameTypeId = UseTdmCloneForListedTest ? CoopGameModeIds.TdmClone : "TeamDeathmatch";
                string content = "AdminPassword " + DashboardAdminPassword + Environment.NewLine
                    + "ServerName " + TestListedServerName + Environment.NewLine
                    + "GameType " + gameTypeId + Environment.NewLine
                    + "Map " + TestListedScene + Environment.NewLine
                    + "add_map_to_usable_maps " + TestListedScene + " " + gameTypeId + Environment.NewLine
                    + "start_game" + Environment.NewLine;
                File.WriteAllText(configPath, content);
                ModLogger.Info("DedicatedHelper: wrote listed-test startup config to " + configPath + " [scene=" + TestListedScene + " serverName=" + TestListedServerName + " gameType=" + gameTypeId + " mapLine=true]");
                return testConfigFileName;
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedHelper: could not write listed-test config: " + ex.Message);
                return null;
            }
        }

        /// <summary>Логує джерело startup state для діагностики (config applied, path, scene, gameType, serverName, adminPassword source, start_game sent via).</summary>
        private static void LogStartupState(bool configApplied, string configPath, string scene, string gameType, string serverName, string adminPasswordSource, string startGameSentVia)
        {
            ModLogger.Info("DedicatedHelper [startup] startup config applied = " + configApplied);
            ModLogger.Info("DedicatedHelper [startup] startup config path = " + (configPath ?? ""));
            ModLogger.Info("DedicatedHelper [startup] startup scene = " + (scene ?? ""));
            ModLogger.Info("DedicatedHelper [startup] startup gameType = " + (gameType ?? ""));
            ModLogger.Info("DedicatedHelper [startup] startup serverName = " + (serverName ?? ""));
            ModLogger.Info("DedicatedHelper [startup] startup adminPassword source = " + (adminPasswordSource ?? ""));
            ModLogger.Info("DedicatedHelper [startup] start_game sent via = " + (startGameSentVia ?? ""));
        }

        /// <summary>
        /// Запускає Dedicated Helper. Токен: з папки Tokens або переданий явно.
        /// Повертає повідомлення для консолі (успіх або текст помилки).
        /// </summary>
        public static string Start(string tokenOverride, int port)
        {
            if (port <= 0) port = DefaultPort;

            // Явні логи джерела startup state (для діагностики Steam-like / modded flow).
            ModLogger.Info("DedicatedHelper [startup] SteamLikeLaunch = " + SteamLikeLaunch);
            ModLogger.Info("DedicatedHelper [startup] AddConfigFileOnly = " + AddConfigFileOnly);
            ModLogger.Info("DedicatedHelper [startup] AddPortOnly = " + AddPortOnly);
            ModLogger.Info("DedicatedHelper [startup] AddTokenOnly = " + AddTokenOnly);
            ModLogger.Info("DedicatedHelper [startup] AddTokenAndPortOnly = " + AddTokenAndPortOnly);

            string token = tokenOverride;
            string tokenFolder = null;
            bool needToken = (!SteamLikeLaunch && !DebugModdedDedicatedLaunch && !UseModdedDedicatedOfficialFlow) || AddTokenAndPortOnly || AddTokenOnly;
            if (needToken && string.IsNullOrWhiteSpace(token) && !TryReadTokenFromFolder(out token, out tokenFolder))
            {
                string tokensPath = GetTokensFolderPath();
                string msg = "No token found. Multiplayer -> Console (ALT+~) -> customserver.gettoken. Folder: " + (tokensPath ?? "(unknown)");
                UiFeedback.ShowMessageDeferred(msg + " Run coop.dedicated_open_tokens to open folder.");
                ModLogger.Info("DedicatedHelper: " + msg);
                return "ERROR: " + msg + " Run coop.dedicated_open_tokens to open Tokens folder. Or: coop.dedicated_start [port] [token]";
            }
            if (string.IsNullOrWhiteSpace(token))
                TryReadTokenFromFolder(out token, out tokenFolder);
            if (!string.IsNullOrEmpty(tokenFolder))
                LogTokenDoctorMessage(tokenFolder);

            string exePath = TryFindDedicatedServerExePath();
            if (string.IsNullOrEmpty(exePath))
            {
                string msg = "Dedicated Server exe not found. Install 'Mount & Blade II: Dedicated Server' from Steam (Tools). Path: Steam\\steamapps\\common\\" + DedicatedServerFolderName + "\\" + ServerBinRelativePath + " (e.g. DedicatedCustomServer.Starter.exe)";
                UiFeedback.ShowMessageDeferred(msg);
                ModLogger.Info("DedicatedHelper: " + msg);
                return "ERROR: " + msg;
            }

            // WorkingDirectory = строго папка, де лежить exe (як при Steam-запуску).
            string workingDir = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(workingDir)) workingDir = Path.GetDirectoryName(Path.GetDirectoryName(exePath));

            // ТЗ C1: еталонний робочий modded launch — без token arg, з official tail.
            if (ModdedOfficialNoTokenArg)
            {
                string tempDir = Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath();
                string logOutputDir = Path.Combine(tempDir, "CoopSpectatorDedicated_logs");
                try { if (!Directory.Exists(logOutputDir)) Directory.CreateDirectory(logOutputDir); } catch (Exception) { }
                ModLogger.Info("DedicatedHelper [ModdedOfficialNoTokenArg] Using token from official environment/files, not from arg.");
                string c1Args = NormalizeDedicatedArguments(BuildArgumentsModdedOfficialNoTokenArg(port, logOutputDir, null));
                ModLogger.Info("DedicatedHelper [LaunchPlan] mode=ModdedOfficialNoTokenArg OurArgs=" + (string.IsNullOrEmpty(c1Args) ? "(empty)" : c1Args));
                ModLogger.Info("DedicatedHelper [LaunchPlan] ExpectedStarterAddsArgs=true ConfigInjectionMode=None");
                ModLogger.Info("DedicatedHelper [ModdedOfficialNoTokenArg] exe=" + exePath);
                ModLogger.Info("DedicatedHelper [ModdedOfficialNoTokenArg] workingDir=" + (workingDir ?? ""));
                ModLogger.Info("DedicatedHelper [ModdedOfficialNoTokenArg] exact command line=" + c1Args);
                var c1StartInfo = new ProcessStartInfo { FileName = exePath, WorkingDirectory = workingDir ?? "", UseShellExecute = false, CreateNoWindow = false, Arguments = c1Args, RedirectStandardInput = true };
                try
                {
                    Process p = Process.Start(c1StartInfo);
                    if (p == null) { ModLogger.Error("DedicatedHelper: Process.Start returned null.", null); return "ERROR: Failed to start process."; }
                    _dedicatedProcess = p;
                    ModLogger.Info("DedicatedHelper [ModdedOfficialNoTokenArg] Starter PID=" + p.Id);
                    LogDedicatedProcessInfo(p, "ModdedOfficialNoTokenArg after Start (Starter)");
                    LogProcessCommandLine(p.Id);
                    TrySwitchToChildProcessIfAny(p.Id);
                    Process current = _dedicatedProcess;
                    if (current != null && current.Id != p.Id)
                        ModLogger.Info("DedicatedHelper [ModdedOfficialNoTokenArg] Child PID=" + current.Id);
                    UiFeedback.ShowMessageDeferred("Dedicated started (ModdedOfficialNoTokenArg — debug-success path).");
                    return "OK: ModdedOfficialNoTokenArg started. Token from environment/files only.";
                }
                catch (Exception ex) { ModLogger.Error("DedicatedHelper: ModdedOfficialNoTokenArg start failed.", ex); return "ERROR: " + ex.Message; }
            }

            // ТЗ B1: чотири окремі launch preset'и для таблиці (module load / manager login / dashboard / shutdown reason).
            if (LaunchPresetB1 >= 0 && LaunchPresetB1 <= 3)
            {
                if (string.IsNullOrWhiteSpace(token))
                    TryReadTokenFromFolder(out token, out tokenFolder);
                string tempDir = Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath();
                string logOutputDir = Path.Combine(tempDir, "CoopSpectatorDedicated_logs");
                try { if (!Directory.Exists(logOutputDir)) Directory.CreateDirectory(logOutputDir); } catch (Exception) { }
                string presetName = GetB1PresetName(LaunchPresetB1);
                string b1Args = NormalizeDedicatedArguments(BuildArgumentsB1Preset(LaunchPresetB1, port, token ?? "", logOutputDir));
                ModLogger.Info("DedicatedHelper [B1] Launch preset=" + presetName);
                ModLogger.Info("DedicatedHelper [B1] exact command line=" + b1Args);
                LogB1Checklist();
                var b1StartInfo = new ProcessStartInfo { FileName = exePath, WorkingDirectory = workingDir ?? "", UseShellExecute = false, CreateNoWindow = false, Arguments = b1Args, RedirectStandardInput = true };
                try
                {
                    Process p = Process.Start(b1StartInfo);
                    if (p == null) { ModLogger.Error("DedicatedHelper: Process.Start returned null.", null); return "ERROR: Failed to start process."; }
                    _dedicatedProcess = p;
                    ModLogger.Info("DedicatedHelper [B1] Starter PID=" + p.Id);
                    LogDedicatedProcessInfo(p, "B1 after Start (Starter)");
                    LogProcessCommandLine(p.Id);
                    TrySwitchToChildProcessIfAny(p.Id);
                    Process current = _dedicatedProcess;
                    if (current != null && current.Id != p.Id)
                        ModLogger.Info("DedicatedHelper [B1] Child PID=" + current.Id);
                    LogB1Checklist();
                    UiFeedback.ShowMessageDeferred("Dedicated started (B1 " + presetName + "). Fill table from server console.");
                    return "OK: B1 preset " + presetName + ". Check dedicated console and fill table: module load, manager login, dashboard, shutdown reason.";
                }
                catch (Exception ex) { ModLogger.Error("DedicatedHelper: B1 start failed.", ex); return "ERROR: " + ex.Message; }
            }

            // Окремий debug launch для modded dedicated (ТЗ 1): не SteamLikeLaunch, явний _MODULES_ з CoopSpectatorDedicated.
            if (DebugModdedDedicatedLaunch)
            {
                if (string.IsNullOrWhiteSpace(token))
                    TryReadTokenFromFolder(out token, out tokenFolder);
                string tempDir = Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath();
                string logOutputDir = Path.Combine(tempDir, "CoopSpectatorDedicated_logs");
                try { if (!Directory.Exists(logOutputDir)) Directory.CreateDirectory(logOutputDir); } catch (Exception) { }
                string loadedPath = Path.Combine(tempDir, "CoopSpectatorDedicated_loaded.txt");
                string errorPath = Path.Combine(tempDir, "CoopSpectatorDedicated_error.txt");
                string moddedArgs = NormalizeDedicatedArguments(BuildArgumentsModdedDedicatedDebug(port, token ?? "", logOutputDir));
                ModLogger.Info("DedicatedHelper [DEBUG Modded] exe=" + exePath);
                ModLogger.Info("DedicatedHelper [DEBUG Modded] workingDir=" + (workingDir ?? ""));
                ModLogger.Info("DedicatedHelper [DEBUG Modded] full args=" + moddedArgs);
                ModLogger.Info("DedicatedHelper [DEBUG Modded] _MODULES_=" + ExactCampaignSceneModulesArg);
                ModLogger.Info("DedicatedHelper [DEBUG Modded] Paths: loaded=" + loadedPath + " error=" + errorPath + " LogOutputPath=" + logOutputDir);
                var moddedStartInfo = new ProcessStartInfo { FileName = exePath, WorkingDirectory = workingDir ?? "", UseShellExecute = false, CreateNoWindow = false, Arguments = moddedArgs, RedirectStandardInput = true };
                try
                {
                    Process p = Process.Start(moddedStartInfo);
                    if (p == null) { ModLogger.Error("DedicatedHelper: Process.Start returned null.", null); return "ERROR: Failed to start process."; }
                    _dedicatedProcess = p;
                    ModLogger.Info("DedicatedHelper [DEBUG Modded] Starter PID=" + p.Id);
                    LogDedicatedProcessInfo(p, "DEBUG Modded after Start (Starter)");
                    LogProcessCommandLine(p.Id);
                    TrySwitchToChildProcessIfAny(p.Id);
                    Process current = _dedicatedProcess;
                    if (current != null && current.Id != p.Id)
                        ModLogger.Info("DedicatedHelper [DEBUG Modded] Child PID=" + current.Id);
                    ModLogger.Info("DedicatedHelper [DEBUG Modded] After start check: loaded=" + loadedPath + " error=" + errorPath + " LogOutputPath=" + logOutputDir);
                    UiFeedback.ShowMessageDeferred("Dedicated started (DEBUG Modded). Check " + loadedPath + " for proof-of-load.");
                    return "OK: DEBUG Modded dedicated started. Check %TEMP%\\CoopSpectatorDedicated_loaded.txt and CoopSpectatorDedicated_logs.";
                }
                catch (Exception ex) { ModLogger.Error("DedicatedHelper: DEBUG Modded start failed.", ex); return "ERROR: " + ex.Message; }
            }

            // ТЗ C3: production-like modded official flow — CoopSpectatorDedicated у _MODULES_, без token arg, токен з Documents.
            if (UseModdedDedicatedOfficialFlow)
            {
                string tempDir = Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath();
                string logOutputDir = Path.Combine(tempDir, "CoopSpectatorDedicated_logs");
                try { if (!Directory.Exists(logOutputDir)) Directory.CreateDirectory(logOutputDir); } catch (Exception) { }
                ModLogger.Info("DedicatedHelper [ModdedOfficial] token arg disabled by design.");
                ModLogger.Info("DedicatedHelper [ModdedOfficial] expecting official token resolution (Documents\\...\\Tokens).");
                string moddedConfigFile = null;
                if (UseStartupConfigInModdedOfficialFlow)
                {
                    moddedConfigFile = TryWriteStartupConfigForListedTest(exePath);
                    string moddedConfigPath = "";
                    if (!string.IsNullOrEmpty(moddedConfigFile))
                    {
                        string root = GetDedicatedServerRootFromExe(exePath);
                        moddedConfigPath = !string.IsNullOrEmpty(root) ? Path.Combine(root, "Modules", "Native", moddedConfigFile) : moddedConfigFile;
                    }
                    LogStartupState(!string.IsNullOrEmpty(moddedConfigFile), moddedConfigPath, TestListedScene, UseTdmCloneForListedTest ? "TdmClone" : "TeamDeathmatch", TestListedServerName, "config file (listed-test)", string.IsNullOrEmpty(moddedConfigFile) ? "none" : "config");
                }
                else
                {
                    LogStartupState(false, "", "", "", "", "default", "manual");
                }
                string moddedOfficialArgs = NormalizeDedicatedArguments(BuildArgumentsModdedOfficialNoTokenArg(port, logOutputDir, moddedConfigFile));
                string configInjectionMode = UseStartupConfigInModdedOfficialFlow ? "ListedTest" : "None";
                ModLogger.Info("DedicatedHelper [LaunchPlan] mode=ModdedOfficial OurArgs=" + (string.IsNullOrEmpty(moddedOfficialArgs) ? "(empty)" : moddedOfficialArgs));
                ModLogger.Info("DedicatedHelper [LaunchPlan] ExpectedStarterAddsArgs=true ConfigInjectionMode=" + configInjectionMode);
                ModLogger.Info("DedicatedHelper [ModdedOfficial] exact args=" + moddedOfficialArgs);
                var moddedOfficialStartInfo = new ProcessStartInfo { FileName = exePath, WorkingDirectory = workingDir ?? "", UseShellExecute = false, CreateNoWindow = false, Arguments = moddedOfficialArgs, RedirectStandardInput = true };
                try
                {
                    Process p = Process.Start(moddedOfficialStartInfo);
                    if (p == null) { ModLogger.Error("DedicatedHelper: Process.Start returned null.", null); return "ERROR: Failed to start process."; }
                    _dedicatedProcess = p;
                    ModLogger.Info("DedicatedHelper [ModdedOfficial] Starter PID=" + p.Id);
                    LogDedicatedProcessInfo(p, "ModdedOfficial after Start (Starter)");
                    LogProcessCommandLine(p.Id);
                    TrySwitchToChildProcessIfAny(p.Id);
                    Process current = _dedicatedProcess;
                    if (current != null && current.Id != p.Id)
                        ModLogger.Info("DedicatedHelper [ModdedOfficial] Child PID=" + current.Id);
                    string loadedHint = Path.Combine(tempDir, "CoopSpectatorDedicated_loaded.txt");
                    UiFeedback.ShowMessageDeferred("Dedicated started (modded official). Check console: minimal mode active, Logging in, no Disconnected.");
                    ModLogger.Info("DedicatedHelper: Modded official flow started. Check dedicated console: CoopSpectatorDedicated minimal mode active, Logging in, RestObjectRequestMessage success, AliveMessage, no Disconnected. Custom Server List, dashboard.");
                    return "OK: Dedicated started (modded official flow). Token from Documents. Check server in Custom Server List.";
                }
                catch (Exception ex) { ModLogger.Error("DedicatedHelper: Modded official start failed.", ex); return "ERROR: " + ex.Message; }
            }

            string arguments;
            string configFile = null;
            if (SteamLikeLaunch)
            {
                if ((AddTokenOnly || AddTokenAndPortOnly) && (AddPortOnly || AddConfigFileOnly))
                    ModLogger.Warn("DedicatedHelper: token flag + port/config — token via args breaks manager; use only port+config (token from Documents).");
                if (AddTokenAndPortOnly)
                {
                    ModLogger.Warn("DedicatedHelper: AddTokenAndPortOnly is ON — this mode is known to break manager connection (Disconnected). Use false for stable launch.");
                    LogStartupState(false, "", "", "", "", "default", "manual");
                    arguments = BuildArgumentsTokenAndPortOnly(port, token ?? "");
                    ModLogger.Info("DedicatedHelper: args = token + port only.");
                }
                else if (AddTokenOnly)
                {
                    LogStartupState(false, "", "", "", "", "default", "manual");
                    arguments = BuildArgumentsTokenOnly(token ?? "");
                    ModLogger.Info("DedicatedHelper: AddTokenOnly — args = token only (known to cause Disconnected).");
                }
                else
                {
                    // Безпечні args: слухати на всіх інтерфейсах (0.0.0.0), порт, конфіг (токен ніколи — ламає manager).
                    var safeArgs = new System.Collections.Generic.List<string>();
                    safeArgs.Add("--multihome 0.0.0.0");
                    if (AddPortOnly) safeArgs.Add("--port " + port);
                    if (AddConfigFileOnly)
                    {
                        configFile = TryWriteStartupConfig(exePath);
                        if (!string.IsNullOrEmpty(configFile)) safeArgs.Add("/dedicatedcustomserverconfigfile " + configFile);
                    }
                    bool steamConfigApplied = AddConfigFileOnly && !string.IsNullOrEmpty(configFile);
                    string steamConfigPath = steamConfigApplied ? Path.Combine(GetDedicatedServerRootFromExe(exePath) ?? "", "Modules", "Native", configFile ?? "") : "";
                    LogStartupState(steamConfigApplied, steamConfigPath, "", "TeamDeathmatch", "Coop Spectator", steamConfigApplied ? "config file" : "default", steamConfigApplied ? "config" : "manual");
                    if (DebugTryAddCoopSpectatorDedicatedToModules)
                    {
                        safeArgs.Add("_MODULES_*Native*Multiplayer*DedicatedCustomServerHelper*CoopSpectatorDedicated*_MODULES_");
                        ModLogger.Info("DedicatedHelper [DEBUG] Added _MODULES_ with CoopSpectatorDedicated. Expect Starter to append its own _MODULES_ and overwrite → Disconnected. Check child process CommandLine in log.");
                    }
                    arguments = safeArgs.Count > 0 ? string.Join(" ", safeArgs) : "";
                    ModLogger.Info("DedicatedHelper: SteamLikeLaunch, safe args (port+config, no token): " + (string.IsNullOrEmpty(arguments) ? "(none)" : arguments));
                }
            }
            else
            {
                configFile = UseStartupConfig ? TryWriteStartupConfig(exePath) : null;
                bool nonSteamConfigApplied = !string.IsNullOrEmpty(configFile);
                string nonSteamConfigPath = nonSteamConfigApplied ? Path.Combine(GetDedicatedServerRootFromExe(exePath) ?? "", "Modules", "Native", configFile ?? "") : "";
                LogStartupState(nonSteamConfigApplied, nonSteamConfigPath, "", "TeamDeathmatch", "Coop Spectator", nonSteamConfigApplied ? "config file" : "default", nonSteamConfigApplied ? "config" : "manual");
                arguments = BuildArguments(port, token, configFile, exePath);
                LogLaunchParams(exePath, workingDir ?? "", port, configFile, arguments);
            }

            arguments = NormalizeDedicatedArguments(arguments ?? "");

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = workingDir ?? "",
                UseShellExecute = false,
                CreateNoWindow = false,
                Arguments = arguments,
                RedirectStandardInput = true // Щоб пізніше можна було відправити start_mission/end_mission через stdin (якщо сервер читає)
            };

            LogFullLaunchDiagnostics(startInfo, exePath, arguments, workingDir ?? "");

            try
            {
                Process p = Process.Start(startInfo);
                if (p == null)
                {
                    ModLogger.Error("DedicatedHelper: Process.Start returned null.", null);
                    return "ERROR: Failed to start process.";
                }
                _dedicatedProcess = p; // Зберігаємо для TrySendConsoleLine (start_mission / end_mission)
                ModLogger.Info("DedicatedHelper: process started PID=" + p.Id);
                LogDedicatedProcessInfo(p, "after Start (Starter)");
                LogProcessCommandLine(p.Id);
                TrySwitchToChildProcessIfAny(p.Id);

                bool safeProfile = SteamLikeLaunch && (AddPortOnly || AddConfigFileOnly) && !AddTokenOnly && !AddTokenAndPortOnly;
                bool isolationTest = SteamLikeLaunch && (AddTokenOnly || AddTokenAndPortOnly);
                string okMsg = SteamLikeLaunch
                    ? (isolationTest
                        ? "Dedicated Helper started (PID " + p.Id + "). Check server console: AliveMessage = ok, Disconnected = this arg breaks."
                        : safeProfile
                            ? "Dedicated Helper started (PID " + p.Id + ", port " + port + "). Server visible in Custom Server List (start_game from config). Friends: Multiplayer -> Custom Server List."
                            : "Dedicated Helper started (PID " + p.Id + ") Steam-like (0 args). In server console type start_game. Friends: Multiplayer -> Custom Server List.")
                    : (configFile != null
                        ? "Dedicated Helper started (PID " + p.Id + ", port " + port + "). Server will be visible in Custom Server List. Friends: Multiplayer -> Custom Server List."
                        : "Dedicated Helper started (PID " + p.Id + ", port " + port + "). In the server console type start_game to make it visible. Friends: Multiplayer -> Custom Server List.");
                UiFeedback.ShowMessageDeferred(okMsg);
                ModLogger.Info("DedicatedHelper: " + okMsg);
                string tempDir = Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath();
                string loadedHint = Path.Combine(tempDir, "CoopSpectatorDedicated_loaded.txt");
                string errorHint = Path.Combine(tempDir, "CoopSpectatorDedicated_error.txt");
                ModLogger.Info("DedicatedHelper: Proof-of-load: check " + loadedHint + " and " + errorHint + "; dedicated console for [CoopSpectator] DEDICATED OnSubModuleLoad. Game logs (if any): %ProgramData%\\Mount and Blade II Bannerlord\\logs or server workingDir\\logs.");
                return "OK: " + okMsg;
            }
            catch (Exception ex)
            {
                ModLogger.Error("DedicatedHelper: failed to start process.", ex);
                return "ERROR: " + ex.Message;
            }
        }

        /// <summary>Логує exe path, working dir і рядок аргументів (токен замінено на "(token)") для діагностики "модового" vs Steam запуску.</summary>
        private static void LogLaunchParams(string exePath, string workingDir, int port, string configFileName, string fullArgsWithToken)
        {
            string argsForLog = BuildArguments(port, "(token)", configFileName, exePath);
            ModLogger.Info("DedicatedHelper launch: exe=" + exePath + " | workingDir=" + workingDir + " | args=" + argsForLog);
        }

        /// <summary>Повний діагностичний лог перед Process.Start: exe, args, CWD, оточення процесу гри, прапорці StartInfo.</summary>
        private static void LogFullLaunchDiagnostics(ProcessStartInfo startInfo, string exePath, string arguments, string workingDir)
        {
            string gameCurrentDir = null;
            try { gameCurrentDir = Environment.CurrentDirectory; } catch (Exception ex) { gameCurrentDir = "error: " + ex.Message; }
            ModLogger.Info("DedicatedHelper [before Start] exePath=" + (exePath ?? ""));
            ModLogger.Info("DedicatedHelper [before Start] arguments=" + (arguments ?? "") + " (length=" + (arguments != null ? arguments.Length : 0) + ")");
            ModLogger.Info("DedicatedHelper [before Start] WorkingDirectory=" + (workingDir ?? ""));
            ModLogger.Info("DedicatedHelper [before Start] Environment.CurrentDirectory(game)=" + (gameCurrentDir ?? ""));
            ModLogger.Info("DedicatedHelper [before Start] UseShellExecute=" + startInfo.UseShellExecute + " CreateNoWindow=" + startInfo.CreateNoWindow + " RedirectStdOut=" + startInfo.RedirectStandardOutput + " RedirectStdErr=" + startInfo.RedirectStandardError);
            if (startInfo.EnvironmentVariables != null && startInfo.EnvironmentVariables.Count > 0)
            {
                var custom = new System.Text.StringBuilder();
                foreach (System.Collections.DictionaryEntry e in startInfo.EnvironmentVariables)
                {
                    if (e.Key == null) continue;
                    string k = e.Key.ToString();
                    if (string.IsNullOrEmpty(k)) continue;
                    custom.Append(" ").Append(k).Append("=").Append((e.Value ?? "").ToString());
                }
                ModLogger.Info("DedicatedHelper [before Start] CustomEnv:" + custom.ToString());
            }
            else
                ModLogger.Info("DedicatedHelper [before Start] CustomEnv: (none)");
        }

        /// <summary>Після старту знімає через WMI CommandLine та ExecutablePath процесу по PID (чи Starter отримав ті ж args).</summary>
        private static void LogProcessCommandLine(int processId)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT ExecutablePath, CommandLine FROM Win32_Process WHERE ProcessId = " + processId))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject obj in results)
                    {
                        string exe = obj["ExecutablePath"] != null ? obj["ExecutablePath"].ToString() : "";
                        string cmd = obj["CommandLine"] != null ? obj["CommandLine"].ToString() : "";
                        ModLogger.Info("DedicatedHelper [WMI PID " + processId + "] ExecutablePath=" + exe);
                        ModLogger.Info("DedicatedHelper [WMI PID " + processId + "] CommandLine=" + cmd);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedHelper [WMI] failed for PID " + processId + ": " + ex.Message);
            }
        }

        /// <summary>Шукає дочірній процес Starter'а (наприклад DedicatedCustomServer.exe), у якого консоль і куди вводять команди вручну; якщо знайдено — підміняє _dedicatedProcess на нього (stdin може бути недоступний, але PID у лозі збігатиметься з Task Manager).</summary>
        private static void TrySwitchToChildProcessIfAny(int starterPid)
        {
            const int waitSecondsTotal = 8;
            const int intervalMs = 1000;
            for (int waited = 0; waited < waitSecondsTotal; waited += (intervalMs / 1000))
            {
                if (waited > 0)
                    Thread.Sleep(intervalMs);
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, Name FROM Win32_Process WHERE ParentProcessId = " + starterPid))
                    using (var results = searcher.Get())
                    {
                        foreach (ManagementObject obj in results)
                        {
                            int childPid = obj["ProcessId"] != null ? Convert.ToInt32(obj["ProcessId"]) : 0;
                            string name = obj["Name"] != null ? (obj["Name"].ToString() ?? "") : "";
                            if (childPid <= 0) continue;
                            if (name.IndexOf("DedicatedCustomServer", StringComparison.OrdinalIgnoreCase) >= 0 && name.IndexOf("Starter", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                Process child = Process.GetProcessById(childPid);
                                if (child != null && !child.HasExited)
                                {
                                    _dedicatedProcess = child;
                                    ModLogger.Info("DedicatedHelper: switched to child process StarterPID=" + starterPid + " ChildPID=" + childPid + " Name=" + name + " (console where you type commands).");
                                    LogDedicatedProcessInfo(child, "after switch to child");
                                    LogProcessCommandLine(childPid);
                                    return;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Info("DedicatedHelper [child lookup] attempt at " + waited + "s: " + ex.Message);
                }
            }
            ModLogger.Info("DedicatedHelper: no child process found for Starter PID=" + starterPid + " (stdin will go to Starter; if commands don't run, compare this PID with Task Manager console window).");
        }

        /// <summary>ТЗ C1/C2: args для ModdedOfficial — тільки те, що ми передаємо Starter. Official tail (_MODULES_*Native*Multiplayer*_MODULES_ /dedicatedcustomserver ... /playerhosteddedicatedserver) НЕ додаємо: Starter сам дописує його при формуванні command line дочірнього процесу. Дубль у Command Args усунуто.</summary>
        private static string BuildArgumentsModdedOfficialNoTokenArg(int port, string logOutputPath, string configFileName = null)
        {
            var args = new System.Collections.Generic.List<string>();
            args.Add("--multihome 0.0.0.0");
            args.Add("--port " + port);
            if (!string.IsNullOrEmpty(configFileName))
                args.Add("/dedicatedcustomserverconfigfile " + configFileName);
            args.Add(ExactCampaignSceneModulesArg);
            if (!string.IsNullOrEmpty(logOutputPath))
                args.Add("/LogOutputPath \"" + logOutputPath.Trim().Replace("\"", "\"\"") + "\"");
            // Не додаємо /dedicatedcustomserver ... /playerhosteddedicatedserver — Starter додає цей блок автоматично; інакше у child Command Args був би дубль.
            return string.Join(" ", args);
        }

        /// <summary>ТЗ B1: ім'я preset для логу.</summary>
        private static string GetB1PresetName(int preset)
        {
            switch (preset)
            {
                case 0: return "PlainOfficialArgs";
                case 1: return "ModdedMixedArgs";
                case 2: return "ModdedOnly";
                case 3: return "ModdedOnlyWithToken";
                default: return "Preset" + preset;
            }
        }

        /// <summary>ТЗ B1: збирає args для preset. 0=без нашого _MODULES_, 1=modded+другий _MODULES_+playerhosted, 2=тільки modded+LogOutputPath, 3=як 2+token.</summary>
        private static string BuildArgumentsB1Preset(int preset, int port, string token, string logOutputPath)
        {
            var args = new System.Collections.Generic.List<string>();
            args.Add("--multihome 0.0.0.0");
            args.Add("--port " + port);
            const string ModdedModules = ExactCampaignSceneModulesArg;
            const string OfficialModules = "_MODULES_*Native*Multiplayer*_MODULES_";
            switch (preset)
            {
                case 0: // PlainOfficialArgs — без CoopSpectatorDedicated, Starter додасть _MODULES_ сам
                    break;
                case 1: // ModdedMixedArgs — наш _MODULES_ + другий _MODULES_ + /dedicatedcustomserver ... /playerhosteddedicatedserver
                    if (!string.IsNullOrWhiteSpace(token))
                        args.Add("/dedicatedcustomserverauthtoken \"" + token.Trim().Replace("\"", "\"\"") + "\"");
                    args.Add(ModdedModules);
                    if (!string.IsNullOrEmpty(logOutputPath))
                        args.Add("/LogOutputPath \"" + logOutputPath.Trim().Replace("\"", "\"\"") + "\"");
                    args.Add(OfficialModules);
                    args.Add("/dedicatedcustomserver " + port + " USER 0 /playerhosteddedicatedserver");
                    break;
                case 2: // ModdedOnly — тільки наш _MODULES_ + LogOutputPath, без другого _MODULES_, без /dedicatedcustomserver
                    args.Add(ModdedModules);
                    if (!string.IsNullOrEmpty(logOutputPath))
                        args.Add("/LogOutputPath \"" + logOutputPath.Trim().Replace("\"", "\"\"") + "\"");
                    break;
                case 3: // ModdedOnlyWithToken — як 2 + token
                    if (!string.IsNullOrWhiteSpace(token))
                        args.Add("/dedicatedcustomserverauthtoken \"" + token.Trim().Replace("\"", "\"\"") + "\"");
                    args.Add(ModdedModules);
                    if (!string.IsNullOrEmpty(logOutputPath))
                        args.Add("/LogOutputPath \"" + logOutputPath.Trim().Replace("\"", "\"\"") + "\"");
                    break;
                default:
                    break;
            }
            return string.Join(" ", args);
        }

        /// <summary>ТЗ B1: лог-нагадування — що перевірити в консолі дедика для заповнення таблиці.</summary>
        private static void LogB1Checklist()
        {
            ModLogger.Info("DedicatedHelper [B1] In dedicated console check: CoopSpectatorDedicated minimal mode active? dashboard startup? Logging in? Login Failed? Disconnected from custom battle server manager? Table: launch preset | module load y/n | manager login y/n | dashboard y/n | shutdown reason.");
        }

        /// <summary>ТЗ C2: нормалізація args — official tail не більше одного разу. Якщо вже є /playerhosteddedicatedserver — не дублювати блок; якщо вже є OfficialModulesTail — не дублювати.</summary>
        private static string NormalizeDedicatedArguments(string args)
        {
            if (string.IsNullOrEmpty(args)) return args;
            string s = args;
            // Видалити дублікати блоку /dedicatedcustomserver ... /playerhosteddedicatedserver
            int idx = s.IndexOf(PlayerHostedSuffix, StringComparison.OrdinalIgnoreCase);
            while (idx >= 0)
            {
                int next = s.IndexOf(PlayerHostedSuffix, idx + 1, StringComparison.OrdinalIgnoreCase);
                if (next < 0) break;
                int blockStart = s.LastIndexOf(DedicatedCustomServerPrefix, next, StringComparison.Ordinal);
                if (blockStart < 0) break;
                int blockEnd = next + PlayerHostedSuffix.Length;
                s = s.Remove(blockStart, blockEnd - blockStart);
                s = s.Replace("  ", " ");
                idx = s.IndexOf(PlayerHostedSuffix, StringComparison.OrdinalIgnoreCase);
            }
            // Видалити дублікати _MODULES_*Native*Multiplayer*_MODULES_
            int firstOfficial = s.IndexOf(OfficialModulesTail, StringComparison.Ordinal);
            if (firstOfficial >= 0)
            {
                int secondOfficial = s.IndexOf(OfficialModulesTail, firstOfficial + OfficialModulesTail.Length, StringComparison.Ordinal);
                while (secondOfficial >= 0)
                {
                    s = s.Remove(secondOfficial, OfficialModulesTail.Length);
                    s = s.Replace("  ", " ").Trim();
                    secondOfficial = s.IndexOf(OfficialModulesTail, firstOfficial + OfficialModulesTail.Length, StringComparison.Ordinal);
                }
            }
            return s.Trim();
        }

        /// <summary>Args для DEBUG Modded dedicated: exact-bootstrap `_MODULES_`, port, token (якщо є), /LogOutputPath. Не SteamLikeLaunch.</summary>
        private static string BuildArgumentsModdedDedicatedDebug(int port, string token, string logOutputPath)
        {
            var args = new System.Collections.Generic.List<string>();
            args.Add("--multihome 0.0.0.0");
            args.Add("--port " + port);
            if (!string.IsNullOrWhiteSpace(token))
                args.Add("/dedicatedcustomserverauthtoken \"" + token.Trim().Replace("\"", "\"\"") + "\"");
            args.Add(ExactCampaignSceneModulesArg);
            if (!string.IsNullOrEmpty(logOutputPath))
                args.Add("/LogOutputPath \"" + logOutputPath.Trim().Replace("\"", "\"\"") + "\"");
            return string.Join(" ", args);
        }

        /// <summary>Тільки токен (для ізоляції тесту AddTokenOnly).</summary>
        private static string BuildArgumentsTokenOnly(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return "";
            return "/dedicatedcustomserverauthtoken \"" + token.Trim().Replace("\"", "\"\"") + "\"";
        }

        /// <summary>Тільки токен і порт (для SteamLikeLaunch + AddTokenAndPortOnly). Без _MODULES_ і без config.</summary>
        private static string BuildArgumentsTokenAndPortOnly(int port, string token)
        {
            var args = new System.Collections.Generic.List<string>();
            args.Add("--multihome 0.0.0.0");
            if (!string.IsNullOrWhiteSpace(token))
                args.Add("/dedicatedcustomserverauthtoken \"" + token.Trim().Replace("\"", "\"\"") + "\"");
            args.Add("--port " + port);
            return string.Join(" ", args);
        }

        private static string BuildArguments(int port, string token, string configFileName, string exePath)
        {
            var args = new System.Collections.Generic.List<string>();
            args.Add("--multihome 0.0.0.0");
            if (!string.IsNullOrWhiteSpace(token))
                args.Add("/dedicatedcustomserverauthtoken \"" + token.Trim().Replace("\"", "\"\"") + "\"");
            args.Add("--port " + port);
            if (!string.IsNullOrEmpty(configFileName))
                args.Add("/dedicatedcustomserverconfigfile " + configFileName);
            args.Add(ModulesArg);
            // Для core exe передаємо аргументи, які Starter додає; для Starter не додаємо — він сам їх підставить.
            string exeName = Path.GetFileName(exePath ?? "");
            if (exeName.IndexOf("Starter", StringComparison.OrdinalIgnoreCase) < 0)
                args.Add("/dedicatedcustomserver " + port + " USER 0 /playerhosteddedicatedserver");
            return string.Join(" ", args);
        }
    }
}
