using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View;

namespace CoopSpectator.UI
{
    // Explicit, bounded research only. Retain strings, never native wrappers or
    // visual/data owners: tracing must not extend their managed lifetimes.
    internal static class CoopCampaignMapVisualLifetimeTrace
    {
        private const int EventLimit = 2048;
        private static readonly bool Enabled = string.Equals(
            Environment.GetEnvironmentVariable("COOPSPECTATOR_MAP_VISUAL_LIFETIME_TRACE"),
            "1", StringComparison.Ordinal);
        private static readonly string OutputPath = Environment.GetEnvironmentVariable(
            "COOPSPECTATOR_MAP_VISUAL_LIFETIME_TRACE_FILE");
        private static readonly List<string> Events = new List<string>();
        private static int _sequence;
        private static int _creation;
        private static bool _fileFailed;

        internal static AgentVisuals Create(AgentVisualsData data, string name,
            bool isRandomProgress, bool needBatchedVersionForWeaponMeshes, bool forceUseFaceCache)
        {
            if (!Enabled)
                return AgentVisuals.Create(data, name, isRandomProgress,
                    needBatchedVersionForWeaponMeshes, forceUseFaceCache);

            int creation = System.Threading.Interlocked.Increment(ref _creation);
            Record("create-begin", creation, data?.AgentVisuals, data?.SceneData, name, null);
            try
            {
                AgentVisuals visual = AgentVisuals.Create(data, name, isRandomProgress,
                    needBatchedVersionForWeaponMeshes, forceUseFaceCache);
                Record("create-return", creation, data?.AgentVisuals, data?.SceneData, name, null);
                return visual;
            }
            catch (Exception ex)
            {
                // AgentVisuals' constructor writes data.AgentVisuals before Refresh.
                // Read that managed field even when no AgentVisuals was returned.
                Record("create-throw", creation, data?.AgentVisuals, data?.SceneData, name, ex);
                throw;
            }
        }

        internal static void Reset(AgentVisuals visual)
        {
            if (!Enabled)
            {
                visual?.Reset();
                return;
            }

            // GetVisuals only returns the existing managed data field. No entity,
            // skeleton, native memory, refcount, or validity query is performed.
            MBAgentVisuals native = visual?.GetVisuals();
            Record("reset-begin", 0, native, null, null, null);
            try
            {
                visual?.Reset();
                Record("reset-return", 0, native, null, null, null);
            }
            catch (Exception ex)
            {
                Record("reset-throw", 0, native, null, null, ex);
                throw;
            }
        }

        internal static void Failure(string operation, Exception exception)
        {
            if (Enabled) Record(operation, 0, null, null, null, exception);
        }

        internal static void PartialCleanup(string operation, MBAgentVisuals visual)
        {
            if (Enabled) Record(operation, 0, visual, null, null, null);
        }

        internal static void SceneEvent(string operation, Scene scene)
        {
            if (Enabled) Record(operation, 0, null, scene, null, null);
        }

        private static void Record(string operation, int creation, MBAgentVisuals visual,
            Scene scene, string name, Exception exception)
        {
            try
            {
                lock (Events)
                {
                    if (Events.Count >= EventLimit) return;
                    // Pointer is NativeObject's managed auto-property. ReferenceEquals
                    // avoids NativeObject's overloaded null/equality operators.
                    string line = (++_sequence).ToString(CultureInfo.InvariantCulture) + "|" +
                        DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "|" + operation +
                        "|creation=" + creation.ToString(CultureInfo.InvariantCulture) +
                        "|native=" + (ReferenceEquals(visual, null) ? "0" : visual.Pointer.ToUInt64().ToString("X")) +
                        "|scene=" + (ReferenceEquals(scene, null) ? "0" : scene.Pointer.ToUInt64().ToString("X")) +
                        "|name=" + Clean(name) + "|error=" + Clean(exception?.ToString());
                    Events.Add(line);
                    if (!_fileFailed && !string.IsNullOrWhiteSpace(OutputPath))
                    {
                        try { File.AppendAllText(OutputPath, line + Environment.NewLine); }
                        catch { _fileFailed = true; }
                    }
                }
            }
            catch { /* Research must not replace the original operation/outcome. */ }
        }

        private static string Clean(string value)
        {
            if (value == null) return string.Empty;
            if (value.Length > 4096) value = value.Substring(0, 4096);
            return value.Replace('\r', ' ').Replace('\n', ' ').Replace('|', '/');
        }
    }
}
