using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

internal static class Program
{
    private static int Main()
    {
        try
        {
            string repositoryRoot = ResolveRepositoryRoot();
            ValidateSharedProperties(Path.Combine(repositoryRoot, "Directory.Build.props"));
            ValidateClientProject(Path.Combine(repositoryRoot, "CoopSpectator.csproj"));
            ValidateDedicatedProject(Path.Combine(repositoryRoot, "DedicatedServer", "CoopSpectatorDedicated.csproj"));
            Console.WriteLine("Coop compile-only project contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void ValidateSharedProperties(string path)
    {
        XDocument document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        XElement compileOnly = FindProperty(document, "CoopCompileOnly");
        Assert(compileOnly.Value == "false", "CoopCompileOnly must default to false.");
        Assert(Contains(compileOnly.Attribute("Condition")?.Value, "'$(CoopCompileOnly)'==''"), "The CoopCompileOnly default must not override an explicit caller value.");

        foreach (string propertyName in new[]
                 {
                     "BaseOutputPath",
                     "BaseIntermediateOutputPath",
                     "MSBuildProjectExtensionsPath",
                     "RestorePackagesPath"
                 })
        {
            XElement property = FindProperty(document, propertyName);
            string condition = property.Attribute("Condition")?.Value ?? string.Empty;
            Assert(Contains(condition, "'$(CoopCompileOnly)'=='true'"), propertyName + " must be scoped to compile-only mode.");
            Assert(Contains(condition, "'$(CoopCompileOutputRoot)'!=''"), propertyName + " must require a caller-owned output root.");
            Assert(Contains(property.Value, "$(CoopCompileOutputRoot)"), propertyName + " must remain under the caller-owned output root.");
        }

        XElement defaultItemExcludes = FindProperty(document, "DefaultItemExcludes");
        Assert(Contains(defaultItemExcludes.Attribute("Condition")?.Value, "'$(CoopCompileOnly)'=='true'"), "Local output exclusions must be scoped to compile-only mode.");
        Assert(Contains(defaultItemExcludes.Value, "$(MSBuildProjectDirectory)\\bin\\**"), "Compile-only evaluation must exclude the project's local bin tree.");
        Assert(Contains(defaultItemExcludes.Value, "$(MSBuildProjectDirectory)\\obj\\**"), "Compile-only evaluation must exclude the project's local obj tree.");
    }

    private static void ValidateClientProject(string path)
    {
        XDocument document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        ValidateOutputPaths(document, "Module\\CoopSpectator\\bin\\Win64_Shipping_Client\\");
        ValidateGuardedTarget(document, "DeployModToGame");
        ValidateGuardedTarget(document, "BuildAndDeployDedicatedModule");

        XElement compileOnlyDedicatedDefault = document.Descendants("BuildDedicatedServerModule")
            .Single(element => Contains(element.Attribute("Condition")?.Value, "'$(CoopCompileOnly)'=='true'"));
        Assert(compileOnlyDedicatedDefault.Value == "false", "Compile-only client builds must disable the implicit dedicated build.");
        ValidateCompileOnlyDiagnosticTarget(document, "CoopSpectator");
    }

    private static void ValidateDedicatedProject(string path)
    {
        XDocument document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        ValidateOutputPaths(document, "..\\Module\\CoopSpectatorDedicated\\bin\\Win64_Shipping_Server\\");
        ValidateGuardedTarget(document, "DeployServerToDedicated");
        ValidateCompileOnlyDiagnosticTarget(document, "CoopSpectatorDedicated");
    }

    private static void ValidateOutputPaths(XDocument document, string normalOutput)
    {
        XElement[] outputPaths = document.Descendants("OutputPath").ToArray();
        XElement normal = outputPaths.Single(element => element.Value == normalOutput);
        XElement compileOnly = outputPaths.Single(element => element.Value == "$(BaseOutputPath)");
        Assert(Contains(normal.Attribute("Condition")?.Value, "'$(CoopCompileOnly)'!='true'"), "The normal output path must be disabled in compile-only mode.");
        Assert(Contains(compileOnly.Attribute("Condition")?.Value, "'$(CoopCompileOnly)'=='true'"), "The compile-only output path must be explicitly gated.");
        Assert(Contains(compileOnly.Attribute("Condition")?.Value, "'$(CoopCompileOutputRoot)'!=''"), "The compile-only output path must require the caller-owned root.");
    }

    private static void ValidateGuardedTarget(XDocument document, string targetName)
    {
        XElement target = document.Descendants("Target")
            .Single(element => string.Equals(element.Attribute("Name")?.Value, targetName, StringComparison.Ordinal));
        Assert(Contains(target.Attribute("Condition")?.Value, "'$(CoopCompileOnly)'!='true'"), targetName + " must be disabled in compile-only mode.");
    }

    private static void ValidateCompileOnlyDiagnosticTarget(XDocument document, string projectName)
    {
        XElement target = document.Descendants("Target")
            .Single(element => string.Equals(element.Attribute("Name")?.Value, "ValidateCoopCompileOnlyConfiguration", StringComparison.Ordinal));
        Assert(Contains(target.Attribute("Condition")?.Value, "'$(CoopCompileOnly)'=='true'"), projectName + " must validate only the explicit compile-only path.");
        Assert(target.Elements("Error").Any(), projectName + " must reject a missing or non-absolute output root.");
        Assert(target.Elements("Message").Any(element => Contains(element.Attribute("Text")?.Value, "CoopCompileOnly=true")), projectName + " must report that compile-only mode is active.");
    }

    private static XElement FindProperty(XDocument document, string name)
    {
        return document.Descendants(name).Single();
    }

    private static string ResolveRepositoryRoot()
    {
        string configuredRoot = Environment.GetEnvironmentVariable("COOPSPECTATOR_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            string resolvedRoot = Path.GetFullPath(configuredRoot);
            if (File.Exists(Path.Combine(resolvedRoot, "CoopSpectator.csproj")) &&
                File.Exists(Path.Combine(resolvedRoot, "Directory.Build.props")))
            {
                return resolvedRoot;
            }

            throw new InvalidOperationException("COOPSPECTATOR_REPOSITORY_ROOT does not identify this repository.");
        }

        DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CoopSpectator.csproj")) &&
                File.Exists(Path.Combine(current.FullName, "Directory.Build.props")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root could not be resolved from the contract-test output directory.");
    }

    private static bool Contains(string value, string expected)
    {
        return (value ?? string.Empty).IndexOf(expected, StringComparison.Ordinal) >= 0;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
