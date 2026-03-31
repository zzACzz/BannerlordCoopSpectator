using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using TaleWorlds.Engine;
using IOPath = System.IO.Path;

namespace CoopSpectator.Infrastructure
{
    public static class DedicatedSceneContractProbe
    {
        private static readonly string[] ProbeScenes =
        {
            "mp_battle_map_001",
            "mp_battle_map_002",
            "battle_terrain_n",
            "battle_terrain_biome_087b"
        };

        private const string SandBoxCoreModuleName = "SandBoxCore";
        private const string SandBoxModuleName = "SandBox";
        private const string CampaignSystemAssemblyName = "TaleWorlds.CampaignSystem";
        private static bool _hasRunStartupProbe;

        public static void RunStartupProbe()
        {
            if (_hasRunStartupProbe || !ExperimentalFeatures.EnableDedicatedSceneContractProbe)
                return;

            _hasRunStartupProbe = true;

            ModLogger.Info("DedicatedSceneContractProbe: begin startup probe.");
            TryLogLoadedModules();
            TryLogOwnedScenes("Multiplayer");
            TryLogOwnedScenes("SandBoxCore");
            TryLogSceneResolution();
            TryRunExactCampaignBootstrapProbe();
            ModLogger.Info("DedicatedSceneContractProbe: end startup probe.");
        }

