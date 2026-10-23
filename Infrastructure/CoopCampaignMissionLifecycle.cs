using System;
using System.Collections.Generic;

namespace CoopSpectator.Infrastructure
{
    internal enum CoopCampaignMissionIntentKind { Hold, Map, Battle }
    internal enum CoopCampaignObservedMissionKind { Intermission, Map, Battle, Other }
    internal enum CoopCampaignMissionAction { Wait, StartMap, EndMap, StartBattle, Fault, EndBattle }

    // Adapter inputs are detached values. Freshness and exact PID/path/start-time
    // admission belong to the adapters; Session binds the admitted process pair.
    internal sealed class CoopCampaignMissionIntent
    {
        public string Session { get; set; }
        public long Revision { get; set; }
        public bool Fresh { get; set; }
        public CoopCampaignMissionIntentKind Kind { get; set; }
        // Exact validated battle/instance/scene/roster digest, not a scenario name.
        public string BattleKey { get; set; }
        public bool HostOnGlobalMap { get; set; }
        public bool AftermathApplied { get; set; }
        public string AppliedBattleKey { get; set; }
        public bool FollowupStagePending { get; set; }

        internal bool SameContent(CoopCampaignMissionIntent other) =>
            Kind == other.Kind && BattleKey == other.BattleKey &&
            HostOnGlobalMap == other.HostOnGlobalMap && AftermathApplied == other.AftermathApplied &&
            AppliedBattleKey == other.AppliedBattleKey && FollowupStagePending == other.FollowupStagePending;

        internal CoopCampaignMissionIntent Copy() => (CoopCampaignMissionIntent)MemberwiseClone();
    }

    internal sealed class CoopCampaignMissionObservation
    {
        public string Session { get; set; }
        public long Sequence { get; set; }
        public bool Fresh { get; set; }
        public CoopCampaignObservedMissionKind Kind { get; set; }
        public string MissionInstance { get; set; }
        // Set by a native mission-opening hook, not by an HTTP acknowledgement.
        public long OriginCommand { get; set; }
        public string BattleKey { get; set; }
        public bool CanStart { get; set; }
        public bool CanEnd { get; set; }
        public string CompletedBattleKey { get; set; }
        public long RejectedCommand { get; set; }
    }

    internal sealed class CoopCampaignMissionDecision
    {
        public readonly CoopCampaignMissionAction Action;
        public readonly long Command;
        public readonly string BattleKey;
        public readonly string MissionInstance;
        public readonly string Reason;
        internal CoopCampaignMissionDecision(CoopCampaignMissionAction action, long command = 0,
            string battleKey = null, string missionInstance = null, string reason = null)
        { Action = action; Command = command; BattleKey = battleKey; MissionInstance = missionInstance; Reason = reason; }
    }

    // Pure one-shot reconciler. It does not read files, call the engine, send
    // commands, own a process or infer battle completion from Mission.Current.
    internal sealed class CoopCampaignMissionLifecycle
    {
        private readonly string _session;
        private readonly double _transitionTimeout;
        private long _lastObservation;
        private CoopCampaignMissionIntent _intent;
        private long _command;
        private CoopCampaignMissionDecision _pending;
        private double _pendingSince;
        private double _lastTime = -1;
        private string _ownedMap;
        private string _ownedBattle;
        private string _lastBattleKey;
        private bool _battleCompleted;
        private string _appliedBattleKey;
        private readonly HashSet<string> _issuedBattles = new HashSet<string>(StringComparer.Ordinal);
        private string _failure;

        public CoopCampaignMissionLifecycle(string session, double transitionTimeoutSeconds)
        {
            if (!Guid.TryParseExact(session, "N", out _) || !Finite(transitionTimeoutSeconds) || transitionTimeoutSeconds <= 0)
                throw new ArgumentException("A fresh bound session and positive finite timeout are required.");
            _session = session;
            _transitionTimeout = transitionTimeoutSeconds;
        }

