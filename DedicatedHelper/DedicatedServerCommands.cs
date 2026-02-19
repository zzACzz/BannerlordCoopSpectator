using System; // Exception
using System.Net; // HttpWebRequest, WebRequest
using CoopSpectator.Infrastructure; // ModLogger, UiFeedback

namespace CoopSpectator.DedicatedHelper // IPC до Dedicated Helper: start_mission, end_mission (Етап 3b)
{
    /// <summary>
    /// Відправка консольних команд на локальний Dedicated Server (HTTP web panel або stdin процесу).
    /// Після того як хост заходить у битву — викликати start_mission; при виході — end_mission.
    /// </summary>
    public static class DedicatedServerCommands
    {
        private const int DashboardPort = 7210; // Порт для спроби HTTP (офіційний UDP 7210; web panel може бути тут або на іншому)
        private const int HttpTimeoutMs = 1500; // Короткий таймаут, щоб не блокувати гру

        /// <summary>Відправити команду на дедик-сервер: спочатку спроба HTTP (якщо знайдемо API), потім stdin процесу, якщо він запущений з моду.</summary>
        /// <returns>true якщо команду прийнято (HTTP успіх або stdin записано), false інакше.</returns>
        public static bool SendCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                ModLogger.Info("DedicatedServerCommands: SendCommand skipped (empty).");
                return false;
            }
            string cmd = command.Trim();
            // 1) Спроба HTTP — якщо у майбутньому з’ясуємо endpoint (наприклад з DevTools), додати сюди конкретний URL.
            if (TrySendCommandViaHttp(cmd))
            {
                ModLogger.Info("DedicatedServerCommands: SendCommand(\"" + cmd + "\") sent via HTTP.");
                UiFeedback.ShowMessageDeferred("Coop: " + cmd + " → dedik (HTTP)");
                return true;
            }
            // 2) Спроба stdin процесу, запущеного з моду (якщо дедик-сервер читає консольні команди з stdin).
            if (DedicatedHelperLauncher.TrySendConsoleLine(cmd))
            {
                ModLogger.Info("DedicatedServerCommands: SendCommand(\"" + cmd + "\") sent via stdin.");
                UiFeedback.ShowMessageDeferred("Coop: " + cmd + " → dedik (stdin)");
                return true;
            }
            ModLogger.Info("DedicatedServerCommands: SendCommand(\"" + cmd + "\") — no channel (HTTP failed, stdin not available or server not started from mod).");
            UiFeedback.ShowMessageDeferred("Coop: " + cmd + " not sent (check game log)");
            return false;
        }

        /// <summary>Спроба відправити команду через HTTP. Спочатку Manager API (GET /Manager/start_mission, /Manager/end_mission), потім інші варіанти.</summary>
        private static bool TrySendCommandViaHttp(string command)
        {
            // Офіційний Dashboard Manager: кнопки Start Mission / End Mission роблять GET на /Manager/{command} (перевірено в DevTools)
            string managerUrl = "http://127.0.0.1:" + DashboardPort + "/Manager/" + Uri.EscapeDataString(command);
            string[] urlsToTry = new[]
            {
                managerUrl,
                "http://127.0.0.1:" + DashboardPort + "/command?cmd=" + Uri.EscapeDataString(command),
                "http://127.0.0.1:" + DashboardPort + "/api/command",
                "http://127.0.0.1:" + DashboardPort + "/"
            };
            foreach (string url in urlsToTry)
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = (url.Contains("/Manager/") || url == "http://127.0.0.1:" + DashboardPort + "/") ? "GET" : (url.Contains("/command") || url.Contains("/api/") ? "POST" : "GET");
                    req.Timeout = HttpTimeoutMs;
                    req.ReadWriteTimeout = HttpTimeoutMs;
                    req.ContentLength = 0;
                    if (req.Method == "POST" && url.Contains("/api/command"))
                    {
                        req.ContentType = "application/x-www-form-urlencoded";
                        string body = "command=" + Uri.EscapeDataString(command);
                        req.ContentLength = System.Text.Encoding.UTF8.GetByteCount(body);
                        using (var stream = req.GetRequestStream())
                            stream.Write(System.Text.Encoding.UTF8.GetBytes(body), 0, (int)req.ContentLength);
                    }
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        if (resp.StatusCode == HttpStatusCode.OK || (int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300)
                            return true;
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Info("DedicatedServerCommands: HTTP " + url + " — " + ex.Message);
                }
            }
            return false;
        }

        /// <summary>Зручні обгортки для бойового циклу.</summary>
        public static bool SendStartMission() => SendCommand("start_mission");
        public static bool SendEndMission() => SendCommand("end_mission");
    }
}
