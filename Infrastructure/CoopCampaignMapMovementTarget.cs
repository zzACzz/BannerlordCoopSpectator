using System;

namespace CoopSpectator.Infrastructure
{
    public enum CoopCampaignMapMovementTargetKind
    {
        None, Point, Party, Settlement
    }

    // Host-only inputs to the installed MapScreen.TickCircles selection rules.
    // IDs must come from the disclosed host catalogue; never client interest.
    public sealed class CoopCampaignMapMovementTargetInput
    {
        public int PartyMoveMode { get; set; }
        public int DefaultBehavior { get; set; }
        public bool ForceAiNoPathMode { get; set; }
        public bool HasAiInteractable { get; set; }
        public bool HasMapEvent { get; set; }
        public float TargetDistanceSquared { get; set; }
        public bool HasMoveTargetParty { get; set; }
        public bool MoveTargetPartyVisible { get; set; }
        public string MoveTargetPartyId { get; set; }
        public bool MoveTargetHasSettlement { get; set; }
        public bool MoveTargetSettlementIsHideout { get; set; }
        public string MoveTargetSettlementId { get; set; }
        public string TargetSettlementId { get; set; }
    }

    public sealed class CoopCampaignMapMovementTargetSelection
    {
        public CoopCampaignMapMovementTargetKind Kind { get; }
        public string EntityId { get; }
        internal CoopCampaignMapMovementTargetSelection(CoopCampaignMapMovementTargetKind kind, string id = null)
        { Kind = kind; EntityId = id; }

        public bool SuppressesHover(string hoveredId) => EntityId != null &&
            string.Equals(EntityId, hoveredId, StringComparison.Ordinal);
    }

    public static class CoopCampaignMapMovementTargetPolicy
    {
        // Numeric values are from the installed game IL, not serialized enum guesses.
        public static CoopCampaignMapMovementTargetSelection Select(
            CoopCampaignMapMovementTargetInput input, Func<string, bool> isDisclosed)
        {
            var none = new CoopCampaignMapMovementTargetSelection(CoopCampaignMapMovementTargetKind.None);
            if (input == null || isDisclosed == null) return none;
            if (input.PartyMoveMode == 1 && input.DefaultBehavior != 2 && input.DefaultBehavior != 0 &&
                !input.ForceAiNoPathMode && !input.HasAiInteractable && !input.HasMapEvent &&
                IsFinite(input.TargetDistanceSquared) && input.TargetDistanceSquared > 0.01f)
                return new CoopCampaignMapMovementTargetSelection(CoopCampaignMapMovementTargetKind.Point);

            string id;
            CoopCampaignMapMovementTargetKind kind;
            if (input.PartyMoveMode == 2 && input.HasMoveTargetParty && input.MoveTargetPartyVisible)
            {
                bool settlement = input.MoveTargetHasSettlement && !input.MoveTargetSettlementIsHideout;
                id = settlement ? input.MoveTargetSettlementId : input.MoveTargetPartyId;
                kind = settlement ? CoopCampaignMapMovementTargetKind.Settlement : CoopCampaignMapMovementTargetKind.Party;
            }
            else if (input.DefaultBehavior == 2)
            {
                id = input.TargetSettlementId;
                kind = CoopCampaignMapMovementTargetKind.Settlement;
            }
            else return none;
            // Once the native target is selected, denial must not fall back to another
            // object (or disclose the rejected ID through hover suppression).
            return !string.IsNullOrEmpty(id) && isDisclosed(id)
                ? new CoopCampaignMapMovementTargetSelection(kind, id) : none;
        }

        public static bool TryPointScale(double hostCameraDistance, out double scale)
        {
            scale = 0;
            if (!IsFinite(hostCameraDistance) || hostCameraDistance < 0 || hostCameraDistance > 100000) return false;
            float adjusted = (float)hostCameraDistance + 80f;
            scale = Math.Max(0.2f, Math.Min(45f, adjusted * adjusted / 5000f));
            return true;
        }

        public static bool TryAnimationIndex(double elapsedSeconds, out int index)
        {
            index = 0;
            if (!IsFinite(elapsedSeconds) || elapsedSeconds < 0 || elapsedSeconds > 100000000d) return false;
            // Preserve the game's float division at atlas frame boundaries.
            int step = (int)((float)elapsedSeconds / 0.1f) % 10;
            index = step >= 5 ? 9 - step : step;
            return true;
        }

        internal static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    // A bounded, detached recipe for native decal rendering. Not a network message.
    // Origin/normal/wind and CircleFrame must be captured by the host. The adapter
    // must gate referenced entities against the current admitted replica state.
    public sealed class CoopCampaignMapMovementTargetPresentation
    {
        public CoopCampaignMapMovementTargetKind Kind { get; set; }
        public string EntityId { get; set; }
        public bool AtSea { get; set; }
        public bool Fortification { get; set; }
        public uint Color { get; set; }
        public float[] CircleFrame { get; set; }
        public float[] Origin { get; set; }
        public float[] VisualOriginOffset { get; set; }
        public float[] TerrainNormal { get; set; }
        public float[] Wind { get; set; }
        public double HostCameraDistance { get; set; }
        public double AnimationElapsedSeconds { get; set; }
    }

    public enum CoopCampaignMapMovementDecalMaterial { Circle, Fortification, Wind }

