import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { Admin } from "./Admin";

const {
  adminListUsersMock,
  adminListPermissionsMock,
  adminGetAiSettingsMock,
  adminListAiModelsMock,
  adminSetAiModelMock,
  adminGetHistorySettingsMock,
  adminListAuthProvidersMock,
  adminGetProviderPresetsMock,
  adminCreateAuthProviderMock,
} = vi.hoisted(() => ({
  adminListUsersMock: vi.fn(),
  adminListPermissionsMock: vi.fn(),
  adminGetAiSettingsMock: vi.fn(),
  adminListAiModelsMock: vi.fn(),
  adminSetAiModelMock: vi.fn(),
  adminGetHistorySettingsMock: vi.fn(),
  adminListAuthProvidersMock: vi.fn(),
  adminGetProviderPresetsMock: vi.fn(),
  adminCreateAuthProviderMock: vi.fn(),
}));

vi.mock("../api/client", async () => {
  const actual = await vi.importActual<typeof import("../api/client")>("../api/client");
  return {
    ...actual,
    api: {
      adminListUsers: adminListUsersMock,
      adminListPermissions: adminListPermissionsMock,
      adminGetAiSettings: adminGetAiSettingsMock,
      adminListAiModels: adminListAiModelsMock,
      adminSetAiModel: adminSetAiModelMock,
      adminGetHistorySettings: adminGetHistorySettingsMock,
      adminListAuthProviders: adminListAuthProvidersMock,
      adminGetProviderPresets: adminGetProviderPresetsMock,
      adminCreateAuthProvider: adminCreateAuthProviderMock,
    },
  };
});

function renderAdmin() {
  return render(
    <MemoryRouter>
      <Admin />
    </MemoryRouter>
  );
}

