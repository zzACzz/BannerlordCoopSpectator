using System;
using System.Reflection;
using HarmonyLib;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Keeps authoritative agent damage inside Bannerlord's multiplayer wire range.
    /// Applied manually by the dedicated-server submodule only.
    /// </summary>
    public static class CoopNetworkSafeAgentBlowPatch
    {
        private const int MinNetworkDamage = 0;
        private const int MaxNetworkDamage = 2000;
        private const int DiagnosticLogBudget = 16;

        private static int _diagnosticLogCount;

        public static void Apply(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));

            MethodInfo target = AccessTools.Method(
                typeof(Agent),
                nameof(Agent.RegisterBlow),
                new[] { typeof(Blow), typeof(AttackCollisionData).MakeByRefType() });
            MethodInfo prefix = AccessTools.Method(
                typeof(CoopNetworkSafeAgentBlowPatch),
                nameof(RegisterBlow_Prefix));

            if (target == null || prefix == null)
            {
                ModLogger.Warn(
                    "[CoopNetworkSafeAgentBlowPatch] Agent.RegisterBlow target or prefix was not found; " +
                    "network damage guard was not applied.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info(
                "[CoopNetworkSafeAgentBlowPatch] Applied dedicated-server Agent.RegisterBlow " +
                "network damage guard (0..2000).");
        }

        private static void RegisterBlow_Prefix(Agent __instance, ref Blow __0)
        {
            Mission mission = __instance?.Mission;
            if (mission == null ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
            {
                return;
            }

            CoopBattlePhase phase = CoopBattlePhaseRuntimeState.GetPhase();
            if (phase < CoopBattlePhase.BattleActive || phase >= CoopBattlePhase.BattleEnded)
                return;

            int originalInflictedDamage = __0.InflictedDamage;
            int originalSelfInflictedDamage = __0.SelfInflictedDamage;
            int safeInflictedDamage = ClampNetworkDamage(originalInflictedDamage);
            int safeSelfInflictedDamage = ClampNetworkDamage(originalSelfInflictedDamage);

            if (safeInflictedDamage == originalInflictedDamage &&
                safeSelfInflictedDamage == originalSelfInflictedDamage)
            {
                return;
            }

            __0.InflictedDamage = safeInflictedDamage;
            __0.SelfInflictedDamage = safeSelfInflictedDamage;

            if (CoopDebugConfig.CombatModelDiagnostics &&
                _diagnosticLogCount < DiagnosticLogBudget)
            {
                _diagnosticLogCount++;
                ModLogger.Warn(
                    $"[CoopNetworkSafeAgentBlowPatch] Clamped damage before Agent.HandleBlow " +
                    $"scene='{mission.SceneName}' victim={__instance.Index} " +
                    $"inflicted={originalInflictedDamage}->{safeInflictedDamage} " +
                    $"self={originalSelfInflictedDamage}->{safeSelfInflictedDamage}.");
            }
        }

        private static int ClampNetworkDamage(int value)
        {
            if (value < MinNetworkDamage)
                return MinNetworkDamage;
            if (value > MaxNetworkDamage)
                return MaxNetworkDamage;
            return value;
        }
    }
}