        private static void TryLogLoadedModules()
        {
            try
            {
                string[] modules = NormalizeStringArray(Utilities.GetModulesNames());
                ModLogger.Info(
                    "DedicatedSceneContractProbe: loaded modules. " +
                    "Count=" + modules.Length +
                    " HasMultiplayer=" + Contains(modules, "Multiplayer") +
                    " HasSandBoxCore=" + Contains(modules, "SandBoxCore") +
                    " Modules=" + FormatArray(modules, 24));
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedSceneContractProbe: loaded modules probe failed: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private static void TryLogOwnedScenes(string moduleName)
        {
            try
            {
                string[] scenes = NormalizeStringArray(Utilities.GetSingleModuleScenesOfModule(moduleName));
                ModLogger.Info(
                    "DedicatedSceneContractProbe: module-owned scenes. " +
                    "Module=" + moduleName +
                    " Count=" + scenes.Length +
                    " ContainsMpBattleMap001=" + Contains(scenes, "mp_battle_map_001") +
                    " ContainsBattleTerrainN=" + Contains(scenes, "battle_terrain_n") +
                    " ContainsBattleTerrainBiome087b=" + Contains(scenes, "battle_terrain_biome_087b") +
                    " Sample=" + FormatArray(scenes, 16));
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "DedicatedSceneContractProbe: module-owned scenes probe failed. " +
                    "Module=" + moduleName +
                    " Exception=" + ex.GetType().Name + " " + ex.Message);
            }
        }

        private static void TryLogSceneResolution()
        {
            foreach (string sceneName in ProbeScenes)
            {
                try
                {
                    bool pathResolved = Utilities.TryGetFullFilePathOfScene(sceneName, out string fullPath);
                    bool uniqueSceneIdResolved = Utilities.TryGetUniqueIdentifiersForScene(sceneName, out var uniqueSceneId);

                    ModLogger.Info(
                        "DedicatedSceneContractProbe: scene resolution. " +
                        "Scene=" + sceneName +
                        " PathResolved=" + pathResolved +
                        " FullPath=" + Safe(fullPath) +
                        " UniqueSceneIdResolved=" + uniqueSceneIdResolved +
                        " UniqueSceneId=" + DescribeObject(uniqueSceneId));
                }
                catch (Exception ex)
                {
                    ModLogger.Info(
                        "DedicatedSceneContractProbe: scene resolution probe failed. " +
                        "Scene=" + sceneName +
                        " Exception=" + ex.GetType().Name + " " + ex.Message);
                }
            }
        }

        private static void TryRunExactCampaignBootstrapProbe()
        {
            if (!ExperimentalFeatures.EnableDedicatedExactCampaignSceneBootstrapProbe)
                return;

            ModLogger.Info("DedicatedSceneContractProbe: begin exact campaign bootstrap probe.");
            TryLogExactCampaignRuntimeFiles();
            TryLogCampaignAssemblyAvailability();
            TryLogSpBattleSceneRegistry();
            TryLogExactSceneManualPairProbe();
            ModLogger.Info("DedicatedSceneContractProbe: end exact campaign bootstrap probe.");
        }

        private static void TryLogExactCampaignRuntimeFiles()
        {
            try
            {
                string rawEngineBasePath = Utilities.GetBasePath();
                string engineBasePath = string.IsNullOrWhiteSpace(rawEngineBasePath) ? string.Empty : rawEngineBasePath;
                string processBaseDirectory = Safe(AppDomain.CurrentDomain.BaseDirectory);
                string sandboxModulePath = IOPath.Combine(engineBasePath, "Modules", SandBoxModuleName);
                string sandboxCoreModulePath = IOPath.Combine(engineBasePath, "Modules", SandBoxCoreModuleName);
                string spBattleScenesXmlPath = IOPath.Combine(sandboxModulePath, "ModuleData", "sp_battle_scenes.xml");
                string campaignSystemServerDll = IOPath.Combine(engineBasePath, "bin", "Win64_Shipping_Server", CampaignSystemAssemblyName + ".dll");
                string campaignSystemClientDll = IOPath.Combine(engineBasePath, "bin", "Win64_Shipping_Client", CampaignSystemAssemblyName + ".dll");

                ModLogger.Info(
                    "DedicatedSceneContractProbe: exact bootstrap runtime files. " +
                    "EngineBasePath=" + Safe(rawEngineBasePath) +
                    " ProcessBaseDirectory=" + processBaseDirectory +
                    " HasSandBoxModule=" + Directory.Exists(sandboxModulePath) +
                    " HasSandBoxCoreModule=" + Directory.Exists(sandboxCoreModulePath) +
                    " HasSpBattleScenesXml=" + File.Exists(spBattleScenesXmlPath) +
                    " HasCampaignSystemServerDll=" + File.Exists(campaignSystemServerDll) +
                    " HasCampaignSystemClientDll=" + File.Exists(campaignSystemClientDll) +
                    " HasBattleTerrainNScene=" + File.Exists(GetExpectedSceneXScenePath(engineBasePath, "battle_terrain_n", SandBoxCoreModuleName)) +
                    " HasBattleTerrainBiome087bScene=" + File.Exists(GetExpectedSceneXScenePath(engineBasePath, "battle_terrain_biome_087b", SandBoxCoreModuleName)));
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedSceneContractProbe: exact bootstrap runtime file probe failed: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private static void TryLogCampaignAssemblyAvailability()
        {
            try
            {
                string[] loadedAssemblyNames = GetLoadedAssemblyNames();
                Type gameSceneDataManagerType = Type.GetType("TaleWorlds.CampaignSystem.GameSceneDataManager, TaleWorlds.CampaignSystem", throwOnError: false);
                Type defaultSceneModelType = Type.GetType("TaleWorlds.CampaignSystem.GameComponents.DefaultSceneModel, TaleWorlds.CampaignSystem", throwOnError: false);

                ModLogger.Info(
                    "DedicatedSceneContractProbe: campaign assembly availability. " +
                    "HasLoadedCampaignSystemAssembly=" + Contains(loadedAssemblyNames, CampaignSystemAssemblyName) +
                    " GameSceneDataManagerTypeResolved=" + (gameSceneDataManagerType != null) +
                    " DefaultSceneModelTypeResolved=" + (defaultSceneModelType != null) +
                    " LoadedAssemblySample=" + FormatArray(loadedAssemblyNames, 24));
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedSceneContractProbe: campaign assembly availability probe failed: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private static void TryLogSpBattleSceneRegistry()
        {
            try
            {
                string rawEngineBasePath = Utilities.GetBasePath();
                string engineBasePath = string.IsNullOrWhiteSpace(rawEngineBasePath) ? string.Empty : rawEngineBasePath;
                string registryPath = IOPath.Combine(engineBasePath, "Modules", SandBoxModuleName, "ModuleData", "sp_battle_scenes.xml");
                if (!File.Exists(registryPath))
                {
                    ModLogger.Info(
                        "DedicatedSceneContractProbe: sp battle scenes registry. " +
                        "Exists=False Path=" + registryPath);
                    return;
                }

                XmlDocument doc = new XmlDocument();
                doc.Load(registryPath);

                XmlNodeList sceneNodes = doc.SelectNodes("/SPBattleScenes/Scene");
                int sceneCount = sceneNodes?.Count ?? 0;
                XmlNode battleTerrainN = FindSceneNode(sceneNodes, "battle_terrain_n");
                XmlNode battleTerrainBiome087b = FindSceneNode(sceneNodes, "battle_terrain_biome_087b");

                ModLogger.Info(
                    "DedicatedSceneContractProbe: sp battle scenes registry. " +
                    "Exists=True Path=" + registryPath +
                    " SceneCount=" + sceneCount +
                    " BattleTerrainN=" + DescribeSpBattleSceneNode(battleTerrainN) +
                    " BattleTerrainBiome087b=" + DescribeSpBattleSceneNode(battleTerrainBiome087b));
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedSceneContractProbe: sp battle scenes registry probe failed: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private static void TryLogExactSceneManualPairProbe()
        {
            foreach (string sceneName in ProbeScenes)
            {
                if (!sceneName.StartsWith("battle_terrain", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    string rawEngineBasePath = Utilities.GetBasePath();
                    string engineBasePath = string.IsNullOrWhiteSpace(rawEngineBasePath) ? string.Empty : rawEngineBasePath;
                    string expectedScenePath = GetExpectedSceneXScenePath(engineBasePath, sceneName, SandBoxCoreModuleName);

                    bool prePairResolved = Utilities.TryGetFullFilePathOfScene(sceneName, out string prePairFullPath);
                    bool prePairUniqueResolved = Utilities.TryGetUniqueIdentifiersForScene(sceneName, out var prePairUniqueSceneId);

                    bool pairAttempted = false;
                    bool pairSucceeded = false;
                    string pairException = string.Empty;
                    try
                    {
                        pairAttempted = true;
                        Utilities.PairSceneNameToModuleName(sceneName, SandBoxCoreModuleName);
                        pairSucceeded = true;
                    }
                    catch (Exception ex)
                    {
                        pairException = ex.GetType().Name + " " + ex.Message;
                    }

                    bool postPairResolved = Utilities.TryGetFullFilePathOfScene(sceneName, out string postPairFullPath);
                    bool postPairUniqueResolved = Utilities.TryGetUniqueIdentifiersForScene(sceneName, out var postPairUniqueSceneId);

                    ModLogger.Info(
                        "DedicatedSceneContractProbe: exact scene manual pair probe. " +
                        "Scene=" + sceneName +
                        " Module=" + SandBoxCoreModuleName +
                        " ExpectedSceneFileExists=" + File.Exists(expectedScenePath) +
                        " ExpectedSceneFile=" + expectedScenePath +
                        " PrePairResolved=" + prePairResolved +
                        " PrePairFullPath=" + Safe(prePairFullPath) +
                        " PrePairUniqueSceneIdResolved=" + prePairUniqueResolved +
                        " PrePairUniqueSceneId=" + DescribeObject(prePairUniqueSceneId) +
                        " PairAttempted=" + pairAttempted +
                        " PairSucceeded=" + pairSucceeded +
                        " PairException=" + Safe(pairException) +
                        " PostPairResolved=" + postPairResolved +
                        " PostPairFullPath=" + Safe(postPairFullPath) +
                        " PostPairUniqueSceneIdResolved=" + postPairUniqueResolved +
                        " PostPairUniqueSceneId=" + DescribeObject(postPairUniqueSceneId));
                }
                catch (Exception ex)
                {
                    ModLogger.Info(
                        "DedicatedSceneContractProbe: exact scene manual pair probe failed. " +
                        "Scene=" + sceneName +
                        " Exception=" + ex.GetType().Name + " " + ex.Message);
                }
            }
        }

        private static string[] GetLoadedAssemblyNames()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            if (assemblies == null || assemblies.Length == 0)
                return Array.Empty<string>();

            List<string> names = new List<string>(assemblies.Length);
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    string name = assemblies[i]?.GetName()?.Name;
                    if (!string.IsNullOrWhiteSpace(name))
                        names.Add(name);
                }
                catch
                {
                }
            }

            return names.ToArray();
        }

        private static XmlNode FindSceneNode(XmlNodeList sceneNodes, string sceneId)
        {
            if (sceneNodes == null || string.IsNullOrWhiteSpace(sceneId))
                return null;

            for (int i = 0; i < sceneNodes.Count; i++)
            {
                XmlNode sceneNode = sceneNodes[i];
                XmlAttributeCollection attributes = sceneNode?.Attributes;
                string id = attributes?["id"]?.Value;
                if (string.Equals(id, sceneId, StringComparison.OrdinalIgnoreCase))
                    return sceneNode;
            }

            return null;
        }

        private static string DescribeSpBattleSceneNode(XmlNode sceneNode)
        {
            if (sceneNode?.Attributes == null)
                return "{Exists=False}";

            XmlAttributeCollection attributes = sceneNode.Attributes;
            return "{Exists=True" +
                ", Id=" + Safe(attributes["id"]?.Value) +
                ", Terrain=" + Safe(attributes["terrain"]?.Value) +
                ", ForestDensity=" + Safe(attributes["forest_density"]?.Value) +
                ", MapIndices=" + Safe(attributes["map_indices"]?.Value) +
                "}";
        }

        private static string GetExpectedSceneXScenePath(string engineBasePath, string sceneName, string moduleName)
        {
            return IOPath.Combine(
                engineBasePath ?? string.Empty,
                "Modules",
                moduleName ?? string.Empty,
                "SceneObj",
                sceneName ?? string.Empty,
                "scene.xscene");
        }

        private static string[] NormalizeStringArray(string[] values)
        {
            if (values == null || values.Length == 0)
                return Array.Empty<string>();

            List<string> normalized = new List<string>(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i];
                if (!string.IsNullOrWhiteSpace(value))
                    normalized.Add(value);
            }

            return normalized.ToArray();
        }

        private static bool Contains(string[] values, string expected)
        {
            if (values == null || string.IsNullOrEmpty(expected))
                return false;

            for (int i = 0; i < values.Length; i++)
            {
                if (string.Equals(values[i], expected, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string FormatArray(string[] values, int maxItems)
        {
            if (values == null || values.Length == 0)
                return "[]";

            int safeCount = Math.Min(values.Length, Math.Max(maxItems, 1));
            string[] sample = new string[safeCount];
            Array.Copy(values, sample, safeCount);
            string suffix = values.Length > safeCount ? ", ..." : string.Empty;
            return "[" + string.Join(", ", sample) + suffix + "]";
        }

        private static string DescribeObject(object value)
        {
            if (value == null)
                return "(null)";

            try
            {
                Type type = value.GetType();
                List<string> members = new List<string>();

                PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
                for (int i = 0; i < properties.Length; i++)
                {
                    PropertyInfo property = properties[i];
                    if (!property.CanRead || property.GetIndexParameters().Length != 0)
                        continue;

                    try
                    {
                        members.Add(property.Name + "=" + SafeValue(property.GetValue(value, null)));
                    }
                    catch (Exception)
                    {
                        members.Add(property.Name + "=<error>");
                    }
                }

                FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    try
                    {
                        members.Add(field.Name + "=" + SafeValue(field.GetValue(value)));
                    }
                    catch (Exception)
                    {
                        members.Add(field.Name + "=<error>");
                    }
                }

                if (members.Count == 0)
                    return type.FullName + " Value=" + SafeValue(value);

                return type.FullName + " {" + string.Join(", ", members) + "}";
            }
            catch (Exception ex)
            {
                return value.GetType().FullName + " <describe failed: " + ex.GetType().Name + " " + ex.Message + ">";
            }
        }

        private static string SafeValue(object value)
        {
            if (value == null)
                return "(null)";

            try
            {
                return value.ToString() ?? "(null-string)";
            }
            catch (Exception ex)
            {
                return "<ToString failed: " + ex.GetType().Name + " " + ex.Message + ">";
            }
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(null)" : value;
        }
    }
}
