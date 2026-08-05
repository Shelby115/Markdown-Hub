using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Services;

public class HistorySettingsServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly HistorySettingsService _sut;

    public HistorySettingsServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _config = new ConfigurationBuilder().Build();
        _sut = new HistorySettingsService(_db, _config);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetAllAsync_NoOverridesSet_ReturnsHardDefaults()
    {
        var settings = await _sut.GetAllAsync();

        Assert.Equal(3, settings.VersionRetentionDays);
        Assert.Equal(30, settings.ActivityRetentionDays);
        Assert.Equal(14, settings.ActivityDefaultDays);
    }

    [Fact]
    public async Task GetVersionRetentionDaysAsync_FallsBackToConfigWhenNoDbOverride()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["History:VersionRetentionDays"] = "9" })
            .Build();
        var sut = new HistorySettingsService(_db, config);

        Assert.Equal(9, await sut.GetVersionRetentionDaysAsync());
    }

    [Fact]
    public async Task SetAsync_ThenGet_ReturnsTheOverride_TakingPrecedenceOverConfig()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["History:VersionRetentionDays"] = "9" })
            .Build();
        var sut = new HistorySettingsService(_db, config);

        await sut.SetAsync(HistorySettingsService.VersionRetentionDaysKey, 5);

        Assert.Equal(5, await sut.GetVersionRetentionDaysAsync());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3651)]
    public async Task SetAsync_OutOfBoundsValue_Throws(int days)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _sut.SetAsync(HistorySettingsService.ActivityRetentionDaysKey, days));
    }

    [Fact]
    public async Task SetAsync_ZeroIsAllowed()
    {
        await _sut.SetAsync(HistorySettingsService.VersionRetentionDaysKey, 0);

        Assert.Equal(0, await _sut.GetVersionRetentionDaysAsync());
    }
}
