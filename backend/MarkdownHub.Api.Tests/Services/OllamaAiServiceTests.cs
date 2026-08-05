using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Services;

/// <summary>Routes every request to a caller-supplied handler function, so tests can inspect the
/// outgoing request and script the response without a real network call.</summary>
public class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        LastRequest = request;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        return respond(request);
    }
}

public class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler);
}

public class OllamaAiServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public OllamaAiServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private static IConfiguration Config(int? timeoutSeconds = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:Ollama:BaseUrl"] = "http://ollama.test:11434",
                ["Ai:Ollama:Model"] = "test-model",
                ["Ai:Ollama:TimeoutSeconds"] = timeoutSeconds?.ToString(),
            })
            .Build();

    [Fact]
    public async Task CompleteAsync_SendsCorrectlyShapedRequestAndParsesReply()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"message":{"role":"assistant","content":"  Hello there  "}}""", Encoding.UTF8, "application/json"),
        });
        var sut = new OllamaAiService(new FakeHttpClientFactory(handler), Config(), _db);

        var result = await sut.CompleteAsync("Be terse.", "Say hi.");

        Assert.Equal("Hello there", result); // trimmed
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("http://ollama.test:11434/api/chat", handler.LastRequest.RequestUri!.ToString());

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var root = doc.RootElement;
        Assert.Equal("test-model", root.GetProperty("model").GetString()); // no DB override - uses configured default
        Assert.False(root.GetProperty("stream").GetBoolean());
        var messages = root.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("Be terse.", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("Say hi.", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task CompleteAsync_DbModelOverride_TakesPrecedenceOverConfiguredDefault()
    {
        _db.Settings.Add(new AppSetting { Key = AppSetting.AiOllamaModelKey, Value = "custom-model" });
        await _db.SaveChangesAsync();

        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"message":{"role":"assistant","content":"hi"}}""", Encoding.UTF8, "application/json"),
        });
        var sut = new OllamaAiService(new FakeHttpClientFactory(handler), Config(), _db);

        await sut.CompleteAsync("sys", "user");

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("custom-model", doc.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task CompleteAsync_NonSuccessStatus_ThrowsAiServiceException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("model not found"),
        });
        var sut = new OllamaAiService(new FakeHttpClientFactory(handler), Config(), _db);

        var ex = await Assert.ThrowsAsync<AiServiceException>(() => sut.CompleteAsync("sys", "user"));
        Assert.Contains("500", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_ConnectionFailure_ThrowsFriendlyAiServiceException()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var sut = new OllamaAiService(new FakeHttpClientFactory(handler), Config(), _db);

        var ex = await Assert.ThrowsAsync<AiServiceException>(() => sut.CompleteAsync("sys", "user"));
        Assert.Contains("Ollama", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_EmptyReply_ThrowsAiServiceException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"message":{"role":"assistant","content":""}}""", Encoding.UTF8, "application/json"),
        });
        var sut = new OllamaAiService(new FakeHttpClientFactory(handler), Config(), _db);

        await Assert.ThrowsAsync<AiServiceException>(() => sut.CompleteAsync("sys", "user"));
    }

    [Fact]
    public async Task ListModelsAsync_ParsesModelNamesFromOllamaTagsResponse()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"models":[{"name":"gpt-oss:20b"},{"name":"llama3.1:8b"}]}""", Encoding.UTF8, "application/json"),
        });
        var sut = new OllamaAiService(new FakeHttpClientFactory(handler), Config(), _db);

        var models = await sut.ListModelsAsync();

        Assert.Equal(["gpt-oss:20b", "llama3.1:8b"], models);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("http://ollama.test:11434/api/tags", handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task ListModelsAsync_NonSuccessStatus_ThrowsAiServiceException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("down for maintenance"),
        });
        var sut = new OllamaAiService(new FakeHttpClientFactory(handler), Config(), _db);

        await Assert.ThrowsAsync<AiServiceException>(() => sut.ListModelsAsync());
    }
}
