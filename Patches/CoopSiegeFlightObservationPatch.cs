using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SandBox.View.Map.Managers;
using SandBox.View.Map.Visuals;
using Missile = TaleWorlds.CampaignSystem.Siege.SiegeEvent.SiegeEngineMissile;

namespace CoopSpectator.Patches
{
    // Explicit owner installation only; never PatchAll-discovered.
    internal static class CoopSiegeFlightObservationPatch
    {
        private const string Id = "com.coopspectator.siege-flight-observation";
        private static Harmony _harmony;
        private static MethodInfo _tick, _managerTick, _calculation, _sideGetter;
        private static Action<SettlementVisualManager> _begin;
        private static Action _finish, _abort;
        private static Action<SettlementVisual, Missile, object> _observe;

        internal static bool TryInstall(Action<SettlementVisualManager> begin, Action finish,
            Action abort, Action<SettlementVisual, Missile, object> observe)
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("COOPSPECTATOR_SIEGE_FLIGHT_OBSERVER"), "1", StringComparison.Ordinal) ||
                !CoopSpectator.Infrastructure.ExperimentalFeatures.EnableCampaignMapPrototype ||
                begin == null || finish == null || abort == null || observe == null || _harmony != null) return false;
            try
            {
                _tick = AccessTools.Method(typeof(SettlementVisual), "Tick");
                _managerTick = AccessTools.Method(typeof(SettlementVisualManager), "OnTick", new[] { typeof(float), typeof(float) });
                _calculation = AccessTools.Method(typeof(SettlementVisual), "CalculateDataAndDurationsForSiegeMachine");
                _sideGetter = AccessTools.PropertyGetter(typeof(TaleWorlds.CampaignSystem.Siege.ISiegeEventSide), "BattleSide");
                if (_tick == null || _managerTick == null || _calculation == null || _sideGetter == null) return false;
                // Verify original instructions before installing either part.
                if (!CoopSiegeFlightCallSite.TryRewrite(PatchProcessor.GetOriginalInstructions(_tick), _tick,
                    _calculation, typeof(Missile), _sideGetter, AccessTools.Method(typeof(CoopSiegeFlightObservationPatch), nameof(Observe)), out _)) return false;
                _begin = begin; _finish = finish; _abort = abort; _observe = observe;
                _harmony = new Harmony(Id);
                _harmony.Patch(_tick, transpiler: new HarmonyMethod(typeof(CoopSiegeFlightObservationPatch), nameof(Transpile)));
                _harmony.Patch(_managerTick,
                    prefix: new HarmonyMethod(typeof(CoopSiegeFlightObservationPatch), nameof(Begin)),
                    postfix: new HarmonyMethod(typeof(CoopSiegeFlightObservationPatch), nameof(Finish)),
                    finalizer: new HarmonyMethod(typeof(CoopSiegeFlightObservationPatch), nameof(FinalizeTick)));
                return true;
            }
            catch (Exception) { Reset(); return false; }
        }

        private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            if (!CoopSiegeFlightCallSite.TryRewrite(instructions, _tick, _calculation, typeof(Missile),
                _sideGetter, AccessTools.Method(typeof(CoopSiegeFlightObservationPatch), nameof(Observe)), out var result))
                throw new InvalidOperationException("Siege flight observation call-site mismatch");
            return result;
        }
        private static void Begin(SettlementVisualManager __instance)
        { try { _begin?.Invoke(__instance); } catch (Exception) { Abort(); } }
        private static void Finish()
        { try { _finish?.Invoke(); } catch (Exception) { Abort(); } }
        private static Exception FinalizeTick(Exception __exception)
        { if (__exception != null) Abort(); return __exception; }
        private static void Observe(SettlementVisual visual, Missile missile, object data)
        { try { _observe?.Invoke(visual, missile, data); } catch (Exception) { Abort(); } }
        private static void Abort()
        { try { _abort?.Invoke(); } catch (Exception) { } }

        internal static void Reset()
        {
            // Disable callbacks before unpatching; do not suppress native exceptions.
            Abort(); _begin = null; _finish = null; _abort = null; _observe = null;
            if (_harmony != null)
            {
                // Attempt both removals even if the first fails. Keep the owner
                // for a later retry; callbacks are already inert in either case.
                bool removed = TryRemoveBoth(
                    () => { if (_tick != null) _harmony.Unpatch(_tick, HarmonyPatchType.All, Id); },
                    () => { if (_managerTick != null) _harmony.Unpatch(_managerTick, HarmonyPatchType.All, Id); });
                if (removed) _harmony = null;
            }
        }

        internal static bool TryRemoveBoth(Action removeTick, Action removeManager)
        {
            bool removed = true;
            try { removeTick(); } catch (Exception) { removed = false; }
            try { removeManager(); } catch (Exception) { removed = false; }
            return removed;
        }
    }
}
