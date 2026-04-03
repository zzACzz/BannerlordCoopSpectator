using System;
using System.Collections.Generic;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade.View;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.MountAndBlade.View.MissionViews.Order;
using TaleWorlds.MountAndBlade.View.MissionViews.Singleplayer;
using TaleWorlds.MountAndBlade.ViewModelCollection.OrderOfBattle;
using TaleWorlds.MountAndBlade.ViewModelCollection.Scoreboard;

namespace TaleWorlds.MountAndBlade.Multiplayer.View.MissionViews;

[ViewCreatorModule]
public static class MultiplayerPracticeMissionViews
{
	[ViewMethod("MultiplayerPractice")]
	public static MissionView[] OpenMultiplayerPracticeMission(Mission mission)
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Expected O, but got Unknown
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Expected O, but got Unknown
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Expected O, but got Unknown
		//IL_00bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c5: Expected O, but got Unknown
		//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00db: Expected O, but got Unknown
		//IL_00e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Expected O, but got Unknown
		//IL_011f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0129: Expected O, but got Unknown
		//IL_012a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0134: Expected O, but got Unknown
		//IL_0135: Unknown result type (might be due to invalid IL or missing references)
		//IL_013f: Expected O, but got Unknown
		//IL_0141: Unknown result type (might be due to invalid IL or missing references)
		//IL_014b: Expected O, but got Unknown
		List<MissionView> obj = new List<MissionView>
		{
			MultiplayerViewCreator.CreateMissionMultiplayerPracticeEscapeMenu(),
			ViewCreator.CreateMissionAgentLabelUIHandler(mission),
			ViewCreator.CreateMissionBattleScoreUIHandler(mission, (ScoreboardBaseVM)new CustomBattleScoreboardVM()),
			ViewCreator.CreateOptionsUIHandler(),
			ViewCreator.CreateMissionMainAgentEquipDropView(mission)
		};
		MissionView val = ViewCreator.CreateMissionOrderUIHandler((Mission)null);
		obj.Add(val);
		obj.Add((MissionView)new OrderTroopPlacer((OrderController)null));
		obj.Add(ViewCreator.CreateMissionAgentStatusUIHandler(mission));
		obj.Add(ViewCreator.CreateMissionMainAgentEquipmentController(mission));
		obj.Add(ViewCreator.CreateMissionMainAgentCheerBarkControllerView(mission));
		obj.Add(ViewCreator.CreateMissionAgentLockVisualizerView(mission));
		obj.Add((MissionView)new DeploymentMissionView());
		ISiegeDeploymentView val2 = (ISiegeDeploymentView)(object)((val is ISiegeDeploymentView) ? val : null);
		obj.Add((MissionView)new MissionEntitySelectionUIHandler((Action<WeakGameEntity>)val2.OnEntitySelection, (Action<WeakGameEntity>)val2.OnEntityHover));
		obj.Add(ViewCreator.CreateMissionBoundaryCrossingView());
		obj.Add((MissionView)new MissionBoundaryWallView());
		obj.Add((MissionView)new MissionDeploymentBoundaryMarker("swallowtail_banner", 2f));
		obj.Add(ViewCreator.CreateMissionFormationMarkerUIHandler(mission));
		obj.Add(ViewCreator.CreateSingleplayerMissionKillNotificationUIHandler());
		obj.Add(ViewCreator.CreateMissionSpectatorControlView(mission));
		obj.Add(ViewCreator.CreatePhotoModeView());
		obj.Add((MissionView)new MissionItemContourControllerView());
		obj.Add((MissionView)new MissionAgentContourControllerView());
		obj.Add((MissionView)new MissionCustomBattlePreloadView());
		obj.Add(ViewCreator.CreateMissionOrderOfBattleUIHandler(mission, new OrderOfBattleVM()));
		return obj.ToArray();
	}
}
