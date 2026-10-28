using System;
using CoopSpectator.Infrastructure;

internal static class MissionLifecycleTests
{
    private const string Session = "c6176114bd734e3ba3969582f39fe821";
    private static int _count;
    private static CoopCampaignMissionIntent Map(long revision = 1) => new CoopCampaignMissionIntent
    { Session = Session, Revision = revision, Fresh = true, Kind = CoopCampaignMissionIntentKind.Map, HostOnGlobalMap = true };
    private static CoopCampaignMissionIntent Battle(string key, long revision = 2) => new CoopCampaignMissionIntent
    { Session = Session, Revision = revision, Fresh = true, Kind = CoopCampaignMissionIntentKind.Battle, BattleKey = key };
    private static CoopCampaignMissionObservation Idle(long seq) => new CoopCampaignMissionObservation
    { Session = Session, Sequence = seq, Fresh = true, Kind = CoopCampaignObservedMissionKind.Intermission, CanStart = true };
    private static CoopCampaignMissionObservation Active(long seq, CoopCampaignObservedMissionKind kind,
        string instance, long origin, string key = null) => new CoopCampaignMissionObservation
    { Session = Session, Sequence = seq, Fresh = true, Kind = kind, MissionInstance = instance, OriginCommand = origin,
        BattleKey = key, CanEnd = true };
    private static void Is(CoopCampaignMissionDecision decision, CoopCampaignMissionAction action, string label)
    { Check(decision.Action == action, label + ": " + decision.Action + " / " + decision.Reason); }
    private static void Check(bool ok, string label)
    { _count++; if (!ok) throw new InvalidOperationException("Mission lifecycle: " + label); }

