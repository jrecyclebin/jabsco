using Jabsco.Core.Skills;

namespace Jabsco.Core.Tests;

public sealed class CommandLoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public CommandLoaderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void WriteCommand(string name, string content)
        => File.WriteAllText(Path.Combine(_dir, name + ".md"), content);

    // ── Load ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_ReturnsFileContent()
    {
        WriteCommand("greet", "Hello, world.");
        Assert.Equal("Hello, world.", CommandLoader.Load("greet", dir: _dir));
    }

    [Fact]
    public void Load_IsCaseInsensitive()
    {
        WriteCommand("Greet", "Hello.");
        Assert.Equal("Hello.", CommandLoader.Load("GREET", dir: _dir));
    }

    [Fact]
    public void Load_StripsFrontMatter()
    {
        WriteCommand("greet", "---\ndescription: A greeting\n---\nHello, world.");
        Assert.Equal("Hello, world.", CommandLoader.Load("greet", dir: _dir));
    }

    [Fact]
    public void Load_MissingCommand_ThrowsWithClearMessage()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => CommandLoader.Load("ghost", dir: _dir));
        Assert.Contains("ghost", ex.Message);
        Assert.Contains(_dir, ex.Message);
    }

    [Fact]
    public void Load_MissingDirectory_ThrowsWithClearMessage()
    {
        var missingDir = Path.Combine(_dir, "nonexistent");
        var ex = Assert.Throws<FileNotFoundException>(() => CommandLoader.Load("anything", dir: missingDir));
        Assert.Contains("anything", ex.Message);
    }

    // ── $ARGUMENTS substitution ───────────────────────────────────────────────

    [Fact]
    public void Load_SubstitutesArguments()
    {
        WriteCommand("review", "Review $ARGUMENTS for issues.");
        Assert.Equal("Review src/foo.cs for issues.", CommandLoader.Load("review", "src/foo.cs", _dir));
    }

    [Fact]
    public void Load_NoArgumentsMarker_AppendsTrailingText()
    {
        WriteCommand("describe", "Describe what you see.");
        Assert.Equal("Describe what you see.\n\nextra context", CommandLoader.Load("describe", "extra context", _dir));
    }

    [Fact]
    public void Load_EmptyArguments_ReturnsContentAsIs()
    {
        WriteCommand("check", "Check the system.");
        Assert.Equal("Check the system.", CommandLoader.Load("check", "", _dir));
    }

    // ── List ──────────────────────────────────────────────────────────────────

    [Fact]
    public void List_ReturnsNamesAlphabetically()
    {
        WriteCommand("zebra", "z");
        WriteCommand("alpha", "a");
        WriteCommand("middle", "m");
        Assert.Equal(["alpha", "middle", "zebra"], CommandLoader.List(_dir));
    }

    [Fact]
    public void List_EmptyDirectory_ReturnsEmpty()
    {
        Assert.Empty(CommandLoader.List(_dir));
    }

    [Fact]
    public void List_MissingDirectory_ReturnsEmpty()
    {
        Assert.Empty(CommandLoader.List(Path.Combine(_dir, "nonexistent")));
    }

    // ── Resolve ───────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_NoSlashToken_ReturnsNull()
    {
        Assert.Null(CommandLoader.Resolve("do something", _dir));
    }

    [Fact]
    public void Resolve_CommandNotFound_ReturnsNull()
    {
        Assert.Null(CommandLoader.Resolve("/ghost", _dir));
    }

    [Fact]
    public void Resolve_ReplacesPromptWithCommandContent()
    {
        WriteCommand("check", "Run a system check.");
        Assert.Equal("Run a system check.", CommandLoader.Resolve("/check", _dir));
    }

    [Fact]
    public void Resolve_SubstitutesArguments()
    {
        WriteCommand("review", "Review $ARGUMENTS for issues.");
        Assert.Equal("Review src/foo.cs for issues.", CommandLoader.Resolve("/review src/foo.cs", _dir));
    }

    [Fact]
    public void Resolve_AppendsTrailingLinesWhenNoArgumentsMarker()
    {
        WriteCommand("check", "Check the system.");
        Assert.Equal("Check the system.\n\nfocus on errors", CommandLoader.Resolve("/check\nfocus on errors", _dir));
    }

    [Fact]
    public void Resolve_AppendsTrailingLinesAfterArgumentSubstitution()
    {
        WriteCommand("review", "Review $ARGUMENTS for issues.");
        Assert.Equal("Review src/foo.cs for issues.\n\nbe thorough", CommandLoader.Resolve("/review src/foo.cs\nbe thorough", _dir));
    }
}
