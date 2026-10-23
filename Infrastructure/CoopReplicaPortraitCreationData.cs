using System;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade.View.Tableaus.Thumbnails;

namespace CoopSpectator.Infrastructure
{
    internal sealed class CoopReplicaPortraitCreationData : CharacterThumbnailCreationData
    {
        internal int Pose { get; }
        internal CoopReplicaPortraitCreationData(CharacterCode character, Action<Texture> completed,
            Action cancelled, bool big, int pose) : base(character, completed, cancelled, big)
        {
            if (pose < 0 || pose > 7 || !character.IsHero) throw new ArgumentOutOfRangeException(nameof(pose));
            Pose = pose;
            // Native normalized code already includes equipment and size. Keep
            // ordinary thumbnails and different host poses in distinct entries.
            RenderId += "_coop_pose_v1:" + pose;
        }
    }
}
