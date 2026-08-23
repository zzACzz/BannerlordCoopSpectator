using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using TaleWorlds.DotNet;

namespace CoopSpectator.DedicatedServer
{
    internal static class SandBoxSceneScriptTypeRegistrar
    {
        private const string CampaignSystemAssemblyFileName = "TaleWorlds.CampaignSystem.dll";
        private const string CampaignSystemAssemblySimpleName = "TaleWorlds.CampaignSystem";
        private const string SandBoxAssemblyFileName = "SandBox.dll";
        private const string SandBoxAssemblySimpleName = "SandBox";

        private static readonly object RegistrationLock = new object();
        private static bool _isRegistered;

        public static void RegisterOrThrow()
        {
            lock (RegistrationLock)
            {
                if (_isRegistered)
                    return;

                bool isDedicatedServer = IsDedicatedServerProcess();
                string moduleBinDirectory = ResolveModuleBinDirectory();
                Assembly campaignSystemAssembly = LoadAssemblyOrThrow(
                    Path.Combine(moduleBinDirectory, CampaignSystemAssemblyFileName),
                    CampaignSystemAssemblySimpleName);
                Assembly sandBoxAssembly = LoadAssemblyOrThrow(
                    Path.Combine(moduleBinDirectory, SandBoxAssemblyFileName),
                    SandBoxAssemblySimpleName);

                Type[] sandBoxTypes = GetAssemblyTypesOrThrow(sandBoxAssembly);
                List<SandBoxSceneScriptRegistrationCandidate> candidates = sandBoxTypes
                    .Select(type => new SandBoxSceneScriptRegistrationCandidate(
                        type.Name,
                        type.FullName,
                        type.Assembly.FullName,
                        typeof(ManagedObject).IsAssignableFrom(type) ||
                        typeof(DotNetObject).IsAssignableFrom(type)))
                    .ToList();

                IDictionary<string, Type> registeredEngineTypes = GetRegisteredEngineTypesOrThrow();
                List<SandBoxSceneScriptRegisteredType> registeredTypes = registeredEngineTypes
                    .Select(pair => new SandBoxSceneScriptRegisteredType(
                        pair.Key,
                        pair.Value?.FullName,
                        pair.Value?.Assembly?.FullName))
                    .ToList();

                SandBoxSceneScriptRegistrationDecision decision =
                    SandBoxSceneScriptRegistrationContract.Resolve(
                        isDedicatedServer,
                        sandBoxAssembly.GetName().Name,
                        candidates,
                        registeredTypes);
                if (decision.Kind == SandBoxSceneScriptRegistrationDecisionKind.Reject)
                {
                    throw new InvalidOperationException(
                        "Dedicated SandBox scene-script registration rejected: " + decision.Reason + ".");
                }

                if (decision.Kind == SandBoxSceneScriptRegistrationDecisionKind.Register)
                {
                    HashSet<string> selectedNames = new HashSet<string>(
                        decision.TypeNamesToRegister,
                        StringComparer.Ordinal);
                    Dictionary<string, Type> typesToRegister = sandBoxTypes
                        .Where(type => selectedNames.Contains(type.Name))
                        .ToDictionary(type => type.Name, type => type, StringComparer.Ordinal);
                    if (typesToRegister.Count != decision.TypeNamesToRegister.Count)
                    {
                        throw new InvalidOperationException(
                            "Dedicated SandBox scene-script registration plan could not be materialized exactly. " +
                            "Planned=" + decision.TypeNamesToRegister.Count +
                            " Resolved=" + typesToRegister.Count + ".");
                    }

                    Managed.AddTypes(typesToRegister);
                }

                _isRegistered = true;
                ModLogger.Info(
                    "CoopSpectatorDedicated: SandBox scene-script engine types registered before mission materialization. " +
                    "Decision=" + decision.Kind +
                    " Reason=" + decision.Reason +
                    " RegisteredNow=" + decision.TypeNamesToRegister.Count +
                    " SandBoxManagedCandidates=" + candidates.Count(candidate => candidate.IsManagedEngineType) +
                    " SandBoxAssembly=" + sandBoxAssembly.FullName +
                    " CampaignSystemAssembly=" + campaignSystemAssembly.FullName + ".");
            }
        }

        private static bool IsDedicatedServerProcess()
        {
            object dedicatedServerType =
                TaleWorlds.MountAndBlade.Module.CurrentModule?.StartupInfo?.DedicatedServerType;
            return dedicatedServerType != null &&
                   !string.Equals(
                       dedicatedServerType.ToString(),
                       "None",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveModuleBinDirectory()
        {
            string assemblyLocation = Assembly.GetExecutingAssembly().Location;
            string directory = string.IsNullOrWhiteSpace(assemblyLocation)
                ? string.Empty
                : Path.GetDirectoryName(assemblyLocation);
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("Dedicated module bin directory could not be resolved.");

            return directory;
        }

        private static Assembly LoadAssemblyOrThrow(string path, string expectedSimpleName)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Required dedicated scene-script assembly is missing.", path);

            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(candidate => string.Equals(
                    candidate.GetName().Name,
                    expectedSimpleName,
                    StringComparison.Ordinal));
            if (assembly == null)
                assembly = Assembly.LoadFrom(path);

            string actualSimpleName = assembly.GetName().Name;
            if (!string.Equals(actualSimpleName, expectedSimpleName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Dedicated scene-script assembly identity mismatch. Expected=" +
                    expectedSimpleName + " Actual=" + actualSimpleName + ".");
            }

            return assembly;
        }

        private static Type[] GetAssemblyTypesOrThrow(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                string loaderErrors = string.Join(
                    " | ",
                    ex.LoaderExceptions
                        .Where(error => error != null)
                        .Select(error => error.GetType().Name + ":" + error.Message)
                        .Distinct());
                throw new InvalidOperationException(
                    "SandBox scene-script types could not be loaded completely. " + loaderErrors,
                    ex);
            }
        }

        private static IDictionary<string, Type> GetRegisteredEngineTypesOrThrow()
        {
            PropertyInfo moduleTypesProperty = typeof(Managed).GetProperty(
                "ModuleTypes",
                BindingFlags.Static | BindingFlags.NonPublic);
            IDictionary<string, Type> moduleTypes =
                moduleTypesProperty?.GetValue(null, null) as IDictionary<string, Type>;
            if (moduleTypes == null)
                throw new InvalidOperationException("Bannerlord managed engine type registry is unavailable.");

            return moduleTypes;
        }
    }
}
