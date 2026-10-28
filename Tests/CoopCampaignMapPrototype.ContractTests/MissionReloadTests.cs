using System;
using System.IO;
using CoopSpectator.Infrastructure;

internal static class MissionReloadTests
{
    private const string First = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", Next = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static CoopCampaignReloadRequest Request(long ordinal = 1, string epoch = First, string campaign = First) =>
        new CoopCampaignReloadRequest { Ordinal = ordinal, Epoch = epoch, CampaignId = campaign };
    private static void Check(bool condition, string label) { if (!condition) throw new Exception("Mission reload: " + label); }
    internal static void Run()
    {
        ValidateWire();
        foreach (string scenario in new[] { "field", "village", "siege", "sally-out", "siege-ambush", "relief", "lords-hall", "hideout-day", "hideout-night" })
        {
            var reload = new CoopCampaignMissionReload();
            Check(reload.Request(Request()), scenario + " accepts an admitted reload");
            Check(!reload.Commit(Request()), scenario + " cannot acknowledge before native teardown");
            var close = reload.Step(CoopCampaignObservedMissionKind.Battle, scenario, true, true, false);
            Check(close.Action == CoopCampaignReloadAction.EndOwnedMission && close.MissionInstance == scenario, scenario + " cancels exact mission without old-result application");
            Check(reload.Step(CoopCampaignObservedMissionKind.Battle, scenario, true, true, false).Action == CoopCampaignReloadAction.Wait, scenario + " does not duplicate close");
            Check(!reload.Commit(close.Request), scenario + " dispatch alone cannot acknowledge teardown");
            // The user changes their mind again before native teardown completes.
            Check(reload.Request(Request(2, Next)), scenario + " newer save supersedes pending target");
            Check(!reload.Commit(close.Request), scenario + " obsolete acknowledgement cannot release new generation");
            Check(reload.Step(CoopCampaignObservedMissionKind.Battle, scenario, true, true, false).Action == CoopCampaignReloadAction.Wait, scenario + " supersession does not re-close");
            Check(reload.Step(CoopCampaignObservedMissionKind.Intermission, null, false, false, false).Action == CoopCampaignReloadAction.Wait, scenario + " actual readiness required instead of delay");
            var ack = reload.Step(CoopCampaignObservedMissionKind.Intermission, null, false, false, true);
            Check(ack.Action == CoopCampaignReloadAction.Acknowledge && ack.Request.Epoch == Next && ack.Request.CampaignId == First,
                scenario + " same-campaign rewind has a fresh generation");
            Check(reload.Commit(ack.Request) && !reload.IsPending, scenario + " cleared stores and exact acknowledgement complete reset");
            Check(!reload.Request(Request()), scenario + " late old request cannot revive previous battle");
            Check(reload.Request(Request(2, Next)) && !reload.IsPending, scenario + " completed request duplicate is idempotent");
            Check(reload.Request(Request(3, "cccccccccccccccccccccccccccccccc", Next)), scenario + " different campaign can reload next");
        }
        var map = new CoopCampaignMissionReload(); map.Request(Request());
        Check(map.Step(CoopCampaignObservedMissionKind.Map, "map", true, true, false).Action == CoopCampaignReloadAction.EndOwnedMission, "map is also reset");
        Check(map.Step(CoopCampaignObservedMissionKind.Map, "other", false, true, false).Action == CoopCampaignReloadAction.Fault, "replacement during close is never adopted");
        var unowned = new CoopCampaignMissionReload(); unowned.Request(Request());
        Check(unowned.Step(CoopCampaignObservedMissionKind.Other, "other", false, true, false).Action == CoopCampaignReloadAction.Wait, "unrelated mission preserved");
        var identity = new CoopCampaignMissionReload(); identity.Request(Request());
        Check(!identity.Request(Request(1, Next)), "same ordinal cannot change generation");
        Check(identity.Step(CoopCampaignObservedMissionKind.Intermission, null, false, false, true).Action == CoopCampaignReloadAction.Fault, "conflicting request has no acknowledgement");
        var epoch = new CoopCampaignMissionReload(); epoch.Request(Request());
        Check(!epoch.Request(Request(2)), "later request cannot reuse old generation");
        var retired = new CoopCampaignMissionReload(); retired.Request(Request());
        var firstAck = retired.Step(CoopCampaignObservedMissionKind.Intermission, null, false, false, true);
        retired.Commit(firstAck.Request); retired.Request(Request(2, Next));
        var secondAck = retired.Step(CoopCampaignObservedMissionKind.Intermission, null, false, false, true);
        retired.Commit(secondAck.Request);
        Check(!retired.Request(Request(3)), "a previously retired epoch cannot be reused after another load");
        Check(!new CoopCampaignMissionReload().Request(Request(0)), "zero ordinal rejected");
        Check(!new CoopCampaignMissionReload().Request(Request(1, "../other")), "noncanonical epoch rejected");
        Console.WriteLine("Mission reload cancellation, owned reset transport and same-save epochs passed; native acceptance pending.");
    }

