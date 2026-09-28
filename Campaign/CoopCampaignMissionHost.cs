using System;
using System.Diagnostics;
using System.IO;
using CoopSpectator.Infrastructure;
using CoopSpectator.Network.Messages;
using Newtonsoft.Json;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Campaign
{
    // All entry points run on the campaign main thread. The launcher attaches the
    // exact process it owns; no discovery or adoption of an existing server.
    internal static class CoopCampaignMissionHost
    {
        private static CoopCampaignMissionEnvelope _state;
        private static TaleWorlds.CampaignSystem.Campaign _campaign;
        private static CoopCampaignMissionIntent _lastIntent;
        private static long _revision, _pulse, _nextPulse;
        private static bool _faulted, _finalApplied = true;
        private static string _appliedBattleKey = "";
        public static bool OwnsCommands => _state != null;

        public static void Prepare(ProcessStartInfo start)
        {
            start.EnvironmentVariables.Remove(CoopCampaignMissionChannel.SessionEnvironment);
            if (!ExperimentalFeatures.EnableAutomaticCampaignMissions) return;
            Reset();
            if (TaleWorlds.CampaignSystem.Campaign.Current == null ||
                !BattleResultWritebackJournalBehavior.TryGetActiveCampaignId(out string campaignId))
                throw new InvalidOperationException("Automatic missions require an active campaign binding.");
            _campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            string session = Guid.NewGuid().ToString("N");
            using (var process = Process.GetCurrentProcess())
                _state = new CoopCampaignMissionEnvelope { CampaignId = campaignId, Host = CoopMissionProcessIdentity.Capture(process),
                    Intent = new CoopCampaignMissionIntent { Session = session } };
            CoopCampaignMissionChannel.HostSession = session;
            start.EnvironmentVariables[CoopCampaignMissionChannel.SessionEnvironment] = session;
        }

        public static void Attach(Process dedicated)
        {
            if (_state == null) return;
            try { _state.Dedicated = CoopMissionProcessIdentity.Capture(dedicated); }
            catch { Fail("dedicated-identity-unavailable"); }
        }

        public static void Reset()
        {
            // A former session's pulse expires; never delete a path owned by another process.
            _state = null; _campaign = null; _lastIntent = null;
            _revision = _pulse = _nextPulse = 0; _faulted = false;
            _appliedBattleKey = ""; _finalApplied = true;
            CoopCampaignMissionChannel.HostSession = null;
        }

        public static bool QueueBattle()
        {
            if (_state == null || _faulted || !ReferenceEquals(_campaign, TaleWorlds.CampaignSystem.Campaign.Current)) return false;
            try
            {
                byte[] bytes = CoopCampaignMissionChannel.ReadBounded(BattleRosterFileHelper.GetRosterFilePath(), CoopCampaignMissionChannel.MaxRosterBytes);
                var dto = JsonConvert.DeserializeObject<BattleRosterFileDto>(System.Text.Encoding.UTF8.GetString(bytes));
                BattleSnapshotMessage battle = dto?.Snapshot;
                if (battle == null || battle.CampaignId != _state.CampaignId || string.IsNullOrEmpty(battle.BattleId) ||
                    string.IsNullOrEmpty(battle.BattleInstanceId) || battle.Sides == null || battle.Sides.Count == 0 ||
                    !CoopCampaignMissionChannel.IsToken(battle.MultiplayerScene) || !CoopCampaignMissionChannel.IsToken(battle.MultiplayerGameType)) return false;
                string key = CoopCampaignMissionChannel.Digest(bytes);
                if (_state.Intent.BattleKey == key) return true;
                if (!string.IsNullOrEmpty(_state.Intent.BattleKey) && _appliedBattleKey != _state.Intent.BattleKey)
                    return false;
                CoopCampaignMissionChannel.WriteAtomic(Path.Combine(
                    CoopCampaignMissionChannel.DirectoryFor(_state.Intent.Session), "host_roster.json"), bytes);
                _state.BattleId = battle.BattleId; _state.BattleInstanceId = battle.BattleInstanceId;
                _state.Scene = battle.MultiplayerScene; _state.GameType = battle.MultiplayerGameType;
                _state.RosterSha256 = key; _state.Intent.BattleKey = key;
                _finalApplied = false;
                _nextPulse = 0;
                Tick();
                return !_faulted;
            }
            catch (Exception) { Fail("battle-intent-publication-failed"); return false; }
        }

        // Called only at the validated applied-result journal boundary, not on file read.
        public static void ObserveApplied(CoopBattleResultBridgeFile.BattleResultSnapshot result, bool followupPending)
        {
            if (_state == null || result == null || result.CampaignId != _state.CampaignId ||
                result.BattleId != _state.BattleId || result.BattleInstanceId != _state.BattleInstanceId) return;
            _appliedBattleKey = _state.Intent.BattleKey;
            _finalApplied = result.IsFinalStage && !followupPending;
            _nextPulse = 0;
        }

        public static void Tick()
        {
            if (_state == null || _faulted || _state.Dedicated == null) return;
            long now = Stopwatch.GetTimestamp();
            if (now < _nextPulse) return;
            _nextPulse = now + Stopwatch.Frequency;
            try
            {
                if (!ReferenceEquals(_campaign, TaleWorlds.CampaignSystem.Campaign.Current) ||
                    !BattleResultWritebackJournalBehavior.TryGetActiveCampaignId(out string campaignId) || campaignId != _state.CampaignId ||
                    !_state.Dedicated.IsAlive()) { Fail("campaign-or-dedicated-identity-lost"); return; }
                bool onMap = Mission.Current == null && GameStateManager.Current?.ActiveState is TaleWorlds.CampaignSystem.GameState.MapState;
                bool awaitingBattle = !string.IsNullOrEmpty(_state.Intent.BattleKey) && _appliedBattleKey != _state.Intent.BattleKey;
                var intent = new CoopCampaignMissionIntent {
                    Session = _state.Intent.Session, BattleKey = _state.Intent.BattleKey ?? "",
                    Kind = awaitingBattle ? CoopCampaignMissionIntentKind.Battle : onMap && _finalApplied ? CoopCampaignMissionIntentKind.Map : CoopCampaignMissionIntentKind.Hold,
                    HostOnGlobalMap = onMap, AftermathApplied = !string.IsNullOrEmpty(_appliedBattleKey),
                    AppliedBattleKey = _appliedBattleKey, FollowupStagePending = !_finalApplied };
                if (_lastIntent == null || !_lastIntent.SameContent(intent)) _revision = checked(_revision + 1);
                intent.Revision = _revision;
                _state.Intent = intent;
                _state.Pulse = checked(++_pulse); _state.SentTimestamp = now;
                CoopCampaignMissionChannel.Write(_state);
                _lastIntent = intent.Copy();
            }
            catch (Exception) { Fail("intent-publication-failed"); }
        }

        private static void Fail(string reason)
        {
            if (_faulted) return;
            _faulted = true;
            ModLogger.Info("CampaignMissionHost: stopped automatic transitions: " + reason);
            UiFeedback.ShowMessageDeferred("Coop: automatic mission transitions stopped (" + reason + "). Restart the owned server after inspection.");
        }
    }
}