    internal static void Run()
    {
        NativeEndedClose();
        // Every supported scenario uses exact battle identity, never a map-return
        // special case. These pure tests do not claim native scenario coverage.
        foreach (string scenario in new[] { "field", "village", "siege", "sally-out", "siege-ambush",
            "relief", "lords-hall", "hideout-day", "hideout-night" })
            RoundTrip(scenario);

        var m = new CoopCampaignMissionLifecycle(Session, 30);
        var idle = Idle(1); idle.CanStart = false;
        Is(m.Step(Map(), idle, 0), CoopCampaignMissionAction.Wait, "server not command-ready");
        var stale = Idle(2); stale.Session = "previous-server";
        Is(m.Step(Map(), stale, 1), CoopCampaignMissionAction.Wait, "old process pair");
        stale = Idle(2); stale.Fresh = false;
        Is(m.Step(Map(), stale, 2), CoopCampaignMissionAction.Wait, "stale native observation");
        var host = Map(); host.Fresh = false;
        Is(m.Step(host, Idle(2), 3), CoopCampaignMissionAction.Wait, "stale host");
        var other = Active(3, CoopCampaignObservedMissionKind.Other, "hero-creator", 0);
        Is(m.Step(Map(), other, 4), CoopCampaignMissionAction.Wait, "never close foreign mission");
        var start = m.Step(Map(), Idle(4), 5);
        Is(start, CoopCampaignMissionAction.StartMap, "eventual startup");
        Is(m.Step(Map(), Idle(4), 6), CoopCampaignMissionAction.Wait, "same observation cannot repeat command");
        Is(m.Step(Map(), Idle(5), 7), CoopCampaignMissionAction.Wait, "HTTP acceptance is not mission open");
        Is(m.Step(Map(), Idle(6), 35), CoopCampaignMissionAction.Fault, "bounded pending transition");
        Is(m.Step(Map(), Idle(7), 36), CoopCampaignMissionAction.Fault, "no automatic restart loop");

        m = new CoopCampaignMissionLifecycle(Session, 30);
        start = m.Step(Map(), Idle(1), 0);
        Is(m.Step(Map(), Active(2, CoopCampaignObservedMissionKind.Map, "foreign", start.Command + 1), 1),
            CoopCampaignMissionAction.Fault, "native opening must match emitted command");

        m = new CoopCampaignMissionLifecycle(Session, 30);
        start = m.Step(Map(), Idle(1), 0);
        idle = Idle(2); idle.RejectedCommand = start.Command;
        Is(m.Step(Map(), idle, 1), CoopCampaignMissionAction.Fault, "explicit command rejection");

        m = new CoopCampaignMissionLifecycle(Session, 30);
        start = m.Step(Map(), Idle(1), 0);
        host = Map(); host.HostOnGlobalMap = false;
        Is(m.Step(host, Idle(2), 1), CoopCampaignMissionAction.Fault, "revision content immutable");

        m = new CoopCampaignMissionLifecycle(Session, 30);
        start = m.Step(Map(5), Idle(1), 0);
        Is(m.Step(Battle("old", 4), Active(2, CoopCampaignObservedMissionKind.Map, "map", start.Command), 1),
            CoopCampaignMissionAction.Wait, "late host revision cannot close map");
        host = Battle("next", 6); host.Fresh = false;
        Is(m.Step(host, Active(3, CoopCampaignObservedMissionKind.Map, "map", start.Command), 2),
            CoopCampaignMissionAction.Wait, "fresh observation cannot revive stale intent");
        Is(m.Step(Map(5), Idle(4), 3), CoopCampaignMissionAction.Fault, "unexpected map disappearance");

        m = new CoopCampaignMissionLifecycle(Session, 30);
        start = m.Step(Battle("battle", 1), Idle(1), 0);
        Is(m.Step(Battle("battle", 1), Active(2, CoopCampaignObservedMissionKind.Battle, "battle-1", start.Command, "battle"), 1),
            CoopCampaignMissionAction.Wait, "battle started directly from intermission");
        Is(m.Step(Map(2), Idle(3), 2), CoopCampaignMissionAction.Fault, "mission disappearance is not authoritative completion");

        m = new CoopCampaignMissionLifecycle(Session, 30);
        start = m.Step(Battle("stage-1", 1), Idle(1), 0);
        m.Step(Battle("stage-1", 1), Active(2, CoopCampaignObservedMissionKind.Battle, "stage-1", start.Command, "stage-1"), 1);
        idle = Idle(3); idle.CompletedBattleKey = "stage-1";
        Is(m.Step(Battle("stage-2", 2), idle, 2), CoopCampaignMissionAction.Wait, "next stage waits for previous aftermath");
        host = Battle("stage-2", 3); host.AftermathApplied = true; host.AppliedBattleKey = "stage-1";
        Is(m.Step(host, Idle(4), 3), CoopCampaignMissionAction.StartBattle, "next stage after applied prior stage");

        m = new CoopCampaignMissionLifecycle(Session, 30);
        Is(m.Step(Map(), Idle(1), double.NaN), CoopCampaignMissionAction.Fault, "invalid clock");
        m = new CoopCampaignMissionLifecycle(Session, 30);
        m.Step(Map(), Idle(1), 3);
        Is(m.Step(Map(), Idle(2), 2), CoopCampaignMissionAction.Fault, "clock cannot regress");

        // Battle intent can supersede map intent while map opening is in flight,
        // but no end command is emitted until that exact map actually exists.
        m = new CoopCampaignMissionLifecycle(Session, 30);
        start = m.Step(Map(), Idle(1), 0);
        Is(m.Step(Battle("rush"), Idle(2), 1), CoopCampaignMissionAction.Wait, "battle waits for opening map");
        Is(m.Step(Battle("rush"), Active(3, CoopCampaignObservedMissionKind.Map, "map", start.Command), 2),
            CoopCampaignMissionAction.EndMap, "battle priority after exact map open");
        m = new CoopCampaignMissionLifecycle(Session, 30);
        Is(m.Step(Battle(new string('x', 257)), Idle(1), 0), CoopCampaignMissionAction.Fault, "bounded battle identity");
        m = new CoopCampaignMissionLifecycle(Session, 30);
        var notMap = Map(); notMap.HostOnGlobalMap = false;
        Is(m.Step(notMap, Idle(1), 0), CoopCampaignMissionAction.Wait, "no map before actual host map");
        CapacityAndReplay();
        AppliedResultClose();
        QueuedStageDuringClose();
        Console.WriteLine("Campaign mission lifecycle assertions passed: " + _count);
    }