    public sealed class CoopCampaignMapMovementDecal
    {
        public CoopCampaignMapMovementDecalMaterial Material { get; internal set; }
        public uint Color { get; internal set; }
        public float[] Frame { get; internal set; }
        public float[] TextureCoordinates { get; internal set; }
    }

    public static class CoopCampaignMapMovementTargetLayout
    {
        public const uint PointColor = 4293993473u;

        public static bool TryBuild(CoopCampaignMapMovementTargetPresentation state,
            out CoopCampaignMapMovementDecal[] decals)
        {
            decals = null;
            if (state == null) return false;
            if (state.Kind == CoopCampaignMapMovementTargetKind.None)
            {
                // Absence is represented by null presentation at integration boundaries;
                // do not accept an ambiguous populated None record.
                return false;
            }
            bool point = state.Kind == CoopCampaignMapMovementTargetKind.Point;
            bool party = state.Kind == CoopCampaignMapMovementTargetKind.Party;
            if (!point && !party && state.Kind != CoopCampaignMapMovementTargetKind.Settlement) return false;
            if (point ? state.EntityId != null : string.IsNullOrEmpty(state.EntityId)) return false;
            if (state.Fortification && (point || party)) return false;
            if (!point && !party && state.AtSea) return false;

            double scale;
            if (point)
            {
                if (state.CircleFrame != null || state.Color != PointColor ||
                    !CoopCampaignMapMovementTargetPolicy.TryPointScale(state.HostCameraDistance, out scale)) return false;
            }
            else
            {
                var circle = new CoopCampaignMapHoverDecal {
                    Fortification = state.Fortification, Color = state.Color, Frame = state.CircleFrame
                };
                if (!CoopCampaignMapHoverDecal.TryEncode(circle, out _)) return false;
                float[] f = state.CircleFrame;
                scale = Math.Sqrt((double)f[0] * f[0] + (double)f[1] * f[1] + (double)f[2] * f[2]);
            }

            if (state.Fortification)
            {
                decals = new[] { Make(CoopCampaignMapMovementDecalMaterial.Fortification, state.Color,
                    (float[])state.CircleFrame.Clone(), -1) };
                return true;
            }
            if (!Vector(state.Origin, 3, 100000) || !Vector(state.TerrainNormal, 3, 1.001f)) return false;
            double normalLength = LengthSquared(state.TerrainNormal);
            if (normalLength < 0.99 || normalLength > 1.01 || state.TerrainNormal[2] <= 0.0001f) return false;

            if (point && state.AtSea)
            {
                if (!Vector(state.Wind, 2, 100000)) return false;
                double length = Math.Sqrt(LengthSquared(state.Wind));
                if (length < 0.000001) return false; // Unknown direction, no fabricated wind arrow.
                float wx = (float)(state.Wind[0] / length), wy = (float)(state.Wind[1] / length);
                float size = (float)(0.238846d * scale + 0.15d);
                // CreateMat3WithForward(wind), then stock +PI/2 about up.
                float[] frame = { wx * size, wy * size, 0, -wy * size, wx * size, 0,
                    0, 0, size, state.Origin[0], state.Origin[1], state.Origin[2] };
                decals = new[] { Make(CoopCampaignMapMovementDecalMaterial.Wind, state.Color, frame, -1) };
                return true;
            }
            if (point)
            {
                decals = new[] {
                    Make(CoopCampaignMapMovementDecalMaterial.Circle, state.Color, SurfaceFrame(state, 0.238846d * scale), 4),
                    Make(CoopCampaignMapMovementDecalMaterial.Circle, state.Color, SurfaceFrame(state, 0.278584d * scale), 5)
                };
                return true;
            }
            int index = 5;
            if (party && !CoopCampaignMapMovementTargetPolicy.TryAnimationIndex(state.AnimationElapsedSeconds, out index)) return false;
            decals = new[] { Make(CoopCampaignMapMovementDecalMaterial.Circle, state.Color,
                SurfaceFrame(state, scale * (party && state.AtSea ? 2.5d : 1.2d)), index) };
            return true;
        }

        private static float[] SurfaceFrame(CoopCampaignMapMovementTargetPresentation state, double scale)
        {
            float s = (float)scale;
            // Stock replaces only Identity.u with the terrain normal, then scales.
            return new[] { s, 0, 0, 0, s, 0, state.TerrainNormal[0] * s,
                state.TerrainNormal[1] * s, state.TerrainNormal[2] * s,
                state.Origin[0], state.Origin[1], state.Origin[2] };
        }
        private static CoopCampaignMapMovementDecal Make(CoopCampaignMapMovementDecalMaterial material,
            uint color, float[] frame, int index) => new CoopCampaignMapMovementDecal {
                Material = material, Color = color, Frame = frame,
                TextureCoordinates = index < 0 ? new[] { 1f, 1f, 0f, 0f } : new[] { .166f, 1f, .166f * index, 0f }
            };
        private static bool Vector(float[] value, int length, float bound)
        {
            if (value == null || value.Length != length) return false;
            foreach (float component in value)
                if (!CoopCampaignMapMovementTargetPolicy.IsFinite(component) || Math.Abs(component) > bound) return false;
            return true;
        }
        private static double LengthSquared(float[] value)
        {
            double result = 0;
            foreach (float component in value) result += (double)component * component;
            return result;
        }
    }
}
