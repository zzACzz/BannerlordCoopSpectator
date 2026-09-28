using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopSpectator.Infrastructure;

internal static class ConversationPoseTests
{
    private static int _count;
    internal static void Run()
    {
        var conversation = new CoopCampaignMapConversationState
        { IsActive = true, Revision = 5, Tableau = ConversationTableauTests.Fixture() };
        var pose = Fixture(conversation);
        Check(CoopCampaignMapConversationPoseCodec.Matches(pose, conversation), "exact binding");
        Check(CoopCampaignMapConversationPoseCodec.TrySerialize(pose, out var bytes), "serialize");
        Check(CoopCampaignMapConversationPoseCodec.TryDeserialize(bytes, out var decoded), "deserialize");
        Check(CoopCampaignMapConversationPoseCodec.TrySerialize(decoded, out var roundtrip) &&
            Convert.ToBase64String(bytes) == Convert.ToBase64String(roundtrip), "all fields roundtrip");
        var clone = pose.Clone(); clone.Actors[0].Progress = 0.99f;
        Check(pose.Actors[0].Progress != clone.Actors[0].Progress, "detached actors");
        var maximal = pose.Clone();
        foreach (var actor in maximal.Actors) actor.ActionName = "act_" + new string('a', 124);
        Check(CoopCampaignMapConversationPoseCodec.TrySerialize(maximal, out var maximum) &&
            maximum.Length == 472 && maximum.Length <= 512, "maximum fits one bounded message");
        var invalid = new List<Action<CoopCampaignMapConversationPose>>
        {
            p => p.ConversationRevision = -1, p => p.Sequence = -1,
            p => p.TableauHash = null, p => p.TableauHash = new string('G', 64),
            p => p.TableauHash += "0", p => p.Actors = null, p => p.Actors.Clear(),
            p => p.Actors.Add(p.Actors[0].Clone()), p => p.Actors[0] = null,
            p => p.Actors[0].ActionName = "../act_idle", p => p.Actors[0].ActionName = "act_none",
            p => p.Actors[0].ActionName = "act_", p => p.Actors[0].ActionName = "act_🔥",
            p => p.Actors[0].ActionName = "act_" + new string('x', 125),
            p => p.Actors[0].Progress = float.NaN, p => p.Actors[0].Progress = float.PositiveInfinity,
            p => p.Actors[0].Progress = -0.01f, p => p.Actors[0].Progress = 1.01f,
            p => p.Actors[0].Speed = float.NaN, p => p.Actors[0].Speed = float.NegativeInfinity,
            p => p.Actors[0].Speed = -1, p => p.Actors[0].Speed = 11
        };
        foreach (var mutate in invalid)
        {
            var bad = pose.Clone(); mutate(bad);
            Check(!CoopCampaignMapConversationPoseCodec.TrySerialize(bad, out _), "invalid field");
        }
        for (int i = 0; i < bytes.Length; i++)
        {
            var truncated = new byte[i]; Array.Copy(bytes, truncated, i);
            Check(!CoopCampaignMapConversationPoseCodec.TryDeserialize(truncated, out _), "truncated payload");
        }
        var trailing = new byte[bytes.Length + 1]; Array.Copy(bytes, trailing, bytes.Length);
        Check(!CoopCampaignMapConversationPoseCodec.TryDeserialize(trailing, out _), "trailing byte");
        Check(!CoopCampaignMapConversationPoseCodec.TryDeserialize(new byte[513], out _), "allocation cap");
        var random = new Random(927);
        for (int i = 0; i < 500; i++)
        {
            var mutated = (byte[])bytes.Clone(); mutated[random.Next(bytes.Length)] = (byte)random.Next(256);
            Check(!CoopCampaignMapConversationPoseCodec.TryDeserialize(mutated, out var candidate) ||
                CoopCampaignMapConversationPoseCodec.IsValid(candidate), "mutation admission");
        }
        var receiver = new CoopCampaignMapConversationPoseReceiver();
        Check(!receiver.TryAccept(pose, out _), "pose cannot create dialogue");
        receiver.Bind(conversation);
        Check(receiver.TryAccept(pose, out var accepted), "matching pose accepted");
        accepted.Actors[0].Progress = 0.88f;
        Check(pose.Actors[0].Progress != accepted.Actors[0].Progress, "receiver returns detached state");
        Check(!receiver.TryAccept(pose, out _), "duplicate rejected");
        clone = pose.Clone(); clone.Sequence--;
        Check(!receiver.TryAccept(clone, out _), "reordered rejected");
        clone.Sequence = 11; clone.TableauHash = new string('0', 64);
        Check(!receiver.TryAccept(clone, out _), "foreign actors rejected");
        clone = pose.Clone(); clone.Sequence = 11; clone.Actors.RemoveAt(0);
        Check(!receiver.TryAccept(clone, out _), "actor count mismatch");
        clone = pose.Clone(); clone.Sequence = 11; clone.ConversationRevision++;
        Check(!receiver.TryAccept(clone, out _), "future pose not buffered");
        clone.ConversationRevision = 5;
        Check(receiver.TryAccept(clone, out _), "bad future sample did not consume sequence");
        receiver.Bind(CoopCampaignMapPrototypeContract.CreateInactiveConversation(6));
        clone.Sequence++;
        Check(!receiver.TryAccept(clone, out _), "closed dialogue cannot resurrect");
        receiver.Bind(conversation);
        Check(!receiver.TryAccept(clone, out _), "stale bind cannot reopen");
        conversation.Revision = 7; receiver.Bind(conversation);
        Check(!receiver.TryAccept(clone, out _), "same actors in new dialogue reject old pose");
        clone.ConversationRevision = 7;
        Check(receiver.TryAccept(clone, out _), "fresh reopened pose accepted");
        var replaced = conversation.Clone(); replaced.Revision = 8; replaced.Tableau.Actors[0].ClothColor1++;
        receiver.Bind(replaced); clone.ConversationRevision = 8; clone.Sequence++;
        Check(!receiver.TryAccept(clone, out _), "replacement descriptor rejects old hash");
        receiver.Reset(); conversation.Revision = 0; receiver.Bind(conversation);
        clone.ConversationRevision = 0; clone.Sequence = 0;
        Check(receiver.TryAccept(clone, out _), "mission reset permits new sequence");
        Check(CoopCampaignMapConversationTableauCodec.Equivalent(conversation.Tableau, conversation.Tableau.Clone()),
            "motion remains outside immutable resource identity");
        ValidateBridge(conversation);
        ValidatePlayback();
        ValidateWiring();
        Console.WriteLine("Conversation pose assertions passed: " + _count);
    }