    private static void ValidateWire()
    {
        var host = new CoopMissionProcessIdentity { Id = 111, StartUtcTicks = 639000000000000000, Path = Path.GetFullPath("host.exe") };
        var server = new CoopMissionProcessIdentity { Id = 222, StartUtcTicks = 639000000000000001, Path = Path.GetFullPath("server.exe") };
        var frame = new CoopCampaignReloadFrame { Binding = new CoopCampaignMissionEnvelope {
            Host = host, Dedicated = server, CampaignId = First, Epoch = Next, Pulse = 1, SentTimestamp = 100000,
            Intent = new CoopCampaignMissionIntent { Session = First, Revision = 1, Kind = CoopCampaignMissionIntentKind.Hold } } };
        byte[] bytes = CoopCampaignMissionReloadChannel.Encode(frame);
        var copy = CoopCampaignMissionReloadChannel.Decode(bytes);
        Check(frame.Matches(copy) && !copy.Acknowledgement, "request exact process/session/generation roundtrip");
        copy.Acknowledgement = true;
        Check(CoopCampaignMissionReloadChannel.Decode(CoopCampaignMissionReloadChannel.Encode(copy)).Acknowledgement && frame.Matches(copy), "acknowledgement correlates exact request");
        copy.Binding.Epoch = First; Check(!frame.Matches(copy), "late prior-load acknowledgement rejected");
        for (int i = 0; i < bytes.Length; i++)
        {
            byte[] truncated = new byte[i]; Array.Copy(bytes, truncated, i);
            Reject(() => CoopCampaignMissionReloadChannel.Decode(truncated), "truncated reset");
        }
        var corrupt = (byte[])bytes.Clone(); corrupt[4] = 2;
        Reject(() => CoopCampaignMissionReloadChannel.Decode(corrupt), "noncanonical acknowledgement flag");
        frame.Binding.Intent.Kind = CoopCampaignMissionIntentKind.Battle;
        Reject(() => CoopCampaignMissionReloadChannel.Encode(frame), "reset cannot smuggle ordinary battle command");
        frame.Binding.Intent.Kind = CoopCampaignMissionIntentKind.Hold;
        frame.Binding.BattleInstanceId = "retired-instance";
        Reject(() => CoopCampaignMissionReloadChannel.Encode(frame), "reset cannot retain retired result identity");
        frame.Binding.BattleInstanceId = null;
        var admitted = new CoopCampaignMissionAdmission(First, server, Next, First, host);
        Check(admitted.Admit(frame.Binding, 100001, 1000, _ => true) != null, "post-cleanup exact epoch admitted");
        frame.Binding.Epoch = First; frame.Binding.Pulse++;
        Check(admitted.Admit(frame.Binding, 100002, 1000, _ => true) == null, "old epoch cannot revive after same-campaign rewind");
        frame.Binding.Epoch = Next; frame.Binding.CampaignId = Next;
        Check(admitted.Admit(frame.Binding, 100002, 1000, _ => true) == null, "acknowledgement cannot admit another campaign");
        frame.Binding.CampaignId = First; frame.Binding.Host.Id++;
        Check(admitted.Admit(frame.Binding, 100002, 1000, _ => true) == null, "reset never adopts another host process");

        string repo = Environment.GetEnvironmentVariable("COOPSPECTATOR_REPOSITORY_ROOT");
        string capture = File.ReadAllText(Path.Combine(repo, "DedicatedServer", "CoopCampaignMissionServer.cs"));
        int cleanup = capture.IndexOf("BattleSnapshotRuntimeState.Clear(\"campaign-save-reload\")", StringComparison.Ordinal);
        int ack = capture.IndexOf("PublishReloadAcknowledgement(_reloadFrame, now)", cleanup, StringComparison.Ordinal);
        int commit = capture.IndexOf("Reload.Commit(decision.Request)", cleanup, StringComparison.Ordinal);
        Check(cleanup >= 0 && ack > cleanup && commit > ack, "adapter clears owned stores before acknowledgement/commit");
        Check(capture.Contains("canStart && !_opening, canEnd && !_opening"), "asynchronous opening gap cannot be mistaken for completed teardown");
        string detector = File.ReadAllText(Path.Combine(repo, "Campaign", "BattleDetector.cs"));
        Check(detector.Contains("ReferenceEquals(_battleResultCampaignContext, TaleWorlds.CampaignSystem.Campaign.Current)"), "same persisted campaign ID does not preserve old runtime result lease");
    }
    private static void Reject(Action action, string label)
    {
        bool rejected = false;
        try { action(); } catch (InvalidDataException) { rejected = true; } catch (IOException) { rejected = true; } catch (ArgumentException) { rejected = true; }
        Check(rejected, label);
    }
}
