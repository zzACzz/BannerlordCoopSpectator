using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using System;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Multiplayer;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.GameMode
{
    public sealed class MissionMultiplayerCoopBattle : MissionMultiplayerGameModeBase
    {
        private const uint DefaultAttackerColor = 0xFFCC2222u;
        private const uint DefaultAttackerColor2 = 0xFF661111u;
        private const uint DefaultDefenderColor = 0xFF2222CCu;
        private const uint DefaultDefenderColor2 = 0xFF111166u;
        private const string DefaultAttackerCultureId = "empire";
        private const string DefaultDefenderCultureId = "vlandia";
        private static readonly MethodInfo MissionTeamCollectionSetPlayerTeamAuxMethod =
            typeof(Mission).GetNestedType("TeamCollection", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?.GetMethod("SetPlayerTeamAux", BindingFlags.Instance | BindingFlags.NonPublic);

        private bool _hasInitialized;
        private bool _hasLoggedFirstServerTick;

        private sealed class TeamAppearanceContract
        {
            public string CultureId;
            public uint Color;
            public uint Color2;
            public string BannerCode;
            public Banner Banner;
            public string Source;
        }

        public override MultiplayerGameType GetMissionType()
        {
            return MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(Mission?.SceneName)
                ? MultiplayerGameType.Battle
                : MultiplayerGameType.TeamDeathmatch;
        }

        public override bool IsGameModeUsingOpposingTeams => true;

        public override bool IsGameModeHidingAllAgentVisuals => false;

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            _hasInitialized = false;
            _hasLoggedFirstServerTick = false;
            CoopBattlePhaseRuntimeState.StartMission(Mission, "CoopBattle.OnBehaviorInitialize");
        }

        public override void AfterStart()
        {
            if (!GameNetwork.IsServer)
            {
                base.AfterStart();
                return;
            }

            try
            {
                ModLogger.Info("CoopBattle server: AfterStart ENTER.");
                EnsureOpposingTeamsReady("CoopBattle.AfterStart pre-base");
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("CoopBattle server: EnsureOpposingTeamsReady failed before base.AfterStart.", ex);
            }

            base.AfterStart();

            try
            {
                EnsureOpposingTeamsReady("CoopBattle.AfterStart post-base");
                ModLogger.Info("CoopBattle server: AfterStart EXIT.");
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("CoopBattle server: EnsureOpposingTeamsReady failed after base.AfterStart.", ex);
            }
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            Mission mission = Mission;
            if (mission?.Teams == null)
                return;

            if (!GameNetwork.IsServer)
                return;

            if (!_hasLoggedFirstServerTick)
            {
                _hasLoggedFirstServerTick = true;
                ModLogger.Info(
                    "CoopBattle server: first mission tick entered. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " MissionType=" + GetMissionType() +
                    " Mode=" + mission.Mode);
            }

            if (!_hasInitialized)
            {
                _hasInitialized = true;
                try
                {
                    InitializeTeamsAndMinimalSpawn();
                    CoopMissionSpawnLogic.RunCoopBattleSpawnOwnerTick(mission, "CoopBattle.OnMissionTick initialize");
                    CoopMissionSpawnLogic.RunCoopBattlePhaseOwnerTick(mission, "CoopBattle.OnMissionTick initialize");
                }
                catch (System.Exception ex)
                {
                    ModLogger.Error("CoopBattle server: InitializeTeamsAndMinimalSpawn failed.", ex);
                }

                return;
            }

            try
            {
                CoopMissionSpawnLogic.RunCoopBattleSpawnOwnerTick(mission, "CoopBattle.OnMissionTick");
                CoopMissionSpawnLogic.RunCoopBattlePhaseOwnerTick(mission, "CoopBattle.OnMissionTick");
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("CoopBattle server: phase owner tick failed.", ex);
            }
        }

        private void InitializeTeamsAndMinimalSpawn()
        {
            Mission mission = Mission;
            if (mission == null)
                return;

            EnsureOpposingTeamsReady("CoopBattle.InitializeTeams");
            ModLogger.Info("CoopBattle mission started (teams initialized; no agents yet).");
        }

        private void EnsureOpposingTeamsReady(string source)
        {
            EnsureOpposingTeamsReadyForMission(Mission, source);
        }

        internal static void TryApplyAuthoritativeBattleCultureOptionsFromRuntimeState(string source)
        {
            TeamAppearanceContract attackerAppearance = ResolveTeamAppearance(BattleSideEnum.Attacker);
            TeamAppearanceContract defenderAppearance = ResolveTeamAppearance(BattleSideEnum.Defender);
            TryApplyAuthoritativeCultureOptions(attackerAppearance, defenderAppearance, source);
        }

        internal static void EnsureOpposingTeamsReadyForMission(Mission mission, string source)
        {
            if (mission == null)
                return;

            TeamAppearanceContract attackerAppearance = ResolveTeamAppearance(BattleSideEnum.Attacker);
            TeamAppearanceContract defenderAppearance = ResolveTeamAppearance(BattleSideEnum.Defender);
            TryApplyAuthoritativeCultureOptions(attackerAppearance, defenderAppearance, source);

            if (mission.Teams.Attacker == null)
                mission.Teams.Add(BattleSideEnum.Attacker, attackerAppearance.Color, attackerAppearance.Color2, attackerAppearance.Banner, false, false, true);
            else
                TryRepairExistingTeamAppearance(mission.Teams.Attacker, attackerAppearance, source);

            if (mission.Teams.Defender == null)
                mission.Teams.Add(BattleSideEnum.Defender, defenderAppearance.Color, defenderAppearance.Color2, defenderAppearance.Banner, false, false, true);
            else
                TryRepairExistingTeamAppearance(mission.Teams.Defender, defenderAppearance, source);

            EnsureOpposingTeamRelations(mission);

            CoopBattlePhaseRuntimeState.AdvanceToAtLeast(CoopBattlePhase.SideSelection, source ?? "CoopBattle.EnsureOpposingTeamsReady", mission);
            ModLogger.Info(
                "CoopBattle server: ensured opposing teams exist. " +
                "Source=" + (source ?? "null") +
                " HasAttacker=" + (mission.Teams.Attacker != null) +
                " HasDefender=" + (mission.Teams.Defender != null) +
                " AttackerCulture=" + (attackerAppearance.CultureId ?? "null") +
                " DefenderCulture=" + (defenderAppearance.CultureId ?? "null") +
                " AttackerBannerCodeLength=" + (attackerAppearance.BannerCode?.Length ?? 0) +
                " DefenderBannerCodeLength=" + (defenderAppearance.BannerCode?.Length ?? 0));
        }

        internal static bool TryRefreshMissionPlayerTeamRelationView(
            Mission mission,
            Team playerTeam,
            string source,
            out string diagnostics)
        {
            diagnostics = "mission-or-player-team-null";
            if (mission == null || playerTeam == null)
                return false;

            EnsureOpposingTeamRelations(mission);

            string refreshMode = "set-player-team";
            if (!ReferenceEquals(mission.PlayerTeam, playerTeam))
            {
                mission.PlayerTeam = playerTeam;
            }
            else
            {
                refreshMode = "set-player-team-aux";
                if (!TryInvokeMissionPlayerTeamAux(mission, playerTeam, out string refreshDiagnostics))
                {
                    diagnostics = refreshDiagnostics;
                    return false;
                }
            }

            diagnostics =
                "RefreshMode=" + refreshMode +
                " PlayerTeam=" + (mission.PlayerTeam == null ? "null" : mission.PlayerTeam.Side + "#" + mission.PlayerTeam.TeamIndex) +
                " PlayerEnemyTeam=" + (mission.PlayerEnemyTeam == null ? "null" : mission.PlayerEnemyTeam.Side + "#" + mission.PlayerEnemyTeam.TeamIndex) +
                " Source=" + (source ?? "unknown");
            return ReferenceEquals(mission.PlayerTeam, playerTeam) && mission.PlayerEnemyTeam != null;
        }

        private static void EnsureOpposingTeamRelations(Mission mission)
        {
            Team attacker = mission?.AttackerTeam ?? mission?.Teams?.Attacker;
            Team defender = mission?.DefenderTeam ?? mission?.Teams?.Defender;
            if (attacker == null || defender == null || ReferenceEquals(attacker, defender))
                return;

            if (!attacker.IsEnemyOf(defender))
                attacker.SetIsEnemyOf(defender, true);

            if (!defender.IsEnemyOf(attacker))
                defender.SetIsEnemyOf(attacker, true);
        }

        private static bool TryInvokeMissionPlayerTeamAux(Mission mission, Team playerTeam, out string diagnostics)
        {
            diagnostics = "mission-team-aux-unavailable";
            if (mission?.Teams == null || playerTeam == null)
                return false;

            MethodInfo setPlayerTeamAux = MissionTeamCollectionSetPlayerTeamAuxMethod ??
                mission.Teams.GetType().GetMethod("SetPlayerTeamAux", BindingFlags.Instance | BindingFlags.NonPublic);
            if (setPlayerTeamAux == null)
            {
                diagnostics = "set-player-team-aux-missing";
                return false;
            }

            int playerTeamIndex = IndexOfMissionTeam(mission, playerTeam);
            if (playerTeamIndex < 0)
            {
                diagnostics = "player-team-index-missing";
                return false;
            }

            setPlayerTeamAux.Invoke(mission.Teams, new object[] { playerTeamIndex });
            diagnostics = "set-player-team-aux-invoked Index=" + playerTeamIndex;
            return true;
        }

        private static int IndexOfMissionTeam(Mission mission, Team team)
        {
            if (mission?.Teams == null || team == null)
                return -1;

            for (int i = 0; i < mission.Teams.Count; i++)
            {
                if (ReferenceEquals(mission.Teams[i], team))
                    return i;
            }

            return -1;
        }

        private static TeamAppearanceContract ResolveTeamAppearance(BattleSideEnum side)
        {
            string fallbackCultureId = side == BattleSideEnum.Attacker ? DefaultAttackerCultureId : DefaultDefenderCultureId;
            uint fallbackColor = side == BattleSideEnum.Attacker ? DefaultAttackerColor : DefaultDefenderColor;
            uint fallbackColor2 = side == BattleSideEnum.Attacker ? DefaultAttackerColor2 : DefaultDefenderColor2;

            string cultureId = BattleSnapshotRuntimeState.ResolveSideCultureId(side, fallbackCultureId);
            uint color = BattleSnapshotRuntimeState.ResolveSideColor(side, fallbackColor);
            uint color2 = BattleSnapshotRuntimeState.ResolveSideColor2(side, fallbackColor2);
            string bannerCode = BattleSnapshotRuntimeState.ResolveSideBannerCode(side, null);
            string source = "battle-snapshot";

            BasicCultureObject culture = !string.IsNullOrWhiteSpace(cultureId)
                ? MBObjectManager.Instance?.GetObject<BasicCultureObject>(cultureId)
                : null;
            if (culture != null)
            {
                if (color == 0u)
                    color = culture.Color;
                if (color2 == 0u)
                    color2 = culture.Color2;
                if (string.IsNullOrWhiteSpace(bannerCode))
                    bannerCode = ResolveBannerCode(culture);
            }

            if (color == 0u)
                color = fallbackColor;
            if (color2 == 0u)
                color2 = fallbackColor2;

            Banner banner = null;
            if (!string.IsNullOrWhiteSpace(bannerCode))
            {
                try
                {
                    banner = new Banner(bannerCode, color, color2);
                }
                catch (Exception ex)
                {
                    source += "|banner-invalid:" + ex.GetType().Name;
                }
            }

            return new TeamAppearanceContract
            {
                CultureId = cultureId,
                Color = color,
                Color2 = color2,
                BannerCode = bannerCode,
                Banner = banner,
                Source = source
            };
        }

        private static void TryApplyAuthoritativeCultureOptions(
            TeamAppearanceContract attackerAppearance,
            TeamAppearanceContract defenderAppearance,
            string source)
        {
            try
            {
                ApplyCultureOption(MultiplayerOptions.MultiplayerOptionsAccessMode.CurrentMapOptions, attackerAppearance?.CultureId, defenderAppearance?.CultureId);
                ApplyCultureOption(MultiplayerOptions.MultiplayerOptionsAccessMode.NextMapOptions, attackerAppearance?.CultureId, defenderAppearance?.CultureId);

                ModLogger.Info(
                    "CoopBattle server: applied authoritative battle cultures to MultiplayerOptions. " +
                    "AttackerCulture=" + (attackerAppearance?.CultureId ?? "null") +
                    " DefenderCulture=" + (defenderAppearance?.CultureId ?? "null") +
                    " Source=" + (source ?? "unknown"));
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattle server: failed to apply authoritative battle cultures: " + ex.Message);
            }
        }

        private static void ApplyCultureOption(
            MultiplayerOptions.MultiplayerOptionsAccessMode accessMode,
            string attackerCultureId,
            string defenderCultureId)
        {
            if (!string.IsNullOrWhiteSpace(attackerCultureId))
                MultiplayerOptionsExtensions.SetValue(MultiplayerOptions.OptionType.CultureTeam1, attackerCultureId, accessMode);

            if (!string.IsNullOrWhiteSpace(defenderCultureId))
                MultiplayerOptionsExtensions.SetValue(MultiplayerOptions.OptionType.CultureTeam2, defenderCultureId, accessMode);
        }

        private static void TryRepairExistingTeamAppearance(Team team, TeamAppearanceContract appearance, string source)
        {
            if (team == null || appearance == null)
                return;

            bool changedColor =
                TrySetMemberValue(team, "Color", appearance.Color) ||
                TrySetMemberValue(team, "_color", appearance.Color) ||
                TrySetMemberValue(team, "<Color>k__BackingField", appearance.Color);
            bool changedColor2 =
                TrySetMemberValue(team, "Color2", appearance.Color2) ||
                TrySetMemberValue(team, "_color2", appearance.Color2) ||
                TrySetMemberValue(team, "<Color2>k__BackingField", appearance.Color2);
            bool changedBanner =
                TrySetMemberValue(team, "Banner", appearance.Banner) ||
                TrySetMemberValue(team, "_banner", appearance.Banner) ||
                TrySetMemberValue(team, "<Banner>k__BackingField", appearance.Banner);

            if (changedColor || changedColor2 || changedBanner)
            {
                ModLogger.Info(
                    "CoopBattle server: repaired existing team appearance. " +
                    "TeamIndex=" + team.TeamIndex +
                    " Side=" + team.Side +
                    " ChangedColor=" + changedColor +
                    " ChangedColor2=" + changedColor2 +
                    " ChangedBanner=" + changedBanner +
                    " Source=" + (source ?? "unknown"));
            }
        }

        private static string ResolveBannerCode(BasicCultureObject culture)
        {
            if (culture == null)
                return null;

            object bannerValue = GetMemberValue(culture, "Banner");
            if (bannerValue is string bannerCode)
                return string.IsNullOrWhiteSpace(bannerCode) ? null : bannerCode;

            if (bannerValue is Banner banner)
                return banner.BannerCode;

            return GetMemberValue(bannerValue, "BannerCode") as string;
        }

        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    return property.GetValue(instance, null);
                }
                catch
                {
                }
            }

            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                try
                {
                    return field.GetValue(instance);
                }
                catch
                {
                }
            }

            return null;
        }

        private static bool TrySetMemberValue(object instance, string memberName, object value)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
                return false;

            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    property.SetValue(instance, value, null);
                    return true;
                }
                catch
                {
                }
            }

            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                try
                {
                    field.SetValue(instance, value);
                    return true;
                }
                catch
                {
                }
            }

            return false;
        }
    }
}
