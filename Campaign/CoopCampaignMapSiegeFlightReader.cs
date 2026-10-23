using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using SandBox.View.Map.Visuals;
using TaleWorlds.Library;

namespace CoopSpectator.Campaign
{
    // Decodes a boxed native value struct only. Does not invoke the calculation,
    // traverse campaign state, or retain engine entity/scene/skeleton wrappers.
    // Not installed as a hook; caller identity/thread admission is still required.
    internal static class CoopCampaignMapSiegeFlightReader
    {
        private static readonly Type Data = typeof(SettlementVisual).GetNestedType(
            "SiegeBombardmentData", BindingFlags.NonPublic);
        private static FieldInfo Field(string name, Type expected)
        {
            var field = Data?.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return field?.FieldType == expected ? field : null;
        }
        private static readonly FieldInfo Origin = Field("LaunchGlobalPosition", typeof(Vec3));
        private static readonly FieldInfo Frame = Field("TargetAlignedShooterGlobalFrame", typeof(MatrixFrame));
        private static readonly FieldInfo Speed = Field("MissileSpeed", typeof(float));
        private static readonly FieldInfo Angle = Field("LaunchAngle", typeof(float));
        private static readonly FieldInfo Gravity = Field("Gravity", typeof(float));
        private static readonly FieldInfo Duration = Field("FlightDuration", typeof(float));

        internal static bool TryRead(object prepared, double collisionHours, out CoopCampaignMapSiegeFlight flight)
        {
            flight = null;
            if (prepared == null || Data == null || !Data.IsValueType || prepared.GetType() != Data ||
                Origin == null || Frame == null || Speed == null || Angle == null || Gravity == null || Duration == null)
                return false;
            try
            {
                var origin = (Vec3)Origin.GetValue(prepared);
                var frame = (MatrixFrame)Frame.GetValue(prepared);
                var duration = (float)Duration.GetValue(prepared);
                if (float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0) return false;
                // Native uses NormalizedCopy for both ballistic axes.
                var forward = Normalize(frame.rotation.f);
                var up = Normalize(frame.rotation.u);
                var value = new CoopCampaignMapSiegeFlight {
                    StartHours = collisionHours - duration, EndHours = collisionHours,
                    Origin = new[] { origin.x, origin.y, origin.z }, Forward = forward, Up = up,
                    Speed = (float)Speed.GetValue(prepared), LaunchAngle = (float)Angle.GetValue(prepared),
                    Gravity = (float)Gravity.GetValue(prepared)
                };
                if (!value.IsValid()) return false;
                flight = value;
                return true;
            }
            catch (Exception) { return false; }
        }

        private static float[] Normalize(Vec3 vector)
        {
            double length = Math.Sqrt((double)vector.x * vector.x + (double)vector.y * vector.y + (double)vector.z * vector.z);
            if (double.IsNaN(length) || double.IsInfinity(length) || length <= 0.000001) return null;
            return new[] { (float)(vector.x / length), (float)(vector.y / length), (float)(vector.z / length) };
        }
    }
}
