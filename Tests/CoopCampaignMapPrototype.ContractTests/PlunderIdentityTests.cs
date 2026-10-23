using System;
using CoopSpectator.Infrastructure;

internal static class PlunderIdentityTests
{
    internal static void Run()
    {
        var lease = new CoopCampaignMapPlunderIdentity<string>();
        object owner = new object();
        Check(lease.TryUpdate(owner, "raid", new[] { "grain", "sword:rusty", "grain" }, out var first));
        Check(first[0] != first[2]);
        for (int tick = 0; tick < 100; tick++)
        {
            Check(lease.TryUpdate(owner, "raid", new[] { "grain", "sword:rusty", "grain" }, out var next));
            Check(next[0] == first[0] && next[1] == first[1] && next[2] == first[2]);
        }
        string original = first[0]; first[0] = "tampered";
        Check(lease.TryResolve(owner, "raid", original, out var item) && item == "grain");
        Check(!lease.TryResolve(owner, "raid", "tampered", out _));
        Check(lease.TryUpdate(owner, "raid", new[] { "grain", "sword:fine" }, out var modified));
        Check(modified[0] == original && modified[1] != first[1]);
        Check(!lease.TryResolve(owner, "raid", first[1], out _));
        Check(!lease.TryResolve(owner, "raid", first[2], out _));
        Check(lease.TryUpdate(owner, "raid", new[] { "sword:fine", "grain" }, out var reordered));
        Check(!lease.TryResolve(owner, "raid", original, out _));
        Check(!lease.TryResolve(new object(), "raid", reordered[0], out _));
        Check(!lease.TryResolve(owner, "other", reordered[0], out _));
        lease.Clear();
        Check(!lease.TryResolve(owner, "raid", reordered[0], out _));
        Check(lease.TryUpdate(owner, "raid", new[] { "sword:fine", "grain" }, out var reopened));
        Check(reopened[0] != reordered[0]);
        Check(!lease.TryUpdate(null, "raid", new[] { "grain" }, out _));
        Check(!lease.TryResolve(owner, "raid", reopened[0], out _));
        Check(!lease.TryUpdate(owner, "raid", new string[257], out _));
        Check(lease.TryUpdate(owner, "raid", Array.Empty<string>(), out var empty) && empty.Length == 0);
        var supplied = new[] { "grain" };
        Check(lease.TryUpdate(owner, "raid", supplied, out var beforeOwnerChange));
        supplied[0] = "fish";
        Check(lease.TryResolve(owner, "raid", beforeOwnerChange[0], out var retained) && retained == "grain");
        object replacementOwner = new object();
        Check(lease.TryUpdate(replacementOwner, "raid", new[] { "grain" }, out var afterOwnerChange));
        Check(afterOwnerChange[0] != beforeOwnerChange[0]);
        Check(!lease.TryResolve(replacementOwner, "raid", beforeOwnerChange[0], out _));
        Check(!lease.TryResolve(owner, "raid", afterOwnerChange[0], out _));
        Check(lease.TryUpdate(replacementOwner, "village", new[] { "grain" }, out var changedMenu));
        Check(changedMenu[0] != afterOwnerChange[0]);
        Check(!lease.TryResolve(replacementOwner, "village", afterOwnerChange[0], out _));
        Check(!lease.TryUpdate(replacementOwner, "", new[] { "grain" }, out _));
        Check(!lease.TryResolve(replacementOwner, "village", changedMenu[0], out _));
        var menu = new CoopCampaignMapMenuPresentation { MenuId = "raid" };
        menu.PlunderItems.Add(new CoopCampaignMapMenuPlunder { ItemId = "grain", Amount = 4, TooltipId = original });
        Check(menu.TrySnapshot(out var snapshot) && snapshot.PlunderItems[0].TooltipId == original);
        string session = Guid.NewGuid().ToString("N");
        var request = new CoopCampaignMapTooltipRequest(session, Guid.NewGuid(), original, 1, false);
        Check(CoopCampaignMapTooltipTargetAdmission.IsPlunderAdmitted(session, request, menu));
        Check(!CoopCampaignMapTooltipTargetAdmission.IsPlunderAdmitted(Guid.NewGuid().ToString("N"), request, menu));
        Check(!CoopCampaignMapTooltipTargetAdmission.IsPlunderAdmitted(session, request, null));
        Check(!CoopCampaignMapTooltipTargetAdmission.IsPlunderAdmitted(session,
            new CoopCampaignMapTooltipRequest(session, Guid.Empty, original, 1, false), menu));
        Check(!CoopCampaignMapTooltipTargetAdmission.ContainsPlunder(menu, "grain"));
        Check(CoopCampaignMapMenuCodec.TrySerialize(1, menu, out var bytes));
        Check(CoopCampaignMapMenuCodec.TryDeserialize(bytes, out _, out var decoded) &&
            decoded.PlunderItems[0].TooltipId == original);
        var old = (byte[])bytes.Clone(); old[0] = 0x34;
        Check(!CoopCampaignMapMenuCodec.TryDeserialize(old, out _, out _));
        foreach (var invalid in new[] { (string)null, "plunder:", "party:grain", "plunder:" + new string('g', 32), original + "x" })
        {
            menu.PlunderItems[0].TooltipId = invalid;
            Check(!menu.IsValid() && !CoopCampaignMapMenuCodec.TrySerialize(1, menu, out _));
        }
        menu.PlunderItems[0].TooltipId = original;
        menu.PlunderItems.Add(menu.PlunderItems[0].Clone());
        Check(!menu.IsValid());
        Check(!CoopCampaignMapTooltipTargetAdmission.IsPlunderAdmitted(session, request, menu));
        menu.PlunderItems[0].TooltipId = ""; menu.PlunderItems[1].TooltipId = "";
        Check(menu.IsValid());
        Console.WriteLine("Plunder identity contracts passed.");
    }
    private static void Check(bool value)
    { if (!value) throw new Exception("Plunder identity contract failed"); }
}
