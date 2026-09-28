using System;
using CoopSpectator.Infrastructure;

internal static class MissionOpeningTests
{
    public static void Run()
    {
        var expected = new object();
        var capture = new CoopCampaignMissionOpening<object>(7);
        capture.Observe(expected, 7);
        if (!ReferenceEquals(expected, capture.Complete(7))) throw new Exception("Exact native return lost.");
        capture.Observe(new object(), 7); // Late observation cannot replace the closed result.
        Reject(() => capture.Complete(7));
        Reject(() => new CoopCampaignMissionOpening<object>(7).Complete(7));
        capture = new CoopCampaignMissionOpening<object>(7);
        capture.Observe(expected, 7);
        capture.Observe(expected, 7); // Even duplicate same-object callbacks are ambiguous.
        Reject(() => capture.Complete(7));
        capture = new CoopCampaignMissionOpening<object>(7);
        capture.Observe(null, 7);
        capture.Observe(expected, 7);
        Reject(() => capture.Complete(7));
        capture = new CoopCampaignMissionOpening<object>(7);
        capture.Observe(expected, 8);
        Reject(() => capture.Complete(7));
        capture = new CoopCampaignMissionOpening<object>(7);
        capture.Observe(expected, 7);
        Reject(() => capture.Complete(8));
        // A fresh dispatch cannot inherit any previous capture.
        Reject(() => new CoopCampaignMissionOpening<object>(7).Complete(7));
        Console.WriteLine("Mission opening capture: 8 assertions passed.");
    }

    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("Ambiguous native opening accepted.");
    }
}
