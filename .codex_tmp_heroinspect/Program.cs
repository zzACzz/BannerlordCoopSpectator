using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

string gameDir = @"C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client";
string runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location) ?? AppContext.BaseDirectory;
var resolver = new PathAssemblyResolver(
    Directory.GetFiles(gameDir, "*.dll")
        .Concat(Directory.GetFiles(runtimeDir, "*.dll")));
using var mlc = new MetadataLoadContext(resolver, "System.Private.CoreLib");

Assembly asm = mlc.LoadFromAssemblyPath(Path.Combine(gameDir, "TaleWorlds.CampaignSystem.dll"));
Type heroType = asm.GetType("TaleWorlds.CampaignSystem.Hero") ?? throw new InvalidOperationException("Hero type not found.");

Console.WriteLine("Properties:");
foreach (PropertyInfo property in heroType
    .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
    .Where(p => p.Name.Contains("Hit", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Health", StringComparison.OrdinalIgnoreCase))
    .OrderBy(p => p.Name))
{
    Console.WriteLine(
        $"P {property.PropertyType.Name} {property.Name} " +
        $"CanWrite={property.CanWrite} PublicGet={property.GetMethod?.IsPublic} PublicSet={property.SetMethod?.IsPublic}");
}

Console.WriteLine("Methods:");
foreach (MethodInfo method in heroType
    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
    .Where(m => m.Name.Contains("Hit", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Health", StringComparison.OrdinalIgnoreCase))
    .OrderBy(m => m.Name))
{
    string parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
    Console.WriteLine($"M {method.ReturnType.Name} {method.Name}({parameters}) Public={method.IsPublic}");
}
