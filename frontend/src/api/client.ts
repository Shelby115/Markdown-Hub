import { clearToken, getToken } from "../auth/auth";

export interface TreeNode {
  name: string;
  relativePath: string;
  isFolder: boolean;
  children: TreeNode[] | null;
}

export interface PageContent {
  relativePath: string;
  pageName: string;
  content: string;
  html: string;
  lastModifiedUtc: string;
  sizeBytes: number;
  isPublished: boolean;
  publishSlug: string | null;
  isTemplate: boolean;
}

export interface SearchHit {
  relativePath: string;
  pageName: string;
  snippet: string;
}

export interface TemplateInfo {
  relativePath: string;
  pageName: string;
}

export type AssistantAction = "Ask" | "Summarize" | "ExpandTopic";

export interface AssistantResultCard {
  title: string;
  content: string;
}

/** One ordered piece of an AI Template: literal Markdown, or the id of a slot to fill. */
export interface AiTemplateElement {
  text: string | null;
  slotId: string | null;
}

export interface AiTemplateSlot {
  id: string;
  name: string;
  index: number;
  count: number;
}

export interface AiTemplateParseResult {
  elements: AiTemplateElement[];
  slots: AiTemplateSlot[];
  fillInVariables: string[];
}

export interface AiTemplateSlotValue {
  id: string;
  content: string;
  locked: boolean;
}

export interface AiTemplateGenerateResult {
  content: string;
  warnings: string[];
  /** Set when the content came from a generation pool - what "Forget" refers to. */
  poolEntryId: number | null;
}

export interface GenerationPool {
  id: number;
  name: string;
  instructions: string;
  targetCount: number;
  enabled: boolean;
  readyCount: number;
  /** Short label - "Generating", "Queued", "Full", "Paused", "Waiting", "Off". */
  status: string;
  /** The sentence explaining that label, shown as a tooltip. */
  statusReason: string;
  updatedAtUtc: string;
}

export interface GenerationPoolEntry {
  id: number;
  content: string;
  status: string;
  createdAtUtc: string;
}

export interface GenerationPoolSettings {
  paused: boolean;
  windowStartUtc: string | null;
  windowEndUtc: string | null;
  intervalSeconds: number;
  usedEntryRetentionDays: number;
}

export interface GenerationPoolStatus {
  settings: GenerationPoolSettings;
  runningNow: boolean;
  /** Why the generator is or isn't running right now. */
  reason: string;
  /** The pool currently having an entry written for it, if any. */
  generatingPoolName: string | null;
  nowUtc: string;
}

export type AiTemplateMode = "Generate" | "Improve";

export interface AiSettings {
  selectedModel: string | null;
  configuredDefaultModel: string;
  effectiveModel: string;
}

export interface CurrentUser {
  id: number;
  username: string;
  email: string | null;
  displayName: string | null;
  isAdministrator: boolean;
  defaultFolderPath: string | null;
}

export interface AdminUser {
  id: number;
  username: string;
  email: string | null;
  isAdministrator: boolean;
  isDisabled: boolean;
  createdAt: string;
  lastLoginAt: string | null;
  hasPassword: boolean;
  linkedIdentityCount: number;
}

export interface CreateUserResult {
  id: number;
  username: string;
  isAdministrator: boolean;
  temporaryPassword: string;
}

export interface AuthMethods {
  hasPassword: boolean;
  linkedIdentities: LinkedIdentity[];
}

export interface LinkedIdentity {
  id: number;
  providerId: number;
  providerName: string;
  providerDisplayName: string;
  createdAt: string;
  lastUsedAt: string | null;
}

export interface SessionInfo {
  id: string;
  createdAt: string;
  expiresAt: string;
  lastActivityAt: string;
  userAgent: string | null;
  ipAddress: string | null;
  isCurrent: boolean;
}

// Mirrors the backend PermissionLevel enum, serialized as its numeric value.
export const PERMISSION_LEVEL_LABELS = ["View", "Edit", "Manage"] as const;

export interface AdminFolderPermission {
  id: number;
  appUserId: number;
  username: string;
  folderPath: string;
  level: number;
}

// Mirrors the backend AuthProviderType enum, serialized as its numeric value.
export const PROVIDER_TYPE_LABELS = ["OIDC", "OAuth 2.0"] as const;

