using System; // Exception
using System.IO; // StreamReader
using System.Net; // HttpWebRequest, WebRequest, WebException
using CoopSpectator.Infrastructure; // ModLogger, UiFeedback

namespace CoopSpectator.DedicatedHelper // IPC до Dedicated Helper: start_mission, end_mission (Етап 3b)
{
    /// <summary>
    /// Відправка консольних команд на локальний Dedicated Server: спочатку stdin процесу (якщо запущений з моду), потім HTTP web panel.
    /// Після того як хост заходить у битву — викликати start_mission; при виході — end_mission.
    /// </summary>
    public static class DedicatedServerCommands
    {
        private const int DashboardPort = 7210; // Порт для спроби HTTP (офіційний UDP 7210; web panel може бути тут або на іншому)
        private const int HttpTimeoutMs = 1500; // Короткий таймаут, щоб не блокувати гру
        private const int MaxResponseBodyLogLength = 400; // Скільки символів body логувати для діагностики

        /// <summary>Відправити команду на дедик-сервер: спочатку stdin (якщо процес запущений з моду), потім HTTP fallback.</summary>
        /// <returns>true якщо команду прийнято (stdin записано або HTTP успіх), false інакше.</returns>
        public static bool SendCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                ModLogger.Info("DedicatedServerCommands: SendCommand skipped (empty).");
                return false;
            }
            string cmd = command.Trim();
            // 1) Спроба HTTP — якщо у майбутньому з’ясуємо endpoint (наприклад з DevTools), додати сюди конкретний URL.
            bool useOnlyHttp = cmd == "start_mission" || cmd == "end_mission";
            if (!useOnlyHttp && DedicatedHelperLauncher.HasDedicatedProcess() && DedicatedHelperLauncher.TrySendConsoleLine(cmd))
            {
                ModLogger.Info("DedicatedServerCommands: SendCommand(\"" + cmd + "\") sent via stdin.");
                UiFeedback.ShowMessageDeferred("Coop: " + cmd + " → dedik (stdin)");
                return true;
            }
            if (TrySendCommandViaHttp(cmd))
            {
                ModLogger.Info("DedicatedServerCommands: SendCommand(\"" + cmd + "\") sent via HTTP.");
                UiFeedback.ShowMessageDeferred("Coop: " + cmd + " → dedik (HTTP)");
                return true;
            }
            ModLogger.Info("DedicatedServerCommands: SendCommand(\"" + cmd + "\") — no channel (HTTP failed, stdin not available or server not started from mod).");
            UiFeedback.ShowMessageDeferred("Coop: " + cmd + " not sent (check game log)");
            return false;
        }

        /// <summary>Спроба відправити команду через HTTP web panel: логін (GET Auth → POST login) + GET /Manager/{command} з auth cookie.</summary>
        private static bool TrySendCommandViaHttp(string command)
        {
            string baseUrl = WebPanelAuth.GetBaseUrl(DashboardPort);
            if (string.IsNullOrEmpty(baseUrl))
            {
                ModLogger.Info("DedicatedServerCommands: dashboard unreachable, no base URL.");
                return false;
            }
            string managerUrl = baseUrl + "/Manager/" + Uri.EscapeDataString(command);
            string password = DedicatedHelperLauncher.GetDashboardAdminPassword();

            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    if (!WebPanelAuth.EnsureSignedIn(password, DashboardPort))
                    {
                        ModLogger.Info("DedicatedServerCommands: WebPanelAuth.EnsureSignedIn failed (attempt " + (attempt + 1) + ").");
                        if (attempt == 0) continue;
                        return false;
                    }

                    ModLogger.Info("DedicatedServerCommands: GET " + managerUrl);
                    var req = (HttpWebRequest)WebRequest.Create(managerUrl);
                    req.Method = "GET";
                    req.Proxy = null;
                    req.Timeout = 3000;
                    req.ReadWriteTimeout = 3000;
                    req.KeepAlive = false;
                    req.AllowAutoRedirect = false;
                    req.CookieContainer = WebPanelAuth.GetCookieContainer();
                    req.ContentLength = 0;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        string bodyPreview = "";
                        string fullBody = "";
                        try
                        {
                            using (var reader = new StreamReader(resp.GetResponseStream(), System.Text.Encoding.UTF8))
                            {
                                fullBody = reader.ReadToEnd();
                                bodyPreview = fullBody.Length <= MaxResponseBodyLogLength ? fullBody : fullBody.Substring(0, MaxResponseBodyLogLength) + "...";
                            }
                        }
                        catch (Exception) { bodyPreview = "(read error)"; }
                        int code = (int)resp.StatusCode;
                        ModLogger.Info("DedicatedServerCommands: HTTP " + managerUrl + " → " + code + " " + (resp.StatusDescription ?? "") + " | body: " + bodyPreview);

                        if (WebPanelAuth.IsLoginPageResponse(fullBody))
                        {
                            ModLogger.Info("DedicatedServerCommands: got login page (attempt " + (attempt + 1) + "), re-login and retry once.");
                            if (attempt == 1) return false;
                            continue;
                        }
                        if (resp.StatusCode == HttpStatusCode.OK || (code >= 200 && code < 300))
                            return true;
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    var webEx = ex as WebException;
                    if (webEx != null)
                    {
                        string detail = "Status=" + webEx.Status + " " + (webEx.InnerException != null ? "Inner=" + webEx.InnerException.Message : "");
                        if (webEx.Response is HttpWebResponse r)
                            detail += " ResponseStatusCode=" + (int)r.StatusCode;
                        ModLogger.Info("DedicatedServerCommands: HTTP " + managerUrl + " — " + detail);
                    }
                    else
                        ModLogger.Info("DedicatedServerCommands: HTTP " + managerUrl + " — " + ex.Message);
                    if (attempt == 0) continue;
                    return false;
                }
            }
            return false;
        }

        /// <summary>Зручні обгортки для бойового циклу.</summary>
        public static bool SendStartMission() => SendCommand("start_mission");
        public static bool SendEndMission() => SendCommand("end_mission");
    }
}