    private static void AppliedResultClose()
    {
        foreach (string invalid in new[] { "replaced", "wrong-completion", "timeout" })
        {
            var guarded = new CoopCampaignMissionLifecycle(Session, 30);
            var initial = Battle("guarded", 1);
            var opening = guarded.Step(initial, Idle(1), 0);
            var active = Active(2, CoopCampaignObservedMissionKind.Battle, "exact", opening.Command, "guarded");
            active.CompletedBattleKey = "guarded";
            var applied = Map(2); applied.AftermathApplied = true; applied.AppliedBattleKey = "guarded";
            Is(guarded.Step(applied, active, 1), CoopCampaignMissionAction.EndBattle, "guarded close admitted");
            active.Sequence++;
            if (invalid == "replaced") active.MissionInstance = "replacement";
            var after = invalid == "wrong-completion" ? Idle(3) : active;
            if (invalid == "wrong-completion") after.CompletedBattleKey = "unrelated";
            Is(guarded.Step(applied, after, invalid == "timeout" ? 31 : 2), CoopCampaignMissionAction.Fault,
                "close fails closed on " + invalid);
        }
        foreach (bool followup in new[] { false, true })
        {
            var m = new CoopCampaignMissionLifecycle(Session, 30);
            var host = Battle("result-bound", 1);
            var start = m.Step(host, Idle(1), 0);
            var observed = Active(2, CoopCampaignObservedMissionKind.Battle, "native", start.Command, host.BattleKey);
            observed.CompletedBattleKey = host.BattleKey;
            Is(m.Step(host, observed, 1), CoopCampaignMissionAction.Wait, "publication without application cannot close");
            host = Map(2); host.AftermathApplied = true; host.AppliedBattleKey = "wrong";
            observed.Sequence++;
            Is(m.Step(host, observed, 100), CoopCampaignMissionAction.Wait, "elapsed time and unrelated result cannot close");
            host = Map(3); host.AftermathApplied = true; host.AppliedBattleKey = "result-bound";
            host.FollowupStagePending = followup; host.Fresh = false;
            observed.Sequence++;
            Is(m.Step(host, observed, 101), CoopCampaignMissionAction.Wait, "stale acknowledgement cannot close");
            host.Fresh = true; observed.Sequence++; observed.CanEnd = false;
            Is(m.Step(host, observed, 102), CoopCampaignMissionAction.Wait, "native end readiness required");
            observed.Sequence++; observed.CanEnd = true;
            var close = m.Step(host, observed, 103);
            Is(close, CoopCampaignMissionAction.EndBattle, "matching published and applied result closes immediately");
            Check(close.MissionInstance == "native" && close.BattleKey == "result-bound", "close binds exact battle and mission");
            observed.Sequence++;
            Is(m.Step(host, observed, 104), CoopCampaignMissionAction.Wait, "close command not repeated");
            var idle = Idle(++observed.Sequence); idle.CompletedBattleKey = "result-bound";
            Is(m.Step(host, idle, 105), followup ? CoopCampaignMissionAction.Wait : CoopCampaignMissionAction.StartMap,
                "native teardown required and followup still blocks map");
        }
    }

    private static void NativeEndedClose()
    {
        Check(CoopCampaignMissionClosePolicy.Select(true, true, true, true, false, true, false) ==
            CoopCampaignMissionCloseRoute.ListedManager, "unfinished combat retains listed-manager close");
        Check(CoopCampaignMissionClosePolicy.Select(true, true, true, true, true, true, false) ==
            CoopCampaignMissionCloseRoute.EndedMissionLobby, "native-ended combat still has a network close route");
        foreach (int missing in new[] { 0, 1, 2, 3, 4, 5 })
            Check(CoopCampaignMissionClosePolicy.Select(missing != 0, missing != 1, missing != 2, missing != 3,
                true, missing != 4, missing == 5) == CoopCampaignMissionCloseRoute.Unavailable,
                "native-ended close rejects missing readiness/owner/lobby/continuation or existing Ending " + missing);
        foreach (string scenario in new[] { "field", "village", "siege", "sally-out", "siege-ambush", "relief", "lords-hall", "hideout-day", "hideout-night" })
        {
            var lifecycle = new CoopCampaignMissionLifecycle(Session, 30);
            var battle = Battle("native-ended-" + scenario, 1);
            var start = lifecycle.Step(battle, Idle(1), 0);
            var observed = Active(2, CoopCampaignObservedMissionKind.Battle, "owned-native", start.Command, battle.BattleKey);
            observed.CanEnd = CoopCampaignMissionClosePolicy.Select(true, true, true, true, true, true, false) !=
                CoopCampaignMissionCloseRoute.Unavailable;
            observed.CompletedBattleKey = battle.BattleKey;
            Is(lifecycle.Step(battle, observed, 1), CoopCampaignMissionAction.Wait,
                "native-ended publication alone never authorizes " + scenario);
            var applied = Map(2); applied.AftermathApplied = true; applied.AppliedBattleKey = battle.BattleKey;
            observed.Sequence++;
            Is(lifecycle.Step(applied, observed, 2), CoopCampaignMissionAction.EndBattle,
                "native-ended exact applied result closes " + scenario);
            observed.Sequence++;
            Is(lifecycle.Step(applied, observed, 3), CoopCampaignMissionAction.Wait,
                "native-ended close is one-shot " + scenario);
            var idle = Idle(5); idle.CompletedBattleKey = battle.BattleKey;
            Is(lifecycle.Step(applied, idle, 4), CoopCampaignMissionAction.StartMap,
                "native teardown then returns map " + scenario);
        }
    }

