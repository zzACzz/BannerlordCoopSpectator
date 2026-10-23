using System;
using CoopSpectator.Infrastructure;

internal static class PartyAnimationTests
{
    internal static void Run()
    {
        foreach (string active in new[] { "act_map_raid", "act_map_attack_1h", "act_map_mount_attack_swing" })
        {
            Require(CoopCampaignMapPartyAnimation.RequiresVisualRecreation(active, "act_none"),
                "End of explicit action rebuilds native locomotion actor");
            Require(CoopCampaignMapPartyAnimation.RequiresVisualRecreation(active, null),
                "Expired or revoked explicit pose rebuilds actor");
            Require(!CoopCampaignMapPartyAnimation.RequiresVisualRecreation(active, active),
                "Repeated action does not recreate actor each frame");
            Require(!CoopCampaignMapPartyAnimation.RequiresVisualRecreation(active, "act_map_raid"),
                "Explicit action transitions retain phase playback");
        }
        foreach (string initial in new[] { null, "", "act_none" })
        foreach (string next in new[] { null, "act_none", "act_map_raid" })
            Require(!CoopCampaignMapPartyAnimation.RequiresVisualRecreation(initial, next),
                "Fresh or locomotion actor does not repeatedly rebuild");
        foreach (float speed in new[] { 0f, 1f, 10f, 12f, 16f })
        {
            var playback = new CoopConversationPosePlayback(16f);
            playback.SetTarget(0.25f, speed, 2f, true);
            Require(playback.Step(0.25f, 0f) == speed, "Map playback preserves full admitted speed");
            for (int frame = 0; frame < 120; frame++)
            {
                float rate = playback.Step((frame % 60) / 60f, 1f / 60f);
                Require(rate >= 0 && rate <= 16f && !float.IsNaN(rate), "Map playback remains finite and bounded");
                if (speed == 0) Require(rate == 0, "Paused map animation remains paused");
            }
        }
        bool defaultRejected = false;
        try { new CoopConversationPosePlayback().SetTarget(0, 12, 1, true); }
        catch (ArgumentOutOfRangeException) { defaultRejected = true; }
        Require(defaultRejected, "Dialogue playback domain remains unchanged");
        foreach (float limit in new[] { float.NaN, float.PositiveInfinity, 0f, -1f, 17f })
        {
            bool rejected = false;
            try { new CoopConversationPosePlayback(limit); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Require(rejected, "Invalid playback domain denied");
        }
        foreach (bool waiting in new[] { false, true })
        for (int mode = 0; mode <= 6; mode++)
        {
            float expected = mode == 0 || mode == 6 || (waiting && (mode == 3 || mode == 4))
                ? 0f : mode == 1 || mode == 3 ? 0.25f : 1f;
            Require(CoopCampaignMapPartyPose.TryResolveTimeScale(mode, waiting, 4f, out var scale) &&
                scale == expected, "Native campaign clock modes and wait gate");
        }
        Require(!CoopCampaignMapPartyPose.TryResolveTimeScale(7, false, 4, out _), "Unknown clock mode denied");
        Require(!CoopCampaignMapPartyPose.TryResolveTimeScale(2, false, float.NaN, out _), "Invalid fast clock denied");
        Require(CoopCampaignMapPartyPose.TryResolveTimeScale(2, false, 8, out var fastScale) && fastScale == 2,
            "Custom speed multiplier preserved");
        foreach (float scale in new[] { 0f, 0.25f, 1f, 2f, 16f })
        {
            var timed = new CoopCampaignMapPartyPose { TimeScale = scale,
                Human = new CoopCampaignMapPartyAnimation { ActionName = "act_map_raid", Speed = 1 } };
            Require(CoopCampaignMapPartyPoseCodec.TryEncode(timed, out var timedBytes) &&
                CoopCampaignMapPartyPoseCodec.TryDecode(timedBytes, out var timedCopy) &&
                timedCopy.TimeScale == scale && timed.Clone().TimeScale == scale, "Clock scale wire and clone");
            Array.Copy(BitConverter.GetBytes(float.NaN), 0, timedBytes, 8, 4);
            Require(!CoopCampaignMapPartyPoseCodec.TryDecode(timedBytes, out _), "Invalid clock wire denied");
        }
        string hostSession = Guid.NewGuid().ToString("D");
        var networkState = CoopCampaignMapPrototypeContract.ToNetworkState(
            new CoopCampaignMapPrototypeHostSnapshot { SessionId = hostSession }, 1);
        Require(networkState.SessionId == Guid.Parse(hostSession).ToString("N") &&
            networkState.Clone().SessionId == networkState.SessionId, "State session projection and clone");
        foreach (string action in new[] { "act_none", "act_map_raid", "act_map_attack_1h", "act_map_mount_attack_swing" })
        foreach (float phase in new[] { 0f, 0.5f, 1f })
        foreach (float speed in new[] { 0f, 1f, 16f })
        {
            var sample = new CoopCampaignMapPartyAnimation { ActionName = action, Progress = phase, Speed = speed };
            Require(sample.IsValid(), "Valid channel sample");
            var copy = sample.Clone();
            Require(!ReferenceEquals(copy, sample) && copy.ActionName == action &&
                copy.Progress == phase && copy.Speed == speed, "Detached clone");
            copy.ActionName = "changed";
            Require(sample.ActionName == action, "Clone isolation");
            Require(CoopCampaignMapPartyAnimationCodec.TryEncode(sample, out var bytes), "Encode");
            Require(CoopCampaignMapPartyAnimationCodec.TryDecode(bytes, out var decoded) &&
                decoded.ActionName == action && decoded.Progress == phase && decoded.Speed == speed, "Round trip");
            for (int count = 0; count < bytes.Length; count++)
            {
                var truncated = new byte[count]; Array.Copy(bytes, truncated, count);
                Require(!CoopCampaignMapPartyAnimationCodec.TryDecode(truncated, out _), "Truncation");
            }
            var extra = new byte[bytes.Length + 1]; Array.Copy(bytes, extra, bytes.Length);
            Require(!CoopCampaignMapPartyAnimationCodec.TryDecode(extra, out _), "Trailing byte");
            var malformed = (byte[])bytes.Clone(); malformed[0] ^= 1;
            Require(!CoopCampaignMapPartyAnimationCodec.TryDecode(malformed, out _), "Magic");
            malformed = (byte[])bytes.Clone(); malformed[8] = 255;
            Require(!CoopCampaignMapPartyAnimationCodec.TryDecode(malformed, out _), "UTF8");
            malformed = (byte[])bytes.Clone();
            Array.Copy(BitConverter.GetBytes(float.NaN), 0, malformed, malformed.Length - 4, 4);
            Require(!CoopCampaignMapPartyAnimationCodec.TryDecode(malformed, out _), "Wire NaN");
        }
        foreach (string name in new[] { null, "", "../action", "act name", "act\nname", "act\0name", new string('a', 129) })
            Require(!new CoopCampaignMapPartyAnimation { ActionName = name }.IsValid(), "Invalid action identifier");
        foreach (float value in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity, -1f, 17f })
        {
            Require(!new CoopCampaignMapPartyAnimation { ActionName = "act_map_raid", Speed = value }.IsValid(), "Invalid speed");
            Require(!new CoopCampaignMapPartyAnimation { ActionName = "act_map_raid", Progress = value }.IsValid(), "Invalid phase");
        }
        Require(!new CoopCampaignMapPartyAnimation { ActionName = "act_map_raid", Progress = 1.01f }.IsValid(), "Phase upper bound");
        Console.WriteLine("Party animation sample validation passed (foundation only).");
        for (int mask = 0; mask < 4; mask++)
        {
            var pose = new CoopCampaignMapPartyPose { AppearanceRevision = 7,
                Human = new CoopCampaignMapPartyAnimation { ActionName = "act_map_raid", Speed = 1 },
                Mount = (mask & 1) == 0 ? null : new CoopCampaignMapPartyAnimation { ActionName = "act_map_mount_attack_1h" },
                Caravan = (mask & 2) == 0 ? null : new CoopCampaignMapPartyAnimation { ActionName = "act_none" } };
            Require(pose.MatchesAppearance(7, true, (mask & 1) != 0, (mask & 2) != 0), "Appearance shape");
            Require(!pose.MatchesAppearance(8, true, (mask & 1) != 0, (mask & 2) != 0), "Stale revision");
            Require(!pose.MatchesAppearance(7, false, (mask & 1) != 0, (mask & 2) != 0), "Missing human");
            Require(!pose.MatchesAppearance(7, true, (mask & 1) == 0, (mask & 2) != 0), "Mount shape mismatch");
            var clone = pose.Clone(); clone.Human.Speed = 5;
            Require(pose.Human.Speed == 1, "Nested clone isolation");
            Require(CoopCampaignMapPartyPoseCodec.TryEncode(pose, out var bytes), "Pose encode");
            Require(CoopCampaignMapPartyPoseCodec.TryDecode(bytes, out var decoded) &&
                decoded.MatchesAppearance(7, true, (mask & 1) != 0, (mask & 2) != 0), "Pose round trip");
            for (int length = 0; length < bytes.Length; length++)
            {
                var truncated = new byte[length]; Array.Copy(bytes, truncated, length);
                Require(!CoopCampaignMapPartyPoseCodec.TryDecode(truncated, out _), "Pose truncation");
            }
            var extra = new byte[bytes.Length + 1]; Array.Copy(bytes, extra, bytes.Length);
            Require(!CoopCampaignMapPartyPoseCodec.TryDecode(extra, out _), "Pose trailing bytes");
            string session = Guid.NewGuid().ToString("N");
            var envelope = new CoopCampaignMapPartyPoseEnvelope { SessionId = session, EntityId = "party_1", Sequence = 4, Pose = pose };
            Require(CoopCampaignMapPartyPoseEnvelopeCodec.TryEncode(envelope, out var wire), "Envelope encode");
            Require(CoopCampaignMapPartyPoseEnvelopeCodec.TryDecode(wire, out var received), "Envelope decode");
            Require(received.Matches(session, "party_1", 3, 7, true, true, (mask & 1) != 0, (mask & 2) != 0), "Envelope admission");
            Require(!received.Matches(session, "party_1", 4, 7, true, true, (mask & 1) != 0, (mask & 2) != 0), "Replay denied");
            Require(!received.Matches(Guid.NewGuid().ToString("N"), "party_1", 3, 7, true, true, (mask & 1) != 0, (mask & 2) != 0), "Old session denied");
            Require(!received.Matches(session, "party_2", 3, 7, true, true, (mask & 1) != 0, (mask & 2) != 0), "Wrong party denied");
            Require(!received.Matches(session, "party_1", 3, 7, false, true, (mask & 1) != 0, (mask & 2) != 0), "Hidden party denied");
            var batch = new[] { envelope };
            Require(CoopCampaignMapPartyPoseBatchCodec.IsBound(batch, session, 4, 7, false), "Batch binding");
            Require(!CoopCampaignMapPartyPoseBatchCodec.IsBound(batch, session, 5, 7, false), "Old publication denied");
            Require(!CoopCampaignMapPartyPoseBatchCodec.IsBound(batch, session, 4, 8, false), "Old catalog denied");
            Require(!CoopCampaignMapPartyPoseBatchCodec.IsBound(batch, session, 4, 7, true), "Dialogue revokes party poses");
            Require(!CoopCampaignMapPartyPoseBatchCodec.IsBound(new[] { envelope, envelope.Clone() }, session, 4, 7, false), "Duplicate party denied");
            Require(CoopCampaignMapPartyPoseBatchCodec.TryEncode(batch, out var encodedBatch) &&
                CoopCampaignMapPartyPoseBatchCodec.TryDecode(encodedBatch, out var decodedBatch) &&
                CoopCampaignMapPartyPoseBatchCodec.IsBound(decodedBatch, session, 4, 7, false), "Batch round trip");
            Require(!CoopCampaignMapPartyPoseBatchCodec.TryDecode(encodedBatch + ";", out _), "Empty trailing element denied");
            Require(!CoopCampaignMapPartyPoseBatchCodec.TryDecode(" " + encodedBatch, out _), "Noncanonical base64 denied");
            Require(!CoopCampaignMapPartyPoseBatchCodec.TryDecode(new string(';', 64), out _), "Bound count before splitting");
            Require(!CoopCampaignMapPartyPoseBatchCodec.TryEncode(new CoopCampaignMapPartyPoseEnvelope[65], out _), "Bound output count");
            var host = new CoopCampaignMapPrototypeHostSnapshot {
                SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
                SessionId = session, Revision = 4, CatalogRevision = 7,
                UpdatedUtc = DateTime.UtcNow, PartyPoses = batch };
            Require(CoopCampaignMapPrototypeBridgeCodec.TryParse(CoopCampaignMapPrototypeBridgeCodec.Serialize(host),
                out var parsed, out _) && parsed.PartyPoses.Length == 1 &&
                parsed.PartyPoses[0].Pose.Human.ActionName == pose.Human.ActionName, "Host-file pose round trip");
            var hostClone = host.Clone(); hostClone.PartyPoses[0].Pose.Human.Speed = 12;
            Require(host.PartyPoses[0].Pose.Human.Speed == 1, "Host-file clone detached");
            var lines = CoopCampaignMapPrototypeBridgeCodec.Serialize(host);
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].StartsWith("Revision=")) lines[i] = "Revision=5";
            Require(!CoopCampaignMapPrototypeBridgeCodec.TryParse(lines, out _, out _), "Mixed host publication denied");
            for (int length = 0; length < wire.Length; length++)
            {
                var truncated = new byte[length]; Array.Copy(wire, truncated, length);
                Require(!CoopCampaignMapPartyPoseEnvelopeCodec.TryDecode(truncated, out _), "Envelope truncation");
            }
        }
    }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
}
