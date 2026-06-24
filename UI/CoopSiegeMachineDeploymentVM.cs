using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Missions;
using TaleWorlds.MountAndBlade.Objects.Siege;

namespace CoopSpectator.UI
{
    public sealed class CoopSiegeMachineDeploymentVM : ViewModel
    {
        private readonly Mission _mission;
        private readonly BattleSideEnum _side;
        private readonly CoopSiegeMachineDeploymentControllerVM _deploymentController;
        private bool _isDeployment = true;
        private bool _isToggleOrderShown;

        public CoopSiegeMachineDeploymentVM(Mission mission, BattleSideEnum side, Camera deploymentCamera)
        {
            _mission = mission;
            _side = side;
            _deploymentController = new CoopSiegeMachineDeploymentControllerVM(mission, side, deploymentCamera);
        }

        [DataSourceProperty]
        public bool IsDeployment
        {
            get => _isDeployment;
            private set
            {
                if (value != _isDeployment)
                {
                    _isDeployment = value;
                    OnPropertyChangedWithValue(value, nameof(IsDeployment));
                }
            }
        }

        [DataSourceProperty]
        public bool IsToggleOrderShown
        {
            get => _isToggleOrderShown;
            private set
            {
                if (value != _isToggleOrderShown)
                {
                    _isToggleOrderShown = value;
                    OnPropertyChangedWithValue(value, nameof(IsToggleOrderShown));
                }
            }
        }

        [DataSourceProperty]
        public CoopSiegeMachineDeploymentControllerVM DeploymentController => _deploymentController;

        public bool HasDeploymentTargets => _deploymentController.DeploymentTargets.Count > 0;

        public void Tick(Camera deploymentCamera)
        {
            if (_mission == null || _side == BattleSideEnum.None)
                return;

            IsDeployment = true;
            IsToggleOrderShown = false;
            _deploymentController.Tick(deploymentCamera);
        }

        public void ExecuteCancelSelectedDeploymentPoint()
        {
            _deploymentController.ExecuteCancelSelectedDeploymentPoint();
        }

        public override void OnFinalize()
        {
            _deploymentController.OnFinalize();
            base.OnFinalize();
        }
    }

    public sealed class CoopSiegeMachineDeploymentControllerVM : ViewModel
    {
        private readonly Mission _mission;
        private readonly BattleSideEnum _side;
        private readonly Dictionary<DeploymentPoint, SiegeWeapon> _localSelections =
            new Dictionary<DeploymentPoint, SiegeWeapon>();
        private Camera _deploymentCamera;
        private MBBindingList<CoopDeploymentSiegeMachineVM> _deploymentTargets =
            new MBBindingList<CoopDeploymentSiegeMachineVM>();
        private MBBindingList<CoopDeploymentSiegeMachineVM> _siegeDeploymentList =
            new MBBindingList<CoopDeploymentSiegeMachineVM>();
        private CoopDeploymentSiegeMachineVM _selectedDeploymentPointVm;
        private bool _isSiegeDeploymentListActive;

        public CoopSiegeMachineDeploymentControllerVM(Mission mission, BattleSideEnum side, Camera deploymentCamera)
        {
            _mission = mission;
            _side = side;
            _deploymentCamera = deploymentCamera;
            RebuildDeploymentTargets();
        }

