using System;

namespace CoopSpectator.Infrastructure
{
    // Detached host-prepared visual trajectory; never selects targets or applies damage.
    // Times and velocity units are campaign hours, matching SettlementVisual.
    // Capture, session admission, transport and orientation remain separate concerns.
    public sealed class CoopCampaignMapSiegeFlight
    {
        public double StartHours { get; set; }
        public double EndHours { get; set; }
        public float[] Origin { get; set; }
        public float[] Forward { get; set; }
        public float[] Up { get; set; }
        public float Speed { get; set; }
        public float LaunchAngle { get; set; }
        public float Gravity { get; set; }
        // Optional until owner-side native appearance capture completes.
        public float[] PresentationScale { get; set; }

        internal CoopCampaignMapSiegeFlight Clone() => new CoopCampaignMapSiegeFlight {
            StartHours = StartHours, EndHours = EndHours, Speed = Speed,
            LaunchAngle = LaunchAngle, Gravity = Gravity, PresentationScale = (float[])PresentationScale?.Clone(),
            Origin = (float[])Origin?.Clone(), Forward = (float[])Forward?.Clone(), Up = (float[])Up?.Clone()
        };

        internal bool IsValid()
        {
            if (!Finite(StartHours) || !Finite(EndHours) || StartHours < 0 || EndHours <= StartHours ||
                !Vector(Origin, 100000) || !Unit(Forward) || !Unit(Up) ||
                !Finite(Speed) || Speed <= 0 || !Finite(Gravity) || Gravity <= 0 ||
                !Finite(LaunchAngle) || Math.Abs(LaunchAngle) > Math.PI / 2) return false;
            double dot = 0;
            for (int i = 0; i < 3; i++) dot += (double)Forward[i] * Up[i];
            if (PresentationScale != null)
            {
                if (!Vector(PresentationScale, 1000)) return false;
                foreach (float component in PresentationScale) if (component <= 0) return false;
            }
            return Math.Abs(dot) <= 0.001;
        }

        internal bool TryPosition(double campaignHours, out float[] position)
        {
            position = null;
            if (!IsValid() || !Finite(campaignHours) ||
                campaignHours <= StartHours || campaignHours >= EndHours) return false;
            double elapsed = campaignHours - StartHours;
            double horizontal = Speed * Math.Cos(LaunchAngle) * elapsed;
            double vertical = Speed * Math.Sin(LaunchAngle) * elapsed - Gravity * 0.5 * elapsed * elapsed;
            var value = new float[3];
            for (int i = 0; i < 3; i++)
            {
                double coordinate = Origin[i] + Forward[i] * horizontal + Up[i] * vertical;
                if (!Finite(coordinate) || Math.Abs(coordinate) > 100000) return false;
                value[i] = (float)coordinate;
            }
            position = value;
            return true;
        }

        // Native orientation uses the chord from t to t + 0.1 campaign hours,
        // not the instantaneous derivative. The look-ahead may exceed EndHours;
        // only the displayed position must remain in the actual flight interval.
        internal bool TryDirection(double campaignHours, out float[] direction)
        {
            direction = null;
            if (!TryPosition(campaignHours, out _)) return false;
            double elapsed = campaignHours - StartHours;
            double horizontal = Speed * Math.Cos(LaunchAngle);
            double vertical = Speed * Math.Sin(LaunchAngle) - Gravity * (elapsed + 0.05);
            var chord = new double[3];
            double lengthSquared = 0;
            for (int i = 0; i < 3; i++)
            {
                chord[i] = Forward[i] * horizontal + Up[i] * vertical;
                lengthSquared += chord[i] * chord[i];
            }
            if (!Finite(lengthSquared) || lengthSquared <= 1e-20) return false;
            double length = Math.Sqrt(lengthSquared);
            direction = new float[3];
            for (int i = 0; i < 3; i++) direction[i] = (float)(chord[i] / length);
            return true;
        }

        // Caller supplies the native projectile up axis and the separately prepared
        // prefab scale. Neither may be inferred from a machine attachment frame.
        internal bool TryFrame(double campaignHours, float[] referenceUp, float[] scale, out float[] frame)
        {
            frame = null;
            if (!TryPosition(campaignHours, out var position) ||
                !TryDirection(campaignHours, out var forward) ||
                !Vector(referenceUp, 100000) || !Vector(scale, 1000)) return false;
            foreach (float component in scale) if (component <= 0) return false;
            var side = Cross(forward, referenceUp);
            double length = Math.Sqrt((double)side[0] * side[0] +
                (double)side[1] * side[1] + (double)side[2] * side[2]);
            if (!Finite(length) || length <= 1e-10) return false;
            for (int i = 0; i < 3; i++) side[i] = (float)(side[i] / length);
            var up = Cross(side, forward);
            var result = new float[12];
            for (int i = 0; i < 3; i++)
            {
                result[i] = side[i] * scale[0];
                result[3 + i] = forward[i] * scale[1];
                result[6 + i] = up[i] * scale[2];
                result[9 + i] = position[i];
            }
            frame = result;
            return true;
        }

        private static float[] Cross(float[] a, float[] b) => new[] {
            a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0]
        };

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Vector(float[] value, double bound)
        {
            if (value == null || value.Length != 3) return false;
            foreach (float component in value)
                if (!Finite(component) || Math.Abs(component) > bound) return false;
            return true;
        }
        private static bool Unit(float[] value)
        {
            if (!Vector(value, 1.001)) return false;
            double lengthSquared = 0;
            foreach (float component in value) lengthSquared += (double)component * component;
            return Math.Abs(lengthSquared - 1) <= 0.001;
        }
    }
}
