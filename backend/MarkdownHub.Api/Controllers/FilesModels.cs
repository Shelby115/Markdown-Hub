namespace MarkdownHub.Api.Controllers;

public record FileTreeNode(string Name, string RelativePath, bool IsFolder, List<FileTreeNode>? Children);
public record SavePageRequest(string Content, DateTimeOffset? ExpectedLastModifiedUtc);
public record RenameRequest(string NewRelativePath);
public record TemplateInfo(string RelativePath, string PageName);
public record MarkTemplateRequest(bool IsTemplate);
