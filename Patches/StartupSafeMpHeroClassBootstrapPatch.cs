using System;
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
        private static bool _loggedHealthyCatalog;
        private static bool _loggedMissingCatalog;

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
                    generatedBootstrapResult = BootstrapFallbackHeroClasses(GeneratedRuntimeClassDivisionsFileName);
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

                string stringId = node.Attributes["id"]?.Value;
                string heroId = node.Attributes["hero"]?.Value;
                string troopId = node.Attributes["troop"]?.Value;
                if (string.IsNullOrWhiteSpace(stringId) ||
                    string.IsNullOrWhiteSpace(heroId) ||
                    string.IsNullOrWhiteSpace(troopId))
                {
                    continue;
                }

                if (IsHeroClassRegistered(stringId))
                    continue;

                MultiplayerClassDivisions.MPHeroClass heroClass = TryGetExistingHeroClass(objectManager, stringId);
                BasicCharacterObject heroCharacter = heroClass?.HeroCharacter ?? TryGetExistingCharacter(objectManager, heroId);
                BasicCharacterObject troopCharacter = heroClass?.TroopCharacter ?? TryGetExistingCharacter(objectManager, troopId);
                if (heroCharacter == null || troopCharacter == null)
                {
                    ModLogger.Info(
                        "StartupSafeMpHeroClassBootstrapPatch: skipped fallback hero class because startup-safe MPCharacters are missing. " +
                        "Class=" + stringId +
                        " Hero=" + heroId +
                        " Troop=" + troopId + ".");
                    result.SkippedMissingCharacters++;
                    continue;
                }

                if (heroClass == null)
                {
                    heroClass = objectManager.CreateObject<MultiplayerClassDivisions.MPHeroClass>(stringId);
                    result.Created++;
                }

                if (heroClass.HeroCharacter == null || heroClass.TroopCharacter == null)
                {
                    XmlElement strippedNode = CreateStartupSafeClassNode(node);
                    heroClass.Deserialize(objectManager, strippedNode);
                }

                heroClass.AfterInitialized();
                result.Activated++;
            }

            return result;
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

        private static XmlElement CreateStartupSafeClassNode(XmlNode sourceNode)
        {
            XmlDocument tempDocument = new XmlDocument();
            XmlElement element = tempDocument.CreateElement("MPClassDivision");
            foreach (XmlAttribute attribute in sourceNode.Attributes)
            {
                XmlAttribute clonedAttribute = tempDocument.CreateAttribute(attribute.Name);
                clonedAttribute.Value = attribute.Value;
                element.Attributes.Append(clonedAttribute);
            }

            return element;
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
