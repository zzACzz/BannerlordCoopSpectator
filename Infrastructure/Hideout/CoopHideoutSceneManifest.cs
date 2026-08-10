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

        public string SpawnGroupTag { get; set; } = string.Empty;

        public bool HasTorchTag { get; set; }
    }

    public sealed class CoopHideoutSceneFrameManifest
    {
        public float PositionX { get; set; }

        public float PositionY { get; set; }

        public float PositionZ { get; set; }

        public float YawRadians { get; set; }
    }

    public sealed class CoopHideoutStealthAreaMarkerManifest
    {
        public string ReinforcementAllyGroupId { get; set; } = string.Empty;

        public float AreaRadius { get; set; }

        public CoopHideoutSceneFrameManifest MarkerFrame { get; set; }

        public CoopHideoutSceneFrameManifest ReinforcementSpawnFrame { get; set; }

        public CoopHideoutSceneFrameManifest WaitFrame { get; set; }

        public bool Contains(float x, float y)
        {
            if (MarkerFrame == null || AreaRadius <= 0f)
                return false;

            float deltaX = x - MarkerFrame.PositionX;
            float deltaY = y - MarkerFrame.PositionY;
            return deltaX * deltaX + deltaY * deltaY <= AreaRadius * AreaRadius;
        }
    }

    public sealed class CoopHideoutPatrolAreaManifest
    {
        public float PositionX { get; set; }

        public float PositionY { get; set; }

        public float PositionZ { get; set; }

        public IReadOnlyList<CoopHideoutPatrolPointManifest> PatrolPoints { get; set; } =
            Array.Empty<CoopHideoutPatrolPointManifest>();
    }

    public sealed class CoopHideoutBossFightManifest
    {
        public CoopHideoutSceneFrameManifest Frame { get; set; }

        public float InnerRadius { get; set; } = 2.5f;

        public float OuterRadius { get; set; } = 6f;

        public float WalkDistance { get; set; } = 3f;
    }

    public sealed class CoopHideoutSceneManifest
    {
        private const string DynamicPatrolAreaEntityName = "dynamic_patrol_area";
        private const string PatrolPointEntityName = "patrol_point";
        private const string PatrolPointScriptName = "PatrolPoint";
        private const string StealthAreaUsePointEntityName = "stealth_area_use_point";
        private const string StealthAreaMarkerEntityName = "stealth_area_marker";
        private const string StealthAreaMarkerScriptName = "StealthAreaMarker";
        private const string ReinforcementSpawnPointTag = "reinforcement_ally_group_spawn_point_tag";
        private const string ReinforcementWaitPointTag = "wait_point_tag";
        private const string TorchTag = "torch";
        private const string BossFightBehaviorScriptName = "HideoutBossFightBehavior";

        public string SceneName { get; set; }

        public string SourcePath { get; set; }

        public IReadOnlyList<CoopHideoutPatrolAreaManifest> PatrolAreas { get; set; } =
            Array.Empty<CoopHideoutPatrolAreaManifest>();

        public CoopHideoutSceneFrameManifest StealthAreaUsePointFrame { get; set; }

        public IReadOnlyList<CoopHideoutStealthAreaMarkerManifest> StealthAreaMarkers { get; set; } =
            Array.Empty<CoopHideoutStealthAreaMarkerManifest>();

        public CoopHideoutSceneFrameManifest CallTroopsCameraFrame { get; set; }

        public CoopHideoutSceneFrameManifest CallTroopsArrowBarrelFrame { get; set; }

        public CoopHideoutSceneFrameManifest CallTroopsArrowPathFrame { get; set; }

        public CoopHideoutBossFightManifest BossFight { get; set; }

        public int PatrolPointCount => PatrolAreas.Sum(area => area?.PatrolPoints?.Count ?? 0);

        public int IdleActionCount => PatrolAreas.Sum(area =>
            area?.PatrolPoints?.Count(point => !string.IsNullOrWhiteSpace(point?.LoopAction)) ?? 0);

        public bool HasNightAmbushContract =>
            StealthAreaUsePointFrame != null &&
            StealthAreaMarkers.Count > 0 &&
            StealthAreaMarkers.All(marker =>
                marker?.MarkerFrame != null &&
                marker.ReinforcementSpawnFrame != null &&
                marker.WaitFrame != null);

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
                        LoopAction = ReadString(variablesNode, "LoopAction"),
                        SpawnGroupTag = ReadString(variablesNode, "SpawnGroupTag"),
                        HasTorchTag = HasTagInAncestorChain(pointNode, TorchTag)
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

            CoopHideoutSceneFrameManifest usePointFrame = null;
            var stealthAreaMarkers = new List<CoopHideoutStealthAreaMarkerManifest>();
            XmlNode usePointNode = document.SelectSingleNode(
                "//game_entity[@name='" + StealthAreaUsePointEntityName + "']");
            if (TryParseLocalFrame(usePointNode, out usePointFrame))
            {
                XmlNodeList markerNodes = usePointNode.SelectNodes(
                    ".//game_entity[@name='" + StealthAreaMarkerEntityName + "']");
                foreach (XmlNode markerNode in markerNodes ?? EmptyNodeList.Instance)
                {
                    if (!TryParseLocalFrame(markerNode, out CoopHideoutSceneFrameManifest markerLocalFrame))
                        continue;

                    CoopHideoutSceneFrameManifest markerFrame = ComposeFrame(
                        usePointFrame,
                        markerLocalFrame);
                    XmlNode markerVariables = markerNode.SelectSingleNode(
                        "./scripts/script[@name='" + StealthAreaMarkerScriptName + "']/variables");
                    XmlNode reinforcementNode = markerNode.SelectSingleNode(
                        ".//game_entity[tags/tag[@name='" + ReinforcementSpawnPointTag + "']] | " +
                        ".//game_entity[@name='" + ReinforcementSpawnPointTag + "']");
                    XmlNode waitNode = markerNode.SelectSingleNode(
                        ".//game_entity[tags/tag[@name='" + ReinforcementWaitPointTag + "']] | " +
                        ".//game_entity[@name='" + ReinforcementWaitPointTag + "']");
                    TryParseLocalFrame(
                        reinforcementNode,
                        out CoopHideoutSceneFrameManifest reinforcementLocalFrame);
                    TryParseLocalFrame(waitNode, out CoopHideoutSceneFrameManifest waitLocalFrame);

                    stealthAreaMarkers.Add(new CoopHideoutStealthAreaMarkerManifest
                    {
                        ReinforcementAllyGroupId = ReadString(
                            markerVariables,
                            "ReinforcementAllyGroupId"),
                        AreaRadius = Math.Max(0f, ReadFloat(markerVariables, "AreaRadius", 0f)),
                        MarkerFrame = markerFrame,
                        ReinforcementSpawnFrame = reinforcementLocalFrame == null
                            ? null
                            : ComposeFrame(markerFrame, reinforcementLocalFrame),
                        WaitFrame = waitLocalFrame == null
                            ? null
                            : ComposeFrame(markerFrame, waitLocalFrame)
                    });
                }
            }

            TryParseTaggedGlobalFrame(
                document,
                CoopHideoutAmbushContract.CallTroopsCameraTag,
                out CoopHideoutSceneFrameManifest callTroopsCameraFrame);
            TryParseTaggedGlobalFrame(
                document,
                CoopHideoutAmbushContract.CallTroopsArrowBarrelTag,
                out CoopHideoutSceneFrameManifest callTroopsArrowBarrelFrame);
            TryParseTaggedGlobalFrame(
                document,
                CoopHideoutAmbushContract.CallTroopsArrowPathTag,
                out CoopHideoutSceneFrameManifest callTroopsArrowPathFrame);

            CoopHideoutBossFightManifest bossFight = null;
            XmlNode bossFightNode = document.SelectSingleNode(
                "//game_entity[@prefab='" + CoopHideoutBossPhaseContract.BossFightEntityTag + "'] | " +
                "//game_entity[@name='" + CoopHideoutBossPhaseContract.BossFightEntityTag + "'] | " +
                "//game_entity[tags/tag[@name='" + CoopHideoutBossPhaseContract.BossFightEntityTag + "']]");
            if (TryParseGlobalFrame(
                    bossFightNode,
                    out CoopHideoutSceneFrameManifest bossFightFrame))
            {
                XmlNode bossFightVariables = bossFightNode.SelectSingleNode(
                    "./scripts/script[@name='" + BossFightBehaviorScriptName + "']/variables");
                bossFight = new CoopHideoutBossFightManifest
                {
                    Frame = bossFightFrame,
                    InnerRadius = Math.Max(
                        0.1f,
                        ReadFloat(bossFightVariables, "InnerRadius", 2.5f)),
                    OuterRadius = Math.Max(
                        0.1f,
                        ReadFloat(bossFightVariables, "OuterRadius", 6f)),
                    WalkDistance = Math.Max(
                        0f,
                        ReadFloat(bossFightVariables, "WalkDistance", 3f))
                };
            }

            manifest = new CoopHideoutSceneManifest
            {
                SceneName = (sceneName ?? string.Empty).Trim(),
                SourcePath = sourcePath,
                PatrolAreas = patrolAreas.ToArray(),
                StealthAreaUsePointFrame = usePointFrame,
                StealthAreaMarkers = stealthAreaMarkers.ToArray(),
                CallTroopsCameraFrame = callTroopsCameraFrame,
                CallTroopsArrowBarrelFrame = callTroopsArrowBarrelFrame,
                CallTroopsArrowPathFrame = callTroopsArrowPathFrame,
                BossFight = bossFight
            };
            diagnostics =
                "loaded areas=" + manifest.PatrolAreas.Count +
                " points=" + manifest.PatrolPointCount +
                " idleActions=" + manifest.IdleActionCount +
                " torchPoints=" + manifest.PatrolAreas.Sum(area =>
                    area?.PatrolPoints?.Count(point => point?.HasTorchTag == true) ?? 0) +
                " stealthMarkers=" + manifest.StealthAreaMarkers.Count +
                " cinematicResources=" +
                (manifest.CallTroopsCameraFrame != null) + "/" +
                (manifest.CallTroopsArrowBarrelFrame != null) + "/" +
                (manifest.CallTroopsArrowPathFrame != null) +
                " bossFight=" +
                (manifest.BossFight != null
                    ? manifest.BossFight.InnerRadius + "/" +
                      manifest.BossFight.OuterRadius + "/" +
                      manifest.BossFight.WalkDistance
                    : "missing") +
                " nightContract=" + manifest.HasNightAmbushContract;
            return true;
        }

        private static bool HasTag(XmlNode entityNode, string expectedTag)
        {
            if (entityNode == null || string.IsNullOrWhiteSpace(expectedTag))
                return false;

            XmlNodeList tags = entityNode.SelectNodes("./tags/tag");
            foreach (XmlNode tag in tags ?? EmptyNodeList.Instance)
            {
                if (string.Equals(
                        tag?.Attributes?["name"]?.Value,
                        expectedTag,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasTagInAncestorChain(
            XmlNode entityNode,
            string expectedTag)
        {
            XmlNode current = entityNode;
            while (current != null)
            {
                if (string.Equals(
                        current.Name,
                        "game_entity",
                        StringComparison.OrdinalIgnoreCase) &&
                    HasTag(current, expectedTag))
                {
                    return true;
                }

                current = current.ParentNode;
            }

            return false;
        }

        private static bool TryParseTaggedGlobalFrame(
            XmlDocument document,
            string tag,
            out CoopHideoutSceneFrameManifest frame)
        {
            frame = null;
            XmlNode entity = document?.SelectSingleNode(
                "//game_entity[tags/tag[@name='" + tag + "']]");
            if (entity == null)
                return false;

            return TryParseGlobalFrame(entity, out frame);
        }

        private static bool TryParseGlobalFrame(
            XmlNode entity,
            out CoopHideoutSceneFrameManifest frame)
        {
            frame = null;
            if (entity == null)
                return false;

            var chain = new Stack<CoopHideoutSceneFrameManifest>();
            XmlNode current = entity;
            while (current != null)
            {
                if (string.Equals(current.Name, "game_entity", StringComparison.OrdinalIgnoreCase) &&
                    TryParseLocalFrame(current, out CoopHideoutSceneFrameManifest localFrame))
                {
                    chain.Push(localFrame);
                }
                current = current.ParentNode;
            }

            while (chain.Count > 0)
                frame = frame == null ? chain.Pop() : ComposeFrame(frame, chain.Pop());
            return frame != null;
        }

        private static bool TryParseLocalFrame(
            XmlNode entityNode,
            out CoopHideoutSceneFrameManifest frame)
        {
            frame = null;
            XmlNode transformNode = entityNode?.SelectSingleNode("./transform");
            if (!TryParsePosition(
                    transformNode?.Attributes?["position"]?.Value,
                    out float x,
                    out float y,
                    out float z))
            {
                return false;
            }

            float yaw = 0f;
            string[] rotationParts =
                (transformNode?.Attributes?["rotation_euler"]?.Value ?? string.Empty)
                .Split(',');
            if (rotationParts.Length >= 3)
            {
                float.TryParse(
                    rotationParts[2].Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out yaw);
            }

            frame = new CoopHideoutSceneFrameManifest
            {
                PositionX = x,
                PositionY = y,
                PositionZ = z,
                YawRadians = yaw
            };
            return true;
        }

        private static CoopHideoutSceneFrameManifest ComposeFrame(
            CoopHideoutSceneFrameManifest parent,
            CoopHideoutSceneFrameManifest local)
        {
            if (parent == null)
                return local;
            if (local == null)
                return null;

            double cosine = Math.Cos(parent.YawRadians);
            double sine = Math.Sin(parent.YawRadians);
            return new CoopHideoutSceneFrameManifest
            {
                PositionX = parent.PositionX +
                            (float)(local.PositionX * cosine - local.PositionY * sine),
                PositionY = parent.PositionY +
                            (float)(local.PositionX * sine + local.PositionY * cosine),
                PositionZ = parent.PositionZ + local.PositionZ,
                YawRadians = parent.YawRadians + local.YawRadians
            };
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
