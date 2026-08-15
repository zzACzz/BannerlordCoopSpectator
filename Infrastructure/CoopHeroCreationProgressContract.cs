using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CoopSpectator.Infrastructure
{
    public sealed class CoopHeroCreationProgressSnapshot
    {
        public int ProtocolVersion { get; set; } = CoopHeroCreationContract.ProtocolVersion;
        public string CampaignScopeId { get; set; }
        public string RequestId { get; set; }
        public string SessionId { get; set; }
        public string Nonce { get; set; }
        public string RulesHash { get; set; }
        public string UpdatedUtc { get; set; }
        public bool EnrollmentClosed { get; set; }
        public bool ResultWritten { get; set; }
        public int RelevantCount { get; set; }
        public int TerminalCount { get; set; }
        public int CompletedCount { get; set; }
        public int DeclinedCount { get; set; }
        public int TimedOutCount { get; set; }
        public int DisconnectedCount { get; set; }
    }

    public sealed class CoopHeroCreationWallClockPollGate
    {
        private readonly TimeSpan _interval;
        private DateTime _nextPollUtc = DateTime.MinValue;

        public CoopHeroCreationWallClockPollGate(TimeSpan interval)
        {
            if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
            _interval = interval;
        }

        public bool TryEnter(DateTime utcNow)
        {
            DateTime normalizedUtc = NormalizeUtc(utcNow);
            if (_nextPollUtc != DateTime.MinValue && normalizedUtc < _nextPollUtc) return false;
            _nextPollUtc = normalizedUtc.Add(_interval);
            return true;
        }

        public void Reset()
        {
            _nextPollUtc = DateTime.MinValue;
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc) return value;
            if (value.Kind == DateTimeKind.Local) return value.ToUniversalTime();
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }
    }

    public static class CoopHeroCreationProgressContract
    {
        public static CoopHeroCreationProgressSnapshot CreateSnapshot(
            CoopHeroCreationRequest request,
            IEnumerable<CoopHeroCreationParticipantSession> sessions,
            bool enrollmentClosed,
            bool resultWritten,
            DateTime utcNow)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            List<CoopHeroCreationParticipantSession> relevantSessions =
                (sessions ?? Enumerable.Empty<CoopHeroCreationParticipantSession>())
                .Where(session => session != null)
                .ToList();

            return new CoopHeroCreationProgressSnapshot
            {
                CampaignScopeId = request.CampaignScopeId,
                RequestId = request.RequestId,
                SessionId = request.SessionId,
                Nonce = request.Nonce,
                RulesHash = request.RulesHash,
                UpdatedUtc = NormalizeUtc(utcNow).ToString("o", CultureInfo.InvariantCulture),
                EnrollmentClosed = enrollmentClosed,
                ResultWritten = resultWritten,
                RelevantCount = relevantSessions.Count,
                TerminalCount = relevantSessions.Count(session => CoopHeroCreationContract.IsTerminal(session.State)),
                CompletedCount = relevantSessions.Count(session => session.State == CoopHeroCreationParticipantState.Completed),
                DeclinedCount = relevantSessions.Count(session => session.State == CoopHeroCreationParticipantState.Declined),
                TimedOutCount = relevantSessions.Count(session => session.State == CoopHeroCreationParticipantState.TimedOut),
                DisconnectedCount = relevantSessions.Count(session => session.State == CoopHeroCreationParticipantState.Disconnected)
            };
        }

        public static bool MatchesActiveRequest(
            CoopHeroCreationRequest request,
            CoopHeroCreationProgressSnapshot snapshot,
            out string error)
        {
            if (request == null) return Fail("request_missing", out error);
            if (snapshot == null) return Fail("progress_missing", out error);
            if (snapshot.ProtocolVersion != CoopHeroCreationContract.ProtocolVersion)
                return Fail("protocol_mismatch", out error);
            if (!string.Equals(request.CampaignScopeId, snapshot.CampaignScopeId, StringComparison.Ordinal))
                return Fail("campaign_scope_mismatch", out error);
            if (!string.Equals(request.RequestId, snapshot.RequestId, StringComparison.Ordinal))
                return Fail("request_id_mismatch", out error);
            if (!string.Equals(request.SessionId, snapshot.SessionId, StringComparison.Ordinal))
                return Fail("session_id_mismatch", out error);
            if (!string.Equals(request.Nonce, snapshot.Nonce, StringComparison.Ordinal))
                return Fail("nonce_mismatch", out error);
            if (!string.Equals(request.RulesHash, snapshot.RulesHash, StringComparison.OrdinalIgnoreCase))
                return Fail("rules_hash_mismatch", out error);
            if (!DateTime.TryParse(
                    snapshot.UpdatedUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out _))
                return Fail("updated_utc_invalid", out error);
            if (snapshot.RelevantCount < 0 || snapshot.TerminalCount < 0 ||
                snapshot.CompletedCount < 0 || snapshot.DeclinedCount < 0 ||
                snapshot.TimedOutCount < 0 || snapshot.DisconnectedCount < 0)
                return Fail("count_negative", out error);
            if (snapshot.TerminalCount > snapshot.RelevantCount)
                return Fail("terminal_count_exceeds_relevant", out error);
            if (snapshot.CompletedCount + snapshot.DeclinedCount + snapshot.TimedOutCount != snapshot.TerminalCount)
                return Fail("terminal_breakdown_mismatch", out error);
            if (snapshot.DisconnectedCount > snapshot.RelevantCount - snapshot.TerminalCount)
                return Fail("disconnected_count_exceeds_waiting", out error);
            if (snapshot.ResultWritten && (!snapshot.EnrollmentClosed || snapshot.TerminalCount != snapshot.RelevantCount))
                return Fail("result_written_before_terminal", out error);

            error = string.Empty;
            return true;
        }

        public static string BuildSignature(CoopHeroCreationProgressSnapshot snapshot)
        {
            if (snapshot == null) return string.Empty;
            return string.Join("|", new[]
            {
                snapshot.CampaignScopeId ?? string.Empty,
                snapshot.RequestId ?? string.Empty,
                snapshot.SessionId ?? string.Empty,
                snapshot.Nonce ?? string.Empty,
                snapshot.RulesHash ?? string.Empty,
                snapshot.EnrollmentClosed ? "1" : "0",
                snapshot.ResultWritten ? "1" : "0",
                snapshot.RelevantCount.ToString(CultureInfo.InvariantCulture),
                snapshot.TerminalCount.ToString(CultureInfo.InvariantCulture),
                snapshot.CompletedCount.ToString(CultureInfo.InvariantCulture),
                snapshot.DeclinedCount.ToString(CultureInfo.InvariantCulture),
                snapshot.TimedOutCount.ToString(CultureInfo.InvariantCulture),
                snapshot.DisconnectedCount.ToString(CultureInfo.InvariantCulture)
            });
        }

        public static string BuildHostStatusText(CoopHeroCreationProgressSnapshot snapshot)
        {
            if (snapshot == null)
                return "Hero creation is starting. Waiting for the dedicated server.";
            if (snapshot.ResultWritten)
                return "Hero creation result is ready. Applying companions in the campaign.";
            if (snapshot.RelevantCount == 0)
                return snapshot.EnrollmentClosed
                    ? "Hero creation finished without eligible participants."
                    : "Hero creation is waiting for connected players to enroll.";

            int waitingCount = snapshot.RelevantCount - snapshot.TerminalCount;
            string status =
                "Hero creation: " + snapshot.TerminalCount + "/" + snapshot.RelevantCount +
                " finished; submitted " + snapshot.CompletedCount +
                ", declined " + snapshot.DeclinedCount +
                ", timed out " + snapshot.TimedOutCount +
                ", waiting " + waitingCount + ".";
            if (snapshot.DisconnectedCount > 0)
                status += " Disconnected: " + snapshot.DisconnectedCount + ".";
            return status;
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc) return value;
            if (value.Kind == DateTimeKind.Local) return value.ToUniversalTime();
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        private static bool Fail(string value, out string error)
        {
            error = value;
            return false;
        }
    }
}
