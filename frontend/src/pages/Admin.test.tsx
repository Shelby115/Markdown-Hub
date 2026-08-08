import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { Admin } from "./Admin";

const {
  adminListUsersMock,
  adminListPermissionsMock,
  adminGetHistorySettingsMock,
  adminListAuthProvidersMock,
  adminGetProviderPresetsMock,
  adminCreateAuthProviderMock,
} = vi.hoisted(() => ({
  adminListUsersMock: vi.fn(),
  adminListPermissionsMock: vi.fn(),
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
