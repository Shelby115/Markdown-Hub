using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Services;

public class AiTemplateValidatorTests
{
    private static AiTemplateInstruction Instruction(string? format = null, int? maxWords = null, int? maxSentences = null) =>
        new("Interactible", [], format, null, maxWords, maxSentences);

    [Fact]
    public void Clean_StripsASurroundingCodeFence()
    {
        Assert.Equal("**Minecart**. It is half buried.", AiTemplateValidator.Clean("```markdown\n**Minecart**. It is half buried.\n```"));
    }

    [Fact]
    public void Clean_StripsAPreambleFollowedByABlankLine()
    {
        Assert.Equal("The mine yawns open.", AiTemplateValidator.Clean("Here's your scene:\n\nThe mine yawns open."));
    }

    [Fact]
    public void Clean_LeavesAnAmbiguousPreambleForCheckToReport()
    {
        var cleaned = AiTemplateValidator.Clean("Sure, here it is\nThe mine yawns open.");

        Assert.StartsWith("Sure, here it is", cleaned);
        Assert.False(AiTemplateValidator.Check(cleaned, null).IsValid);
    }

    [Fact]
    public void Check_EmptyContent_IsInvalid()
    {
        Assert.False(AiTemplateValidator.Check("   ", null).IsValid);
    }

    [Fact]
    public void Check_HeadingInContent_IsInvalid()
    {
        Assert.False(AiTemplateValidator.Check("## Interactibles\n\nA minecart.", null).IsValid);
    }

    [Fact]
    public void Check_LeftoverPlaceholder_IsInvalid()
    {
        Assert.False(AiTemplateValidator.Check("A {{Thing}} sits here.", null).IsValid);
    }

    [Fact]
    public void Check_OverWordLimit_IsInvalid()
    {
        Assert.False(AiTemplateValidator.Check("one two three four", Instruction(maxWords: 3)).IsValid);
        Assert.True(AiTemplateValidator.Check("one two three", Instruction(maxWords: 3)).IsValid);
    }

    [Fact]
    public void Check_OverSentenceLimit_IsInvalid()
    {
        Assert.False(AiTemplateValidator.Check("One thing. Another thing.", Instruction(maxSentences: 1)).IsValid);
    }

    [Fact]
    public void Check_BoldNameFormat_IsEnforced()
    {
        var instruction = Instruction(format: "**Name**. One brief sentence.");

        Assert.True(AiTemplateValidator.Check("**Minecart**. It is half buried.", instruction).IsValid);
        Assert.False(AiTemplateValidator.Check("A minecart is half buried.", instruction).IsValid);
    }

    [Fact]
    public void Check_ValidContent_ReportsNoProblems()
    {
        var result = AiTemplateValidator.Check("**Minecart**. It is half buried.", Instruction(format: "**Name**.", maxWords: 10));

        Assert.True(result.IsValid);
        Assert.Empty(result.Problems);
    }
}
