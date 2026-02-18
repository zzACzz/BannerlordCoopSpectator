using System; // Exception, Environment, IntPtr
using System.Diagnostics; // Process, ProcessStartInfo
using System.IO; // Path, File, Directory
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
        private const string ExeRelativePath = @"bin\Win64_Shipping_Server\DedicatedCustomServer.Starter.exe";
        /// <summary>Папка Tokens у Documents — у Windows зазвичай "Mount and Blade II Bannerlord" (слово "and"), не "&".</summary>
        private const string TokensSubFolder = @"Mount and Blade II Bannerlord\Tokens";
        /// <summary>Ім'я файлу токена, який гра створює після customserver.gettoken.</summary>
        private const string OfficialTokenFileName = "DedicatedCustomServerAuthToken.txt";

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

        /// <summary>Шукає exe дедик-сервера: поряд з грою (Steam common) або через змінну середовища.</summary>
        public static string TryFindDedicatedServerExePath()
        {
            // 1) Змінна середовища (для кастомного шляху)
            string envPath = Environment.GetEnvironmentVariable("BANNERLORD_DEDICATED_SERVER_PATH");
            if (!string.IsNullOrWhiteSpace(envPath))
            {
                string exe = Path.Combine(envPath, ExeRelativePath);
                if (File.Exists(exe)) return exe;
                if (File.Exists(envPath)) return envPath; // повний шлях до exe
            }

            // 2) Поряд з грою: ...\Steam\steamapps\common\Mount & Blade II Bannerlord -> ...\Mount & Blade II Dedicated Server
            string gameRoot = TryGetGameRootFromProcess();
            if (!string.IsNullOrEmpty(gameRoot))
            {
                string parent = Path.GetDirectoryName(gameRoot);
                if (!string.IsNullOrEmpty(parent))
                {
                    string dedicatedRoot = Path.Combine(parent, DedicatedServerFolderName);
                    string exe = Path.Combine(dedicatedRoot, ExeRelativePath);
                    if (File.Exists(exe)) return exe;
                }
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

        /// <summary>
        /// Запускає Dedicated Helper. Токен: з папки Tokens або переданий явно.
        /// Повертає повідомлення для консолі (успіх або текст помилки).
        /// </summary>
        public static string Start(string tokenOverride, int port)
        {
            if (port <= 0) port = DefaultPort;

            string token = tokenOverride;
            if (string.IsNullOrWhiteSpace(token) && !TryReadTokenFromFolder(out token))
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
                string msg = "Dedicated Server exe not found. Install 'Mount & Blade II: Dedicated Server' from Steam (Tools). Path: Steam\\steamapps\\common\\" + DedicatedServerFolderName + "\\" + ExeRelativePath;
                UiFeedback.ShowMessageDeferred(msg);
                ModLogger.Info("DedicatedHelper: " + msg);
                return "ERROR: " + msg;
            }

            string workingDir = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(workingDir)) workingDir = Path.GetDirectoryName(Path.GetDirectoryName(exePath));

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = workingDir ?? "",
                UseShellExecute = false,
                CreateNoWindow = false,
                Arguments = BuildArguments(port, token)
            };

            try
            {
                Process p = Process.Start(startInfo);
                if (p == null)
                {
                    ModLogger.Error("DedicatedHelper: Process.Start returned null.", null);
                    return "ERROR: Failed to start process.";
                }
                string okMsg = "Dedicated Helper started (PID " + p.Id + ", port " + port + "). In the server console type start_game to make it visible. Friends join via Multiplayer -> Custom Server List.";
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

        private static string BuildArguments(int port, string token)
        {
            var args = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(token))
                args.Add("/dedicatedcustomserverauthtoken \"" + token.Trim().Replace("\"", "\"\"") + "\"");
            args.Add("/port " + port);
            return string.Join(" ", args);
        }
    }
}