    internal static CoopCampaignMapConversationPose Fixture(CoopCampaignMapConversationState conversation)
    {
        var pose = new CoopCampaignMapConversationPose
        { ConversationRevision = conversation.Revision, Sequence = 10, TableauHash = CoopCampaignMapConversationPoseCodec.Hash(conversation.Tableau) };
        for (int i = 0; i < conversation.Tableau.Actors.Count; i++)
            pose.Actors.Add(new CoopCampaignMapConversationActorPose
            { ActionName = "act_inventory_idle_loop", Progress = i * 0.2f, Speed = i * 0.5f });
        return pose;
    }

    private static void ValidateBridge(CoopCampaignMapConversationState conversation)
    {
        var host = new CoopCampaignMapPrototypeHostSnapshot
        {
            SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
            SessionId = "0123456789abcdef0123456789abcdef", Revision = 10, IsActive = true,
            UpdatedUtc = DateTime.UtcNow, Conversation = conversation, ConversationPose = Fixture(conversation)
        };
        var lines = CoopCampaignMapPrototypeBridgeCodec.Serialize(host);
        Check(CoopCampaignMapPrototypeBridgeCodec.TryParse(lines, out var decoded, out _) &&
            CoopCampaignMapConversationPoseCodec.Matches(decoded.ConversationPose, conversation) &&
            decoded.ConversationPose.Sequence == 10, "bridge pose roundtrip");
        var copy = host.Clone(); copy.ConversationPose.Actors[0].Progress = 0.9f;
        Check(host.ConversationPose.Actors[0].Progress != copy.ConversationPose.Actors[0].Progress, "host deep clone");
        Check(!CoopCampaignMapPrototypeBridgeCodec.TryParse(lines.Where(s => !s.StartsWith("ConversationPose=")).ToArray(),
            out _, out _), "bridge requires new schema field");
        Check(!CoopCampaignMapPrototypeBridgeCodec.TryParse(lines.Select(s => s.StartsWith("ConversationPose=") ?
            "ConversationPose=!" : s).ToArray(), out _, out _), "bridge rejects corrupt pose");
        Check(!CoopCampaignMapPrototypeBridgeCodec.TryParse(lines.Select(s => s.StartsWith("ConversationPose=") ?
            "ConversationPose=" + new string('A', 685) : s).ToArray(), out _, out _), "bridge rejects oversized encoded pose");
        copy = host.Clone(); copy.ConversationPose.Sequence++;
        Check(!CoopCampaignMapPrototypeBridgeCodec.TryParse(CoopCampaignMapPrototypeBridgeCodec.Serialize(copy), out _, out _),
            "bridge enforces host publication sequence");
        copy = host.Clone(); copy.ConversationPose.ConversationRevision++;
        Check(!CoopCampaignMapPrototypeBridgeCodec.TryParse(CoopCampaignMapPrototypeBridgeCodec.Serialize(copy), out _, out _),
            "bridge enforces exact dialogue");
        copy = host.Clone(); copy.Conversation.Tableau.Actors[0].ClothColor2++;
        Check(!CoopCampaignMapPrototypeBridgeCodec.TryParse(CoopCampaignMapPrototypeBridgeCodec.Serialize(copy), out _, out _),
            "bridge enforces exact actor resources");
        copy = host.Clone(); copy.ConversationPose = null;
        Check(CoopCampaignMapPrototypeBridgeCodec.TryParse(CoopCampaignMapPrototypeBridgeCodec.Serialize(copy), out _, out _),
            "unavailable motion does not invalidate tableau");
    }

