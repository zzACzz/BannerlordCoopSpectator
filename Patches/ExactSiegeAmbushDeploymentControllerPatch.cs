using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.SiegeAmbush;
using CoopSpectator.Network.Messages;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    public static class ExactSiegeAmbushDeploymentControllerPatch
    {
        [ThreadStatic]
        private static int _authoritativeFinishScopeDepth;
        private static bool _automaticFinishBlockedLogged;

        private static readonly MethodInfo AgentControllerSetter =
            AccessTools.PropertySetter(typeof(Agent), nameof(Agent.Controller));
        private static readonly MethodInfo AgentSetDetachableFromFormationMethod =
            AccessTools.Method(typeof(Agent), nameof(Agent.SetDetachableFromFormation));
        private static readonly MethodInfo ShouldSkipMissingInitialPlayerAgentOperationsMethod =
            AccessTools.Method(
                typeof(ExactSiegeAmbushDeploymentControllerPatch),
                nameof(ShouldSkipMissingInitialPlayerAgentOperations));

        public static void Apply(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));

            MethodInfo setupTeamsTarget =
                AccessTools.Method(typeof(DeploymentMissionController), "SetupTeams");
            MethodInfo setupTeamsTranspiler =
                AccessTools.Method(
                    typeof(ExactSiegeAmbushDeploymentControllerPatch),
                    nameof(DeploymentMissionController_SetupTeams_Transpiler));
            MethodInfo finishDeploymentTarget =
                AccessTools.Method(
                    typeof(DeploymentMissionController),
                    nameof(DeploymentMissionController.FinishDeployment));
            MethodInfo finishDeploymentTranspiler =
                AccessTools.Method(
                    typeof(ExactSiegeAmbushDeploymentControllerPatch),
                    nameof(DeploymentMissionController_FinishDeployment_Transpiler));
            MethodInfo finishDeploymentPrefix =
                AccessTools.Method(
                    typeof(ExactSiegeAmbushDeploymentControllerPatch),
                    nameof(DeploymentMissionController_FinishDeployment_Prefix));
            MethodInfo finishDeploymentPostfix =
                AccessTools.Method(
                    typeof(ExactSiegeAmbushDeploymentControllerPatch),
                    nameof(DeploymentMissionController_FinishDeployment_Postfix));

            if (setupTeamsTarget == null ||
                setupTeamsTranspiler == null ||
                finishDeploymentTarget == null ||
                finishDeploymentTranspiler == null ||
                finishDeploymentPrefix == null ||
                finishDeploymentPostfix == null)
            {
                throw new MissingMethodException(
                    "ExactSiegeAmbushDeploymentControllerPatch: required deployment methods were not found.");
            }

            harmony.Patch(
                setupTeamsTarget,
                transpiler: new HarmonyMethod(setupTeamsTranspiler));
            harmony.Patch(
                finishDeploymentTarget,
                prefix: new HarmonyMethod(finishDeploymentPrefix),
                postfix: new HarmonyMethod(finishDeploymentPostfix),
                transpiler: new HarmonyMethod(finishDeploymentTranspiler));

            ModLogger.Info(
                "ExactSiegeAmbushDeploymentControllerPatch: guarded missing InitialPlayerAgent operations and " +
                "gated automatic dedicated-server FinishDeployment for exact SiegeAmbush.");
        }

        public static IDisposable BeginAuthoritativeFinishScope(Mission mission)
        {
            if (!IsExactSiegeAmbushMission(mission))
                return NoopScope.Instance;

            _authoritativeFinishScopeDepth++;
            return new AuthoritativeFinishScope();
        }

        private static IEnumerable<CodeInstruction> DeploymentMissionController_SetupTeams_Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            return InsertMissingInitialPlayerAgentGuard(
                instructions,
                generator,
                AgentControllerSetter,
                AgentSetDetachableFromFormationMethod,
                "DeploymentMissionController.SetupTeams");
        }

        private static IEnumerable<CodeInstruction> DeploymentMissionController_FinishDeployment_Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            return InsertMissingInitialPlayerAgentGuard(
                instructions,
                generator,
                AgentSetDetachableFromFormationMethod,
                AgentControllerSetter,
                "DeploymentMissionController.FinishDeployment");
        }

        private static bool DeploymentMissionController_FinishDeployment_Prefix(
            DeploymentMissionController __instance,
            out bool __state)
        {
            __state = false;
            try
            {
                Mission mission = __instance?.Mission;
                if (!GameNetwork.IsServer ||
                    !(__instance is SiegeDeploymentMissionController) ||
                    !IsExactSiegeAmbushMission(mission))
                {
                    return true;
                }

                if (_authoritativeFinishScopeDepth > 0)
                {
                    __state = true;
                    return true;
                }

                if (!_automaticFinishBlockedLogged)
                {
                    _automaticFinishBlockedLogged = true;
                    ModLogger.Info(
                        "ExactSiegeAmbushDeploymentControllerPatch: blocked automatic server " +
                        "DeploymentMissionController.FinishDeployment until coop commander deployment completion.");
                }

                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "ExactSiegeAmbushDeploymentControllerPatch: FinishDeployment gate failed open: " +
                    ex.Message);
                return true;
            }
        }

        private static void DeploymentMissionController_FinishDeployment_Postfix(
            DeploymentMissionController __instance,
            bool __state)
        {
            if (!__state)
                return;

            try
            {
                Mission mission = __instance?.Mission;
                RestorePreBattleHoldAfterAuthoritativeDeployment(
                    mission,
                    "authoritative deployment finished");
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "ExactSiegeAmbushDeploymentControllerPatch: post-deployment reinforcement gate failed: " +
                    ex.Message);
            }
        }

        internal static void RestorePreBattleHoldAfterAuthoritativeDeployment(
            Mission mission,
            string source)
        {
            if (!IsExactSiegeAmbushMission(mission) ||
                CoopBattlePhaseRuntimeState.GetPhase() >= CoopBattlePhase.BattleActive)
            {
                return;
            }

            DefaultBattleMissionAgentSpawnLogic spawnLogic =
                mission.GetMissionBehavior<DefaultBattleMissionAgentSpawnLogic>();
            spawnLogic?.SetReinforcementsSpawnEnabled(false);
            ApplyPreBattleAgentHold(mission, source);
            ModLogger.Info(
                "ExactSiegeAmbushDeploymentControllerPatch: restored the exact SallyOut pre-battle hold " +
                "after coop deployment completion until BattleActive. " +
                "Source=" + (source ?? "unknown"));
        }

        internal static void ApplyPreBattleAgentHold(
            Mission mission,
            string source)
        {
            if (!GameNetwork.IsServer ||
                !IsExactSiegeAmbushMission(mission) ||
                CoopBattlePhaseRuntimeState.GetPhase() >= CoopBattlePhase.BattleActive)
            {
                return;
            }

            mission.AllowAiTicking = false;
            mission.PauseAITick = true;
            mission.IsTeleportingAgents = false;

            int pausedAgentCount = 0;
            if (mission.AllAgents != null)
            {
                foreach (Agent agent in mission.AllAgents)
                {
                    if (agent == null ||
                        !agent.IsActive() ||
                        !agent.IsHuman ||
                        !agent.IsAIControlled)
                    {
                        continue;
                    }

                    agent.SetAlarmState(Agent.AIStateFlag.None);
                    agent.SetIsAIPaused(isPaused: true);
                    pausedAgentCount++;
                }
            }

            ModLogger.Info(
                "ExactSiegeAmbushDeploymentControllerPatch: applied exact SallyOut pre-battle agent hold. " +
                "PausedAgents=" + pausedAgentCount +
                " AllowAiTicking=" + mission.AllowAiTicking +
                " PauseAITick=" + mission.PauseAITick +
                " Source=" + (source ?? "unknown"));
        }

        internal static void ReleaseBattleAgentHold(
            Mission mission,
            string source)
        {
            if (!GameNetwork.IsServer ||
                !IsExactSiegeAmbushMission(mission) ||
                CoopBattlePhaseRuntimeState.GetPhase() < CoopBattlePhase.BattleActive)
            {
                return;
            }

            mission.IsTeleportingAgents = false;
            mission.AllowAiTicking = true;
            mission.PauseAITick = false;

            int releasedAgentCount = 0;
            if (mission.AllAgents != null)
            {
                foreach (Agent agent in mission.AllAgents)
                {
                    if (agent == null ||
                        !agent.IsActive() ||
                        !agent.IsHuman ||
                        !agent.IsAIControlled)
                    {
                        continue;
                    }

                    agent.SetAlarmState(Agent.AIStateFlag.Alarmed);
                    agent.SetIsAIPaused(isPaused: false);
                    if ((agent.GetAgentFlags() & AgentFlag.CanWieldWeapon) != 0)
                        agent.ResetEnemyCaches();
                    agent.HumanAIComponent?.SyncBehaviorParamsIfNecessary();
                    releasedAgentCount++;
                }
            }

            ModLogger.Info(
                "ExactSiegeAmbushDeploymentControllerPatch: released exact SallyOut battle agent hold. " +
                "ReleasedAgents=" + releasedAgentCount +
                " AllowAiTicking=" + mission.AllowAiTicking +
                " PauseAITick=" + mission.PauseAITick +
                " Source=" + (source ?? "unknown"));
        }

        private static IEnumerable<CodeInstruction> InsertMissingInitialPlayerAgentGuard(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator,
            MethodInfo firstGuardedCall,
            MethodInfo lastGuardedCall,
            string targetName)
        {
            if (instructions == null)
                throw new ArgumentNullException(nameof(instructions));
            if (generator == null)
                throw new ArgumentNullException(nameof(generator));
            if (firstGuardedCall == null ||
                lastGuardedCall == null ||
                ShouldSkipMissingInitialPlayerAgentOperationsMethod == null)
            {
                throw new MissingMethodException(
                    "ExactSiegeAmbushDeploymentControllerPatch: guarded Agent method was not found.");
            }

            var codes = new List<CodeInstruction>(instructions);
            int firstCallIndex = FindCallIndex(codes, firstGuardedCall, 0);
            int lastCallIndex = FindCallIndex(codes, lastGuardedCall, firstCallIndex + 1);
            int guardedBlockStart = firstCallIndex - 2;
            int continuationIndex = lastCallIndex + 1;
            bool usesLocalAgent = guardedBlockStart >= 0 &&
                                  IsLoadLocal(codes[guardedBlockStart]);
            bool usesStackAgent = guardedBlockStart >= 0 &&
                                  codes[guardedBlockStart].opcode == OpCodes.Dup;
            if (firstCallIndex < 2 ||
                lastCallIndex <= firstCallIndex ||
                continuationIndex >= codes.Count ||
                (!usesLocalAgent && !usesStackAgent))
            {
                throw new InvalidOperationException(
                    "ExactSiegeAmbushDeploymentControllerPatch: unexpected IL shape in " +
                    targetName + ".");
            }

            Label skipInitialPlayerAgentOperations = generator.DefineLabel();
            codes[continuationIndex].labels.Add(skipInitialPlayerAgentOperations);

            List<CodeInstruction> guard;
            if (usesStackAgent)
            {
                Label continueInitialPlayerAgentOperations = generator.DefineLabel();
                codes[guardedBlockStart].labels.Add(continueInitialPlayerAgentOperations);
                guard = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(
                        OpCodes.Call,
                        ShouldSkipMissingInitialPlayerAgentOperationsMethod),
                    new CodeInstruction(OpCodes.Brfalse, continueInitialPlayerAgentOperations),
                    new CodeInstruction(OpCodes.Pop),
                    new CodeInstruction(OpCodes.Br, skipInitialPlayerAgentOperations)
                };
            }
            else
            {
                guard = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(
                        OpCodes.Call,
                        ShouldSkipMissingInitialPlayerAgentOperationsMethod),
                    new CodeInstruction(OpCodes.Brtrue, skipInitialPlayerAgentOperations)
                };
            }

            if (codes[guardedBlockStart].labels.Count > 0)
            {
                List<Label> originalLabels = new List<Label>(
                    codes[guardedBlockStart].labels);
                if (usesStackAgent)
                    originalLabels.RemoveAt(originalLabels.Count - 1);

                guard[0].labels.AddRange(originalLabels);
                foreach (Label originalLabel in originalLabels)
                    codes[guardedBlockStart].labels.Remove(originalLabel);
            }

            codes.InsertRange(guardedBlockStart, guard);
            return codes;
        }

        private static int FindCallIndex(
            IReadOnlyList<CodeInstruction> codes,
            MethodInfo method,
            int startIndex)
        {
            if (codes == null || method == null)
                return -1;

            for (int i = Math.Max(0, startIndex); i < codes.Count; i++)
            {
                if (codes[i].Calls(method))
                    return i;
            }

            return -1;
        }

        private static bool IsLoadLocal(CodeInstruction instruction)
        {
            if (instruction == null)
                return false;

            OpCode opcode = instruction.opcode;
            return opcode == OpCodes.Ldloc ||
                   opcode == OpCodes.Ldloc_S ||
                   opcode == OpCodes.Ldloc_0 ||
                   opcode == OpCodes.Ldloc_1 ||
                   opcode == OpCodes.Ldloc_2 ||
                   opcode == OpCodes.Ldloc_3;
        }

        private static bool ShouldSkipMissingInitialPlayerAgentOperations(
            DeploymentMissionController controller)
        {
            try
            {
                if (!GameNetwork.IsDedicatedServer ||
                    !(controller is SiegeDeploymentMissionController))
                {
                    return false;
                }

                Mission mission = controller.Mission;
                if (mission == null ||
                    mission.InitialPlayerAgent != null ||
                    !ExactCampaignArmyBootstrap.IsActive(mission))
                {
                    return false;
                }

                BattleScenarioContextMessage scenarioContext =
                    BattleSnapshotRuntimeState.GetScenarioContext() ??
                    BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                    BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
                return SiegeAmbushScenarioContract.IsSiegeAmbushScenario(scenarioContext) &&
                       ExactCampaignSiegeAssaultWithDeploymentRuntime
                           .IsExactSiegeWithDeploymentScenario(scenarioContext);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsExactSiegeAmbushMission(Mission mission)
        {
            if (mission == null)
                return false;

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            return SiegeAmbushScenarioContract.IsSiegeAmbushScenario(scenarioContext) &&
                   ExactCampaignSiegeAssaultWithDeploymentRuntime
                       .IsExactSiegeWithDeploymentScenario(scenarioContext);
        }

        private sealed class AuthoritativeFinishScope : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                if (_authoritativeFinishScopeDepth > 0)
                    _authoritativeFinishScopeDepth--;
            }
        }

        private sealed class NoopScope : IDisposable
        {
            public static readonly NoopScope Instance = new NoopScope();

            public void Dispose()
            {
            }
        }
    }
}
