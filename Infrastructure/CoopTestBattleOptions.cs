using CoopSpectator.Network.Messages;

namespace CoopSpectator.Infrastructure
{
    public static class CoopTestBattleOptions
    {
        public enum RosterMode
        {
            MatrixOnly = 0,
            AllCampaignTroops = 1,
            Mixed = 2,
            FiveModeMatrix = 3,
            RoleMatrixStream = 4,
            RoleMatrixStreamMounted = 5,
            CampaignMirrorAll = 6,
            CampaignMirrorHeroes = 7,
            CampaignMirrorHeroesCombat = 8,
            ShieldBanners = 9
        }

        public enum CraftedWeaponsMode
        {
            Safe = 0,
            CreateTime = 1
        }

        public const string BattleId = "coop_test_battle";
        public const string BattleType = "CoopTestBattle";
        public const string FiveModeMatrixBattleType = "CoopTestBattleFiveModeMatrix";
        public const string FiveModeMatrixProtocolBattleType = "CoopTestBattleFiveModeMatrixProtocol";
        public const string RoleMatrixStreamBattleType = "CoopTestBattleRoleMatrixStream";
        public const string RoleMatrixStreamMountedBattleType = "CoopTestBattleRoleMatrixStreamMounted";
        public const string CampaignMirrorAllBattleType = "CoopTestBattleCampaignMirrorAll";
        public const string CampaignMirrorAllCampaignAiBattleType = "CoopTestBattleCampaignMirrorAllCampaignAI";
        public const string CampaignMirrorAllWeaponPriorityBattleType = "CoopTestBattleCampaignMirrorAllWeaponPriority";
        public const string CampaignMirrorHeroesBattleType = "CoopTestBattleCampaignMirrorHeroes";
        public const string CampaignMirrorHeroesCombatBattleType = "CoopTestBattleCampaignMirrorHeroesCombat";
        public const string ShieldBannersBattleType = "CoopTestBattleShieldBanners";
        public const string ResolverSource = "coop-test-battle";
        public const string RuntimeScene = "mp_battle_map_001";
        public const string RuntimeGameType = CoopGameModeIds.OfficialBattle;
        public const string DefaultWeaponPriorityFocus = "all";
        public const string WeaponPrioritySuspectsFocus = "suspects";
        public const int DefaultWeaponSlotMatrixLimit = 160;
        public const int DefaultRoleMatrixStreamActiveLimit = 80;
        public const float DefaultRoleMatrixStreamWaveLifetimeSeconds = 7f;
        public const float DefaultWeaponPriorityLifetimeSeconds = 45f;

        private static bool _enabled;
        private static RosterMode _rosterMode = RosterMode.MatrixOnly;
        private static bool _includeWeaponSlotMatrix = true;
        private static bool _fiveModeWeaponUsageProtocolEnabled = true;
        private static bool _campaignFieldAiEnabled;
        private static bool _weaponPriorityEnabled;
        private static int _weaponSlotMatrixLimit = DefaultWeaponSlotMatrixLimit;
        private static int _roleMatrixStreamActiveLimit = DefaultRoleMatrixStreamActiveLimit;
        private static float _roleMatrixStreamWaveLifetimeSeconds = DefaultRoleMatrixStreamWaveLifetimeSeconds;
        private static float _weaponPriorityLifetimeSeconds = DefaultWeaponPriorityLifetimeSeconds;
        private static string _weaponPriorityFocus = DefaultWeaponPriorityFocus;
        private static CraftedWeaponsMode _craftedWeaponsMode = CraftedWeaponsMode.Safe;

        public static bool Enabled => _enabled;

        public static RosterMode CurrentRosterMode => _rosterMode;

        public static bool IncludeWeaponSlotMatrix => _includeWeaponSlotMatrix;

        public static bool FiveModeWeaponUsageProtocolEnabled => _fiveModeWeaponUsageProtocolEnabled;

