using System;
using CoopSpectator.Infrastructure;

internal static class EncounterIdentityTests
{
    internal static void Run()
    {
        var lease = new CoopCampaignMapEncounterIdentity();
        object context = new object(), overlay = new object(), a = new object(), b = new object();
        var input = new[] { a, a };
        Check(lease.TryUpdate(context, overlay, "encounter", input, new[] { b }, out var first, out var defense));
        Check(first[0] != first[1] && first[0] != defense[0]);
        string token = first[0], second = first[1], defender = defense[0];
        first[0] = "tampered"; input[0] = b;
        Check(lease.TryResolve(context, overlay, "encounter", token, out var found) && ReferenceEquals(found, a));
        Check(!lease.TryResolve(context, overlay, "encounter", "tampered", out _));
        for (int tick = 0; tick < 100; tick++)
        {
            Check(lease.TryUpdate(context, overlay, "encounter", new[] { a, a }, new[] { b }, out var stable, out var stableDefense));
            Check(stable[0] == token && stable[1] == second && stableDefense[0] == defender);
        }
        Check(lease.TryUpdate(context, overlay, "encounter", new[] { b }, new[] { a }, out var swapped, out _));
        Check(swapped[0] != token && swapped[0] != defender);
        Check(!lease.TryResolve(context, overlay, "encounter", token, out _));
        Check(!lease.TryResolve(context, overlay, "encounter", second, out _));
        Check(!lease.TryResolve(context, overlay, "encounter", defender, out _));
        Check(!lease.TryResolve(new object(), overlay, "encounter", swapped[0], out _));
        Check(!lease.TryResolve(context, new object(), "encounter", swapped[0], out _));
        Check(!lease.TryResolve(context, overlay, "other", swapped[0], out _));
        object replacement = new object();
        Check(lease.TryUpdate(context, replacement, "encounter", new[] { b }, new[] { a }, out var replaced, out _));
        Check(replaced[0] != swapped[0]);
        Check(!lease.TryResolve(context, replacement, "encounter", swapped[0], out _));
        lease.Clear();
        Check(!lease.TryResolve(context, replacement, "encounter", replaced[0], out _));
        Check(lease.TryUpdate(context, replacement, "encounter", new[] { b }, new[] { a }, out var reopened, out _));
        Check(reopened[0] != replaced[0]);
        object nextContext = new object();
        Check(lease.TryUpdate(nextContext, replacement, "encounter", new[] { b }, new[] { a }, out var contextChanged, out _));
        Check(contextChanged[0] != reopened[0]);
        Check(!lease.TryResolve(nextContext, replacement, "encounter", reopened[0], out _));
        Check(lease.TryUpdate(nextContext, replacement, "siege", new[] { b }, new[] { a }, out var menuChanged, out _));
        Check(menuChanged[0] != contextChanged[0]);
        Check(!lease.TryResolve(nextContext, replacement, "siege", contextChanged[0], out _));
        Check(lease.TryUpdate(context, replacement, "encounter", new[] { b }, new[] { a }, out reopened, out _));
        Check(!lease.TryUpdate(context, replacement, "encounter", new object[] { null }, new[] { a }, out _, out _));
        Check(!lease.TryResolve(context, replacement, "encounter", reopened[0], out _));
        Check(!lease.TryUpdate(context, replacement, "", new[] { a }, new[] { b }, out _, out _));
        Check(!lease.TryUpdate(context, replacement, "encounter", new object[257], new[] { b }, out _, out _));
        Check(lease.TryUpdate(context, replacement, "encounter", Array.Empty<object>(), Array.Empty<object>(), out var empty, out var otherEmpty));
        Check(empty.Length == 0 && otherEmpty.Length == 0);
        var menu = new CoopCampaignMapMenuPresentation { MenuId = "battle", IsEncounterMenu = true,
            Encounter = new CoopCampaignMapEncounterPresentation() };
        menu.Encounter.Attackers.Add(new CoopCampaignMapEncounterParty { TooltipId = token });
        menu.Encounter.Defenders.Add(new CoopCampaignMapEncounterParty { TooltipId = defender });
        Check(menu.IsValid() && menu.Clone().Encounter.Attackers[0].TooltipId == token);
        string session = Guid.NewGuid().ToString("N");
        var request = new CoopCampaignMapTooltipRequest(session, Guid.NewGuid(), token, 1, false);
        Check(CoopCampaignMapTooltipTargetAdmission.IsEncounterAdmitted(session, request, menu));
        Check(!CoopCampaignMapTooltipTargetAdmission.IsEncounterAdmitted(Guid.NewGuid().ToString("N"), request, menu));
        Check(!CoopCampaignMapTooltipTargetAdmission.IsEncounterAdmitted(session, request, null));
        Check(!CoopCampaignMapTooltipTargetAdmission.IsEncounterAdmitted(session,
            new CoopCampaignMapTooltipRequest(session, Guid.Empty, token, 1, false), menu));
        Check(!CoopCampaignMapTooltipTargetAdmission.ContainsEncounter(menu, "party:arbitrary"));
        menu.IsEncounterMenu = false;
        Check(!CoopCampaignMapTooltipTargetAdmission.IsEncounterAdmitted(session, request, menu));
        menu.IsEncounterMenu = true;
        Check(CoopCampaignMapMenuCodec.TrySerialize(1, menu, out var bytes));
        Check(CoopCampaignMapMenuCodec.TryDeserialize(bytes, out _, out var decoded) &&
            decoded.Encounter.Attackers[0].TooltipId == token && decoded.Encounter.Defenders[0].TooltipId == defender);
        var old = (byte[])bytes.Clone(); old[0] = 0x35;
        Check(!CoopCampaignMapMenuCodec.TryDeserialize(old, out _, out _));
        menu.Encounter.Defenders[0].TooltipId = token;
        Check(!CoopCampaignMapTooltipTargetAdmission.IsEncounterAdmitted(session, request, menu));
        Check(!menu.IsValid() && !CoopCampaignMapMenuCodec.TrySerialize(1, menu, out _));
        menu.Encounter.Defenders[0].TooltipId = defender;
        foreach (var invalid in new[] { (string)null, "encounter:", "party:abc", "encounter:" + new string('A', 32), token + "0" })
        {
            menu.Encounter.Attackers[0].TooltipId = invalid;
            Check(!menu.IsValid() && !CoopCampaignMapMenuCodec.TrySerialize(1, menu, out _));
        }
        menu.Encounter.Attackers[0].TooltipId = "";
        Check(menu.IsValid());
        Check(!CoopCampaignMapTooltipTargetAdmission.IsEncounterAdmitted(session, request, menu));
        Console.WriteLine("Encounter identity contracts passed.");
    }
    private static void Check(bool condition)
    { if (!condition) throw new Exception("Encounter identity contract failed"); }
}
