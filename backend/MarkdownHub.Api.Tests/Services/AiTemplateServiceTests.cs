using MarkdownHub.Api.Services;
using MarkdownHub.Api.Tests.Controllers;

namespace MarkdownHub.Api.Tests.Services;

public class AiTemplateServiceTests
{
    private const string Template = """
        # Adventure

        {{Scene}}

        {{Interactible}}
        {{Interactible}}

        ```ai-template
        Scene:
        - Random biome and location.

        Interactible:
        - One brief interactible.
        - Format: **Name**. One brief sentence.
        - Example: **Rusted Lantern**. It still holds a little oil.
        ```
        """;

    private readonly FakeAiService _ai = new();
    private readonly AiTemplateService _sut;
    private readonly ParsedAiTemplate _template = AiTemplateParser.Parse(Template);

    public AiTemplateServiceTests()
    {
        _sut = new AiTemplateService(_ai);
    }

    private AiTemplateSlot Slot(string id) => _template.Slots.First(s => s.Id == id);

    [Fact]
    public async Task GenerateSlot_ValidFirstReply_IsReturnedWithNoWarnings()
    {
        _ai.Respond = (_, _) => "**Minecart**. It is half buried.";

        var result = await _sut.GenerateSlotAsync(_template, Slot("Interactible#1"), [], AiTemplateMode.Generate);

        Assert.Equal("**Minecart**. It is half buried.", result.Content);
        Assert.Empty(result.Warnings);
        Assert.Equal(AiPrompts.AiTemplateSystemPrompt, _ai.LastSystemPrompt);
    }

    [Fact]
    public async Task GenerateSlot_InvalidThenValid_RetriesOnceAndNamesTheFailedCheck()
    {
        var calls = 0;
        string? retryPrompt = null;
        _ai.Respond = (_, prompt) =>
        {
            calls++;
            if (calls == 1)
            {
                return "## A minecart sits here.";
            }
            retryPrompt = prompt;
            return "**Minecart**. It is half buried.";
        };

        var result = await _sut.GenerateSlotAsync(_template, Slot("Interactible#1"), [], AiTemplateMode.Generate);

        Assert.Equal(2, calls);
        Assert.Equal("**Minecart**. It is half buried.", result.Content);
        Assert.Empty(result.Warnings);
        Assert.Contains("PROBLEM WITH YOUR LAST REPLY", retryPrompt);
        Assert.Contains("heading", retryPrompt);
    }

    [Fact]
    public async Task GenerateSlot_InvalidTwice_ReturnsTheContentWithWarningsRatherThanThrowing()
    {
        _ai.Respond = (_, _) => "A minecart sits here.";

        var result = await _sut.GenerateSlotAsync(_template, Slot("Interactible#1"), [], AiTemplateMode.Generate);

        Assert.Equal("A minecart sits here.", result.Content);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public async Task GenerateSlot_LockedSiblings_AppearInThePromptMarkedLocked()
    {
        List<AiTemplateSlotValue> values =
        [
            new("Scene#1", "An abandoned mine in an orange hill.", true),
            new("Interactible#2", "**Warning Sign**. Its paint has flaked away.", false),
        ];

        await _sut.GenerateSlotAsync(_template, Slot("Interactible#1"), values, AiTemplateMode.Generate);

        Assert.Contains("ALREADY GENERATED", _ai.LastUserPrompt);
        Assert.Contains("Scene (LOCKED", _ai.LastUserPrompt);
        Assert.Contains("An abandoned mine in an orange hill.", _ai.LastUserPrompt);
        Assert.Contains("**Warning Sign**", _ai.LastUserPrompt);
    }

    [Fact]
    public async Task GenerateSlot_RepeatedPlaceholder_TellsTheModelWhichItemItIs()
    {
        await _sut.GenerateSlotAsync(_template, Slot("Interactible#2"), [], AiTemplateMode.Generate);

        Assert.Contains("item 2 of 2", _ai.LastUserPrompt);
    }

    [Fact]
    public async Task GenerateSlot_Example_IsFencedAsFormatOnly()
    {
        await _sut.GenerateSlotAsync(_template, Slot("Interactible#1"), [], AiTemplateMode.Generate);

        Assert.Contains("FORMAT EXAMPLE", _ai.LastUserPrompt);
        Assert.Contains("never reuse its subject matter", _ai.LastUserPrompt);
    }

    [Fact]
    public async Task GenerateSlot_Improve_SendsTheCurrentTextToRevise()
    {
        List<AiTemplateSlotValue> values = [new("Interactible#1", "**Minecart**. It is here.", false)];

        await _sut.GenerateSlotAsync(_template, Slot("Interactible#1"), values, AiTemplateMode.Improve);

        Assert.Contains("CURRENT TEXT", _ai.LastUserPrompt);
        Assert.Contains("**Minecart**. It is here.", _ai.LastUserPrompt);
    }

    [Fact]
    public async Task GenerateSlot_AiServiceFailure_Propagates()
    {
        _ai.ThrowOnComplete = new AiServiceException("Ollama is unreachable.");

        await Assert.ThrowsAsync<AiServiceException>(
            () => _sut.GenerateSlotAsync(_template, Slot("Scene#1"), [], AiTemplateMode.Generate));
    }
}