        public static bool CampaignFieldAiEnabled => _campaignFieldAiEnabled;

        public static bool WeaponPriorityEnabled => _weaponPriorityEnabled;

        public static int WeaponSlotMatrixLimit => _weaponSlotMatrixLimit;

        public static int RoleMatrixStreamActiveLimit => _roleMatrixStreamActiveLimit;

        public static float RoleMatrixStreamWaveLifetimeSeconds => _roleMatrixStreamWaveLifetimeSeconds;

        public static float WeaponPriorityLifetimeSeconds => _weaponPriorityLifetimeSeconds;

        public static string WeaponPriorityFocus => _weaponPriorityFocus;

        public static CraftedWeaponsMode CurrentCraftedWeaponsMode => _craftedWeaponsMode;

        public static void SetEnabled(bool enabled)
        {
            _enabled = enabled;
        }

        public static void SetRosterMode(RosterMode mode)
        {
            _rosterMode = mode;
        }

        public static void SetIncludeWeaponSlotMatrix(bool enabled)
        {
            _includeWeaponSlotMatrix = enabled;
        }

        public static void SetFiveModeWeaponUsageProtocolEnabled(bool enabled)
        {
            _fiveModeWeaponUsageProtocolEnabled = enabled;
        }

        public static void SetCampaignFieldAiEnabled(bool enabled)
        {
            _campaignFieldAiEnabled = enabled;
        }

        public static void SetWeaponPriorityEnabled(bool enabled)
        {
            _weaponPriorityEnabled = enabled;
            if (enabled)
            {
                _rosterMode = RosterMode.CampaignMirrorAll;
                _campaignFieldAiEnabled = true;
            }
            else
            {
                _weaponPriorityFocus = DefaultWeaponPriorityFocus;
            }
        }

        public static void SetWeaponPriorityFocus(string focus)
        {
            string normalized = (focus ?? string.Empty).Trim().ToLowerInvariant();
            _weaponPriorityFocus =
                string.Equals(normalized, WeaponPrioritySuspectsFocus, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "suspect", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "focus_suspects", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "focus-suspects", System.StringComparison.OrdinalIgnoreCase)
                    ? WeaponPrioritySuspectsFocus
                    : DefaultWeaponPriorityFocus;
        }

        public static void SetCraftedWeaponsMode(CraftedWeaponsMode mode)
        {
            _craftedWeaponsMode = mode;
        }

