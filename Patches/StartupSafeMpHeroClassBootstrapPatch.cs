using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Patches
{
    public static class StartupSafeMpHeroClassBootstrapPatch
    {
        private const string StartupClassDivisionsFileName = "coopspectator_mpclassdivisions.xml";
        private const string GeneratedRuntimeClassDivisionsFileName = "coopspectator_generated_runtime_mpclassdivisions.xml";
        private const string GeneratedRuntimeClassIdPrefix = "coopspectator_generated_class_";
        private static readonly object GeneratedDefinitionSync = new object();
        private static bool _loggedHealthyCatalog;
        private static bool _loggedMissingCatalog;
        private static bool _loggedDeferredGeneratedBootstrap;
        private static Dictionary<string, FallbackHeroClassDefinition> _generatedRuntimeDefinitionsByCharacterId;

        public static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo target = AccessTools.Method(typeof(MultiplayerClassDivisions), nameof(MultiplayerClassDivisions.Initialize));
                MethodInfo postfix = AccessTools.Method(typeof(StartupSafeMpHeroClassBootstrapPatch), nameof(Initialize_Postfix));
                if (target == null || postfix == null)
                {
                    ModLogger.Info("StartupSafeMpHeroClassBootstrapPatch: target not found. Skip.");
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                ModLogger.Info("StartupSafeMpHeroClassBootstrapPatch: postfix applied.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("StartupSafeMpHeroClassBootstrapPatch.Apply failed.", ex);
            }
        }

        private static void Initialize_Postfix()
        {
            try
            {
                MBReadOnlyList<MultiplayerClassDivisions.MPHeroClass> heroClasses = MultiplayerClassDivisions.GetMPHeroClasses();
                int existingCount = heroClasses?.Count ?? 0;
                int existingGeneratedCount = CountRegisteredHeroClassesByPrefix(GeneratedRuntimeClassIdPrefix);
                if (existingCount > 0)
                {
                    if (!_loggedHealthyCatalog)
                    {
                        _loggedHealthyCatalog = true;
                        ModLogger.Info(
                            "StartupSafeMpHeroClassBootstrapPatch: vanilla startup MPHeroClass catalog is healthy. " +
                            "Count=" + existingCount +
                            " GeneratedCount=" + existingGeneratedCount + ".");
                    }
                }

                BootstrapResult startupBootstrapResult = default;
                BootstrapResult generatedBootstrapResult = default;

                if (existingCount == 0)
                {
                    startupBootstrapResult = BootstrapFallbackHeroClasses(StartupClassDivisionsFileName);
                }

                if (existingGeneratedCount == 0)
                {
                    if (AreAnyGeneratedRuntimeCharactersLoaded())
                    {
                        generatedBootstrapResult = BootstrapFallbackHeroClasses(GeneratedRuntimeClassDivisionsFileName);
                    }
                    else if (!_loggedDeferredGeneratedBootstrap)
                    {
                        _loggedDeferredGeneratedBootstrap = true;
                        ModLogger.Info(
                            "StartupSafeMpHeroClassBootstrapPatch: deferred generated runtime MPHeroClass bootstrap until generated MPCharacters are loaded.");
                    }
                }

                if (startupBootstrapResult.HasWork || generatedBootstrapResult.HasWork)
                {
                    int totalCount = MultiplayerClassDivisions.GetMPHeroClasses()?.Count ?? 0;
                    int totalGeneratedCount = CountRegisteredHeroClassesByPrefix(GeneratedRuntimeClassIdPrefix);
                    ModLogger.Info(
                        "StartupSafeMpHeroClassBootstrapPatch: ensured MPHeroClass catalog registration. " +
                        "Startup[Created=" + startupBootstrapResult.Created +
                        ",Activated=" + startupBootstrapResult.Activated +
                        ",SkippedMissingCharacters=" + startupBootstrapResult.SkippedMissingCharacters +
                        ",Xml=" + (startupBootstrapResult.XmlPath ?? "null") + "] " +
                        "Generated[Created=" + generatedBootstrapResult.Created +
                        ",Activated=" + generatedBootstrapResult.Activated +
                        ",SkippedMissingCharacters=" + generatedBootstrapResult.SkippedMissingCharacters +
                        ",Xml=" + (generatedBootstrapResult.XmlPath ?? "null") + "] " +
                        "Total=" + totalCount +
                        " GeneratedTotal=" + totalGeneratedCount + ".");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("StartupSafeMpHeroClassBootstrapPatch.Initialize_Postfix failed.", ex);
            }
        }

        public static bool EnsureGeneratedRuntimeHeroClassRegisteredForCharacter(string characterId, out string diagnostics)
        {
            diagnostics = "character-null";
            if (string.IsNullOrWhiteSpace(characterId))
                return false;

            try
            {
                FallbackHeroClassDefinition definition = GetGeneratedRuntimeHeroClassDefinition(characterId);
                if (definition == null)
                {
                    diagnostics = "definition-missing Character=" + characterId;
                    return false;
                }

                if (IsHeroClassRegistered(definition.StringId))
                {
                    diagnostics =
                        "already-registered Character=" + characterId +
                        " Class=" + definition.StringId;
                    return true;
                }

                MBObjectManager objectManager = MBObjectManager.Instance;
                if (objectManager == null)
                {
                    diagnostics = "object-manager-null Character=" + characterId;
                    return false;
                }

                if (!TryEnsureFallbackHeroClassRegistered(objectManager, definition, logMissingCharacters: false, out bool activated, out string ensureDiagnostics))
                {
                    diagnostics =
                        "registration-deferred Character=" + characterId + " " +
                        ensureDiagnostics;
                    return false;
                }

                diagnostics =
                    "registered-on-demand Character=" + characterId +
                    " Class=" + definition.StringId +
                    " Activated=" + activated;
                ModLogger.Info(
                    "StartupSafeMpHeroClassBootstrapPatch: ensured generated runtime MPHeroClass registration on demand. " +
                    diagnostics);
                return true;
            }
            catch (Exception ex)
            {
                diagnostics = "exception:" + ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        private static BootstrapResult BootstrapFallbackHeroClasses(string fileName)
        {
            var result = new BootstrapResult();
            string xmlPath = ModulePathHelper.GetModuleDataFilePath(fileName);
            result.XmlPath = xmlPath;
            if (string.IsNullOrWhiteSpace(xmlPath) || !File.Exists(xmlPath))
            {
                if (!_loggedMissingCatalog)
                {
                    _loggedMissingCatalog = true;
                    ModLogger.Info(
                        "StartupSafeMpHeroClassBootstrapPatch: fallback XML not found. " +
                        "Path=" + (xmlPath ?? "null") + ".");
                }

                return result;
            }

            MBObjectManager objectManager = MBObjectManager.Instance;
            if (objectManager == null)
                return result;

            XmlDocument document = new XmlDocument();
            document.Load(xmlPath);

            XmlNodeList nodes = document.SelectNodes("/MPClassDivisions/MPClassDivision");
            if (nodes == null || nodes.Count == 0)
                return result;

            foreach (XmlNode node in nodes)
            {
                if (node?.Attributes == null)
                    continue;

                FallbackHeroClassDefinition definition = BuildFallbackHeroClassDefinition(node);
                if (definition == null)
                {
                    continue;
                }

                if (IsHeroClassRegistered(definition.StringId))
                    continue;

                if (!TryEnsureFallbackHeroClassRegistered(
                        objectManager,
                        definition,
                        logMissingCharacters: !string.Equals(fileName, GeneratedRuntimeClassDivisionsFileName, StringComparison.OrdinalIgnoreCase),
                        out bool activated,
                        out string diagnostics))
                {
                    result.SkippedMissingCharacters++;
                    continue;
                }

                if (diagnostics.StartsWith("created", StringComparison.Ordinal))
                    result.Created++;
                if (activated)
                    result.Activated++;
            }

            return result;
        }

        private static bool AreAnyGeneratedRuntimeCharactersLoaded()
        {
            MBObjectManager objectManager = MBObjectManager.Instance;
            if (objectManager == null)
                return false;

            Dictionary<string, FallbackHeroClassDefinition> definitions = GetGeneratedRuntimeHeroClassDefinitionsByCharacterId();
            if (definitions == null || definitions.Count == 0)
                return false;

            foreach (FallbackHeroClassDefinition definition in definitions.Values)
            {
                BasicCharacterObject heroCharacter = TryGetExistingCharacter(objectManager, definition.HeroId);
                BasicCharacterObject troopCharacter = TryGetExistingCharacter(objectManager, definition.TroopId);
                if (heroCharacter != null && troopCharacter != null)
                    return true;
            }

            return false;
        }

        private static Dictionary<string, FallbackHeroClassDefinition> GetGeneratedRuntimeHeroClassDefinitionsByCharacterId()
        {
            lock (GeneratedDefinitionSync)
            {
                if (_generatedRuntimeDefinitionsByCharacterId != null)
                    return _generatedRuntimeDefinitionsByCharacterId;

                _generatedRuntimeDefinitionsByCharacterId =
                    LoadFallbackHeroClassDefinitionsByCharacterId(GeneratedRuntimeClassDivisionsFileName);
                return _generatedRuntimeDefinitionsByCharacterId;
            }
        }

        private static FallbackHeroClassDefinition GetGeneratedRuntimeHeroClassDefinition(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return null;

            Dictionary<string, FallbackHeroClassDefinition> definitions = GetGeneratedRuntimeHeroClassDefinitionsByCharacterId();
            if (definitions == null)
                return null;

            definitions.TryGetValue(characterId, out FallbackHeroClassDefinition definition);
            return definition;
        }

        private static Dictionary<string, FallbackHeroClassDefinition> LoadFallbackHeroClassDefinitionsByCharacterId(string fileName)
        {
            string xmlPath = ModulePathHelper.GetModuleDataFilePath(fileName);
            if (string.IsNullOrWhiteSpace(xmlPath) || !File.Exists(xmlPath))
                return new Dictionary<string, FallbackHeroClassDefinition>(StringComparer.Ordinal);

            XmlDocument document = new XmlDocument();
            document.Load(xmlPath);

            var definitions = new Dictionary<string, FallbackHeroClassDefinition>(StringComparer.Ordinal);
            XmlNodeList nodes = document.SelectNodes("/MPClassDivisions/MPClassDivision");
            if (nodes == null)
                return definitions;

            foreach (XmlNode node in nodes)
            {
                FallbackHeroClassDefinition definition = BuildFallbackHeroClassDefinition(node);
                if (definition == null)
                    continue;

                if (!string.IsNullOrWhiteSpace(definition.HeroId))
                    definitions[definition.HeroId] = definition;
                if (!string.IsNullOrWhiteSpace(definition.TroopId))
                    definitions[definition.TroopId] = definition;
            }

            return definitions;
        }

        private static FallbackHeroClassDefinition BuildFallbackHeroClassDefinition(XmlNode node)
        {
            if (node?.Attributes == null)
                return null;

            string stringId = node.Attributes["id"]?.Value;
            string heroId = node.Attributes["hero"]?.Value;
            string troopId = node.Attributes["troop"]?.Value;
            if (string.IsNullOrWhiteSpace(stringId) ||
                string.IsNullOrWhiteSpace(heroId) ||
                string.IsNullOrWhiteSpace(troopId))
            {
                return null;
            }

            var definition = new FallbackHeroClassDefinition
            {
                StringId = stringId,
                HeroId = heroId,
                TroopId = troopId
            };

            foreach (XmlAttribute attribute in node.Attributes)
            {
                definition.Attributes[attribute.Name] = attribute.Value;
            }

            return definition;
        }

        private static bool TryEnsureFallbackHeroClassRegistered(
            MBObjectManager objectManager,
            FallbackHeroClassDefinition definition,
            bool logMissingCharacters,
            out bool activated,
            out string diagnostics)
        {
            activated = false;
            diagnostics = "definition-null";
            if (objectManager == null || definition == null)
                return false;

            MultiplayerClassDivisions.MPHeroClass heroClass = TryGetExistingHeroClass(objectManager, definition.StringId);
            BasicCharacterObject heroCharacter = heroClass?.HeroCharacter ?? TryGetExistingCharacter(objectManager, definition.HeroId);
            BasicCharacterObject troopCharacter = heroClass?.TroopCharacter ?? TryGetExistingCharacter(objectManager, definition.TroopId);
            if (heroCharacter == null || troopCharacter == null)
            {
                diagnostics =
                    "missing-characters Class=" + definition.StringId +
                    " Hero=" + definition.HeroId +
                    " Troop=" + definition.TroopId;
                if (logMissingCharacters)
                {
                    ModLogger.Info(
                        "StartupSafeMpHeroClassBootstrapPatch: skipped fallback hero class because startup-safe MPCharacters are missing. " +
                        "Class=" + definition.StringId +
                        " Hero=" + definition.HeroId +
                        " Troop=" + definition.TroopId + ".");
                }

                return false;
            }

            bool created = false;
            if (heroClass == null)
            {
                heroClass = objectManager.CreateObject<MultiplayerClassDivisions.MPHeroClass>(definition.StringId);
                created = true;
            }

            if (heroClass.HeroCharacter == null || heroClass.TroopCharacter == null)
            {
                XmlElement strippedNode = CreateStartupSafeClassNode(definition);
                heroClass.Deserialize(objectManager, strippedNode);
            }

            heroClass.AfterInitialized();
            activated = true;
            diagnostics =
                (created ? "created" : "updated") +
                " Class=" + definition.StringId +
                " Hero=" + definition.HeroId +
                " Troop=" + definition.TroopId;
            return true;
        }

        private static bool IsHeroClassRegistered(string stringId)
        {
            if (string.IsNullOrWhiteSpace(stringId))
                return false;

            MBReadOnlyList<MultiplayerClassDivisions.MPHeroClass> heroClasses = MultiplayerClassDivisions.GetMPHeroClasses();
            if (heroClasses == null)
                return false;

            for (int i = 0; i < heroClasses.Count; i++)
            {
                if (string.Equals(heroClasses[i]?.StringId, stringId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static int CountRegisteredHeroClassesByPrefix(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
                return 0;

            MBReadOnlyList<MultiplayerClassDivisions.MPHeroClass> heroClasses = MultiplayerClassDivisions.GetMPHeroClasses();
            if (heroClasses == null)
                return 0;

            int count = 0;
            for (int i = 0; i < heroClasses.Count; i++)
            {
                string stringId = heroClasses[i]?.StringId;
                if (!string.IsNullOrWhiteSpace(stringId) &&
                    stringId.StartsWith(prefix, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static MultiplayerClassDivisions.MPHeroClass TryGetExistingHeroClass(MBObjectManager objectManager, string stringId)
        {
            try
            {
                return objectManager.GetObject<MultiplayerClassDivisions.MPHeroClass>(stringId);
            }
            catch
            {
                return null;
            }
        }

        private static BasicCharacterObject TryGetExistingCharacter(MBObjectManager objectManager, string stringId)
        {
            try
            {
                return objectManager.GetObject<BasicCharacterObject>(stringId);
            }
            catch
            {
                return null;
            }
        }

        private static XmlElement CreateStartupSafeClassNode(FallbackHeroClassDefinition definition)
        {
            XmlDocument tempDocument = new XmlDocument();
            XmlElement element = tempDocument.CreateElement("MPClassDivision");
            foreach (KeyValuePair<string, string> attribute in definition.Attributes)
            {
                XmlAttribute clonedAttribute = tempDocument.CreateAttribute(attribute.Key);
                clonedAttribute.Value = attribute.Value;
                element.Attributes.Append(clonedAttribute);
            }

            return element;
        }

        private sealed class FallbackHeroClassDefinition
        {
            public string StringId { get; set; }

            public string HeroId { get; set; }

            public string TroopId { get; set; }

            public Dictionary<string, string> Attributes { get; } =
                new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private struct BootstrapResult
        {
            public int Created;
            public int Activated;
            public int SkippedMissingCharacters;
            public string XmlPath;

            public bool HasWork => Created > 0 || Activated > 0 || SkippedMissingCharacters > 0;
        }
    }
}
