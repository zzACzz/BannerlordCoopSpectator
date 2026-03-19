using System;
using TaleWorlds.MountAndBlade;
class X {
    static void Main() {
        Console.WriteLine(typeof(Formation).GetProperty("Index") != null);
        Console.WriteLine(typeof(Formation).GetProperty("FormationIndex") != null);
        Console.WriteLine(typeof(Formation).GetProperty("CountOfUnits") != null);
    }
}
