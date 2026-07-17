using System;
using System.Reflection;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Source.Missions;

namespace CoopSpectator.Campaign.SallyOut
{
    internal static class SallyOutCampaignBattleAdapter
    {
        private static readonly FieldInfo MissionInitializerRecordBackingField =
            typeof(Mission).GetField(
                "<InitializerRecord>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly PropertyInfo MissionInitializerRecordProperty =
            typeof(Mission).GetProperty(
                "InitializerRecord",
                BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool IsCampaignBattle(MapEvent battle)
        {
            return battle?.IsSallyOut == true &&
                   battle.IsBlockadeSallyOut != true &&
                   battle.IsSiegeAmbush != true;
        }

        public static bool IsCampaignStage(MapEvent battle, Settlement settlement)
        {
            return IsCampaignBattle(battle) &&
                   settlement?.IsFortification == true &&
                   settlement.SiegeEvent != null;
        }

        public static bool TryValidateActiveMission(
            MapEvent battle,
            Settlement settlement,
            Mission mission,
            out string expectedScene,
            out string diagnostics)
        {
            expectedScene = string.Empty;
            diagnostics = "not-sally-out-campaign-battle";
            if (!IsCampaignBattle(battle))
                return false;

            if (!IsCampaignStage(battle, settlement))
            {
                diagnostics =
                    "campaign-stage-invalid Settlement=" + (settlement?.StringId ?? "null") +
                    " IsFortification=" + (settlement?.IsFortification ?? false) +
                    " HasSiegeEvent=" + (settlement?.SiegeEvent != null);
                return false;
            }

            if (battle.PlayerSide == BattleSideEnum.None)
            {
                diagnostics = "player-side-none";
                return false;
            }

            if (mission == null)
            {
                diagnostics = "mission-null";
                return false;
            }

            if (!TryGetMissionInitializerRecord(mission, out MissionInitializerRecord initializerRecord))
            {
                diagnostics = "mission-initializer-missing";
                return false;
            }

            expectedScene = initializerRecord.SceneName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(expectedScene))
                expectedScene = mission.SceneName ?? string.Empty;

            if (string.IsNullOrWhiteSpace(expectedScene) ||
                !string.Equals(mission.SceneName, expectedScene, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics =
                    "scene-mismatch Runtime=" + (mission.SceneName ?? "null") +
                    " Expected=" + (expectedScene ?? "null");
                return false;
            }

            if (!initializerRecord.SceneHasMapPatch)
            {
                diagnostics = "mission-initializer-map-patch-disabled";
                return false;
            }

            if (mission.GetMissionBehavior<BattleSpawnLogic>() == null)
            {
                diagnostics = "native-battle-spawn-logic-missing";
                return false;
            }

            if (mission.GetMissionBehavior<SallyOutMissionController>() != null)
            {
                diagnostics = "unexpected-siege-ambush-controller";
                return false;
            }

            diagnostics =
                "validated Settlement=" + settlement.StringId +
                " Scene=" + expectedScene +
                " PlayerSide=" + battle.PlayerSide +
                " SceneHasMapPatch=True";
            return true;
        }

        public static bool RequiresNativeEncounterDirectionReversal(MapEvent battle)
        {
            return IsCampaignBattle(battle);
        }

        private static bool TryGetMissionInitializerRecord(
            Mission mission,
            out MissionInitializerRecord record)
        {
            record = default;
            if (mission == null)
                return false;

            try
            {
                object boxedRecord = MissionInitializerRecordProperty?.GetValue(mission) ??
                                     MissionInitializerRecordBackingField?.GetValue(mission);
                if (boxedRecord is MissionInitializerRecord initializerRecord)
                {
                    record = initializerRecord;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }
    }
}
