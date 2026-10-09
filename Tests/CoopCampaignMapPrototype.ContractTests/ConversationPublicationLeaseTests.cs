using System;
using System.IO;
using CoopSpectator.Campaign;
using CoopSpectator.Infrastructure;

internal static class ConversationPublicationLeaseTests
{
    private static int _checks;
    private static readonly DateTime Now = new DateTime(2026, 9, 29, 7, 0, 0, DateTimeKind.Utc);

    internal static void Run()
    {
        var campaign = new object();
        var mission = new object();
        var lease = new CoopCampaignMapConversationPublicationLease();
        var map = Map();
        Check(!lease.TryAcquire(campaign, map.SessionId, mission, true, 10, out _), "no fabricated anchor");
        Check(lease.RememberPublishedMap(campaign, map, 10, Now), "remember successful map");
        map.Camera.OriginX = 77;
        map.Conversation.SpeakerName = "mutated caller";
        Check(lease.TryAcquire(campaign, map.SessionId, mission, true, 11.999, out var anchor), "fresh entry");
        Check(anchor.Camera.OriginX != 77 && anchor.Conversation.SpeakerName != "mutated caller", "detached input");
        anchor.Camera.OriginX = 88;
        anchor.NormalizedX = 999;
        Check(lease.TryAcquire(campaign, map.SessionId, mission, true, 7200, out anchor), "long same mission");
        Check(anchor.Camera.OriginX != 88 && anchor.NormalizedX == 123, "detached output");
        Check(anchor.UpdatedUtc == Now && anchor.Revision == 7 && anchor.SampleTimeMilliseconds == 100 &&
            anchor.CatalogRevision == 4 && anchor.DynamicRevision == 5, "does not fabricate freshness or revisions");

        // Every context-loss case must revoke, not temporarily bypass, the lease.
        for (int fault = 0; fault < 9; fault++)
        {
            map = Map();
            Check(lease.RememberPublishedMap(campaign, map, 10, Now), "new baseline");
            Check(lease.TryAcquire(campaign, map.SessionId, mission, true, 11, out _), "initial mission");
            Check(!lease.TryAcquire(fault == 0 ? new object() : fault == 1 ? null : campaign,
                fault == 2 ? Guid.NewGuid().ToString("N") : map.SessionId,
                fault == 3 ? null : fault == 4 ? new object() : mission,
                fault != 5, fault == 6 ? 10.9 : fault == 7 ? double.NaN : fault == 8 ? double.PositiveInfinity : 12,
                out var denied) && denied == null, "context loss " + fault);
            Check(!lease.TryAcquire(campaign, map.SessionId, mission, true, 13, out _), "no revival " + fault);
        }
        foreach (double age in new[] { 2d, 2.001d, 1000d })
        {
            Check(lease.RememberPublishedMap(campaign, Map(), 10, Now), "age baseline");
            Check(!lease.TryAcquire(campaign, Map().SessionId, mission, true, 10 + age, out _), "expired entry");
        }
        for (int fault = 0; fault < 11; fault++)
        {
            map = Map();
            Check(lease.RememberPublishedMap(campaign, map, 10, Now), "before invalid publication");
            if (fault == 0) map.SchemaVersion--;
            if (fault == 1) map.Camera = null;
            if (fault == 2) map.CatalogRevision = 0;
            if (fault == 3) map.DynamicRevision = 0;
            if (fault == 4) map.IsActive = false;
            if (fault == 5) map.UpdatedUtc = Now.AddSeconds(-3);
            if (fault == 6) map.UpdatedUtc = Now.AddSeconds(6);
            Check(!lease.RememberPublishedMap(fault == 7 ? null : campaign, fault == 8 ? null : map,
                fault == 9 ? -1 : fault == 10 ? double.NegativeInfinity : 10, Now), "invalid publication " + fault);
            Check(!lease.TryAcquire(campaign, Map().SessionId, mission, true, 11, out _), "invalid baseline revokes old map");
        }
        Check(lease.RememberPublishedMap(campaign, Map(), 10, Now), "reset baseline");
        lease.Reset();
        Check(!lease.TryAcquire(campaign, Map().SessionId, mission, true, 11, out _), "explicit shutdown reset");
        // A fresh real map after a battle/replacement is the sole re-entry route.
        Check(lease.RememberPublishedMap(campaign, Map(), 100, Now), "map after battle");
        Check(lease.TryAcquire(campaign, Map().SessionId, new object(), true, 100.1, out _), "new dialogue after real map");
        ValidateNativeWiring();
        Console.WriteLine("Conversation publication lease checks passed: " + _checks);
    }

