using System;
using System.Collections.Generic;
using CoopSpectator.Multiplayer.Automation;
using TaleWorlds.Library;

namespace CoopSpectator.Commands
{
    public static class CoopAutomationConsoleCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("automation_join", "coop")]
        public static string AutomationJoin(List<string> args)
        {
            if (args == null || args.Count == 0 ||
                string.Equals(args[0], "status", StringComparison.OrdinalIgnoreCase))
            {
                return CoopLobbyAutomationController.GetStatusSummary();
            }

            string action = (args[0] ?? string.Empty).Trim();
            if (string.Equals(action, "cancel", StringComparison.OrdinalIgnoreCase))
            {
                CoopLobbyAutomationController.TryCancel(out string cancelMessage);
                return cancelMessage;
            }

            if (string.Equals(action, "start", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Count < 2)
                    return "ERROR: Usage: coop.automation_join start <RunId>";

                action = args[1];
            }

            CoopLobbyAutomationController.TryArmConfiguredRun(action, out string armMessage);
            return armMessage;
        }
    }
}
