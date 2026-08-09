using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;

namespace CoopSpectator.Infrastructure.Hideout
{
    public sealed class CoopHideoutPatrolPointManifest
    {
        public int Index { get; set; }

        public int WaitDurationSeconds { get; set; } = 1;

        public int WaitDeviationSeconds { get; set; }

        public bool IsInfiniteWaitPoint { get; set; }

        public float PatrollingSpeed { get; set; } = -1f;

        public string LoopAction { get; set; } = string.Empty;
    }

    public sealed class CoopHideoutPatrolAreaManifest
    {
        public float PositionX { get; set; }

        public float PositionY { get; set; }

        public float PositionZ { get; set; }

        public IReadOnlyList<CoopHideoutPatrolPointManifest> PatrolPoints { get; set; } =
            Array.Empty<CoopHideoutPatrolPointManifest>();
    }

    public sealed class CoopHideoutSceneManifest
    {
        private const string DynamicPatrolAreaEntityName = "dynamic_patrol_area";
        private const string PatrolPointEntityName = "patrol_point";
        private const string PatrolPointScriptName = "PatrolPoint";

        public string SceneName { get; set; }

        public string SourcePath { get; set; }

        public IReadOnlyList<CoopHideoutPatrolAreaManifest> PatrolAreas { get; set; } =
            Array.Empty<CoopHideoutPatrolAreaManifest>();

        public int PatrolPointCount => PatrolAreas.Sum(area => area?.PatrolPoints?.Count ?? 0);

        public int IdleActionCount => PatrolAreas.Sum(area =>
            area?.PatrolPoints?.Count(point => !string.IsNullOrWhiteSpace(point?.LoopAction)) ?? 0);

        public static bool TryLoad(
            string path,
            string sceneName,
            out CoopHideoutSceneManifest manifest,
            out string diagnostics)
        {
            manifest = null;
            diagnostics = null;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                diagnostics = "scene-manifest-file-missing";
                return false;
            }

            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    IgnoreComments = true,
                    IgnoreWhitespace = true,
                    XmlResolver = null
                };
                var document = new XmlDocument { XmlResolver = null };
                using (XmlReader reader = XmlReader.Create(path, settings))
                    document.Load(reader);

