using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.UI;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;

internal static class TooltipPresenterTests
{
    private static int _count;
    internal static void Run()
    {
        // Deterministic substitutes model the decompiled global view/VM event
        // ordering. These exercise the REAL presenter, not the native renderer.
        InformationManager.OnShowTooltip += InformationManager.NativeShow;
        InformationManager.OnHideTooltip += InformationManager.NativeHide;
        using (var presenter = new CoopCampaignMapTooltipPresenter())
        {
            presenter.Tick(.05f, "party:a");
            Check(InformationManager.Current == null, "no speculative card");
            var first = Rows("party:a", false, "first");
            CoopCampaignMapPrototypeNetworkController.CurrentClientTooltip = first;
            presenter.Tick(.05f, "party:a");
            var vm = InformationManager.Current;
            Check(vm != null && vm.IsActive && vm.Mode == 3 && vm.TooltipPropertyList[0].Value == "first", "stock VM detached rows");
            Check(vm.TooltipPropertyList[0].TextHeight == 12 && vm.TooltipPropertyList[0].Color.alpha == .4f && (int)vm.TooltipPropertyList[0].Flags == 8, "presentation exact");
            int shows = InformationManager.Shows;
            CoopCampaignMapPrototypeNetworkController.CurrentClientTooltip = Rows("party:a", false, "refreshed");
            presenter.Tick(.05f, "party:a");
            Check(ReferenceEquals(vm, InformationManager.Current) && InformationManager.Shows == shows && vm.TooltipPropertyList[0].Value == "refreshed", "same movie refresh");
            Check(vm.HideTransitions == 0, "same-mode data refresh never toggles visible card off");
            var retainedRow = vm.TooltipPropertyList[0];
            for (int i = 0; i < 100; i++)
            {
                CoopCampaignMapPrototypeNetworkController.CurrentClientTooltip = Rows("party:a", false, "refreshed");
                presenter.Tick(.05f, "party:a");
                Check(ReferenceEquals(retainedRow, vm.TooltipPropertyList[0]) && vm.HideTransitions == 0 && InformationManager.Shows == shows,
                    "unchanged refresh preserves row/movie/visibility");
            }
            var changedStyle = Rows("party:a", false, "refreshed");
            changedStyle.Rows[0].Alpha = .6f;
            CoopCampaignMapPrototypeNetworkController.CurrentClientTooltip = changedStyle;
            presenter.Tick(.05f, "party:a");
            Check(vm.TooltipPropertyList[0].Color.alpha == .6f && vm.HideTransitions == 0, "style-only update remains visible");
            presenter.Tick(.05f, "party:b");
            Check(InformationManager.Current == null, "hover switch hides previous");
            CoopCampaignMapPrototypeNetworkController.CurrentClientTooltip = Rows("party:b", true, "extended");
            Input.Held = true;
            presenter.Tick(.05f, "party:b"); // changes interest, invalidates premature reply
            CoopCampaignMapPrototypeNetworkController.CurrentClientTooltip = Rows("party:b", true, "extended");
            presenter.Tick(.05f, "party:b");
            Check(InformationManager.Current?.IsExtended == true && InformationManager.Current.IsActive, "initial extended rows");
            InformationManager.Current.IsExtended = false;
            Check(!InformationManager.Current.IsActive && InformationManager.Current.TooltipPropertyList.Count == 0, "native extension change cannot reuse wrong rows");
            Input.Held = false;
            presenter.Tick(.05f, "party:b");
            Check(InformationManager.Current == null, "extension transition clears");

            InformationManager.RegisterTooltip<Foreign, PropertyBasedTooltipVM>((value, args) => value.IsActive = true, "foreign");
            InformationManager.ShowTooltip(typeof(Foreign));
            var foreign = InformationManager.Current;
            CoopCampaignMapPrototypeNetworkController.CurrentClientTooltip = Rows("party:b", false, "ours");
            presenter.Tick(.05f, "party:b");
            Check(ReferenceEquals(foreign, InformationManager.Current), "cannot steal foreign tooltip");
            presenter.Clear();
            Check(ReferenceEquals(foreign, InformationManager.Current), "clear cannot hide foreign tooltip");
            InformationManager.HideTooltip();
            presenter.Tick(.05f, "party:b");
            CoopCampaignMapPrototypeNetworkController.CurrentClientTooltip = Rows("party:b", false, "ours");
            presenter.Tick(.05f, "party:b");
            Check(InformationManager.Current?.IsActive == true, "resume after other tooltip closes");
            presenter.Tick(.05f, null);
            Check(InformationManager.Current == null, "focus/dialogue/hover exit hides");

            Input.IsGamepadActive = true; Input.Held = true;
            presenter.Tick(.1f, "party:b");
            Check(!CoopCampaignMapPrototypeNetworkController.Extended, "gamepad threshold not reached");
            presenter.Tick(.1f, "party:b");
            Check(CoopCampaignMapPrototypeNetworkController.Extended, "gamepad threshold reached");
            Input.IsGamepadActive = false; Input.Held = false;
        }
        Check(InformationManager.RegisteredTypes.Count == 1, "only foreign registration survives");
        var old = new CoopCampaignMapTooltipPresenter();
        using (var replacement = new CoopCampaignMapTooltipPresenter())
        {
            old.Dispose();
            Check(InformationManager.RegisteredTypes.Count == 4, "old disposal cannot unregister replacement");
            replacement.Tick(.05f, "barhint:test");
            var finance = new CoopCampaignMapTooltipPayload { EntityId = "barhint:test", Kind = CoopCampaignMapTooltipKind.BarRundown,
                Rundown = new CoopCampaignMapRundownPresentation { IsActive = true, TitleText = "Finance", ExpectedChangeText = "17,13" } };
            finance.Rundown.Lines.Add(new CoopCampaignMapRundownLine { Name = "Income", Value = 17.125f, ValueAsString = "17,13" });
            CoopCampaignMapPrototypeNetworkController.CurrentClientTooltip = finance;
            replacement.Tick(.05f, "barhint:test");
            var rundown = InformationManager.CurrentRundown;
            Check(rundown?.IsActive == true && rundown.Lines[0].ValueAsString == "17,13", "native rundown bindings");
            int shown = InformationManager.Shows;
            CoopCampaignMapPrototypeNetworkController.CurrentClientTooltip = new CoopCampaignMapTooltipPayload {
                EntityId = finance.EntityId, Kind = finance.Kind, Rundown = finance.Rundown.Clone() };
            replacement.Tick(.05f, "barhint:test");
            Check(ReferenceEquals(rundown, InformationManager.CurrentRundown) && shown == InformationManager.Shows, "rundown stable refresh");
            replacement.Tick(.05f, "party:c");
            Check(InformationManager.CurrentRundown == null, "leaving finance hides owned movie");
            CoopCampaignMapPrototypeNetworkController.CurrentClientTooltip = Rows("party:c", false, "ordinary");
            replacement.Tick(.05f, "party:c");
            Check(InformationManager.Current?.IsActive == true, "ordinary movie resumes");
            replacement.Tick(.05f, "barhint:control");
            var control = new CoopCampaignMapTooltipPayload { EntityId = "barhint:control",
                Kind = CoopCampaignMapTooltipKind.BarControl, ControlHint = new CoopCampaignMapControlHint { Text = "Pause (1)" } };
            CoopCampaignMapPrototypeNetworkController.CurrentClientTooltip = control;
            replacement.Tick(.05f, control.EntityId);
            var hint = InformationManager.CurrentHint;
            Check(hint?.Text == "Pause (1)" && hint.IsActive, "plain native hint bindings");
            shown = InformationManager.Shows;
            var refreshed = new CoopCampaignMapTooltipPayload { EntityId = control.EntityId, Kind = control.Kind,
                ControlHint = control.ControlHint.Clone() };
            refreshed.ControlHint.Text = "Play (2)";
            CoopCampaignMapPrototypeNetworkController.CurrentClientTooltip = refreshed;
            replacement.Tick(.05f, control.EntityId);
            Check(ReferenceEquals(hint, InformationManager.CurrentHint) && hint.Text == "Play (2)" &&
                shown == InformationManager.Shows, "plain hint refresh without reopening");
            var structured = new CoopCampaignMapTooltipPayload { EntityId = control.EntityId, Kind = control.Kind,
                ControlHint = new CoopCampaignMapControlHint() };
            structured.ControlHint.Rows.Add(new CoopCampaignMapTooltipRow { Definition = "Morning", Value = "Reset camera" });
            CoopCampaignMapPrototypeNetworkController.CurrentClientTooltip = structured;
            replacement.Tick(.05f, control.EntityId);
            Check(InformationManager.CurrentHint == null && InformationManager.Current?.TooltipPropertyList[0].Definition == "Morning",
                "control shape switch uses native property card");
            InformationManager.ShowTooltip(typeof(Foreign));
            var foreign = InformationManager.Current;
            replacement.Clear();
            Check(ReferenceEquals(foreign, InformationManager.Current), "control cleanup preserves foreign tooltip");
            InformationManager.HideTooltip();
        }
        Check(InformationManager.RegisteredTypes.Count == 1, "replacement disposes exactly its mapping");
        InformationManager.UnregisterTooltip<Foreign>();
        InformationManager.OnShowTooltip -= InformationManager.NativeShow;
        InformationManager.OnHideTooltip -= InformationManager.NativeHide;
        Console.WriteLine("Tooltip presenter assertions passed: " + _count);
    }
    private sealed class Foreign { }
    private static CoopCampaignMapTooltipPayload Rows(string id, bool extended, string text) => new CoopCampaignMapTooltipPayload
    { EntityId = id, IsExtended = extended, Mode = 3, Rows = new List<CoopCampaignMapTooltipRow> {
        new CoopCampaignMapTooltipRow { Definition = "Label", Value = text, TextHeight = 12, PropertyModifier = 8, Red = .1f, Green = .2f, Blue = .3f, Alpha = .4f } } };
    private static void Check(bool value, string name)
    { _count++; if (!value) throw new InvalidOperationException("Tooltip presenter: " + name); }
}

