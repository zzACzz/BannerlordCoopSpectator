using System;
using System.Collections.Generic;

namespace CoopSpectator.Infrastructure
{
    public sealed class CoopHeroCreationParticipantSession
    {
        public string PlayerIdentityHash { get; set; }
        public CoopHeroCreationParticipantState State { get; set; }
        public CoopHeroCreationParticipantState StateBeforeDisconnect { get; set; }
        public DateTime? DisconnectedUtc { get; set; }
        public int Revision { get; set; }
        public string SubmissionId { get; set; }
        public string PayloadHash { get; set; }
        public CoopHeroDraft Draft { get; set; }
        public string Reason { get; set; }
    }

    public static class CoopHeroCreationStateMachine
    {
        public static CoopHeroCreationParticipantSession Invite(string playerIdentityHash)
        {
            if (string.IsNullOrWhiteSpace(playerIdentityHash))
                throw new ArgumentException("Stable player identity is required.", nameof(playerIdentityHash));
            return new CoopHeroCreationParticipantSession
            {
                PlayerIdentityHash = playerIdentityHash,
                State = CoopHeroCreationParticipantState.Invited,
                StateBeforeDisconnect = CoopHeroCreationParticipantState.Invited
            };
        }

        public static bool BeginEditing(CoopHeroCreationParticipantSession session, out string reason)
        {
            if (session != null && (session.State == CoopHeroCreationParticipantState.Invited ||
                                    session.State == CoopHeroCreationParticipantState.Editing))
            {
                session.State = CoopHeroCreationParticipantState.Editing;
                reason = string.Empty;
                return true;
            }
            reason = "invalid_transition_to_editing";
            return false;
        }

        public static bool Decline(CoopHeroCreationParticipantSession session, out string reason)
        {
            if (session != null && (session.State == CoopHeroCreationParticipantState.Invited ||
                                    session.State == CoopHeroCreationParticipantState.Editing))
            {
                session.State = CoopHeroCreationParticipantState.Declined;
                session.Reason = "player_declined";
                reason = string.Empty;
                return true;
            }
            reason = "invalid_transition_to_declined";
            return false;
        }

        public static bool Submit(
            CoopHeroCreationParticipantSession session,
            int revision,
            string submissionId,
            string payloadHash,
            CoopHeroDraft draft,
            CoopHeroCreationRules rules,
            out string reason)
        {
            if (session == null) { reason = "session_missing"; return false; }
            if (session.State == CoopHeroCreationParticipantState.Completed)
            {
                bool exactRetry = revision == session.Revision &&
                                  string.Equals(submissionId, session.SubmissionId, StringComparison.Ordinal) &&
                                  string.Equals(payloadHash, session.PayloadHash, StringComparison.OrdinalIgnoreCase);
                reason = exactRetry ? "already_completed_exact_retry" : "completed_payload_immutable";
                return exactRetry;
            }
            if (session.State != CoopHeroCreationParticipantState.Invited &&
                session.State != CoopHeroCreationParticipantState.Editing)
            {
                reason = "invalid_transition_to_completed";
                return false;
            }
            if (revision <= session.Revision || string.IsNullOrWhiteSpace(submissionId))
            {
                reason = "revision_or_submission_invalid";
                return false;
            }
            if (!CoopHeroCreationContract.ValidateDraft(draft, rules, out reason)) return false;
            string authoritativeHash = CoopHeroCreationHash.ComputeCanonicalJsonHash(draft);
            if (!string.Equals(payloadHash, authoritativeHash, StringComparison.OrdinalIgnoreCase))
            {
                reason = "payload_hash_mismatch";
                return false;
            }
            session.State = CoopHeroCreationParticipantState.Completed;
            session.Revision = revision;
            session.SubmissionId = submissionId;
            session.PayloadHash = authoritativeHash;
            session.Draft = draft;
            session.Reason = "completed";
            reason = string.Empty;
            return true;
        }

        public static bool Disconnect(CoopHeroCreationParticipantSession session, DateTime utcNow)
        {
            if (session == null || CoopHeroCreationContract.IsTerminal(session.State)) return false;
            if (session.State != CoopHeroCreationParticipantState.Disconnected)
                session.StateBeforeDisconnect = session.State;
            session.State = CoopHeroCreationParticipantState.Disconnected;
            session.DisconnectedUtc = utcNow;
            return true;
        }

        public static bool Reconnect(CoopHeroCreationParticipantSession session)
        {
            if (session == null || session.State != CoopHeroCreationParticipantState.Disconnected) return false;
            session.State = CoopHeroCreationParticipantState.Reconnected;
            session.State = session.StateBeforeDisconnect == CoopHeroCreationParticipantState.Editing
                ? CoopHeroCreationParticipantState.Editing
                : CoopHeroCreationParticipantState.Invited;
            session.DisconnectedUtc = null;
            return true;
        }

        public static void ApplyTimeouts(
            IEnumerable<CoopHeroCreationParticipantSession> sessions,
            DateTime utcNow,
            DateTime sessionDeadlineUtc,
            TimeSpan disconnectGrace)
        {
            if (sessions == null) return;
            foreach (CoopHeroCreationParticipantSession session in sessions)
            {
                if (session == null || CoopHeroCreationContract.IsTerminal(session.State)) continue;
                bool sessionExpired = utcNow >= sessionDeadlineUtc;
                bool graceExpired = session.State == CoopHeroCreationParticipantState.Disconnected &&
                                    session.DisconnectedUtc.HasValue &&
                                    utcNow >= session.DisconnectedUtc.Value.Add(disconnectGrace);
                if (sessionExpired || graceExpired)
                {
                    session.State = CoopHeroCreationParticipantState.TimedOut;
                    session.Reason = sessionExpired ? "session_timeout" : "disconnect_timeout";
                }
            }
        }
    }
}
