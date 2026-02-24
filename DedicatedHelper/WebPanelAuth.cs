using System; // Exception
using System.IO; // StreamReader
using System.Net; // HttpWebRequest, CookieContainer, WebException
using System.Text.RegularExpressions; // Regex для витягування __RequestVerificationToken
using CoopSpectator.Infrastructure; // ModLogger

namespace CoopSpectator.DedicatedHelper // Авторизація в web panel дедик-сервера (ASP.NET Core Auth + cookie)
{
    /// <summary>
    /// Логін у web panel (GET /Auth → POST login) для подальших GET /Manager/start_mission та end_mission.
    /// Healthcheck по 127.0.0.1, localhost, ::1; перша робоча база кешується. Proxy=null, стабільні таймаути.
    /// </summary>
    public static class WebPanelAuth
    {
        private const int HttpTimeoutMs = 3000;

        private static readonly CookieContainer CookieContainer = new CookieContainer();
        private static string _cachedBaseUrl;
        private static int _cachedPort = -1;

        /// <summary>Базові адреси для healthcheck (IPv4, localhost, IPv6).</summary>
        private static readonly string[] BaseHosts = new[] { "127.0.0.1", "localhost", "[::1]" };

        public static CookieContainer GetCookieContainer() => CookieContainer;

        /// <summary>Повертає робочу базову URL (наприклад http://127.0.0.1:7210). Якщо порт змінився або ще не визначали — робить healthcheck.</summary>
        public static string GetBaseUrl(int port)
        {
            if (_cachedBaseUrl != null && _cachedPort == port)
                return _cachedBaseUrl;
            _cachedPort = port;
            _cachedBaseUrl = null;
            if (!EnsureBaseUrl(port))
                return null;
            return _cachedBaseUrl;
        }

        /// <summary>Healthcheck: перебирає BaseHosts, перший успішний GET зберігає в _cachedBaseUrl.</summary>
        private static bool EnsureBaseUrl(int port)
        {
            foreach (string host in BaseHosts)
            {
                string baseUrl = "http://" + host + ":" + port;
                try
                {
                    ModLogger.Info("WebPanelAuth: healthcheck GET " + baseUrl + "/");
                    var req = (HttpWebRequest)WebRequest.Create(baseUrl + "/");
                    ApplyStableRequestSettings(req);
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        _cachedBaseUrl = baseUrl;
                        ModLogger.Info("WebPanelAuth: dashboard reachable at " + baseUrl);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    LogWebException("WebPanelAuth: healthcheck " + baseUrl, ex);
                }
            }
            ModLogger.Info("WebPanelAuth: dashboard unreachable (tried 127.0.0.1, localhost, [::1]).");
            return false;
        }

        private static void ApplyStableRequestSettings(HttpWebRequest req)
        {
            req.Proxy = null;
            req.Timeout = HttpTimeoutMs;
            req.ReadWriteTimeout = HttpTimeoutMs;
            req.KeepAlive = false;
            req.AllowAutoRedirect = false;
        }

        private static void LogWebException(string prefix, Exception ex)
        {
            var webEx = ex as WebException;
            if (webEx != null)
            {
                string detail = "Status=" + webEx.Status + " " + (webEx.InnerException != null ? "Inner=" + webEx.InnerException.Message : "");
                if (webEx.Response is HttpWebResponse r)
                    detail += " ResponseStatusCode=" + (int)r.StatusCode;
                ModLogger.Info(prefix + " — " + detail);
            }
            else
                ModLogger.Info(prefix + " — " + ex.Message);
        }

