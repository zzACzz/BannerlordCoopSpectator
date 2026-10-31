using System;
using System.Collections.Generic;
using Path = System.IO.Path;
using System.Reflection;
using System.Runtime.CompilerServices;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.DotNet;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.UI
{
    internal static class CoopCampaignNavalMapResources
    {
        private static bool _prepared;
        private static readonly ConditionalWeakTable<object, object> ReplicaStormComponents = new ConditionalWeakTable<object, object>();

        internal static void EnsurePrepared()
        {
            if (!CoopCampaignContentRuntime.UsesNavalResources || CoopCampaignContentRuntime.IsCampaign || _prepared) return;
            string bin = Path.Combine(CoopCampaignContentRuntime.GameRoot, "Modules", "NavalDLC", "bin", "Win64_Shipping_Client");
            Assembly naval = Assembly.LoadFrom(Path.Combine(bin, "NavalDLC.dll"));
            Assembly view = Assembly.LoadFrom(Path.Combine(bin, "NavalDLC.View.dll"));
            Type storm = naval.GetType("TaleWorlds.MountAndBlade.Objects.CampaignMapAmbientOccluder", true);
            Type pirate = view.GetType("NavalDLC.View.PirateSpawnPoint", true);
            var existing = typeof(Managed).GetProperty("ModuleTypes", BindingFlags.Static | BindingFlags.NonPublic)?
                .GetValue(null, null) as IDictionary<string, Type>;
            if (existing == null) throw new InvalidOperationException("Cannot verify Naval map script registry.");
            var add = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (Type type in new[] { storm, pirate })
            {
                if (!typeof(ScriptComponentBehavior).IsAssignableFrom(type)) throw new InvalidOperationException("Unsupported Naval map script type.");
                if (existing.TryGetValue(type.Name, out var prior))
                {
                    if (prior != type) throw new InvalidOperationException("Conflicting Naval map script: " + type.Name);
                }
                else add.Add(type.Name, type);
            }
            var flags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            MethodInfo init = storm.GetMethod("OnInit", flags, null, Type.EmptyTypes, null);
            MethodInfo tick = storm.GetMethod("OnTick", flags, null, new[] { typeof(float) }, null);
            if (init?.ReturnType != typeof(void) || tick?.ReturnType != typeof(void))
                throw new InvalidOperationException("Unsupported Naval storm component methods.");
            var harmony = new Harmony("CoopSpectator.NavalReplicaScene");
            harmony.Patch(init, prefix: new HarmonyMethod(typeof(CoopCampaignNavalMapResources), nameof(StormInitPrefix)));
            harmony.Patch(tick, prefix: new HarmonyMethod(typeof(CoopCampaignNavalMapResources), nameof(StormTickPrefix)));
            if (add.Count != 0) Managed.AddTypes(add);
            _prepared = true;
        }

        private static bool StormInitPrefix(ScriptComponentBehavior __instance)
        {
            if (!CoopCampaignMapPrototypeSceneLoadScope.IsActive || !GameNetwork.IsClient ||
                CoopCampaignContentRuntime.IsCampaign || !CoopCampaignContentRuntime.UsesNavalResources) return true;
            ReplicaStormComponents.GetValue(__instance, _ => new object());
            // Match native empty-storm visual initialization without creating or
            // reading a campaign StormManager. No registered game objects change.
            Mesh mesh = __instance.GameEntity.GetFirstMesh();
            if (mesh != null)
            {
                mesh.SetupAdditionalBoneBuffer(16);
                MatrixFrame zero = MatrixFrame.Zero;
                for (int i = 0; i < 16; i++) mesh.SetAdditionalBoneFrame(i, in zero);
            }
            return false;
        }

        private static bool StormTickPrefix(object __instance) => !ReplicaStormComponents.TryGetValue(__instance, out _);
    }
}
