using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.GameMode
{
    /// <summary>
    /// Допоміжні методи для додавання mission behaviors. TryCreateBehavior — тільки з Multiplayer.
    /// TryCreateBehaviorFromAnyAssembly — лише для типів з allowlist (без пошуку за коротким іменем), з кешем Type.
    /// Обов'язкові для UI: MissionOptionsComponent, MissionBoundaryCrossingHandler, MissionHardBorderPlacer, MissionBoundaryPlacer.
    /// </summary>
    internal static class MissionBehaviorHelpers
    {
        private static Assembly MultiplayerAssembly => typeof(MissionLobbyComponent).Assembly;

        /// <summary>Дозволені повні імена типів для пошуку по всіх збірках (TaleWorlds.MountAndBlade — base assembly). Без allowlist не шукаємо.</summary>
        private static readonly HashSet<string> AnyAssemblyAllowlist = new HashSet<string>(StringComparer.Ordinal)
        {
            "TaleWorlds.MountAndBlade.MissionOptionsComponent",
            "TaleWorlds.MountAndBlade.MissionBoundaryCrossingHandler",
            "TaleWorlds.MountAndBlade.MissionHardBorderPlacer",
            "TaleWorlds.MountAndBlade.MissionBoundaryPlacer",
        };

        /// <summary>Кеш знайдених типів (fullName → Type), щоб не сканувати assemblies щоразу.</summary>
        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>(StringComparer.Ordinal);

        private static bool _missionOptionsDumpDone;

        /// <summary>Повертає рядок з сигнатурами всіх public instance конструкторів типу (для діагностики).</summary>
        private static string GetConstructorSignatures(Type type)
        {
            if (type == null) return "";
            var sb = new StringBuilder();
            foreach (var c in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                var ps = c.GetParameters();
                sb.Append("(").Append(string.Join(", ", ps.Select(p => p.ParameterType.Name ?? "?"))).Append("); ");
            }
            return sb.ToString();
        }

        /// <summary>Створює behavior за повним ім'ям типу з збірки Multiplayer. Повертає null і логує, якщо тип не знайдено або створення не вдалося.</summary>
        public static MissionBehavior TryCreateBehavior(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName)) return null;
            try
            {
                Type type = MultiplayerAssembly.GetType(fullTypeName);
                if (type == null)
                {
                    ModLogger.Info("MissionBehaviorHelpers: type not found in Multiplayer assembly: " + fullTypeName);
                    return null;
                }
                object obj = Activator.CreateInstance(type);
                if (obj is MissionBehavior behavior)
                    return behavior;
                ModLogger.Info("MissionBehaviorHelpers: created " + fullTypeName + " but is not MissionBehavior.");
                return null;
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionBehaviorHelpers: Could not create " + fullTypeName + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>Шукає тип тільки серед allowlist-кандидатів по всіх завантажених збірках; кешує Type. Повертає null, якщо fullTypeName не в allowlist або тип не знайдено.</summary>
        public static MissionBehavior TryCreateBehaviorFromAnyAssembly(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName)) return null;
            if (!AnyAssemblyAllowlist.Contains(fullTypeName))
                return null;

            lock (TypeCache)
            {
                if (TypeCache.TryGetValue(fullTypeName, out Type cached) && cached != null)
                {
                    try
                    {
                        object obj = Activator.CreateInstance(cached);
                        if (obj is MissionBehavior behavior)
                            return behavior;
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Info("MissionBehaviorHelpers: CreateInstance from cache failed for " + fullTypeName + ": " + ex.Message);
                    }
                    return null;
                }
            }

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                Type type = asm.GetType(fullTypeName, throwOnError: false);
                if (type == null) continue;
                if (!typeof(MissionBehavior).IsAssignableFrom(type)) continue;
                lock (TypeCache)
                {
                    if (!TypeCache.ContainsKey(fullTypeName))
                        TypeCache[fullTypeName] = type;
                }
                try
                {
                    object obj = Activator.CreateInstance(type);
                    if (obj is MissionBehavior behavior)
                    {
                        ModLogger.Info("MissionBehaviorHelpers: " + type.Name + " created from assembly " + asm.GetName().Name);
                        return behavior;
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Info("MissionBehaviorHelpers: Could not create " + fullTypeName + " from " + asm.GetName().Name + ": " + ex.Message);
                }
                return null;
            }

            ModLogger.Info("MissionBehaviorHelpers: " + fullTypeName + " not found in any loaded assembly (allowlist).");
            return null;
        }

        /// <summary>MissionOptionsComponent — потрібен Gauntlet. Smart resolver по short name; ctor(Mission) або parameterless; optional при невдачі.</summary>
        public static MissionBehavior TryCreateMissionOptionsComponent(Mission mission)
        {
            const string shortName = "MissionOptionsComponent";
            const string multiName = "TaleWorlds.MountAndBlade.Multiplayer.MissionOptionsComponent";
            const string baseName = "TaleWorlds.MountAndBlade.MissionOptionsComponent";

            // Одноразовий дамп: усі типи, де FullName містить MissionOption або OptionsComponent (для діагностики неймінгу/модуля).
            if (!_missionOptionsDumpDone)
            {
                _missionOptionsDumpDone = true;
                try
                {
                    var candidates = new List<string>();
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (asm.IsDynamic) continue;
                        try
                        {
                            foreach (var t in asm.GetTypes())
                            {
                                string fn = t.FullName ?? "";
                                if (fn.IndexOf("MissionOption", StringComparison.OrdinalIgnoreCase) >= 0 || fn.IndexOf("OptionsComponent", StringComparison.OrdinalIgnoreCase) >= 0)
                                    candidates.Add(t.FullName + " in " + asm.GetName().Name);
                            }
                        }
                        catch (ReflectionTypeLoadException) { }
                    }
                    ModLogger.Info("MissionBehaviorHelpers: [MissionOptionsComponent dump] types containing MissionOption or OptionsComponent: " + (candidates.Count == 0 ? "(none)" : string.Join(" | ", candidates)));
                }
                catch (Exception ex) { ModLogger.Info("MissionBehaviorHelpers: MissionOptionsComponent dump failed: " + ex.Message); }
            }

            // 1) Як раніше: жорсткі FullName (Multiplayer, потім allowlist)
            Type type = MultiplayerAssembly.GetType(multiName, throwOnError: false);
            if (type != null && typeof(MissionBehavior).IsAssignableFrom(type))
            {
                MissionBehavior created = TryCreateMissionOptionsFromType(type, mission, multiName, MultiplayerAssembly.GetName().Name);
                if (created != null) return created;
            }
            type = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                type = asm.GetType(baseName, throwOnError: false);
                if (type != null && typeof(MissionBehavior).IsAssignableFrom(type)) break;
                type = null;
            }
            if (type != null)
            {
                MissionBehavior created = TryCreateMissionOptionsFromType(type, mission, baseName, type.Assembly.GetName().Name);
                if (created != null) return created;
            }

            // 2) Smart resolver: по short name у всіх збірках
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name != shortName && (t.FullName == null || !t.FullName.EndsWith("." + shortName, StringComparison.Ordinal))) continue;
                        if (!typeof(MissionBehavior).IsAssignableFrom(t)) continue;
                        ModLogger.Info("MissionBehaviorHelpers: Found candidate type " + t.FullName + " in " + asm.GetName().Name + ".");
                        MissionBehavior created = TryCreateMissionOptionsFromType(t, mission, t.FullName, asm.GetName().Name);
                        if (created != null) return created;
                        break; // один кандидат на збірку
                    }
                }
                catch (ReflectionTypeLoadException) { }
            }

            ModLogger.Info("MissionBehaviorHelpers: MissionOptionsComponent could not be created (optional; mission will continue without it).");
            return null;
        }

        /// <summary>Пробує створити MissionOptionsComponent з типу: ctor(Mission), потім parameterless. При невдачі логує сигнатури ctor.</summary>
        private static MissionBehavior TryCreateMissionOptionsFromType(Type type, Mission mission, string fullName, string assemblyName)
        {
            if (type == null) return null;
            // 1) ctor(Mission)
            if (mission != null)
            {
                foreach (var c in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
                {
                    var p = c.GetParameters();
                    if (p.Length == 1 && p[0].ParameterType == typeof(Mission))
                    {
                        try
                        {
                            object obj = c.Invoke(new object[] { mission });
                            if (obj is MissionBehavior behavior)
                            {
                                ModLogger.Info("MissionBehaviorHelpers: MissionOptionsComponent created via ctor(Mission) from " + assemblyName + ".");
                                return behavior;
                            }
                        }
                        catch (Exception ex)
                        {
                            ModLogger.Info("MissionBehaviorHelpers: MissionOptionsComponent ctor(Mission) failed for " + fullName + ": " + ex.Message);
                        }
                        break; // вже спробували ctor(Mission), далі parameterless
                    }
                }
            }
            // 2) parameterless
            try
            {
                object obj = Activator.CreateInstance(type);
                if (obj is MissionBehavior behavior)
                {
                    ModLogger.Info("MissionBehaviorHelpers: MissionOptionsComponent created via parameterless ctor from " + assemblyName + ".");
                    return behavior;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionBehaviorHelpers: MissionOptionsComponent could not create " + fullName + ": " + ex.Message + ". Constructors: " + GetConstructorSignatures(type));
            }
            return null;
        }

        /// <summary>MissionBoundaryCrossingHandler — потрібен BoundaryCrossingVM. Спроба: Multiplayer, потім allowlist; ctor(Mission), fallback parameterless ctor.</summary>
        public static MissionBehavior TryCreateBoundaryCrossingHandler(Mission mission)
        {
            MissionBehavior fromMultiplayer = TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MissionBoundaryCrossingHandler");
            if (fromMultiplayer != null)
            {
                ModLogger.Info("MissionBehaviorHelpers: MissionBoundaryCrossingHandler created via Multiplayer assembly (parameterless).");
                return fromMultiplayer;
            }
            const string fullName = "TaleWorlds.MountAndBlade.MissionBoundaryCrossingHandler";
            if (!AnyAssemblyAllowlist.Contains(fullName)) return null;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                Type type = asm.GetType(fullName, throwOnError: false);
                if (type == null || !typeof(MissionBehavior).IsAssignableFrom(type)) continue;

                // 1) Спробувати ctor(Mission)
                ConstructorInfo ctorMission = null;
                foreach (var c in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
                {
                    var p = c.GetParameters();
                    if (p.Length == 1 && p[0].ParameterType == typeof(Mission))
                    {
                        ctorMission = c;
                        break;
                    }
                }
                if (ctorMission != null && mission != null)
                {
                    try
                    {
                        object obj = ctorMission.Invoke(new object[] { mission });
                        if (obj is MissionBehavior behavior)
                        {
                            ModLogger.Info("MissionBehaviorHelpers: MissionBoundaryCrossingHandler created via ctor(Mission) from " + asm.GetName().Name + ".");
                            return behavior;
                        }
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Info("MissionBehaviorHelpers: MissionBoundaryCrossingHandler ctor(Mission) failed for " + asm.GetName().Name + ": " + ex.Message);
                    }
                }
                else if (ctorMission == null)
                    ModLogger.Info("MissionBehaviorHelpers: MissionBoundaryCrossingHandler has no ctor(Mission) in " + asm.GetName().Name + ", trying parameterless.");

                // 2) Fallback: parameterless ctor
                try
                {
                    object obj = Activator.CreateInstance(type);
                    if (obj is MissionBehavior behavior)
                    {
                        ModLogger.Info("MissionBehaviorHelpers: MissionBoundaryCrossingHandler created via parameterless ctor from " + asm.GetName().Name + ".");
                        return behavior;
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Info("MissionBehaviorHelpers: MissionBoundaryCrossingHandler parameterless ctor failed for " + asm.GetName().Name + ": " + ex.Message);
                }
            }
            ModLogger.Info("MissionBehaviorHelpers: MissionBoundaryCrossingHandler could not be created (optional; mission will continue without it).");
            return null;
        }

        /// <summary>MissionHardBorderPlacer — обов'язковий для коректної роботи boundary. Спершу Multiplayer, потім allowlist.</summary>
        public static MissionBehavior TryCreateHardBorderPlacer()
        {
            return TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MissionHardBorderPlacer")
                ?? TryCreateBehaviorFromAnyAssembly("TaleWorlds.MountAndBlade.MissionHardBorderPlacer");
        }

        /// <summary>MissionBoundaryPlacer — обов'язковий для boundary. Спершу Multiplayer, потім allowlist.</summary>
        public static MissionBehavior TryCreateBoundaryPlacer()
        {
            return TryCreateBehavior("TaleWorlds.MountAndBlade.Multiplayer.MissionBoundaryPlacer")
                ?? TryCreateBehaviorFromAnyAssembly("TaleWorlds.MountAndBlade.MissionBoundaryPlacer");
        }
    }
}
