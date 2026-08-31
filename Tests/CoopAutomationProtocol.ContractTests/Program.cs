using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoopSpectator.Infrastructure.Automation;

internal sealed class ProtocolPayload
{
    public int Sequence { get; set; }
    public string Value { get; set; }
}

internal static class Program
{
    private const string RunId = "M2A-protocol-contract";
    private const string NonceHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string OtherHash = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    private static int Main()
    {
        try
        {
            ValidateProtocolCompatibility();
            ValidateEnvelopeIdentityAndOrdering();
            ValidateLeaseTimeline();
            ValidateOutcomeCodesAndPrecedence();
            ValidateFailureReasonsAndKnownIssues();
            ValidateRecoveryClassification();
            ValidateAtomicReplacementAndConcurrentReads();
            ValidateMalformedAndOversizedFiles();
            ValidateConcurrentAppendAndTemporaryLock();
            ValidateSimulatedCommitFailureCleanup();
            ValidateProcessedCommandAndRepeatReads();
            ValidateUnknownFieldCompatibility();
            Console.WriteLine("Coop automation protocol contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void ValidateProtocolCompatibility()
    {
        Assert(CoopAutomationRunContract.TryValidateProtocolVersion(1, 0, out _, out _), "The current protocol version must be accepted.");
        AssertFailure(CoopAutomationRunContract.TryValidateProtocolVersion(2, 0, out string majorCode, out _), majorCode, "ProtocolMajorUnsupported");
        AssertFailure(CoopAutomationRunContract.TryValidateProtocolVersion(1, 1, out string minorCode, out _), minorCode, "ProtocolMinorUnsupported");
    }

    private static void ValidateEnvelopeIdentityAndOrdering()
    {
        DateTime now = DateTime.UtcNow;
        CoopAutomationEnvelopeIdentity envelope = ValidEnvelope(now, 2);
        Assert(
            CoopAutomationRunContract.TryValidateEnvelope(
                envelope, RunId, NonceHash, "Runner", "runner-01", "MultiplayerClient", "multiplayer-client-01", 1, now,
                out string code, out string message),
            "The next exact role-bound command must be accepted: " + code + ": " + message);

        envelope.RunId = "other-run";
        AssertEnvelopeFailure(envelope, 1, now, "RunIdMismatch");
        envelope = ValidEnvelope(now, 2);
        envelope.NonceSha256 = OtherHash;
        AssertEnvelopeFailure(envelope, 1, now, "NonceMismatch");
        envelope = ValidEnvelope(now, 2);
        envelope.TargetRoleInstanceId = "multiplayer-client-02";
        AssertEnvelopeFailure(envelope, 1, now, "TargetRoleMismatch");
        envelope = ValidEnvelope(now, 2);
        AssertEnvelopeFailure(envelope, 2, now, "DuplicateCommand");
        envelope = ValidEnvelope(now, 1);
        AssertEnvelopeFailure(envelope, 2, now, "SequenceStale");
        envelope = ValidEnvelope(now, 4);
        AssertEnvelopeFailure(envelope, 2, now, "SequenceGap");
    }

    private static void ValidateLeaseTimeline()
    {
        DateTime now = DateTime.UtcNow;
        CoopAutomationRunLease lease = ValidLease(now);
        Assert(
            CoopAutomationRunContract.TryValidateLease(lease, RunId, NonceHash, now, out string code, out string message),
            "A current exact runner lease must be accepted: " + code + ": " + message);

        lease.CreatedUtc = now.AddMinutes(-3);
        lease.LastHeartbeatUtc = now.AddMinutes(-2);
        lease.ExpiresUtc = now.AddMinutes(-1);
        AssertFailure(
            CoopAutomationRunContract.TryValidateLease(lease, RunId, NonceHash, now, out code, out _),
            code,
            "LeaseExpired");

        lease = ValidLease(now);
        lease.NonceSha256 = OtherHash;
        AssertFailure(
            CoopAutomationRunContract.TryValidateLease(lease, RunId, NonceHash, now, out code, out _),
            code,
            "LeaseNonceMismatch");
    }

    private static void ValidateOutcomeCodesAndPrecedence()
    {
        var expected = new Dictionary<string, int>
        {
            ["Pass"] = 0,
            ["EnvironmentBlocked"] = 10,
            ["PreconditionsFailed"] = 11,
            ["AssertionFailed"] = 20,
            ["Crash"] = 30,
            ["Timeout"] = 31,
            ["RunnerInternalError"] = 40,
            ["Cancelled"] = 50
        };
        foreach (KeyValuePair<string, int> pair in expected)
            Assert(CoopAutomationRunContract.GetExitCode(pair.Key) == pair.Value, pair.Key + " must retain its stable exit code.");

        Assert(
            CoopAutomationRunContract.SelectInvocationOutcome(new[] { "Pass", "EnvironmentBlocked", "AssertionFailed", "Crash" }) == "Crash",
            "Multi-result precedence must select Crash over lower-priority outcomes.");
        Assert(CoopAutomationRunContract.GetExitCode("unknown") == 40, "Unknown outcomes must fail as RunnerInternalError.");
    }

    private static void ValidateFailureReasonsAndKnownIssues()
    {
        foreach (string reason in new[]
                 {
                     "RunIdMismatch", "NonceMismatch", "TopologyRejected",
                     "SnapshotDecodeFailed", "MaterializationAckTimeout",
                     "ReadinessGateStuck", "ControlledAgentNotSpawned",
                     "ResultIdentityMismatch", "NoHeartbeat", "NoProgress",
                     "CrashReporterDetected"
                 })
        {
            Assert(CoopAutomationRunContract.IsStableFailureReason(reason), reason + " must remain in the stable failure vocabulary.");
        }

        var annotation = new CoopAutomationKnownIssueAnnotation
        {
            KnownIssueId = "TW-NATIVE-001",
            OriginalOutcome = "Crash",
            AffectedVersions = new List<string> { "client-build-123" },
            AffectedSha256 = new List<string> { NonceHash },
            EvidenceReference = "artifact://crash.json",
            QuarantineReason = "Confirmed native crash outside mod ownership.",
            ReviewOrExpiryCondition = "Review on the next TaleWorlds build."
        };
        Assert(
            CoopAutomationRunContract.TryValidateKnownIssueAnnotation(annotation, out string code, out string message),
            "A complete non-pass known-issue annotation must be accepted: " + code + ": " + message);

        annotation.OriginalOutcome = "Pass";
        AssertFailure(
            CoopAutomationRunContract.TryValidateKnownIssueAnnotation(annotation, out code, out _),
            code,
            "KnownIssueUnexpectedPassReviewRequired");
    }

    private static void ValidateRecoveryClassification()
    {
        Assert(CoopAutomationRunContract.ClassifyRecoveryState(false, false, false, false, false) == CoopAutomationRecoveryState.None, "No artifacts must classify as no recovery state.");
        Assert(CoopAutomationRunContract.ClassifyRecoveryState(true, false, false, false, false) == CoopAutomationRecoveryState.PendingUnacknowledged, "A request without acknowledgement must remain pending and unacknowledged.");
        Assert(CoopAutomationRunContract.ClassifyRecoveryState(false, true, true, true, false) == CoopAutomationRecoveryState.AcknowledgedNonTerminal, "Processed plus matching non-terminal status must remain acknowledged/non-terminal.");
        Assert(CoopAutomationRunContract.ClassifyRecoveryState(false, true, true, true, true) == CoopAutomationRecoveryState.TerminalAcknowledged, "A matching terminal acknowledgement must be distinguishable.");
        Assert(CoopAutomationRunContract.ClassifyRecoveryState(false, true, true, false, true) == CoopAutomationRecoveryState.IdentityMismatch, "A foreign acknowledgement must never be accepted.");
        Assert(CoopAutomationRunContract.ClassifyRecoveryState(true, true, true, true, true) == CoopAutomationRecoveryState.Ambiguous, "Contradictory artifacts must require inspection.");
    }

    private static void ValidateAtomicReplacementAndConcurrentReads()
    {
        WithTemporaryDirectory(directory =>
        {
            string path = Path.Combine(directory, "status.json");
            CoopAutomationProtocolFileIO.WriteJsonStrictAtomic(path, new ProtocolPayload { Sequence = 0, Value = new string('a', 1024) });
            var failures = new ConcurrentQueue<Exception>();
            bool writerFinished = false;
            Task writer = Task.Run(() =>
            {
                try
                {
                    for (int i = 1; i <= 100; i++)
                        CoopAutomationProtocolFileIO.WriteJsonStrictAtomic(path, new ProtocolPayload { Sequence = i, Value = new string((char)('a' + i % 20), 1024) });
                }
                catch (Exception ex)
                {
                    failures.Enqueue(ex);
                }
                finally
                {
                    writerFinished = true;
                }
            });

            while (!writerFinished)
            {
                if (!CoopAutomationProtocolFileIO.TryReadJson(path, 1024 * 1024, out ProtocolPayload value, out string code, out string message) || value.Value?.Length != 1024)
                    failures.Enqueue(new InvalidOperationException("Concurrent reader observed a partial status: " + code + ": " + message));
            }
            writer.Wait();
            Assert(failures.IsEmpty, "Atomic replacement must not expose partial JSON: " + string.Join(" | ", failures.Select(item => item.Message)));
            Assert(!Directory.GetFiles(directory, "*.tmp").Any(), "Successful atomic replacement must not leave temporary files.");
        });
    }

    private static void ValidateMalformedAndOversizedFiles()
    {
        WithTemporaryDirectory(directory =>
        {
            string path = Path.Combine(directory, "command.json");
            File.WriteAllText(path, "{\"Sequence\":", new UTF8Encoding(false));
            AssertFailure(
                CoopAutomationProtocolFileIO.TryReadJson(path, 1024, out ProtocolPayload _, out string code, out _),
                code,
                "JsonMalformed");

            File.WriteAllText(path, new string('x', 2048), new UTF8Encoding(false));
            AssertFailure(
                CoopAutomationProtocolFileIO.TryReadJson(path, 128, out ProtocolPayload _, out code, out _),
                code,
                "FileTooLarge");

            string journal = Path.Combine(directory, "events.jsonl");
            File.WriteAllText(journal, "{\"Sequence\":1,\"Value\":\"ok\"}\n{\"Sequence\":", new UTF8Encoding(false));
            AssertFailure(
                CoopAutomationProtocolFileIO.TryReadJsonLines(journal, out List<ProtocolPayload> _, out code, out _),
                code,
                "JsonLineMalformed");
        });
    }

    private static void ValidateConcurrentAppendAndTemporaryLock()
    {
        WithTemporaryDirectory(directory =>
        {
            string journal = Path.Combine(directory, "events.jsonl");
            Task[] writers = Enumerable.Range(0, 4)
                .Select(writer => Task.Run(() =>
                {
                    for (int i = 1; i <= 20; i++)
                        CoopAutomationProtocolFileIO.AppendJsonLineStrict(journal, new ProtocolPayload { Sequence = writer * 100 + i, Value = "writer-" + writer });
                }))
                .ToArray();
            Task.WaitAll(writers);
            Assert(CoopAutomationProtocolFileIO.TryReadJsonLines(journal, out List<ProtocolPayload> records, out string code, out string message), "Concurrent event journal must remain readable: " + code + ": " + message);
            Assert(records.Count == 80 && records.Select(item => item.Sequence).Distinct().Count() == 80, "Concurrent append must preserve every complete unique event.");

            string lockedJournal = Path.Combine(directory, "locked-events.jsonl");
            Task appendTask;
            using (var lockStream = new FileStream(lockedJournal, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
            {
                appendTask = Task.Run(() => CoopAutomationProtocolFileIO.AppendJsonLineStrict(lockedJournal, new ProtocolPayload { Sequence = 1, Value = "after-lock" }));
                Thread.Sleep(50);
                Assert(!appendTask.IsCompleted, "The append must wait while another writer owns the journal.");
            }
            appendTask.Wait();
            Assert(CoopAutomationProtocolFileIO.TryReadJsonLines(lockedJournal, out records, out _, out _) && records.Count == 1, "The bounded append retry must succeed after a temporary lock is released.");
        });
    }

    private static void ValidateSimulatedCommitFailureCleanup()
    {
        WithTemporaryDirectory(directory =>
        {
            string path = Path.Combine(directory, "manifest.json");
            CoopAutomationProtocolFileIO.WriteJsonStrictAtomic(path, new ProtocolPayload { Sequence = 1, Value = "original" });
            bool threw = false;
            try
            {
                CoopAutomationProtocolFileIO.WriteJsonStrictAtomic(
                    path,
                    new ProtocolPayload { Sequence = 2, Value = "replacement" },
                    (_, _, _) => throw new IOException("simulated commit failure"));
            }
            catch (IOException)
            {
                threw = true;
            }

            Assert(threw, "A simulated commit failure must be observable.");
            Assert(CoopAutomationProtocolFileIO.TryReadJson(path, 4096, out ProtocolPayload value, out _, out _) && value.Sequence == 1, "A failed commit must preserve the previous complete value.");
            Assert(!Directory.GetFiles(directory, "*.tmp").Any(), "A failed commit must remove its temporary file.");
        });
    }

    private static void ValidateProcessedCommandAndRepeatReads()
    {
        WithTemporaryDirectory(directory =>
        {
            string inbox = Path.Combine(directory, "commands", "inbox", "command.json");
            string processed = Path.Combine(directory, "commands", "processed", "command.json");
            CoopAutomationProtocolFileIO.WriteJsonStrictAtomic(inbox, new ProtocolPayload { Sequence = 1, Value = "command" });
            Assert(CoopAutomationProtocolFileIO.TryMoveInboxToProcessed(inbox, processed, out string code, out string message), "A same-volume command must move to processed state: " + code + ": " + message);
            Assert(CoopAutomationProtocolFileIO.TryReadJson(processed, 4096, out ProtocolPayload first, out _, out _), "The processed command must be readable.");
            Assert(CoopAutomationProtocolFileIO.TryReadJson(processed, 4096, out ProtocolPayload second, out _, out _), "Repeated processed-command reads must remain stable.");
            Assert(first.Sequence == second.Sequence && first.Value == second.Value, "Repeated reads must return the same processed command.");

            CoopAutomationProtocolFileIO.WriteJsonStrictAtomic(inbox, new ProtocolPayload { Sequence = 1, Value = "duplicate" });
            AssertFailure(CoopAutomationProtocolFileIO.TryMoveInboxToProcessed(inbox, processed, out code, out _), code, "ProcessedAlreadyExists");
        });
    }

    private static void ValidateUnknownFieldCompatibility()
    {
        WithTemporaryDirectory(directory =>
        {
            string path = Path.Combine(directory, "payload.json");
            File.WriteAllText(path, "{\"Sequence\":7,\"Value\":\"known\",\"FutureOptionalField\":42}", new UTF8Encoding(false));
            Assert(CoopAutomationProtocolFileIO.TryReadJson(path, 4096, out ProtocolPayload value, out _, out _), "Unknown JSON fields must be ignored within a supported protocol version.");
            Assert(value.Sequence == 7 && value.Value == "known", "Known fields must survive unknown-field decoding.");
        });
    }

    private static CoopAutomationEnvelopeIdentity ValidEnvelope(DateTime now, long sequence)
    {
        return new CoopAutomationEnvelopeIdentity
        {
            ProtocolMajorVersion = 1,
            ProtocolMinorVersion = 0,
            RunId = RunId,
            NonceSha256 = NonceHash,
            SourceRoleType = "Runner",
            SourceRoleInstanceId = "runner-01",
            TargetRoleType = "MultiplayerClient",
            TargetRoleInstanceId = "multiplayer-client-01",
            Sequence = sequence,
            CommandId = Guid.NewGuid().ToString("D"),
            IssuedUtc = now,
            CampaignId = string.Empty,
            BattleInstanceId = string.Empty,
            BattleStage = string.Empty
        };
    }

    private static CoopAutomationRunLease ValidLease(DateTime now)
    {
        return new CoopAutomationRunLease
        {
            ProtocolMajorVersion = 1,
            ProtocolMinorVersion = 0,
            RunId = RunId,
            NonceSha256 = NonceHash,
            OwnerRoleType = "Runner",
            OwnerRoleInstanceId = "runner-01",
            OwnerProcessId = 1234,
            OwnerProcessStartUtc = now.AddMinutes(-1),
            CreatedUtc = now.AddSeconds(-30),
            LastHeartbeatUtc = now,
            ExpiresUtc = now.AddMinutes(5)
        };
    }

    private static void AssertEnvelopeFailure(CoopAutomationEnvelopeIdentity envelope, long lastSequence, DateTime now, string expectedCode)
    {
        bool accepted = CoopAutomationRunContract.TryValidateEnvelope(
            envelope, RunId, NonceHash, "Runner", "runner-01", "MultiplayerClient", "multiplayer-client-01", lastSequence, now,
            out string code, out _);
        AssertFailure(accepted, code, expectedCode);
    }

    private static void AssertFailure(bool accepted, string actualCode, string expectedCode)
    {
        Assert(!accepted && actualCode == expectedCode, "Expected failure " + expectedCode + " but observed accepted=" + accepted + " code=" + actualCode + ".");
    }

    private static void WithTemporaryDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "CoopAutomationProtocol.ContractTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            action(directory);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
