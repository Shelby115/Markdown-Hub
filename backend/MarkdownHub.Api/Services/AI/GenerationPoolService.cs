using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;

namespace MarkdownHub.Api.Services;

/// <summary>
/// Named libraries of pre-generated content for AI Template placeholders. A pool turns a slow
/// live model call into a database read: the background generator fills the pool ahead of time
/// (see PoolFillHostedService) and a template that opts in with "- Pool: Name" gets an entry
/// instantly. Falls back to generating live when a pool is empty, so a template never breaks
/// just because the pool hasn't been filled yet.
/// </summary>
public class GenerationPoolService
{
    public const string PausedKey = "Ai.Pools.Paused";
    public const string WindowStartKey = "Ai.Pools.WindowStartUtc";
    public const string WindowEndKey = "Ai.Pools.WindowEndUtc";
    public const string IntervalSecondsKey = "Ai.Pools.IntervalSeconds";
    public const string UsedEntryRetentionDaysKey = "Ai.Pools.UsedEntryRetentionDays";

    public const int MaxTargetCount = 500;
    public const int MinIntervalSeconds = 10;
    public const int MaxIntervalSeconds = 86400;
    public const int MaxRetentionDays = 3650;
    private const int MaxEntryChars = 4000;
    private const int AvoidListSize = 15;

    private static readonly Regex ValidName = new(@"^[A-Za-z0-9 _-]{1,60}$");

    private readonly AppDbContext _db;
    private readonly IAiService _ai;
    private readonly PoolActivityTracker _activity;

    public GenerationPoolService(AppDbContext db, IAiService ai, PoolActivityTracker activity)
    {
        _db = db;
        _ai = ai;
        _activity = activity;
    }

    // --- Settings ---

    public async Task<GenerationPoolSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        var values = await _db.Settings
            .Where(s => s.Key == PausedKey || s.Key == WindowStartKey || s.Key == WindowEndKey
                || s.Key == IntervalSecondsKey || s.Key == UsedEntryRetentionDaysKey)
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        string? Text(string key) => values.GetValueOrDefault(key) is { Length: > 0 } value ? value : null;
        int Number(string key, int fallback) => int.TryParse(Text(key), out var parsed) ? parsed : fallback;

