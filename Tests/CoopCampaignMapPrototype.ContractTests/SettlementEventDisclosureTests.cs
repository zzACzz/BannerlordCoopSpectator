using System;
using System.IO;
using CoopSpectator.Infrastructure;

internal static class SettlementEventDisclosureTests
{
    private static int _count;
    internal static void Run()
    {
        for (int flags = 0; flags < 256; flags++)
        {
            Check(CoopCampaignMapSettlementEventPolicy.FilterEvents(flags, true) == (flags & 127), "inspected event mask");
            Check(CoopCampaignMapSettlementEventPolicy.FilterEvents(flags, false) == (flags & 64), "uninspected production only");
            Check(CoopCampaignMapSettlementEventPolicy.FilterQuests(flags, true) == (flags & 31), "inspected quest mask");
            Check(CoopCampaignMapSettlementEventPolicy.FilterQuests(flags, false) == 0, "uninspected quest nondisclosure");
        }
        Check(CoopCampaignMapSettlementEventPolicy.FilterEvents(66, false) == 64, "captured Ethemisa issue must not disclose");
        Check(CoopCampaignMapSettlementEventPolicy.FilterEvents(66, true) == 66, "inspection reveals available issue");
        Check(CoopCampaignMapSettlementEventPolicy.FilterEvents(66, false) == 64, "range loss clears issue again");
        Check(CoopCampaignMapSettlementEventPolicy.FilterEvents(0, true) == 0, "no fabricated event");
        Check(CoopCampaignMapSettlementEventPolicy.FilterEvents(-1, false) == 64, "unknown bits cannot pass range gate");
        string publisher = File.ReadAllText(Path.Combine("Campaign", "CoopCampaignMapPrototypePublisherBehavior.cs"));
        string overlay = File.ReadAllText(Path.Combine("UI", "CoopCampaignMapPrototypeSettlementOverlayVM.cs"));
        Check(publisher.Replace("\r\n", "\n").Contains("CoopCampaignMapSettlementEventPolicy.FilterEvents(\n                    1 << item.Type, information.IsSettlementInRange)"), "host filters before ordered publication");
        Check(publisher.Contains("if (information.IsSettlementInRange)") &&
              publisher.Contains("questFlags.Add((int)quest.IssueQuestFlag)"), "host gates native cached quests");
        Check(overlay.Contains("entity.SettlementQuestOrder, entity.IsSettlementInRange") &&
              overlay.Contains("eventOrder, eventFlags, inRange") &&
              overlay.Contains("questOrder, questMarkerFlags, inRange"), "client decoder uses host range, not local distance");
        Console.WriteLine("Settlement event disclosure assertions passed: " + _count);
    }
    private static void Check(bool condition, string message)
    {
        _count++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