        public CoopCampaignMissionDecision Step(CoopCampaignMissionIntent intent,
            CoopCampaignMissionObservation observed, double monotonicSeconds)
        {
            if (_failure != null) return Fault(_failure);
            if (!Finite(monotonicSeconds) || monotonicSeconds < 0 || monotonicSeconds < _lastTime)
                return Fault("invalid-monotonic-time");
            _lastTime = monotonicSeconds;
            if (_pending != null && monotonicSeconds - _pendingSince >= _transitionTimeout)
                return Fault("native-transition-timeout");
            if (observed == null || !observed.Fresh || observed.Session != _session || observed.Sequence <= _lastObservation)
                return Wait("no-fresh-bound-observation");
            _lastObservation = observed.Sequence;
            if (!Enum.IsDefined(typeof(CoopCampaignObservedMissionKind), observed.Kind) ||
                !Bounded(observed.MissionInstance) || !Bounded(observed.BattleKey) || !Bounded(observed.CompletedBattleKey) ||
                (observed.Kind != CoopCampaignObservedMissionKind.Intermission && string.IsNullOrEmpty(observed.MissionInstance)))
                return Fault("invalid-native-observation");

            bool admittedIntent = intent != null && intent.Fresh && intent.Session == _session && intent.Revision > 0;
            if (admittedIntent)
            {
                if (!Enum.IsDefined(typeof(CoopCampaignMissionIntentKind), intent.Kind) ||
                    !Bounded(intent.BattleKey) || !Bounded(intent.AppliedBattleKey) ||
                    (intent.Kind == CoopCampaignMissionIntentKind.Battle && string.IsNullOrWhiteSpace(intent.BattleKey)))
                    return Fault("invalid-host-intent");
                if (_intent != null && intent.Revision < _intent.Revision) admittedIntent = false;
                else if (_intent != null && intent.Revision == _intent.Revision && !_intent.SameContent(intent))
                    return Fault("intent-revision-reused");
                else _intent = intent.Copy();
            }

            if (_pending != null)
            {
                if (observed.RejectedCommand == _pending.Command) return Fault("native-command-rejected");
                if (_pending.Action == CoopCampaignMissionAction.EndBattle)
                {
                    if (observed.Kind == CoopCampaignObservedMissionKind.Intermission)
                    {
                        if (observed.CompletedBattleKey != _lastBattleKey)
                            return Fault("battle-ended-without-matching-completion");
                        _ownedBattle = null; _battleCompleted = true; _pending = null;
                    }
                    else if (observed.Kind != CoopCampaignObservedMissionKind.Battle ||
                        observed.MissionInstance != _ownedBattle || observed.BattleKey != _lastBattleKey)
                        return Fault("mission-replaced-during-battle-close");
                    else return Wait("awaiting-battle-teardown");
                }
                else if (_pending.Action == CoopCampaignMissionAction.EndMap)
                {
                    if (observed.Kind == CoopCampaignObservedMissionKind.Intermission)
                    { _ownedMap = null; _pending = null; }
                    else if (observed.MissionInstance != _ownedMap)
                        return Fault("mission-replaced-during-map-close");
                    else return Wait("awaiting-map-teardown");
                }
                else
                {
                    var expected = _pending.Action == CoopCampaignMissionAction.StartMap
                        ? CoopCampaignObservedMissionKind.Map : CoopCampaignObservedMissionKind.Battle;
                    if (observed.Kind == CoopCampaignObservedMissionKind.Intermission)
                        return Wait("awaiting-native-mission-open");
                    if (observed.Kind != expected || observed.OriginCommand != _pending.Command ||
                        (expected == CoopCampaignObservedMissionKind.Battle && observed.BattleKey != _pending.BattleKey))
                        return Fault("opened-mission-identity-mismatch");
                    if (expected == CoopCampaignObservedMissionKind.Map) _ownedMap = observed.MissionInstance;
                    else { _ownedBattle = observed.MissionInstance; _lastBattleKey = _pending.BattleKey; _battleCompleted = false; }
                    _pending = null;
                }
            }

            if (_ownedBattle != null)
            {
                if (observed.Kind == CoopCampaignObservedMissionKind.Battle && observed.MissionInstance == _ownedBattle &&
                    observed.BattleKey == _lastBattleKey)
                {
                    // Publication alone is insufficient: require the host's journal
                    // acknowledgement for this exact battle, never elapsed time.
                    if (admittedIntent && observed.CanEnd && observed.CompletedBattleKey == _lastBattleKey &&
                        _intent.AftermathApplied && _intent.AppliedBattleKey == _lastBattleKey)
                    {
                        _appliedBattleKey = _lastBattleKey;
                        return Issue(CoopCampaignMissionAction.EndBattle, monotonicSeconds, _lastBattleKey, _ownedBattle);
                    }
                    return Wait("battle-owns-lifecycle");
                }
                if (observed.Kind != CoopCampaignObservedMissionKind.Intermission || observed.CompletedBattleKey != _lastBattleKey)
                    return Fault("battle-ended-without-matching-completion");
                _ownedBattle = null;
                _battleCompleted = true;
            }
            if (_ownedMap != null &&
                (observed.Kind != CoopCampaignObservedMissionKind.Map || observed.MissionInstance != _ownedMap))
                return Fault("owned-map-disappeared");
            // Stale host data may confirm an in-flight operation but cannot
            // authorize a subsequent start/end using the cached previous intent.
            if (!admittedIntent) return Wait("no-fresh-host-intent");
            if (_battleCompleted && _intent.AftermathApplied && _intent.AppliedBattleKey == _lastBattleKey)
                _appliedBattleKey = _lastBattleKey;
            if (_ownedMap != null)
            {
                if (_intent.Kind == CoopCampaignMissionIntentKind.Battle && !_issuedBattles.Contains(_intent.BattleKey) && observed.CanEnd)
                    return Issue(CoopCampaignMissionAction.EndMap, monotonicSeconds, _intent.BattleKey, _ownedMap);
                return Wait("map-active");
            }
            if (observed.Kind != CoopCampaignObservedMissionKind.Intermission || !observed.CanStart)
                return Wait("native-intermission-not-ready-or-unowned-mission");
            if (_intent.Kind == CoopCampaignMissionIntentKind.Battle)
            {
                if (_issuedBattles.Contains(_intent.BattleKey)) return Wait("battle-already-consumed");
                if (_lastBattleKey != null && _appliedBattleKey != _lastBattleKey)
                    return Wait("awaiting-previous-stage-aftermath");
                return Issue(CoopCampaignMissionAction.StartBattle, monotonicSeconds, _intent.BattleKey);
            }
            if (_intent.Kind != CoopCampaignMissionIntentKind.Map || !_intent.HostOnGlobalMap || _intent.FollowupStagePending)
                return Wait("host-not-ready-for-map");
            if (_lastBattleKey != null && (!_battleCompleted || _appliedBattleKey != _lastBattleKey))
                return Wait("awaiting-matching-aftermath");
            return Issue(CoopCampaignMissionAction.StartMap, monotonicSeconds);
        }

        private CoopCampaignMissionDecision Issue(CoopCampaignMissionAction action, double now,
            string battleKey = null, string instance = null)
        {
            if (_command == long.MaxValue) return Fault("command-sequence-exhausted");
            if (action == CoopCampaignMissionAction.StartBattle)
            {
                // No eviction: dropping an old identity would permit replay.
                if (_issuedBattles.Count >= 4096) return Fault("session-battle-capacity-exhausted");
                _issuedBattles.Add(battleKey);
            }
            _pendingSince = now;
            _pending = new CoopCampaignMissionDecision(action, ++_command, battleKey, instance);
            return _pending;
        }
        private CoopCampaignMissionDecision Fault(string reason)
        { _failure = reason; return new CoopCampaignMissionDecision(CoopCampaignMissionAction.Fault, reason: reason); }
        private static CoopCampaignMissionDecision Wait(string reason) =>
            new CoopCampaignMissionDecision(CoopCampaignMissionAction.Wait, reason: reason);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Bounded(string value) => value == null || value.Length <= 256;
    }
}
