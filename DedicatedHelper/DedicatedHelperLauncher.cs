using System; // Exception, Environment, IntPtr
using System.Diagnostics; // Process, ProcessStartInfo
using System.IO; // Path, File, Directory
using System.Management; // ManagementObjectSearcher, Win32_Process
using CoopSpectator.Infrastructure; // ModLogger, UiFeedback

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
        /// <summary>Папка Tokens у Documents — у Windows зазвичай "Mount and Blade II Bannerlord" (слово "and"), не "&".</summary>
        private const string TokensSubFolder = @"Mount and Blade II Bannerlord\Tokens";
        /// <summary>Ім'я файлу токена, який гра створює після customserver.gettoken.</summary>
        private const string OfficialTokenFileName = "DedicatedCustomServerAuthToken.txt";
        /// <summary>Ім'я стартового конфігу для автоматичного start_game (файл у Dedicated Server Modules\Native).</summary>
        private const string StartupConfigFileName = "ds_config_coop_start.txt";
        /// <summary>Якщо false — не передаємо конфіг при запуску (як Steam: "Command file is null"), щоб сервер не від'єднувався від Diamond. start_game тоді вводять вручну в консолі сервера.</summary>
        private const bool UseStartupConfig = false;
        /// <summary>Явний список модулів, як у ванільному dedicated (Steam). Без цього сервер може піднятися без Multiplayer/DedicatedCustomServerHelper і від'єднатися від custom battle server manager.</summary>
        private const string ModulesArg = "_MODULES_*Native*Multiplayer*DedicatedCustomServerHelper*_MODULES_";
        /// <summary>true = не передаємо наш _MODULES_/конфіг (Starter сам додає свої args — тоді AliveMessage ок). Якщо false — передаємо повний набір (раніше давало Disconnected).</summary>
        private const bool SteamLikeLaunch = true;
        /// <summary>При SteamLikeLaunch: true = додати тільки токен+порт. ВІДОМО ЛАМАЄ manager-конект (Disconnected). Залишати false.</summary>
        private const bool AddTokenAndPortOnly = false;
        /// <summary>Передавати /port (ізоляція: не ламає manager).</summary>
        private const bool AddPortOnly = true;
        /// <summary>НЕ вмикати: передача токена аргументом ламає manager (Disconnected). Токен лише з Documents.</summary>
        private const bool AddTokenOnly = false;
        /// <summary>Конфіг + /dedicatedcustomserverconfigfile — авто start_game (ізоляція: не ламає). Разом з AddPortOnly працює.</summary>
        private const bool AddConfigFileOnly = true;

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

        /// <summary>Кандидати для папки Tokens: MyDocuments, OneDrive\\Documents, OneDrive\\Документи.</summary>
        private static System.Collections.Generic.IEnumerable<string> GetTokenFolderCandidates()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrEmpty(docs))
                yield return Path.Combine(docs, TokensSubFolder);
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(user))
            {
                yield return Path.Combine(user, "OneDrive", "Documents", TokensSubFolder);
                yield return Path.Combine(user, "OneDrive", "Документи", TokensSubFolder);
            }
        }

        /// <summary>Шукає токен у всіх кандидатах папок. Спочатку DedicatedCustomServerAuthToken.txt, потім будь-який файл.</summary>
        public static bool TryReadTokenFromFolder(out string tokenContent)
        {
            tokenContent = null;
            foreach (string folder in GetTokenFolderCandidates())
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
                if (TryReadTokenFromFolderInternal(folder, out tokenContent))
                    return true;
            }
            return false;
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

        /// <summary>Відкриває папку Tokens у провіднику (для UX при відсутності токена).</summary>
        public static void OpenTokensFolderInExplorer()
        {
            string folder = GetTokensFolderPath();
            if (string.IsNullOrEmpty(folder)) return;
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
                string content = "ServerName Coop Spectator" + Environment.NewLine + "start_game" + Environment.NewLine;
                File.WriteAllText(configPath, content);
                ModLogger.Info("DedicatedHelper: wrote startup config to " + configPath);
                return StartupConfigFileName;
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedHelper: could not write config: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Запускає Dedicated Helper. Токен: з папки Tokens або переданий явно.
        /// Повертає повідомлення для консолі (успіх або текст помилки).
        /// </summary>
        public static string Start(string tokenOverride, int port)
        {
            if (port <= 0) port = DefaultPort;

            string token = tokenOverride;
            bool needToken = !SteamLikeLaunch || AddTokenAndPortOnly || AddTokenOnly;
            if (needToken && string.IsNullOrWhiteSpace(token) && !TryReadTokenFromFolder(out token))
            {
                string tokensPath = GetTokensFolderPath();
                string msg = "No token found. Multiplayer -> Console (ALT+~) -> customserver.gettoken. Folder: " + (tokensPath ?? "(unknown)");
                UiFeedback.ShowMessageDeferred(msg + " Run coop.dedicated_open_tokens to open folder.");
                ModLogger.Info("DedicatedHelper: " + msg);
                return "ERROR: " + msg + " Run coop.dedicated_open_tokens to open Tokens folder. Or: coop.dedicated_start [port] [token]";
            }

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

            string arguments;
            string configFile = null;
            if (SteamLikeLaunch)
            {
                if ((AddTokenOnly || AddTokenAndPortOnly) && (AddPortOnly || AddConfigFileOnly))
                    ModLogger.Warn("DedicatedHelper: token flag + port/config — token via args breaks manager; use only port+config (token from Documents).");
                if (AddTokenAndPortOnly)
                {
                    ModLogger.Warn("DedicatedHelper: AddTokenAndPortOnly is ON — this mode is known to break manager connection (Disconnected). Use false for stable launch.");
                    arguments = BuildArgumentsTokenAndPortOnly(port, token ?? "");
                    ModLogger.Info("DedicatedHelper: args = token + port only.");
                }
                else if (AddTokenOnly)
                {
                    arguments = BuildArgumentsTokenOnly(token ?? "");
                    ModLogger.Info("DedicatedHelper: AddTokenOnly — args = token only (known to cause Disconnected).");
                }
                else
                {
                    // Безпечні args: порт і/або конфіг (токен ніколи — ламає manager).
                    var safeArgs = new System.Collections.Generic.List<string>();
                    if (AddPortOnly) safeArgs.Add("/port " + port);
                    if (AddConfigFileOnly)
                    {
                        configFile = TryWriteStartupConfig(exePath);
                        if (!string.IsNullOrEmpty(configFile)) safeArgs.Add("/dedicatedcustomserverconfigfile " + configFile);
                    }
                    arguments = safeArgs.Count > 0 ? string.Join(" ", safeArgs) : "";
                    ModLogger.Info("DedicatedHelper: SteamLikeLaunch, safe args (port+config, no token): " + (string.IsNullOrEmpty(arguments) ? "(none)" : arguments));
                }
            }
            else
            {
                configFile = UseStartupConfig ? TryWriteStartupConfig(exePath) : null;
                arguments = BuildArguments(port, token, configFile, exePath);
                LogLaunchParams(exePath, workingDir ?? "", port, configFile, arguments);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = workingDir ?? "",
                UseShellExecute = false,
                CreateNoWindow = false,
                Arguments = arguments
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
                ModLogger.Info("DedicatedHelper: process started PID=" + p.Id);
                LogProcessCommandLine(p.Id);

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
            if (!string.IsNullOrWhiteSpace(token))
                args.Add("/dedicatedcustomserverauthtoken \"" + token.Trim().Replace("\"", "\"\"") + "\"");
            args.Add("/port " + port);
            return string.Join(" ", args);
        }

        private static string BuildArguments(int port, string token, string configFileName, string exePath)
        {
            var args = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(token))
                args.Add("/dedicatedcustomserverauthtoken \"" + token.Trim().Replace("\"", "\"\"") + "\"");
            args.Add("/port " + port);
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
