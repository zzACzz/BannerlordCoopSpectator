using System;
using CoopSpectator.Campaign;
using CoopSpectator.Infrastructure.LordsHall;
using CoopSpectator.Infrastructure.SallyOut;
using CoopSpectator.Infrastructure.SiegeAmbush;
using CoopSpectator.Network.Messages;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    /// <summary>
    /// Shared helper that copies campaign encounter patch context into a mission
    /// initializer record for battle-map runtime scenes, regardless of whether
    /// startup currently goes through CoopBattle or stable vanilla Battle.
    /// </summary>
    public static class CampaignMapPatchMissionInit
    {
        private static readonly FieldInfo MissionInitializerRecordBackingField =
            typeof(Mission).GetField("<InitializerRecord>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly PropertyInfo MissionInitializerRecordProperty =
            typeof(Mission).GetProperty("InitializerRecord", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo BattleSpawnPathSelectorField =
            typeof(Mission).GetField("_battleSpawnPathSelector", BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool TryApplyCampaignAtmosphereToLiveScene(Mission mission, string logSource)
        {
            if (mission?.Scene == null ||
                !SceneRuntimeClassifier.IsExactSiegeAssaultWithDeploymentScene(mission.SceneName ?? string.Empty))
            {
                return false;
            }

            if (GameNetwork.IsServer && !GameNetwork.IsClient)
                return false;

            string source = string.IsNullOrWhiteSpace(logSource)
                ? "CampaignMapPatchMissionInit.Atmosphere"
                : logSource;
            BattleSnapshotMessage snapshot = TryResolveSnapshot(source);
            if (snapshot == null ||
                !ExactCampaignSiegeAssaultWithDeploymentRuntime.IsExactSiegeWithDeploymentScenario(snapshot.ScenarioContext))
            {
                return false;
            }

            CampaignAtmosphereSnapshotMessage atmosphereSnapshot = snapshot.CampaignAtmosphere;
            if (atmosphereSnapshot == null)
                return TryApplyCampaignTimeOfDayFallbackToLiveScene(mission, snapshot, source);

            try
            {
                AtmosphereInfo atmosphere = CreateCampaignAtmosphereInfo(atmosphereSnapshot);
                float previousTimeOfDay = mission.Scene.TimeOfDay;
                if (TryGetMissionInitializerRecord(mission, out MissionInitializerRecord initializerRecord) &&
                    AreCampaignAtmospheresEquivalent(initializerRecord.AtmosphereOnCampaign, atmosphere) &&
                    IsCampaignTimeOfDayWithinTolerance(
                        previousTimeOfDay,
                        atmosphere.TimeInfo.TimeOfDay,
                        0.51f))
                {
                    ModLogger.Info(
                        "CampaignMapPatchMissionInit: skipped late campaign atmosphere replay because mission initializer already applied it. " +
                        "Scene=" + (mission.SceneName ?? "null") +
                        " TimeOfDay=" + previousTimeOfDay.ToString("0.###") +
                        " AtmosphereName=" + (atmosphere.AtmosphereName ?? "null") +
                        " InterpolatedAtmosphereName=" + (atmosphere.InterpolatedAtmosphereName ?? "null") +
                        " SnapshotSource=" + (atmosphereSnapshot.Source ?? "unknown") +
                        " Source=" + source + ".");
                    return true;
                }

                string atmosphereName = atmosphere.AtmosphereName;
                if (!string.IsNullOrWhiteSpace(atmosphereName))
                    mission.Scene.SetAtmosphereWithName(atmosphereName);

                mission.Scene.TimeOfDay = atmosphere.TimeInfo.TimeOfDay;
                mission.Scene.SetWinterTimeFactor(atmosphere.TimeInfo.WinterTimeFactor);
                mission.Scene.SetDrynessFactor(atmosphere.TimeInfo.DrynessFactor);
                mission.Scene.SetTemperature(atmosphere.AreaInfo.Temperature);
                mission.Scene.SetHumidity(atmosphere.AreaInfo.Humidity);
                mission.Scene.SetRainDensity(atmosphere.RainInfo.Density);
                mission.Scene.SetSnowDensity(atmosphere.SnowInfo.Density);
                mission.Scene.SetGlobalWindStrengthVector(atmosphere.NauticalInfo.WindVector);

                ModLogger.Info(
                    "CampaignMapPatchMissionInit: applied reduced late campaign atmosphere fallback to live exact siege scene. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " PreviousTimeOfDay=" + previousTimeOfDay.ToString("0.###") +
                    " TimeOfDay=" + atmosphere.TimeInfo.TimeOfDay.ToString("0.###") +
                    " AtmosphereName=" + (atmosphere.AtmosphereName ?? "null") +
                    " InterpolatedAtmosphereName=" + (atmosphere.InterpolatedAtmosphereName ?? "null") +
                    " SnapshotSource=" + (atmosphereSnapshot.Source ?? "unknown") +
                    " Source=" + source + ".");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CampaignMapPatchMissionInit: failed to apply campaign atmosphere to live exact siege scene. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " TimeOfDay=" + atmosphereSnapshot.TimeOfDay.ToString("0.###") +
                    " Message=" + ex.Message +
                    " Source=" + source + ".");
                return false;
            }
        }

        private static bool TryApplyCampaignTimeOfDayFallbackToLiveScene(
            Mission mission,
            BattleSnapshotMessage snapshot,
            string source)
        {
            if (snapshot?.HasCampaignTimeOfDay != true ||
                !TryNormalizeCampaignTimeOfDay(snapshot.CampaignTimeOfDay, out float targetTimeOfDay))
            {
                return false;
            }

            try
            {
                float previousTimeOfDay = mission.Scene.TimeOfDay;
                mission.Scene.TimeOfDay = targetTimeOfDay;
                ModLogger.Info(
                    "CampaignMapPatchMissionInit: applied campaign time-of-day fallback to exact siege scene. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " PreviousTimeOfDay=" + previousTimeOfDay.ToString("0.###") +
                    " TimeOfDay=" + targetTimeOfDay.ToString("0.###") +
                    " SnapshotSource=" + (snapshot.CampaignTimeOfDaySource ?? "unknown") +
                    " Source=" + source + ".");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CampaignMapPatchMissionInit: failed to apply campaign time-of-day fallback to exact siege scene. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " TimeOfDay=" + targetTimeOfDay.ToString("0.###") +
                    " Message=" + ex.Message +
                    " Source=" + source + ".");
                return false;
            }
        }

        private static bool TryNormalizeCampaignTimeOfDay(float value, out float normalized)
        {
            normalized = -1f;
            if (float.IsNaN(value) || float.IsInfinity(value))
                return false;

            normalized = value % 24f;
            if (normalized < 0f)
                normalized += 24f;
            return true;
        }

        public static void TryApply(ref MissionInitializerRecord record, string runtimeScene, string logSource)
        {
            string source = string.IsNullOrWhiteSpace(logSource) ? "CampaignMapPatchMissionInit" : logSource;
            TryPrimeEarlySnapshotFromLocalRoster(runtimeScene, source + " pre-classify");
            if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(runtimeScene))
                return;

            BattleMapContractDiagnostics.LogMissionInitializerRecordState(record, source + " pre-apply");
            ApplyVillageBattleSceneContext(ref record, runtimeScene, source);
            BattleSnapshotMessage snapshot = TryResolveSnapshot(source);
            if (snapshot == null)
            {
                ModLogger.Info(source + ": skipped campaign map patch context (battle snapshot missing).");
                BattleMapContractDiagnostics.LogMissionInitializerRecordState(record, source + " skipped-missing-snapshot");
                return;
            }

            ApplyCampaignDifficultyContext(ref record, snapshot, runtimeScene, source);
            ApplySiegeSceneLevelContext(ref record, snapshot, runtimeScene, source);

            if (IsSiegeScenario(snapshot))
            {
                string siegeSubtype = snapshot?.ScenarioContext?.SiegeContext?.SiegeSubtype ?? string.Empty;
                if (ExactCampaignSiegeAssaultWithDeploymentRuntime
                    .IsExactSiegeWithDeploymentScenario(
                        snapshot.ScenarioContext))
                {
                    ApplyCampaignAtmosphereContext(ref record, snapshot, runtimeScene, source);
                    ApplyOpenSiegeAssaultSceneProfile(ref record, snapshot, runtimeScene, source);
                    record.SceneHasMapPatch = false;
                    record.PatchCoordinates = new Vec2(0f, 0f);
                    record.PatchEncounterDir = new Vec2(0f, 0f);
                    ModLogger.Info(
                        source + ": skipped campaign map patch context for open siege assault runtime. " +
                        "RuntimeScene=" + (runtimeScene ?? "unknown") +
                        " SiegeSubtype=" + (string.IsNullOrWhiteSpace(siegeSubtype) ? "unknown" : siegeSubtype) +
                        " SceneLevels=" + (record.SceneLevels ?? "null") + ".");
                    BattleMapContractDiagnostics.LogMissionInitializerRecordState(record, source + " skipped-open-siege-assault-map-patch");
                    return;
                }

                if (LordsHallScenarioContract.IsLordsHallScenario(snapshot.ScenarioContext) ||
                    string.Equals(siegeSubtype, "Blockade", StringComparison.OrdinalIgnoreCase))
                {
                    ModLogger.Info(
                        source + ": skipped campaign map patch context for closed siege runtime. " +
                        "RuntimeScene=" + (runtimeScene ?? "unknown") +
                        " SiegeSubtype=" + (string.IsNullOrWhiteSpace(siegeSubtype) ? "unknown" : siegeSubtype) + ".");
                    BattleMapContractDiagnostics.LogMissionInitializerRecordState(record, source + " skipped-closed-siege-map-patch");
                    return;
                }

                ModLogger.Info(
                    source + ": enabling campaign map patch context for siege runtime. " +
                    "RuntimeScene=" + (runtimeScene ?? "unknown") +
                    " SiegeSubtype=" + (string.IsNullOrWhiteSpace(siegeSubtype) ? "unknown" : siegeSubtype) + ".");
            }

            if (SceneRuntimeClassifier.IsVillageBattleScene(runtimeScene))
            {
                ModLogger.Info(
                    source + ": skipped campaign map patch context for village battle runtime. " +
                    "RuntimeScene=" + (runtimeScene ?? "unknown") +
                    " SceneLevels=" + (record.SceneLevels ?? "null") + ".");
                BattleMapContractDiagnostics.LogMissionInitializerRecordState(record, source + " skipped-village-map-patch");
                return;
            }

            if (snapshot.MapPatchSceneIndex < 0)
            {
                ModLogger.Info(
                    source + ": skipped campaign map patch context (MapPatchSceneIndex missing). " +
                    "RuntimeScene=" + (runtimeScene ?? "unknown") + ".");
                BattleMapContractDiagnostics.LogMissionInitializerRecordState(record, source + " skipped-missing-scene-index");
                return;
            }

            if (!snapshot.HasPatchEncounterDirection)
            {
                ModLogger.Info(
                    source + ": skipped campaign map patch context (PatchEncounterDirection missing). " +
                    "RuntimeScene=" + (runtimeScene ?? "unknown") +
                    " MapPatchSceneIndex=" + snapshot.MapPatchSceneIndex + ".");
                BattleMapContractDiagnostics.LogMissionInitializerRecordState(record, source + " skipped-missing-direction");
                return;
            }

            float dirX = snapshot.PatchEncounterDirX;
            float dirY = snapshot.PatchEncounterDirY;
            double directionLength = Math.Sqrt(dirX * dirX + dirY * dirY);
            if (directionLength <= 0.001d)
            {
                ModLogger.Info(
                    source + ": skipped campaign map patch context (PatchEncounterDirection too small). " +
                    "RuntimeScene=" + (runtimeScene ?? "unknown") +
                    " MapPatchSceneIndex=" + snapshot.MapPatchSceneIndex +
                    " PatchEncounterDir=(" + dirX.ToString("0.###") + ", " + dirY.ToString("0.###") + ").");
                BattleMapContractDiagnostics.LogMissionInitializerRecordState(record, source + " skipped-small-direction");
                return;
            }

            record.PlayingInCampaignMode = false;
            record.SceneHasMapPatch = true;
            record.PatchCoordinates = new Vec2(
                Clamp01(snapshot.MapPatchNormalizedX),
                Clamp01(snapshot.MapPatchNormalizedY));
            record.PatchEncounterDir = new Vec2(
                (float)(dirX / directionLength),
                (float)(dirY / directionLength));

            ModLogger.Info(
                source + ": applied campaign map patch context. " +
                "RuntimeScene=" + (runtimeScene ?? "unknown") +
                " WorldMapScene=" + (snapshot.WorldMapScene ?? "unknown") +
                " MapPatchSceneIndex=" + snapshot.MapPatchSceneIndex +
                " PatchCoordinates=(" + record.PatchCoordinates.x.ToString("0.###") + ", " + record.PatchCoordinates.y.ToString("0.###") + ")" +
                " PatchEncounterDir=(" + record.PatchEncounterDir.x.ToString("0.###") + ", " + record.PatchEncounterDir.y.ToString("0.###") + ")" +
                " DirectionSource=" + (snapshot.PatchEncounterDirectionSource ?? "unknown") + ".");
            BattleMapContractDiagnostics.LogMissionInitializerRecordState(record, source + " post-apply");
        }

        private static void ApplyCampaignAtmosphereContext(
            ref MissionInitializerRecord record,
            BattleSnapshotMessage snapshot,
            string runtimeScene,
            string source)
        {
            CampaignAtmosphereSnapshotMessage atmosphereSnapshot = snapshot?.CampaignAtmosphere;
            if (atmosphereSnapshot == null ||
                !ExactCampaignSiegeAssaultWithDeploymentRuntime.IsExactSiegeWithDeploymentScenario(snapshot?.ScenarioContext))
            {
                return;
            }

            AtmosphereInfo atmosphere = CreateCampaignAtmosphereInfo(atmosphereSnapshot);
            if (TryNormalizeCampaignTimeOfDay(atmosphere.TimeInfo.TimeOfDay, out float normalizedTimeOfDay))
                atmosphere.TimeInfo.TimeOfDay = normalizedTimeOfDay;

            AtmosphereInfo previousAtmosphere = record.AtmosphereOnCampaign;
            if (AreCampaignAtmospheresEquivalent(previousAtmosphere, atmosphere))
                return;

            record.AtmosphereOnCampaign = atmosphere;
            ModLogger.Info(
                source + ": applied full campaign atmosphere to exact siege mission initializer. " +
                "RuntimeScene=" + (runtimeScene ?? "unknown") +
                " PreviousTimeOfDay=" + previousAtmosphere.TimeInfo.TimeOfDay.ToString("0.###") +
                " TimeOfDay=" + atmosphere.TimeInfo.TimeOfDay.ToString("0.###") +
                " AtmosphereName=" + (atmosphere.AtmosphereName ?? "null") +
                " InterpolatedAtmosphereName=" + (atmosphere.InterpolatedAtmosphereName ?? "null") +
                " SunBrightness=" + atmosphere.SunInfo.Brightness.ToString("0.###") +
                " EnvironmentMultiplier=" + atmosphere.AmbientInfo.EnvironmentMultiplier.ToString("0.###") +
                " AtmosphereSource=" + (atmosphereSnapshot.Source ?? "unknown") + ".");
        }

        private static AtmosphereInfo CreateCampaignAtmosphereInfo(
            CampaignAtmosphereSnapshotMessage snapshot)
        {
            string atmosphereName = ResolveStandaloneSemiCloudyAtmosphereName(snapshot.TimeOfDay);

            return new AtmosphereInfo
            {
                Seed = snapshot.Seed,
                AtmosphereName = atmosphereName,
                InterpolatedAtmosphereName = snapshot.InterpolatedAtmosphereName,
                SunInfo = new SunInformation
                {
                    Altitude = snapshot.SunAltitude,
                    Angle = snapshot.SunAngle,
                    Color = new Vec3(snapshot.SunColorX, snapshot.SunColorY, snapshot.SunColorZ),
                    Brightness = snapshot.SunBrightness,
                    MaxBrightness = snapshot.SunMaxBrightness,
                    Size = snapshot.SunSize,
                    RayStrength = snapshot.SunRayStrength
                },
                RainInfo = new RainInformation
                {
                    Density = snapshot.RainDensity
                },
                SnowInfo = new SnowInformation
                {
                    Density = snapshot.SnowDensity
                },
                AmbientInfo = new AmbientInformation
                {
                    EnvironmentMultiplier = snapshot.AmbientEnvironmentMultiplier,
                    AmbientColor = new Vec3(snapshot.AmbientColorX, snapshot.AmbientColorY, snapshot.AmbientColorZ),
                    MieScatterStrength = snapshot.AmbientMieScatterStrength,
                    RayleighConstant = snapshot.AmbientRayleighConstant
                },
                FogInfo = new FogInformation
                {
                    Density = snapshot.FogDensity,
                    Color = new Vec3(snapshot.FogColorX, snapshot.FogColorY, snapshot.FogColorZ),
                    Falloff = snapshot.FogFalloff
                },
                SkyInfo = new SkyInformation
                {
                    Brightness = snapshot.SkyBrightness
                },
                NauticalInfo = new NauticalInformation
                {
                    WaveStrength = snapshot.NauticalWaveStrength,
                    WindVector = new Vec2(snapshot.NauticalWindX, snapshot.NauticalWindY),
                    CanUseLowAltitudeAtmosphere = snapshot.NauticalCanUseLowAltitudeAtmosphere,
                    UseSceneWindDirection = snapshot.NauticalUseSceneWindDirection,
                    IsRiverBattle = snapshot.NauticalIsRiverBattle,
                    IsInsideStorm = snapshot.NauticalIsInsideStorm,
                    UsesNavalSimulatedWater = snapshot.NauticalUsesNavalSimulatedWater
                },
                TimeInfo = new TimeInformation
                {
                    TimeOfDay = snapshot.TimeOfDay,
                    NightTimeFactor = snapshot.NightTimeFactor,
                    DrynessFactor = snapshot.DrynessFactor,
                    WinterTimeFactor = snapshot.WinterTimeFactor,
                    Season = snapshot.Season
                },
                AreaInfo = new AreaInformation
                {
                    Temperature = snapshot.AreaTemperature,
                    Humidity = snapshot.AreaHumidity
                },
                PostProInfo = new PostProcessInformation
                {
                    MinExposure = snapshot.PostProcessMinExposure,
                    MaxExposure = snapshot.PostProcessMaxExposure,
                    BrightpassThreshold = snapshot.PostProcessBrightpassThreshold,
                    MiddleGray = snapshot.PostProcessMiddleGray
                }
            };
        }

        private static bool AreCampaignAtmospheresEquivalent(
            AtmosphereInfo left,
            AtmosphereInfo right)
        {
            return string.Equals(left.AtmosphereName, right.AtmosphereName, StringComparison.Ordinal) &&
                   string.Equals(left.InterpolatedAtmosphereName, right.InterpolatedAtmosphereName, StringComparison.Ordinal) &&
                   Approximately(left.TimeInfo.TimeOfDay, right.TimeInfo.TimeOfDay) &&
                   Approximately(left.SunInfo.Brightness, right.SunInfo.Brightness) &&
                   Approximately(left.SunInfo.Altitude, right.SunInfo.Altitude) &&
                   Approximately(left.SkyInfo.Brightness, right.SkyInfo.Brightness) &&
                   Approximately(left.AmbientInfo.EnvironmentMultiplier, right.AmbientInfo.EnvironmentMultiplier) &&
                   Approximately(left.PostProInfo.MinExposure, right.PostProInfo.MinExposure) &&
                   Approximately(left.PostProInfo.MaxExposure, right.PostProInfo.MaxExposure);
        }

        private static bool Approximately(float left, float right)
        {
            return Math.Abs(left - right) <= 0.0001f;
        }

        private static bool IsCampaignTimeOfDayWithinTolerance(
            float left,
            float right,
            float tolerance)
        {
            if (!TryNormalizeCampaignTimeOfDay(left, out float normalizedLeft) ||
                !TryNormalizeCampaignTimeOfDay(right, out float normalizedRight))
            {
                return false;
            }

            float difference = Math.Abs(normalizedLeft - normalizedRight);
            difference = Math.Min(difference, 24f - difference);
            return difference <= Math.Max(0f, tolerance);
        }

        private static string ResolveStandaloneSemiCloudyAtmosphereName(float campaignTimeOfDay)
        {
            if (!TryNormalizeCampaignTimeOfDay(campaignTimeOfDay, out float normalizedTimeOfDay))
                normalizedTimeOfDay = 12f;

            int profileHour;
            if (normalizedTimeOfDay <= 12f)
            {
                profileHour = ClampStandaloneAtmosphereProfileHour(
                    (int)Math.Round(normalizedTimeOfDay, MidpointRounding.AwayFromZero));
            }
            else if (normalizedTimeOfDay <= 15f)
            {
                profileHour = InterpolateStandaloneAtmosphereProfileHour(
                    12,
                    4,
                    (normalizedTimeOfDay - 12f) / 3f);
            }
            else if (normalizedTimeOfDay <= 18f)
            {
                profileHour = InterpolateStandaloneAtmosphereProfileHour(
                    4,
                    3,
                    (normalizedTimeOfDay - 15f) / 3f);
            }
            else if (normalizedTimeOfDay <= 22f)
            {
                profileHour = InterpolateStandaloneAtmosphereProfileHour(
                    3,
                    1,
                    (normalizedTimeOfDay - 18f) / 4f);
            }
            else
            {
                profileHour = 1;
            }

            return "TOD_" + profileHour.ToString("00") + "_00_SemiCloudy";
        }

        private static int InterpolateStandaloneAtmosphereProfileHour(
            int startProfileHour,
            int endProfileHour,
            float amount)
        {
            float clampedAmount = Math.Max(0f, Math.Min(1f, amount));
            float interpolated = startProfileHour +
                                 (endProfileHour - startProfileHour) * clampedAmount;
            return ClampStandaloneAtmosphereProfileHour(
                (int)Math.Round(interpolated, MidpointRounding.AwayFromZero));
        }

        private static int ClampStandaloneAtmosphereProfileHour(int profileHour)
        {
            if (profileHour < 1)
                return 1;
            if (profileHour > 12)
                return 12;
            return profileHour;
        }

        private static void ApplyCampaignDifficultyContext(
            ref MissionInitializerRecord record,
            BattleSnapshotMessage snapshot,
            string runtimeScene,
            string source)
        {
            float playerTroopsReceivedDamageMultiplier = snapshot?.PlayerTroopsReceivedDamageMultiplier ?? 1f;
            if (playerTroopsReceivedDamageMultiplier <= 0f)
                playerTroopsReceivedDamageMultiplier = 1f;

            record.DamageToFriendsMultiplier = playerTroopsReceivedDamageMultiplier;
            record.DamageFromPlayerToFriendsMultiplier = playerTroopsReceivedDamageMultiplier;

            ModLogger.Info(
                source + ": applied campaign player-troops damage multiplier. " +
                "RuntimeScene=" + (runtimeScene ?? "unknown") +
                " Multiplier=" + playerTroopsReceivedDamageMultiplier.ToString("0.###") + ".");
        }

        private static void ApplyVillageBattleSceneContext(
            ref MissionInitializerRecord record,
            string runtimeScene,
            string source)
        {
            if (!SceneRuntimeClassifier.RequiresLandRaidSceneLevel(runtimeScene))
                return;

            if (string.Equals(record.SceneLevels, "land_raid", StringComparison.OrdinalIgnoreCase))
                return;

            record.SceneLevels = "land_raid";
            ModLogger.Info(
                source + ": applied village battle scene-level context. " +
                "RuntimeScene=" + (runtimeScene ?? "unknown") +
                " SceneLevels=" + (record.SceneLevels ?? "null") + ".");
        }

        private static void ApplySiegeSceneLevelContext(
            ref MissionInitializerRecord record,
            BattleSnapshotMessage snapshot,
            string runtimeScene,
            string source)
        {
            if (snapshot?.ScenarioContext?.IsSiegeBattle != true)
                return;

            string siegeSubtype = snapshot.ScenarioContext.SiegeContext?.SiegeSubtype ?? string.Empty;
            if (SallyOutScenarioContract.IsSallyOutScenario(snapshot.ScenarioContext))
            {
                BattleSiegeContextMessage sallyOutContext = snapshot.ScenarioContext.SiegeContext;
                if (sallyOutContext?.HasMissionInitializerRecord != true)
                    return;

                string sallyOutSceneLevels = sallyOutContext.MissionInitializerSceneLevels ?? string.Empty;
                if (string.Equals(record.SceneLevels, sallyOutSceneLevels, StringComparison.Ordinal))
                    return;

                string previousSceneLevels = record.SceneLevels;
                record.SceneLevels = sallyOutSceneLevels;
                ModLogger.Info(
                    source + ": restored native sally-out field scene-level context. " +
                    "RuntimeScene=" + (runtimeScene ?? "unknown") +
                    " PreviousSceneLevels=" + (previousSceneLevels ?? "null") +
                    " SceneLevels=" + (record.SceneLevels ?? "null") +
                    " ExactSource=" + (sallyOutContext.MissionInitializerSource ?? "unknown") + ".");
                return;
            }

            if (LordsHallScenarioContract.IsLordsHallScenario(snapshot.ScenarioContext))
            {
                BattleSiegeContextMessage lordsHallContext = snapshot.ScenarioContext.SiegeContext;
                string lordsHallExactSceneLevels = lordsHallContext?.MissionInitializerSceneLevels ?? string.Empty;
                string lordsHallDesiredSceneLevels = !string.IsNullOrWhiteSpace(lordsHallExactSceneLevels)
                    ? lordsHallExactSceneLevels
                    : LordsHallScenarioContract.MissionSceneLevels;
                if (string.Equals(record.SceneLevels, lordsHallDesiredSceneLevels, StringComparison.OrdinalIgnoreCase))
                    return;

                string previousSceneLevels = record.SceneLevels;
                record.SceneLevels = lordsHallDesiredSceneLevels;
                ModLogger.Info(
                    source + ": applied native lords-hall scene-level context. " +
                    "RuntimeScene=" + (runtimeScene ?? "unknown") +
                    " PreviousSceneLevels=" + (previousSceneLevels ?? "null") +
                    " SceneLevels=" + (record.SceneLevels ?? "null") +
                    " ExactSource=" + (lordsHallContext?.MissionInitializerSource ?? "fallback-native-contract") + ".");
                return;
            }

            if (string.Equals(siegeSubtype, "Blockade", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            BattleSiegeContextMessage siegeContext = snapshot.ScenarioContext.SiegeContext;
            string exactSceneLevels = siegeContext?.MissionInitializerSceneLevels ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(exactSceneLevels))
            {
                if (string.Equals(record.SceneLevels, exactSceneLevels, StringComparison.OrdinalIgnoreCase))
                    return;

                string previousSceneLevels = record.SceneLevels;
                record.SceneLevels = exactSceneLevels;
                ModLogger.Info(
                    source + ": applied exact siege mission initializer scene-level context. " +
                    "RuntimeScene=" + (runtimeScene ?? "unknown") +
                    " SiegeSubtype=" + (string.IsNullOrWhiteSpace(siegeSubtype) ? "unknown" : siegeSubtype) +
                    " PreviousSceneLevels=" + (previousSceneLevels ?? "null") +
                    " SceneLevels=" + (record.SceneLevels ?? "null") +
                    " ExactSource=" + (siegeContext?.MissionInitializerSource ?? "unknown") + ".");
                return;
            }

            int wallLevel = siegeContext?.WallLevel ?? 0;
            if (wallLevel < 1)
                wallLevel = 1;
            if (wallLevel > 3)
                wallLevel = 3;

            string desiredSceneLevels = "level_" + wallLevel + " siege";
            if (string.Equals(record.SceneLevels, desiredSceneLevels, StringComparison.OrdinalIgnoreCase))
                return;

            record.SceneLevels = desiredSceneLevels;
            ModLogger.Info(
                source + ": applied siege scene-level context. " +
                "RuntimeScene=" + (runtimeScene ?? "unknown") +
                " SiegeSubtype=" + (string.IsNullOrWhiteSpace(siegeSubtype) ? "unknown" : siegeSubtype) +
                " WallLevel=" + wallLevel +
                " SceneLevels=" + (record.SceneLevels ?? "null") + ".");
        }

        private static void ApplyOpenSiegeAssaultSceneProfile(
            ref MissionInitializerRecord record,
            BattleSnapshotMessage snapshot,
            string runtimeScene,
            string source)
        {
            if (!ExperimentalFeatures.EnableExactSiegeCampaignSceneInitializerProfile)
                return;

            if (!ExactCampaignSiegeAssaultWithDeploymentRuntime.IsExactSiegeWithDeploymentScenario(snapshot?.ScenarioContext))
                return;

            bool previousPlayingInCampaignMode = record.PlayingInCampaignMode;
            int previousDecalAtlasGroup = record.DecalAtlasGroup;
            int previousTerrainType = record.TerrainType;
            int previousSceneUpgradeLevel = record.SceneUpgradeLevel;
            BattleSiegeContextMessage siegeContext = snapshot?.ScenarioContext?.SiegeContext;
            bool hasExactInitializer = siegeContext?.HasMissionInitializerRecord == true;
            int targetDecalAtlasGroup = hasExactInitializer && siegeContext.MissionInitializerDecalAtlasGroup >= 0
                ? siegeContext.MissionInitializerDecalAtlasGroup
                : 3;
            int targetTerrainType = hasExactInitializer && siegeContext.MissionInitializerTerrainType >= 0
                ? siegeContext.MissionInitializerTerrainType
                : (int)TerrainType.Plain;
            int targetSceneUpgradeLevel = hasExactInitializer && siegeContext.MissionInitializerSceneUpgradeLevel >= 0
                ? siegeContext.MissionInitializerSceneUpgradeLevel
                : record.SceneUpgradeLevel;

            // Campaign mode pulls singleplayer mission views and expects campaign-only initializer fields.
            // Preserve the multiplayer value here; only mirror safe visual fields used by the campaign siege scene.
            record.DecalAtlasGroup = targetDecalAtlasGroup;
            record.TerrainType = targetTerrainType;
            record.SceneUpgradeLevel = targetSceneUpgradeLevel;

            if (previousDecalAtlasGroup == record.DecalAtlasGroup &&
                previousTerrainType == record.TerrainType &&
                previousSceneUpgradeLevel == record.SceneUpgradeLevel)
            {
                return;
            }

            ModLogger.Info(
                source + ": applied safe campaign siege scene initializer profile. " +
                "RuntimeScene=" + (runtimeScene ?? "unknown") +
                " PreservedPlayingInCampaignMode=" + previousPlayingInCampaignMode +
                " PreviousDecalAtlasGroup=" + previousDecalAtlasGroup +
                " NewDecalAtlasGroup=" + record.DecalAtlasGroup +
                " PreviousTerrainType=" + previousTerrainType +
                " NewTerrainType=" + record.TerrainType +
                " PreviousSceneUpgradeLevel=" + previousSceneUpgradeLevel +
                " SceneUpgradeLevel=" + record.SceneUpgradeLevel + ".");
        }

        public static bool TryRepairLiveMissionContract(Mission mission, string logSource)
        {
            if (mission == null)
                return false;

            string runtimeScene = mission.SceneName ?? string.Empty;
            if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(runtimeScene))
                return false;

            string source = string.IsNullOrWhiteSpace(logSource) ? "CampaignMapPatchMissionInit.LiveMissionRepair" : logSource;
            bool changed = false;
            bool initializerPatched = false;
            BattleSnapshotMessage snapshot = null;
            bool skipSpawnPathRepairForSiegeAssault = false;
            string siegeSubtype = "none";

            try
            {
                snapshot = TryResolveSnapshot(source + " live-contract");
                skipSpawnPathRepairForSiegeAssault =
                    ExactCampaignSiegeAssaultNoDeploymentRuntime.IsSiegeAssaultScenario(snapshot?.ScenarioContext);
                siegeSubtype = snapshot?.ScenarioContext?.SiegeContext?.SiegeSubtype ?? "none";
            }
            catch
            {
            }

            try
            {
                if (TryGetMissionInitializerRecord(mission, out MissionInitializerRecord record))
                {
                    string previousSceneLevels = record.SceneLevels;
                    bool hadPatchBefore = record.SceneHasMapPatch;
                    TryApply(ref record, runtimeScene, source + " initializer");
                    bool writeBackSucceeded = TrySetMissionInitializerRecord(mission, record);

                    initializerPatched = writeBackSucceeded && (record.SceneHasMapPatch || hadPatchBefore != record.SceneHasMapPatch);
                    changed |= hadPatchBefore != record.SceneHasMapPatch;
                    changed |= !string.Equals(previousSceneLevels, record.SceneLevels, StringComparison.Ordinal);
                    if (TryGetMissionInitializerRecord(mission, out MissionInitializerRecord storedRecord))
                        BattleMapContractDiagnostics.LogMissionInitializerRecordState(storedRecord, source + " live-mission-record");
                    else
                        BattleMapContractDiagnostics.LogMissionInitializerRecordState(record, source + " live-mission-record-local");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    source + ": live mission initializer repair failed. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " Message=" + ex.Message);
            }

            try
            {
                Mission.MissionTeamAITypeEnum targetType = ResolveMissionTeamAiType(snapshot?.ScenarioContext);
                if (mission.MissionTeamAIType != targetType)
                {
                    Mission.MissionTeamAITypeEnum previousType = mission.MissionTeamAIType;
                    mission.MissionTeamAIType = targetType;
                    changed = true;
                    ModLogger.Info(
                        source + ": repaired live mission team AI type. " +
                        "Scene=" + (mission.SceneName ?? "null") +
                        " PreviousType=" + previousType +
                        " NewType=" + mission.MissionTeamAIType +
                        " SiegeSubtype=" + siegeSubtype + ".");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    source + ": failed to repair live mission team AI type. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " Message=" + ex.Message);
            }

            bool spawnPathReinitialized = false;
            try
            {
                if (!skipSpawnPathRepairForSiegeAssault)
                {
                    object spawnPathSelectorObject = BattleSpawnPathSelectorField?.GetValue(mission);
                    if (spawnPathSelectorObject is BattleSpawnPathSelector selector)
                    {
                        selector.Initialize();
                        spawnPathReinitialized = selector.IsInitialized;
                        changed = true;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    source + ": live mission spawn-path reinitialize failed. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " Message=" + ex.Message);
            }

            ModLogger.Info(
                source + ": live mission contract repair applied. " +
                "Scene=" + (mission.SceneName ?? "null") +
                " InitializerPatched=" + initializerPatched +
                " MissionTeamAIType=" + mission.MissionTeamAIType +
                " SiegeSubtype=" + siegeSubtype +
                " HasSceneMapPatch=" + SafeHasSceneMapPatch(mission) +
                " HasSpawnPath=" + SafeHasSpawnPath(mission) +
                " SpawnPathRepairSkipped=" + skipSpawnPathRepairForSiegeAssault +
                " SpawnPathReinitialized=" + spawnPathReinitialized +
                " Changed=" + changed + ".");

            return changed;
        }

        public static bool TryPrimeEarlySnapshotFromLocalRoster(string runtimeScene, string logSource)
        {
            if (!GameNetwork.IsClient && !GameNetwork.IsServer)
                return false;

            try
            {
                BattleSnapshotMessage current = BattleSnapshotRuntimeState.GetCurrent();
                if ((current?.Sides?.Count ?? 0) > 0)
                    return false;
            }
            catch
            {
            }

            if (GameNetwork.IsClient && !CustomGameJoinContextState.ShouldAllowLocalBattleRosterFileFallback())
                return false;

            BattleSnapshotMessage snapshot;
            try
            {
                snapshot = BattleRosterFileHelper.PeekSnapshot();
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    (string.IsNullOrWhiteSpace(logSource) ? "CampaignMapPatchMissionInit" : logSource) +
                    ": early local snapshot prime failed while reading battle roster. " +
                    "RuntimeScene=" + (runtimeScene ?? "null") +
                    " Message=" + ex.Message);
                return false;
            }

            if ((snapshot?.Sides?.Count ?? 0) <= 0)
                return false;

            if (!IsRuntimeSceneCompatibleWithSnapshot(runtimeScene, snapshot))
                return false;

            string source = string.IsNullOrWhiteSpace(logSource) ? "CampaignMapPatchMissionInit" : logSource;
            BattleSnapshotRuntimeState.SetCurrent(snapshot, source + " battle-roster-prime");
            ModLogger.Info(
                source + ": primed battle snapshot runtime state from local battle roster before mission open. " +
                "RuntimeScene=" + (runtimeScene ?? "unknown") +
                " SnapshotMapScene=" + (snapshot.MapScene ?? "null") +
                " SnapshotMultiplayerScene=" + (snapshot.MultiplayerScene ?? "null") +
                " SiegeSubtype=" + (snapshot.ScenarioContext?.SiegeContext?.SiegeSubtype ?? "none") + ".");
            return true;
        }

        private static BattleSnapshotMessage TryResolveSnapshot(string source)
        {
            try
            {
                BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
                if (snapshot != null)
                    return snapshot;
            }
            catch
            {
            }

            if (GameNetwork.IsClient && !CustomGameJoinContextState.ShouldAllowLocalBattleRosterFileFallback())
            {
                ModLogger.Info(
                    (string.IsNullOrWhiteSpace(source) ? "CampaignMapPatchMissionInit" : source) +
                    ": skipped local battle roster snapshot fallback for remote custom-game join.");
                return null;
            }

            try
            {
                return BattleRosterFileHelper.ReadSnapshot();
            }
            catch
            {
                return null;
            }
        }

        private static bool IsSiegeScenario(BattleSnapshotMessage snapshot)
        {
            return snapshot?.ScenarioContext?.IsSiegeBattle == true;
        }

        private static bool IsRuntimeSceneCompatibleWithSnapshot(string runtimeScene, BattleSnapshotMessage snapshot)
        {
            if (string.IsNullOrWhiteSpace(runtimeScene) || snapshot == null)
                return false;

            return string.Equals(runtimeScene, snapshot.MapScene ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                || string.Equals(runtimeScene, snapshot.MultiplayerScene ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static Mission.MissionTeamAITypeEnum ResolveMissionTeamAiType(BattleScenarioContextMessage scenarioContext)
        {
            if (scenarioContext?.IsSiegeBattle != true)
                return Mission.MissionTeamAITypeEnum.FieldBattle;

            string siegeSubtype = scenarioContext.SiegeContext?.SiegeSubtype ?? string.Empty;
            if (LordsHallScenarioContract.IsLordsHallScenario(scenarioContext))
                return Mission.MissionTeamAITypeEnum.NoTeamAI;

            if (SallyOutScenarioContract.IsSallyOutScenario(scenarioContext))
                return Mission.MissionTeamAITypeEnum.FieldBattle;

            if (string.Equals(siegeSubtype, "Blockade", StringComparison.OrdinalIgnoreCase))
                return Mission.MissionTeamAITypeEnum.NoTeamAI;

            if (string.Equals(siegeSubtype, "BlockadeSallyOut", StringComparison.OrdinalIgnoreCase))
            {
                return Mission.MissionTeamAITypeEnum.SallyOut;
            }

            if (SiegeAmbushScenarioContract.IsSiegeAmbushScenario(scenarioContext))
                return Mission.MissionTeamAITypeEnum.SallyOut;

            if (ExactCampaignSiegeAssaultWithDeploymentRuntime.IsSiegeAssaultScenario(scenarioContext))
                return Mission.MissionTeamAITypeEnum.Siege;

            // Native siege no-deployment assault currently runs through
            // MissionCombatantsLogic(FieldBattle), not Siege TeamAI.
            if (ExactCampaignSiegeAssaultNoDeploymentRuntime.IsSiegeAssaultScenario(scenarioContext))
                return Mission.MissionTeamAITypeEnum.FieldBattle;

            return Mission.MissionTeamAITypeEnum.Siege;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
                return 0f;
            if (value > 1f)
                return 1f;
            return value;
        }

        private static bool TryGetMissionInitializerRecord(Mission mission, out MissionInitializerRecord record)
        {
            record = default;
            if (mission == null)
                return false;

            try
            {
                if (MissionInitializerRecordBackingField != null)
                {
                    object boxed = MissionInitializerRecordBackingField.GetValue(mission);
                    if (boxed is MissionInitializerRecord fieldRecord)
                    {
                        record = fieldRecord;
                        return true;
                    }
                }
            }
            catch
            {
            }

            try
            {
                object boxed = MissionInitializerRecordProperty?.GetValue(mission, null);
                if (boxed is MissionInitializerRecord propertyRecord)
                {
                    record = propertyRecord;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TrySetMissionInitializerRecord(Mission mission, MissionInitializerRecord record)
        {
            if (mission == null)
                return false;

            try
            {
                if (MissionInitializerRecordBackingField != null)
                {
                    MissionInitializerRecordBackingField.SetValue(mission, record);
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                if (MissionInitializerRecordProperty != null)
                {
                    MissionInitializerRecordProperty.SetValue(mission, record, null);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool SafeHasSceneMapPatch(Mission mission)
        {
            try
            {
                return mission != null && mission.HasSceneMapPatch();
            }
            catch
            {
                return false;
            }
        }

        private static bool SafeHasSpawnPath(Mission mission)
        {
            try
            {
                return mission != null && mission.HasSpawnPath;
            }
            catch
            {
                return false;
            }
        }
    }
}
