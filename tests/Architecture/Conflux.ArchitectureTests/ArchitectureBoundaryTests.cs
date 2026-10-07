using Xunit;
using FluentAssertions;

namespace Conflux.ArchitectureTests;

/// <summary>Enforces source-level boundaries between Conflux services.</summary>
public sealed class ArchitectureBoundaryTests
{
    /// <summary>Prevents service projects from referencing another service project.</summary>
    [Fact]
    public void ServiceProjects_DoNotReferenceOtherServiceProjects()
    {
        var root = FindRepositoryRoot();
        var projectFiles = Directory.EnumerateFiles(
            Path.Combine(root, "src", "Services"),
            "*.csproj",
            SearchOption.AllDirectories).ToArray();

        foreach (var project in projectFiles)
        {
            var text = File.ReadAllText(project);
            var ownService = Path.GetFileName(Path.GetDirectoryName(project)!)!;
            var references = System.Text.RegularExpressions.Regex.Matches(
                text,
                @"<ProjectReference\s+Include=""([^""]+)""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            foreach (System.Text.RegularExpressions.Match reference in references)
            {
                var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, reference.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar)));
                target.Should().NotContain($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}Services{Path.DirectorySeparatorChar}", because: $"{ownService} must not reference another service project");
            }
        }
    }

    /// <summary>Prevents source files from reaching directly into another service namespace.</summary>
    [Fact]
    public void ServiceSource_DoesNotReferenceAnotherServiceNamespace()
    {
        var root = FindRepositoryRoot();
        var services = Directory.EnumerateDirectories(Path.Combine(root, "src", "Services"))
            .SelectMany(directory => Directory.EnumerateDirectories(directory))
            .ToArray();

        var serviceNames = services
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        foreach (var serviceDirectory in services)
        {
            var own = Path.GetFileName(serviceDirectory)!;
            foreach (var file in Directory.EnumerateFiles(serviceDirectory, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                foreach (var other in serviceNames.Where(name => !string.Equals(name, own, StringComparison.Ordinal)))
                {
                    var namespacePrefix = other!.StartsWith("Conflux.", StringComparison.Ordinal) ? other + "." : "Conflux." + other + ".";
                    text.Should().NotContain(namespacePrefix, because: $"{own} must communicate through contracts/APIs rather than another service's implementation namespace");
                }
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Conflux.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the Conflux repository root.");
    }
}
