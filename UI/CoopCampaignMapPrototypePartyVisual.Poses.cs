using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View;

namespace CoopSpectator.UI
{
    internal sealed partial class CoopCampaignMapPrototypePartyVisual
    {
        private sealed class PoseChannel
        {
            internal AgentVisuals Visual;
            internal ActionIndexCache OriginalAction;
            internal float OriginalSpeed;
            internal string Action;
            internal CoopConversationPosePlayback Playback;
        }

        private bool _acceptsHostPose;
        private PoseChannel[] _poseChannels;
        private int _poseSequence = -1;
        private string _poseSession;
        private bool _poseRecreationRequired;

        internal bool RequiresPoseRecreation(CoopCampaignMapPartyPoseEnvelope envelope)
        {
            if (_poseRecreationRequired) return true;
            if (_poseChannels == null) return false;
            var pose = envelope?.IsValid() == true && envelope.SessionId == _poseSession
                ? envelope.Pose : null;
            return NeedsChannelRecreation(0, pose?.Human) ||
                NeedsChannelRecreation(1, pose?.Mount) ||
                NeedsChannelRecreation(2, pose?.Caravan);
        }

        private bool NeedsChannelRecreation(int index, CoopCampaignMapPartyAnimation next)
        {
            return CoopCampaignMapPartyAnimation.RequiresVisualRecreation(
                _poseChannels[index]?.Action, next?.ActionName);
        }

        // Owner-screen tick only; never called from a network callback. Only exact
        // catalog-built actors are eligible, not character fallback approximations.
        private void ApplyHostPose(CoopCampaignMapPartyPoseEnvelope envelope, float dt)
        {
            if (!_acceptsHostPose || envelope == null) return;
            if (envelope.IsValid() != true ||
                !envelope.Pose.MatchesAppearance(envelope.Pose.AppearanceRevision,
                    _humanVisual != null, _mountVisual != null, _caravanMountVisual != null))
            { ReleaseHostPose(); return; }
            if (_poseSession != envelope.SessionId) ReleaseHostPose();
            if (envelope.Sequence > _poseSequence)
            {
                var visuals = new[] { _humanVisual, _mountVisual, _caravanMountVisual };
                var samples = new[] { envelope.Pose.Human, envelope.Pose.Mount, envelope.Pose.Caravan };
                var actions = new ActionIndexCache[3];
                var skeletons = new Skeleton[3];
                var durations = new float[3];
                var cyclic = new bool[3];
                // Validate the entire mounted tuple before touching any actor.
                for (int i = 0; i < 3; i++)
                {
                    if (visuals[i] == null) continue;
                    skeletons[i] = visuals[i].GetVisuals()?.GetSkeleton();
                    actions[i] = ActionIndexCache.Create(samples[i].ActionName);
                    var set = visuals[i].GetCopyAgentVisualsData().ActionSetData;
                    // act_none is the native channel release sentinel, not a clip.
                    // It has no positive animation duration and must still replace
                    // a previously replicated looping battle/raid action.
                    if (samples[i].ActionName == "act_none")
                    {
                        if (skeletons[i] == null) { ReleaseHostPose(); return; }
                        actions[i] = ActionIndexCache.act_none;
                        continue;
                    }
                    if (skeletons[i] == null || actions[i].Index < 0 || !set.IsValid ||
                        string.IsNullOrEmpty(set.GetAnimationName(in actions[i])))
                    { ReleaseHostPose(); return; }
                    durations[i] = MBActionSet.GetActionAnimationDuration(set, in actions[i]);
                    if (!IsFinite(durations[i]) || durations[i] <= 0f)
                    { ReleaseHostPose(); return; }
                    cyclic[i] = (MBActionSet.GetActionAnimationFlags(set, in actions[i]) & AnimFlags.anf_cyclic) != 0;
                }
                if (_poseChannels == null)
                {
                    _poseChannels = new PoseChannel[3];
                    for (int i = 0; i < 3; i++)
                        if (visuals[i] != null)
                            _poseChannels[i] = new PoseChannel { Visual = visuals[i],
                                OriginalAction = skeletons[i].GetActionAtChannel(0),
                                OriginalSpeed = skeletons[i].GetAnimationSpeedAtChannel(0) };
                }
                for (int i = 0; i < 3; i++)
                {
                    var channel = _poseChannels[i];
                    if (channel == null) continue;
                    var sample = samples[i];
                    bool changed = skeletons[i].GetActionAtChannel(0).GetName() != sample.ActionName;
                    if (sample.ActionName == "act_none")
                    {
                        if (changed) visuals[i].SetAction(in actions[i], 0f, false);
                        skeletons[i].SetAnimationSpeedAtChannel(0, sample.Speed);
                        channel.Action = sample.ActionName;
                        channel.Playback = null;
                        continue;
                    }
                    bool seed = channel.Playback == null || channel.Action != sample.ActionName || changed;
                    if (changed) visuals[i].SetAction(in actions[i], sample.Progress, false);
                    else if (seed) skeletons[i].SetAnimationParameterAtChannel(0, sample.Progress);
                    if (seed)
                    {
                        channel.Playback = new CoopConversationPosePlayback(16f);
                        channel.Action = sample.ActionName;
                        skeletons[i].SetAnimationSpeedAtChannel(0, sample.Speed);
                    }
                    channel.Playback.SetTarget(sample.Progress, sample.Speed, durations[i], cyclic[i]);
                }
                _poseSequence = envelope.Sequence;
                _poseSession = envelope.SessionId;
            }
            if (_poseChannels == null) return;
            foreach (var channel in _poseChannels)
            {
                if (channel?.Playback == null) continue;
                var skeleton = channel.Visual.GetVisuals()?.GetSkeleton();
                if (skeleton != null && skeleton.GetActionAtChannel(0).GetName() == channel.Action)
                    skeleton.SetAnimationSpeedAtChannel(0,
                        channel.Playback.Step(skeleton.GetAnimationParameterAtChannel(0), dt));
            }
        }

        private void ReleaseHostPose()
        {
            var channels = _poseChannels;
            _poseChannels = null;
            _poseSequence = -1;
            _poseSession = null;
            if (channels == null) return;
            foreach (var channel in channels)
            {
                if (channel == null) continue;
                _poseRecreationRequired |= CoopCampaignMapPartyAnimation.RequiresVisualRecreation(
                    channel.Action, null);
                var skeleton = channel.Visual.GetVisuals()?.GetSkeleton();
                // Do not overwrite a native successor action already selected by Tick.
                if (skeleton == null || skeleton.GetActionAtChannel(0).GetName() != channel.Action) continue;
                if (channel.OriginalAction.Index >= 0 ||
                    channel.OriginalAction.GetName() == "act_none")
                    channel.Visual.SetAction(in channel.OriginalAction, 0f, false);
                if (IsFinite(channel.OriginalSpeed) && channel.OriginalSpeed >= 0f)
                    skeleton.SetAnimationSpeedAtChannel(0, channel.OriginalSpeed);
            }
        }
    }
}