export interface ProviderConfiguration {
  authority: string | null;
  requireHttpsMetadata: boolean;
  audience: string | null;
  authorizationEndpoint: string | null;
  tokenEndpoint: string | null;
  userInfoEndpoint: string | null;
  scopes: string;
  userIdField: string;
  emailField: string | null;
  nameField: string | null;
  // Mirrors AutoProvisionPolicy: 0 = Allow, 1 = RequireApproval, 2 = Disabled.
  autoProvision: number;
}

export interface AuthenticationProvider {
  id: number;
  name: string;
  displayName: string;
  type: number;
  clientId: string;
  hasClientSecret: boolean;
  configuration: ProviderConfiguration;
  enabled: boolean;
  createdAt: string;
  updatedAt: string;
  usersUsingProvider: number;
}

export interface ProviderPreset {
  key: string;
  displayName: string;
  type: number;
  configuration: ProviderConfiguration;
}

export interface SaveAuthenticationProviderRequest {
  name?: string; // required on create only - immutable afterward (baked into redirect URIs)
  displayName: string;
  type: number;
  clientId: string;
  clientSecret?: string; // omit/blank on update to leave the stored secret unchanged
  configuration: ProviderConfiguration;
}

export interface HistorySettings {
  versionRetentionDays: number;
  activityRetentionDays: number;
  activityDefaultDays: number;
}

export interface VersionSummary {
  id: number;
  documentId: number;
  createdAtUtc: string;
  updatedAtUtc: string;
  isOpen: boolean;
  versionType: string;
  userId: number | null;
  username: string | null;
  relativePath: string;
}

export interface VersionDetail extends VersionSummary {
  content: string;
}

export interface DocumentHistory {
  documentId: number;
  relativePath: string;
  isDeleted: boolean;
  versions: VersionSummary[];
}

export interface CompareResult {
  from: VersionDetail;
  to: VersionDetail;
}

export interface DeletedDocument {
  documentId: number;
  relativePath: string;
  pageName: string;
  deletedAtUtc: string | null;
  deletedByUserId: number | null;
  deletedByUsername: string | null;
  latestVersionId: number | null;
}

export interface ActivitySummary {
  id: number;
  timestamp: string;
  userId: number | null;
  username: string | null;
  action: string;
  objectType: string | null;
  objectId: number | null;
  targetPath: string | null;
  occurrenceCount: number;
  lastOccurredAtUtc: string | null;
  relatedVersionId: number | null;
  ipAddress: string | null;
}

export interface ActivityDetail extends ActivitySummary {
  details: string | null;
  ipAddress: string | null;
}

