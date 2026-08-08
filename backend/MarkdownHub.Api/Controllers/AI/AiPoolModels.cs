namespace MarkdownHub.Api.Controllers.AI;

public record GenerationPoolDto(int Id, string Name, string Instructions, int TargetCount, bool Enabled, int ReadyCount, string UpdatedAtUtc);

public record GenerationPoolEntryDto(int Id, string Content, string Status, string CreatedAtUtc);

public record SaveGenerationPoolRequest(string Name, string Instructions, int TargetCount, bool Enabled);

public record GenerationPoolSettingsDto(bool Paused, string? WindowStartUtc, string? WindowEndUtc, int IntervalSeconds, int UsedEntryRetentionDays);

/// <summary>Adds the two things only the server can answer: whether the window currently allows
/// generation, and what time the server thinks it is - so an admin setting a UTC window isn't
/// guessing.</summary>
public record GenerationPoolStatusDto(GenerationPoolSettingsDto Settings, bool RunningNow, string NowUtc);
