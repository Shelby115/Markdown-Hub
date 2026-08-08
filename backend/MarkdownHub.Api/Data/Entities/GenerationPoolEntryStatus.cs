namespace MarkdownHub.Api.Data.Entities;

/// <summary>
/// Ready entries are available to hand out; Used ones already were; Forgotten ones were rejected
/// by a user. Only Ready is ever served, and Forgotten rows are kept indefinitely so a rejected
/// entry can never be regenerated (see GenerationPoolEntry.ContentHash).
/// </summary>
public static class GenerationPoolEntryStatus
{
    public const string Ready = "Ready";
    public const string Used = "Used";
    public const string Forgotten = "Forgotten";
}
