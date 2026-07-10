using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using TaleWorlds.Library;

namespace CoopSpectator.Commands
{
    public static class CoopSyntheticRosterConsoleCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("test_campaign_roster", "coop")]
        public static string TestCampaignRoster(List<string> args)
        {
            if (args == null || args.Count == 0)
            {
                return "Synthetic roster mode is " +
                       Campaign.BattleDetector.GetSyntheticRosterStatusSummary() +
                       ". Usage: coop.test_campaign_roster <on|off|status>.";
            }

            string action = (args[0] ?? string.Empty).Trim().ToLowerInvariant();
            if (action == "status")
            {
                return "Synthetic roster mode is " +
                       Campaign.BattleDetector.GetSyntheticRosterStatusSummary() + ".";
            }

            if (action == "on" || action == "enable" || action == "1" || action == "true")
            {
                string summary = Campaign.BattleDetector.SetSyntheticAllCampaignTroopsRosterEnabled(true);
                return "Synthetic campaign roster test mode enabled. " + summary;
            }

            if (action == "off" || action == "disable" || action == "0" || action == "false")
            {
                Campaign.BattleDetector.SetSyntheticAllCampaignTroopsRosterEnabled(false);
                return "Synthetic campaign roster test mode disabled.";
            }

            return "Usage: coop.test_campaign_roster <on|off|status>.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("test_hero_roster", "coop")]
        public static string TestHeroRoster(List<string> args)
        {
            if (args == null || args.Count == 0)
            {
                return "Synthetic hero roster mode is " +
                       (Campaign.BattleDetector.IsSyntheticLiveHeroesRosterEnabled() ? "ON" : "OFF") +
                       ". Usage: coop.test_hero_roster <on|off|status>.";
            }

            string action = (args[0] ?? string.Empty).Trim().ToLowerInvariant();
            if (action == "status")
            {
                return "Synthetic roster mode is " +
                       Campaign.BattleDetector.GetSyntheticRosterStatusSummary() + ".";
            }

            if (action == "on" || action == "enable" || action == "1" || action == "true")
            {
                string summary = Campaign.BattleDetector.SetSyntheticLiveHeroesRosterEnabled(true);
                return "Synthetic hero roster mode enabled. " + summary;
            }

            if (action == "off" || action == "disable" || action == "0" || action == "false")
            {
                Campaign.BattleDetector.SetSyntheticLiveHeroesRosterEnabled(false);
                return "Synthetic hero roster mode disabled.";
            }

            return "Usage: coop.test_hero_roster <on|off|status>.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("test_battle", "coop")]
        public static string TestBattle(List<string> args)
        {
            if (args == null || args.Count == 0)
            {
                return "Coop test battle is " +
                       Campaign.BattleDetector.GetCoopTestBattleStatusSummary() +
                       ". Usage: coop.test_battle <on|off|status|roster matrix_only|roster campaign_all|roster campaign_mirror_all|roster campaign_mirror_heroes|roster campaign_mirror_heroes_combat|roster shield_banners|roster mixed|roster five_mode_matrix|roster role_matrix_stream|roster role_matrix_stream_mounted|matrix on|matrix off|five_mode_protocol on|five_mode_protocol off|campaign_ai on|campaign_ai off|weapon_priority on|weapon_priority all|weapon_priority suspects|weapon_priority off|crafted_weapons safe|crafted_weapons create_time|debug possession on|limit N|active_limit N|wave_lifetime seconds|priority_lifetime seconds|matrix_progress|matrix_progress reset|matrix_unsafe list|matrix_unsafe add M00001 [reason]|matrix_unsafe add_last [reason]|matrix_unsafe remove M00001|matrix_unsafe clear>.";
            }

            string action = (args[0] ?? string.Empty).Trim().ToLowerInvariant();
            if (action == "status")
                return "Coop test battle is " + Campaign.BattleDetector.GetCoopTestBattleStatusSummary() + ".";

            if (action == "on" || action == "enable" || action == "1" || action == "true")
            {
                string summary = Campaign.BattleDetector.SetCoopTestBattleEnabled(true);
                return "Coop test battle enabled. " + summary;
            }

            if (action == "off" || action == "disable" || action == "0" || action == "false")
            {
                Campaign.BattleDetector.SetCoopTestBattleEnabled(false);
                return "Coop test battle disabled.";
            }

            if (action == "matrix" && args.Count >= 2)
            {
                string matrixAction = (args[1] ?? string.Empty).Trim().ToLowerInvariant();
                if (matrixAction == "on" || matrixAction == "enable" || matrixAction == "1" || matrixAction == "true")
                {
                    CoopTestBattleOptions.SetIncludeWeaponSlotMatrix(true);
                    return "Coop test battle weapon-slot matrix enabled. " + CoopTestBattleOptions.GetStatusSummary() + ".";
                }

                if (matrixAction == "off" || matrixAction == "disable" || matrixAction == "0" || matrixAction == "false")
                {
                    CoopTestBattleOptions.SetIncludeWeaponSlotMatrix(false);
                    return "Coop test battle weapon-slot matrix disabled. " + CoopTestBattleOptions.GetStatusSummary() + ".";
                }
            }

            if ((action == "five_mode_protocol" || action == "5_mode_protocol" || action == "weapon_usage_protocol") &&
                args.Count >= 2)
            {
                string protocolAction = (args[1] ?? string.Empty).Trim().ToLowerInvariant();
                if (protocolAction == "on" || protocolAction == "enable" || protocolAction == "1" || protocolAction == "true")
                {
                    CoopTestBattleOptions.SetFiveModeWeaponUsageProtocolEnabled(true);
                    return "Coop test battle five-mode weapon usage protocol enabled. " + CoopTestBattleOptions.GetStatusSummary() + ".";
                }

                if (protocolAction == "off" || protocolAction == "disable" || protocolAction == "0" || protocolAction == "false")
                {
                    CoopTestBattleOptions.SetFiveModeWeaponUsageProtocolEnabled(false);
                    return "Coop test battle five-mode weapon usage protocol disabled. " + CoopTestBattleOptions.GetStatusSummary() + ".";
                }
            }

            if ((action == "campaign_ai" || action == "field_ai" || action == "campaign_field_ai") &&
                args.Count >= 2)
            {
                string aiAction = (args[1] ?? string.Empty).Trim().ToLowerInvariant();
                if (aiAction == "on" || aiAction == "enable" || aiAction == "1" || aiAction == "true")
                {
                    CoopTestBattleOptions.SetCampaignFieldAiEnabled(true);
                    return "Coop test battle campaign field AI enabled. " + CoopTestBattleOptions.GetStatusSummary() + ".";
                }

                if (aiAction == "off" || aiAction == "disable" || aiAction == "0" || aiAction == "false")
                {
                    CoopTestBattleOptions.SetCampaignFieldAiEnabled(false);
                    return "Coop test battle campaign field AI disabled. " + CoopTestBattleOptions.GetStatusSummary() + ".";
                }
            }

            if ((action == "weapon_priority" || action == "weapon-priority" || action == "priority_test" || action == "priority-test") &&
                args.Count >= 2)
            {
                string priorityAction = (args[1] ?? string.Empty).Trim().ToLowerInvariant();
                if (priorityAction == "on" || priorityAction == "enable" || priorityAction == "1" || priorityAction == "true")
                {
                    CoopTestBattleOptions.SetWeaponPriorityFocus(CoopTestBattleOptions.DefaultWeaponPriorityFocus);
                    CoopTestBattleOptions.SetWeaponPriorityEnabled(true);
                    return "Coop test battle weapon priority test enabled. " + CoopTestBattleOptions.GetStatusSummary() + ".";
                }

                if (priorityAction == "all" || priorityAction == "full" || priorityAction == "reset")
                {
                    CoopTestBattleOptions.SetWeaponPriorityFocus(CoopTestBattleOptions.DefaultWeaponPriorityFocus);
                    CoopTestBattleOptions.SetWeaponPriorityEnabled(true);
                    return "Coop test battle weapon priority full test enabled. " + CoopTestBattleOptions.GetStatusSummary() + ".";
                }

                if (priorityAction == "suspects" || priorityAction == "suspect" || priorityAction == "focus_suspects" || priorityAction == "focus-suspects")
                {
                    CoopTestBattleOptions.SetWeaponPriorityFocus(CoopTestBattleOptions.WeaponPrioritySuspectsFocus);
                    CoopTestBattleOptions.SetWeaponPriorityEnabled(true);
                    return "Coop test battle weapon priority suspect focus enabled. " + CoopTestBattleOptions.GetStatusSummary() + ".";
                }

                if (priorityAction == "off" || priorityAction == "disable" || priorityAction == "0" || priorityAction == "false")
                {
                    CoopTestBattleOptions.SetWeaponPriorityEnabled(false);
                    return "Coop test battle weapon priority test disabled. " + CoopTestBattleOptions.GetStatusSummary() + ".";
                }
            }

            if ((action == "crafted_weapons" || action == "crafted-weapons" || action == "crafted_weapon" || action == "crafted-weapon") &&
                args.Count >= 2)
            {
                string craftedWeaponsAction = (args[1] ?? string.Empty).Trim().ToLowerInvariant();
                if (craftedWeaponsAction == "status")
                    return "Coop test battle crafted weapons mode is " +
                           CoopTestBattleOptions.FormatCraftedWeaponsMode(CoopTestBattleOptions.CurrentCraftedWeaponsMode) +
                           ". " + CoopTestBattleOptions.GetStatusSummary() + ".";

                if (CoopTestBattleOptions.TryParseCraftedWeaponsMode(
                        craftedWeaponsAction,
                        out CoopTestBattleOptions.CraftedWeaponsMode craftedWeaponsMode))
                {
                    CoopTestBattleOptions.SetCraftedWeaponsMode(craftedWeaponsMode);
                    return "Coop test battle crafted weapons mode updated. " + CoopTestBattleOptions.GetStatusSummary() + ".";
                }

                return "Unknown coop test battle crafted weapons mode. Use safe or create_time.";
            }

            if (action == "debug" && args.Count >= 2)
            {
                string debugTarget = (args[1] ?? string.Empty).Trim().ToLowerInvariant();
                if (debugTarget == "possession" || debugTarget == "possess" || debugTarget == "controlled_agent" || debugTarget == "controlled-agent")
                {
                    string debugAction = args.Count >= 3 ? (args[2] ?? string.Empty).Trim().ToLowerInvariant() : "status";
                    if (debugAction == "status")
                        return CoopDebugConfig.GetPossessionDiagnosticsStatus() + ".";

                    if (debugAction == "on" || debugAction == "enable" || debugAction == "1" || debugAction == "true")
                    {
                        CoopDebugConfig.SetPossessionDiagnosticsRuntimeOverride(true);
                        return "Possession diagnostics enabled. " + CoopDebugConfig.GetPossessionDiagnosticsStatus() + ".";
                    }

                    if (debugAction == "off" || debugAction == "disable" || debugAction == "0" || debugAction == "false")
                    {
                        CoopDebugConfig.SetPossessionDiagnosticsRuntimeOverride(false);
                        return "Possession diagnostics disabled. " + CoopDebugConfig.GetPossessionDiagnosticsStatus() + ".";
                    }

                    if (debugAction == "reset" || debugAction == "env" || debugAction == "inherit")
                    {
                        CoopDebugConfig.SetPossessionDiagnosticsRuntimeOverride(null);
                        return "Possession diagnostics runtime override cleared. " + CoopDebugConfig.GetPossessionDiagnosticsStatus() + ".";
                    }

                    return "Usage: coop.test_battle debug possession <on|off|status|reset>.";
                }
            }

            if (action == "roster" && args.Count >= 2)
            {
                if (CoopTestBattleOptions.TryParseRosterMode(args[1], out CoopTestBattleOptions.RosterMode rosterMode))
                {
                    CoopTestBattleOptions.SetRosterMode(rosterMode);
                    return "Coop test battle roster mode updated. " + CoopTestBattleOptions.GetStatusSummary() + ".";
                }

                return "Unknown coop test battle roster mode. Use matrix_only, campaign_all, campaign_mirror_all, campaign_mirror_heroes, campaign_mirror_heroes_combat, shield_banners, mixed, five_mode_matrix, role_matrix_stream, or role_matrix_stream_mounted.";
            }

            if (action == "limit" && args.Count >= 2 && int.TryParse(args[1], out int limit))
            {
                CoopTestBattleOptions.SetWeaponSlotMatrixLimit(limit);
                return "Coop test battle weapon-slot matrix limit updated. " + CoopTestBattleOptions.GetStatusSummary() + ".";
            }

            if ((action == "active_limit" || action == "stream_limit") &&
                args.Count >= 2 &&
                int.TryParse(args[1], out int activeLimit))
            {
                CoopTestBattleOptions.SetRoleMatrixStreamActiveLimit(activeLimit);
                return "Coop test battle role-matrix stream active limit updated. " + CoopTestBattleOptions.GetStatusSummary() + ".";
            }

            if ((action == "wave_lifetime" || action == "wave_seconds") &&
                args.Count >= 2 &&
                float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float waveLifetime))
            {
                CoopTestBattleOptions.SetRoleMatrixStreamWaveLifetimeSeconds(waveLifetime);
                return "Coop test battle role-matrix stream wave lifetime updated. " + CoopTestBattleOptions.GetStatusSummary() + ".";
            }

            if ((action == "priority_lifetime" || action == "priority_seconds" || action == "weapon_priority_lifetime") &&
                args.Count >= 2 &&
                float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float priorityLifetime))
            {
                CoopTestBattleOptions.SetWeaponPriorityLifetimeSeconds(priorityLifetime);
                return "Coop test battle weapon priority lifetime updated. " + CoopTestBattleOptions.GetStatusSummary() + ".";
            }

            if (action == "matrix_progress")
            {
                if (args.Count >= 2 && string.Equals(args[1], "reset", StringComparison.OrdinalIgnoreCase))
                {
                    return CoopRoleMatrixStreamBridgeFile.ClearProgress()
                        ? "Coop role-matrix stream progress cleared. Path=" + CoopRoleMatrixStreamBridgeFile.GetProgressFilePath()
                        : "Failed to clear coop role-matrix stream progress.";
                }

                return "Coop role-matrix stream progress: " + CoopRoleMatrixStreamBridgeFile.FormatProgress();
            }

            if (action == "matrix_unsafe" && args.Count >= 2)
            {
                string unsafeAction = (args[1] ?? string.Empty).Trim().ToLowerInvariant();
                if (unsafeAction == "list" || unsafeAction == "status")
                    return "Coop role-matrix unsafe table: " + CoopRoleMatrixStreamBridgeFile.FormatUnsafeEntries(24);

                if (unsafeAction == "clear")
                {
                    return CoopRoleMatrixStreamBridgeFile.ClearUnsafeMatrices()
                        ? "Coop role-matrix unsafe table cleared. Path=" + CoopRoleMatrixStreamBridgeFile.GetUnsafeFilePath()
                        : "Failed to clear coop role-matrix unsafe table.";
                }

                if (unsafeAction == "add_last")
                {
                    string reason = args.Count >= 3 ? string.Join(" ", args.GetRange(2, args.Count - 2)) : "suspect-last-wave";
                    return CoopRoleMatrixStreamBridgeFile.AddLastProgressActiveMatricesToUnsafe(reason, "coop.test_battle matrix_unsafe add_last")
                        ? "Last active role-matrix stream wave marked unsafe. " + CoopRoleMatrixStreamBridgeFile.FormatUnsafeEntries(24)
                        : "No active matrices found in role-matrix stream progress.";
                }

                if (unsafeAction == "add" && args.Count >= 3)
                {
                    string reason = args.Count >= 4 ? string.Join(" ", args.GetRange(3, args.Count - 3)) : "manual";
                    return CoopRoleMatrixStreamBridgeFile.AddUnsafeMatrix(args[2], reason, "coop.test_battle matrix_unsafe add", string.Empty)
                        ? "Role-matrix marked unsafe. " + CoopRoleMatrixStreamBridgeFile.FormatUnsafeEntries(24)
                        : "Failed to mark role-matrix unsafe.";
                }

                if ((unsafeAction == "remove" || unsafeAction == "delete") && args.Count >= 3)
                {
                    return CoopRoleMatrixStreamBridgeFile.RemoveUnsafeMatrix(args[2])
                        ? "Role-matrix removed from unsafe table. " + CoopRoleMatrixStreamBridgeFile.FormatUnsafeEntries(24)
                        : "Failed to remove role-matrix from unsafe table.";
                }
            }

            return "Usage: coop.test_battle <on|off|status|roster matrix_only|roster campaign_all|roster campaign_mirror_all|roster campaign_mirror_heroes|roster campaign_mirror_heroes_combat|roster shield_banners|roster mixed|roster five_mode_matrix|roster role_matrix_stream|roster role_matrix_stream_mounted|matrix on|matrix off|five_mode_protocol on|five_mode_protocol off|campaign_ai on|campaign_ai off|weapon_priority on|weapon_priority all|weapon_priority suspects|weapon_priority off|crafted_weapons safe|crafted_weapons create_time|debug possession on|limit N|active_limit N|wave_lifetime seconds|priority_lifetime seconds|matrix_progress|matrix_progress reset|matrix_unsafe list|matrix_unsafe add M00001 [reason]|matrix_unsafe add_last [reason]|matrix_unsafe remove M00001|matrix_unsafe clear>.";
        }
    }
}
