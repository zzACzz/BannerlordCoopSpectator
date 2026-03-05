// Harmony-патчі для DedicatedCustomServerWebPanelSubModule: прибираємо блокування threadpool (_host.Run() у Task.Run)
// та додаємо коректний stop/dispose при unload. Fix реалізовано через Harmony, не змінюючи вихідний код TaleWorlds.
using System;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using CoopSpectator.Infrastructure;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Патчі для TaleWorlds.MountAndBlade.DedicatedCustomServer.WebPanel.DedicatedCustomServerWebPanelSubModule:
    /// 1) DedicatedCustomGameServerStateActivated — запуск WebHost на окремому Thread (не Task.Run), щоб не блокувати threadpool.
    /// 2) OnSubModuleUnloaded — StopAsync + Dispose хоста.
    /// </summary>
    public static class DedicatedWebPanelPatches
    {
        private const string TargetTypeName = "TaleWorlds.MountAndBlade.DedicatedCustomServer.WebPanel.DedicatedCustomServerWebPanelSubModule";
        private const string HostFieldName = "_host";

        private static bool _runningOnDedicatedThread;
        private static MethodInfo _activationMethod;

        public static void Apply(Harmony harmony)
        {
            try
            {
                Type targetType = AccessTools.TypeByName(TargetTypeName);
                if (targetType == null)
                {
                    ModLogger.Info("DedicatedWebPanelPatches: type " + TargetTypeName + " not found (WebPanel assembly not loaded?). Skip.");
                    return;
                }

                MethodInfo activated = AccessTools.Method(targetType, "DedicatedCustomGameServerStateActivated");
                if (activated == null)
                {
                    ModLogger.Info("DedicatedWebPanelPatches: DedicatedCustomGameServerStateActivated not found. Skip.");
                    return;
                }
                _activationMethod = activated;

                MethodInfo unloaded = AccessTools.Method(targetType, "OnSubModuleUnloaded");
                if (unloaded == null)
                {
                    ModLogger.Info("DedicatedWebPanelPatches: OnSubModuleUnloaded not found. Skip.");
                }
                else
                {
                    var postfix = typeof(DedicatedWebPanelPatches).GetMethod(nameof(OnSubModuleUnloaded_Postfix), BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(unloaded, postfix: new HarmonyMethod(postfix));
                    ModLogger.Info("DedicatedWebPanelPatches: OnSubModuleUnloaded postfix applied.");
                }

                var prefix = typeof(DedicatedWebPanelPatches).GetMethod(nameof(DedicatedCustomGameServerStateActivated_Prefix), BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(activated, prefix: new HarmonyMethod(prefix));
                ModLogger.Info("DedicatedWebPanelPatches: DedicatedCustomGameServerStateActivated prefix applied (WebPanel will run on dedicated thread, not threadpool).");
            }
            catch (Exception ex)
            {
                ModLogger.Error("DedicatedWebPanelPatches.Apply failed.", ex);
            }
        }

        /// <summary>Prefix: якщо вже запущено на нашому потокі — дати виконати оригінал. Якщо _host != null — повторна активація, skip. Інакше запустити оригінал на окремому Thread (IsBackground), щоб _host.Run() не блокував threadpool.</summary>
        public static bool DedicatedCustomGameServerStateActivated_Prefix(object __instance)
        {
            if (__instance == null) return true;

            try
            {
                Type t = __instance.GetType();
                FieldInfo hostField = AccessTools.Field(t, HostFieldName);
                if (hostField == null) return true;

                object host = hostField.GetValue(__instance);
                if (host != null)
                {
                    ModLogger.Info("DedicatedWebPanelPatches: WebPanel already started (reentrancy), skip.");
                    return false;
                }

                if (_runningOnDedicatedThread)
                {
                    return true;
                }

                if (_activationMethod == null) return true;

                _runningOnDedicatedThread = true;
                object instance = __instance;
                MethodInfo original = _activationMethod;
                Thread th = new Thread(() =>
                {
                    try
                    {
                        original.Invoke(instance, null);
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Info("DedicatedWebPanelPatches: WebPanel thread invoke failed: " + ex.Message);
                    }
                    finally
                    {
                        _runningOnDedicatedThread = false;
                    }
                })
                { IsBackground = true, Name = "CoopSpectator-WebPanelHost" };
                th.Start();
                ModLogger.Info("DedicatedWebPanelPatches: WebPanel host started on dedicated thread (avoids threadpool starvation).");
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedWebPanelPatches: Prefix failed: " + ex.Message);
                _runningOnDedicatedThread = false;
                return true;
            }
        }

        /// <summary>Postfix на OnSubModuleUnloaded: зупинити і звільнити WebHost.</summary>
        public static void OnSubModuleUnloaded_Postfix(object __instance)
        {
            if (__instance == null) return;
            try
            {
                Type t = __instance.GetType();
                FieldInfo hostField = AccessTools.Field(t, HostFieldName);
                if (hostField == null) return;

                object host = hostField.GetValue(__instance);
                if (host == null) return;

                try
                {
                    MethodInfo stopAsync = host.GetType().GetMethod("StopAsync", Type.EmptyTypes)
                        ?? host.GetType().GetMethod("StopAsync", new Type[] { typeof(CancellationToken) });
                    if (stopAsync != null)
                    {
                        object task = stopAsync.GetParameters().Length == 0
                            ? stopAsync.Invoke(host, null)
                            : stopAsync.Invoke(host, new object[] { default(CancellationToken) });
                        ModLogger.Info("DedicatedWebPanelPatches: WebPanel StopAsync invoked.");
                    }
                }
                catch (Exception ex) { ModLogger.Info("DedicatedWebPanelPatches: StopAsync failed: " + ex.Message); }

                try
                {
                    MethodInfo dispose = host.GetType().GetMethod("Dispose", Type.EmptyTypes);
                    if (dispose != null)
                    {
                        dispose.Invoke(host, null);
                        ModLogger.Info("DedicatedWebPanelPatches: WebPanel host disposed.");
                    }
                }
                catch (Exception ex) { ModLogger.Info("DedicatedWebPanelPatches: Dispose failed: " + ex.Message); }

                try { hostField.SetValue(__instance, null); } catch { }
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedWebPanelPatches: OnSubModuleUnloaded postfix failed: " + ex.Message);
            }
        }
    }
}
