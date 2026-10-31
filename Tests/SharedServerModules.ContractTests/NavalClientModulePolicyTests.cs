using System;
using System.IO;
using System.Linq;
using CoopSpectator.Infrastructure;

internal static class NavalClientModulePolicyTests
{
    internal static void Run()
    {
        string[] valid = { "Native", "SandBoxCore", "Sandbox", "Multiplayer", "CoopSpectatorNavalCatalog",
            "CoopSpectatorMapResources", "CoopSpectator", "NavalDLC" };
        CoopNavalClientModulePolicy.RequireLayout(valid);
        foreach (string id in valid)
            Reject(() => CoopNavalClientModulePolicy.RequireLayout(valid.Where(s => s != id).ToArray()));
        Reject(() => CoopNavalClientModulePolicy.RequireLayout(valid.Concat(new[] { "CoopSpectatorNavalResources" }).ToArray()));
        Reject(() => CoopNavalClientModulePolicy.RequireLayout(valid.Concat(new[] { "NavalDLC" }).ToArray()));
        Reject(() => CoopNavalClientModulePolicy.RequireLayout(valid.Take(6).Concat(new[] { "NavalDLC", "CoopSpectator" }).ToArray()));
        Reject(() => CoopNavalClientModulePolicy.RequireLayout(new[] { "StoryMode" }.Concat(valid).ToArray()));
        string[] submodules = {
            "NavalDLC.dll|NavalDLC.NavalDLCSubModule",
            "NavalDLC.CustomBattle.dll|NavalDLC.CustomBattle.NavalDLCCustomBattleSubModule",
            "NavalDLC.View.dll|NavalDLC.View.NavalDLCViewSubModule",
            "NavalDLC.GauntletUI.dll|NavalDLC.GauntletUI.NavalDLCGauntletUISubModule"
        };
        CoopNavalClientModulePolicy.RequireKnownSubmodules(submodules.Reverse());
        Reject(() => CoopNavalClientModulePolicy.RequireKnownSubmodules(submodules.Take(3)));
        Reject(() => CoopNavalClientModulePolicy.RequireKnownSubmodules(submodules.Concat(new[] { "New.dll|New.CampaignSubmodule" })));
        Reject(() => CoopNavalClientModulePolicy.RequireKnownSubmodules(submodules.Take(3).Concat(new[] { submodules[0] })));
        Console.WriteLine("PASS Naval client module contract: canonical identity/order, no legacy duplicate resources, exact known gameplay definitions and fail-before-mutation; no game/native call.");
    }

    private static void Reject(Action action)
    {
        try { action(); } catch (IOException) { return; }
        throw new InvalidOperationException("Unsafe Naval module contract was accepted.");
    }
}
