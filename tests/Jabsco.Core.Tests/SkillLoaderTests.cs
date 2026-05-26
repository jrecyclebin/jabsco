using Jabsco.Core.Skills;

namespace Jabsco.Core.Tests;

public sealed class SkillLoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public SkillLoaderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void WriteSkill(string name, string content)
    {
        var skillDir = Path.Combine(_dir, name);
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), content);
    }

    // ── Load ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_NoFrontMatter_ReturnsBodyContent()
    {
        WriteSkill("analyst", "You are a data analyst.");
        Assert.Equal("You are a data analyst.", SkillLoader.Load("analyst", dir: _dir));
    }

    [Fact]
    public void Load_IsCaseInsensitive()
    {
        WriteSkill("Analyst", "You are a data analyst.");
        Assert.Equal("You are a data analyst.", SkillLoader.Load("ANALYST", dir: _dir));
    }

    [Fact]
    public void Load_StripsFrontMatter()
    {
        WriteSkill("analyst", "---\nname: Analyst\ndescription: Data analysis\n---\nYou are a data analyst.");
        Assert.Equal("You are a data analyst.", SkillLoader.Load("analyst", dir: _dir));
    }

    [Fact]
    public void Load_MissingSkill_ThrowsWithClearMessage()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => SkillLoader.Load("ghost", dir: _dir));
        Assert.Contains("ghost", ex.Message);
        Assert.Contains(_dir, ex.Message);
    }

    [Fact]
    public void Load_MissingDirectory_ThrowsWithClearMessage()
    {
        var missingDir = Path.Combine(_dir, "nonexistent");
        var ex = Assert.Throws<FileNotFoundException>(() => SkillLoader.Load("anything", dir: missingDir));
        Assert.Contains("anything", ex.Message);
    }

    // ── Argument substitution ─────────────────────────────────────────────────

    [Fact]
    public void Load_InlineArguments_SubstitutesValues()
    {
        WriteSkill("fixer", "---\narguments: issue-number\n---\nFix issue $issue-number.");
        Assert.Equal("Fix issue 42.", SkillLoader.Load("fixer", args: ["42"], dir: _dir));
    }

    [Fact]
    public void Load_BlockArguments_SubstitutesValues()
    {
        WriteSkill("conv", "---\narguments:\n  - from\n  - to\n---\nConvert $from to $to.");
        Assert.Equal("Convert json to yaml.", SkillLoader.Load("conv", args: ["json", "yaml"], dir: _dir));
    }

    [Fact]
    public void Load_FewerArgsThanNames_SubstitutesAvailable()
    {
        WriteSkill("multi", "---\narguments: a b\n---\n$a and $b.");
        Assert.Equal("X and $b.", SkillLoader.Load("multi", args: ["X"], dir: _dir));
    }

    // ── LoadInfo / front matter parsing ──────────────────────────────────────

    [Fact]
    public void LoadInfo_ParsesAllFrontMatterFields()
    {
        WriteSkill("role", """
            ---
            name: My Role
            description: Does role things
            when_to_use: When a role is needed
            argument-hint: [role-name]
            arguments: role-name
            ---
            Act as $role-name.
            """);

        var info = SkillLoader.LoadInfo("role", _dir);
        Assert.Equal("role", info.Key);
        Assert.Equal("My Role", info.Name);
        Assert.Equal("Does role things", info.Description);
        Assert.Equal("When a role is needed", info.WhenToUse);
        Assert.Equal("[role-name]", info.ArgumentHint);
        Assert.Equal(["role-name"], info.Arguments);
        Assert.Equal("Act as $role-name.", info.Content.Trim());
    }

    [Fact]
    public void LoadInfo_NoName_FallsBackToKey()
    {
        WriteSkill("my-skill", "Some content.");
        var info = SkillLoader.LoadInfo("my-skill", _dir);
        Assert.Equal("my-skill", info.Name);
    }

    [Fact]
    public void LoadInfo_NoDescription_UsesFirstParagraph()
    {
        WriteSkill("auto-desc", "# Heading\n\nFirst paragraph text.\n\nSecond paragraph.");
        var info = SkillLoader.LoadInfo("auto-desc", _dir);
        Assert.Equal("First paragraph text.", info.Description);
    }

    [Fact]
    public void LoadInfo_ListingDescription_TruncatesAt1536()
    {
        var longDesc = new string('x', 2000);
        WriteSkill("long", $"---\ndescription: {longDesc}\n---\nbody");
        var info = SkillLoader.LoadInfo("long", _dir);
        Assert.Equal(1536, info.ListingDescription.Length);
    }

    [Fact]
    public void LoadInfo_ListingDescription_CombinesDescriptionAndWhenToUse()
    {
        WriteSkill("combo", "---\ndescription: Does things\nwhen_to_use: When you need things\n---\nbody");
        var info = SkillLoader.LoadInfo("combo", _dir);
        Assert.Equal("Does things When you need things", info.ListingDescription);
    }

    // ── List ─────────────────────────────────────────────────────────────────

    [Fact]
    public void List_ReturnsAllSkillsSortedByKey()
    {
        WriteSkill("zebra", "z");
        WriteSkill("alpha", "a");
        WriteSkill("middle", "m");

        var keys = SkillLoader.List(_dir).Select(s => s.Key).ToList();
        Assert.Equal(["alpha", "middle", "zebra"], keys);
    }

    [Fact]
    public void List_EmptyDirectory_ReturnsEmpty()
    {
        Assert.Empty(SkillLoader.List(_dir));
    }

    [Fact]
    public void List_MissingDirectory_ReturnsEmpty()
    {
        Assert.Empty(SkillLoader.List(Path.Combine(_dir, "nonexistent")));
    }

    [Fact]
    public void List_IgnoresSubdirsWithoutSkillMd()
    {
        WriteSkill("valid", "content");
        Directory.CreateDirectory(Path.Combine(_dir, "no-skill-file"));

        Assert.Equal(["valid"], SkillLoader.List(_dir).Select(s => s.Key));
    }

    // ── Resolve ───────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_NoSkillTokens_ReturnsPromptUnchanged()
    {
        var prompt = "Do the thing.";
        Assert.Equal(prompt, SkillLoader.Resolve(prompt, _dir));
    }

    [Fact]
    public void Resolve_SingleSkillToken_PrependsContent()
    {
        WriteSkill("analyst", "You are an analyst.");
        var result = SkillLoader.Resolve("/analyst\nDo the analysis.", _dir);
        Assert.Equal("You are an analyst.\n\nDo the analysis.", result);
    }

    [Fact]
    public void Resolve_SkillWithArguments_SubstitutesInline()
    {
        WriteSkill("fixer", "---\narguments: issue\n---\nFix $issue.");
        var result = SkillLoader.Resolve("/fixer 99\nBe thorough.", _dir);
        Assert.Equal("Fix 99.\n\nBe thorough.", result);
    }

    [Fact]
    public void Resolve_MultipleSkillTokens_ConcatenatesInOrder()
    {
        WriteSkill("role", "Role content.");
        WriteSkill("style", "Style content.");
        var result = SkillLoader.Resolve("/role\n/style\nDo the work.", _dir);
        Assert.Equal("Role content.\n\nStyle content.\n\nDo the work.", result);
    }

    [Fact]
    public void Resolve_SkillOnlyPrompt_ReturnsSkillContent()
    {
        WriteSkill("analyst", "You are an analyst.");
        Assert.Equal("You are an analyst.", SkillLoader.Resolve("/analyst", _dir));
    }

    [Fact]
    public void Resolve_MissingSkill_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => SkillLoader.Resolve("/ghost", _dir));
    }
}
