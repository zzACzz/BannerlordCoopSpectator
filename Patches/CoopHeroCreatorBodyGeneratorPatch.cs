using System;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Completes the native face editor without touching Mission.MainAgent. The stock Done path
    /// assumes that every mission owns a main agent, which is false in the creator mission.
    /// </summary>
    public static class CoopHeroCreatorBodyGeneratorPatch
    {
        private const string TargetTypeName =
            "TaleWorlds.MountAndBlade.GauntletUI.BodyGenerator.BodyGeneratorView";

        private static MethodBase _patchedDone;
        private static MethodBase _patchedCancel;
        private static Action _done;
        private static Action _cancel;
        private static bool _armed;

        public static void Apply(Harmony harmony)
        {
            if (harmony == null) return;

            Type targetType = AccessTools.TypeByName(TargetTypeName);
            if (targetType == null) return;

            MethodInfo done = FindExplicitInterfaceMethod(targetType, "Done");
            MethodInfo cancel = FindExplicitInterfaceMethod(targetType, "Cancel");
            MethodInfo donePrefix = AccessTools.Method(typeof(CoopHeroCreatorBodyGeneratorPatch), nameof(DonePrefix));
            MethodInfo cancelPrefix = AccessTools.Method(typeof(CoopHeroCreatorBodyGeneratorPatch), nameof(CancelPrefix));

            if (done != null && donePrefix != null && !ReferenceEquals(_patchedDone, done))
            {
                harmony.Patch(done, prefix: new HarmonyMethod(donePrefix));
                _patchedDone = done;
                ModLogger.Info("CoopHeroCreatorBodyGeneratorPatch: native face-editor Done guard applied.");
            }

            if (cancel != null && cancelPrefix != null && !ReferenceEquals(_patchedCancel, cancel))
            {
                harmony.Patch(cancel, prefix: new HarmonyMethod(cancelPrefix));
                _patchedCancel = cancel;
                ModLogger.Info("CoopHeroCreatorBodyGeneratorPatch: native face-editor Cancel guard applied.");
            }
        }

        public static void Arm(Action done, Action cancel)
        {
            _done = done;
            _cancel = cancel;
            _armed = true;
        }

        public static void Disarm()
        {
            _armed = false;
            _done = null;
            _cancel = null;
        }

        private static bool DonePrefix(object __instance)
        {
            if (!IsCreatorEditorActive()) return true;

            Action done = _done;
            try
            {
                object bodyGenerator = AccessTools.Property(__instance.GetType(), "BodyGen")?.GetValue(__instance, null);
                MethodInfo save = bodyGenerator == null ? null : AccessTools.Method(bodyGenerator.GetType(), "SaveCurrentCharacter");
                if (save == null) throw new MissingMethodException("BodyGenerator.SaveCurrentCharacter");
                save.Invoke(bodyGenerator, null);

                AccessTools.Method(__instance.GetType(), "ClearAgentVisuals")?.Invoke(__instance, null);
                Disarm();
                done?.Invoke();
                InvokeActionField(__instance, "_affirmativeAction");
            }
            catch (Exception ex)
            {
                ModLogger.Error("CoopHeroCreatorBodyGeneratorPatch: safe Done completion failed.", Unwrap(ex));
                Action cancel = _cancel;
                Disarm();
                cancel?.Invoke();
                TryInvokeActionField(__instance, "_negativeAction");
            }

            return false;
        }

        private static bool CancelPrefix()
        {
            if (!IsCreatorEditorActive()) return true;

            Action cancel = _cancel;
            Disarm();
            cancel?.Invoke();
            return true;
        }

        private static bool IsCreatorEditorActive()
        {
            return _armed &&
                   Mission.Current != null &&
                   Mission.Current.GetMissionBehavior<CoopHeroCreationMissionNetwork>() != null;
        }

        private static MethodInfo FindExplicitInterfaceMethod(Type targetType, string methodName)
        {
            return targetType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(method =>
                    method.GetParameters().Length == 0 &&
                    (string.Equals(method.Name, methodName, StringComparison.Ordinal) ||
                     method.Name.EndsWith("." + methodName, StringComparison.Ordinal)));
        }

        private static void InvokeActionField(object instance, string fieldName)
        {
            FieldInfo field = AccessTools.Field(instance.GetType(), fieldName);
            Delegate action = field?.GetValue(instance) as Delegate;
            if (action == null) throw new MissingFieldException(instance.GetType().FullName, fieldName);
            action.DynamicInvoke();
        }

        private static void TryInvokeActionField(object instance, string fieldName)
        {
            try { InvokeActionField(instance, fieldName); }
            catch (Exception ex)
            {
                ModLogger.Error("CoopHeroCreatorBodyGeneratorPatch: failed to close face editor after an error.", Unwrap(ex));
            }
        }

        private static Exception Unwrap(Exception ex)
        {
            return ex is TargetInvocationException invocation && invocation.InnerException != null
                ? invocation.InnerException
                : ex;
        }
    }
}
