using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    /// <summary>
    /// Одноразовий reflection-dump API GameTextManager та валідація ключа (BeforeLoad/AfterLoad). Використовується при COOP_DEBUG_TEXTS=1 та для двофазної перевірки.
    /// </summary>
    public static class GameTextManagerDebugHelper
    {
        private static bool _dumpDone;

        private static void LogDebugTexts(string message)
        {
            ModLogger.Info("CoopSpectator DEBUG_TEXTS: " + message);
        }

        /// <summary>Один раз за процес: dump усіх методів GameTextManager (public+nonpublic, static+instance) та окремо список методів з параметром string.</summary>
        public static void DumpGameTextManagerApiOnce(Type gameTextManagerType)
        {
            if (gameTextManagerType == null) return;
            if (_dumpDone) return;
            _dumpDone = true;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            MethodInfo[] methods = gameTextManagerType.GetMethods(flags);
            var withString = new List<MethodInfo>();

            LogDebugTexts("GameTextManager API dump begin. Type=" + gameTextManagerType.FullName);
            var sb = new StringBuilder();
            foreach (MethodInfo m in methods)
            {
                string sig = MethodSignature(m);
                sb.Append("  ").AppendLine(sig);
                var p = m.GetParameters();
                if (p.Length >= 1 && p[0].ParameterType == typeof(string))
                    withString.Add(m);
            }
            LogDebugTexts("All methods (" + methods.Length + "):");
            foreach (string line in sb.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                LogDebugTexts(line.Trim().Length > 0 ? line : "(blank)");
            LogDebugTexts("GameTextManager API dump end.");

            LogDebugTexts("Methods with first param string (" + withString.Count + "):");
            foreach (MethodInfo m in withString)
                LogDebugTexts("  " + MethodSignature(m));
        }

        private static string MethodSignature(MethodInfo m)
        {
            var p = m.GetParameters();
            var ps = new string[p.Length];
            for (int i = 0; i < p.Length; i++)
                ps[i] = (p[i].IsOut ? "out " : "") + p[i].ParameterType.Name + " " + (p[i].Name ?? "p" + i);
            string returns = m.ReturnType?.Name ?? "void";
            return (m.IsStatic ? "static " : "") + returns + " " + m.Name + "(" + string.Join(", ", ps) + ")";
        }

        /// <summary>Перевіряє наявність ключа через GetText/TryGetText. Повертає true, якщо текст знайдено.</summary>
        public static bool ValidateKey(object gameTextManager, Type gmType, string testId)
        {
            if (gameTextManager == null || gmType == null || string.IsNullOrEmpty(testId)) return false;
            try
            {
                var getText = gmType.GetMethod("GetText", new[] { typeof(string) });
                if (getText != null)
                {
                    var obj = getText.Invoke(gameTextManager, new object[] { testId });
                    if (obj != null && (obj.ToString() ?? "").Trim().Length > 0) return true;
                }
                foreach (var m in gmType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name != "TryGetText" && m.Name != "FindText") continue;
                    var par = m.GetParameters();
                    if (par.Length >= 1 && par[0].ParameterType == typeof(string))
                    {
                        var args = new object[par.Length];
                        args[0] = testId;
                        for (int i = 1; i < args.Length; i++) args[i] = null;
                        var result = m.Invoke(gameTextManager, args);
                        if ((result is bool b && b) || (args.Length > 1 && args[1] != null)) return true;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("LoadGameTexts: ValidateKey failed: " + ex.Message);
            }
            return false;
        }

        /// <summary>Шукає loader: спочатку LoadGameTexts(string), інакше методи void (string) або void (string,...) з назвою, що містить "Load". Викликає перший безпечний; повертає ім'я методу або null.</summary>
        public static string TryFindAndInvokeLoader(object gameTextManager, Type gmType, string path, bool autoFindFromDump)
        {
            if (gameTextManager == null || gmType == null || string.IsNullOrEmpty(path)) return null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

            // 1) Явний LoadGameTexts(string)
            var loadGameTexts = gmType.GetMethod("LoadGameTexts", new[] { typeof(string) })
                ?? gmType.GetMethod("LoadGameTexts", flags, null, new[] { typeof(string) }, null);
            if (loadGameTexts != null)
            {
                try
                {
                    loadGameTexts.Invoke(gameTextManager, new object[] { path });
                    return "LoadGameTexts(string)";
                }
                catch (Exception ex)
                {
                    ModLogger.Error("LoadGameTexts: LoadGameTexts(path) invoke failed.", ex);
                    return null;
                }
            }

            if (!autoFindFromDump) return null;

            // 2) Авто-пошук: void (string) або void (string, ...), назва містить "Load" (виключаємо GetText)
            foreach (var m in gmType.GetMethods(flags))
            {
                if (!m.Name.Contains("Load") || m.Name.Contains("Get")) continue;
                var p = m.GetParameters();
                if (p.Length < 1 || p[0].ParameterType != typeof(string)) continue;
                if (m.ReturnType != null && m.ReturnType != typeof(void)) continue;
                object[] args = new object[p.Length];
                args[0] = path;
                for (int i = 1; i < args.Length; i++) args[i] = null;
                try
                {
                    m.Invoke(gameTextManager, args);
                    LogDebugTexts("Loader chosen: " + m.Name + "(" + (p.Length == 1 ? "string" : "string,...") + ")");
                    return m.Name;
                }
                catch (Exception ex)
                {
                    LogDebugTexts("Try " + m.Name + " failed: " + ex.Message);
                }
            }
            return null;
        }
    }
}