    private static void ValidateNativeWiring()
    {
        // Supplement pure lifetime tests with native integration guardrails. These
        // are NOT proof that Bannerlord loaded the patch or rendered a dialogue.
        string publisher = File.ReadAllText("Campaign/CoopCampaignMapPrototypePublisherBehavior.cs");
        string overlay = File.ReadAllText("Campaign/CoopCampaignMapPrototypePublisherBehavior.MissionConversation.cs");
        string context = File.ReadAllText("Campaign/CoopCampaignMapMissionConversationContext.cs");
        string capture = File.ReadAllText("Campaign/CoopCampaignMapConversationCapture.cs");
        string opening = File.ReadAllText("Patches/MissionStateOpenNewPatches.cs");
        Check(opening.Contains("#else\n") || opening.Contains("#else\r\n"), "client-only opening branch");
        Check(opening.Contains("CoopCampaignMapPrototypePublisherBehavior.ObserveNativeOpening(__result, missionName)"),
            "pre-loading exact mission observation");
        Check(context.Contains("missionName != \"Conversation\"") && context.Contains("GameNetwork.IsClientOrReplay") &&
            context.Contains("GameNetwork.IsServer") && context.Contains("SandBox.Conversation.MissionLogics.ConversationMissionLogic") &&
            context.Contains("type.Assembly.GetName().Name == \"SandBox\""), "native role/name/behavior identity, not mode alone");
        Check(context.Contains("ReferenceEquals(state.CurrentMission, mission)") &&
            context.Contains("ReferenceEquals(candidate.Mission, mission)") &&
            context.Contains("SandBox.GauntletUI.Missions.MissionGauntletConversationView") &&
            context.Contains("mission.Mode != MissionMode.Conversation"), "view/current mission/barter boundary");
        Check(publisher.IndexOf("_conversationPublication.RememberPublishedMap", StringComparison.Ordinal) >
            publisher.IndexOf("if (!CoopCampaignMapPrototypeBridgeFile.TryWrite(snapshot", StringComparison.Ordinal),
            "anchor only after completed bridge write");
        Check(publisher.Contains("if (mapPublication) RevokeMissionConversation();") &&
            (publisher.Contains("if (mapPublication)\n") || publisher.Contains("if (mapPublication)\r\n")),
            "failed map capture cannot retain old lease");
        Check(overlay.Contains("snapshot.IsMoving = false;") && overlay.Contains("snapshot.MovementTarget = string.Empty;") &&
            !overlay.Contains("CaptureVisibleEntities(") && !overlay.Contains("TryCaptureMapCamera(") &&
            !overlay.Contains("_lastPublishedCampaign ="), "overlay freezes map and grants no tooltip capture");
        Check(publisher.Contains("if (!_clearConversationOnCaptureFailure) return;") &&
            overlay.Contains("_clearConversationOnCaptureFailure = true;"), "mission failure clears instead of retaining stale text");
        Check(publisher.Contains("if (mapPublication || CoopDebugConfig.VerboseDiagnostics)") &&
            publisher.Contains("if (!_clearConversationOnCaptureFailure || CoopDebugConfig.VerboseDiagnostics)"),
            "new mission diagnostics are explicit verbose only");
        Check(capture.Contains("dialog = ResolveDataSource(missionView) as MissionConversationVM;") &&
            capture.Contains("if (conversationView != null &&") && !capture.Contains("new MissionConversationVM(") &&
            !capture.Contains("ExecuteContinue(") && !capture.Contains("DoOption("), "read existing mission VM; no fake map tableau/choice");
    }

    private static CoopCampaignMapPrototypeHostSnapshot Map()
    {
        if (!CoopCampaignMapPrototypeContract.TryQuantizeCamera(1, 2, 3, 0, 1, 0, 0, 0, 1, 1, out var camera))
            throw new InvalidOperationException("test camera");
        return new CoopCampaignMapPrototypeHostSnapshot
        {
            SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
            SessionId = "ae0a0c1d61d84e5ca405922deec90d1a", Revision = 7,
            IsActive = true, NormalizedX = 123, NormalizedY = 321,
            SampleTimeMilliseconds = 100, CatalogRevision = 4, DynamicRevision = 5,
            Camera = camera, UpdatedUtc = Now
        };
    }

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException("Conversation publication lease: " + message);
    }
}