describe("Admin AI model settings", () => {
  beforeEach(() => {
    adminListUsersMock.mockReset().mockResolvedValue([]);
    adminListPermissionsMock.mockReset().mockResolvedValue([]);
    adminGetAiSettingsMock.mockReset();
    adminListAiModelsMock.mockReset();
    adminSetAiModelMock.mockReset();
    adminGetHistorySettingsMock.mockReset().mockResolvedValue({
      versionRetentionDays: 3,
      activityRetentionDays: 30,
      activityDefaultDays: 14,
    });
    adminListAuthProvidersMock.mockReset().mockResolvedValue([]);
    adminGetProviderPresetsMock.mockReset().mockResolvedValue([]);
  });

  it("shows the configured default when no override is set, and disables Reset", async () => {
    adminGetAiSettingsMock.mockResolvedValue({
      selectedModel: null,
      configuredDefaultModel: "gpt-oss:20b",
      effectiveModel: "gpt-oss:20b",
    });
    adminListAiModelsMock.mockResolvedValue({ models: ["gpt-oss:20b", "llama3.1:8b"] });

    renderAdmin();

    expect(await screen.findByText("gpt-oss:20b", { selector: "strong" })).toBeInTheDocument();
    expect(screen.getByText(/configured default/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reset to default" })).toBeDisabled();
  });

  it("saving a typed model name calls the API and updates the displayed effective model", async () => {
    adminGetAiSettingsMock.mockResolvedValue({
      selectedModel: null,
      configuredDefaultModel: "gpt-oss:20b",
      effectiveModel: "gpt-oss:20b",
    });
    adminListAiModelsMock.mockResolvedValue({ models: ["gpt-oss:20b", "llama3.1:8b"] });
    adminSetAiModelMock.mockResolvedValue({
      selectedModel: "llama3.1:8b",
      configuredDefaultModel: "gpt-oss:20b",
      effectiveModel: "llama3.1:8b",
    });

    const user = userEvent.setup();
    renderAdmin();
    await screen.findByText("gpt-oss:20b", { selector: "strong" });

    const input = screen.getByPlaceholderText("gpt-oss:20b");
    await user.clear(input);
    await user.type(input, "llama3.1:8b");
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(adminSetAiModelMock).toHaveBeenCalledWith("llama3.1:8b"));
    expect(await screen.findByText("llama3.1:8b", { selector: "strong" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reset to default" })).toBeEnabled();
  });

  it("Reset to default clears the override", async () => {
    adminGetAiSettingsMock.mockResolvedValue({
      selectedModel: "llama3.1:8b",
      configuredDefaultModel: "gpt-oss:20b",
      effectiveModel: "llama3.1:8b",
    });
    adminListAiModelsMock.mockResolvedValue({ models: ["gpt-oss:20b", "llama3.1:8b"] });
    adminSetAiModelMock.mockResolvedValue({
      selectedModel: null,
      configuredDefaultModel: "gpt-oss:20b",
      effectiveModel: "gpt-oss:20b",
    });

    const user = userEvent.setup();
    renderAdmin();
    await screen.findByText("llama3.1:8b", { selector: "strong" });

    await user.click(screen.getByRole("button", { name: "Reset to default" }));

    await waitFor(() => expect(adminSetAiModelMock).toHaveBeenCalledWith(null));
    expect(await screen.findByText("gpt-oss:20b", { selector: "strong" })).toBeInTheDocument();
  });

  it("shows a warning but still lets you type a model manually when listing models fails", async () => {
    adminGetAiSettingsMock.mockResolvedValue({
      selectedModel: null,
      configuredDefaultModel: "gpt-oss:20b",
      effectiveModel: "gpt-oss:20b",
    });
    adminListAiModelsMock.mockRejectedValue(new Error(JSON.stringify({ message: "Couldn't reach the AI service." })));

    renderAdmin();

    expect(await screen.findByText(/Couldn't reach the AI service\./)).toBeInTheDocument();
    expect(screen.getByPlaceholderText("gpt-oss:20b")).toBeEnabled();
  });
});

const EMPTY_CONFIG = {
  authority: null,
  requireHttpsMetadata: true,
  audience: null,
  authorizationEndpoint: null,
  tokenEndpoint: null,
  userInfoEndpoint: null,
  scopes: "openid profile email",
  userIdField: "sub",
  emailField: "email",
  nameField: "name",
  autoProvision: 0,
};

describe("Admin authentication providers", () => {
  beforeEach(() => {
    adminListUsersMock.mockReset().mockResolvedValue([]);
    adminListPermissionsMock.mockReset().mockResolvedValue([]);
    adminGetAiSettingsMock.mockReset().mockResolvedValue({
      selectedModel: null,
      configuredDefaultModel: "gpt-oss:20b",
      effectiveModel: "gpt-oss:20b",
    });
    adminListAiModelsMock.mockReset().mockResolvedValue({ models: [] });
    adminGetHistorySettingsMock.mockReset().mockResolvedValue({
      versionRetentionDays: 3,
      activityRetentionDays: 30,
      activityDefaultDays: 14,
    });
    adminListAuthProvidersMock.mockReset();
    adminGetProviderPresetsMock.mockReset().mockResolvedValue([]);
    adminCreateAuthProviderMock.mockReset();
  });

  it("lists configured providers, including whether a client secret is set", async () => {
    adminListAuthProvidersMock.mockResolvedValue([
      {
        id: 1,
        name: "keycloak",
        displayName: "Keycloak",
        type: 0,
        clientId: "example-client",
        hasClientSecret: true,
        configuration: { ...EMPTY_CONFIG, authority: "https://auth.example.com/realms/example-realm" },
        enabled: true,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        usersUsingProvider: 3,
      },
    ]);

    renderAdmin();

    expect(await screen.findByText("Keycloak")).toBeInTheDocument();
    expect(screen.getAllByText("OIDC").length).toBeGreaterThan(0);
    expect(screen.getByText("3")).toBeInTheDocument();
  });

  it("adding a provider calls the API and appends it to the list", async () => {
    adminListAuthProvidersMock.mockResolvedValue([]);
    adminCreateAuthProviderMock.mockResolvedValue({
      id: 2,
      name: "authentik",
      displayName: "Authentik",
      type: 0,
      clientId: "example-client",
      hasClientSecret: true,
      configuration: { ...EMPTY_CONFIG, authority: "https://authentik.example.com" },
      enabled: true,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      usersUsingProvider: 0,
    });

    const user = userEvent.setup();
    renderAdmin();
    await screen.findByText(/Optional external sign-in methods/);

    await user.type(screen.getByPlaceholderText("Internal name (e.g. keycloak)"), "authentik");
    await user.type(screen.getByPlaceholderText("Display name (e.g. Keycloak)"), "Authentik");
    await user.type(screen.getByPlaceholderText("Client ID"), "example-client");
    await user.type(screen.getByPlaceholderText("Client secret"), "s3cr3t");
    await user.type(screen.getByPlaceholderText("Authority (issuer URL)"), "https://authentik.example.com");
    await user.click(screen.getByRole("button", { name: "Add provider" }));

    await waitFor(() => expect(adminCreateAuthProviderMock).toHaveBeenCalled());
    const requestArg = adminCreateAuthProviderMock.mock.calls[0][0];
    expect(requestArg.name).toBe("authentik");
    expect(requestArg.displayName).toBe("Authentik");
    expect(requestArg.clientId).toBe("example-client");
    expect(requestArg.configuration.authority).toBe("https://authentik.example.com");
    expect(await screen.findByText("Authentik")).toBeInTheDocument();
  });

  it("shows a preset button and applies it to the form", async () => {
    adminListAuthProvidersMock.mockResolvedValue([]);
    adminGetProviderPresetsMock.mockResolvedValue([
      {
        key: "google",
        displayName: "Google",
        type: 0,
        configuration: { ...EMPTY_CONFIG, authority: "https://accounts.google.com" },
      },
    ]);

    const user = userEvent.setup();
    renderAdmin();
    await screen.findByText(/Optional external sign-in methods/);

    await user.click(await screen.findByRole("button", { name: "Google" }));

    expect(screen.getByPlaceholderText("Display name (e.g. Keycloak)")).toHaveValue("Google");
    expect(screen.getByPlaceholderText("Authority (issuer URL)")).toHaveValue("https://accounts.google.com");
  });
});
