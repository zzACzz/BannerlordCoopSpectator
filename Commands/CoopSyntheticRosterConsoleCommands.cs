using System;
using System.Collections.Generic;
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
    }
}
