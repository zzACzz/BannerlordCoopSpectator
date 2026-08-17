using System.Collections.Generic;
using CoopSpectator.DedicatedHelper;
using CoopSpectator.Infrastructure;
using TaleWorlds.Library;

namespace CoopSpectator.Commands
{
    public static class CoopCampaignMapPrototypeConsoleCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction(
            "map_prototype_start",
            "coop")]
        public static string Start(List<string> args)
        {
            if (!ExperimentalFeatures.EnableCampaignMapPrototype)
            {
                return "Campaign map prototype is disabled. Set " +
                       "COOPSPECTATOR_CAMPAIGN_MAP_PROTOTYPE=1 before launching " +
                       "the game and dedicated server.";
            }

            return DedicatedServerCommands.SendStartCampaignMapPrototypeMission()
                ? "Campaign map prototype mission start requested."
                : "Campaign map prototype mission start failed; inspect CoopSpectator logs.";
        }
    }
}
