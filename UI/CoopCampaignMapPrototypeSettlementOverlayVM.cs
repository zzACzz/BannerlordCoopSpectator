using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.Screens;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.UI
{
    internal sealed class CoopCampaignMapPrototypeSettlementOverlayVM : ViewModel
    {
        private readonly Dictionary<
            string,
            CoopCampaignMapPrototypeSettlementNameplateVM> _nameplatesById =
                new Dictionary<
                    string,
                    CoopCampaignMapPrototypeSettlementNameplateVM>(
                        StringComparer.OrdinalIgnoreCase);

        internal CoopCampaignMapPrototypeSettlementOverlayVM()
        {
            SmallNameplates =
                new MBBindingList<
                    CoopCampaignMapPrototypeSettlementNameplateVM>();
            MediumNameplates =
                new MBBindingList<
                    CoopCampaignMapPrototypeSettlementNameplateVM>();
            LargeNameplates =
                new MBBindingList<
                    CoopCampaignMapPrototypeSettlementNameplateVM>();
        }

        [DataSourceProperty]
        public MBBindingList<CoopCampaignMapPrototypeSettlementNameplateVM>
            SmallNameplates { get; }

        [DataSourceProperty]
        public MBBindingList<CoopCampaignMapPrototypeSettlementNameplateVM>
            MediumNameplates { get; }

        [DataSourceProperty]
        public MBBindingList<CoopCampaignMapPrototypeSettlementNameplateVM>
            LargeNameplates { get; }

        internal void Synchronize(
            IReadOnlyList<CoopCampaignMapPrototypeEntityState> entities)
        {
            var observed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (entities != null)
            {
                foreach (CoopCampaignMapPrototypeEntityState entity in entities)
                {
                    if (!CoopCampaignMapPrototypeContract.IsValidVisibleEntity(
                            entity) ||
                        entity.Kind !=
                            CoopCampaignMapPrototypeEntityKind.Settlement ||
                        !observed.Add(entity.EntityId))
                    {
                        continue;
                    }

                    if (!_nameplatesById.TryGetValue(
                            entity.EntityId,
                            out CoopCampaignMapPrototypeSettlementNameplateVM
                                nameplate))
                    {
                        nameplate =
                            new CoopCampaignMapPrototypeSettlementNameplateVM(
                                entity.EntityId);
                        nameplate.UpdateIdentity(entity);
                        _nameplatesById.Add(entity.EntityId, nameplate);
                        GetNameplateList(nameplate.Size)?.Add(nameplate);
                    }
                    else if (nameplate.Size != entity.SettlementNameplateSize)
                    {
                        GetNameplateList(nameplate.Size)?.Remove(nameplate);
                        nameplate.UpdateIdentity(entity);
                        GetNameplateList(nameplate.Size)?.Add(nameplate);
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
                         CoopCampaignMapPrototypeSettlementNameplateVM> pair in
                     _nameplatesById)
            {
                if (!observed.Contains(pair.Key))
                    removed.Add(pair.Key);
            }

            foreach (string entityId in removed)
            {
                CoopCampaignMapPrototypeSettlementNameplateVM nameplate =
                    _nameplatesById[entityId];
                _nameplatesById.Remove(entityId);
                GetNameplateList(nameplate.Size)?.Remove(nameplate);
                nameplate.OnFinalize();
            }
        }

        internal void Upsert(CoopCampaignMapPrototypeEntityState entity)
        {
            if (!CoopCampaignMapPrototypeContract.IsValidVisibleEntity(entity) ||
                entity.Kind != CoopCampaignMapPrototypeEntityKind.Settlement)
            {
                return;
            }

            if (!_nameplatesById.TryGetValue(
                    entity.EntityId,
                    out CoopCampaignMapPrototypeSettlementNameplateVM nameplate))
            {
                nameplate = new CoopCampaignMapPrototypeSettlementNameplateVM(
                    entity.EntityId);
                nameplate.UpdateIdentity(entity);
                _nameplatesById.Add(entity.EntityId, nameplate);
                GetNameplateList(nameplate.Size)?.Add(nameplate);
                return;
            }

            if (nameplate.Size != entity.SettlementNameplateSize)
            {
                GetNameplateList(nameplate.Size)?.Remove(nameplate);
                nameplate.UpdateIdentity(entity);
                GetNameplateList(nameplate.Size)?.Add(nameplate);
                return;
            }
            nameplate.UpdateIdentity(entity);
        }

        internal void Remove(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId) ||
                !_nameplatesById.TryGetValue(
                    entityId,
                    out CoopCampaignMapPrototypeSettlementNameplateVM nameplate))
            {
                return;
            }

            _nameplatesById.Remove(entityId);
            GetNameplateList(nameplate.Size)?.Remove(nameplate);
            nameplate.OnFinalize();
        }

        internal void UpdatePosition(
            string entityId,
            Vec3 worldPosition,
            Camera camera)
        {
            if (string.IsNullOrEmpty(entityId) ||
                !_nameplatesById.TryGetValue(
                    entityId,
                    out CoopCampaignMapPrototypeSettlementNameplateVM nameplate))
            {
                return;
            }
            nameplate.UpdatePosition(worldPosition, camera);
        }

        internal void Hide(string entityId)
        {
            if (string.IsNullOrEmpty(entityId) ||
                !_nameplatesById.TryGetValue(
                    entityId,
                    out CoopCampaignMapPrototypeSettlementNameplateVM nameplate))
            {
                return;
            }
            nameplate.Hide();
        }

        internal void HideAll()
        {
            foreach (CoopCampaignMapPrototypeSettlementNameplateVM nameplate in
                     _nameplatesById.Values)
            {
                nameplate.Hide();
            }
        }

        public override void OnFinalize()
        {
            foreach (CoopCampaignMapPrototypeSettlementNameplateVM nameplate in
                     _nameplatesById.Values)
            {
                nameplate.OnFinalize();
            }
            SmallNameplates.Clear();
            MediumNameplates.Clear();
            LargeNameplates.Clear();
            _nameplatesById.Clear();
            base.OnFinalize();
        }

        private MBBindingList<CoopCampaignMapPrototypeSettlementNameplateVM>
            GetNameplateList(
                CoopCampaignMapPrototypeSettlementNameplateSize size)
        {
            switch (size)
            {
                case CoopCampaignMapPrototypeSettlementNameplateSize.Small:
                    return SmallNameplates;
                case CoopCampaignMapPrototypeSettlementNameplateSize.Medium:
                    return MediumNameplates;
                case CoopCampaignMapPrototypeSettlementNameplateSize.Large:
                    return LargeNameplates;
                default:
                    return null;
            }
        }
    }

    internal sealed class CoopCampaignMapPrototypeSettlementNameplateVM :
        ViewModel
    {
        private const string FallbackBannerCode =
            "11.163.166.1528.1528.764.764.1.0.0.133.171.171.483.483.764.764.0.0.0";
        private const uint FallbackPrimaryColor = 0xFFB89A5Au;
        private const uint FallbackSecondaryColor = 0xFFF2D078u;

        private readonly string _entityId;
        private Vec2 _position = new Vec2(-1000f, -1000f);
        private bool _isVisibleOnMap;
        private bool _isInside;
        private int _wSign = -1;
        private float _wPos = -1f;
        private float _distanceToCamera;
        private string _name = string.Empty;
        private string _factionColor = string.Empty;
        private CoopCampaignMapPrototypeBannerIdentifierVM _banner;
        private string _bannerCode = string.Empty;
        private uint _primaryColor;
        private uint _secondaryColor;

        internal CoopCampaignMapPrototypeSettlementNameplateVM(string entityId)
        {
            _entityId = entityId ?? string.Empty;
            SettlementNotifications =
                new CoopCampaignMapPrototypeSettlementNotificationsVM();
            SettlementEvents =
                new CoopCampaignMapPrototypeSettlementEventsVM();
            SettlementParties =
                new CoopCampaignMapPrototypeSettlementPartiesVM();
            FactionColor = Color.FromUint(FallbackPrimaryColor).ToString();
            UpdateBanner(
                string.Empty,
                FallbackPrimaryColor,
                FallbackSecondaryColor);
        }

        internal CoopCampaignMapPrototypeSettlementNameplateSize Size
        {
            get;
            private set;
        }

        [DataSourceProperty]
        public Vec2 Position
        {
            get => _position;
            private set => SetField(ref _position, value, nameof(Position));
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
        public bool IsInside
        {
            get => _isInside;
            private set => SetField(ref _isInside, value, nameof(IsInside));
        }

        [DataSourceProperty]
        public int WSign
        {
            get => _wSign;
            private set => SetField(ref _wSign, value, nameof(WSign));
        }

        [DataSourceProperty]
        public float WPos
        {
            get => _wPos;
            private set => SetField(ref _wPos, value, nameof(WPos));
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
        public string Name
        {
            get => _name;
            private set => SetField(ref _name, value, nameof(Name));
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
        public CoopCampaignMapPrototypeBannerIdentifierVM Banner
        {
            get => _banner;
            private set
            {
                if (ReferenceEquals(_banner, value))
                    return;
                CoopCampaignMapPrototypeBannerIdentifierVM previous = _banner;
                _banner = value;
                OnPropertyChangedWithValue(value, nameof(Banner));
                previous?.OnFinalize();
            }
        }

        [DataSourceProperty]
        public CoopCampaignMapPrototypeSettlementNotificationsVM
            SettlementNotifications { get; }

        [DataSourceProperty]
        public CoopCampaignMapPrototypeSettlementEventsVM SettlementEvents
        {
            get;
        }

        [DataSourceProperty]
        public CoopCampaignMapPrototypeSettlementPartiesVM SettlementParties
        {
            get;
        }

        [DataSourceProperty]
        public int Relation => 0;

        [DataSourceProperty]
        public bool IsTracked => false;

        [DataSourceProperty]
        public bool IsInRange => false;

        [DataSourceProperty]
        public bool IsTargetedByTutorial => false;

        [DataSourceProperty]
        public bool CanParley => false;

        [DataSourceProperty]
        public bool HasPort => false;

        [DataSourceProperty]
        public int PortLevel => 0;

        [DataSourceProperty]
        public int MapEventVisualType => 0;

        [DataSourceProperty]
        public bool IsTrackerHighlightEnabled => false;

        [DataSourceProperty]
        public int SettlementType => Math.Max(0, (int)Size - 1);

        [DataSourceProperty]
        public double Scale => 1d;

        [DataSourceProperty]
        public int NameplateOrder => 0;

        public void ExecuteOpenEncyclopedia()
        {
        }

        public void ExecuteSetCameraPosition()
        {
        }

        public void ExecuteTrack()
        {
        }

        internal void UpdateIdentity(CoopCampaignMapPrototypeEntityState entity)
        {
            if (entity == null ||
                entity.Kind != CoopCampaignMapPrototypeEntityKind.Settlement ||
                !string.Equals(
                    _entityId,
                    entity.EntityId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Size = entity.SettlementNameplateSize;
            Name = entity.DisplayName ?? string.Empty;
            uint primaryColor = entity.PrimaryColor == 0u
                ? FallbackPrimaryColor
                : entity.PrimaryColor;
            uint secondaryColor = entity.SecondaryColor == 0u
                ? FallbackSecondaryColor
                : entity.SecondaryColor;
            FactionColor = Color.FromUint(primaryColor).ToString();
            UpdateBanner(entity.BannerCode, primaryColor, secondaryColor);
        }

        internal void UpdatePosition(Vec3 worldPosition, Camera camera)
        {
            if (camera == null)
            {
                Hide();
                return;
            }

            try
            {
                float heightOffset = ResolveHeightOffset(camera.Position.z);
                Vec3 anchor = worldPosition + Vec3.Up * heightOffset;
                float x = -1000f;
                float y = -1000f;
                float depth = 0f;
                MBWindowManager.WorldToScreenInsideUsableArea(
                    camera,
                    anchor,
                    ref x,
                    ref y,
                    ref depth);
                if (!IsFinite(x) || !IsFinite(y) || !IsFinite(depth))
                {
                    Hide();
                    return;
                }

                bool isInFront = depth >= 0f;
                float resolutionScale =
                    Screen.RealScreenResolutionWidth * 0.00052083336f;
                bool isInside =
                    x <= Screen.RealScreenResolutionWidth +
                         200f * resolutionScale &&
                    y <= Screen.RealScreenResolutionHeight +
                         100f * resolutionScale &&
                    x + 200f * resolutionScale >= 0f &&
                    y + 100f * resolutionScale >= 0f;

                Position = isInFront && isInside
                    ? new Vec2(x, y)
                    : new Vec2(-1000f, -1000f);
                IsInside = isInside;
                float distanceToCamera =
                    worldPosition.Distance(camera.Position);
                IsVisibleOnMap =
                    CoopCampaignMapPrototypeVisibilityPolicy
                        .ShouldShowSettlement(
                            Size,
                            camera.Position.z,
                            distanceToCamera,
                            isInFront,
                            isInside);
                WPos = isInFront ? 1.1f : -1f;
                WSign = isInFront ? 1 : -1;
                DistanceToCamera = distanceToCamera;
            }
            catch
            {
                Hide();
            }
        }

        internal void Hide()
        {
            Position = new Vec2(-1000f, -1000f);
            IsInside = false;
            IsVisibleOnMap = false;
            WPos = -1f;
            WSign = -1;
        }

        public override void OnFinalize()
        {
            Banner = null;
            SettlementNotifications.OnFinalize();
            SettlementEvents.OnFinalize();
            SettlementParties.OnFinalize();
            base.OnFinalize();
        }

        private float ResolveHeightOffset(float cameraHeight)
        {
            float normalizedHeight = Math.Max(
                0f,
                Math.Min(1f, cameraHeight / 30f));
            switch (Size)
            {
                case CoopCampaignMapPrototypeSettlementNameplateSize.Medium:
                    return 0.5f + normalizedHeight * 3f;
                case CoopCampaignMapPrototypeSettlementNameplateSize.Large:
                    return 0.5f + normalizedHeight * 6f;
                default:
                    return 0.5f + normalizedHeight * 2.5f;
            }
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
            Banner = identifier;
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

        private void SetField(ref int field, int value, string propertyName)
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

    internal sealed class CoopCampaignMapPrototypeSettlementNotificationsVM :
        ViewModel
    {
        internal CoopCampaignMapPrototypeSettlementNotificationsVM()
        {
            Notifications =
                new MBBindingList<
                    CoopCampaignMapPrototypeSettlementEmptyItemVM>();
        }

        [DataSourceProperty]
        public MBBindingList<CoopCampaignMapPrototypeSettlementEmptyItemVM>
            Notifications { get; }

        public override void OnFinalize()
        {
            Notifications.Clear();
            base.OnFinalize();
        }
    }

    internal sealed class CoopCampaignMapPrototypeSettlementEventsVM : ViewModel
    {
        internal CoopCampaignMapPrototypeSettlementEventsVM()
        {
            EventsList =
                new MBBindingList<
                    CoopCampaignMapPrototypeSettlementEmptyItemVM>();
            TrackQuests =
                new MBBindingList<
                    CoopCampaignMapPrototypeSettlementEmptyItemVM>();
        }

        [DataSourceProperty]
        public MBBindingList<CoopCampaignMapPrototypeSettlementEmptyItemVM>
            EventsList { get; }

        [DataSourceProperty]
        public MBBindingList<CoopCampaignMapPrototypeSettlementEmptyItemVM>
            TrackQuests { get; }

        public override void OnFinalize()
        {
            EventsList.Clear();
            TrackQuests.Clear();
            base.OnFinalize();
        }
    }

    internal sealed class CoopCampaignMapPrototypeSettlementPartiesVM : ViewModel
    {
        internal CoopCampaignMapPrototypeSettlementPartiesVM()
        {
            PartiesInSettlement =
                new MBBindingList<
                    CoopCampaignMapPrototypeSettlementEmptyItemVM>();
        }

        [DataSourceProperty]
        public MBBindingList<CoopCampaignMapPrototypeSettlementEmptyItemVM>
            PartiesInSettlement { get; }

        public override void OnFinalize()
        {
            PartiesInSettlement.Clear();
            base.OnFinalize();
        }
    }

    internal sealed class CoopCampaignMapPrototypeSettlementEmptyItemVM :
        ViewModel
    {
        [DataSourceProperty]
        public int QuestMarkerType => 0;
    }
}