        public static bool TryParseCraftedWeaponsMode(string value, out CraftedWeaponsMode mode)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "create_time":
                case "create-time":
                case "createtime":
                case "spawn":
                case "spawn_time":
                case "spawn-time":
                case "on":
                case "enable":
                case "enabled":
                case "1":
                case "true":
                    mode = CraftedWeaponsMode.CreateTime;
                    return true;
                case "safe":
                case "strip":
                case "stripped":
                case "off":
                case "disable":
                case "disabled":
                case "0":
                case "false":
                    mode = CraftedWeaponsMode.Safe;
                    return true;
                default:
                    mode = CraftedWeaponsMode.Safe;
                    return false;
            }
        }

        public static string FormatCraftedWeaponsMode(CraftedWeaponsMode mode)
        {
            return mode == CraftedWeaponsMode.CreateTime
                ? "create_time"
                : "safe";
        }

        public static void SetWeaponSlotMatrixLimit(int value)
        {
            _weaponSlotMatrixLimit = value <= 0
                ? DefaultWeaponSlotMatrixLimit
                : value;
        }

        public static void SetRoleMatrixStreamActiveLimit(int value)
        {
            if (value <= 0)
            {
                _roleMatrixStreamActiveLimit = DefaultRoleMatrixStreamActiveLimit;
                return;
            }

            _roleMatrixStreamActiveLimit = System.Math.Max(1, System.Math.Min(value, 240));
        }

        public static void SetRoleMatrixStreamWaveLifetimeSeconds(float value)
        {
            if (value <= 0f)
            {
                _roleMatrixStreamWaveLifetimeSeconds = DefaultRoleMatrixStreamWaveLifetimeSeconds;
                return;
            }

            _roleMatrixStreamWaveLifetimeSeconds = System.Math.Max(3f, System.Math.Min(value, 30f));
        }

        public static void SetWeaponPriorityLifetimeSeconds(float value)
        {
            if (value <= 0f)
            {
                _weaponPriorityLifetimeSeconds = DefaultWeaponPriorityLifetimeSeconds;
                return;
            }

            _weaponPriorityLifetimeSeconds = System.Math.Max(5f, System.Math.Min(value, 180f));
        }

        public static bool TryParseRosterMode(string value, out RosterMode mode)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "matrix":
                case "matrix_only":
                case "matrix-only":
                case "mp_matrix":
                case "multiplayer_matrix":
                    mode = RosterMode.MatrixOnly;
                    return true;
                case "campaign":
                case "campaign_all":
                case "all_campaign":
                case "all-campaign":
                case "all_campaign_troops":
                    mode = RosterMode.AllCampaignTroops;
                    return true;
                case "mixed":
                case "campaign_and_matrix":
                case "all_campaign_and_matrix":
                    mode = RosterMode.Mixed;
                    return true;
                case "five_mode":
                case "five-mode":
                case "five_mode_matrix":
                case "five-mode-matrix":
                case "5_mode":
                case "5-mode":
                case "5_mode_matrix":
                case "5-mode-matrix":
                    mode = RosterMode.FiveModeMatrix;
                    return true;
                case "role_matrix_stream":
                case "role-matrix-stream":
                case "role_matrix":
                case "role-matrix":
                case "stream":
                case "matrix_stream":
                case "matrix-stream":
                    mode = RosterMode.RoleMatrixStream;
                    return true;
                case "role_matrix_stream_mounted":
                case "role-matrix-stream-mounted":
                case "role_matrix_mounted":
                case "role-matrix-mounted":
                case "mounted_role_matrix_stream":
                case "mounted-role-matrix-stream":
                case "mounted_role_matrix":
                case "mounted-role-matrix":
                case "mounted_stream":
                case "mounted-stream":
                    mode = RosterMode.RoleMatrixStreamMounted;
                    return true;
                case "campaign_mirror_all":
                case "campaign-mirror-all":
                case "campaign_mirror":
                case "campaign-mirror":
                case "mirror_campaign":
                case "mirror-campaign":
                case "campaign_all_mirror":
                case "campaign-all-mirror":
                    mode = RosterMode.CampaignMirrorAll;
                    return true;
                case "campaign_mirror_heroes":
                case "campaign-mirror-heroes":
                case "campaign_heroes_mirror":
                case "campaign-heroes-mirror":
                case "mirror_heroes":
                case "mirror-heroes":
                case "heroes_mirror":
                case "heroes-mirror":
                case "crafted_heroes":
                case "crafted-heroes":
                    mode = RosterMode.CampaignMirrorHeroes;
                    return true;
                case "campaign_mirror_heroes_combat":
                case "campaign-mirror-heroes-combat":
                case "campaign_heroes_combat":
                case "campaign-heroes-combat":
                case "mirror_heroes_combat":
                case "mirror-heroes-combat":
                case "heroes_mirror_combat":
                case "heroes-mirror-combat":
                case "crafted_heroes_combat":
                case "crafted-heroes-combat":
                case "hero_combat":
                case "hero-combat":
                    mode = RosterMode.CampaignMirrorHeroesCombat;
                    return true;
                case "shield_banners":
                case "shield-banners":
                case "banner_shields":
                case "banner-shields":
                case "heraldry_shields":
                case "heraldry-shields":
                case "shields":
                    mode = RosterMode.ShieldBanners;
                    return true;
                default:
                    mode = RosterMode.MatrixOnly;
                    return false;
            }
        }

        public static string FormatRosterMode(RosterMode mode)
        {
            switch (mode)
            {
                case RosterMode.AllCampaignTroops:
                    return "campaign_all";
                case RosterMode.Mixed:
                    return "mixed";
                case RosterMode.FiveModeMatrix:
                    return "five_mode_matrix";
                case RosterMode.RoleMatrixStream:
                    return "role_matrix_stream";
                case RosterMode.RoleMatrixStreamMounted:
                    return "role_matrix_stream_mounted";
                case RosterMode.CampaignMirrorAll:
                    return "campaign_mirror_all";
                case RosterMode.CampaignMirrorHeroes:
                    return "campaign_mirror_heroes";
                case RosterMode.CampaignMirrorHeroesCombat:
                    return "campaign_mirror_heroes_combat";
                case RosterMode.ShieldBanners:
                    return "shield_banners";
                default:
                    return "matrix_only";
            }
        }

        public static string GetCurrentBattleType()
        {
            if (_rosterMode == RosterMode.CampaignMirrorAll)
            {
                if (_weaponPriorityEnabled)
                    return CampaignMirrorAllWeaponPriorityBattleType;

                return _campaignFieldAiEnabled
                    ? CampaignMirrorAllCampaignAiBattleType
                    : CampaignMirrorAllBattleType;
            }

            if (_rosterMode == RosterMode.CampaignMirrorHeroes)
                return CampaignMirrorHeroesBattleType;

            if (_rosterMode == RosterMode.CampaignMirrorHeroesCombat)
                return CampaignMirrorHeroesCombatBattleType;

            if (_rosterMode == RosterMode.ShieldBanners)
                return ShieldBannersBattleType;

            if (_rosterMode == RosterMode.RoleMatrixStreamMounted)
                return RoleMatrixStreamMountedBattleType;

            if (_rosterMode == RosterMode.RoleMatrixStream)
                return RoleMatrixStreamBattleType;

            if (_rosterMode == RosterMode.FiveModeMatrix)
            {
                return _fiveModeWeaponUsageProtocolEnabled
                    ? FiveModeMatrixProtocolBattleType
                    : FiveModeMatrixBattleType;
            }

            return BattleType;
        }

        public static bool IsTestBattleSnapshot(BattleSnapshotMessage snapshot)
        {
            return string.Equals(snapshot?.BattleId, BattleId, System.StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsFiveModeWeaponUsageProtocolSnapshot(BattleSnapshotMessage snapshot)
        {
            return IsTestBattleSnapshot(snapshot) &&
                   string.Equals(snapshot?.BattleType, FiveModeMatrixProtocolBattleType, System.StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsRoleMatrixStreamSnapshot(BattleSnapshotMessage snapshot)
        {
            return IsTestBattleSnapshot(snapshot) &&
                   (string.Equals(snapshot?.BattleType, RoleMatrixStreamBattleType, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(snapshot?.BattleType, RoleMatrixStreamMountedBattleType, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(snapshot?.BattleType, CampaignMirrorAllBattleType, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(snapshot?.BattleType, CampaignMirrorAllCampaignAiBattleType, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(snapshot?.BattleType, CampaignMirrorAllWeaponPriorityBattleType, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(snapshot?.BattleType, CampaignMirrorHeroesBattleType, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(snapshot?.BattleType, CampaignMirrorHeroesCombatBattleType, System.StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsRoleMatrixStreamMountedSnapshot(BattleSnapshotMessage snapshot)
        {
            return IsTestBattleSnapshot(snapshot) &&
                   string.Equals(snapshot?.BattleType, RoleMatrixStreamMountedBattleType, System.StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsShieldBannersSnapshot(BattleSnapshotMessage snapshot)
        {
            return IsTestBattleSnapshot(snapshot) &&
                   string.Equals(snapshot?.BattleType, ShieldBannersBattleType, System.StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsCampaignMirrorAllSnapshot(BattleSnapshotMessage snapshot)
        {
            return IsTestBattleSnapshot(snapshot) &&
                   (string.Equals(snapshot?.BattleType, CampaignMirrorAllBattleType, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(snapshot?.BattleType, CampaignMirrorAllCampaignAiBattleType, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(snapshot?.BattleType, CampaignMirrorAllWeaponPriorityBattleType, System.StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsCampaignMirrorHeroesSnapshot(BattleSnapshotMessage snapshot)
        {
            return IsTestBattleSnapshot(snapshot) &&
                   (string.Equals(snapshot?.BattleType, CampaignMirrorHeroesBattleType, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(snapshot?.BattleType, CampaignMirrorHeroesCombatBattleType, System.StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsCampaignMirrorHeroesCombatSnapshot(BattleSnapshotMessage snapshot)
        {
            return IsTestBattleSnapshot(snapshot) &&
                   string.Equals(snapshot?.BattleType, CampaignMirrorHeroesCombatBattleType, System.StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsCampaignMirrorHeroesCraftedWeaponsCreateTimeSnapshot(BattleSnapshotMessage snapshot)
        {
            if (!IsCampaignMirrorHeroesSnapshot(snapshot))
                return false;

            string mode = TryGetScenarioSourceValue(snapshot, "CraftedWeapons");
            return !string.IsNullOrWhiteSpace(mode) &&
                   TryParseCraftedWeaponsMode(mode, out CraftedWeaponsMode parsedMode) &&
                   parsedMode == CraftedWeaponsMode.CreateTime;
        }

        public static bool IsCampaignMirrorSnapshot(BattleSnapshotMessage snapshot)
        {
            return IsCampaignMirrorAllSnapshot(snapshot) || IsCampaignMirrorHeroesSnapshot(snapshot);
        }

        public static bool IsCampaignMirrorAllCampaignAiSnapshot(BattleSnapshotMessage snapshot)
        {
            return IsTestBattleSnapshot(snapshot) &&
                   (string.Equals(snapshot?.BattleType, CampaignMirrorAllCampaignAiBattleType, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(snapshot?.BattleType, CampaignMirrorAllWeaponPriorityBattleType, System.StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsCampaignMirrorAllWeaponPrioritySnapshot(BattleSnapshotMessage snapshot)
        {
            return IsTestBattleSnapshot(snapshot) &&
                   string.Equals(snapshot?.BattleType, CampaignMirrorAllWeaponPriorityBattleType, System.StringComparison.OrdinalIgnoreCase);
        }

        public static string BuildScenarioSource()
        {
            return ResolverSource +
                   "|WeaponPriority=" + (_weaponPriorityEnabled ? "1" : "0") +
                   "|WeaponPriorityFocus=" + (_weaponPriorityFocus ?? DefaultWeaponPriorityFocus) +
                   "|CraftedWeapons=" + FormatCraftedWeaponsMode(_craftedWeaponsMode) +
                   "|ActiveLimit=" + _roleMatrixStreamActiveLimit +
                   "|WaveLifetimeSeconds=" + _roleMatrixStreamWaveLifetimeSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                   "|PriorityLifetimeSeconds=" + _weaponPriorityLifetimeSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string ResolveWeaponPriorityFocus(BattleSnapshotMessage snapshot)
        {
            string focus = TryGetScenarioSourceValue(snapshot, "WeaponPriorityFocus");
            if (string.IsNullOrWhiteSpace(focus))
                return _weaponPriorityFocus ?? DefaultWeaponPriorityFocus;

            string normalized = focus.Trim().ToLowerInvariant();
            return string.Equals(normalized, WeaponPrioritySuspectsFocus, System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "suspect", System.StringComparison.OrdinalIgnoreCase)
                ? WeaponPrioritySuspectsFocus
                : DefaultWeaponPriorityFocus;
        }

        public static bool IsWeaponPrioritySuspectsSnapshot(BattleSnapshotMessage snapshot)
        {
            return IsCampaignMirrorAllWeaponPrioritySnapshot(snapshot) &&
                   string.Equals(
                       ResolveWeaponPriorityFocus(snapshot),
                       WeaponPrioritySuspectsFocus,
                       System.StringComparison.OrdinalIgnoreCase);
        }

        public static int ResolveRoleMatrixStreamActiveLimit(BattleSnapshotMessage snapshot)
        {
            if (TryGetScenarioSourceInt(snapshot, "ActiveLimit", out int value))
                return System.Math.Max(1, System.Math.Min(value, 240));

            return _roleMatrixStreamActiveLimit;
        }

        public static float ResolveRoleMatrixStreamWaveLifetimeSeconds(BattleSnapshotMessage snapshot)
        {
            if (IsCampaignMirrorAllWeaponPrioritySnapshot(snapshot))
                return ResolveWeaponPriorityLifetimeSeconds(snapshot);

            if (TryGetScenarioSourceFloat(snapshot, "WaveLifetimeSeconds", out float value))
                return System.Math.Max(3f, System.Math.Min(value, 30f));

            return _roleMatrixStreamWaveLifetimeSeconds;
        }

        public static float ResolveWeaponPriorityLifetimeSeconds(BattleSnapshotMessage snapshot)
        {
            if (TryGetScenarioSourceFloat(snapshot, "PriorityLifetimeSeconds", out float value))
                return System.Math.Max(5f, System.Math.Min(value, 180f));

            return _weaponPriorityLifetimeSeconds;
        }

        private static bool TryGetScenarioSourceInt(BattleSnapshotMessage snapshot, string key, out int value)
        {
            value = 0;
            string rawValue = TryGetScenarioSourceValue(snapshot, key);
            return !string.IsNullOrWhiteSpace(rawValue) &&
                   int.TryParse(rawValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        private static bool TryGetScenarioSourceFloat(BattleSnapshotMessage snapshot, string key, out float value)
        {
            value = 0f;
            string rawValue = TryGetScenarioSourceValue(snapshot, key);
            return !string.IsNullOrWhiteSpace(rawValue) &&
                   float.TryParse(rawValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        private static string TryGetScenarioSourceValue(BattleSnapshotMessage snapshot, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            string source = snapshot?.ScenarioContext?.Source;
            if (string.IsNullOrWhiteSpace(source))
                return null;

            string prefix = key + "=";
            string[] parts = source.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i]?.Trim();
                if (string.IsNullOrWhiteSpace(part) ||
                    !part.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return part.Substring(prefix.Length);
            }

            return null;
        }

        public static string GetStatusSummary()
        {
            return "Mode=" + (_enabled ? "ON" : "OFF") +
                   " RosterMode=" + FormatRosterMode(_rosterMode) +
                   " RuntimeScene=" + RuntimeScene +
                   " RuntimeGameType=" + RuntimeGameType +
                   " WeaponSlotMatrix=" + (_includeWeaponSlotMatrix ? "ON" : "OFF") +
                   " FiveModeWeaponUsageProtocol=" + (_fiveModeWeaponUsageProtocolEnabled ? "ON" : "OFF") +
                   " CampaignFieldAI=" + (_campaignFieldAiEnabled ? "ON" : "OFF") +
                   " WeaponPriority=" + (_weaponPriorityEnabled ? "ON" : "OFF") +
                   " WeaponPriorityFocus=" + (_weaponPriorityFocus ?? DefaultWeaponPriorityFocus) +
                   " CraftedWeapons=" + FormatCraftedWeaponsMode(_craftedWeaponsMode) +
                   " WeaponSlotMatrixLimit=" + _weaponSlotMatrixLimit +
                   " RoleMatrixStreamActiveLimit=" + _roleMatrixStreamActiveLimit +
                   " RoleMatrixStreamWaveLifetimeSeconds=" + _roleMatrixStreamWaveLifetimeSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                   " WeaponPriorityLifetimeSeconds=" + _weaponPriorityLifetimeSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
