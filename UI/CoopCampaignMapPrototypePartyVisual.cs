using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.UI
{
    internal sealed class CoopCampaignMapPrototypePartyVisual
    {
        private const float MapScale = 0.3f;
        private static readonly Vec3 CaravanMountOffset =
            new Vec3(0.3f, -0.25f, 0f);

        private AgentVisuals _humanVisual;
        private AgentVisuals _mountVisual;
        private AgentVisuals _caravanMountVisual;

        internal bool IsUsable =>
            HasValidEntity(_humanVisual) &&
            (_mountVisual == null || HasValidEntity(_mountVisual)) &&
            (_caravanMountVisual == null || HasValidEntity(_caravanMountVisual));

        internal static bool TryCreate(
            Scene scene,
            CoopCampaignMapPrototypeEntityState state,
            MatrixFrame initialFrame,
            out CoopCampaignMapPrototypePartyVisual visual)
        {
            visual = null;
            if (scene == null ||
                state == null ||
                state.Kind == CoopCampaignMapPrototypeEntityKind.Settlement ||
                state.PartyVisualKind ==
                    CoopCampaignMapPrototypePartyVisualKind.None)
            {
                return false;
            }

            if (state.HumanVisual != null &&
                TryCreateExact(scene, state, initialFrame, out visual))
            {
                return true;
            }

            foreach (string candidateId in BuildCharacterCandidates(state))
            {
                BasicCharacterObject character = ResolveCharacter(candidateId);
                if (character == null)
                    continue;
                if (TryCreateFromCharacter(
                        scene,
                        character,
                        state,
                        initialFrame,
                        out visual))
                {
                    return true;
                }
            }

            return false;
        }

        internal void Update(
            MatrixFrame worldFrame,
            float dt,
            bool isMoving,
            float speed)
        {
            float safeDt = dt > 0f && IsFinite(dt) ? dt : 0.0001f;
            float safeSpeed = IsFinite(speed)
                ? Math.Max(0f, Math.Min(20f, speed))
                : 0f;

            SetFrame(_mountVisual, worldFrame);
            SetFrame(_humanVisual, worldFrame);
            MatrixFrame caravanFrame = worldFrame;
            caravanFrame.origin +=
                worldFrame.rotation.s * CaravanMountOffset.x +
                worldFrame.rotation.f * CaravanMountOffset.y +
                worldFrame.rotation.u * CaravanMountOffset.z;
            SetFrame(_caravanMountVisual, caravanFrame);

            _humanVisual?.Tick(_mountVisual, safeDt, isMoving, safeSpeed);
            _mountVisual?.Tick(null, safeDt, isMoving, safeSpeed);
            _caravanMountVisual?.Tick(null, safeDt, isMoving, safeSpeed);
        }

        internal void SetVisible(bool visible)
        {
            SetVisualVisible(_humanVisual, visible);
            SetVisualVisible(_mountVisual, visible);
            SetVisualVisible(_caravanMountVisual, visible);
        }

        internal void Reset()
        {
            AgentVisuals humanVisual = _humanVisual;
            AgentVisuals mountVisual = _mountVisual;
            AgentVisuals caravanMountVisual = _caravanMountVisual;
            _humanVisual = null;
            _mountVisual = null;
            _caravanMountVisual = null;

            ResetVisual(humanVisual);
            ResetVisual(mountVisual);
            ResetVisual(caravanMountVisual);
        }

        private static bool TryCreateExact(
            Scene scene,
            CoopCampaignMapPrototypeEntityState state,
            MatrixFrame initialFrame,
            out CoopCampaignMapPrototypePartyVisual visual)
        {
            visual = null;
            var candidate = new CoopCampaignMapPrototypePartyVisual();
            try
            {
                candidate._humanVisual = CreateExactHumanVisual(
                    scene,
                    state,
                    initialFrame);
                if (candidate._humanVisual == null)
                    return FailCandidate(candidate, out visual);

                if (state.MountVisual != null)
                {
                    candidate._mountVisual = CreateExactMountVisual(
                        scene,
                        state.MountVisual,
                        state.EntityId,
                        initialFrame,
                        "Mount");
                    if (candidate._mountVisual == null)
                        return FailCandidate(candidate, out visual);
                }

                if (state.CaravanMountVisual != null)
                {
                    MatrixFrame caravanFrame = initialFrame;
                    caravanFrame.origin +=
                        initialFrame.rotation.s * CaravanMountOffset.x +
                        initialFrame.rotation.f * CaravanMountOffset.y +
                        initialFrame.rotation.u * CaravanMountOffset.z;
                    candidate._caravanMountVisual = CreateExactMountVisual(
                        scene,
                        state.CaravanMountVisual,
                        state.EntityId,
                        caravanFrame,
                        "CaravanMount");
                    if (candidate._caravanMountVisual == null)
                        return FailCandidate(candidate, out visual);
                }

                if (state.PartyVisualKind ==
                        CoopCampaignMapPrototypePartyVisualKind.Mounted &&
                    candidate._mountVisual == null)
                {
                    return FailCandidate(candidate, out visual);
                }
                if (state.PartyVisualKind ==
                        CoopCampaignMapPrototypePartyVisualKind.Caravan &&
                    candidate._mountVisual == null &&
                    candidate._caravanMountVisual == null)
                {
                    return FailCandidate(candidate, out visual);
                }

                return CompleteCandidate(
                    candidate,
                    initialFrame,
                    out visual);
            }
            catch
            {
                return FailCandidate(candidate, out visual);
            }
        }

        private static bool TryCreateFromCharacter(
            Scene scene,
            BasicCharacterObject character,
            CoopCampaignMapPrototypeEntityState state,
            MatrixFrame initialFrame,
            out CoopCampaignMapPrototypePartyVisual visual)
        {
            visual = null;
            bool needsMount =
                state.PartyVisualKind ==
                    CoopCampaignMapPrototypePartyVisualKind.Mounted ||
                state.PartyVisualKind ==
                    CoopCampaignMapPrototypePartyVisualKind.Caravan;
            if (needsMount && !character.HasMount())
                return false;

            var candidate = new CoopCampaignMapPrototypePartyVisual();
            try
            {
                candidate._humanVisual = CreateFallbackHumanVisual(
                    scene,
                    character,
                    state,
                    initialFrame);
                if (candidate._humanVisual == null)
                    return FailCandidate(candidate, out visual);

                if (needsMount)
                {
                    candidate._mountVisual = CreateFallbackMountVisual(
                        scene,
                        character,
                        initialFrame);
                    if (candidate._mountVisual == null)
                        return FailCandidate(candidate, out visual);
                }

                return CompleteCandidate(
                    candidate,
                    initialFrame,
                    out visual);
            }
            catch
            {
                return FailCandidate(candidate, out visual);
            }
        }

        private static bool CompleteCandidate(
            CoopCampaignMapPrototypePartyVisual candidate,
            MatrixFrame initialFrame,
            out CoopCampaignMapPrototypePartyVisual visual)
        {
            SetFrame(candidate._mountVisual, initialFrame);
            candidate._mountVisual?.Tick(null, 0.0001f, false, 0f);
            ForceUpdateBoneFrames(candidate._mountVisual);

            SetFrame(candidate._humanVisual, initialFrame);
            candidate._humanVisual?.Tick(
                candidate._mountVisual,
                0.0001f,
                false,
                0f);
            ForceUpdateBoneFrames(candidate._humanVisual);

            MatrixFrame caravanFrame = initialFrame;
            caravanFrame.origin +=
                initialFrame.rotation.s * CaravanMountOffset.x +
                initialFrame.rotation.f * CaravanMountOffset.y +
                initialFrame.rotation.u * CaravanMountOffset.z;
            SetFrame(candidate._caravanMountVisual, caravanFrame);
            candidate._caravanMountVisual?.Tick(null, 0.0001f, false, 0f);
            ForceUpdateBoneFrames(candidate._caravanMountVisual);
            candidate.SetVisible(true);
            if (!candidate.IsUsable)
                return FailCandidate(candidate, out visual);
            visual = candidate;
            return true;
        }

        private static bool FailCandidate(
            CoopCampaignMapPrototypePartyVisual candidate,
            out CoopCampaignMapPrototypePartyVisual visual)
        {
            candidate?.Reset();
            visual = null;
            return false;
        }

        private static AgentVisuals CreateExactHumanVisual(
            Scene scene,
            CoopCampaignMapPrototypeEntityState state,
            MatrixFrame initialFrame)
        {
            CoopCampaignMapPrototypeAgentVisualState exact = state.HumanVisual;
            if (exact == null ||
                !BodyProperties.FromString(
                    exact.BodyProperties,
                    out BodyProperties bodyProperties) ||
                !TryBuildEquipment(exact, out Equipment equipment))
            {
                return null;
            }

            Monster monster =
                TaleWorlds.Core.FaceGen.GetBaseMonsterFromRace(exact.Race);
            if (monster == null)
                return null;

            MBActionSet actionSet = MBGlobals.GetActionSetWithSuffix(
                monster,
                exact.IsFemale,
                exact.HasBanner ? "_map_with_banner" : "_map");
            AgentVisualsData data = new AgentVisualsData()
                .UseMorphAnims(true)
                .Equipment(equipment)
                .BodyProperties(bodyProperties)
                .SkeletonType((SkeletonType)exact.SkeletonType)
                .Scale(MapScale)
                .Frame(initialFrame)
                .ActionSet(actionSet)
                .Scene(scene)
                .Monster(monster)
                .PrepareImmediately(false)
                .RightWieldedItemIndex(exact.RightWieldedItemIndex)
                .LeftWieldedItemIndex(exact.LeftWieldedItemIndex)
                .HasClippingPlane(true)
                .UseScaledWeapons(true)
                .ClothColor1(state.PrimaryColor)
                .ClothColor2(state.SecondaryColor)
                .CharacterObjectStringId(state.VisualCharacterId ?? string.Empty)
                .AddColorRandomness(exact.AddColorRandomness)
                .Race(exact.Race);
            if (exact.HasBanner && !string.IsNullOrWhiteSpace(state.BannerCode))
                data.Banner(new Banner(state.BannerCode));

            return AgentVisuals.Create(
                data,
                "CoopCampaignMapPartyExact " + (state.EntityId ?? string.Empty),
                false,
                false,
                false);
        }

        private static AgentVisuals CreateExactMountVisual(
            Scene scene,
            CoopCampaignMapPrototypeAgentVisualState exact,
            string entityId,
            MatrixFrame initialFrame,
            string label)
        {
            if (exact == null ||
                !TryBuildEquipment(exact, out Equipment equipment))
            {
                return null;
            }

            ItemObject horseItem = equipment[EquipmentIndex.Horse].Item;
            Monster monster = horseItem?.HorseComponent?.Monster;
            if (horseItem == null || monster == null)
                return null;

            AgentVisualsData data = new AgentVisualsData()
                .Equipment(equipment)
                .Scale(horseItem.ScaleFactor * MapScale)
                .Frame(initialFrame)
                .ActionSet(MBGlobals.GetActionSet(monster.ActionSetCode + "_map"))
                .Scene(scene)
                .Monster(monster)
                .PrepareImmediately(false)
                .UseScaledWeapons(true)
                .HasClippingPlane(true);
            if (!string.IsNullOrWhiteSpace(exact.MountCreationKey))
                data.MountCreationKey(exact.MountCreationKey);

            return AgentVisuals.Create(
                data,
                "CoopCampaignMapPartyExact" + label + " " +
                (entityId ?? string.Empty),
                false,
                false,
                false);
        }

        private static AgentVisuals CreateFallbackHumanVisual(
            Scene scene,
            BasicCharacterObject character,
            CoopCampaignMapPrototypeEntityState state,
            MatrixFrame initialFrame)
        {
            Equipment equipment = character.Equipment?.Clone(false);
            if (equipment == null)
                return null;

            Monster monster =
                TaleWorlds.Core.FaceGen.GetBaseMonsterFromRace(character.Race);
            if (monster == null)
                return null;

            MBActionSet actionSet = MBGlobals.GetActionSetWithSuffix(
                monster,
                character.IsFemale,
                "_map");
            AgentVisualsData data = new AgentVisualsData()
                .UseMorphAnims(true)
                .Equipment(equipment)
                .BodyProperties(character.GetBodyProperties(character.Equipment, -1))
                .SkeletonType((SkeletonType)(character.IsFemale ? 1 : 0))
                .Scale(MapScale)
                .Frame(initialFrame)
                .ActionSet(actionSet)
                .Scene(scene)
                .Monster(monster)
                .PrepareImmediately(false)
                .HasClippingPlane(true)
                .UseScaledWeapons(true)
                .ClothColor1(state.PrimaryColor)
                .ClothColor2(state.SecondaryColor)
                .CharacterObjectStringId(character.StringId)
                .AddColorRandomness(!character.IsHero)
                .Race(character.Race);

            return AgentVisuals.Create(
                data,
                "CoopCampaignMapPartyFallback " +
                (state.EntityId ?? string.Empty),
                false,
                false,
                false);
        }

        private static AgentVisuals CreateFallbackMountVisual(
            Scene scene,
            BasicCharacterObject character,
            MatrixFrame initialFrame)
        {
            Equipment equipment = character.Equipment?.Clone(false);
            ItemObject horseItem = equipment?[EquipmentIndex.Horse].Item;
            Monster monster = horseItem?.HorseComponent?.Monster;
            if (equipment == null || horseItem == null || monster == null)
                return null;

            AgentVisualsData data = new AgentVisualsData()
                .Equipment(equipment)
                .Scale(horseItem.ScaleFactor * MapScale)
                .Frame(initialFrame)
                .ActionSet(MBGlobals.GetActionSet(monster.ActionSetCode + "_map"))
                .Scene(scene)
                .Monster(monster)
                .PrepareImmediately(false)
                .UseScaledWeapons(true)
                .HasClippingPlane(true)
                .MountCreationKey(
                    MountCreationKey.GetRandomMountKeyString(
                        horseItem,
                        character.GetMountKeySeed()));

            return AgentVisuals.Create(
                data,
                "CoopCampaignMapPartyFallbackMount " + character.StringId,
                false,
                false,
                false);
        }

        private static bool TryBuildEquipment(
            CoopCampaignMapPrototypeAgentVisualState exact,
            out Equipment equipment)
        {
            equipment = null;
            if (!CoopCampaignMapPrototypeContract.IsValidAgentVisualState(
                    exact,
                    requireBodyProperties: false))
            {
                return false;
            }

            MBObjectManager objectManager =
                Game.Current?.ObjectManager ?? MBObjectManager.Instance;
            if (objectManager == null)
                return false;

            var resolved = new Equipment();
            for (int slot = 0;
                 slot < CoopCampaignMapPrototypeContract.EquipmentSlotCount;
                 slot++)
            {
                string itemId = exact.EquipmentItemIds[slot];
                if (string.IsNullOrWhiteSpace(itemId))
                    continue;

                ItemObject item = TryResolveItem(objectManager, itemId);
                if (item == null &&
                    ExactCampaignRuntimeItemRegistry.TryResolvePreloadedMirrorItem(
                        itemId,
                        out string mirrorItemId,
                        out _))
                {
                    item = TryResolveItem(objectManager, mirrorItemId);
                }
                if (item == null)
                    return false;

                resolved[(EquipmentIndex)slot] =
                    new EquipmentElement(item, null, null, false);
            }

            equipment = resolved;
            return true;
        }

        private static ItemObject TryResolveItem(
            MBObjectManager objectManager,
            string itemId)
        {
            if (objectManager == null || string.IsNullOrWhiteSpace(itemId))
                return null;
            try
            {
                return objectManager.GetObject<ItemObject>(itemId);
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<string> BuildCharacterCandidates(
            CoopCampaignMapPrototypeEntityState state)
        {
            var candidates = new List<string>();
            var observed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddCandidate(candidates, observed, state.VisualCharacterId);

            string cultureId = NormalizeCultureId(state.CultureId);
            bool needsMount =
                state.PartyVisualKind ==
                    CoopCampaignMapPrototypePartyVisualKind.Mounted ||
                state.PartyVisualKind ==
                    CoopCampaignMapPrototypePartyVisualKind.Caravan;
            if (needsMount)
            {
                AddCandidate(candidates, observed,
                    "mp_coop_light_cavalry_" + cultureId + "_hero");
                AddCandidate(candidates, observed,
                    "mp_coop_light_cavalry_" + cultureId + "_troop");
                AddCandidate(candidates, observed,
                    "mp_coop_light_cavalry_empire_hero");
                AddCandidate(candidates, observed,
                    "mp_coop_light_cavalry_empire_troop");
            }
            else
            {
                AddCandidate(candidates, observed,
                    "mp_light_infantry_" + cultureId + "_troop");
                AddCandidate(candidates, observed,
                    "mp_light_infantry_empire_troop");
                AddCandidate(candidates, observed,
                    "mp_coop_light_infantry_empire_hero");
                AddCandidate(candidates, observed,
                    "mp_coop_light_infantry_empire_troop");
            }

            return candidates;
        }

        private static BasicCharacterObject ResolveCharacter(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return null;
            MBObjectManager objectManager = MBObjectManager.Instance;
            if (objectManager == null)
                return null;
            try
            {
                return objectManager.GetObject<BasicCharacterObject>(characterId);
            }
            catch
            {
                return null;
            }
        }

        private static void SetFrame(
            AgentVisuals visual,
            MatrixFrame worldFrame)
        {
            if (visual == null)
                return;

            WeakGameEntity weakEntity = visual.GetWeakEntity();
            if (weakEntity == WeakGameEntity.Invalid)
                return;

            Vec3 scale = Vec3.One * visual.GetScale();
            worldFrame.Scale(in scale);
            weakEntity.SetFrame(ref worldFrame, true);
        }

        private static void SetVisualVisible(
            AgentVisuals visual,
            bool visible)
        {
            try
            {
                visual?.SetVisible(visible);
            }
            catch
            {
            }
        }

        private static bool HasValidEntity(AgentVisuals visual)
        {
            if (visual == null)
                return false;
            try
            {
                return visual.GetWeakEntity() != WeakGameEntity.Invalid;
            }
            catch
            {
                return false;
            }
        }

        private static void ForceUpdateBoneFrames(AgentVisuals visual)
        {
            if (visual == null)
                return;
            try
            {
                WeakGameEntity entity = visual.GetWeakEntity();
                if (entity != WeakGameEntity.Invalid)
                    entity.Skeleton.ForceUpdateBoneFrames();
            }
            catch
            {
            }
        }

        private static void ResetVisual(AgentVisuals visual)
        {
            try
            {
                visual?.Reset();
            }
            catch
            {
            }
        }

        private static void AddCandidate(
            ICollection<string> candidates,
            ISet<string> observed,
            string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return;

            string normalized = characterId.Trim();
            if (observed.Add(normalized))
                candidates.Add(normalized);
        }

        private static string NormalizeCultureId(string cultureId)
        {
            string normalized = string.IsNullOrWhiteSpace(cultureId)
                ? "empire"
                : cultureId.Trim().ToLowerInvariant();
            const string culturePrefix = "culture.";
            if (normalized.StartsWith(
                    culturePrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(culturePrefix.Length);
            }

            switch (normalized)
            {
                case "sturgia":
                case "battania":
                case "vlandia":
                case "empire":
                case "aserai":
                case "khuzait":
                    return normalized;
                default:
                    return "empire";
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