        /// <summary>Переконатися, що ми залогінені: healthcheck → GET Auth (302 = вже залогінені) → POST login → опційно GET /Manager.</summary>
        public static bool EnsureSignedIn(string adminPassword, int port)
        {
            if (string.IsNullOrEmpty(adminPassword))
            {
                ModLogger.Info("WebPanelAuth: admin password empty, skip login.");
                return false;
            }
            string baseUrl = GetBaseUrl(port);
            if (string.IsNullOrEmpty(baseUrl))
                return false;
            string authUrl = baseUrl + "/Auth?ReturnUrl=%2F";
            try
            {
                // 1) GET сторінки логіну
                string verificationToken = null;
                ModLogger.Info("WebPanelAuth: GET " + authUrl);
                var getReq = (HttpWebRequest)WebRequest.Create(authUrl);
                getReq.Method = "GET";
                ApplyStableRequestSettings(getReq);
                getReq.CookieContainer = CookieContainer;
                using (var getResp = (HttpWebResponse)getReq.GetResponse())
                {
                    if ((int)getResp.StatusCode >= 300 && (int)getResp.StatusCode < 400)
                    {
                        ModLogger.Info("WebPanelAuth: GET Auth → " + getResp.StatusCode + " (already signed in).");
                        return true;
                    }
                    using (var reader = new StreamReader(getResp.GetResponseStream(), System.Text.Encoding.UTF8))
                    {
                        string html = reader.ReadToEnd();
                        verificationToken = ExtractRequestVerificationToken(html);
                    }
                }
                if (string.IsNullOrEmpty(verificationToken))
                {
                    ModLogger.Info("WebPanelAuth: __RequestVerificationToken not found in login page.");
                    return false;
                }

                // 2) POST логін
                ModLogger.Info("WebPanelAuth: POST " + authUrl);
                var postReq = (HttpWebRequest)WebRequest.Create(authUrl);
                postReq.Method = "POST";
                ApplyStableRequestSettings(postReq);
                postReq.CookieContainer = CookieContainer;
                postReq.ContentType = "application/x-www-form-urlencoded";
                string formBody = "password=" + Uri.EscapeDataString(adminPassword) + "&__RequestVerificationToken=" + Uri.EscapeDataString(verificationToken);
                byte[] bodyBytes = System.Text.Encoding.UTF8.GetBytes(formBody);
                postReq.ContentLength = bodyBytes.Length;
                using (var stream = postReq.GetRequestStream())
                    stream.Write(bodyBytes, 0, bodyBytes.Length);
                using (var postResp = (HttpWebResponse)postReq.GetResponse())
                {
                    int code = (int)postResp.StatusCode;
                    ModLogger.Info("WebPanelAuth: POST login → " + code + " " + (postResp.StatusDescription ?? ""));
                    if (code >= 300 && code < 400)
                    {
                        ModLogger.Info("WebPanelAuth: login OK (redirect), cookie stored.");
                        // 3) Опційно GET /Manager для стабілізації сесії
                        try
                        {
                            string managerUrl = baseUrl + "/Manager";
                            ModLogger.Info("WebPanelAuth: GET " + managerUrl + " (stabilize session)");
                            var mgrReq = (HttpWebRequest)WebRequest.Create(managerUrl);
                            mgrReq.Method = "GET";
                            ApplyStableRequestSettings(mgrReq);
                            mgrReq.CookieContainer = CookieContainer;
                            using (mgrReq.GetResponse()) { }
                        }
                        catch (Exception ex) { LogWebException("WebPanelAuth: GET /Manager", ex); }
                        return true;
                    }
                    if (code >= 200 && code < 300)
                    {
                        ModLogger.Info("WebPanelAuth: login returned 2xx, assume OK.");
                        return true;
                    }
                    ModLogger.Info("WebPanelAuth: login failed with " + code);
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogWebException("WebPanelAuth: EnsureSignedIn", ex);
                return false;
            }
        }

        private static string ExtractRequestVerificationToken(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;
            var match = Regex.Match(html, @"__RequestVerificationToken[^>]*value\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1)
                return match.Groups[1].Value;
            return null;
        }

        public static bool IsLoginPageResponse(string responseBody)
        {
            if (string.IsNullOrEmpty(responseBody)) return false;
            return responseBody.IndexOf("__RequestVerificationToken", StringComparison.OrdinalIgnoreCase) >= 0
                   || responseBody.IndexOf("Auth?ReturnUrl", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
