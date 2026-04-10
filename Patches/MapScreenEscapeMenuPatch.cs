using System;
using System.Collections.Generic;
using CoopSpectator.Campaign;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade.ViewModelCollection.EscapeMenu;

namespace CoopSpectator.Patches
{
    [HarmonyPatch(typeof(MapScreen), "GetEscapeMenuItems")]
    public static class MapScreenEscapeMenuPatch
    {
        private static void Postfix(MapScreen __instance, ref List<EscapeMenuItemVM> __result)
        {
            if (__instance == null || __result == null)
                return;

            int insertIndex = __result.Count >= 3 ? 3 : __result.Count;
            __result.Insert(
                insertIndex,
                new EscapeMenuItemVM(
                    new TextObject("{=CoopDedicatedMenu}Coop Dedicated Server"),
                    delegate(object _)
                    {
                        __instance.CloseEscapeMenu();
                        __instance.AddMapView<CoopDedicatedServerSettingsMapView>(Array.Empty<object>());
                    },
                    null,
                    () => new Tuple<bool, TextObject>(false, null),
                    false));
        }
    }
}
