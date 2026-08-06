namespace MarkdownHub.Api.Controllers.AI;

public record AiSettingsResponse(string? SelectedModel, string ConfiguredDefaultModel, string EffectiveModel);
public record SetAiModelRequest(string? Model);
