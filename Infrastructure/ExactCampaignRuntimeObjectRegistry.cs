using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Infrastructure
{
    public static class ExactCampaignRuntimeObjectRegistry
    {
        private const uint RuntimeCharacterTypeId = 64001u;
        private const uint RuntimeHeroClassTypeId = 64002u;

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, ExactCampaignRuntimeCharacterObject> CharacterByEntryId =
            new Dictionary<string, ExactCampaignRuntimeCharacterObject>(StringComparer.Ordinal);
        private static readonly Dictionary<string, ExactCampaignRuntimeHeroClass> HeroClassByEntryId =
            new Dictionary<string, ExactCampaignRuntimeHeroClass>(StringComparer.Ordinal);
        private static readonly MethodInfo BuildSnapshotEquipmentMethod =
            typeof(CoopMissionClientLogic).GetMethod(
                "BuildSnapshotEquipmentForExactRuntime",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        private static string _activeBattleId;

        public static void SyncFromState(BattleRuntimeState runtimeState, string source)
        {
            if (!ExperimentalFeatures.EnableExactCampaignRuntimeObjectRegistry || runtimeState == null)
                return;

            MBObjectManager objectManager = Game.Current?.ObjectManager ?? MBObjectManager.Instance;
            if (objectManager == null)
                return;

            lock (Sync)
            {
                EnsureTypesRegistered(objectManager);

                string battleId = runtimeState.Snapshot?.BattleId ?? "unknown";
                if (!string.Equals(_activeBattleId, battleId, StringComparison.Ordinal))
                {
                    ClearInternal(objectManager, "battle-changed:" + battleId, log: false);
                    _activeBattleId = battleId;
                }

                int createdCharacters = 0;
                int updatedCharacters = 0;
                int createdHeroClasses = 0;
                int updatedHeroClasses = 0;
                int unresolvedCharacters = 0;
                int unresolvedHeroClasses = 0;

                foreach (RosterEntryState entryState in EnumerateEntries(runtimeState))
                {
                    if (entryState == null || string.IsNullOrWhiteSpace(entryState.EntryId))
                        continue;

                    BasicCharacterObject baseCharacter = ResolveBaseCharacter(entryState);
                    if (baseCharacter == null)
                    {
                        unresolvedCharacters++;
                        continue;
                    }

                    Equipment battleEquipment = BuildRuntimeEquipment(entryState);
                    if (battleEquipment == null)
                        battleEquipment = CloneBattleEquipment(baseCharacter);

                    TextObject runtimeName = ResolveEntryName(entryState, baseCharacter);
                    bool hasExactBodyProperties = TryResolveExactBodyProperties(entryState, out BodyProperties exactBodyProperties);

                    if (!CharacterByEntryId.TryGetValue(entryState.EntryId, out ExactCampaignRuntimeCharacterObject runtimeCharacter))
                    {
                        runtimeCharacter = new ExactCampaignRuntimeCharacterObject(
                            BuildRuntimeCharacterId(battleId, entryState, baseCharacter),
                            baseCharacter);
                        objectManager.RegisterObject(runtimeCharacter);
                        CharacterByEntryId[entryState.EntryId] = runtimeCharacter;
                        createdCharacters++;
                    }
                    else
                    {
                        updatedCharacters++;
                    }

                    runtimeCharacter.UpdateFromEntry(
                        entryState,
                        battleEquipment,
                        runtimeName,
                        hasExactBodyProperties,
                        exactBodyProperties);

                    if (!TryResolveBaseHeroClass(entryState, baseCharacter, out MultiplayerClassDivisions.MPHeroClass baseHeroClass, out bool treatAsTroop))
                    {
                        unresolvedHeroClasses++;
                        continue;
                    }

                    if (!HeroClassByEntryId.TryGetValue(entryState.EntryId, out ExactCampaignRuntimeHeroClass runtimeHeroClass))
                    {
                        runtimeHeroClass = new ExactCampaignRuntimeHeroClass(BuildRuntimeHeroClassId(battleId, entryState));
                        objectManager.RegisterObject(runtimeHeroClass);
                        HeroClassByEntryId[entryState.EntryId] = runtimeHeroClass;
                        createdHeroClasses++;
                    }
                    else
                    {
                        updatedHeroClasses++;
                    }

                    runtimeHeroClass.UpdateFromTemplate(baseHeroClass, runtimeCharacter, treatAsTroop);
                }

                ModLogger.Info(
                    "ExactCampaignRuntimeObjectRegistry: synced runtime exact objects for pre-spawn exact pipeline. " +
                    "Source=" + (source ?? "unknown") +
                    " BattleId=" + (_activeBattleId ?? "null") +
                    " RuntimeCharacters=" + CharacterByEntryId.Count +
                    " RuntimeHeroClasses=" + HeroClassByEntryId.Count +
                    " CreatedCharacters=" + createdCharacters +
                    " UpdatedCharacters=" + updatedCharacters +
                    " CreatedHeroClasses=" + createdHeroClasses +
                    " UpdatedHeroClasses=" + updatedHeroClasses +
                    " UnresolvedCharacters=" + unresolvedCharacters +
                    " UnresolvedHeroClasses=" + unresolvedHeroClasses);
            }
        }

        public static void Clear(string reason)
        {
            lock (Sync)
            {
                MBObjectManager objectManager = Game.Current?.ObjectManager ?? MBObjectManager.Instance;
                ClearInternal(objectManager, reason, log: true);
            }
        }

        public static BasicCharacterObject TryResolveCharacter(string entryId)
        {
            if (!ExperimentalFeatures.EnableExactCampaignRuntimeObjectRegistry || string.IsNullOrWhiteSpace(entryId))
                return null;

            lock (Sync)
            {
                return CharacterByEntryId.TryGetValue(entryId, out ExactCampaignRuntimeCharacterObject runtimeCharacter)
                    ? runtimeCharacter
                    : null;
            }
        }

        public static bool IsRuntimeCharacter(BasicCharacterObject character)
        {
            return character is ExactCampaignRuntimeCharacterObject;
        }

        private static IEnumerable<RosterEntryState> EnumerateEntries(BattleRuntimeState runtimeState)
        {
            if (runtimeState?.Sides == null)
                yield break;

            foreach (BattleSideState sideState in runtimeState.Sides)
            {
                if (sideState?.Entries == null)
                    continue;

                foreach (RosterEntryState entryState in sideState.Entries)
                    yield return entryState;
            }
        }

        private static void EnsureTypesRegistered(MBObjectManager objectManager)
        {
            try
            {
                if (!objectManager.HasType(typeof(ExactCampaignRuntimeCharacterObject)))
                {
                    objectManager.RegisterType<ExactCampaignRuntimeCharacterObject>(
                        "CoopSpectatorExactRuntimeCharacter",
                        "CoopSpectatorExactRuntimeCharacters",
                        RuntimeCharacterTypeId,
                        true,
                        true);
                }

                if (!objectManager.HasType(typeof(ExactCampaignRuntimeHeroClass)))
                {
                    objectManager.RegisterType<ExactCampaignRuntimeHeroClass>(
                        "CoopSpectatorExactRuntimeHeroClass",
                        "CoopSpectatorExactRuntimeHeroClasses",
                        RuntimeHeroClassTypeId,
                        true,
                        true);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("ExactCampaignRuntimeObjectRegistry: runtime type registration failed: " + ex.Message);
            }
        }

        private static void ClearInternal(MBObjectManager objectManager, string reason, bool log)
        {
            if (objectManager != null)
            {
                foreach (ExactCampaignRuntimeHeroClass runtimeHeroClass in HeroClassByEntryId.Values)
                    TryUnregisterObject(objectManager, runtimeHeroClass);

                foreach (ExactCampaignRuntimeCharacterObject runtimeCharacter in CharacterByEntryId.Values)
                    TryUnregisterObject(objectManager, runtimeCharacter);
            }

            HeroClassByEntryId.Clear();
            CharacterByEntryId.Clear();
            _activeBattleId = null;

            if (log)
            {
                ModLogger.Info(
                    "ExactCampaignRuntimeObjectRegistry: cleared runtime exact objects. " +
                    "Reason=" + (reason ?? "unknown"));
            }
        }

        private static void TryUnregisterObject(MBObjectManager objectManager, MBObjectBase obj)
        {
            if (objectManager == null || obj == null)
                return;

            try
            {
                objectManager.UnregisterObject(obj);
            }
            catch
            {
            }
        }

        private static BasicCharacterObject ResolveBaseCharacter(RosterEntryState entryState)
        {
            string[] candidateIds =
            {
                entryState?.OriginalCharacterId,
                entryState?.HeroTemplateId,
                entryState?.SpawnTemplateId,
                entryState?.CharacterId
            };

            foreach (string candidateId in candidateIds)
            {
                if (string.IsNullOrWhiteSpace(candidateId))
                    continue;

                try
                {
                    BasicCharacterObject candidate = MBObjectManager.Instance.GetObject<BasicCharacterObject>(candidateId);
                    if (candidate != null)
                        return candidate;
                }
                catch
                {
                }
            }

            return null;
        }

        private static bool TryResolveBaseHeroClass(
            RosterEntryState entryState,
            BasicCharacterObject baseCharacter,
            out MultiplayerClassDivisions.MPHeroClass baseHeroClass,
            out bool treatAsTroop)
        {
            baseHeroClass = null;
            treatAsTroop = false;
            if (baseCharacter == null)
                return false;

            if (CampaignMultiplayerHeroClassResolver.TryResolve(baseCharacter, out baseHeroClass, out treatAsTroop, out string _))
                return baseHeroClass != null;

            baseHeroClass = MultiplayerClassDivisions.GetMPHeroClassForCharacter(baseCharacter);
            if (baseHeroClass != null)
            {
                treatAsTroop = baseHeroClass.IsTroopCharacter(baseCharacter);
                return true;
            }

            if (!string.IsNullOrWhiteSpace(entryState?.SpawnTemplateId))
            {
                try
                {
                    BasicCharacterObject templateCharacter = MBObjectManager.Instance.GetObject<BasicCharacterObject>(entryState.SpawnTemplateId);
                    if (templateCharacter != null)
                    {
                        baseHeroClass = MultiplayerClassDivisions.GetMPHeroClassForCharacter(templateCharacter);
                        if (baseHeroClass != null)
                        {
                            treatAsTroop = true;
                            return true;
                        }
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private static Equipment CloneBattleEquipment(BasicCharacterObject character)
        {
            try
            {
                return character?.FirstBattleEquipment?.Clone(false) ??
                       character?.Equipment?.Clone(false);
            }
            catch
            {
                return null;
            }
        }

        private static Equipment BuildRuntimeEquipment(RosterEntryState entryState)
        {
            if (entryState == null)
                return null;

            try
            {
                return BuildSnapshotEquipmentMethod?.Invoke(null, new object[] { entryState }) as Equipment;
            }
            catch
            {
                return null;
            }
        }

        private static TextObject ResolveEntryName(RosterEntryState entryState, BasicCharacterObject baseCharacter)
        {
            if (!string.IsNullOrWhiteSpace(entryState?.TroopName))
                return new TextObject(entryState.TroopName);

            return baseCharacter?.Name;
        }

        private static bool TryResolveExactBodyProperties(RosterEntryState entryState, out BodyProperties bodyProperties)
        {
            bodyProperties = default(BodyProperties);
            if (string.IsNullOrWhiteSpace(entryState?.HeroBodyProperties))
                return false;

            try
            {
                return BodyProperties.FromString(entryState.HeroBodyProperties, out bodyProperties);
            }
            catch
            {
                return false;
            }
        }

        private static string BuildRuntimeCharacterId(string battleId, RosterEntryState entryState, BasicCharacterObject baseCharacter)
        {
            return "coopspectator_exact_rt_char_" +
                   SanitizeIdComponent(battleId) + "_" +
                   SanitizeIdComponent(entryState?.EntryId) + "_" +
                   SanitizeIdComponent(baseCharacter?.StringId);
        }

        private static string BuildRuntimeHeroClassId(string battleId, RosterEntryState entryState)
        {
            return "coopspectator_exact_rt_class_" +
                   SanitizeIdComponent(battleId) + "_" +
                   SanitizeIdComponent(entryState?.EntryId);
        }

        private static string SanitizeIdComponent(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "null";

            return new string(
                value.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_').ToArray());
        }

        private sealed class ExactCampaignRuntimeCharacterObject : BasicCharacterObject
        {
            private readonly BasicCharacterObject _baseCharacter;
            private Equipment _battleEquipment;
            private TextObject _runtimeName;
            private BodyProperties _exactBodyProperties;
            private bool _hasExactBodyProperties;
            private bool _runtimeIsMounted;
            private bool _runtimeIsRanged;
            private bool _runtimeIsHero;
            private int _runtimeLevel;
            private float _runtimeAge;
            private bool _runtimeIsFemale;
            private int _runtimeHitPoints;
            private readonly Dictionary<string, int> _skillValues =
                new Dictionary<string, int>(StringComparer.Ordinal);

            public ExactCampaignRuntimeCharacterObject(string stringId, BasicCharacterObject baseCharacter)
                : base()
            {
                StringId = stringId;
                _baseCharacter = baseCharacter;
                if (baseCharacter != null)
                    FillFrom(baseCharacter);

                _battleEquipment = CloneBattleEquipment(baseCharacter) ?? new Equipment();
                _runtimeName = baseCharacter?.Name ?? new TextObject(stringId ?? "exact_runtime");
                _runtimeIsMounted = baseCharacter?.IsMounted == true;
                _runtimeIsRanged = baseCharacter?.IsRanged == true;
                _runtimeIsHero = baseCharacter?.IsHero == true;
                _runtimeLevel = baseCharacter?.Level ?? 1;
                _runtimeAge = baseCharacter?.Age ?? 18f;
                _runtimeIsFemale = baseCharacter?.IsFemale == true;
                _runtimeHitPoints = Math.Max(1, baseCharacter?.MaxHitPoints() ?? 100);
                Initialize();
            }

            public void UpdateFromEntry(
                RosterEntryState entryState,
                Equipment battleEquipment,
                TextObject runtimeName,
                bool hasExactBodyProperties,
                BodyProperties exactBodyProperties)
            {
                _battleEquipment = battleEquipment?.Clone(false) ?? CloneBattleEquipment(_baseCharacter) ?? new Equipment();
                _runtimeName = runtimeName ?? _baseCharacter?.Name ?? new TextObject(base.StringId ?? "exact_runtime");
                _runtimeIsMounted = ResolveMounted(entryState, _battleEquipment, _baseCharacter);
                _runtimeIsRanged = ResolveRanged(entryState, _battleEquipment, _baseCharacter);
                _runtimeIsHero = entryState?.IsHero == true || _baseCharacter?.IsHero == true;
                _runtimeLevel = entryState?.HeroLevel > 0 ? entryState.HeroLevel : (_baseCharacter?.Level ?? 1);
                _runtimeAge = entryState?.HeroAge > 0f ? entryState.HeroAge : (_baseCharacter?.Age ?? 18f);
                _runtimeIsFemale =
                    entryState?.IsHero == true
                        ? entryState.HeroIsFemale
                        : (_baseCharacter?.IsFemale == true);
                _runtimeHitPoints = Math.Max(
                    1,
                    entryState?.BaseHitPoints > 0
                        ? entryState.BaseHitPoints
                        : (_baseCharacter?.MaxHitPoints() ?? 100));
                _hasExactBodyProperties = hasExactBodyProperties;
                _exactBodyProperties = exactBodyProperties;

                Level = _runtimeLevel;
                Age = _runtimeAge;
                IsFemale = _runtimeIsFemale;
                DefaultFormationClass = ResolveFormationClass(_runtimeIsMounted, _runtimeIsRanged, _baseCharacter);

                if (Culture == null && !string.IsNullOrWhiteSpace(entryState?.CultureId))
                {
                    try
                    {
                        Culture = MBObjectManager.Instance.GetObject<BasicCultureObject>(entryState.CultureId);
                    }
                    catch
                    {
                    }
                }

                UpdateSkillValues(entryState);
            }

            public override TextObject Name => _runtimeName ?? _baseCharacter?.Name ?? base.Name;

            public override Equipment Equipment => _battleEquipment ?? _baseCharacter?.Equipment ?? base.Equipment;

            public override IEnumerable<Equipment> BattleEquipments
            {
                get { yield return Equipment; }
            }

            public override Equipment FirstBattleEquipment => Equipment;

            public override Equipment RandomBattleEquipment => Equipment;

            public override Equipment GetRandomEquipment => Equipment;

            public override bool IsMounted => _runtimeIsMounted;

            public override bool IsRanged => _runtimeIsRanged;

            public override bool IsHero => _runtimeIsHero;

            public override int Level
            {
                get => _runtimeLevel;
                set => _runtimeLevel = value;
            }

            public override float Age
            {
                get => _runtimeAge;
                set => _runtimeAge = value;
            }

            public override bool IsFemale
            {
                get => _runtimeIsFemale;
                set => _runtimeIsFemale = value;
            }

            public override BodyProperties GetBodyPropertiesMin(bool returnBaseValue = false)
            {
                return _hasExactBodyProperties
                    ? _exactBodyProperties
                    : (_baseCharacter?.GetBodyPropertiesMin(returnBaseValue) ?? base.GetBodyPropertiesMin(returnBaseValue));
            }

            public override BodyProperties GetBodyPropertiesMax(bool returnBaseValue = false)
            {
                return _hasExactBodyProperties
                    ? _exactBodyProperties
                    : (_baseCharacter?.GetBodyPropertiesMax(returnBaseValue) ?? base.GetBodyPropertiesMax(returnBaseValue));
            }

            public override BodyProperties GetBodyProperties(Equipment equipment, int seed = -1)
            {
                return _hasExactBodyProperties
                    ? _exactBodyProperties
                    : (_baseCharacter?.GetBodyProperties(equipment, seed) ?? base.GetBodyProperties(equipment, seed));
            }

            public override int MaxHitPoints()
            {
                return _runtimeHitPoints > 0 ? _runtimeHitPoints : (_baseCharacter?.MaxHitPoints() ?? base.MaxHitPoints());
            }

            public override int GetSkillValue(SkillObject skill)
            {
                if (skill != null && _skillValues.TryGetValue(skill.StringId ?? string.Empty, out int value))
                    return value;

                return _baseCharacter?.GetSkillValue(skill) ?? base.GetSkillValue(skill);
            }

            private void UpdateSkillValues(RosterEntryState entryState)
            {
                _skillValues.Clear();
                if (entryState == null)
                    return;

                AddSkillValue(DefaultSkills.OneHanded, entryState.SkillOneHanded);
                AddSkillValue(DefaultSkills.TwoHanded, entryState.SkillTwoHanded);
                AddSkillValue(DefaultSkills.Polearm, entryState.SkillPolearm);
                AddSkillValue(DefaultSkills.Bow, entryState.SkillBow);
                AddSkillValue(DefaultSkills.Crossbow, entryState.SkillCrossbow);
                AddSkillValue(DefaultSkills.Throwing, entryState.SkillThrowing);
                AddSkillValue(DefaultSkills.Riding, entryState.SkillRiding);
                AddSkillValue(DefaultSkills.Athletics, entryState.SkillAthletics);
            }

            private void AddSkillValue(SkillObject skill, int value)
            {
                if (skill == null || value <= 0)
                    return;

                _skillValues[skill.StringId] = value;
            }

            private static bool ResolveMounted(RosterEntryState entryState, Equipment battleEquipment, BasicCharacterObject baseCharacter)
            {
                if (entryState?.IsMounted == true)
                    return true;

                try
                {
                    if (battleEquipment != null && !battleEquipment[EquipmentIndex.ArmorItemEndSlot].IsEmpty)
                        return true;
                }
                catch
                {
                }

                return baseCharacter?.IsMounted == true;
            }

            private static bool ResolveRanged(RosterEntryState entryState, Equipment battleEquipment, BasicCharacterObject baseCharacter)
            {
                if (entryState?.IsRanged == true)
                    return true;

                for (EquipmentIndex slot = EquipmentIndex.WeaponItemBeginSlot; slot < EquipmentIndex.NumAllWeaponSlots; slot++)
                {
                    try
                    {
                        ItemObject item = battleEquipment?[slot].Item;
                        if (item?.PrimaryWeapon != null && item.PrimaryWeapon.IsRangedWeapon)
                            return true;
                    }
                    catch
                    {
                    }
                }

                return baseCharacter?.IsRanged == true;
            }

            private static FormationClass ResolveFormationClass(bool isMounted, bool isRanged, BasicCharacterObject baseCharacter)
            {
                if (isMounted && isRanged)
                    return FormationClass.HorseArcher;
                if (isMounted)
                    return FormationClass.Cavalry;
                if (isRanged)
                    return FormationClass.Ranged;

                return baseCharacter?.DefaultFormationClass ?? FormationClass.Infantry;
            }
        }

        private sealed class ExactCampaignRuntimeHeroClass : MultiplayerClassDivisions.MPHeroClass
        {
            private static readonly FieldInfo HeroCharacterField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<HeroCharacter>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo TroopCharacterField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<TroopCharacter>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo BannerBearerCharacterField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<BannerBearerCharacter>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo CultureField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<Culture>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo ClassGroupField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<ClassGroup>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo HeroIdleAnimField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<HeroIdleAnim>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo HeroMountIdleAnimField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<HeroMountIdleAnim>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo TroopIdleAnimField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<TroopIdleAnim>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo TroopMountIdleAnimField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<TroopMountIdleAnim>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo ArmorValueField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<ArmorValue>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo HealthField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<Health>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo HeroMovementSpeedMultiplierField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<HeroMovementSpeedMultiplier>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo HeroCombatMovementSpeedMultiplierField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<HeroCombatMovementSpeedMultiplier>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo HeroTopSpeedReachDurationField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<HeroTopSpeedReachDuration>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo TroopMovementSpeedMultiplierField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<TroopMovementSpeedMultiplier>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo TroopCombatMovementSpeedMultiplierField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<TroopCombatMovementSpeedMultiplier>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo TroopTopSpeedReachDurationField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<TroopTopSpeedReachDuration>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo TroopMultiplierField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<TroopMultiplier>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo TroopCostField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<TroopCost>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo TroopCasualCostField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<TroopCasualCost>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo TroopBattleCostField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<TroopBattleCost>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo MeleeAiField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<MeleeAI>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo RangedAiField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<RangedAI>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo HeroInformationField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<HeroInformation>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo TroopInformationField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<TroopInformation>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo IconTypeField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("<IconType>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo PerksField = typeof(MultiplayerClassDivisions.MPHeroClass).GetField("_perks", BindingFlags.Instance | BindingFlags.NonPublic);

            public ExactCampaignRuntimeHeroClass(string stringId)
            {
                StringId = stringId;
                Initialize();
            }

            public void UpdateFromTemplate(
                MultiplayerClassDivisions.MPHeroClass template,
                BasicCharacterObject runtimeCharacter,
                bool treatAsTroop)
            {
                if (template == null || runtimeCharacter == null)
                    return;

                SetField(HeroCharacterField, treatAsTroop ? template.HeroCharacter ?? runtimeCharacter : runtimeCharacter);
                SetField(TroopCharacterField, treatAsTroop ? runtimeCharacter : template.TroopCharacter ?? runtimeCharacter);
                SetField(BannerBearerCharacterField, template.BannerBearerCharacter ?? runtimeCharacter);
                // Keep Culture null so these runtime-only hero classes do not leak into native team class lists.
                SetField(CultureField, null);
                SetField(ClassGroupField, template.ClassGroup ?? new MultiplayerClassDivisions.MPHeroClassGroup(runtimeCharacter.DefaultFormationClass.GetName()));
                SetField(HeroIdleAnimField, template.HeroIdleAnim);
                SetField(HeroMountIdleAnimField, template.HeroMountIdleAnim);
                SetField(TroopIdleAnimField, template.TroopIdleAnim);
                SetField(TroopMountIdleAnimField, template.TroopMountIdleAnim);
                SetField(ArmorValueField, template.ArmorValue);
                SetField(HealthField, template.Health);
                SetField(HeroMovementSpeedMultiplierField, template.HeroMovementSpeedMultiplier);
                SetField(HeroCombatMovementSpeedMultiplierField, template.HeroCombatMovementSpeedMultiplier);
                SetField(HeroTopSpeedReachDurationField, template.HeroTopSpeedReachDuration);
                SetField(TroopMovementSpeedMultiplierField, template.TroopMovementSpeedMultiplier);
                SetField(TroopCombatMovementSpeedMultiplierField, template.TroopCombatMovementSpeedMultiplier);
                SetField(TroopTopSpeedReachDurationField, template.TroopTopSpeedReachDuration);
                SetField(TroopMultiplierField, template.TroopMultiplier);
                SetField(TroopCostField, template.TroopCost);
                SetField(TroopCasualCostField, template.TroopCasualCost);
                SetField(TroopBattleCostField, template.TroopBattleCost);
                SetField(MeleeAiField, template.MeleeAI);
                SetField(RangedAiField, template.RangedAI);
                SetField(HeroInformationField, template.HeroInformation);
                SetField(TroopInformationField, template.TroopInformation);
                SetField(IconTypeField, template.IconType);
                SetField(PerksField, ClonePerks(template));
            }

            private void SetField(FieldInfo field, object value)
            {
                try
                {
                    field?.SetValue(this, value);
                }
                catch
                {
                }
            }

            private static List<IReadOnlyPerkObject> ClonePerks(MultiplayerClassDivisions.MPHeroClass template)
            {
                if (PerksField == null || template == null)
                    return new List<IReadOnlyPerkObject>();

                try
                {
                    List<IReadOnlyPerkObject> source = PerksField.GetValue(template) as List<IReadOnlyPerkObject>;
                    return source != null ? new List<IReadOnlyPerkObject>(source) : new List<IReadOnlyPerkObject>();
                }
                catch
                {
                    return new List<IReadOnlyPerkObject>();
                }
            }
        }
    }
}
