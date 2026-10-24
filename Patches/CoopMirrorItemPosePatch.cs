using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Tableaus.Thumbnails;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Patches
{
    // Normalize only the pose selector's local string, never the registered item
    // or cache key. Native camera creation and lifetime remain unchanged.
    internal static class CoopMirrorItemPosePatch
    {
        private const string OwnerId = "com.coopspectator.mirror-item-pose";
        private static bool _installed;
        internal static void Apply()
        {
            if (_installed || !ExperimentalFeatures.EnableCampaignMapPrototype) return;
            var harmony = new Harmony(OwnerId);
            try
            {
                harmony.Patch(AccessTools.Method(typeof(ItemThumbnailCache), "GetItemPoseAndCamera"),
                    transpiler: new HarmonyMethod(typeof(CoopMirrorItemPosePatch), nameof(Transpile)));
                _installed = true;
            }
            catch (Exception ex)
            {
                harmony.UnpatchAll(OwnerId);
                ModLogger.Error("Mirror item pose hook unavailable; removed.", ex);
            }
        }

        private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(MBObjectBase), "StringId");
            var normalize = AccessTools.Method(typeof(CoopMirrorItemPosePatch), nameof(PoseId));
            if (getter == null || normalize == null) throw new MissingMethodException("Item pose selector dependencies");
            var result = new List<CodeInstruction>();
            int matches = 0;
            foreach (var instruction in instructions)
            {
                result.Add(instruction);
                if (!instruction.Calls(getter)) continue;
                // Preserve original call, labels, exception metadata and null semantics.
                result.Add(new CodeInstruction(OpCodes.Call, normalize));
                matches++;
            }
            // Installed native method: nine craft-material comparisons plus dragon banner.
            if (matches != 10) throw new InvalidOperationException("Item pose selector shape changed: " + matches);
            return result;
        }

        private static string PoseId(string id)
        {
            if (!GameNetwork.IsMultiplayer || id == null ||
                !id.StartsWith("cs_mirror_", StringComparison.Ordinal) ||
                !ExactCampaignRuntimeItemRegistry.TryResolveOriginalItemIdByMirrorId(id, out string original)) return id;
            switch (original)
            {
                case "iron": case "hardwood": case "charcoal":
                case "ironIngot1": case "ironIngot2": case "ironIngot3":
                case "ironIngot4": case "ironIngot5": case "ironIngot6": return original;
                case "dragon_banner_center": return original;
                default: return id;
            }
        }
    }
}
