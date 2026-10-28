using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.Infrastructure;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core.ViewModelCollection;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;

namespace CoopSpectator.Campaign
{
    // Never invoke a native UI callback. Admit only the audited native closure
    // body that forwards a captured ExplainedNumber field to the plain formatter.
    internal static class CoopCampaignAutoBattleRewardCapture
    {
        private static readonly FieldInfo Provider = typeof(BasicTooltipViewModel).GetField("_tooltipProperties", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Dictionary<BasicTooltipViewModel, CoopCampaignPresentationValue> Cache =
            new Dictionary<BasicTooltipViewModel, CoopCampaignPresentationValue>();
        private static CoopCampaignMapTooltipCapture _formatter;
        internal static void Reset() { Cache.Clear(); _formatter = null; }

        internal static void Append(ViewModel owner, CoopCampaignPresentationValue root)
        {
            if (owner?.GetType().FullName != "SandBox.ViewModelCollection.SPScoreboardVM" ||
                root?.Properties == null || !VisibleFlag(root, "IsSimulation") || !VisibleFlag(root, "ShowScoreboard") ||
                !VisibleFlag(root, "IsOver") ||
                !root.Properties.TryGetValue("BattleResults", out var target) || target.Items == null) return;
            var list = owner.GetType().GetProperty("BattleResults")?.GetValue(owner) as IMBBindingList;
            if (list == null || list.Count != target.Items.Count || list.Count > 128) return;
            _formatter = _formatter ?? new CoopCampaignMapTooltipCapture();
            for (int i = 0; i < list.Count; i++)
            {
                if (!(list[i] is BattleResultVM reward) || reward.GetType() != typeof(BattleResultVM) ||
                    reward.Hint?.GetType() != typeof(BasicTooltipViewModel) || target.Items[i]?.Properties == null ||
                    !target.Items[i].Properties.TryGetValue("Hint", out var hint) || hint.Properties == null) continue;
                if (!Cache.TryGetValue(reward.Hint, out var rows))
                {
                    rows = null;
                    bool supported = TryReadExplanation(reward.Hint, owner.GetType(), out var explanation);
                    bool captured = supported && _formatter.TryCaptureAutoBattleExplanation(explanation, out rows);
                    // A busy text/language scope is transient. Do not poison this
                    // reward for its whole window merely because a capture yielded.
                    if (Cache.Count < 128 && (!supported || captured)) Cache.Add(reward.Hint, rows);
                }
                if (rows != null) hint.Properties["ReplicaTooltipRows"] = rows;
            }
        }

        private static bool VisibleFlag(CoopCampaignPresentationValue root, string key) =>
            root.Properties.TryGetValue(key, out var value) && value?.Kind == CoopCampaignPresentationValueKind.Boolean && value.Number == 1;

        internal static bool TryReadExplanation(BasicTooltipViewModel hint, Type scoreboardType, out ExplainedNumber explanation)
        {
            explanation = default(ExplainedNumber);
            try
            {
                if (hint?.GetType() != typeof(BasicTooltipViewModel) || scoreboardType?.FullName != "SandBox.ViewModelCollection.SPScoreboardVM" ||
                    scoreboardType.Assembly.GetName().Name != "SandBox.ViewModelCollection" ||
                    !(Provider?.GetValue(hint) is Func<List<TooltipProperty>> callback) || callback.GetInvocationList().Length != 1 ||
                    callback.Target == null) return false;
                var method = callback.Method;
                var type = callback.Target.GetType();
                if (method.IsStatic || method.DeclaringType != type || type.DeclaringType != scoreboardType ||
                    type.Assembly != scoreboardType.Assembly || !method.Name.StartsWith("<GetBattleRewards>b__", StringComparison.Ordinal) ||
                    method.GetParameters().Length != 0 || method.ReturnType != typeof(List<TooltipProperty>)) return false;
                // ldarg.0; ldflda <prepared field>; call GetExplainedNumberTooltip; ret
                byte[] il = method.GetMethodBody()?.GetILAsByteArray();
                if (il == null || il.Length != 12 || il[0] != 0x02 || il[1] != 0x7c || il[6] != 0x28 || il[11] != 0x2a) return false;
                var field = method.Module.ResolveField(BitConverter.ToInt32(il, 2));
                var call = method.Module.ResolveMethod(BitConverter.ToInt32(il, 7));
                var formatter = typeof(SandBox.ViewModelCollection.SandBoxUIHelper).GetMethod("GetExplainedNumberTooltip",
                    BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ExplainedNumber).MakeByRefType() }, null);
                if (field.DeclaringType != type || field.IsStatic || field.FieldType != typeof(ExplainedNumber) || call != formatter ||
                    (field.Name != "renownExplained" && field.Name != "influenceExplained" && field.Name != "moraleExplained")) return false;
                explanation = (ExplainedNumber)field.GetValue(callback.Target); // boxed value copy, no callback execution
                return true;
            }
            catch (Exception) { return false; }
        }
    }
}