        return new GenerationPoolSettings(
            Text(PausedKey) == "true",
            Text(WindowStartKey),
            Text(WindowEndKey),
            Number(IntervalSecondsKey, 60),
            Number(UsedEntryRetentionDaysKey, 90));
    }

    /// <summary>Throws <see cref="ArgumentException"/> if any value is out of range.</summary>
    public async Task SaveSettingsAsync(GenerationPoolSettings settings, CancellationToken ct = default)
    {
        if (!GenerationPoolSettings.IsValidTime(settings.WindowStartUtc) || !GenerationPoolSettings.IsValidTime(settings.WindowEndUtc))
        {
            throw new ArgumentException("Window times must be in HH:mm 24-hour format, or left blank.");
        }
        if (settings.IntervalSeconds < MinIntervalSeconds || settings.IntervalSeconds > MaxIntervalSeconds)
        {
            throw new ArgumentException($"The generation interval must be between {MinIntervalSeconds} and {MaxIntervalSeconds} seconds.");
        }
        if (settings.UsedEntryRetentionDays < 0 || settings.UsedEntryRetentionDays > MaxRetentionDays)
        {
            throw new ArgumentException($"Used-entry retention must be between 0 and {MaxRetentionDays} days.");
        }

        await SetAsync(PausedKey, settings.Paused ? "true" : "false", ct);
        await SetAsync(WindowStartKey, settings.WindowStartUtc?.Trim(), ct);
        await SetAsync(WindowEndKey, settings.WindowEndUtc?.Trim(), ct);
        await SetAsync(IntervalSecondsKey, settings.IntervalSeconds.ToString(), ct);
        await SetAsync(UsedEntryRetentionDaysKey, settings.UsedEntryRetentionDays.ToString(), ct);
        await _db.SaveChangesAsync(ct);
    }

    private async Task SetAsync(string key, string? value, CancellationToken ct)
    {
        var setting = await _db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null)
        {
            _db.Settings.Add(new AppSetting { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
        }
    }

    // --- Pools ---

    public Task<List<GenerationPool>> ListPoolsAsync(CancellationToken ct = default) =>
        _db.GenerationPools.OrderBy(p => p.Name).ToListAsync(ct);

    public Task<GenerationPool?> FindPoolAsync(int id, CancellationToken ct = default) =>
        _db.GenerationPools.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<GenerationPool?> FindPoolAsync(string name, CancellationToken ct = default) =>
        _db.GenerationPools.FirstOrDefaultAsync(p => p.Name == name, ct);

    public Task<int> CountReadyAsync(int poolId, CancellationToken ct = default) =>
        _db.GenerationPoolEntries.CountAsync(e => e.PoolId == poolId && e.Status == GenerationPoolEntryStatus.Ready, ct);

    /// <summary>Throws <see cref="ArgumentException"/> for an invalid or already-taken name.</summary>
    public async Task<GenerationPool> CreatePoolAsync(string name, string instructions, int targetCount, bool enabled, CancellationToken ct = default)
    {
        var trimmed = (name ?? "").Trim();
        if (!ValidName.IsMatch(trimmed))
        {
            throw new ArgumentException("A pool name may only contain letters, numbers, spaces, hyphens, and underscores.");
        }
        if (await _db.GenerationPools.AnyAsync(p => p.Name == trimmed, ct))
        {
            throw new ArgumentException($"A pool named '{trimmed}' already exists.");
        }

        var pool = new GenerationPool { Name = trimmed };
        Apply(pool, instructions, targetCount, enabled);
        _db.GenerationPools.Add(pool);
        await _db.SaveChangesAsync(ct);
        return pool;
    }

    public async Task UpdatePoolAsync(GenerationPool pool, string instructions, int targetCount, bool enabled, CancellationToken ct = default)
    {
        Apply(pool, instructions, targetCount, enabled);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Removes the pool and every entry it owns. Templates referencing it keep working -
    /// an unknown pool name just falls back to generating live.</summary>
    public async Task DeletePoolAsync(GenerationPool pool, CancellationToken ct = default)
    {
        var entries = await _db.GenerationPoolEntries.Where(e => e.PoolId == pool.Id).ToListAsync(ct);
        _db.GenerationPoolEntries.RemoveRange(entries);
        _db.GenerationPools.Remove(pool);
        await _db.SaveChangesAsync(ct);
    }

    private static void Apply(GenerationPool pool, string instructions, int targetCount, bool enabled)
    {
        if (targetCount < 0 || targetCount > MaxTargetCount)
        {
            throw new ArgumentException($"The target entry count must be between 0 and {MaxTargetCount}.");
        }
        if ((instructions ?? "").Length > AiTemplateParser.MaxInstructionChars)
        {
            throw new ArgumentException($"The prompt is longer than the {AiTemplateParser.MaxInstructionChars}-character limit.");
        }

        pool.Instructions = instructions ?? "";
        pool.TargetCount = targetCount;
        pool.Enabled = enabled;
        pool.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    // --- Entries ---

    public Task<List<GenerationPoolEntry>> ListEntriesAsync(int poolId, string status, CancellationToken ct = default) =>
        _db.GenerationPoolEntries
            .Where(e => e.PoolId == poolId && e.Status == status)
            .OrderByDescending(e => e.Id)
            .ToListAsync(ct);

    /// <summary>Takes a random ready entry and marks it used, or returns null if the pool is empty
    /// or doesn't exist. Single-instance app, so a plain read-then-write is enough here - the worst
    /// a simultaneous second request could do is hand the same entry out twice.</summary>
    public async Task<GenerationPoolEntry?> TakeAsync(string poolName, CancellationToken ct = default)
    {
        var pool = await FindPoolAsync(poolName, ct);
        if (pool is null)
        {
            return null;
        }

        // Picked in memory rather than with ORDER BY RANDOM() so the same code runs against every
        // provider the app and its tests use; a pool holds at most MaxTargetCount rows.
        var ready = await ListEntriesAsync(pool.Id, GenerationPoolEntryStatus.Ready, ct);
        if (ready.Count == 0)
        {
            return null;
        }

        var entry = ready[Random.Shared.Next(ready.Count)];
        entry.Status = GenerationPoolEntryStatus.Used;
        entry.SpentAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return entry;
    }

    /// <summary>Marks an entry forgotten so it's never served again. The row itself stays: its
    /// content hash is what stops the generator from regenerating the same text later.</summary>
    public async Task<bool> ForgetAsync(int entryId, CancellationToken ct = default)
    {
        var entry = await _db.GenerationPoolEntries.FirstOrDefaultAsync(e => e.Id == entryId, ct);
        if (entry is null || entry.Status == GenerationPoolEntryStatus.Forgotten)
        {
            return false;
        }

        entry.Status = GenerationPoolEntryStatus.Forgotten;
        entry.SpentAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Records content that was generated live because a pool was empty, so the generator
    /// won't later produce the same thing again. No-op if the pool doesn't exist.</summary>
    public async Task RecordUsedAsync(string poolName, string content, CancellationToken ct = default)
    {
        var pool = await FindPoolAsync(poolName, ct);
        if (pool is not null)
        {
            await AddEntryAsync(pool, content, GenerationPoolEntryStatus.Used, ct);
        }
    }

    /// <summary>Generates one new entry for the pool and stores it as ready. Returns null when the
    /// model produced nothing usable or a duplicate of something the pool has already seen.</summary>
    public async Task<GenerationPoolEntry?> GenerateEntryAsync(GenerationPool pool, CancellationToken ct = default)
    {
        var instruction = AiTemplateParser.ParseInstruction(pool.Name, pool.Instructions);
        var existing = await _db.GenerationPoolEntries
            .Where(e => e.PoolId == pool.Id && e.Status != GenerationPoolEntryStatus.Forgotten)
            .OrderByDescending(e => e.Id)
            .Take(AvoidListSize)
            .Select(e => e.Content)
            .ToListAsync(ct);

        var prompt = AiTemplatePromptBuilder.BuildForPool(instruction, existing, null);
        var content = AiTemplateValidator.Clean(await _ai.CompleteAsync(AiPrompts.AiTemplateSystemPrompt, prompt, ct));

        var validation = AiTemplateValidator.Check(content, instruction);
        if (!validation.IsValid)
        {
            // One correction retry, same as live slot generation - but unlike there, a still-failing
            // result is simply dropped: nobody is waiting on it, so a bad entry never enters the pool.
            var retryPrompt = AiTemplatePromptBuilder.BuildForPool(instruction, existing, validation.Problems);
            content = AiTemplateValidator.Clean(await _ai.CompleteAsync(AiPrompts.AiTemplateSystemPrompt, retryPrompt, ct));
            if (!AiTemplateValidator.Check(content, instruction).IsValid)
            {
                return null;
            }
        }

        return await AddEntryAsync(pool, content, GenerationPoolEntryStatus.Ready, ct);
    }

    /// <summary>One background pass: adds at most one entry to each enabled pool that is below its
    /// target. One at a time on purpose - this runs against the same single local model someone
    /// might be actively using. Returns how many entries were actually added.</summary>
    public async Task<int> FillOnceAsync(CancellationToken ct = default)
    {
        var added = 0;
        foreach (var pool in await ListPoolsAsync(ct))
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }
            if (!pool.Enabled || await CountReadyAsync(pool.Id, ct) >= pool.TargetCount)
            {
                continue;
            }

            // Marked for the whole model call so the admin page can show which pool is filling -
            // a single entry can take long enough to look like nothing is happening.
            _activity.Start(pool.Name);
            try
            {
                if (await GenerateEntryAsync(pool, ct) is not null)
                {
                    added++;
                }
            }
            finally
            {
                _activity.Finish();
            }
        }
        return added;
    }

    private async Task<GenerationPoolEntry?> AddEntryAsync(GenerationPool pool, string content, string status, CancellationToken ct)
    {
        var trimmed = (content ?? "").Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }
        if (trimmed.Length > MaxEntryChars)
        {
            trimmed = trimmed[..MaxEntryChars];
        }

        var hash = HashOf(trimmed);
        if (await _db.GenerationPoolEntries.AnyAsync(e => e.PoolId == pool.Id && e.ContentHash == hash, ct))
        {
            return null;
        }

        var entry = new GenerationPoolEntry
        {
            PoolId = pool.Id,
            Content = trimmed,
            ContentHash = hash,
            Status = status,
            SpentAtUtc = status == GenerationPoolEntryStatus.Ready ? null : DateTimeOffset.UtcNow,
        };
        _db.GenerationPoolEntries.Add(entry);
        await _db.SaveChangesAsync(ct);
        return entry;
    }

    /// <summary>Drops used entries past their retention window. Forgotten entries are kept forever -
    /// removing one would let the generator produce it again, which is exactly what "forget" rules out.</summary>
    public async Task<int> CleanupUsedEntriesAsync(int retentionDays, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        // Same DateTimeOffset-translation limitation as VersionService.CleanupExpiredVersionsAsync
        // - compare client-side. Pool entries are bounded by target size plus this retention window.
        var used = await _db.GenerationPoolEntries
            .Where(e => e.Status == GenerationPoolEntryStatus.Used)
            .ToListAsync(ct);
        var stale = used.Where(e => (e.SpentAtUtc ?? e.CreatedAtUtc) < cutoff).ToList();
        if (stale.Count == 0)
        {
            return 0;
        }

        _db.GenerationPoolEntries.RemoveRange(stale);
        await _db.SaveChangesAsync(ct);
        return stale.Count;
    }

    private static string HashOf(string content)
    {
        // Normalized so trivial whitespace/casing differences still count as the same entry.
        var normalized = Regex.Replace(content.Trim().ToLowerInvariant(), @"\s+", " ");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}
