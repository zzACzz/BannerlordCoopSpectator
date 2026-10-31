using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Infrastructure
{
    internal static class CoopCampaignContentRuntime
    {
        private static Game _owner;
        private static readonly CoopCampaignContentIdentityCache IdentityCache = new CoopCampaignContentIdentityCache();
        private static CoopNavalCatalogReadiness _navalReadiness;
        public static bool IsCampaign
        {
            get
            {
                for (Type type = Game.Current?.GameType?.GetType(); type != null; type = type.BaseType)
                    if (type.FullName == "TaleWorlds.CampaignSystem.Campaign") return true;
                return false;
            }
        }
        private static bool IsDedicated
        {
            get
            {
                object type = TaleWorlds.MountAndBlade.Module.CurrentModule?.StartupInfo?.DedicatedServerType;
                return type != null && !string.Equals(type.ToString(), "None", StringComparison.OrdinalIgnoreCase);
            }
        }

        public static bool UsesNavalResources => ModulePathHelper.GetActiveModulePaths().Any(module =>
            string.Equals(module.Key, IsCampaign ? "NavalDLC" :
                IsDedicated ? CoopCampaignContentProfile.NavalAdapterId : CoopCampaignContentProfile.NavalClientCatalogId,
                StringComparison.OrdinalIgnoreCase));

        internal static string ResolveActiveMapOwner() =>
            CoopCampaignMapResourceOwner.Resolve(ModulePathHelper.GetActiveModulePaths());

        public static string GameRoot
        {
            get
            {
                object dedicated = TaleWorlds.MountAndBlade.Module.CurrentModule?.StartupInfo?.DedicatedServerType;
                bool server = dedicated != null && !string.Equals(dedicated.ToString(), "None", StringComparison.OrdinalIgnoreCase);
                string root = server ? Environment.GetEnvironmentVariable("BANNERLORD_GAME_ROOT") :
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..");
                if (string.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root))
                    throw new IOException("The dedicated server requires the running game's absolute BANNERLORD_GAME_ROOT.");
                return Path.GetFullPath(root);
            }
        }

        public static string GetLocalId()
        {
            if (Game.Current == null) throw new InvalidOperationException("No active game content owner.");
            if (!ReferenceEquals(_owner, Game.Current))
            {
                _owner = Game.Current; _navalReadiness = null;
            }
            return IdentityCache.GetOrCreate(Game.Current, DateTime.UtcNow, CaptureLocalId);
        }

        private static string CaptureLocalId()
        {
            var modules = ModulePathHelper.GetActiveModulePaths();
            bool campaign = IsCampaign;
            string adapter = IsDedicated ? CoopCampaignContentProfile.NavalAdapterId : CoopCampaignContentProfile.NavalClientCatalogId;
            bool naval = modules.Any(module => string.Equals(module.Key,
                campaign ? "NavalDLC" : adapter, StringComparison.OrdinalIgnoreCase));
            if (campaign)
            {
                string owner = CoopCampaignMapResourceOwner.Resolve(modules);
                if (!string.Equals(owner, naval ? "NavalDLC" : "Sandbox", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Unsupported campaign map owner: " + (owner ?? "unknown") +
                        ". This resource profile supports the base map and War Sails map.");
            }
            if (!campaign && IsDedicated && modules.Any(module => module.Key == "NavalDLC"))
                throw new IOException("The dedicated resource profile must not load the official NavalDLC campaign module.");
#if !COOPSPECTATOR_DEDICATED
            if (!campaign && !IsDedicated && (naval || modules.Any(module => module.Key == "NavalDLC")))
            {
                if (!CoopNavalClientModuleAdmission.IsPrepared || !naval || !modules.Any(module => module.Key == "NavalDLC"))
                    throw new IOException("Restart through the current Warhost Naval launcher; canonical native content admission is missing.");
            }
#endif
            string ownData = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(CoopCampaignContentRuntime).Assembly.Location),
                "..", "..", "ModuleData"));
            string profile = CoopCampaignContentProfile.Compute(GameRoot, naval, ownData);
            if (!campaign && naval)
            {
                string marker = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "Modules",
                    adapter, "warhost-resources.txt");
                string[] lines = File.ReadAllLines(marker);
                if (lines.Length < 3 || lines[2] != profile) throw new IOException("Naval resources changed. Restart through the multiplayer launcher.");
            }
            return profile;
        }

        public static bool Matches(string expected, out string reason)
        {
            reason = null;
            try
            {
                if (!CoopCampaignContentProfile.IsValid(expected)) { reason = "Host content profile is missing or invalid. Update all Warhost roles."; return false; }
                string local = GetLocalId();
                if (string.Equals(local, expected, StringComparison.Ordinal))
                {
                    if (!CoopCampaignContentProfile.IsNaval(local) || IsCampaign) return true;
                    if (_navalReadiness == null) _navalReadiness = CoopNavalCatalogReadiness.Read(Path.Combine(GameRoot, "Modules", "NavalDLC"));
                    MBObjectManager manager = Game.Current?.ObjectManager ?? MBObjectManager.Instance;
                    if (manager == null) { reason = "Game object catalog is not initialized."; return false; }
                    return _navalReadiness.Validate(id => manager.GetObject<ItemObject>(id)?.IsReady == true,
                        id => manager.GetObject<CraftingPiece>(id)?.IsReady == true, out reason);
                }
                reason = "Campaign resources differ. Restart multiplayer with the host's " +
                    (CoopCampaignContentProfile.IsNaval(expected) ? "War Sails" : "Base game") +
                    " profile and matching game/DLC versions.";
                return false;
            }
            catch (Exception ex) { reason = ex.Message; return false; }
        }

        public static void RememberLocalHost()
        {
            if (!IsCampaign) return;
            string profile = GetLocalId();
            using (var process = Process.GetCurrentProcess())
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Mount and Blade II Bannerlord", "CoopSpectator");
                Directory.CreateDirectory(folder);
                AtomicBridgeFileIO.WriteAllLines(Path.Combine(folder, "content-profile-host.txt"), new[] {
                    "warhost-local-content-v1", process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    process.StartTime.ToUniversalTime().ToString("O"), profile });
            }
        }
    }
}