    private static void QueuedStageDuringClose()
    {
        // Host preparation may overtake native teardown in multi-stage battles.
        // This checks ordering only, not native siege/hideout acceptance.
        foreach (bool queueBeforeClose in new[] { true, false })
        {
            var m = new CoopCampaignMissionLifecycle(Session, 30);
            var first = Battle("previous-stage", 1);
            var opening = m.Step(first, Idle(1), 0);
            var active = Active(2, CoopCampaignObservedMissionKind.Battle, "previous-native",
                opening.Command, first.BattleKey);
            active.CompletedBattleKey = first.BattleKey;
            var next = Battle("next-stage", 3);
            next.AftermathApplied = true; next.AppliedBattleKey = first.BattleKey;
            next.FollowupStagePending = true;
            var hold = Map(2); hold.Kind = CoopCampaignMissionIntentKind.Hold;
            hold.AftermathApplied = true; hold.AppliedBattleKey = first.BattleKey;
            hold.FollowupStagePending = true;
            var close = m.Step(queueBeforeClose ? next : hold, active, 1);
            Is(close, CoopCampaignMissionAction.EndBattle, "queued stage closes only acknowledged predecessor");
            Check(close.BattleKey == first.BattleKey && close.MissionInstance == "previous-native",
                "next-stage intent cannot retarget predecessor close");
            active.Sequence++;
            Is(m.Step(next, active, 2), CoopCampaignMissionAction.Wait, "queued stage waits for actual teardown");
            var idle = Idle(4); idle.CompletedBattleKey = first.BattleKey; idle.CanStart = false;
            Is(m.Step(next, idle, 3), CoopCampaignMissionAction.Wait, "teardown alone is not start readiness");
            idle.Sequence++; idle.CanStart = true; next.Fresh = false;
            Is(m.Step(next, idle, 4), CoopCampaignMissionAction.Wait, "cached queued stage needs fresh authorization");
            idle.Sequence++; next.Fresh = true;
            var start = m.Step(next, idle, 5);
            Is(start, CoopCampaignMissionAction.StartBattle, "ready next stage starts without intervening map");
            Check(start.BattleKey == next.BattleKey && start.Command > close.Command,
                "next stage preserves identity and command ordering");
            idle.Sequence++;
            Is(m.Step(next, idle, 6), CoopCampaignMissionAction.Wait, "next-stage dispatch remains one-shot");
        }
    }

    private static void CapacityAndReplay()
    {
        var m = new CoopCampaignMissionLifecycle(Session, 30);
        long seq = 0;
        string previous = null;
        for (int i = 0; i < 4096; i++)
        {
            var host = Battle("battle-" + i, i + 1);
            host.AftermathApplied = previous != null; host.AppliedBattleKey = previous;
            var opened = m.Step(host, Idle(++seq), seq);
            Is(opened, CoopCampaignMissionAction.StartBattle, "bounded sequential battle history");
            m.Step(host, Active(++seq, CoopCampaignObservedMissionKind.Battle, "native-" + i, opened.Command, host.BattleKey), seq);
            var ended = Idle(++seq); ended.CompletedBattleKey = host.BattleKey;
            m.Step(host, ended, seq);
            previous = host.BattleKey;
        }
        var replay = Battle("battle-0", 4097);
        Is(m.Step(replay, Idle(++seq), seq), CoopCampaignMissionAction.Wait, "oldest identity was not evicted");
        var next = Battle("overflow", 4098); next.AftermathApplied = true; next.AppliedBattleKey = previous;
        var full = m.Step(next, Idle(++seq), seq);
        Is(full, CoopCampaignMissionAction.Fault, "bounded session capacity");
        Check(full.Reason == "session-battle-capacity-exhausted", "capacity failure is explicit");
    }

