using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Engine;
using TaleWorlds.Engine.Screens;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.UI
{
    internal sealed class CoopCampaignMapPrototypeEntityOverlayVM : ViewModel
    {
        private readonly Dictionary<
            string,
            CoopCampaignMapPrototypeEntityNameplateVM> _nameplatesById =
                new Dictionary<
                    string,
                    CoopCampaignMapPrototypeEntityNameplateVM>(
                        StringComparer.OrdinalIgnoreCase);

        internal CoopCampaignMapPrototypeEntityOverlayVM()
        {
            Nameplates =
                new MBBindingList<CoopCampaignMapPrototypeEntityNameplateVM>();
        }

        [DataSourceProperty]
        public MBBindingList<CoopCampaignMapPrototypeEntityNameplateVM>
            Nameplates { get; }

        // The stock PartyNameplate movie supports a dedicated player template,
        // but its VM requires campaign-only hero visuals. The replicated main
        // party uses the ordinary campaign party template instead.
        [DataSourceProperty]
        public CoopCampaignMapPrototypeEntityNameplateVM PlayerNameplate => null;

        internal void Synchronize(
            IReadOnlyList<CoopCampaignMapPrototypeEntityState> entities)
        {
            var observed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (entities != null)
            {
                foreach (CoopCampaignMapPrototypeEntityState entity in entities)
                {
                    if (!CoopCampaignMapPrototypeContract.IsValidVisibleEntity(entity) ||
                        entity.Kind ==
                            CoopCampaignMapPrototypeEntityKind.Settlement ||
                        !observed.Add(entity.EntityId))
                    {
                        continue;
                    }

                    if (!_nameplatesById.TryGetValue(
                            entity.EntityId,
                            out CoopCampaignMapPrototypeEntityNameplateVM nameplate))
                    {
                        nameplate =
                            new CoopCampaignMapPrototypeEntityNameplateVM(
                                entity.EntityId);
                        nameplate.UpdateIdentity(entity);
                        _nameplatesById.Add(entity.EntityId, nameplate);
                        Nameplates.Add(nameplate);
                    }
                    else
                    {
                        nameplate.UpdateIdentity(entity);
                    }
                }
            }

            var removed = new List<string>();
            foreach (KeyValuePair<
                         string,
                         CoopCampaignMapPrototypeEntityNameplateVM> pair in
                     _nameplatesById)
            {
                if (!observed.Contains(pair.Key))
                    removed.Add(pair.Key);
            }

            foreach (string entityId in removed)
            {
                CoopCampaignMapPrototypeEntityNameplateVM nameplate =
                    _nameplatesById[entityId];
                _nameplatesById.Remove(entityId);
                Nameplates.Remove(nameplate);
                nameplate.OnFinalize();
            }
        }

        internal void UpdatePosition(
            string entityId,
            Vec3 worldPosition,
            Vec3 headWorldPosition,
            Camera camera)
        {
            if (string.IsNullOrEmpty(entityId) ||
                !_nameplatesById.TryGetValue(
                    entityId,
                    out CoopCampaignMapPrototypeEntityNameplateVM nameplate))
            {
                return;
            }
            nameplate.UpdatePosition(worldPosition, headWorldPosition, camera);
        }

        internal void HideAll()
        {
            foreach (CoopCampaignMapPrototypeEntityNameplateVM nameplate in
                     Nameplates)
            {
                nameplate.Hide();
            }
        }

        public override void OnFinalize()
        {
            foreach (CoopCampaignMapPrototypeEntityNameplateVM nameplate in
                     Nameplates)
            {
                nameplate.OnFinalize();
            }
            Nameplates.Clear();
            _nameplatesById.Clear();
            base.OnFinalize();
        }
    }

    internal sealed class CoopCampaignMapPrototypeEntityNameplateVM : ViewModel
    {
        private const string FallbackBannerCode =
            "11.163.166.1528.1528.764.764.1.0.0.133.171.171.483.483.764.764.0.0.0";
        private const uint FallbackPrimaryColor = 0xFFB89A5Au;
        private const uint FallbackSecondaryColor = 0xFFF2D078u;

        private readonly string _entityId;
        private Vec2 _position = new Vec2(-500f, -500f);
        private Vec2 _headPosition = new Vec2(-500f, -500f);
        private bool _isVisibleOnMap;
        private bool _isBehind;
        private string _count = string.Empty;
        private string _fullName = string.Empty;
        private string _factionColor = string.Empty;
        private bool _shouldShowFullName;
        private float _distanceToCamera;
        private CoopCampaignMapPrototypeBannerIdentifierVM _partyBanner;
        private string _bannerCode = string.Empty;
        private uint _primaryColor;
        private uint _secondaryColor;

        internal CoopCampaignMapPrototypeEntityNameplateVM(string entityId)
        {
            _entityId = entityId ?? string.Empty;
            Quests = new MBBindingList<CoopCampaignMapPrototypeEmptyQuestMarkerVM>();
            FactionColor = Color.FromUint(FallbackPrimaryColor).ToString();
            UpdateBanner(string.Empty, FallbackPrimaryColor, FallbackSecondaryColor);
        }

        [DataSourceProperty]
        public MBBindingList<CoopCampaignMapPrototypeEmptyQuestMarkerVM>
            Quests { get; }

        [DataSourceProperty]
        public Vec2 Position
        {
            get => _position;
            private set => SetField(ref _position, value, nameof(Position));
        }

        [DataSourceProperty]
        public Vec2 HeadPosition
        {
            get => _headPosition;
            private set => SetField(
                ref _headPosition,
                value,
                nameof(HeadPosition));
        }

        [DataSourceProperty]
        public string Count
        {
            get => _count;
            private set => SetField(ref _count, value, nameof(Count));
        }

        [DataSourceProperty]
        public string FullName
        {
            get => _fullName;
            private set => SetField(ref _fullName, value, nameof(FullName));
        }

        [DataSourceProperty]
        public string FactionColor
        {
            get => _factionColor;
            private set => SetField(
                ref _factionColor,
                value,
                nameof(FactionColor));
        }

        [DataSourceProperty]
        public bool ShouldShowFullName
        {
            get => _shouldShowFullName;
            private set => SetField(
                ref _shouldShowFullName,
                value,
                nameof(ShouldShowFullName));
        }

        [DataSourceProperty]
        public bool IsVisibleOnMap
        {
            get => _isVisibleOnMap;
            private set => SetField(
                ref _isVisibleOnMap,
                value,
                nameof(IsVisibleOnMap));
        }

        [DataSourceProperty]
        public bool IsBehind
        {
            get => _isBehind;
            private set => SetField(ref _isBehind, value, nameof(IsBehind));
        }

        [DataSourceProperty]
        public float DistanceToCamera
        {
            get => _distanceToCamera;
            private set => SetField(
                ref _distanceToCamera,
                value,
                nameof(DistanceToCamera));
        }

        [DataSourceProperty]
        public CoopCampaignMapPrototypeBannerIdentifierVM PartyBanner
        {
            get => _partyBanner;
            private set
            {
                if (ReferenceEquals(_partyBanner, value))
                    return;
                CoopCampaignMapPrototypeBannerIdentifierVM previous =
                    _partyBanner;
                _partyBanner = value;
                OnPropertyChangedWithValue(value, nameof(PartyBanner));
                previous?.OnFinalize();
            }
        }

        [DataSourceProperty]
        public string ExtraInfoText => string.Empty;

        [DataSourceProperty]
        public string MovementSpeedText => string.Empty;

        [DataSourceProperty]
        public bool IsArmy => false;

        [DataSourceProperty]
        public bool IsHigh => false;

        [DataSourceProperty]
        public bool IsInArmy => false;

        [DataSourceProperty]
        public bool IsInSettlement => false;

        [DataSourceProperty]
        public bool IsTargetedByTutorial => false;

        [DataSourceProperty]
        public bool CanParley => false;

        [DataSourceProperty]
        public bool IsDisorganized => false;

        [DataSourceProperty]
        public bool IsCurrentlyAtSea => false;

        [DataSourceProperty]
        public double Scale => 1d;

        [DataSourceProperty]
        public int NameplateOrder => 0;

        internal void UpdateIdentity(CoopCampaignMapPrototypeEntityState entity)
        {
            if (entity == null ||
                !string.Equals(
                    _entityId,
                    entity.EntityId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            bool isSettlement =
                entity.Kind == CoopCampaignMapPrototypeEntityKind.Settlement;
            Count = isSettlement || entity.PartySize <= 0
                ? string.Empty
                : entity.PartySize.ToString();
            FullName = entity.DisplayName ?? string.Empty;
            ShouldShowFullName = isSettlement;
            uint primaryColor = entity.PrimaryColor == 0u
                ? FallbackPrimaryColor
                : entity.PrimaryColor;
            uint secondaryColor = entity.SecondaryColor == 0u
                ? FallbackSecondaryColor
                : entity.SecondaryColor;
            FactionColor = Color.FromUint(primaryColor).ToString();
            UpdateBanner(entity.BannerCode, primaryColor, secondaryColor);
        }

        internal void UpdatePosition(
            Vec3 worldPosition,
            Vec3 headWorldPosition,
            Camera camera)
        {
            if (camera == null)
            {
                Hide();
                return;
            }

            try
            {
                float x = -500f;
                float y = -500f;
                float depth = 0f;
                MBWindowManager.WorldToScreenInsideUsableArea(
                    camera,
                    worldPosition,
                    ref x,
                    ref y,
                    ref depth);
                float headX = -500f;
                float headY = -500f;
                float headDepth = 0f;
                MBWindowManager.WorldToScreenInsideUsableArea(
                    camera,
                    headWorldPosition,
                    ref headX,
                    ref headY,
                    ref headDepth);
                if (!IsFinite(x) ||
                    !IsFinite(y) ||
                    !IsFinite(depth) ||
                    !IsFinite(headX) ||
                    !IsFinite(headY) ||
                    !IsFinite(headDepth))
                {
                    Hide();
                    return;
                }

                IsBehind = depth <= 0f || headDepth <= 0f;
                if (IsBehind)
                {
                    Hide();
                    return;
                }

                Position = new Vec2(x, y);
                HeadPosition = new Vec2(headX, headY);
                DistanceToCamera = depth;
                IsVisibleOnMap =
                    x >= -180f &&
                    x <= Screen.RealScreenResolutionWidth + 180f &&
                    y >= -120f &&
                    y <= Screen.RealScreenResolutionHeight + 120f;
            }
            catch
            {
                Hide();
            }
        }

        internal void Hide()
        {
            IsVisibleOnMap = false;
            IsBehind = true;
            Position = new Vec2(-500f, -500f);
            HeadPosition = new Vec2(-500f, -500f);
        }

        public override void OnFinalize()
        {
            PartyBanner = null;
            Quests.Clear();
            base.OnFinalize();
        }

        private void UpdateBanner(
            string bannerCode,
            uint primaryColor,
            uint secondaryColor)
        {
            string safeBannerCode = string.IsNullOrWhiteSpace(bannerCode)
                ? FallbackBannerCode
                : bannerCode;
            if (string.Equals(
                    _bannerCode,
                    safeBannerCode,
                    StringComparison.Ordinal) &&
                _primaryColor == primaryColor &&
                _secondaryColor == secondaryColor)
            {
                return;
            }

            CoopCampaignMapPrototypeBannerIdentifierVM identifier;
            try
            {
                identifier = new CoopCampaignMapPrototypeBannerIdentifierVM(
                    new Banner(
                        safeBannerCode,
                        primaryColor,
                        secondaryColor));
            }
            catch
            {
                safeBannerCode = FallbackBannerCode;
                primaryColor = FallbackPrimaryColor;
                secondaryColor = FallbackSecondaryColor;
                identifier = new CoopCampaignMapPrototypeBannerIdentifierVM(
                    new Banner(
                        safeBannerCode,
                        primaryColor,
                        secondaryColor));
            }

            _bannerCode = safeBannerCode;
            _primaryColor = primaryColor;
            _secondaryColor = secondaryColor;
            PartyBanner = identifier;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void SetField(ref Vec2 field, Vec2 value, string propertyName)
        {
            if (field == value)
                return;
            field = value;
            OnPropertyChangedWithValue(value, propertyName);
        }

        private void SetField(ref bool field, bool value, string propertyName)
        {
            if (field == value)
                return;
            field = value;
            OnPropertyChangedWithValue(value, propertyName);
        }

        private void SetField(ref float field, float value, string propertyName)
        {
            if (Math.Abs(field - value) <= 0.0001f)
                return;
            field = value;
            OnPropertyChangedWithValue(value, propertyName);
        }

        private void SetField(ref string field, string value, string propertyName)
        {
            string normalized = value ?? string.Empty;
            if (string.Equals(field, normalized, StringComparison.Ordinal))
                return;
            field = normalized;
            OnPropertyChangedWithValue(normalized, propertyName);
        }
    }

    internal sealed class CoopCampaignMapPrototypeBannerIdentifierVM : ViewModel
    {
        internal CoopCampaignMapPrototypeBannerIdentifierVM(Banner banner)
        {
            var identifier = new BannerImageIdentifier(banner, true);
            Id = identifier.Id ?? string.Empty;
            AdditionalArgs = identifier.AdditionalArgs ?? string.Empty;
            TextureProviderName = identifier.TextureProviderName ?? string.Empty;
        }

        [DataSourceProperty]
        public string Id { get; }

        [DataSourceProperty]
        public string AdditionalArgs { get; }

        [DataSourceProperty]
        public string TextureProviderName { get; }
    }

    internal sealed class CoopCampaignMapPrototypeEmptyQuestMarkerVM : ViewModel
    {
        [DataSourceProperty]
        public int QuestMarkerType => 0;
    }
}
