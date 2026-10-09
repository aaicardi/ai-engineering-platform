namespace AIHarness.Tests;

/// <summary>Argument validation of <c>--process-issue</c> with a target workspace; every case fails before any network call.</summary>
[Collection(ConsoleCollection.Name)]
public sealed class ProcessIssueCliTests
{
    [Fact]
    public async Task Main_TargetDirWithoutProcessIssue_IsRejected()
    {
        var (exitCode, _) = await CliHost.RunAsync("--target-dir", Path.GetTempPath());

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task Main_ProcessIssueWithInvalidRepo_IsRejected()
    {
        var (exitCode, _) = await CliHost.RunAsync("--process-issue", "1", "--repo", "not a repo");

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task Main_ProcessIssueWithMissingTargetDirAndNoRepoToClone_Fails()
    {
        var missing = Path.Combine(Path.GetTempPath(), "aiharness-missing-" + Guid.NewGuid().ToString("N"));

        var (exitCode, _) = await CliHost.RunAsync("--process-issue", "1", "--target-dir", missing);

        Assert.Equal(1, exitCode);
        Assert.False(Directory.Exists(missing));
    }
}