    private static void RoundTrip(string scenario)
    {
        var m = new CoopCampaignMissionLifecycle(Session, 30);
        var map = Map();
        var startMap = m.Step(map, Idle(1), 0);
        Is(startMap, CoopCampaignMissionAction.StartMap, scenario + " initial map");
        Is(m.Step(map, Active(2, CoopCampaignObservedMissionKind.Map, "map-1", startMap.Command), 1),
            CoopCampaignMissionAction.Wait, "map ownership confirmed");
        var battle = Battle(scenario + ":instance-1:scene:roster-digest");
        var endMap = m.Step(battle, Active(3, CoopCampaignObservedMissionKind.Map, "map-1", startMap.Command), 2);
        Is(endMap, CoopCampaignMissionAction.EndMap, "battle replaces owned map");
        Check(endMap.MissionInstance == "map-1", "end command carries exact map identity");
        Is(m.Step(battle, Active(4, CoopCampaignObservedMissionKind.Map, "map-1", startMap.Command), 3),
            CoopCampaignMissionAction.Wait, "no repeated end command");
        var idle = Idle(5); idle.CanStart = false;
        Is(m.Step(battle, idle, 4), CoopCampaignMissionAction.Wait, "teardown is not intermission readiness");
        var startBattle = m.Step(battle, Idle(6), 5);
        Is(startBattle, CoopCampaignMissionAction.StartBattle, "start exact battle after teardown");
        Check(startBattle.BattleKey == battle.BattleKey && startBattle.Command > endMap.Command, "battle identity and monotonic commands");
        Is(m.Step(battle, Active(7, CoopCampaignObservedMissionKind.Battle, "battle-1", startBattle.Command, battle.BattleKey), 6),
            CoopCampaignMissionAction.Wait, "battle owns lifecycle");
        map = Map(3);
        Is(m.Step(map, Active(8, CoopCampaignObservedMissionKind.Battle, "battle-1", startBattle.Command, battle.BattleKey), 7),
            CoopCampaignMissionAction.Wait, "never close battle for map request");
        idle = Idle(9); idle.CompletedBattleKey = battle.BattleKey;
        Is(m.Step(map, idle, 8), CoopCampaignMissionAction.Wait, "await campaign aftermath");
        map = Map(4); map.AftermathApplied = true; map.AppliedBattleKey = "wrong";
        Is(m.Step(map, Idle(10), 9), CoopCampaignMissionAction.Wait, "wrong result cannot authorize return");
        map = Map(5); map.AftermathApplied = true; map.AppliedBattleKey = battle.BattleKey; map.FollowupStagePending = true;
        Is(m.Step(map, Idle(11), 10), CoopCampaignMissionAction.Wait, "next stage prevents map return");
        // A new validated stage is allowed while map return is inhibited.
        var secondBattle = Battle(scenario + ":instance-2:scene:roster-digest", 6);
        var secondStart = m.Step(secondBattle, Idle(12), 11);
        Is(secondStart, CoopCampaignMissionAction.StartBattle, "consecutive stage starts without intermediate map");
        Is(m.Step(secondBattle, Active(13, CoopCampaignObservedMissionKind.Battle, "battle-2", secondStart.Command, secondBattle.BattleKey), 12),
            CoopCampaignMissionAction.Wait, "second battle owns lifecycle");
        idle = Idle(14); idle.CompletedBattleKey = secondBattle.BattleKey;
        Is(m.Step(secondBattle, idle, 13), CoopCampaignMissionAction.Wait, "consumed battle not restarted");
        map = Map(7); map.AftermathApplied = true; map.AppliedBattleKey = secondBattle.BattleKey;
        var returnMap = m.Step(map, Idle(15), 14);
        Is(returnMap, CoopCampaignMissionAction.StartMap, "final aftermath authorizes map return");
        Is(m.Step(map, Active(16, CoopCampaignObservedMissionKind.Map, "map-2", returnMap.Command), 15),
            CoopCampaignMissionAction.Wait, "returned map remains active");
        secondBattle.Revision = 8;
        Is(m.Step(secondBattle, Active(17, CoopCampaignObservedMissionKind.Map, "map-2", returnMap.Command), 16),
            CoopCampaignMissionAction.Wait, "duplicate completed battle cannot tear down returned map");
        battle.Revision = 9;
        Is(m.Step(battle, Active(18, CoopCampaignObservedMissionKind.Map, "map-2", returnMap.Command), 17),
            CoopCampaignMissionAction.Wait, "earlier completed stage cannot be replayed either");
    }
}