// Narrow test doubles only. Production references the installed game assemblies.
namespace TaleWorlds.InputSystem
{
    public enum InputKey { }
    public static class Input
    { public static bool Held, IsGamepadActive; public static bool IsKeyDown(InputKey key) => Held; }
}
namespace TaleWorlds.Library
{
    public sealed class DataSourcePropertyAttribute : Attribute { }
    public class ViewModel { public virtual void OnFinalize() { } protected void OnPropertyChanged(string property) { } }
    public class MBBindingList<T> : List<T> { }
    public class TooltipBaseVM : ViewModel
    {
        private readonly Type _type; private readonly object[] _args; private bool _extended;
        public bool IsActive { get; set; }
        public bool IsExtended { get => _extended; set { if (_extended != value) { _extended = value; OnIsExtendedChanged(); } } }
        public TooltipBaseVM(Type type, object[] args) { _type = type; _args = args; }
        protected virtual void OnIsExtendedChanged() { }
        protected void InvokeRefreshData<T>(T vm) { ((Action<T, object[]>)InformationManager.RegisteredTypes[_type].OnRefreshData)(vm, _args); }
    }
    public struct Color
    { public float red, green, blue, alpha; public Color(float r, float g, float b, float a) { red=r; green=g; blue=b; alpha=a; } }
    public static class InformationManager
    {
        public sealed class Registry { public object OnRefreshData; public Type VmType; }
        public static Dictionary<Type, Registry> RegisteredTypes = new Dictionary<Type, Registry>();
        public static event Action<Type, object[]> OnShowTooltip;
        public static event Action OnHideTooltip;
        public static PropertyBasedTooltipVM Current;
        public static CoopCampaignMapRundownVM CurrentRundown;
        public static HintVM CurrentHint;
        public static int Shows;
        public static void RegisterTooltip<T, TVm>(Action<TVm, object[]> renderer, string movie)
        { RegisteredTypes[typeof(T)] = new Registry { OnRefreshData = renderer, VmType = typeof(TVm) }; }
        public static void UnregisterTooltip<T>() { RegisteredTypes.Remove(typeof(T)); }
        public static bool GetIsAnyTooltipActive() => Current?.IsActive == true || CurrentRundown?.IsActive == true || CurrentHint?.IsActive == true;
        public static void ShowTooltip(Type type, params object[] args) { OnShowTooltip?.Invoke(type, args); }
        public static void HideTooltip() { OnHideTooltip?.Invoke(); }
        public static void NativeShow(Type type, object[] args)
        {
            NativeHide(); Shows++;
            if (RegisteredTypes[type].VmType == typeof(CoopCampaignMapRundownVM)) CurrentRundown = new CoopCampaignMapRundownVM(type, args);
            else if (RegisteredTypes[type].VmType == typeof(HintVM)) CurrentHint = new HintVM(type, args);
            else Current = new PropertyBasedTooltipVM(type, args);
        }
        public static void NativeHide() { Current?.FinalizeVm(); Current = null; CurrentRundown?.OnFinalize(); CurrentRundown = null; CurrentHint = null; }
    }
}
namespace TaleWorlds.Core.ViewModelCollection.Information.RundownTooltip
{
    public class RundownLineVM : ViewModel
    {
        public string Name { get; set; } public float Value { get; set; } public string ValueAsString { get; set; }
        public RundownLineVM(string name, float value) { Name = name; Value = value; }
    }
}
namespace TaleWorlds.Core.ViewModelCollection.Information
{
    public sealed class HintVM : TooltipBaseVM
    {
        public string Text { get; set; }
        public HintVM(Type type, object[] args) : base(type, args) { InvokeRefreshData(this); IsActive = true; }
    }
    public sealed class TooltipProperty
    {
        public enum TooltipPropertyFlags { None }
        public string Definition, Value; public int TextHeight; public Color Color; public TooltipPropertyFlags Flags;
        public TooltipProperty(string definition, string value, int height, Color color, bool extended, TooltipPropertyFlags flags)
        { Definition=definition; Value=value; TextHeight=height; Color=color; Flags=flags; }
    }
    public sealed class PropertyBasedTooltipVM
    {
        private Type _type; private object[] _args; private bool _extended;
        private bool _active;
        public int HideTransitions;
        public bool IsActive { get => _active; set { if (_active && !value) HideTransitions++; _active=value; } }
        public int Mode;
        public List<TooltipProperty> TooltipPropertyList = new List<TooltipProperty>();
        public bool IsExtended { get => _extended; set { if (_extended == value) return; _extended=value; if (IsActive) { IsActive=false; TooltipPropertyList.Clear(); Refresh(); } } }
        public PropertyBasedTooltipVM(Type type, object[] args) { _type=type; _args=args; Refresh(); }
        private void Refresh()
        { ((Action<PropertyBasedTooltipVM, object[]>)InformationManager.RegisteredTypes[_type].OnRefreshData)(this, _args); if (TooltipPropertyList.Count > 0) IsActive=true; }
        public void FinalizeVm() { IsActive=false; TooltipPropertyList.Clear(); _args=null; }
    }
}
namespace CoopSpectator.Infrastructure { internal static class ModLogger { internal static void Info(string text) { } } }
namespace CoopSpectator.MissionBehaviors
{
    internal static class CoopCampaignMapPrototypeNetworkController
    {
        public static CoopCampaignMapTooltipPayload CurrentClientTooltip;
        private static string _desired; public static bool Extended;
        public static void ClearClientTooltip() { _desired=null; CurrentClientTooltip=null; }
        public static void SetClientTooltipInterest(string id, bool extended)
        { if (_desired!=id || Extended!=extended) CurrentClientTooltip=null; _desired=id; Extended=extended; }
    }
}
