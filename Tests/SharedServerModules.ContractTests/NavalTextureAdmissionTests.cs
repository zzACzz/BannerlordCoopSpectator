using System;
using CoopSpectator.Infrastructure;

internal static class NavalTextureAdmissionTests
{
    internal static void Run()
    {
        foreach (bool dedicated in new[] { false, true })
        foreach (bool campaign in new[] { false, true })
        foreach (bool alias in new[] { false, true })
        foreach (bool official in new[] { false, true })
        {
            var admission = new CoopNavalTextureAdmission(); int calls = 0;
            string[] modules = { "Native", alias ? "CoopSpectatorNavalResources" : "Multiplayer", official ? "NavalDLC" : "Sandbox" };
            bool expected = !dedicated && !campaign && alias && !official;
            Action<string> load = name => { Check(name == CoopCampaignContentProfile.NavalAdapterId, "Exact local adapter only"); calls++; };
            Check(admission.Ensure(dedicated, campaign, modules, load) == expected, "All role/profile combinations");
            Check(!admission.Ensure(dedicated, campaign, modules, load) && calls == (expected ? 1 : 0), "No duplicate or ineligible load");
        }
        var retry = new CoopNavalTextureAdmission(); string[] active = { CoopCampaignContentProfile.NavalAdapterId };
        try { retry.Ensure(false, false, active, _ => throw new InvalidOperationException("loader failure")); }
        catch (InvalidOperationException) { }
        int count = 0;
        Check(retry.Ensure(false, false, active, _ => { count++; Check(!retry.Ensure(false, false, active, n => count++), "Reentrant load refused"); }), "Failed activation can retry");
        Check(count == 1 && !retry.Ensure(false, false, active, _ => count++), "Process-wide successful activation retained across games/screens");
        Console.WriteLine("PASS Naval texture admission:16 role/profile combinations, once-only activation, failure and reentrancy; no native API invoked.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
