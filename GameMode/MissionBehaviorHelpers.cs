using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Multiplayer;

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

        /// <summary>Клієнт-онлі тип: на dedicated/server відсутній. Шукаємо в Multiplayer, потім у базовій збірці, потім по короткому імені в усіх збірках; на сервері не викликати.</summary>
        public static MissionBehavior TryCreateMissionAgentVisualSpawnComponent()
        {
            const string multiFullName = "TaleWorlds.MountAndBlade.Multiplayer.MultiplayerMissionAgentVisualSpawnComponent";
            const string baseFullName = "TaleWorlds.MountAndBlade.MultiplayerMissionAgentVisualSpawnComponent";
            const string shortName = "MultiplayerMissionAgentVisualSpawnComponent";

            // 1) Multiplayer assembly (стандартний namespace)
            MissionBehavior b = TryCreateBehavior(multiFullName);
            if (b != null)
            {
                ModLogger.Info("MissionBehaviorHelpers: MultiplayerMissionAgentVisualSpawnComponent created from Multiplayer assembly.");
                return b;
            }

            // 2) Базова збірка TaleWorlds.MountAndBlade (деякі build мають тип там)
            try
            {
                Type baseType = typeof(Mission).Assembly.GetType(baseFullName, throwOnError: false);
                if (baseType != null && typeof(MissionBehavior).IsAssignableFrom(baseType))
                {
                    object obj = Activator.CreateInstance(baseType);
                    if (obj is MissionBehavior behavior)
                    {
                        ModLogger.Info("MissionBehaviorHelpers: MultiplayerMissionAgentVisualSpawnComponent created from TaleWorlds.MountAndBlade assembly.");
                        return behavior;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionBehaviorHelpers: MultiplayerMissionAgentVisualSpawnComponent base assembly try failed: " + ex.Message);
            }

            // 3) Пошук по короткому імені в усіх завантажених збірках
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                Type type = asm.GetType(multiFullName, throwOnError: false) ?? asm.GetType(baseFullName, throwOnError: false);
                if (type == null)
                {
                    try
                    {
                        type = asm.GetExportedTypes().FirstOrDefault(t => t.Name == shortName && typeof(MissionBehavior).IsAssignableFrom(t));
                    }
                    catch { /* ignore */ }
                }
                if (type == null || !typeof(MissionBehavior).IsAssignableFrom(type)) continue;
                try
                {
                    object obj = Activator.CreateInstance(type);
                    if (obj is MissionBehavior behavior)
                    {
                        ModLogger.Info("MissionBehaviorHelpers: MultiplayerMissionAgentVisualSpawnComponent created from assembly " + asm.GetName().Name);
                        return behavior;
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Info("MissionBehaviorHelpers: CreateInstance " + type.FullName + " from " + asm.GetName().Name + " failed: " + ex.Message);
                }
            }

            ModLogger.Info("MissionBehaviorHelpers: MultiplayerMissionAgentVisualSpawnComponent not found in any assembly; using safe fallback (do not add MissionLobbyEquipmentNetworkComponent).");
            return null;
        }

        /// <summary>Перевіряє, чи в списку є behavior з заданим коротким іменем типу.</summary>
        public static bool ListContainsBehaviorType(List<MissionBehavior> list, string typeShortName)
        {
            if (list == null || string.IsNullOrEmpty(typeShortName)) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].GetType().Name == typeShortName)
                    return true;
            }
            return false;
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
        public static MissionBehavior TryCreateBehaviorFromMountAndBlade(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName)) return null;
            try
            {
                Type type = typeof(Mission).Assembly.GetType(fullTypeName, throwOnError: false);
                if (type == null)
                {
                    ModLogger.Info("MissionBehaviorHelpers: type not found in TaleWorlds.MountAndBlade assembly: " + fullTypeName);
                    return null;
                }
                object obj = Activator.CreateInstance(type);
                if (obj is MissionBehavior behavior)
                {
                    ModLogger.Info("MissionBehaviorHelpers: " + type.Name + " created from TaleWorlds.MountAndBlade assembly.");
                    return behavior;
                }
                ModLogger.Info("MissionBehaviorHelpers: created " + fullTypeName + " from TaleWorlds.MountAndBlade but is not MissionBehavior.");
                return null;
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionBehaviorHelpers: Could not create from TaleWorlds.MountAndBlade " + fullTypeName + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>Шукає тип за повним ім'ям у всіх already-loaded assemblies і створює його через parameterless ctor.</summary>
        public static MissionBehavior TryCreateBehaviorFromLoadedAssemblies(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName)) return null;

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;

                Type type = null;
                try
                {
                    type = asm.GetType(fullTypeName, throwOnError: false);
                }
                catch (Exception ex)
                {
                    ModLogger.Info("MissionBehaviorHelpers: assembly scan failed for " + asm.GetName().Name + " / " + fullTypeName + ": " + ex.Message);
                }

                if (type == null || !typeof(MissionBehavior).IsAssignableFrom(type))
                    continue;

                try
                {
                    object obj = Activator.CreateInstance(type);
                    if (obj is MissionBehavior behavior)
                    {
                        ModLogger.Info("MissionBehaviorHelpers: " + type.Name + " created from loaded assembly " + asm.GetName().Name + ".");
                        return behavior;
                    }

                    ModLogger.Info("MissionBehaviorHelpers: created " + fullTypeName + " from loaded assembly " + asm.GetName().Name + " but is not MissionBehavior.");
                    return null;
                }
                catch (Exception ex)
                {
                    ModLogger.Info("MissionBehaviorHelpers: Could not create " + fullTypeName + " from loaded assembly " + asm.GetName().Name + ": " + ex.Message);
                    return null;
                }
            }

            ModLogger.Info("MissionBehaviorHelpers: type not found in loaded assemblies: " + fullTypeName);
            return null;
        }

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
        public static MissionBehavior TryCreateMissionAgentLabelUiHandler(Mission mission)
        {
            return TryCreateViewCreatorBehavior(
                "CreateMissionAgentLabelUIHandler",
                new[] { typeof(Mission) },
                new object[] { mission },
                "MissionAgentLabelUIHandler");
        }

        public static MissionBehavior TryCreateMissionAgentLabelUiParityView(Mission mission)
        {
            MissionBehavior createdByViewCreator = TryCreateMissionAgentLabelUiHandler(mission);
            if (createdByViewCreator != null)
                return createdByViewCreator;

            MissionBehavior directView = TryCreateBehaviorFromLoadedAssemblies("TaleWorlds.MountAndBlade.View.MissionViews.MissionAgentLabelView");
            if (directView != null)
            {
                ModLogger.Info("MissionBehaviorHelpers: MissionAgentLabelView created directly from loaded assemblies as ViewCreator fallback.");
                return directView;
            }

            return null;
        }

        public static MissionBehavior TryCreateMissionFormationMarkerUiHandler(Mission mission)
        {
            return TryCreateViewCreatorBehavior(
                "CreateMissionFormationMarkerUIHandler",
                new[] { typeof(Mission) },
                new object[] { mission },
                "MissionFormationMarkerUIHandler");
        }

        public static MissionBehavior TryCreateMissionFormationMarkerUiParityView(Mission mission)
        {
            MissionBehavior createdByViewCreator = TryCreateMissionFormationMarkerUiHandler(mission);
            if (createdByViewCreator != null)
                return createdByViewCreator;

            MissionBehavior directHandler = TryCreateBehaviorFromLoadedAssemblies("TaleWorlds.MountAndBlade.View.MissionViews.Singleplayer.MissionFormationMarkerUIHandler");
            if (directHandler != null)
            {
                ModLogger.Info("MissionBehaviorHelpers: MissionFormationMarkerUIHandler created directly from loaded assemblies as ViewCreator fallback.");
                return directHandler;
            }

            MissionBehavior directGauntletView = TryCreateBehaviorFromLoadedAssemblies("TaleWorlds.MountAndBlade.GauntletUI.Mission.Singleplayer.MissionGauntletFormationMarker");
            if (directGauntletView != null)
            {
                ModLogger.Info("MissionBehaviorHelpers: MissionGauntletFormationMarker created directly from loaded assemblies as handler fallback.");
                return directGauntletView;
            }

            return null;
        }

        private static MissionBehavior TryCreateViewCreatorBehavior(
            string methodName,
            Type[] parameterTypes,
            object[] arguments,
            string behaviorLabel)
        {
            try
            {
                Type viewCreatorType =
                    Type.GetType("TaleWorlds.MountAndBlade.View.ViewCreator, TaleWorlds.MountAndBlade.View", throwOnError: false)
                    ?? TryResolveTypeFromLoadedAssemblies("TaleWorlds.MountAndBlade.View.ViewCreator");
                if (viewCreatorType == null)
                {
                    ModLogger.Info("MissionBehaviorHelpers: " + behaviorLabel + " creation skipped because TaleWorlds.MountAndBlade.View is unavailable in current runtime.");
                    return null;
                }

                MethodInfo createMethod = viewCreatorType.GetMethod(
                    methodName,
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: parameterTypes,
                    modifiers: null);
                if (createMethod == null)
                {
                    ModLogger.Info("MissionBehaviorHelpers: " + behaviorLabel + " creation skipped because ViewCreator." + methodName + " was not found.");
                    return null;
                }

                if (createMethod.Invoke(null, arguments) is MissionBehavior behavior)
                {
                    return behavior;
                }

                ModLogger.Info("MissionBehaviorHelpers: " + behaviorLabel + " creation returned null or non-MissionBehavior.");
                return null;
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionBehaviorHelpers: " + behaviorLabel + " creation failed: " + ex.Message);
                return null;
            }
        }

        private static Type TryResolveTypeFromLoadedAssemblies(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName))
                return null;

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic)
                    continue;

                try
                {
                    Type type = asm.GetType(fullTypeName, throwOnError: false);
                    if (type != null)
                        return type;
                }
                catch (Exception ex)
                {
                    ModLogger.Info("MissionBehaviorHelpers: type scan failed for " + asm.GetName().Name + " / " + fullTypeName + ": " + ex.Message);
                }
            }

            return null;
        }

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

        /// <summary>MissionBoundaryCrossingHandler — обов'язковий для BoundaryCrossingVM (ванільний UI крашиться без нього). Ваніль: TaleWorlds.MountAndBlade.MissionBoundaryCrossingHandler, ctor(float leewayTime=10f).</summary>
        public static MissionBehavior TryCreateBoundaryCrossingHandler(Mission mission)
        {
            const string baseFullName = "TaleWorlds.MountAndBlade.MissionBoundaryCrossingHandler";
            const string multiFullName = "TaleWorlds.MountAndBlade.Multiplayer.MissionBoundaryCrossingHandler";
            const float VanillaLeewayTime = 10f;

            // 1) Ванільний тип у базовій збірці MountAndBlade (API 1.3.4: ctor(float leewayTime=10f))
            Type baseType = typeof(Mission).Assembly.GetType(baseFullName, throwOnError: false);
            if (baseType != null && typeof(MissionBehavior).IsAssignableFrom(baseType))
            {
                MissionBehavior created = TryCreateBoundaryCrossingHandlerFromType(baseType, baseType.Assembly.GetName().Name, mission, VanillaLeewayTime);
                if (created != null) return created;
            }

            // 2) Тип у Multiplayer (якщо є окремий клас)
            Type multiType = MultiplayerAssembly.GetType(multiFullName, throwOnError: false);
            if (multiType != null && typeof(MissionBehavior).IsAssignableFrom(multiType))
            {
                MissionBehavior created = TryCreateBoundaryCrossingHandlerFromType(multiType, MultiplayerAssembly.GetName().Name, mission, VanillaLeewayTime);
                if (created != null) return created;
            }

            // 3) Пошук по всіх збірках (allowlist)
            Type lastTriedType = baseType ?? multiType;
            if (AnyAssemblyAllowlist.Contains(baseFullName))
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.IsDynamic) continue;
                    Type type = asm.GetType(baseFullName, throwOnError: false);
                    if (type == null || !typeof(MissionBehavior).IsAssignableFrom(type)) continue;
                    lastTriedType = type;
                    MissionBehavior created = TryCreateBoundaryCrossingHandlerFromType(type, asm.GetName().Name, mission, VanillaLeewayTime);
                    if (created != null) return created;
                }
            }

            ModLogger.Info("MissionBehaviorHelpers: MissionBoundaryCrossingHandler could not be created. BoundaryCrossingVM will crash without it. Expected ctor(float leewayTime=10f) or parameterless. Signatures: " + GetConstructorSignatures(lastTriedType));
            return null;
        }

        /// <summary>Пробує створити handler: ctor(float) з leewayTime=10f (ваніль), ctor(Mission), потім parameterless.</summary>
        private static MissionBehavior TryCreateBoundaryCrossingHandlerFromType(Type type, string assemblyName, Mission mission, float leewayTime)
        {
            if (type == null) return null;
            var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            // 1) ctor(float) — як у ванілі (float leewayTime=10f)
            foreach (var c in ctors)
            {
                var p = c.GetParameters();
                if (p.Length == 1 && (p[0].ParameterType == typeof(float) || p[0].ParameterType == typeof(double)))
                {
                    try
                    {
                        object arg = p[0].ParameterType == typeof(double) ? (object)(double)leewayTime : (object)leewayTime;
                        object obj = c.Invoke(new object[] { arg });
                        if (obj is MissionBehavior behavior)
                        {
                            ModLogger.Info("MissionBehaviorHelpers: MissionBoundaryCrossingHandler created via ctor(float) from " + assemblyName + ".");
                            return behavior;
                        }
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Info("MissionBehaviorHelpers: MissionBoundaryCrossingHandler ctor(float) failed for " + assemblyName + ": " + ex.Message);
                    }
                    break;
                }
            }
            // 2) ctor(Mission) — деякі збірки можуть мати такий варіант
            if (mission != null)
            {
                foreach (var c in ctors)
                {
                    var p = c.GetParameters();
                    if (p.Length == 1 && p[0].ParameterType == typeof(Mission))
                    {
                        try
                        {
                            object obj = c.Invoke(new object[] { mission });
                            if (obj is MissionBehavior behavior)
                            {
                                ModLogger.Info("MissionBehaviorHelpers: MissionBoundaryCrossingHandler created via ctor(Mission) from " + assemblyName + ".");
                                return behavior;
                            }
                        }
                        catch (Exception ex)
                        {
                            ModLogger.Info("MissionBehaviorHelpers: MissionBoundaryCrossingHandler ctor(Mission) failed for " + assemblyName + ": " + ex.Message);
                        }
                        break;
                    }
                }
            }
            // 3) parameterless (C# optional float leewayTime=10f виглядає як parameterless)
            try
            {
                object obj = Activator.CreateInstance(type);
                if (obj is MissionBehavior behavior)
                {
                    ModLogger.Info("MissionBehaviorHelpers: MissionBoundaryCrossingHandler created via parameterless ctor from " + assemblyName + ".");
                    return behavior;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionBehaviorHelpers: MissionBoundaryCrossingHandler parameterless failed for " + assemblyName + ": " + ex.Message);
            }
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

        /// <summary>MissionScoreboardComponent — потрібен для MissionCustomGameServerComponent.AfterStart на dedicated. Повертає null при помилці (інший build/assembly).</summary>
        public static MissionBehavior TryCreateMissionScoreboardComponent()
        {
            try
            {
                return new MissionScoreboardComponent(new TDMScoreboardData());
            }
            catch (Exception ex)
            {
                ModLogger.Error("[TdmCloneStack] MissionScoreboardComponent create failed (MissionCustomGameServerComponent.AfterStart may crash): " + ex.Message, ex);
                return null;
            }
        }
    }
}