    private static void ValidatePlayback()
    {
        var playback = new CoopConversationPosePlayback();
        playback.SetTarget(0.01f, 1f, 4f, true);
        float rate = playback.Step(0.99f, 0.016f);
        Check(rate > 1f && rate <= 1.0161f, "cyclic wrap corrects forward with bounded rate slew");
        playback = new CoopConversationPosePlayback();
        playback.SetTarget(0.98f, 1f, 4f, true);
        rate = playback.Step(0.02f, 0.016f);
        Check(rate < 1f && rate >= 0.9839f, "cyclic wrap corrects backward by slowing, not reverse playback");
        playback = new CoopConversationPosePlayback();
        playback.SetTarget(0.01f, 1f, 4f, false);
        Check(playback.Step(0.99f, 0.016f) < 1f, "noncyclic clip must not wrap");
        float phase = 0.8f, prior = 1f;
        playback = new CoopConversationPosePlayback();
        for (int frame = 0; frame < 1200; frame++)
        {
            // Jittered delivery, intermittent gaps and phase disagreement. The
            // simulated skeleton advances only by the returned rate, never seeks.
            if (frame % 13 == 0 && frame % 130 < 80)
                playback.SetTarget((frame / 60f / 4f) % 1f, 1f, 4f, true);
            rate = playback.Step(phase, 1f / 60f);
            Check(rate >= 0.8499f && rate <= 1.1501f, "bounded playback speed despite stale/jittered samples");
            Check(Math.Abs(rate - prior) <= 1f / 60f + 0.0001f, "rate slew bound across packet arrivals");
            float advanced = rate / 60f / 4f;
            Check(advanced >= 0f && advanced <= 0.0048f, "continuous forward phase progression");
            phase = (phase + advanced) % 1f; prior = rate;
        }
        for (int frame = 0; frame < 120; frame++) rate = playback.Step(phase, 1f / 60f);
        Check(Math.Abs(rate - 1f) < 0.0001f, "lost packets restore nominal speed, not stale correction");
        playback.SetTarget(phase, 0f, 4f, true);
        for (int frame = 0; frame < 120; frame++) rate = playback.Step(phase, 1f / 60f);
        Check(rate == 0f, "host zero speed settles to stopped without reverse");
        playback.SetTarget(phase, 10f, 4f, true);
        rate = playback.Step(phase, 9f);
        Check(rate <= 1.0001f, "long frame cannot cause unbounded rate jump");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -1f })
        {
            bool rejected = false;
            try { playback.Step(phase, invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "invalid tick rejected");
        }
        bool badDuration = false;
        try { playback.SetTarget(phase, 1f, 0f, true); } catch (ArgumentOutOfRangeException) { badDuration = true; }
        Check(badDuration, "zero duration rejected");
        playback = new CoopConversationPosePlayback();
        playback.SetTarget(0.4f, 1f, 4f, true);
        Check(Math.Abs(playback.Step(0.4f, 0f) - 1f) < 0.0001f, "replacement actor starts with nominal speed");
    }

    private static void ValidateWiring()
    {
        string root = Environment.GetEnvironmentVariable("COOPSPECTATOR_REPOSITORY_ROOT");
        string controller = File.ReadAllText(Path.Combine(root, "Mission", "CoopCampaignMapPrototypeNetworkController.cs"));
        string capture = File.ReadAllText(Path.Combine(root, "Campaign", "CoopCampaignMapConversationTableauCapture.cs"));
        string view = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapConversationTableauView.cs"));
        string screen = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapConversationView.cs"));
        Check(capture.Contains("skeleton.GetActionAtChannel(0)") && capture.Contains("skeleton.GetAnimationParameterAtChannel(0)") &&
            capture.Contains("skeleton.GetAnimationSpeedAtChannel(0)") && !capture.Contains("skeleton.Set"), "host reads live body channel only");
        string send = controller.Substring(controller.IndexOf("private void BroadcastConversationPose(", StringComparison.Ordinal));
        send = send.Substring(0, send.IndexOf("private static void TickClientCatalogTransport", StringComparison.Ordinal));
        Check(send.Contains("_conversationSends.ContainsKey(peer.Index)") && !send.Contains("_conversationSends.Clear") &&
            !send.Contains("SendConversation("), "motion skips busy text transfer rather than starving it");
        Check(controller.Contains("pose.ConversationRevision = _serverConversation.Revision;") &&
            controller.Contains("pose.Sequence = _networkRevision;") && controller.Contains("ClientPoseReceiver.Reset();"),
            "host session remapped and receiver reset");
        string apply = view.Substring(view.IndexOf("internal bool ApplyPose(", StringComparison.Ordinal));
        apply = apply.Substring(0, apply.IndexOf("private void Create(", StringComparison.Ordinal));
        Check(apply.Contains("_lifetime.IsReady") && apply.Contains("_poseReceiver.TryAccept") &&
            !apply.Contains("Create(state)") && !apply.Contains("Scene.CreateNewScene") &&
            !apply.Contains("Scene.Read") && !apply.Contains("AgentVisuals.Create"),
            "pose-only update never rebuilds scene or actor");
        Check(apply.Contains("SetAnimationParameterAtChannel(0, motion.Progress)") &&
            apply.Contains("else if (seed)") && apply.Contains("if (seed) skeletons[i].SetAnimationSpeedAtChannel") &&
            apply.Contains("_posePlayback[i].SetTarget") && apply.Contains("AnimFlags.anf_cyclic") &&
            apply.Contains("GetActionAnimationDuration"), "phase seeded only at first pose/action change; later packets steer rate");
        Check(view.Contains("_posePlayback[i].Step(") && view.Contains("_posePlayback.Clear();") &&
            view.Contains("_poseActions.Clear();"), "per-frame smoothing and resource lifetime reset");
        Check(apply.Contains("_verboseAttempt && _lastPoseTraceRevision != accepted.ConversationRevision"),
            "first-pose evidence is explicit-verbose and revision-bounded");
        Check(screen.Contains("_poseAge.Elapsed.TotalSeconds > 0.5") && screen.Contains("_tableau.ApplyPose(_pendingPose)") &&
            screen.Contains("ClientConversationPoseChanged -= OnClientConversationPoseChanged"), "coalesced pending sample expires and unsubscribes");
    }
    private static void Check(bool condition, string message)
    { _count++; if (!condition) throw new InvalidOperationException("Conversation pose: " + message); }
}
