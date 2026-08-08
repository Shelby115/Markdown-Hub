using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Services;

public class AiTemplateParserTests
{
    private const string AdventureTemplate = """
        # Adventure

        {{Scene}}

        ## Interactibles

        {{Interactible}}
        {{Interactible}}

        ## Encounter

        {{Encounter}}

        ```ai-template
        Scene:
        - Random biome and location.
        - Max words: 60

        Interactible:
        - One brief interactible.
        - Format: **Name**. One brief sentence.
        - Example: **Rusted Lantern**. It still holds a little oil.
        - Max sentences: 1

        Encounter:
        - One NPC or monster appropriate to the setting.
        ```
        """;

    [Fact]
    public void Parse_NoInstructionBlock_ProducesNoSlots()
    {
        var parsed = AiTemplateParser.Parse("# Notes\n\n{{Author}}\n");

        Assert.Empty(parsed.Slots);
        Assert.Equal(["Author"], parsed.FillInVariables);
    }

    [Fact]
    public void Parse_PlaceholderWithoutInstruction_BecomesFillInVariableNotSlot()
    {
        var parsed = AiTemplateParser.Parse("{{Author}}\n{{Scene}}\n\n```ai-template\nScene:\n- Something.\n```");

        Assert.Equal(["Author"], parsed.FillInVariables);
        Assert.Single(parsed.Slots);
        Assert.Equal("Scene", parsed.Slots[0].Name);
    }

    [Fact]
    public void Parse_RepeatedPlaceholder_NumbersSlotsAndSharesTheCount()
    {
        var parsed = AiTemplateParser.Parse(AdventureTemplate);

        var interactibles = parsed.Slots.Where(s => s.Name == "Interactible").ToList();
        Assert.Equal(2, interactibles.Count);
        Assert.Equal("Interactible#1", interactibles[0].Id);
        Assert.Equal("Interactible#2", interactibles[1].Id);
        Assert.All(interactibles, s => Assert.Equal(2, s.Count));
        Assert.Equal([1, 2], interactibles.Select(s => s.Index));
    }

    [Fact]
    public void Parse_InstructionBlock_IsRemovedFromTheStructure()
    {
        var parsed = AiTemplateParser.Parse(AdventureTemplate);

        var literal = string.Concat(parsed.Elements.Select(e => e.LiteralText ?? ""));
        Assert.DoesNotContain("ai-template", literal);
        Assert.DoesNotContain("Random biome", literal);
        Assert.Contains("## Interactibles", literal);
    }

    [Fact]
    public void Parse_TypedRules_AreRecognized()
    {
        var parsed = AiTemplateParser.Parse(AdventureTemplate);

        var interactible = parsed.Instructions["Interactible"];
        Assert.Equal("**Name**. One brief sentence.", interactible.Format);
        Assert.Equal("**Rusted Lantern**. It still holds a little oil.", interactible.Example);
        Assert.Equal(1, interactible.MaxSentences);
        Assert.Equal(["One brief interactible."], interactible.Rules);
        Assert.Equal(60, parsed.Instructions["Scene"].MaxWords);
    }

    [Fact]
    public void Parse_Purpose_IsTheProseBeforeTheFirstPlaceholder()
    {
        var parsed = AiTemplateParser.Parse(AdventureTemplate);

        Assert.Equal("# Adventure", parsed.Purpose);
    }

    [Fact]
    public void Parse_ElementsRoundTripTheStructureInOrder()
    {
        var parsed = AiTemplateParser.Parse("A {{Scene}} B\n\n```ai-template\nScene:\n- x\n```");

        Assert.Collection(
            parsed.Elements,
            e => Assert.Equal("A ", e.LiteralText),
            e => Assert.Equal("Scene#1", e.Slot!.Id),
            e => Assert.Equal(" B", e.LiteralText));
    }

    [Fact]
    public void Parse_TooManySlots_Throws()
    {
        var structure = string.Concat(Enumerable.Repeat("{{Scene}}\n", AiTemplateParser.MaxSlots + 1));
        var template = structure + "\n```ai-template\nScene:\n- x\n```";

        Assert.Throws<AiTemplateParseException>(() => AiTemplateParser.Parse(template));
    }

    [Fact]
    public void Parse_OversizedInstructionBlock_Throws()
    {
        var rules = string.Concat(Enumerable.Repeat("- a long rule that says very little\n", 400));
        var template = "{{Scene}}\n\n```ai-template\nScene:\n" + rules + "```";

        Assert.Throws<AiTemplateParseException>(() => AiTemplateParser.Parse(template));
    }

    [Fact]
    public void Parse_PoolPrefix_NamesTheGenerationPoolAndIsNotTreatedAsARule()
    {
        var template = "{{Interactible}}\n\n```ai-template\nInteractible:\n- Pool: Dungeon Interactible\n- Keep it brief.\n```";

        var instruction = AiTemplateParser.Parse(template).Instructions["Interactible"];

        Assert.Equal("Dungeon Interactible", instruction.Pool);
        Assert.Equal(["Keep it brief."], instruction.Rules);
    }

    [Fact]
    public void Parse_NoPoolPrefix_LeavesPoolUnset()
    {
        var template = "{{Scene}}\n\n```ai-template\nScene:\n- Random biome.\n```";

        Assert.Null(AiTemplateParser.Parse(template).Instructions["Scene"].Pool);
    }

    [Fact]
    public void ParseInstruction_ReadsAPoolsOwnPromptWithTheSameRecognizedPrefixes()
    {
        var instruction = AiTemplateParser.ParseInstruction(
            "Interactible", "- One brief interactible.\n- Format: **Name**. One sentence.\n- Max words: 30\n");

        Assert.Equal("Interactible", instruction.Name);
        Assert.Equal(["One brief interactible."], instruction.Rules);
        Assert.Equal("**Name**. One sentence.", instruction.Format);
        Assert.Equal(30, instruction.MaxWords);
    }

    [Fact]
    public void ParseInstruction_EmptyPrompt_ReturnsAnInstructionWithNoRules()
    {
        var instruction = AiTemplateParser.ParseInstruction("Interactible", "");

        Assert.Empty(instruction.Rules);
    }
}
