using System;
using System.Collections.Generic;

namespace CoopSpectator.Infrastructure
{
    internal enum CoopCampaignReloadAction { Wait, EndOwnedMission, Acknowledge, Fault }

    internal sealed class CoopCampaignReloadRequest
    {
        // Epoch is per LOAD, including a rewind of the same saved campaign ID.
        // The process-pair channel must admit this request before reconciliation.
        public long Ordinal;
        public string Epoch, CampaignId;
        internal bool IsValid => Ordinal > 0 && Token(Epoch) && Token(CampaignId);
        private static bool Token(string value) => value?.Length == 32 &&
            Guid.TryParseExact(value, "N", out var id) && id != Guid.Empty && value == id.ToString("N");
        internal CoopCampaignReloadRequest Copy() => (CoopCampaignReloadRequest)MemberwiseClone();
        internal bool Same(CoopCampaignReloadRequest value) => value != null &&
            Ordinal == value.Ordinal && Epoch == value.Epoch && CampaignId == value.CampaignId;
    }

    internal sealed class CoopCampaignReloadDecision
    {
        public readonly CoopCampaignReloadAction Action;
        public readonly string MissionInstance, Reason;
        public readonly CoopCampaignReloadRequest Request;
        internal CoopCampaignReloadDecision(CoopCampaignReloadAction action, CoopCampaignReloadRequest request = null,
            string mission = null, string reason = null)
        { Action = action; Request = request?.Copy(); MissionInstance = mission; Reason = reason; }
    }

    // Explicit cancellation of an abandoned saved-world generation. This is not
    // normal battle completion: old losses/rewards must never be applied merely
    // to make a save-load transition possible. No files, clocks, engine or sleeps.
    internal sealed class CoopCampaignMissionReload
    {
        private CoopCampaignReloadRequest _pending, _completed;
        private string _closingMission;
        private bool _faulted, _readyToCommit;
        private readonly HashSet<string> _usedEpochs = new HashSet<string>(StringComparer.Ordinal);
        public bool IsPending => _pending != null;

        public bool Request(CoopCampaignReloadRequest value)
        {
            if (_faulted || value == null || !value.IsValid) return false;
            var previous = _pending ?? _completed;
            if (previous != null)
            {
                if (value.Ordinal < previous.Ordinal) return false;
                if (value.Ordinal == previous.Ordinal)
                {
                    if (!previous.Same(value)) _faulted = true;
                    return !_faulted;
                }
                // A later save load always has a fresh generation, even if its
                // persisted CampaignId is equal. Reused epochs cannot revive data.
                if (_usedEpochs.Contains(value.Epoch)) { _faulted = true; return false; }
            }
            _usedEpochs.Add(value.Epoch);
            _pending = value.Copy();
            _readyToCommit = false;
            // A new request while closing supersedes the target but does not
            // dispatch a second close against the same native mission.
            return true;
        }

        public CoopCampaignReloadDecision Step(CoopCampaignObservedMissionKind kind, string missionInstance,
            bool owned, bool canEnd, bool readyIntermission)
        {
            if (_faulted) return new CoopCampaignReloadDecision(CoopCampaignReloadAction.Fault, reason: "reload-request-reused");
            if (_pending == null) return new CoopCampaignReloadDecision(CoopCampaignReloadAction.Wait, reason: "no-reload");
            _readyToCommit = false;
            if (kind == CoopCampaignObservedMissionKind.Intermission)
            {
                if (missionInstance != null || owned || !readyIntermission)
                    return new CoopCampaignReloadDecision(CoopCampaignReloadAction.Wait, reason: "awaiting-ready-intermission");
                // The adapter clears the retired runtime/roster/result stores,
                // then persists this exact acknowledgement before Commit.
                _readyToCommit = true;
                return new CoopCampaignReloadDecision(CoopCampaignReloadAction.Acknowledge, _pending);
            }
            if (_closingMission != null)
            {
                if (!owned || missionInstance != _closingMission)
                {
                    _faulted = true;
                    return new CoopCampaignReloadDecision(CoopCampaignReloadAction.Fault, reason: "mission-replaced-during-reload");
                }
                return new CoopCampaignReloadDecision(CoopCampaignReloadAction.Wait, reason: "awaiting-owned-teardown");
            }
            if (!owned || string.IsNullOrEmpty(missionInstance) ||
                (kind != CoopCampaignObservedMissionKind.Map && kind != CoopCampaignObservedMissionKind.Battle))
                return new CoopCampaignReloadDecision(CoopCampaignReloadAction.Wait, reason: "unowned-mission-preserved");
            if (!canEnd) return new CoopCampaignReloadDecision(CoopCampaignReloadAction.Wait, reason: "awaiting-owned-close-readiness");
            _closingMission = missionInstance;
            return new CoopCampaignReloadDecision(CoopCampaignReloadAction.EndOwnedMission, _pending, missionInstance);
        }

        public bool Commit(CoopCampaignReloadRequest acknowledged)
        {
            if (_faulted || !_readyToCommit || _pending == null || !_pending.Same(acknowledged)) return false;
            _completed = _pending.Copy(); _pending = null; _closingMission = null;
            _readyToCommit = false;
            return true;
        }
    }
}
