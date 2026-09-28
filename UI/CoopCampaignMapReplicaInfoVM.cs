using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.Library;

namespace CoopSpectator.UI
{
    internal sealed class CoopCampaignMapReplicaInfoVM : ViewModel
    {
        private bool _isVisible;
        private float _positionX;
        private float _positionY;
        private string _title = string.Empty;
        private string _typeText = string.Empty;
        private string _detailsText = string.Empty;
        private string _interactionText = string.Empty;
        private readonly Action _followHost;
        private string _cameraModeText = "Камера хоста";

        internal string DisplayedEntityId { get; private set; }

        public CoopCampaignMapReplicaInfoVM(Action followHost)
        {
            _followHost = followHost;
        }

        [DataSourceProperty]
        public string CameraModeText
        {
            get => _cameraModeText;
            private set => SetField(ref _cameraModeText, value, nameof(CameraModeText));
        }

        [DataSourceProperty]
        public string FollowHostText => "До камери хоста";

        public void ExecuteFollowHost() => _followHost?.Invoke();

        internal void SetCameraMode(bool isFollowingHost)
        {
            CameraModeText = isFollowingHost ? "Камера хоста" : "Самостійний огляд";
        }

        [DataSourceProperty]
        public bool IsVisible
        {
            get => _isVisible;
            private set => SetField(ref _isVisible, value, nameof(IsVisible));
        }

        [DataSourceProperty]
        public float PositionX
        {
            get => _positionX;
            private set => SetField(ref _positionX, value, nameof(PositionX));
        }

        [DataSourceProperty]
        public float PositionY
        {
            get => _positionY;
            private set => SetField(ref _positionY, value, nameof(PositionY));
        }

        [DataSourceProperty]
        public float PanelWidth =>
            (float)CoopCampaignMapPrototypeInteractionPolicy.InformationPanelWidth;

        [DataSourceProperty]
        public float PanelHeight =>
            (float)CoopCampaignMapPrototypeInteractionPolicy.InformationPanelHeight;

        [DataSourceProperty]
        public string Title
        {
            get => _title;
            private set => SetField(ref _title, value, nameof(Title));
        }

        [DataSourceProperty]
        public string TypeText
        {
            get => _typeText;
            private set => SetField(ref _typeText, value, nameof(TypeText));
        }

        [DataSourceProperty]
        public string DetailsText
        {
            get => _detailsText;
            private set => SetField(ref _detailsText, value, nameof(DetailsText));
        }

        [DataSourceProperty]
        public string InteractionText
        {
            get => _interactionText;
            private set => SetField(
                ref _interactionText,
                value,
                nameof(InteractionText));
        }

        [DataSourceProperty]
        public string ControlsText =>
            "WASD/стрілки — рух • коліщатко — масштаб • ПКМ — огляд • ЛКМ — відомості";

        internal void Show(
            CoopCampaignMapPrototypeCatalogEntityState catalog,
            CoopCampaignMapPrototypeDynamicEntityState dynamicState,
            Vec2 anchor,
            bool pinned,
            float screenWidth,
            float screenHeight,
            float uiScale)
        {
            if (catalog == null || dynamicState == null)
            {
                Hide();
                return;
            }

            CoopCampaignMapPrototypeInteractionPolicy.ResolveInformationPanelPosition(
                anchor.x, anchor.y, pinned, screenWidth, screenHeight, uiScale,
                out double x, out double y);
            PositionX = (float)x;
            PositionY = (float)y;
            Title = catalog.DisplayName ?? string.Empty;
            DisplayedEntityId = catalog.EntityId;
            TypeText = ResolveTypeText(catalog);
            DetailsText = BuildDetails(catalog, dynamicState);
            InteractionText = pinned
                ? "Закріплено — клацніть об’єкт ще раз, щоб відкріпити"
                : "Клацніть лівою кнопкою, щоб закріпити";
            IsVisible = true;
        }

        internal void Hide()
        {
            IsVisible = false;
            DisplayedEntityId = null;
        }

        private static string ResolveTypeText(
            CoopCampaignMapPrototypeCatalogEntityState catalog)
        {
            if (catalog.Kind != CoopCampaignMapPrototypeEntityKind.Settlement)
            {
                if (catalog.Kind == CoopCampaignMapPrototypeEntityKind.MainParty)
                    return "Головний загін";
                if (!string.IsNullOrWhiteSpace(catalog.ArmyId))
                    return catalog.IsArmyLeader
                        ? "Армія — загін ватажка"
                        : "Загін у складі армії";
                return "Мобільний загін";
            }

            switch (catalog.SettlementKind)
            {
                case CoopCampaignMapPrototypeSettlementKind.Town:
                    return "Місто";
                case CoopCampaignMapPrototypeSettlementKind.Castle:
                    return "Замок";
                case CoopCampaignMapPrototypeSettlementKind.Village:
                    return "Село";
                case CoopCampaignMapPrototypeSettlementKind.Hideout:
                    return "Схованка";
                case CoopCampaignMapPrototypeSettlementKind.Special:
                    return "Особливе місце";
                default:
                    return "Поселення";
            }
        }

        private static string BuildDetails(
            CoopCampaignMapPrototypeCatalogEntityState catalog,
            CoopCampaignMapPrototypeDynamicEntityState dynamicState)
        {
            var lines = new System.Collections.Generic.List<string>();
            if (catalog.Kind != CoopCampaignMapPrototypeEntityKind.Settlement)
                lines.Add("Чисельність: " + (dynamicState.IsPartyInformationHidden
                    ? "?" : dynamicState.PartySize.ToString()));
            if (!string.IsNullOrWhiteSpace(catalog.LeaderName))
                lines.Add("Ватажок: " + catalog.LeaderName);
            if (!string.IsNullOrWhiteSpace(catalog.OwnerName))
                lines.Add("Власник: " + catalog.OwnerName);
            if (!string.IsNullOrWhiteSpace(catalog.FactionName))
                lines.Add("Фракція: " + catalog.FactionName);
            if (!string.IsNullOrWhiteSpace(catalog.ArmyName))
            {
                lines.Add("Армія: " + catalog.ArmyName);
                if (!dynamicState.IsPartyInformationHidden && dynamicState.ArmyPartyCount > 0)
                    lines.Add("Загонів в армії: " + dynamicState.ArmyPartyCount);
                if (!dynamicState.IsPartyInformationHidden && dynamicState.ArmyTotalSize > 0)
                    lines.Add("Воїнів в армії: " + dynamicState.ArmyTotalSize);
                if (!dynamicState.IsPartyInformationHidden)
                {
                    lines.Add(
                        "Згуртованість: " +
                        Math.Round(
                            CoopCampaignMapPrototypeContract.DequantizeUnit(
                                dynamicState.ArmyCohesion) * 100d) +
                        "%");
                }
            }
            return string.Join("\n", lines);
        }

        private void SetField(ref float field, float value, string name)
        {
            if (field == value)
                return;
            field = value;
            OnPropertyChangedWithValue(value, name);
        }

        private void SetField(ref bool field, bool value, string name)
        {
            if (field == value)
                return;
            field = value;
            OnPropertyChangedWithValue(value, name);
        }

        private void SetField(ref string field, string value, string name)
        {
            string normalized = value ?? string.Empty;
            if (string.Equals(field, normalized, StringComparison.Ordinal))
                return;
            field = normalized;
            OnPropertyChangedWithValue(normalized, name);
        }
    }
}
