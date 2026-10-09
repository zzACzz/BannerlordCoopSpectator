using System;
using System.IO;

namespace CoopSpectator.Infrastructure
{
    // Detached native CircleLocalFrame and host diplomatic color, never a game handle.
    public sealed class CoopCampaignMapHoverDecal
    {
        public const int EncodedLength = 72;
        public bool Fortification { get; set; }
        public uint Color { get; set; }
        // Mat3 s/f/u, then origin. Fortification origin is global; others are local.
        public float[] Frame { get; set; }

        public static uint ResolveColor(bool war, bool sameFaction, bool alliance) =>
            war ? 4292093218u : sameFaction ? 4284183827u : alliance ? 4279386828u : 4291596077u;

        public static bool TryEncode(CoopCampaignMapHoverDecal state, out string encoded)
        {
            encoded = string.Empty;
            if (!IsValid(state)) return false;
            using (var stream = new MemoryStream(53))
            {
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write((byte)(state.Fortification ? 2 : 1));
                    writer.Write(state.Color);
                    foreach (float value in state.Frame) writer.Write(value);
                    encoded = Convert.ToBase64String(stream.ToArray());
                }
            }
            return true;
        }

        public static bool TryDecode(string encoded, out CoopCampaignMapHoverDecal state)
        {
            state = null;
            if (encoded == string.Empty) return true; // No admitted native presentation.
            if (encoded == null || encoded.Length != EncodedLength) return false;
            try
            {
                byte[] bytes = Convert.FromBase64String(encoded);
                if (bytes.Length != 53 || Convert.ToBase64String(bytes) != encoded) return false;
                using (var reader = new BinaryReader(new MemoryStream(bytes, false)))
                {
                    byte shape = reader.ReadByte();
                    if (shape != 1 && shape != 2) return false;
                    var result = new CoopCampaignMapHoverDecal {
                        Fortification = shape == 2, Color = reader.ReadUInt32(), Frame = new float[12]
                    };
                    for (int i = 0; i < result.Frame.Length; i++) result.Frame[i] = reader.ReadSingle();
                    if (!IsValid(result)) return false;
                    state = result;
                    return true;
                }
            }
            catch (FormatException) { return false; }
            catch (IOException) { return false; }
        }

        public static bool IsValidForEntity(string encoded, CoopCampaignMapPrototypeEntityKind kind)
        {
            return TryDecode(encoded, out var state) &&
                (state == null || !state.Fortification || kind == CoopCampaignMapPrototypeEntityKind.Settlement);
        }

        private static bool IsValid(CoopCampaignMapHoverDecal state)
        {
            if (state?.Frame == null || state.Frame.Length != 12 ||
                (state.Color != 4292093218u && state.Color != 4284183827u &&
                 state.Color != 4279386828u && state.Color != 4291596077u)) return false;
            for (int i = 0; i < state.Frame.Length; i++)
                if (float.IsNaN(state.Frame[i]) || float.IsInfinity(state.Frame[i]) ||
                    Math.Abs(state.Frame[i]) > (i < 9 ? 64f : 100000f)) return false;
            float[] f = state.Frame;
            double determinant = (double)f[0] * (f[4] * f[8] - f[5] * f[7]) -
                (double)f[1] * (f[3] * f[8] - f[5] * f[6]) +
                (double)f[2] * (f[3] * f[7] - f[4] * f[6]);
            return Math.Abs(determinant) > 0.000001d;
        }
    }
}
