namespace MarkdownHub.Api.Controllers.AI;

/// <summary>Status is a short label ("Full", "Waiting", "Off"); StatusReason is the sentence
/// explaining it, meant to be shown as a tooltip - see PoolFillStatus.</summary>
public record GenerationPoolDto(
    int Id, string Name, string Instructions, int TargetCount, bool Enabled, int ReadyCount,
    string Status, string StatusReason, string UpdatedAtUtc);

public record GenerationPoolEntryDto(int Id, string Content, string Status, string CreatedAtUtc);

public record SaveGenerationPoolRequest(string Name, string Instructions, int TargetCount, bool Enabled);

public record GenerationPoolSettingsDto(bool Paused, string? WindowStartUtc, string? WindowEndUtc, int IntervalSeconds, int UsedEntryRetentionDays);

/// <summary>Adds what only the server can answer: whether the window currently allows generation
/// and why, which pool (if any) is being written right now, how long until the generator's next
/// pass, and what time the server thinks it is - so an admin setting a UTC window isn't guessing.
/// SecondsUntilNextCheck is null until the generator has scheduled its first pass.</summary>
public record GenerationPoolStatusDto(
    GenerationPoolSettingsDto Settings, bool RunningNow, string Reason, string? GeneratingPoolName,
    int? SecondsUntilNextCheck, string NowUtc);
