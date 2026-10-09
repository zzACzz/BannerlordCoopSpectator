using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using SandBox.View.Map;
using SandBox.View.Map.Managers;
using SandBox.View.Map.Visuals;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Campaign
{
    public sealed partial class CoopCampaignMapPrototypePublisherBehavior
    {
        private static readonly FieldInfo TargetCircleStartField = typeof(MapScreen).GetField(
            "_targetCircleRotationStartTime", BindingFlags.Instance | BindingFlags.NonPublic);

        private string CaptureMovementTarget(MobileParty main)
        {
            try
            {
                var map = MapScreen.Instance;
                if (main == null || map?.MapScene == null || !map.IsReady || map.MapCameraView == null || Mission.Current != null)
                    return string.Empty;
                MobileParty pursuit = main.MoveTargetParty;
                var occupied = pursuit?.CurrentSettlement;
                var settlement = main.TargetSettlement;
                var input = new CoopCampaignMapMovementTargetInput {
                    PartyMoveMode = (int)main.PartyMoveMode, DefaultBehavior = (int)main.DefaultBehavior,
                    ForceAiNoPathMode = main.ForceAiNoPathMode, HasAiInteractable = main.Ai.AiBehaviorInteractable != null,
                    HasMapEvent = main.MapEvent != null, TargetDistanceSquared = main.TargetPosition.DistanceSquared(main.Position),
                    HasMoveTargetParty = pursuit != null, MoveTargetPartyVisible = pursuit?.IsVisible == true,
                    MoveTargetPartyId = pursuit == null ? null : "party:" + pursuit.StringId,
                    MoveTargetHasSettlement = occupied != null, MoveTargetSettlementIsHideout = occupied?.IsHideout == true,
                    MoveTargetSettlementId = occupied == null ? null : "settlement:" + occupied.StringId,
                    TargetSettlementId = settlement == null ? null : "settlement:" + settlement.StringId
                };
                var selection = CoopCampaignMapMovementTargetPolicy.Select(input, id =>
                    _knownCatalogById.ContainsKey(id) && _knownDynamicById.TryGetValue(id, out var d) && d.IsVisible);
                if (selection.Kind == CoopCampaignMapMovementTargetKind.None) return string.Empty;
                var recipe = new CoopCampaignMapMovementTargetPresentation {
                    Kind = selection.Kind, EntityId = selection.EntityId,
                    HostCameraDistance = map.MapCameraView.CameraDistance
                };
                Vec3 origin;
                if (selection.Kind == CoopCampaignMapMovementTargetKind.Point)
                {
                    recipe.Color = CoopCampaignMapMovementTargetLayout.PointColor;
                    recipe.AtSea = !main.TargetPosition.IsOnLand;
                    origin = main.TargetPosition.AsVec3();
                    if (recipe.AtSea)
                    {
                        Vec2 wind = TaleWorlds.CampaignSystem.Campaign.Current.Models.MapWeatherModel.GetWindForPosition(main.TargetPosition);
                        recipe.Wind = new[] { wind.x, wind.y };
                    }
                }
                else
                {
                    PartyBase target = selection.Kind == CoopCampaignMapMovementTargetKind.Party ? pursuit?.Party :
                        (selection.EntityId == input.MoveTargetSettlementId ? occupied?.Party : settlement?.Party);
                    if (target == null) return string.Empty;
                    MapEntityVisual visual;
                    if (target.IsSettlement)
                    {
                        var s = target.Settlement;
                        ISpottable spottable = s.SettlementComponent as ISpottable;
                        if (!CoopCampaignMapPrototypeVisibilityPolicy.ShouldReplicateSettlement(
                            s.IsVisible, spottable != null, spottable?.IsSpotted == true)) return string.Empty;
                        visual = SettlementVisualManager.Current?.GetSettlementVisual(s);
                    }
                    else
                    {
                        MobileParty p = target.MobileParty;
                        if (p == null || !p.IsVisible || !CoopCampaignMapPrototypeVisibilityPolicy.ShouldReplicateMobileParty(
                            p == main, p.IsActive, p.IsGarrison, p.IsMilitia, p.CurrentSettlement != null, p.IsSpotted())) return string.Empty;
                        visual = TryGetMobilePartyVisual(MobilePartyVisualManager.Current, target);
                        recipe.AtSea = p.IsCurrentlyAtSea;
                        if (TargetCircleStartField == null) return string.Empty;
                        float start = (float)TargetCircleStartField.GetValue(map);
                        recipe.AnimationElapsedSeconds = Math.Max(0f, MBCommon.GetApplicationTime() - start);
                    }
                    if (!CoopCampaignMapHoverDecal.TryDecode(CaptureHoverDecal(target, visual), out var circle) || circle == null)
                        return string.Empty;
                    recipe.Fortification = circle.Fortification;
                    recipe.Color = circle.Color;
                    recipe.CircleFrame = circle.Frame;
                    origin = target.Position.AsVec3();
                    if (target.IsMobile)
                    {
                        origin += (target.MobileParty.EventPositionAdder + target.MobileParty.ArmyPositionAdder).ToVec3();
                        Vec3 offset = origin - ((MobilePartyVisual)visual).GetVisualPosition();
                        recipe.VisualOriginOffset = new[] { offset.x, offset.y, offset.z };
                    }
                }
                if (!recipe.Fortification)
                {
                    Vec3 normal = map.MapScene.GetNormalAt(origin.AsVec2);
                    recipe.Origin = new[] { origin.x, origin.y, origin.z };
                    recipe.TerrainNormal = new[] { normal.x, normal.y, normal.z };
                }
                return CoopCampaignMapMovementTargetCodec.TryEncode(recipe, out var encoded) ? encoded : string.Empty;
            }
            catch { return string.Empty; } // Clear unavailable state; no native mutations or per-tick diagnostics.
        }
    }
}
