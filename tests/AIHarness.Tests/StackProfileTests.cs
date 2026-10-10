using AIHarness.Orchestration;

namespace AIHarness.Tests;

public sealed class StackProfileTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("aiharness-stack-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Touch(string file) => File.WriteAllText(Path.Combine(_root, file), "{}");

    [Fact]
    public void Detect_DotNetSolution_AllowsOnlyDotNetCommands()
    {
        Touch("App.sln");

        var profile = StackProfile.Detect(_root);

        Assert.Equal([StackProfile.DotNet], profile.Stacks);
        Assert.Contains("Bash(dotnet test:*)", profile.AllowedCommands);
        Assert.DoesNotContain("Bash(npm test:*)", profile.AllowedCommands);
    }

    [Fact]
    public void Detect_AngularApp_CombinesNodeAndAngular()
    {
        Touch("package.json");
        Touch("angular.json");

        var profile = StackProfile.Detect(_root);

        Assert.Equal([StackProfile.Node, StackProfile.Angular], profile.Stacks);
        Assert.Contains("Bash(npm test:*)", profile.AllowedCommands);
        Assert.Contains("Bash(npx ng test:*)", profile.AllowedCommands);
    }

    [Fact]
    public void Detect_Greenfield_AllowsEveryStackSoTheAgentCanCreateTheProject()
    {
        var profile = StackProfile.Detect(_root);

        Assert.Empty(profile.Stacks);
        Assert.Contains("Bash(dotnet new classlib:*)", profile.AllowedCommands);
        Assert.Contains("Bash(npm init -y:*)", profile.AllowedCommands);
        Assert.Contains("Bash(make test:*)", profile.AllowedCommands);
    }

    [Fact]
    public void Detect_NeverAllowsNpxOrTemplateInstalls()
    {
        var profile = StackProfile.Detect(_root);

        Assert.DoesNotContain(profile.AllowedCommands, c => c.StartsWith("Bash(npx tsc", StringComparison.Ordinal) || c.StartsWith("Bash(npx vitest", StringComparison.Ordinal));
        Assert.DoesNotContain("Bash(dotnet new:*)", profile.AllowedCommands);
        Assert.DoesNotContain("Bash(npm init:*)", profile.AllowedCommands);
    }

    [Fact]
    public void Detect_NeverAllowsApplyingMigrations()
    {
        var profile = StackProfile.Detect(_root);

        Assert.DoesNotContain(profile.AllowedCommands, c => c.Contains("database update") || c.Contains("migrate deploy"));
        Assert.Contains("Bash(dotnet ef migrations add:*)", profile.AllowedCommands);
    }
}