        [DataSourceProperty]
        public MBBindingList<CoopDeploymentSiegeMachineVM> DeploymentTargets
        {
            get => _deploymentTargets;
            private set
            {
                if (value != _deploymentTargets)
                {
                    _deploymentTargets = value;
                    OnPropertyChanged(nameof(DeploymentTargets));
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<CoopDeploymentSiegeMachineVM> SiegeDeploymentList
        {
            get => _siegeDeploymentList;
            private set
            {
                if (value != _siegeDeploymentList)
                {
                    _siegeDeploymentList = value;
                    OnPropertyChanged(nameof(SiegeDeploymentList));
                }
            }
        }

        [DataSourceProperty]
        public bool IsSiegeDeploymentListActive
        {
            get => _isSiegeDeploymentListActive;
            private set
            {
                if (value != _isSiegeDeploymentListActive)
                {
                    _isSiegeDeploymentListActive = value;
                    OnPropertyChangedWithValue(value, nameof(IsSiegeDeploymentListActive));
                    if (_selectedDeploymentPointVm != null)
                        _selectedDeploymentPointVm.IsSelected = value;
                }
            }
        }

        public void Tick(Camera deploymentCamera)
        {
            if (deploymentCamera != null)
                _deploymentCamera = deploymentCamera;

            RefreshDeploymentTargetMachines();
            for (int i = 0; i < DeploymentTargets.Count; i++)
                DeploymentTargets[i].Update(_deploymentCamera);
        }

        public void SelectDeploymentPoint(CoopDeploymentSiegeMachineVM target)
        {
            if (target == null || target.DeploymentPoint == null)
            {
                ExecuteCancelSelectedDeploymentPoint();
                return;
            }

            IsSiegeDeploymentListActive = false;
            if (_selectedDeploymentPointVm != null && !ReferenceEquals(_selectedDeploymentPointVm, target))
                _selectedDeploymentPointVm.IsSelected = false;

            _selectedDeploymentPointVm = target;
            _selectedDeploymentPointVm.IsSelected = true;
            RebuildSiegeDeploymentList(target.DeploymentPoint);
            IsSiegeDeploymentListActive = SiegeDeploymentList.Count > 0;
        }

        public void SelectSiegeMachine(CoopDeploymentSiegeMachineVM option)
        {
            if (option == null || option.DeploymentPoint == null)
            {
                ExecuteCancelSelectedDeploymentPoint();
                return;
            }

            bool clearSelection = option.SiegeWeapon == null;
            bool sent = CoopBattleNetworkRequestTransport.TrySyncCommanderDeploymentSiegeMachineSelection(
                _side,
                option.DeploymentPoint,
                option.SiegeWeapon,
                clearSelection,
                "CoopSiegeMachineDeploymentVM.SelectSiegeMachine");
            if (sent)
            {
                ApplyLocalSelectionState(option.DeploymentPoint, option.SiegeWeapon, clearSelection);
                ExactCampaignSiegeAssaultWithDeploymentRuntime.TryApplyCommanderDeploymentSiegeMachineSelectionLocally(
                    _mission,
                    _side,
                    option.DeploymentPoint,
                    option.SiegeWeapon,
                    clearSelection,
                    out string _);
                RefreshDeploymentTargetMachines();
            }

            IsSiegeDeploymentListActive = false;
            SiegeDeploymentList.Clear();
            if (_selectedDeploymentPointVm != null)
                _selectedDeploymentPointVm.IsSelected = false;
            _selectedDeploymentPointVm = null;
        }

        public void ExecuteCancelSelectedDeploymentPoint()
        {
            IsSiegeDeploymentListActive = false;
            SiegeDeploymentList.Clear();
            if (_selectedDeploymentPointVm != null)
                _selectedDeploymentPointVm.IsSelected = false;
            _selectedDeploymentPointVm = null;
        }

        public override void OnFinalize()
        {
            SiegeDeploymentList.Clear();
            DeploymentTargets.Clear();
            _localSelections.Clear();
            _selectedDeploymentPointVm = null;
            base.OnFinalize();
        }

        private void ApplyLocalSelectionState(
            DeploymentPoint deploymentPoint,
            SiegeWeapon siegeWeapon,
            bool clearSelection)
        {
            if (deploymentPoint == null)
                return;

            if (clearSelection)
            {
                _localSelections.Remove(deploymentPoint);
                return;
            }

            Type selectedWeaponType = ResolveWeaponType(siegeWeapon);
            if (selectedWeaponType != null)
            {
                List<DeploymentPoint> pointsToClear = _localSelections
                    .Where(pair => !ReferenceEquals(pair.Key, deploymentPoint) &&
                                   ResolveWeaponType(pair.Value) == selectedWeaponType)
                    .Select(pair => pair.Key)
                    .ToList();
                foreach (DeploymentPoint pointToClear in pointsToClear)
                    _localSelections.Remove(pointToClear);
            }

            _localSelections[deploymentPoint] = siegeWeapon;
        }

        private void RebuildDeploymentTargets()
        {
            var targets = new MBBindingList<CoopDeploymentSiegeMachineVM>();
            foreach (DeploymentPoint deploymentPoint in CollectDeploymentPoints())
            {
                List<SiegeWeapon> weapons = CollectDeployableWeapons(deploymentPoint);
                if (weapons.Count <= 0)
                    continue;

                SiegeWeapon selectedWeapon = ResolveSelectedWeapon(deploymentPoint);
                targets.Add(CoopDeploymentSiegeMachineVM.CreateTarget(
                    this,
                    deploymentPoint,
                    selectedWeapon,
                    selectedWeapon ?? weapons.FirstOrDefault()));
            }

            DeploymentTargets = targets;
        }

        private void RebuildSiegeDeploymentList(DeploymentPoint deploymentPoint)
        {
            var options = new MBBindingList<CoopDeploymentSiegeMachineVM>();
            SiegeWeapon currentWeapon = ResolveSelectedWeapon(deploymentPoint);
            List<SiegeWeapon> weapons = CollectDeployableWeapons(deploymentPoint);
            foreach (SiegeWeapon weapon in weapons)
            {
                int remainingCount = GetRemainingCountForOption(deploymentPoint, weapon, currentWeapon);
                bool isCurrent = ReferenceEquals(currentWeapon, weapon);
                if (remainingCount > 0 || isCurrent)
                {
                    options.Add(CoopDeploymentSiegeMachineVM.CreateOption(
                        this,
                        deploymentPoint,
                        weapon,
                        remainingCount,
                        isCurrent));
                }
            }

            options.Add(CoopDeploymentSiegeMachineVM.CreateOption(
                this,
                deploymentPoint,
                null,
                -1,
                currentWeapon == null));
            SiegeDeploymentList = options;
        }

        private void RefreshDeploymentTargetMachines()
        {
            for (int i = 0; i < DeploymentTargets.Count; i++)
            {
                CoopDeploymentSiegeMachineVM target = DeploymentTargets[i];
                SiegeWeapon selectedWeapon = ResolveSelectedWeapon(target.DeploymentPoint);
                SiegeWeapon displayTypeWeapon = selectedWeapon ?? CollectDeployableWeapons(target.DeploymentPoint).FirstOrDefault();
                target.RefreshWithSelectedWeapon(selectedWeapon, displayTypeWeapon);
            }
        }

        private IEnumerable<DeploymentPoint> CollectDeploymentPoints()
        {
            if (_mission?.ActiveMissionObjects == null)
                yield break;

            IEnumerable<DeploymentPoint> points;
            try
            {
                points = _mission.ActiveMissionObjects.FindAllWithType<DeploymentPoint>();
            }
            catch
            {
                yield break;
            }

            foreach (DeploymentPoint deploymentPoint in points)
            {
                if (deploymentPoint == null ||
                    deploymentPoint.IsDisabled ||
                    deploymentPoint.Side != _side)
                {
                    continue;
                }

                yield return deploymentPoint;
            }
        }

        private List<SiegeWeapon> CollectDeployableWeapons(DeploymentPoint deploymentPoint)
        {
            var result = new List<SiegeWeapon>();
            if (deploymentPoint == null)
                return result;

            AddDeployableWeapons(result, SafeEnumerateDeployableWeapons(deploymentPoint));
            if (result.Count <= 0)
                AddDeployableWeapons(result, SafeGetWeaponsUnder(deploymentPoint));

            SiegeWeapon selectedWeapon = ResolveSelectedWeapon(deploymentPoint);
            return result
                .Where(weapon => weapon != null && !weapon.IsDisabled && weapon.Side == deploymentPoint.Side)
                .Where(weapon => IsCampaignAllowedWeapon(weapon) || ReferenceEquals(weapon, selectedWeapon))
                .Distinct()
                .ToList();
        }

        private static IEnumerable<SynchedMissionObject> SafeEnumerateDeployableWeapons(DeploymentPoint deploymentPoint)
        {
            try
            {
                return deploymentPoint.DeployableWeapons?.ToList() ?? new List<SynchedMissionObject>();
            }
            catch
            {
                return new List<SynchedMissionObject>();
            }
        }

        private static IEnumerable<SynchedMissionObject> SafeGetWeaponsUnder(DeploymentPoint deploymentPoint)
        {
            try
            {
                return deploymentPoint.GetWeaponsUnder()?.ToList() ?? new List<SynchedMissionObject>();
            }
            catch
            {
                return new List<SynchedMissionObject>();
            }
        }

        private static void AddDeployableWeapons(
            ICollection<SiegeWeapon> output,
            IEnumerable<SynchedMissionObject> candidates)
        {
            if (output == null || candidates == null)
                return;

            foreach (SynchedMissionObject candidate in candidates)
            {
                if (candidate is SiegeWeapon siegeWeapon && !output.Contains(siegeWeapon))
                    output.Add(siegeWeapon);
            }
        }

        private SiegeWeapon ResolveSelectedWeapon(DeploymentPoint deploymentPoint)
        {
            if (deploymentPoint == null)
                return null;

            if (_localSelections.TryGetValue(deploymentPoint, out SiegeWeapon selectedWeapon))
                return selectedWeapon;

            try
            {
                return deploymentPoint.DeployedWeapon as SiegeWeapon;
            }
            catch
            {
                return null;
            }
        }

        private bool IsCampaignAllowedWeapon(SiegeWeapon weapon)
        {
            int maxCount = GetMaxDeployableWeaponCount(ResolveWeaponType(weapon));
            return maxCount > 0 || maxCount < 0;
        }

        private int GetRemainingCountForOption(
            DeploymentPoint deploymentPoint,
            SiegeWeapon optionWeapon,
            SiegeWeapon currentWeapon)
        {
            Type weaponType = ResolveWeaponType(optionWeapon);
            int maxCount = GetMaxDeployableWeaponCount(weaponType);
            if (maxCount < 0)
                return 1;

            int selectedCount = CountSelectedWeaponsOfType(weaponType);
            int remaining = Math.Max(0, maxCount - selectedCount);
            if (currentWeapon != null && ResolveWeaponType(currentWeapon) == weaponType)
                remaining++;
            return remaining;
        }

        private int CountSelectedWeaponsOfType(Type weaponType)
        {
            if (weaponType == null)
                return 0;

            int count = 0;
            foreach (DeploymentPoint deploymentPoint in CollectDeploymentPoints())
            {
                SiegeWeapon selectedWeapon = ResolveSelectedWeapon(deploymentPoint);
                if (selectedWeapon != null && ResolveWeaponType(selectedWeapon) == weaponType)
                    count++;
            }

            return count;
        }

        private int GetMaxDeployableWeaponCount(Type weaponType)
        {
            if (weaponType == null)
                return 0;

            try
            {
                MissionSiegeEnginesLogic siegeEnginesLogic = _mission?.GetMissionBehavior<MissionSiegeEnginesLogic>();
                IMissionSiegeWeaponsController weaponsController = siegeEnginesLogic?.GetSiegeWeaponsController(_side);
                if (weaponsController == null)
                    return -1;

                return weaponsController.GetMaxDeployableWeaponCount(weaponType);
            }
            catch
            {
                return 0;
            }
        }

        private static Type ResolveWeaponType(SiegeWeapon siegeWeapon)
        {
            if (siegeWeapon == null)
                return null;

            try
            {
                return MissionSiegeWeaponsController.GetWeaponType(siegeWeapon);
            }
            catch
            {
                return siegeWeapon.GetType();
            }
        }
    }

    public sealed class CoopDeploymentSiegeMachineVM : ViewModel
    {
        private readonly CoopSiegeMachineDeploymentControllerVM _controller;
        private readonly bool _isOption;
        private Vec3 _worldPos;
        private float _latestX;
        private float _latestY;
        private string _machineClass = string.Empty;
        private int _remainingCount = -1;
        private bool _isSelected;
        private bool _isPlayerGeneral = true;
        private int _type;
        private bool _isInside = true;
        private bool _isInFront = true;
        private string _breachedText = "BREACHED";
        private Vec2 _position;

        private CoopDeploymentSiegeMachineVM(
            CoopSiegeMachineDeploymentControllerVM controller,
            DeploymentPoint deploymentPoint,
            SiegeWeapon siegeWeapon,
            SiegeWeapon displayTypeWeapon,
            bool isOption,
            int remainingCount,
            bool isSelected)
        {
            _controller = controller;
            DeploymentPoint = deploymentPoint;
            SiegeWeapon = siegeWeapon;
            _isOption = isOption;
            RemainingCount = remainingCount;
            IsSelected = isSelected;
            Type = ResolveDeploymentPointType(deploymentPoint, displayTypeWeapon ?? siegeWeapon);
            _worldPos = ResolveWorldPosition(deploymentPoint);
            RefreshWithSelectedWeapon(siegeWeapon, displayTypeWeapon);
        }

        public DeploymentPoint DeploymentPoint { get; }

        public SiegeWeapon SiegeWeapon { get; private set; }

        [DataSourceProperty]
        public int Type
        {
            get => _type;
            private set
            {
                if (value != _type)
                {
                    _type = value;
                    OnPropertyChangedWithValue(value, nameof(Type));
                }
            }
        }

        [DataSourceProperty]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (value != _isSelected)
                {
                    _isSelected = value;
                    OnPropertyChangedWithValue(value, nameof(IsSelected));
                }
            }
        }

        [DataSourceProperty]
        public bool IsPlayerGeneral
        {
            get => _isPlayerGeneral;
            private set
            {
                if (value != _isPlayerGeneral)
                {
                    _isPlayerGeneral = value;
                    OnPropertyChangedWithValue(value, nameof(IsPlayerGeneral));
                }
            }
        }

        [DataSourceProperty]
        public string MachineClass
        {
            get => _machineClass;
            private set
            {
                if (value != _machineClass)
                {
                    _machineClass = value;
                    OnPropertyChangedWithValue(value, nameof(MachineClass));
                }
            }
        }

        [DataSourceProperty]
        public string BreachedText
        {
            get => _breachedText;
            private set
            {
                if (value != _breachedText)
                {
                    _breachedText = value;
                    OnPropertyChangedWithValue(value, nameof(BreachedText));
                }
            }
        }

        [DataSourceProperty]
        public int RemainingCount
        {
            get => _remainingCount;
            private set
            {
                if (value != _remainingCount)
                {
                    _remainingCount = value;
                    OnPropertyChangedWithValue(value, nameof(RemainingCount));
                }
            }
        }

        [DataSourceProperty]
        public bool IsInside
        {
            get => _isInside;
            private set
            {
                if (value != _isInside)
                {
                    _isInside = value;
                    OnPropertyChangedWithValue(value, nameof(IsInside));
                }
            }
        }

        [DataSourceProperty]
        public bool IsInFront
        {
            get => _isInFront;
            private set
            {
                if (value != _isInFront)
                {
                    _isInFront = value;
                    OnPropertyChangedWithValue(value, nameof(IsInFront));
                }
            }
        }

        [DataSourceProperty]
        public Vec2 Position
        {
            get => _position;
            private set
            {
                if (_position != value)
                {
                    _position = value;
                    OnPropertyChangedWithValue(value, nameof(Position));
                }
            }
        }

        public static CoopDeploymentSiegeMachineVM CreateTarget(
            CoopSiegeMachineDeploymentControllerVM controller,
            DeploymentPoint deploymentPoint,
            SiegeWeapon deployedWeapon,
            SiegeWeapon displayTypeWeapon)
        {
            return new CoopDeploymentSiegeMachineVM(
                controller,
                deploymentPoint,
                deployedWeapon,
                displayTypeWeapon,
                isOption: false,
                remainingCount: -1,
                isSelected: deploymentPoint?.IsDeployed == true);
        }

        public static CoopDeploymentSiegeMachineVM CreateOption(
            CoopSiegeMachineDeploymentControllerVM controller,
            DeploymentPoint deploymentPoint,
            SiegeWeapon siegeWeapon,
            int remainingCount,
            bool isSelected)
        {
            return new CoopDeploymentSiegeMachineVM(
                controller,
                deploymentPoint,
                siegeWeapon,
                siegeWeapon,
                isOption: true,
                remainingCount: remainingCount,
                isSelected: isSelected);
        }

        public void Update(Camera deploymentCamera)
        {
            if (_isOption || deploymentCamera == null)
                return;

            CalculatePosition(deploymentCamera);
            RefreshPosition();
        }

        public void ExecuteAction()
        {
            if (_isOption)
                _controller.SelectSiegeMachine(this);
            else
                _controller.SelectDeploymentPoint(this);
        }

        public void ExecuteFocusBegin()
        {
        }

        public void ExecuteFocusEnd()
        {
        }

        public void RefreshWithSelectedWeapon(SiegeWeapon siegeWeapon, SiegeWeapon displayTypeWeapon = null)
        {
            SiegeWeapon = siegeWeapon;
            Type = ResolveDeploymentPointType(DeploymentPoint, displayTypeWeapon ?? siegeWeapon);
            MachineClass = ResolveMachineClass(siegeWeapon);
            IsPlayerGeneral = true;
        }

        private void CalculatePosition(Camera deploymentCamera)
        {
            _latestX = 0f;
            _latestY = 0f;
            MatrixFrame viewProj = MatrixFrame.Identity;
            deploymentCamera.GetViewProjMatrix(ref viewProj);
            Vec3 worldPos = _worldPos;
            worldPos.z += 8f;
            worldPos.w = 1f;
            Vec3 projected = worldPos * viewProj;
            IsInFront = projected.w > 0f;
            if (Math.Abs(projected.w) < 0.0001f)
                return;

            projected.x /= projected.w;
            projected.y /= projected.w;
            projected.z /= projected.w;
            projected.w /= projected.w;
            projected *= 0.5f;
            projected.x += 0.5f;
            projected.y += 0.5f;
            projected.y = 1f - projected.y;
            _latestX = projected.x * TaleWorlds.Engine.Screen.RealScreenResolutionWidth;
            _latestY = projected.y * TaleWorlds.Engine.Screen.RealScreenResolutionHeight;
        }

        private void RefreshPosition()
        {
            IsInside = IsInsideWindow();
            Position = new Vec2(_latestX, _latestY);
        }

        private bool IsInsideWindow()
        {
            if (_latestX > TaleWorlds.Engine.Screen.RealScreenResolutionWidth ||
                _latestY > TaleWorlds.Engine.Screen.RealScreenResolutionHeight ||
                _latestX + 200f < 0f)
            {
                return false;
            }

            return !(_latestY + 100f < 0f);
        }

        private static int ResolveDeploymentPointType(DeploymentPoint deploymentPoint, SiegeWeapon displayTypeWeapon)
        {
            Type weaponType = ResolveWeaponType(displayTypeWeapon);
            if (weaponType == typeof(BatteringRam))
                return (int)DeploymentPoint.DeploymentPointType.BatteringRam;
            if (weaponType == typeof(SiegeTower))
                return (int)DeploymentPoint.DeploymentPointType.TowerLadder;
            if (weaponType != null)
                return (int)DeploymentPoint.DeploymentPointType.Ranged;

            try
            {
                return deploymentPoint == null ? 0 : (int)deploymentPoint.GetDeploymentPointType();
            }
            catch
            {
                return 0;
            }
        }

        private static Vec3 ResolveWorldPosition(DeploymentPoint deploymentPoint)
        {
            try
            {
                return deploymentPoint?.GameEntity.GlobalPosition ?? Vec3.Zero;
            }
            catch
            {
                return Vec3.Zero;
            }
        }

        private static string ResolveMachineClass(SiegeWeapon siegeWeapon)
        {
            if (siegeWeapon == null)
                return "Empty";

            try
            {
                return siegeWeapon.GetSiegeEngineType()?.StringId ?? "Empty";
            }
            catch
            {
                return "Empty";
            }
        }

        private static Type ResolveWeaponType(SiegeWeapon siegeWeapon)
        {
            if (siegeWeapon == null)
                return null;

            try
            {
                return MissionSiegeWeaponsController.GetWeaponType(siegeWeapon);
            }
            catch
            {
                return siegeWeapon.GetType();
            }
        }
    }
}
