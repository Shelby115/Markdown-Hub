using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;
using MarkdownHub.Api.Tests.Controllers;

namespace MarkdownHub.Api.Tests.Services;

public class AiTemplateServiceTests : IDisposable
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
    private readonly AppDbContext _db;
    private readonly GenerationPoolService _pools;
    private readonly AiTemplateService _sut;
    private readonly ParsedAiTemplate _template = AiTemplateParser.Parse(Template);

    public AiTemplateServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _pools = new GenerationPoolService(_db, _ai);
        _sut = new AiTemplateService(_ai, _pools);
    }

    public void Dispose() => _db.Dispose();

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

    // --- Generation pools ---

    private const string PooledTemplate = """
        # Adventure

        {{Interactible}}

        ```ai-template
        Interactible:
        - Pool: Interactible
        ```
        """;

    private async Task<ParsedAiTemplate> PooledAsync(bool poolExists = true, params string[] readyEntries)
    {
        if (poolExists)
        {
            var pool = await _pools.CreatePoolAsync("Interactible", "- One brief interactible.\n", 5, true);
            foreach (var content in readyEntries)
            {
                _ai.Respond = (_, _) => content;
                await _pools.GenerateEntryAsync(pool);
            }
        }
        _ai.Respond = null;
        return AiTemplateParser.Parse(PooledTemplate);
    }

    [Fact]
    public async Task GenerateSlot_PooledSlot_IsServedFromThePoolWithoutCallingTheModel()
    {
        var template = await PooledAsync(true, "A rusted lantern hangs here.");
        _ai.LastUserPrompt = null;

        var result = await _sut.GenerateSlotAsync(template, template.Slots[0], [], AiTemplateMode.Generate);

        Assert.Equal("A rusted lantern hangs here.", result.Content);
        Assert.NotNull(result.PoolEntryId);
        Assert.Null(_ai.LastUserPrompt);
    }

    [Fact]
    public async Task GenerateSlot_EmptyPool_GeneratesLiveAndRecordsItSoItIsNotGeneratedAgain()
    {
        var template = await PooledAsync();
        _ai.Respond = (_, _) => "A cracked bell sits in the corner.";

        var result = await _sut.GenerateSlotAsync(template, template.Slots[0], [], AiTemplateMode.Generate);

        Assert.Equal("A cracked bell sits in the corner.", result.Content);
        Assert.Null(result.PoolEntryId); // nothing to forget - it was never a pooled entry
        var pool = await _pools.FindPoolAsync("Interactible");
        Assert.Null(await _pools.GenerateEntryAsync(pool!));
    }

    [Fact]
    public async Task GenerateSlot_PooledSlot_UsesThePoolsOwnRulesNotTheTemplates()
    {
        await _pools.CreatePoolAsync("Interactible", "- Must mention rust.\n", 5, true);
        var template = AiTemplateParser.Parse(PooledTemplate);
        _ai.Respond = (_, _) => "A cracked bell sits in the corner.";

        await _sut.GenerateSlotAsync(template, template.Slots[0], [], AiTemplateMode.Generate);

        Assert.Contains("Must mention rust.", _ai.LastUserPrompt);
    }

    [Fact]
    public async Task GenerateSlot_UnknownPoolName_FallsBackToOrdinaryGeneration()
    {
        var template = await PooledAsync(poolExists: false);
        _ai.Respond = (_, _) => "A cracked bell sits in the corner.";

        var result = await _sut.GenerateSlotAsync(template, template.Slots[0], [], AiTemplateMode.Generate);

        Assert.Equal("A cracked bell sits in the corner.", result.Content);
    }

    [Fact]
    public async Task GenerateSlot_ImproveOnAPooledSlot_RewritesInsteadOfDrawingAnotherEntry()
    {
        var template = await PooledAsync(true, "A rusted lantern hangs here.");
        List<AiTemplateSlotValue> values = [new(template.Slots[0].Id, "A rusted lantern hangs here.", false)];
        _ai.Respond = (_, _) => "A rusted lantern hangs from a bent nail.";

        var result = await _sut.GenerateSlotAsync(template, template.Slots[0], values, AiTemplateMode.Improve);

        Assert.Equal("A rusted lantern hangs from a bent nail.", result.Content);
        Assert.Null(result.PoolEntryId);
        Assert.Contains("CURRENT TEXT", _ai.LastUserPrompt);
    }
}