                return TryParseDocument(document, sceneName, path, out manifest, out diagnostics);
            }
            catch (Exception ex)
            {
                diagnostics = "scene-manifest-load-failed:" + ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        public static bool TryParse(
            string xml,
            string sceneName,
            out CoopHideoutSceneManifest manifest,
            out string diagnostics)
        {
            manifest = null;
            diagnostics = null;
            if (string.IsNullOrWhiteSpace(xml))
            {
                diagnostics = "scene-manifest-xml-empty";
                return false;
            }

            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    IgnoreComments = true,
                    IgnoreWhitespace = true,
                    XmlResolver = null
                };
                var document = new XmlDocument { XmlResolver = null };
                using (var stringReader = new StringReader(xml))
                using (XmlReader reader = XmlReader.Create(stringReader, settings))
                    document.Load(reader);

                return TryParseDocument(document, sceneName, "in-memory", out manifest, out diagnostics);
            }
            catch (Exception ex)
            {
                diagnostics = "scene-manifest-parse-failed:" + ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        private static bool TryParseDocument(
            XmlDocument document,
            string sceneName,
            string sourcePath,
            out CoopHideoutSceneManifest manifest,
            out string diagnostics)
        {
            manifest = null;
            diagnostics = null;
            if (document?.DocumentElement == null)
            {
                diagnostics = "scene-manifest-document-empty";
                return false;
            }

            var patrolAreas = new List<CoopHideoutPatrolAreaManifest>();
            XmlNodeList areaNodes = document.SelectNodes(
                "//game_entity[@name='" + DynamicPatrolAreaEntityName + "']");
            foreach (XmlNode areaNode in areaNodes ?? EmptyNodeList.Instance)
            {
                XmlNode transformNode = areaNode.SelectSingleNode("./transform");
                if (!TryParsePosition(
                        transformNode?.Attributes?["position"]?.Value,
                        out float x,
                        out float y,
                        out float z))
                {
                    continue;
                }

                var patrolPoints = new List<CoopHideoutPatrolPointManifest>();
                XmlNodeList pointNodes = areaNode.SelectNodes(
                    ".//game_entity[@name='" + PatrolPointEntityName + "']");
                int fallbackIndex = 0;
                foreach (XmlNode pointNode in pointNodes ?? EmptyNodeList.Instance)
                {
                    XmlNode variablesNode = pointNode.SelectSingleNode(
                        "./scripts/script[@name='" + PatrolPointScriptName + "']/variables");
                    if (variablesNode == null)
                        continue;

                    var point = new CoopHideoutPatrolPointManifest
                    {
                        Index = ReadInt(variablesNode, "Index", fallbackIndex),
                        WaitDurationSeconds = Math.Max(0, ReadInt(variablesNode, "WaitDuration", 1)),
                        WaitDeviationSeconds = Math.Max(0, ReadInt(variablesNode, "WaitDeviation", 0)),
                        IsInfiniteWaitPoint = ReadBool(variablesNode, "IsInfiniteWaitPoint", false),
                        PatrollingSpeed = ReadFloat(variablesNode, "PatrollingSpeed", -1f),
                        LoopAction = ReadString(variablesNode, "LoopAction")
                    };
                    patrolPoints.Add(point);
                    fallbackIndex++;
                }

                patrolAreas.Add(new CoopHideoutPatrolAreaManifest
                {
                    PositionX = x,
                    PositionY = y,
                    PositionZ = z,
                    PatrolPoints = patrolPoints
                        .OrderBy(point => point.Index)
                        .ToArray()
                });
            }

            if (patrolAreas.Count == 0)
            {
                diagnostics = "scene-manifest-dynamic-patrol-areas-missing";
                return false;
            }

            manifest = new CoopHideoutSceneManifest
            {
                SceneName = (sceneName ?? string.Empty).Trim(),
                SourcePath = sourcePath,
                PatrolAreas = patrolAreas.ToArray()
            };
            diagnostics =
                "loaded areas=" + manifest.PatrolAreas.Count +
                " points=" + manifest.PatrolPointCount +
                " idleActions=" + manifest.IdleActionCount;
            return true;
        }

        private static bool TryParsePosition(
            string rawPosition,
            out float x,
            out float y,
            out float z)
        {
            x = 0f;
            y = 0f;
            z = 0f;
            string[] parts = (rawPosition ?? string.Empty).Split(',');
            return parts.Length >= 3 &&
                   float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
                   float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out y) &&
                   float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out z);
        }

        private static string ReadString(XmlNode variablesNode, string name)
        {
            XmlNode variable = variablesNode?.SelectSingleNode("./variable[@name='" + name + "']");
            return variable?.Attributes?["value"]?.Value ?? string.Empty;
        }

        private static int ReadInt(XmlNode variablesNode, string name, int fallback)
        {
            return int.TryParse(
                ReadString(variablesNode, name),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value)
                ? value
                : fallback;
        }

        private static float ReadFloat(XmlNode variablesNode, string name, float fallback)
        {
            return float.TryParse(
                ReadString(variablesNode, name),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float value)
                ? value
                : fallback;
        }

        private static bool ReadBool(XmlNode variablesNode, string name, bool fallback)
        {
            return bool.TryParse(ReadString(variablesNode, name), out bool value)
                ? value
                : fallback;
        }

        private sealed class EmptyNodeList : XmlNodeList
        {
            internal static readonly EmptyNodeList Instance = new EmptyNodeList();

            public override int Count => 0;

            public override System.Collections.IEnumerator GetEnumerator()
            {
                yield break;
            }

            public override XmlNode Item(int index)
            {
                return null;
            }
        }
    }
}
