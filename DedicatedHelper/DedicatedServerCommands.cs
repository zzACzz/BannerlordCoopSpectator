using System; // Exception
using System.IO; // StreamReader
using System.Net; // HttpWebRequest, WebRequest, WebException
using System.Threading; // Thread.Sleep
using CoopSpectator.Campaign; // BattleRosterFileHelper
using CoopSpectator.Infrastructure; // ModLogger, UiFeedback, CoopGameModeIds
using CoopSpectator.Network.Messages; // BattleSnapshotMessage
using Newtonsoft.Json.Linq; // JToken, JObject, JArray
using TaleWorlds.MountAndBlade;

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

        /// <summary>Зручні обгортки для бойового циклу. GameType задається в конфігу дедика (rotation); тут лише лог для перевірки узгодження ID.</summary>
        public static bool SendStartMission()
        {
            TryLogAvailableServerOptionsViaHttp();
            TryApplySceneAwareMissionSelectionFromBattleRoster();
            ModLogger.Info("DedicatedServerCommands: SendStartMission [ID check] expected GameTypeId on dedicated=" + CoopGameModeIds.OfficialBattle + " for battle-map runtime, fallback custom ids otherwise.");
            return SendCommand("start_mission");
        }
        public static bool SendEndMission() => SendCommand("end_mission");

        private static void TryApplySceneAwareMissionSelectionFromBattleRoster()
        {
            try
            {
                BattleSnapshotMessage snapshot = BattleRosterFileHelper.ReadSnapshot();
                if (snapshot == null)
                {
                    ModLogger.Info("DedicatedServerCommands: scene-aware mission selection skipped (battle snapshot missing).");
                    return;
                }

                string requestedScene = snapshot.MultiplayerScene;
                string requestedGameType = snapshot.MultiplayerGameType;
                bool exactCampaignScene = CampaignToMultiplayerSceneResolver.IsCampaignBattleScene(requestedScene);
                string appliedGameType = string.Equals(requestedGameType, CoopGameModeIds.OfficialBattle, StringComparison.Ordinal)
                    ? CoopGameModeIds.OfficialBattle
                    : CoopGameModeIds.CoopBattle;

                if (string.IsNullOrWhiteSpace(requestedScene))
                {
                    ModLogger.Info("DedicatedServerCommands: scene-aware mission selection skipped (snapshot MultiplayerScene missing).");
                    return;
                }

                if (!DedicatedHelperLauncher.HasDedicatedProcess())
                {
                    ModLogger.Info(
                        "DedicatedServerCommands: scene-aware mission selection skipped (no local dedicated stdin). " +
                        "RequestedScene=" + requestedScene +
                        " RequestedGameType=" + (requestedGameType ?? "unknown") +
                        " AppliedGameType=" + appliedGameType + ".");
                    return;
                }

                if (exactCampaignScene)
                {
                    bool preRegisteredExactScene = DedicatedHelperLauncher.TrySendConsoleLine("add_map_to_usable_maps " + requestedScene + " " + appliedGameType);
                    ModLogger.Info(
                        "DedicatedServerCommands: pre-registered exact campaign runtime scene before start_mission. " +
                        "RequestedScene=" + requestedScene +
                        " AppliedGameType=" + appliedGameType +
                        " AddMapSent=" + preRegisteredExactScene + ".");
                }

                ModLogger.Info(
                    "DedicatedServerCommands: applying scene-aware mission selection before start_mission. " +
                    "RequestedScene=" + requestedScene +
                    " RequestedGameType=" + (requestedGameType ?? "unknown") +
                    " AppliedGameType=" + appliedGameType +
                    " ResolverSource=" + (snapshot.MultiplayerSceneResolverSource ?? "unknown") + ".");

                if (TryApplySceneAwareMissionSelectionViaWebOptions(requestedScene, requestedGameType, appliedGameType))
                    return;

                bool addMapSent = DedicatedHelperLauncher.TrySendConsoleLine("add_map_to_usable_maps " + requestedScene + " " + appliedGameType);
                bool gameTypeSent = DedicatedHelperLauncher.TrySendConsoleLine("GameType " + appliedGameType);
                bool mapSent = DedicatedHelperLauncher.TrySendConsoleLine("Map " + requestedScene);

                Thread.Sleep(100);

                ModLogger.Info(
                    "DedicatedServerCommands: scene-aware mission selection command results. " +
                    "AddMapSent=" + addMapSent +
                    " GameTypeSent=" + gameTypeSent +
                    " MapSent=" + mapSent +
                    " RequestedScene=" + requestedScene +
                    " AppliedGameType=" + appliedGameType + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedServerCommands: scene-aware mission selection failed. " + ex.Message);
            }
        }

        private static bool TryApplySceneAwareMissionSelectionViaWebOptions(string requestedScene, string requestedGameType, string appliedGameType)
        {
            try
            {
                string baseUrl = WebPanelAuth.GetBaseUrl(DashboardPort);
                if (string.IsNullOrEmpty(baseUrl))
                {
                    ModLogger.Info("DedicatedServerCommands: set_options scene-aware apply skipped (dashboard unreachable).");
                    return false;
                }

                string password = DedicatedHelperLauncher.GetDashboardAdminPassword();
                if (!WebPanelAuth.EnsureSignedIn(password, DashboardPort))
                {
                    ModLogger.Info("DedicatedServerCommands: set_options scene-aware apply skipped (auth failed).");
                    return false;
                }

                JObject optionValues = TryBuildOptionValuesPayload(baseUrl);
                if (optionValues == null || !optionValues.HasValues)
                {
                    ModLogger.Info("DedicatedServerCommands: set_options scene-aware apply skipped (option payload missing).");
                    return false;
                }

                optionValues["GameType"] = appliedGameType;
                optionValues["Map"] = requestedScene;
                if (string.Equals(appliedGameType, CoopGameModeIds.OfficialBattle, StringComparison.Ordinal))
                {
        // CoopBattle owns side/unit selection, deployment, and the explicit H-start flow.
                    // Disable native Battle/TDM warmup countdown so the client does not keep showing
                    // "Warmup Phase / Waiting for players to join" over our own pre-battle phases.
                    // Also neutralize native round/map timeout auto-end so our authoritative
                    // battle completion, not the Battle shell, decides when the mission ends.
                    // Values must remain inside native network compression bounds so late-joining
                    // peers can deserialize current mission options successfully.
                    optionValues["WarmupTimeLimitInSeconds"] =
                        MultiplayerOptions.OptionType.WarmupTimeLimitInSeconds.GetMinimumValue();
                    optionValues["RoundPreparationTimeLimit"] =
                        MultiplayerOptions.OptionType.RoundPreparationTimeLimit.GetMinimumValue();
                    optionValues["MapTimeLimit"] =
                        MultiplayerOptions.OptionType.MapTimeLimit.GetMaximumValue();
                    optionValues["RoundTimeLimit"] =
                        MultiplayerOptions.OptionType.RoundTimeLimit.GetMaximumValue();
                }

                string setOptionsUrl = baseUrl + "/Manager/set_options";
                string payloadJson = optionValues.ToString(Newtonsoft.Json.Formatting.None);
                ModLogger.Info(
                    "DedicatedServerCommands: POST " + setOptionsUrl +
                    " (scene-aware apply). GameType=" + appliedGameType +
                    " Map=" + requestedScene +
                    " MapTimeLimit=" + (optionValues["MapTimeLimit"]?.ToString() ?? "unchanged") +
                    " RoundTimeLimit=" + (optionValues["RoundTimeLimit"]?.ToString() ?? "unchanged") +
                    " WarmupTimeLimitInSeconds=" + (optionValues["WarmupTimeLimitInSeconds"]?.ToString() ?? "unchanged") +
                    " RoundPreparationTimeLimit=" + (optionValues["RoundPreparationTimeLimit"]?.ToString() ?? "unchanged") +
                    " RequestedGameType=" + (requestedGameType ?? "unknown") + ".");

                var req = (HttpWebRequest)WebRequest.Create(setOptionsUrl);
                req.Method = "POST";
                req.Proxy = null;
                req.Timeout = 3000;
                req.ReadWriteTimeout = 3000;
                req.KeepAlive = false;
                req.AllowAutoRedirect = false;
                req.CookieContainer = WebPanelAuth.GetCookieContainer();
                req.ContentType = "application/json";

                byte[] payloadBytes = System.Text.Encoding.UTF8.GetBytes(payloadJson);
                req.ContentLength = payloadBytes.Length;
                using (var stream = req.GetRequestStream())
                    stream.Write(payloadBytes, 0, payloadBytes.Length);

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(), System.Text.Encoding.UTF8))
                {
                    string body = reader.ReadToEnd() ?? string.Empty;
                    string bodyPreview = body.Length <= MaxResponseBodyLogLength
                        ? body
                        : body.Substring(0, MaxResponseBodyLogLength) + "...";
                    ModLogger.Info(
                        "DedicatedServerCommands: set_options scene-aware apply response = " +
                        (int)resp.StatusCode + " " + (resp.StatusDescription ?? string.Empty) +
                        " | body: " + bodyPreview);
                }

                string summaryAfterApply = TryGetCurrentOptionsSummary(baseUrl);
                if (!string.IsNullOrWhiteSpace(summaryAfterApply))
                    ModLogger.Info("DedicatedServerCommands: get_options parsed summary after scene-aware apply = " + summaryAfterApply);

                bool selectionVerified = TryVerifyAppliedSceneAwareMissionSelection(baseUrl, requestedScene, appliedGameType);
                ModLogger.Info(
                    "DedicatedServerCommands: scene-aware apply verification. " +
                    "RequestedScene=" + requestedScene +
                    " AppliedGameType=" + appliedGameType +
                    " Verified=" + selectionVerified + ".");
                return selectionVerified;
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedServerCommands: set_options scene-aware apply failed. " + ex.Message);
                return false;
            }
        }

        private static bool TryVerifyAppliedSceneAwareMissionSelection(string baseUrl, string requestedScene, string appliedGameType)
        {
            try
            {
                JObject payload = TryBuildOptionValuesPayload(baseUrl);
                if (payload == null || !payload.HasValues)
                {
                    ModLogger.Info("DedicatedServerCommands: scene-aware apply verification failed (current options payload missing).");
                    return false;
                }

                string currentScene = payload.Value<string>("Map") ?? string.Empty;
                string currentGameType = payload.Value<string>("GameType") ?? string.Empty;
                bool sceneMatches = string.Equals(currentScene, requestedScene ?? string.Empty, StringComparison.Ordinal);
                bool gameTypeMatches = string.Equals(currentGameType, appliedGameType ?? string.Empty, StringComparison.Ordinal);
                if (!sceneMatches || !gameTypeMatches)
                {
                    ModLogger.Info(
                        "DedicatedServerCommands: scene-aware apply verification mismatch. " +
                        "ExpectedMap=" + (requestedScene ?? string.Empty) +
                        " ActualMap=" + currentScene +
                        " ExpectedGameType=" + (appliedGameType ?? string.Empty) +
                        " ActualGameType=" + currentGameType + ".");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedServerCommands: scene-aware apply verification failed. " + ex.Message);
                return false;
            }
        }

        private static void TryLogAvailableServerOptionsViaHttp()
        {
            try
            {
                string baseUrl = WebPanelAuth.GetBaseUrl(DashboardPort);
                if (string.IsNullOrEmpty(baseUrl))
                {
                    ModLogger.Info("DedicatedServerCommands: get_options skipped (dashboard unreachable).");
                    return;
                }

                string password = DedicatedHelperLauncher.GetDashboardAdminPassword();
                if (!WebPanelAuth.EnsureSignedIn(password, DashboardPort))
                {
                    ModLogger.Info("DedicatedServerCommands: get_options skipped (auth failed).");
                    return;
                }

                string optionsUrl = baseUrl + "/Manager/get_options";
                ModLogger.Info("DedicatedServerCommands: GET " + optionsUrl + " (scene-aware diagnostics).");
                var req = (HttpWebRequest)WebRequest.Create(optionsUrl);
                req.Method = "GET";
                req.Proxy = null;
                req.Timeout = 3000;
                req.ReadWriteTimeout = 3000;
                req.KeepAlive = false;
                req.AllowAutoRedirect = false;
                req.CookieContainer = WebPanelAuth.GetCookieContainer();
                req.ContentLength = 0;

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(), System.Text.Encoding.UTF8))
                {
                    string body = reader.ReadToEnd() ?? string.Empty;
                    string bodyPreview = body.Length <= MaxResponseBodyLogLength
                        ? body
                        : body.Substring(0, MaxResponseBodyLogLength) + "...";
                    ModLogger.Info("DedicatedServerCommands: get_options response preview = " + bodyPreview);

                    string parsedSummary = TryBuildOptionsDiagnosticSummary(body);
                    if (!string.IsNullOrWhiteSpace(parsedSummary))
                        ModLogger.Info("DedicatedServerCommands: get_options parsed summary = " + parsedSummary);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedServerCommands: get_options diagnostics failed. " + ex.Message);
            }
        }

        private static JObject TryBuildOptionValuesPayload(string baseUrl)
        {
            string body = TryGetOptionsResponseBody(baseUrl);
            if (string.IsNullOrWhiteSpace(body))
                return null;

            try
            {
                JToken token = JToken.Parse(body);
                if (!(token is JArray array))
                    return null;

                var payload = new JObject();
                foreach (JToken item in array)
                {
                    if (!(item is JObject optionObject))
                        continue;

                    string name = optionObject.Value<string>("name") ?? optionObject.Value<string>("Name");
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    JToken currentValueToken = optionObject["currentValue"] ?? optionObject["CurrentValue"];
                    JToken defaultValueToken = optionObject["defaultValue"] ?? optionObject["DefaultValue"];
                    JToken chosenValueToken = currentValueToken ?? defaultValueToken;
                    if (chosenValueToken == null || chosenValueToken.Type == JTokenType.Null)
                        continue;

                    payload[name] = chosenValueToken.Type == JTokenType.String
                        ? (JToken)(chosenValueToken.Value<string>() ?? string.Empty)
                        : chosenValueToken.DeepClone();
                }

                return payload;
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedServerCommands: failed to build option payload from get_options. " + ex.Message);
                return null;
            }
        }

        private static string TryGetCurrentOptionsSummary(string baseUrl)
        {
            string body = TryGetOptionsResponseBody(baseUrl);
            if (string.IsNullOrWhiteSpace(body))
                return null;

            return TryBuildOptionsDiagnosticSummary(body);
        }

        private static string TryGetOptionsResponseBody(string baseUrl)
        {
            try
            {
                string optionsUrl = baseUrl + "/Manager/get_options";
                var req = (HttpWebRequest)WebRequest.Create(optionsUrl);
                req.Method = "GET";
                req.Proxy = null;
                req.Timeout = 3000;
                req.ReadWriteTimeout = 3000;
                req.KeepAlive = false;
                req.AllowAutoRedirect = false;
                req.CookieContainer = WebPanelAuth.GetCookieContainer();
                req.ContentLength = 0;

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(), System.Text.Encoding.UTF8))
                    return reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedServerCommands: get_options body fetch failed. " + ex.Message);
                return null;
            }
        }

        private static string TryBuildOptionsDiagnosticSummary(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return "empty";

            try
            {
                JToken token = JToken.Parse(body);
                if (token is JArray array)
                {
                    var summaries = new System.Collections.Generic.List<string>();
                    foreach (JToken item in array)
                    {
                        if (!(item is JObject optionObject))
                            continue;

                        string name = optionObject.Value<string>("name") ?? optionObject.Value<string>("Name") ?? "?";
                        string defaultValue = optionObject.Value<string>("defaultValue") ?? optionObject.Value<string>("DefaultValue") ?? "?";
                        string currentValue = optionObject.Value<string>("currentValue") ?? optionObject.Value<string>("CurrentValue") ?? string.Empty;
                        summaries.Add(name + "=" + (!string.IsNullOrWhiteSpace(currentValue) ? currentValue : defaultValue));
                    }

                    return "[" + string.Join("; ", summaries) + "]";
                }

                if (token is JObject objectToken)
                    return objectToken.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch (Exception ex)
            {
                return "parse-failed:" + ex.GetType().Name;
            }

            return "unsupported-json";
        }
    }
}