export interface ActivityPage {
  items: ActivitySummary[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface ActivityQuery {
  from?: string;
  to?: string;
  userId?: number;
  action?: string;
  objectSearch?: string;
  page?: number;
  pageSize?: number;
}

class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

/// Backend error bodies come in two shapes depending on the endpoint: a bare JSON string
/// (e.g. UsersController's `BadRequest("...")`) or a `{ message: "..." }` object (e.g. the
/// AI/Versions controllers' `StatusCode(..., new { message = ex.Message })`) - handle both.
export function extractErrorMessage(err: unknown, fallback: string): string {
  const message = (err as { message?: string } | undefined)?.message;
  if (!message) return fallback;
  try {
    const parsed: unknown = JSON.parse(message);
    if (typeof parsed === "string") return parsed;
    if (parsed && typeof parsed === "object" && typeof (parsed as { message?: unknown }).message === "string") {
      return (parsed as { message: string }).message;
    }
    return fallback;
  } catch {
    return message;
  }
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const token = getToken();
  const res = await fetch(path, {
    ...init,
    headers: {
      ...(init.body ? { "Content-Type": "application/json" } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...init.headers,
    },
  });

  if (res.status === 401 && token) {
    // The token expired or its session was revoked mid-visit - drop it and reload so the app
    // falls back to the sign-in splash instead of the rest of the page failing silently.
    clearToken();
    window.location.reload();
  }

  if (res.status === 204) return undefined as T;
  if (!res.ok) {
    const body = await res.text();
    throw new ApiError(res.status, body || res.statusText);
  }
  return res.json() as Promise<T>;
}

export const api = {
  getTree: () => request<TreeNode[]>("/api/files/tree"),

  getTemplates: () => request<TemplateInfo[]>("/api/files/templates"),

  getPage: (relativePath: string) =>
    request<PageContent>(`/api/files/${encodeHubPath(relativePath)}`),

  savePage: (relativePath: string, content: string, expectedLastModifiedUtc?: string) =>
    request<PageContent>(`/api/files/${encodeHubPath(relativePath)}`, {
      method: "PUT",
      body: JSON.stringify({ content, expectedLastModifiedUtc }),
    }),

  deletePage: (relativePath: string) =>
    request<void>(`/api/files/${encodeHubPath(relativePath)}`, { method: "DELETE" }),

  renamePage: (relativePath: string, newRelativePath: string) =>
    request<void>(`/api/files/rename/${encodeHubPath(relativePath)}`, {
      method: "POST",
      body: JSON.stringify({ newRelativePath }),
    }),

  createFolder: (relativePath: string) =>
    request<void>(`/api/files/folder/${encodeHubPath(relativePath)}`, { method: "POST" }),

  renameFolder: (relativePath: string, newRelativePath: string) =>
    request<void>(`/api/files/rename-folder/${encodeHubPath(relativePath)}`, {
      method: "POST",
      body: JSON.stringify({ newRelativePath }),
    }),

  deleteFolder: (relativePath: string) =>
    request<void>(`/api/files/folder/${encodeHubPath(relativePath)}`, { method: "DELETE" }),

  search: (q: string) => request<SearchHit[]>(`/api/search?q=${encodeURIComponent(q)}`),

  getBacklinks: (relativePath: string) =>
    request<{ relativePath: string; pageName: string }[]>(`/api/backlinks/${encodeHubPath(relativePath)}`),

  suggestWikiLinks: (prefix: string) =>
    request<{ relativePath: string; pageName: string }[]>(
      `/api/wikilink-suggestions?prefix=${encodeURIComponent(prefix)}`
    ),

  setPublished: (relativePath: string, published: boolean) =>
    request<{ isPublished: boolean; publishSlug: string | null }>(`/api/publish/${encodeHubPath(relativePath)}`, {
      method: "POST",
      body: JSON.stringify({ published }),
    }),

  setTemplate: (relativePath: string, isTemplate: boolean) =>
    request<{ isTemplate: boolean }>(`/api/files/mark-template/${encodeHubPath(relativePath)}`, {
      method: "POST",
      body: JSON.stringify({ isTemplate }),
    }),

  resolveAttachment: (filename: string, from?: string) =>
    request<{ relativePath: string }>(
      `/api/attachments/resolve?filename=${encodeURIComponent(filename)}${from ? `&from=${encodeURIComponent(from)}` : ""}`
    ),

  fetchAttachmentBlob: async (relativePath: string): Promise<Blob> => {
    const token = getToken();
    const res = await fetch(`/api/attachments/${encodeHubPath(relativePath)}`, {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    });
    if (!res.ok) throw new ApiError(res.status, res.statusText);
    return res.blob();
  },

  /// A directly-loadable URL for `<audio>`/`<video>`/`<iframe>` src attributes, which can't
  /// attach an Authorization header the way fetchAttachmentBlob does. Carries the access token
  /// as a query param instead (accepted server-side only for this route, see Program.cs) so the
  /// browser's own media engine fetches the file natively - with HTTP Range support for
  /// seeking - rather than the whole file having to be downloaded into memory as a Blob first
  /// (which is fine for small images but made large audio/video files lag or crash the tab).
  attachmentStreamUrl: (relativePath: string): string => {
    const token = getToken();
    const path = `/api/attachments/${encodeHubPath(relativePath)}`;
    return token ? `${path}?access_token=${encodeURIComponent(token)}` : path;
  },

  getAiAssistantStatus: () => request<{ available: boolean }>("/api/ai/assistant/status"),

  aiAssistant: (action: AssistantAction, question: string | null, contextPaths: string[]) =>
    request<{ results: AssistantResultCard[] }>("/api/ai/assistant", {
      method: "POST",
      body: JSON.stringify({ action, question, contextPaths }),
    }),

  aiTemplateParse: (templatePath: string) =>
    request<AiTemplateParseResult>("/api/ai/template/parse", {
      method: "POST",
      body: JSON.stringify({ templatePath }),
    }),

  aiTemplateGenerate: (templatePath: string, slotId: string, mode: AiTemplateMode, slots: AiTemplateSlotValue[]) =>
    request<AiTemplateGenerateResult>("/api/ai/template/generate", {
      method: "POST",
      body: JSON.stringify({ templatePath, slotId, mode, slots }),
    }),

  aiPoolForgetEntry: (entryId: number) =>
    request<void>(`/api/ai/pool/entries/${entryId}/forget`, { method: "POST" }),

  adminGetPools: () => request<GenerationPool[]>("/api/admin/ai/pools"),

  adminCreatePool: (pool: { name: string; instructions: string; targetCount: number; enabled: boolean }) =>
    request<GenerationPool>("/api/admin/ai/pools", { method: "POST", body: JSON.stringify(pool) }),

  adminUpdatePool: (id: number, pool: { name: string; instructions: string; targetCount: number; enabled: boolean }) =>
    request<GenerationPool>(`/api/admin/ai/pools/${id}`, { method: "PUT", body: JSON.stringify(pool) }),

  adminDeletePool: (id: number) => request<void>(`/api/admin/ai/pools/${id}`, { method: "DELETE" }),

  adminGeneratePoolEntry: (id: number) =>
    request<GenerationPoolEntry>(`/api/admin/ai/pools/${id}/generate`, { method: "POST" }),

  adminGetPoolEntries: (id: number) => request<GenerationPoolEntry[]>(`/api/admin/ai/pools/${id}/entries`),

  adminGetPoolSettings: () => request<GenerationPoolStatus>("/api/admin/ai/pool-settings"),

  adminSetPoolSettings: (settings: GenerationPoolSettings) =>
    request<GenerationPoolStatus>("/api/admin/ai/pool-settings", { method: "PUT", body: JSON.stringify(settings) }),

  adminListAiModels: () => request<{ models: string[] }>("/api/admin/ai/models"),

  adminGetAiSettings: () => request<AiSettings>("/api/admin/ai/settings"),

  adminSetAiModel: (model: string | null) =>
    request<AiSettings>("/api/admin/ai/settings", {
      method: "PUT",
      body: JSON.stringify({ model }),
    }),

  getMe: () => request<CurrentUser>("/api/me"),

  logout: () => request<void>("/api/me/logout", { method: "POST" }),

  setDefaultFolder: (folderPath: string | null) =>
    request<{ defaultFolderPath: string | null }>("/api/me/default-folder", {
      method: "PUT",
      body: JSON.stringify({ folderPath }),
    }),

  changePassword: (currentPassword: string | null, newPassword: string, confirmNewPassword: string) =>
    request<void>("/api/me/change-password", {
      method: "POST",
      body: JSON.stringify({ currentPassword, newPassword, confirmNewPassword }),
    }),

  getAuthMethods: () => request<AuthMethods>("/api/me/authentication-methods"),

  removeAuthMethod: (id: number) => request<void>(`/api/me/authentication-methods/${id}`, { method: "DELETE" }),

  linkProviderStart: (providerName: string) =>
    request<{ redirectUrl: string }>(
      `/api/auth/external/${encodeURIComponent(providerName)}/link-start?returnOrigin=${encodeURIComponent(window.location.origin)}`,
      { method: "POST" }
    ),

  getSessions: () => request<SessionInfo[]>("/api/me/sessions"),

  revokeSession: (id: string) => request<void>(`/api/me/sessions/${id}`, { method: "DELETE" }),

  adminListUsers: () => request<AdminUser[]>("/api/admin/users"),

  adminCreateUser: (username: string, isAdministrator: boolean, temporaryPassword?: string) =>
    request<CreateUserResult>("/api/admin/users", {
      method: "POST",
      body: JSON.stringify({ username, isAdministrator, temporaryPassword: temporaryPassword || undefined }),
    }),

  adminSetUserPassword: (id: number, newPassword: string) =>
    request<void>(`/api/admin/users/${id}/set-password`, {
      method: "POST",
      body: JSON.stringify({ newPassword }),
    }),

  adminSetUserDisabled: (id: number, disabled: boolean) =>
    request<void>(`/api/admin/users/${id}/${disabled ? "disable" : "enable"}`, { method: "POST" }),

  adminSetUserRole: (id: number, isAdministrator: boolean) =>
    request<void>(`/api/admin/users/${id}/${isAdministrator ? "promote" : "demote"}`, { method: "POST" }),

  adminListAuthProviders: () => request<AuthenticationProvider[]>("/api/admin/auth-providers"),

  adminGetProviderPresets: () => request<ProviderPreset[]>("/api/admin/auth-providers/presets"),

  adminCreateAuthProvider: (provider: SaveAuthenticationProviderRequest) =>
    request<AuthenticationProvider>("/api/admin/auth-providers", {
      method: "POST",
      body: JSON.stringify(provider),
    }),

  adminUpdateAuthProvider: (id: number, provider: SaveAuthenticationProviderRequest) =>
    request<AuthenticationProvider>(`/api/admin/auth-providers/${id}`, {
      method: "PUT",
      body: JSON.stringify(provider),
    }),

  adminDeleteAuthProvider: (id: number) =>
    request<void>(`/api/admin/auth-providers/${id}`, { method: "DELETE" }),

  adminEnableAuthProvider: (id: number) =>
    request<AuthenticationProvider>(`/api/admin/auth-providers/${id}/enable`, { method: "POST" }),

  adminDisableAuthProvider: (id: number) =>
    request<AuthenticationProvider>(`/api/admin/auth-providers/${id}/disable`, { method: "POST" }),

  adminListPermissions: () => request<AdminFolderPermission[]>("/api/admin/permissions"),

  adminGrantPermission: (appUserId: number, folderPath: string, level: number) =>
    request<void>("/api/admin/permissions", {
      method: "POST",
      body: JSON.stringify({ appUserId, folderPath, level }),
    }),

  adminRevokePermission: (id: number) =>
    request<void>(`/api/admin/permissions/${id}`, { method: "DELETE" }),

  getVersionHistory: (relativePath: string) =>
    request<DocumentHistory>(`/api/versions/by-path/${encodeHubPath(relativePath)}`),

  getVersion: (versionId: number) => request<VersionDetail>(`/api/versions/${versionId}`),

  compareVersions: (fromId: number, toId: number) =>
    request<CompareResult>(`/api/versions/compare?fromId=${fromId}&toId=${toId}`),

  restoreVersion: (versionId: number) =>
    request<VersionDetail>(`/api/versions/${versionId}/restore`, { method: "POST" }),

  listDeletedDocuments: () => request<DeletedDocument[]>("/api/versions/deleted"),

  adminGetHistorySettings: () => request<HistorySettings>("/api/admin/history-settings"),

  adminSetHistorySettings: (settings: HistorySettings) =>
    request<HistorySettings>("/api/admin/history-settings", {
      method: "PUT",
      body: JSON.stringify(settings),
    }),

  adminQueryActivity: (query: ActivityQuery) => {
    const params = new URLSearchParams();
    if (query.from) params.set("from", query.from);
    if (query.to) params.set("to", query.to);
    if (query.userId !== undefined) params.set("userId", String(query.userId));
    if (query.action) params.set("action", query.action);
    if (query.objectSearch) params.set("objectSearch", query.objectSearch);
    params.set("page", String(query.page ?? 1));
    params.set("pageSize", String(query.pageSize ?? 50));
    return request<ActivityPage>(`/api/admin/activity?${params.toString()}`);
  },

  adminGetActivityDetail: (id: number) => request<ActivityDetail>(`/api/admin/activity/${id}`),

  adminGetActivityActionTypes: () => request<string[]>("/api/admin/activity/action-types"),

  uploadAttachment: async (folder: string, file: File) => {
    const token = getToken();
    const form = new FormData();
    form.append("file", file);
    const res = await fetch(`/api/attachments?folder=${encodeURIComponent(folder)}`, {
      method: "POST",
      headers: token ? { Authorization: `Bearer ${token}` } : {},
      body: form,
    });
    if (!res.ok) throw new ApiError(res.status, await res.text());
    return res.json() as Promise<{ relativePath: string; markdownSyntax: string }>;
  },
};

// Path segments are already validated server-side; we still encode each segment
// individually so folder/file names with special characters round-trip safely.
function encodeHubPath(relativePath: string): string {
  return relativePath.split("/").map(encodeURIComponent).join("/");
}
