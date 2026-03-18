using System;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
class P {
  static void Dump(Type t) {
    if (t == null) return;
    Console.WriteLine("TYPE=" + t.FullName);
    foreach (var p in t.GetProperties(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance).OrderBy(x => x.Name))
      Console.WriteLine("P " + p.PropertyType.Name + " " + p.Name);
  }
  static void Main() {
    var asm = typeof(Campaign).Assembly;
    Dump(asm.GetType("TaleWorlds.CampaignSystem.Encounters.PlayerEncounter"));
    Dump(asm.GetType("TaleWorlds.CampaignSystem.PlayerEncounter"));
    Dump(asm.GetType("TaleWorlds.CampaignSystem.MapEvents.MapEvent"));
    Dump(asm.GetType("TaleWorlds.CampaignSystem.MapEvent"));
  }
}
