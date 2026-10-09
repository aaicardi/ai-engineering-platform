using AIHarness.Orchestration;

namespace AIHarness.Tests;

public sealed class QualityGateTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("aiharness-gate-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string fileName, string content) => File.WriteAllText(Path.Combine(_root, fileName), content);

    [Theory]
    [InlineData("App.sln")]
    [InlineData("App.slnx")]
    [InlineData("App.csproj")]
    public void Detect_DotnetProject_RunsDotnetTest(string fileName)
    {
        Write(fileName, "");

        Assert.Same(QualityGate.DotnetTest, QualityGate.Detect(_root));
    }

    [Fact]
    public void Detect_PackageJsonWithTestScript_RunsNpmTest()
    {
        Write("package.json", """{ "name": "web", "scripts": { "test": "vitest run" } }""");

        var gate = QualityGate.Detect(_root);

        Assert.Same(QualityGate.NpmTest, gate);
        Assert.Equal("npm test", gate!.ToString());
    }

    [Theory]
    [InlineData("""{ "name": "web" }""")]
    [InlineData("""{ "scripts": { "build": "tsc" } }""")]
    [InlineData("""{ "scripts": { "test": "echo \"Error: no test specified\" && exit 1" } }""")]
    [InlineData("not json")]
    public void Detect_PackageJsonWithoutRealTests_IsNotAQualityGate(string packageJson)
    {
        Write("package.json", packageJson);

        Assert.Null(QualityGate.Detect(_root));
    }

    [Theory]
    [InlineData("build:\n\tgo build\n\ntest:\n\tgo test ./...\n", true)]
    [InlineData("test: build\n\t./run-tests\n", true)]
    [InlineData("test := value\nbuild:\n\tcc main.c\n", false)]
    public void Detect_Makefile_RunsMakeTestOnlyWithATestTarget(string makefile, bool expected)
    {
        Write("Makefile", makefile);

        Assert.Equal(expected ? QualityGate.MakeTest : null, QualityGate.Detect(_root));
    }

    [Fact]
    public void Detect_DotnetTakesPrecedenceOverNpmAndMake()
    {
        Write("package.json", """{ "scripts": { "test": "jest" } }""");
        Write("Makefile", "test:\n\tmake check\n");
        Write("App.sln", "");

        Assert.Same(QualityGate.DotnetTest, QualityGate.Detect(_root));
    }

    [Fact]
    public void Detect_EmptyOrMissingRepository_HasNoQualityGate()
    {
        Assert.Null(QualityGate.Detect(_root));
        Assert.Null(QualityGate.Detect(Path.Combine(_root, "missing")));
    }
}
