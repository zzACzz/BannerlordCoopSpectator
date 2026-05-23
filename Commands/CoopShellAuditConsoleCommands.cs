using System.Collections.Generic;
using CoopSpectator.Campaign;
using TaleWorlds.Library;

namespace CoopSpectator.Commands
{
    public static class CoopShellAuditConsoleCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("export_shell_audit", "coop")]
        public static string ExportShellAudit(List<string> args)
        {
            return BattleDetector.ExportCampaignShellAudit();
        }
    }
}
