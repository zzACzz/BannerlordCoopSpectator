using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure;

internal static class SettlementMarkerOrderTests
{
    private static int _checks;
    private static int _eventOrders;
    private static int _questOrders;

    internal static void Run()
    {
        Visit(new List<int>(), 7, false);
        Visit(new List<int>(), 5, true);
        Check(_eventOrders == 13700, "every ordered subset of seven event types");
        Check(_questOrders == 326, "every ordered subset of five quest flags");
        Check(CoopCampaignMapSettlementMarkerOrder.MaxEventOrder == 2097151, "event wire ceiling");
        Check(CoopCampaignMapSettlementMarkerOrder.MaxQuestOrder == 32767, "quest wire ceiling");

        Check(CoopCampaignMapSettlementMarkerOrder.TryEncodeEvents(new[] { 6, 1 },
            out int productionFirst, out int eventFlags) && productionFirst == 23 && eventFlags == 66,
            "native village constructor production precedes newly available issue");
        Check(CoopCampaignMapSettlementMarkerOrder.TryEncodeEvents(new[] { 1, 6 },
            out int issueFirst, out int reorderedFlags) && issueFirst == 58 && reorderedFlags == eventFlags,
            "same flags preserve different source order");
        Check(CoopCampaignMapSettlementMarkerOrder.TryEncodeEvents(new[] { 1, 0 },
            out int lateTournament, out _) && lateTournament == 10,
            "a newly started tournament is appended, not numerically sorted");
        Check(CoopCampaignMapSettlementMarkerOrder.TryEncodeQuests(new[] { 16, 8 },
            out int storyFirst, out int questFlags) && storyFirst == 37 && questFlags == 24,
            "native special-before-ordinary tracked quest order");
        Check(CoopCampaignMapSettlementMarkerOrder.TryEncodeQuests(new[] { 8, 16 },
            out int ordinaryFirst, out int sameQuestFlags) && ordinaryFirst == 44 && sameQuestFlags == questFlags,
            "reverse tracked quest order remains distinct");

        foreach (int[] invalid in new[] { new[] { -1 }, new[] { 7 }, new[] { 1, 1 },
                     new[] { 0, 1, 2, 3, 4, 5, 6, 0 }, new[] { int.MaxValue } })
        {
            Check(!CoopCampaignMapSettlementMarkerOrder.TryEncodeEvents(invalid, out int order, out int flags)
                && order == 0 && flags == 0, "invalid event capture leaves no partial output");
        }
        foreach (int[] invalid in new[] { new[] { -1 }, new[] { 0 }, new[] { 3 }, new[] { 32 },
                     new[] { 8, 8 }, new[] { 1, 2, 4, 8, 16, 1 } })
        {
            Check(!CoopCampaignMapSettlementMarkerOrder.TryEncodeQuests(invalid, out int order, out int flags)
                && order == 0 && flags == 0, "invalid quest capture leaves no partial output");
        }
        Check(!CoopCampaignMapSettlementMarkerOrder.TryEncodeEvents(null, out _, out _), "missing event source rejects");
        Check(!CoopCampaignMapSettlementMarkerOrder.TryEncodeQuests(null, out _, out _), "missing quest source rejects");

        RejectEvents(-1, 127);
        RejectEvents(1 << 21, 0);
        RejectEvents(int.MaxValue, 127);
        RejectEvents(1, -1);
        RejectEvents(1, 128);
        RejectEvents(0, 1);
        RejectEvents(1, 0);
        RejectEvents(8, 1); // Leading hole.
        RejectEvents(65, 1); // Interior hole followed by a duplicate.
        RejectEvents(9, 1); // Adjacent duplicate.
        RejectEvents(23, 2); // Production in the order but absent from the mask.
        RejectQuests(-1, 31);
        RejectQuests(1 << 15, 0);
        RejectQuests(6, 0); // Unknown type digit.
        RejectQuests(7, 0);
        RejectQuests(8, 1);
        RejectQuests(45, 16); // Duplicate story quest.
        RejectQuests(37, 16);
        RejectQuests(0, 32);

        Console.WriteLine("Settlement marker order assertions passed: " + _checks);
    }

    private static void Visit(List<int> prefix, int count, bool quests)
    {
        int[] expected = prefix.Select(type => quests ? 1 << type : type).ToArray();
        bool encoded = quests
            ? CoopCampaignMapSettlementMarkerOrder.TryEncodeQuests(expected, out int order, out int flags)
            : CoopCampaignMapSettlementMarkerOrder.TryEncodeEvents(expected, out order, out flags);
        Check(encoded, "every ordered subset encodes");
        Check(flags == prefix.Aggregate(0, (mask, type) => mask | (1 << type)), "independent flag oracle");
        int oracle = 0;
        for (int i = 0; i < prefix.Count; i++) oracle += (prefix[i] + 1) * (1 << (3 * i));
        Check(order == oracle, "independent base-eight order oracle");
        Check(quests ? CoopCampaignMapSettlementMarkerOrder.IsValidQuests(order, flags)
            : CoopCampaignMapSettlementMarkerOrder.IsValidEvents(order, flags), "canonical encoding validates");
        foreach (bool inRange in new[] { false, true })
        {
            bool decoded = quests
                ? CoopCampaignMapSettlementMarkerOrder.TryDecodeQuests(order, flags, inRange, out int[] actual)
                : CoopCampaignMapSettlementMarkerOrder.TryDecodeEvents(order, flags, inRange, out actual);
            Check(decoded, "admitted source decodes");
            int[] filtered = inRange ? expected : quests ? Array.Empty<int>() : expected.Where(type => type == 6).ToArray();
            Check(actual.SequenceEqual(filtered), "order retained while host range filters disclosure");
        }
        // A repeated refresh cannot mutate the caller's detached list.
        Check(expected.SequenceEqual(prefix.Select(type => quests ? 1 << type : type)), "caller list unmodified");
        if (quests) _questOrders++; else _eventOrders++;
        for (int next = 0; next < count; next++)
        {
            if (prefix.Contains(next)) continue;
            prefix.Add(next);
            Visit(prefix, count, quests);
            prefix.RemoveAt(prefix.Count - 1);
        }
    }

    private static void RejectEvents(int order, int flags)
    {
        Check(!CoopCampaignMapSettlementMarkerOrder.IsValidEvents(order, flags), "invalid event encoding rejects");
        foreach (bool inRange in new[] { false, true })
            Check(!CoopCampaignMapSettlementMarkerOrder.TryDecodeEvents(order, flags, inRange, out int[] values)
                && values.Length == 0, "invalid events never disclose a partial list");
    }

    private static void RejectQuests(int order, int flags)
    {
        Check(!CoopCampaignMapSettlementMarkerOrder.IsValidQuests(order, flags), "invalid quest encoding rejects");
        foreach (bool inRange in new[] { false, true })
            Check(!CoopCampaignMapSettlementMarkerOrder.TryDecodeQuests(order, flags, inRange, out int[] values)
                && values.Length == 0, "invalid quests never disclose a partial list");
    }

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
