param([Parameter(Mandatory=$true)][string]$CandidateDirectory)
$ErrorActionPreference = 'Stop'
$game = 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord'
$harmony = Join-Path $CandidateDirectory '0Harmony.dll'
if (-not (Test-Path -LiteralPath $harmony)) { throw 'Candidate Harmony dependency missing.' }
[void][Reflection.Assembly]::LoadFrom($harmony)
# Offline inspection only: no native game startup, patch installation or module writes.
Add-Type -ReferencedAssemblies @($harmony, 'System.Core') -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using HarmonyLib;
public static class SiegeFlightNativeInspection
{
    public static string Run(string game, string candidate)
    {
        var roots = new[] { candidate, Path.Combine(game, "bin", "Win64_Shipping_Client"),
            Path.Combine(game, "Modules", "SandBox", "bin", "Win64_Shipping_Client"),
            Path.Combine(game, "Modules", "SandBoxCore", "bin", "Win64_Shipping_Client"),
            Path.Combine(game, "Modules", "Native", "bin", "Win64_Shipping_Client"),
            Path.Combine(game, "Modules", "Multiplayer", "bin", "Win64_Shipping_Client") };
        ResolveEventHandler resolve = (sender, args) => {
            string name = new AssemblyName(args.Name).Name + ".dll";
            foreach (string root in roots) {
                string path = Path.Combine(root, name);
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
        AppDomain.CurrentDomain.AssemblyResolve += resolve;
        try
        {
            var view = Assembly.LoadFrom(Path.Combine(roots[2], "SandBox.View.dll"));
            var visual = view.GetType("SandBox.View.Map.Visuals.SettlementVisual", true);
            var campaign = Assembly.LoadFrom(Path.Combine(roots[1], "TaleWorlds.CampaignSystem.dll"));
            var missile = campaign.GetType("TaleWorlds.CampaignSystem.Siege.SiegeEvent+SiegeEngineMissile", true);
            var side = campaign.GetType("TaleWorlds.CampaignSystem.Siege.ISiegeEventSide", true);
            var tick = visual.GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic);
            var calculation = visual.GetMethod("CalculateDataAndDurationsForSiegeMachine", BindingFlags.Instance | BindingFlags.NonPublic);
            var observer = new DynamicMethod("NeverExecutedObserver", typeof(void), new[] { visual, missile, typeof(object) });
            observer.GetILGenerator().Emit(OpCodes.Ret);
            var original = PatchProcessor.GetOriginalInstructions(tick).ToList();
            var module = Assembly.LoadFrom(Path.Combine(candidate, "CoopSpectator.dll"));
            var matcher = module.GetType("CoopSpectator.Patches.CoopSiegeFlightCallSite", true)
                .GetMethod("TryRewrite", BindingFlags.Static | BindingFlags.NonPublic);
            object[] parameters = { original, tick, calculation, missile, side.GetProperty("BattleSide").GetGetMethod(), observer, null };
            bool accepted = (bool)matcher.Invoke(null, parameters);
            if (!accepted) throw new InvalidOperationException("Installed native call-site shape rejected");
            var rewritten = (List<CodeInstruction>)parameters[6];
            if (rewritten.Count != original.Count + 5 || original.Any(c => c.Calls(observer)))
                throw new InvalidOperationException("Insertion/original-integrity mismatch");
            return "PASS offline native matcher; original=" + original.Count + "; rewritten=" + rewritten.Count +
                "; SandBox.View MVID=" + view.ManifestModule.ModuleVersionId + "; no patch installed";
        }
        finally { AppDomain.CurrentDomain.AssemblyResolve -= resolve; }
    }
}
'@
[SiegeFlightNativeInspection]::Run($game, $CandidateDirectory)
