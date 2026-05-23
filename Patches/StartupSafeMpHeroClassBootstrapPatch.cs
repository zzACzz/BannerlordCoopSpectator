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
                if (existingCount > 0)
                {
                    if (!_loggedHealthyCatalog)
                    {
                        _loggedHealthyCatalog = true;
                        ModLogger.Info(
                            "StartupSafeMpHeroClassBootstrapPatch: vanilla startup MPHeroClass catalog is healthy. " +
                            "Count=" + existingCount + ".");
                    }

                    return;
                }

                string xmlPath = ModulePathHelper.GetModuleDataFilePath(StartupClassDivisionsFileName);
                if (string.IsNullOrWhiteSpace(xmlPath) || !File.Exists(xmlPath))
                {
                    if (!_loggedMissingCatalog)
                    {
                        _loggedMissingCatalog = true;
                        ModLogger.Info(
                            "StartupSafeMpHeroClassBootstrapPatch: fallback XML not found. " +
                            "Path=" + (xmlPath ?? "null") + ".");
                    }

                    return;
                }

                int created = BootstrapFallbackHeroClasses(xmlPath);
                int totalCount = MultiplayerClassDivisions.GetMPHeroClasses()?.Count ?? 0;
                ModLogger.Info(
                    "StartupSafeMpHeroClassBootstrapPatch: bootstrapped startup-safe MPHeroClass catalog. " +
                    "Created=" + created +
                    " Total=" + totalCount +
                    " Xml=" + xmlPath + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Error("StartupSafeMpHeroClassBootstrapPatch.Initialize_Postfix failed.", ex);
            }
        }

        private static int BootstrapFallbackHeroClasses(string xmlPath)
        {
            MBObjectManager objectManager = MBObjectManager.Instance;
            if (objectManager == null)
                return 0;

            XmlDocument document = new XmlDocument();
            document.Load(xmlPath);

            XmlNodeList nodes = document.SelectNodes("/MPClassDivisions/MPClassDivision");
            if (nodes == null || nodes.Count == 0)
                return 0;

            int created = 0;
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

                if (TryGetExistingHeroClass(objectManager, stringId) != null)
                    continue;

                BasicCharacterObject heroCharacter = TryGetExistingCharacter(objectManager, heroId);
                BasicCharacterObject troopCharacter = TryGetExistingCharacter(objectManager, troopId);
                if (heroCharacter == null || troopCharacter == null)
                {
                    ModLogger.Info(
                        "StartupSafeMpHeroClassBootstrapPatch: skipped fallback hero class because startup-safe MPCharacters are missing. " +
                        "Class=" + stringId +
                        " Hero=" + heroId +
                        " Troop=" + troopId + ".");
                    continue;
                }

                XmlElement strippedNode = CreateStartupSafeClassNode(node);
                MultiplayerClassDivisions.MPHeroClass heroClass = objectManager.CreateObject<MultiplayerClassDivisions.MPHeroClass>(stringId);
                heroClass.Deserialize(objectManager, strippedNode);
                heroClass.AfterInitialized();
                created++;
            }

            return created;
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
    }
}
