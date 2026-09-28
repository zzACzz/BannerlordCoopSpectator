using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace CoopSpectator.Infrastructure
{
    // Motion is deliberately separate from the immutable tableau and text payload.
    // Actor order is meaningful only with the exact descriptor hash and revision.
    public sealed class CoopCampaignMapConversationPose
    {
        public int ConversationRevision { get; set; }
        public int Sequence { get; set; }
        public string TableauHash { get; set; } = string.Empty;
        public List<CoopCampaignMapConversationActorPose> Actors { get; set; } =
            new List<CoopCampaignMapConversationActorPose>();

        public CoopCampaignMapConversationPose Clone()
        {
            var copy = (CoopCampaignMapConversationPose)MemberwiseClone();
            copy.Actors = Actors == null ? null : new List<CoopCampaignMapConversationActorPose>();
            if (Actors != null) foreach (var actor in Actors) copy.Actors.Add(actor?.Clone());
            return copy;
        }
    }

    public sealed class CoopCampaignMapConversationActorPose
    {
        public string ActionName { get; set; } = string.Empty;
        public float Progress { get; set; }
        public float Speed { get; set; } = 1f;
        public CoopCampaignMapConversationActorPose Clone() =>
            (CoopCampaignMapConversationActorPose)MemberwiseClone();
    }

    public static class CoopCampaignMapConversationPoseCodec
    {
        public const int MaxPayloadBytes = 512;
        public const int SchemaVersion = 1;
        private const int Magic = 0x43504F31;

        public static string Hash(CoopCampaignMapConversationTableauState tableau)
        {
            if (!CoopCampaignMapConversationTableauCodec.TrySerialize(tableau, out var bytes)) return null;
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        public static bool IsValid(CoopCampaignMapConversationPose pose)
        {
            if (pose == null || pose.ConversationRevision < 0 || pose.Sequence < 0 ||
                !IsHash(pose.TableauHash) || pose.Actors == null || pose.Actors.Count < 1 ||
                pose.Actors.Count > CoopCampaignMapConversationTableauCodec.MaxActors) return false;
            foreach (var actor in pose.Actors)
                if (actor == null || !IsAction(actor.ActionName) || !InRange(actor.Progress, 0f, 1f) ||
                    !InRange(actor.Speed, 0f, 10f)) return false;
            return true;
        }

        public static bool Matches(CoopCampaignMapConversationPose pose, CoopCampaignMapConversationState conversation) =>
            IsValid(pose) && conversation != null && conversation.IsActive &&
            pose.ConversationRevision == conversation.Revision && conversation.Tableau != null &&
            pose.Actors.Count == conversation.Tableau.Actors?.Count &&
            string.Equals(pose.TableauHash, Hash(conversation.Tableau), StringComparison.Ordinal);

        public static bool TrySerialize(CoopCampaignMapConversationPose pose, out byte[] bytes)
        {
            bytes = null;
            if (!IsValid(pose)) return false;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                writer.Write(SchemaVersion);
                writer.Write(pose.ConversationRevision);
                writer.Write(pose.Sequence);
                for (int i = 0; i < 64; i += 2) writer.Write(Convert.ToByte(pose.TableauHash.Substring(i, 2), 16));
                writer.Write(pose.Actors.Count);
                foreach (var actor in pose.Actors)
                {
                    CoopCampaignMapConversationTableauCodec.WriteText(writer, actor.ActionName);
                    writer.Write(actor.Progress);
                    writer.Write(actor.Speed);
                }
                writer.Flush();
                if (stream.Length > MaxPayloadBytes) return false;
                bytes = stream.ToArray();
                return true;
            }
        }

        public static bool TryDeserialize(byte[] bytes, out CoopCampaignMapConversationPose pose)
        {
            pose = null;
            if (bytes == null || bytes.Length < 1 || bytes.Length > MaxPayloadBytes) return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream))
                {
                    if (reader.ReadInt32() != Magic || reader.ReadInt32() != SchemaVersion) return false;
                    var result = new CoopCampaignMapConversationPose
                    {
                        ConversationRevision = reader.ReadInt32(), Sequence = reader.ReadInt32(),
                        TableauHash = BitConverter.ToString(reader.ReadBytes(32)).Replace("-", "").ToLowerInvariant()
                    };
                    int count = reader.ReadInt32();
                    if (count < 1 || count > CoopCampaignMapConversationTableauCodec.MaxActors) return false;
                    for (int i = 0; i < count; i++) result.Actors.Add(new CoopCampaignMapConversationActorPose
                    {
                        ActionName = CoopCampaignMapConversationTableauCodec.ReadText(reader, 128),
                        Progress = reader.ReadSingle(), Speed = reader.ReadSingle()
                    });
                    if (stream.Position != stream.Length || !IsValid(result)) return false;
                    pose = result;
                    return true;
                }
            }
            catch (InvalidDataException) { return false; }
            catch (IOException) { return false; }
            catch (ArgumentException) { return false; }
        }

        private static bool IsAction(string value)
        {
            if (value == null || value.Length > 128 || !value.StartsWith("act_", StringComparison.Ordinal) ||
                value.Length == 4 || value == "act_none") return false;
            foreach (char c in value)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                    !(c >= '0' && c <= '9') && c != '_') return false;
            return true;
        }
        private static bool IsHash(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
            return true;
        }
        private static bool InRange(float value, float min, float max) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
    }

    // Same-action phase error changes playback rate, never seeks the skeleton.
    // The caller seeds phase once on first pose/action replacement. Local receipt
    // age is not an estimate of one-way network latency or a synchronized clock.
    public sealed class CoopConversationPosePlayback
    {
        private float _phase, _speed, _duration, _age, _rate;
        private bool _cyclic;
        public bool HasTarget { get; private set; }

        public void SetTarget(float phase, float speed, float duration, bool cyclic)
        {
            if (!Finite(phase) || phase < 0f || phase > 1f || !Finite(speed) || speed < 0f || speed > 10f ||
                !Finite(duration) || duration <= 0f)
                throw new ArgumentOutOfRangeException(nameof(phase));
            if (!HasTarget) _rate = speed;
            _phase = phase; _speed = speed; _duration = duration; _cyclic = cyclic; _age = 0f;
            HasTarget = true;
        }

        public float Step(float localPhase, float dt)
        {
            if (!HasTarget || !Finite(localPhase) || localPhase < 0f || localPhase > 1f || !Finite(dt) || dt < 0f)
                throw new ArgumentOutOfRangeException(nameof(localPhase));
            // A long frame expires correction but cannot cause a large rate jump.
            _age = Math.Min(1f, _age + dt);
            float wanted = _speed;
            if (_age <= 0.5f && _speed > 0f)
            {
                double projected = _phase + (double)_age * _speed / _duration;
                float target = _cyclic ? (float)(projected - Math.Floor(projected)) : (float)Math.Min(1d, projected);
                float error = target - localPhase;
                if (_cyclic) { if (error > 0.5f) error -= 1f; else if (error < -0.5f) error += 1f; }
                // Correct at most 15 percent; a sample cannot teleport or reverse
                // playback, including the 1 -> 0 wrap of a cyclic animation.
                if (Math.Abs(error) > 0.005f)
                    wanted += Clamp(error * _duration / 0.75f, -0.15f * _speed, 0.15f * _speed);
            }
            float step = Math.Min(dt, 0.1f) * Math.Max(1f, _speed);
            _rate = Clamp(_rate + Clamp(wanted - _rate, -step, step), 0f, 10f);
            return _rate;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
    }

    // Reject unmatched/future samples instead of retaining a cross-dialogue queue.
    // The publisher repeats fresh poses; delayed scene readiness cannot replay old motion.
    public sealed class CoopCampaignMapConversationPoseReceiver
    {
        private int _revision = -1;
        private int _sequence = -1;
        private string _hash;
        private int _count;

        public void Bind(CoopCampaignMapConversationState state)
        {
            if (state == null || state.Revision <= _revision) return;
            _revision = state.Revision;
            _sequence = -1;
            _hash = state.IsActive ? CoopCampaignMapConversationPoseCodec.Hash(state.Tableau) : null;
            _count = _hash == null ? 0 : state.Tableau.Actors.Count;
        }

        public bool TryAccept(CoopCampaignMapConversationPose pose, out CoopCampaignMapConversationPose accepted)
        {
            accepted = null;
            if (!CoopCampaignMapConversationPoseCodec.IsValid(pose) || _hash == null ||
                pose.ConversationRevision != _revision || pose.Sequence <= _sequence ||
                pose.Actors.Count != _count || !string.Equals(pose.TableauHash, _hash, StringComparison.Ordinal)) return false;
            _sequence = pose.Sequence;
            accepted = pose.Clone();
            return true;
        }

        public void Reset() { _revision = -1; _sequence = -1; _hash = null; _count = 0; }
    }
}
