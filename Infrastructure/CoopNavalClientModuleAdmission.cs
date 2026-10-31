using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;

namespace CoopSpectator.Infrastructure
{
    internal static class CoopNavalClientModuleAdmission
    {
        internal static bool IsPrepared { get; private set; }

        // Constructor-time admission precedes the stock loader's later NavalDLC
        // iteration. Only its public managed SubModules list changes in memory.
        internal static void PrepareBeforeSubmoduleConstruction()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            if (!arguments.Any(a => string.Equals(a, "/multiplayer", StringComparison.OrdinalIgnoreCase)) ||
                arguments.Any(a => string.Equals(a, "/singleplayer", StringComparison.OrdinalIgnoreCase))) return;
            object dedicated = TaleWorlds.MountAndBlade.Module.CurrentModule?.StartupInfo?.DedicatedServerType;
            if (dedicated == null || !string.Equals(dedicated.ToString(), "None", StringComparison.OrdinalIgnoreCase)) return;
            var paths = ModulePathHelper.GetActiveModulePaths();
            bool canonical = paths.Any(p => p.Key == "NavalDLC");
            bool catalog = paths.Any(p => p.Key == CoopCampaignContentProfile.NavalClientCatalogId);
            bool legacy = paths.Any(p => p.Key == CoopCampaignContentProfile.NavalAdapterId);
            if (!canonical && !catalog && !legacy) return;
            if (IsPrepared) throw new IOException("Duplicate Naval client submodule construction.");
            CoopNavalClientModulePolicy.RequireLayout(paths.Select(p => p.Key).ToArray());
            string source = paths.Single(p => p.Key == "NavalDLC").Value;
            string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", ".."));
            if (!string.Equals(Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.Combine(root, "Modules", "NavalDLC"), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Unexpected official Naval module location.");
            XmlDocument descriptor = CoopCampaignContentProfile.ReadDescriptor(source, "NavalDLC");
            CoopNavalClientModulePolicy.RequireKnownSubmodules(descriptor.SelectNodes("/Module/SubModules/SubModule")
                .Cast<XmlNode>().Select(n => n.SelectSingleNode("DLLName/@value")?.Value + "|" +
                    n.SelectSingleNode("SubModuleClassType/@value")?.Value));
            Type helper = Type.GetType("TaleWorlds.ModuleManager.ModuleHelper, TaleWorlds.ModuleManager", true);
            object module = helper.GetMethod("GetModuleInfo", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { "NavalDLC" });
            var submodules = module?.GetType().GetField("SubModules", BindingFlags.Public | BindingFlags.Instance)?.GetValue(module) as IList;
            if (submodules == null || submodules.IsReadOnly || submodules.IsFixedSize)
                throw new IOException("Naval submodule list is unavailable before loading.");
            string[] identities = submodules.Cast<object>().Select(s =>
                s.GetType().GetProperty("DLLName")?.GetValue(s, null) + "|" +
                s.GetType().GetProperty("SubModuleClassTypeName")?.GetValue(s, null)).ToArray();
            CoopNavalClientModulePolicy.RequireKnownSubmodules(identities);
            foreach (string identity in identities)
                if (TaleWorlds.MountAndBlade.Module.CurrentModule.GetSubModuleType(identity.Split('|')[1]) != null)
                    throw new IOException("Naval gameplay submodule already constructed; unsafe module order.");
            string catalogRoot = paths.Single(p => p.Key == CoopCampaignContentProfile.NavalClientCatalogId).Value;
            CoopSpectator.DedicatedHelper.CoopNavalResourceAdapter.ValidateClientCatalog(root, catalogRoot);
            submodules.Clear();
            IsPrepared = true;
        }
    }
}
