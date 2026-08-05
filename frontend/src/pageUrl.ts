// Page URLs never carry the .md extension (see FilesController.ResolveWikiLinkHref on the
// backend) but tree/search/backlink/wiki-link results give back the raw on-disk relative
// path, which does - this is the single place that conversion happens.
export function toPageUrl(relativePath: string): string {
  return `/page/${relativePath.replace(/\.md$/i, "")}`;
}
