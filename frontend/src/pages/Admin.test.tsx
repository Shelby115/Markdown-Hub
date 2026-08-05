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
  adminListOidcProvidersMock,
  adminCreateOidcProviderMock,
} = vi.hoisted(() => ({
  adminListUsersMock: vi.fn(),
  adminListPermissionsMock: vi.fn(),
  adminGetAiSettingsMock: vi.fn(),
  adminListAiModelsMock: vi.fn(),
  adminSetAiModelMock: vi.fn(),
  adminGetHistorySettingsMock: vi.fn(),
  adminListOidcProvidersMock: vi.fn(),
  adminCreateOidcProviderMock: vi.fn(),
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
      adminListOidcProviders: adminListOidcProvidersMock,
      adminCreateOidcProvider: adminCreateOidcProviderMock,
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
    adminListOidcProvidersMock.mockReset().mockResolvedValue([]);
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

describe("Admin OIDC providers", () => {
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
    adminListOidcProvidersMock.mockReset();
    adminCreateOidcProviderMock.mockReset();
  });

  it("lists configured providers", async () => {
    adminListOidcProvidersMock.mockResolvedValue([
      {
        id: 1,
        name: "Keycloak",
        authority: "https://auth.example.com/realms/example-realm",
        clientId: "example-client-spa",
        audience: "example-client-api",
        requireHttpsMetadata: true,
        isEnabled: true,
        createdAt: new Date().toISOString(),
      },
    ]);

    renderAdmin();

    expect(await screen.findByText("Keycloak")).toBeInTheDocument();
    expect(screen.getByText("https://auth.example.com/realms/example-realm")).toBeInTheDocument();
  });

  it("adding a provider calls the API and appends it to the list", async () => {
    adminListOidcProvidersMock.mockResolvedValue([]);
    adminCreateOidcProviderMock.mockResolvedValue({
      id: 2,
      name: "Authentik",
      authority: "https://authentik.example.com",
      clientId: "example-client",
      audience: "example-client",
      requireHttpsMetadata: true,
      isEnabled: true,
      createdAt: new Date().toISOString(),
    });

    const user = userEvent.setup();
    renderAdmin();
    await screen.findByText(/At least one/);

    await user.type(screen.getByPlaceholderText("Name (e.g. Keycloak)"), "Authentik");
    await user.type(screen.getByPlaceholderText("Authority (issuer URL)"), "https://authentik.example.com");
    await user.type(screen.getByPlaceholderText("Client ID"), "example-client");
    await user.type(screen.getByPlaceholderText("Audience"), "example-client");
    await user.click(screen.getByRole("button", { name: "Add provider" }));

    await waitFor(() =>
      expect(adminCreateOidcProviderMock).toHaveBeenCalledWith({
        name: "Authentik",
        authority: "https://authentik.example.com",
        clientId: "example-client",
        audience: "example-client",
        requireHttpsMetadata: true,
      })
    );
    expect(await screen.findByText("Authentik")).toBeInTheDocument();
  });
});
