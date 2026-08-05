using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure;
using Newtonsoft.Json;

internal static class Program
{
    private static int Main()
    {
        try
        {
            ValidateBalancedDraft();
            ValidateContinuousAgeBudgets();
            ValidateCanonicalHashAndLogicalIdentity();
            ValidateRequestJsonRoundTrip();
            ValidateFailureResultIdentity();
            ValidateSubmissionIdempotency();
            ValidateDisconnectAndTimeoutTransitions();
            ValidatePerkRules();
            ValidateChunkTransportRoundTrip();
            ValidateChunkTransportRejections();
            Console.WriteLine("Coop hero creation contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void ValidateBalancedDraft()
    {
        CoopHeroCreationRules rules = BuildRules();
        CoopHeroDraft draft = BuildValidDraft(rules);
        Assert(CoopHeroCreationContract.ValidateDraft(draft, rules, out string error), error);
        draft.Attributes["Vigor"]++;
        Assert(!CoopHeroCreationContract.ValidateDraft(draft, rules, out error) && error == "attribute_budget_mismatch", "Attribute over-budget must be rejected.");
    }

    private static void ValidateContinuousAgeBudgets()
    {
        CoopHeroCreationRules rules = BuildRules();
        Assert(rules.SessionSeconds == 900, "Creator session must allow fifteen minutes.");
        Assert(rules.AllowedAges.SequenceEqual(Enumerable.Range(20, 31)), "Every whole age from 20 through 50 must be allowed.");

        Assert(CoopHeroCreationRules.GetAttributeBudget(20) == 18, "Age 20 attribute budget is invalid.");
        Assert(CoopHeroCreationRules.GetAttributeBudget(29) == 18, "Age 29 attribute budget is invalid.");
        Assert(CoopHeroCreationRules.GetAttributeBudget(30) == 19, "Age 30 attribute budget is invalid.");
        Assert(CoopHeroCreationRules.GetAttributeBudget(39) == 19, "Age 39 attribute budget is invalid.");
        Assert(CoopHeroCreationRules.GetAttributeBudget(40) == 20, "Age 40 attribute budget is invalid.");
        Assert(CoopHeroCreationRules.GetAttributeBudget(49) == 20, "Age 49 attribute budget is invalid.");
        Assert(CoopHeroCreationRules.GetAttributeBudget(50) == 21, "Age 50 attribute budget is invalid.");
        Assert(CoopHeroCreationRules.GetFocusBudget(20) == 12, "Age 20 focus budget is invalid.");
        Assert(CoopHeroCreationRules.GetFocusBudget(29) == 12, "Age 29 focus budget is invalid.");
        Assert(CoopHeroCreationRules.GetFocusBudget(30) == 14, "Age 30 focus budget is invalid.");
        Assert(CoopHeroCreationRules.GetFocusBudget(39) == 14, "Age 39 focus budget is invalid.");
        Assert(CoopHeroCreationRules.GetFocusBudget(40) == 16, "Age 40 focus budget is invalid.");
        Assert(CoopHeroCreationRules.GetFocusBudget(49) == 16, "Age 49 focus budget is invalid.");
        Assert(CoopHeroCreationRules.GetFocusBudget(50) == 18, "Age 50 focus budget is invalid.");
        Assert(CoopHeroCreationRules.GetAttributeBudget(19) == -1 && CoopHeroCreationRules.GetFocusBudget(51) == -1,
            "Out-of-range ages must not receive a budget.");

        foreach (int age in new[] { 20, 29, 30, 39, 40, 49, 50 })
        {
            CoopHeroDraft draft = BuildValidDraft(rules, age);
            Assert(CoopHeroCreationContract.ValidateDraft(draft, rules, out string error), "Age " + age + ": " + error);
        }
    }

    private static void ValidateCanonicalHashAndLogicalIdentity()
    {
        CoopHeroCreationRules rules = BuildRules();
        CoopHeroDraft first = BuildValidDraft(rules);
        CoopHeroDraft second = BuildValidDraft(rules);
        second.Attributes = second.Attributes.Reverse().ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        Assert(CoopHeroCreationHash.ComputeCanonicalJsonHash(first) == CoopHeroCreationHash.ComputeCanonicalJsonHash(second), "Canonical hash must ignore dictionary insertion order.");
        string firstId = CoopHeroCreationContract.BuildLogicalHeroId("campaign-a", "player-a");
        string secondId = CoopHeroCreationContract.BuildLogicalHeroId("campaign-a", "player-a");
        Assert(firstId == secondId && firstId.Length == 64, "Logical hero id must be deterministic SHA-256.");
    }

    private static void ValidateSubmissionIdempotency()
    {
        CoopHeroCreationRules rules = BuildRules();
        CoopHeroDraft draft = BuildValidDraft(rules);
        string hash = CoopHeroCreationHash.ComputeCanonicalJsonHash(draft);
        CoopHeroCreationParticipantSession session = CoopHeroCreationStateMachine.Invite("player-hash");
        Assert(CoopHeroCreationStateMachine.BeginEditing(session, out _), "Invited participant must enter editing.");
        Assert(CoopHeroCreationStateMachine.Submit(session, 1, "submission-a", hash, draft, rules, out _), "Valid submission must complete.");
        Assert(CoopHeroCreationStateMachine.Submit(session, 1, "submission-a", hash, draft, rules, out string retryReason) && retryReason == "already_completed_exact_retry", "Exact retry must be acknowledged.");
        Assert(!CoopHeroCreationStateMachine.Submit(session, 2, "submission-b", hash, draft, rules, out string conflictReason) && conflictReason == "completed_payload_immutable", "Completed payload must be immutable.");
    }

    private static void ValidateDisconnectAndTimeoutTransitions()
    {
        DateTime now = DateTime.UtcNow;
        CoopHeroCreationParticipantSession reconnecting = CoopHeroCreationStateMachine.Invite("reconnect");
        CoopHeroCreationStateMachine.BeginEditing(reconnecting, out _);
        Assert(CoopHeroCreationStateMachine.Disconnect(reconnecting, now), "Editing participant must enter disconnected state.");
        Assert(CoopHeroCreationStateMachine.Reconnect(reconnecting) && reconnecting.State == CoopHeroCreationParticipantState.Editing, "Reconnect must restore editing state.");

        CoopHeroCreationParticipantSession expiring = CoopHeroCreationStateMachine.Invite("expiring");
        CoopHeroCreationStateMachine.Disconnect(expiring, now);
        CoopHeroCreationStateMachine.ApplyTimeouts(new[] { expiring }, now.AddSeconds(31), now.AddMinutes(5), TimeSpan.FromSeconds(30));
        Assert(expiring.State == CoopHeroCreationParticipantState.TimedOut && expiring.Reason == "disconnect_timeout", "Disconnect grace expiry must be terminal.");
    }

    private static void ValidatePerkRules()
    {
        CoopHeroCreationRules rules = BuildRules();
        CoopHeroDraft draft = BuildValidDraft(rules);
        draft.PerkIds.Add("perk-a");
        Assert(CoopHeroCreationContract.ValidateDraft(draft, rules, out _), "Eligible perk must be accepted.");
        draft.PerkIds.Add("perk-b");
        Assert(!CoopHeroCreationContract.ValidateDraft(draft, rules, out string error) && error.StartsWith("perk_alternative_conflict:"), "Alternative perks must be mutually exclusive.");
    }

    private static void ValidateChunkTransportRoundTrip()
    {
        byte[] noise = new byte[12000];
        new Random(314159).NextBytes(noise);
        string original = JsonConvert.SerializeObject(new
        {
            Name = "Український герой 測試 Герой",
            BodyProperties = Convert.ToBase64String(noise)
        }, Formatting.None);
        Assert(original.Length > 11025, "Transport fixture must exceed the native string crash payload.");

        Assert(
            CoopHeroCreationChunkCodec.TryEncode(original, out CoopHeroCreationChunkedPayload payload, out string error),
            error);
        Assert(payload.ChunkCount > 1, "Large payload must be split into multiple chunks.");
        Assert(payload.Chunks.All(chunk => chunk.Length > 0 && chunk.Length <= CoopHeroCreationChunkCodec.MaxChunkBytes),
            "Every transport chunk must stay within the native-safe byte limit.");

        Assert(
            CoopHeroCreationChunkAccumulator.TryCreate(
                payload.ChunkCount,
                payload.LogicalByteCount,
                payload.PayloadHash,
                DateTime.UtcNow,
                out CoopHeroCreationChunkAccumulator accumulator,
                out error),
            error);

        for (int index = payload.ChunkCount - 1; index >= 0; index--)
        {
            Assert(
                accumulator.TryAccept(
                    index,
                    payload.ChunkCount,
                    payload.Chunks[index],
                    DateTime.UtcNow,
                    out _,
                    out error),
                error);
        }
        Assert(accumulator.TryComplete(out string restored, out error), error);
        Assert(restored == original, "Chunk transport must preserve the exact Unicode JSON payload.");
    }

    private static void ValidateChunkTransportRejections()
    {
        byte[] noise = new byte[4096];
        new Random(271828).NextBytes(noise);
        string original = JsonConvert.SerializeObject(new { Payload = Convert.ToBase64String(noise) }, Formatting.None);
        Assert(
            CoopHeroCreationChunkCodec.TryEncode(original, out CoopHeroCreationChunkedPayload payload, out string error),
            error);

        Assert(
            CoopHeroCreationChunkAccumulator.TryCreate(
                payload.ChunkCount,
                payload.LogicalByteCount,
                payload.PayloadHash,
                DateTime.UtcNow,
                out CoopHeroCreationChunkAccumulator duplicateAccumulator,
                out error),
            error);
        Assert(duplicateAccumulator.TryAccept(0, payload.ChunkCount, payload.Chunks[0], DateTime.UtcNow, out _, out error), error);
        Assert(duplicateAccumulator.TryAccept(0, payload.ChunkCount, payload.Chunks[0], DateTime.UtcNow, out _, out error),
            "An identical duplicate chunk must be idempotent.");
        byte[] conflictingChunk = (byte[])payload.Chunks[0].Clone();
        conflictingChunk[0] ^= 0x5a;
        Assert(
            !duplicateAccumulator.TryAccept(0, payload.ChunkCount, conflictingChunk, DateTime.UtcNow, out _, out error) &&
            error == "duplicate_chunk_conflict",
            "A conflicting duplicate chunk must be rejected.");

        Assert(
            CoopHeroCreationChunkAccumulator.TryCreate(
                payload.ChunkCount,
                payload.LogicalByteCount,
                payload.PayloadHash,
                DateTime.UtcNow,
                out CoopHeroCreationChunkAccumulator incompleteAccumulator,
                out error),
            error);
        for (int index = 0; index < payload.ChunkCount - 1; index++)
            Assert(incompleteAccumulator.TryAccept(index, payload.ChunkCount, payload.Chunks[index], DateTime.UtcNow, out _, out error), error);
        Assert(!incompleteAccumulator.TryComplete(out _, out error) && error == "chunks_incomplete",
            "An incomplete transfer must not produce JSON.");

        string wrongHash = (payload.PayloadHash[0] == '0' ? "1" : "0") + payload.PayloadHash.Substring(1);
        Assert(
            CoopHeroCreationChunkAccumulator.TryCreate(
                payload.ChunkCount,
                payload.LogicalByteCount,
                wrongHash,
                DateTime.UtcNow,
                out CoopHeroCreationChunkAccumulator hashAccumulator,
                out error),
            error);
        for (int index = 0; index < payload.ChunkCount; index++)
            Assert(hashAccumulator.TryAccept(index, payload.ChunkCount, payload.Chunks[index], DateTime.UtcNow, out _, out error), error);
        Assert(!hashAccumulator.TryComplete(out _, out error) && error == "transport_hash_mismatch",
            "A payload with the wrong transport hash must be rejected.");

        Assert(
            CoopHeroCreationChunkAccumulator.TryCreate(
                1,
                16,
                CoopHeroCreationHash.ComputeSha256("not-the-payload"),
                DateTime.UtcNow,
                out CoopHeroCreationChunkAccumulator malformedAccumulator,
                out error),
            error);
        Assert(malformedAccumulator.TryAccept(0, 1, new byte[] { 1, 2, 3 }, DateTime.UtcNow, out _, out error), error);
        Assert(!malformedAccumulator.TryComplete(out _, out error) && error.StartsWith("payload_decompression_failed:"),
            "Malformed compressed data must be rejected.");

        Assert(
            !CoopHeroCreationChunkAccumulator.TryCreate(
                CoopHeroCreationChunkCodec.MaxChunkCount + 1,
                1,
                payload.PayloadHash,
                DateTime.UtcNow,
                out _,
                out error) && error == "chunk_count_invalid",
            "An oversized chunk manifest must be rejected.");
        Assert(
            !CoopHeroCreationChunkCodec.TryEncode(
                new string('x', CoopHeroCreationChunkCodec.MaxLogicalCharacters + 1),
                out _,
                out error) && error == "logical_character_count_invalid",
            "An oversized logical payload must be rejected before compression.");
    }

    private static CoopHeroCreationRules BuildRules()
    {
        CoopHeroCreationRules rules = new CoopHeroCreationRules();
        rules.Perks.Add(new CoopHeroCreationPerkRule { PerkId = "perk-a", SkillId = "OneHanded", RequiredSkillValue = 10, AlternativePerkId = "perk-b" });
        rules.Perks.Add(new CoopHeroCreationPerkRule { PerkId = "perk-b", SkillId = "OneHanded", RequiredSkillValue = 10, AlternativePerkId = "perk-a" });
        return rules;
    }

    private static CoopHeroDraft BuildValidDraft(CoopHeroCreationRules rules, int age = 20)
    {
        CoopHeroDraft draft = new CoopHeroDraft
        {
            Name = "Test Hero",
            CultureId = "empire",
            Age = age,
            BodyProperties = "<BodyProperties version=\"4\" age=\"" + age + "\" weight=\"0\" build=\"0\" key=\"00000000000000000000000000000000\" />"
        };
        foreach (string attribute in rules.AttributeIds) draft.Attributes[attribute] = 3;
        for (int i = 0; i < CoopHeroCreationRules.GetAttributeBudget(age) - 18; i++)
            draft.Attributes[rules.AttributeIds[i % rules.AttributeIds.Count]]++;
        for (int i = 0; i < rules.SkillIds.Count; i++)
        {
            draft.Skills[rules.SkillIds[i]] = i < 10 ? 10 : 0;
            draft.Focus[rules.SkillIds[i]] = i < 2 ? 2 : i < 10 ? 1 : 0;
        }
        int additionalFocus = CoopHeroCreationRules.GetFocusBudget(age) - 12;
        for (int i = 0; i < additionalFocus; i++)
            draft.Focus[rules.SkillIds[i % rules.SkillIds.Count]]++;

        return draft;
    }

    private static void ValidateRequestJsonRoundTrip()
    {
        CoopHeroCreationRules rules = BuildRules();
        CoopHeroCreationRequest request = new CoopHeroCreationRequest
        {
            CampaignScopeId = "campaign-round-trip",
            RequestId = "request-round-trip",
            SessionId = "session-round-trip",
            Nonce = "nonce-round-trip",
            CreatedUtc = DateTime.UtcNow.ToString("o"),
            Rules = rules,
            RulesHash = rules.ComputeHash()
        };

        string json = JsonConvert.SerializeObject(request, Formatting.None);
        CoopHeroCreationRequest restored = JsonConvert.DeserializeObject<CoopHeroCreationRequest>(json);
        Assert(restored != null && restored.Rules != null, "Request round-trip must preserve rules.");
        Assert(restored.Rules.AllowedAges.SequenceEqual(rules.AllowedAges), "Allowed ages must be replaced instead of appended during JSON deserialization.");
        Assert(restored.Rules.AllowedCultureIds.SequenceEqual(rules.AllowedCultureIds), "Culture ids must be replaced instead of appended during JSON deserialization.");
        Assert(restored.Rules.AttributeIds.SequenceEqual(rules.AttributeIds), "Attribute ids must be replaced instead of appended during JSON deserialization.");
        Assert(restored.Rules.SkillIds.SequenceEqual(rules.SkillIds), "Skill ids must be replaced instead of appended during JSON deserialization.");
        Assert(restored.Rules.ComputeHash() == request.RulesHash, "Rules hash must survive a JSON round-trip.");
        Assert(CoopHeroCreationContract.ValidateRequest(restored, out string error), error);
    }

    private static void ValidateFailureResultIdentity()
    {
        CoopHeroCreationResult result = new CoopHeroCreationResult
        {
            CampaignScopeId = "campaign-failure",
            RequestId = "request-failure",
            SessionId = "session-failure",
            Nonce = "nonce-failure",
            RulesHash = "rules-failure",
            CompletedUtc = DateTime.UtcNow.ToString("o"),
            FailureReason = "rules_hash_mismatch",
            Participants = new List<CoopHeroCreationParticipantResult>()
        };
        result.ResultId = CoopHeroCreationContract.ComputeResultId(result);

        string json = JsonConvert.SerializeObject(result, Formatting.None);
        CoopHeroCreationResult restored = JsonConvert.DeserializeObject<CoopHeroCreationResult>(json);
        Assert(restored != null && restored.Participants.Count == 0, "A failure result must not contain completed participants.");
        Assert(restored.ResultId == CoopHeroCreationContract.ComputeResultId(restored), "Failure result identity must survive a JSON round-trip.");
        restored.FailureReason = "different_failure";
        Assert(restored.ResultId != CoopHeroCreationContract.ComputeResultId(restored), "Failure reason must be covered by the result identity.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
