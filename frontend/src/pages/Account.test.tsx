import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { Account } from "./Account";

const {
  getMeMock,
  getAuthMethodsMock,
  getSessionsMock,
  changePasswordMock,
  removeAuthMethodMock,
  revokeSessionMock,
  linkProviderStartMock,
  getProvidersMock,
} = vi.hoisted(() => ({
  getMeMock: vi.fn(),
  getAuthMethodsMock: vi.fn(),
  getSessionsMock: vi.fn(),
  changePasswordMock: vi.fn(),
  removeAuthMethodMock: vi.fn(),
  revokeSessionMock: vi.fn(),
  linkProviderStartMock: vi.fn(),
  getProvidersMock: vi.fn(),
}));

vi.mock("../api/client", async () => {
  const actual = await vi.importActual<typeof import("../api/client")>("../api/client");
  return {
    ...actual,
    api: {
      getMe: getMeMock,
      getAuthMethods: getAuthMethodsMock,
      getSessions: getSessionsMock,
      changePassword: changePasswordMock,
      removeAuthMethod: removeAuthMethodMock,
      revokeSession: revokeSessionMock,
      linkProviderStart: linkProviderStartMock,
    },
  };
});

vi.mock("../auth/auth", async () => {
  const actual = await vi.importActual<typeof import("../auth/auth")>("../auth/auth");
  return { ...actual, getProviders: getProvidersMock };
});

function renderAccount() {
  return render(
    <MemoryRouter>
      <Account />
    </MemoryRouter>
  );
}

describe("Account", () => {
  beforeEach(() => {
    getMeMock.mockReset().mockResolvedValue({
      id: 1,
      username: "alice",
      email: "alice@example.com",
      displayName: null,
      isAdministrator: false,
      defaultFolderPath: null,
    });
    getAuthMethodsMock.mockReset().mockResolvedValue({ hasPassword: true, linkedIdentities: [] });
    getSessionsMock.mockReset().mockResolvedValue([]);
    getProvidersMock.mockReset().mockResolvedValue([]);
    changePasswordMock.mockReset();
    removeAuthMethodMock.mockReset();
    revokeSessionMock.mockReset();
    linkProviderStartMock.mockReset();
  });

  it("shows the signed-in username", async () => {
    renderAccount();

    expect(await screen.findByText("alice")).toBeInTheDocument();
  });

  it("requires the current password when one is already set", async () => {
    renderAccount();

    expect(await screen.findByLabelText(/Current password/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Change password" })).toBeInTheDocument();
  });

  it("does not require a current password for an account with none yet", async () => {
    getAuthMethodsMock.mockResolvedValue({ hasPassword: false, linkedIdentities: [] });

    renderAccount();
    await screen.findByText("alice");

    expect(screen.queryByLabelText(/Current password/i)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Set password" })).toBeInTheDocument();
  });

  it("disables removing the only remaining authentication method", async () => {
    getAuthMethodsMock.mockResolvedValue({
      hasPassword: false,
      linkedIdentities: [
        { id: 1, providerId: 1, providerName: "keycloak", providerDisplayName: "Keycloak", createdAt: new Date().toISOString(), lastUsedAt: null },
      ],
    });

    renderAccount();

    const removeButton = await screen.findByRole("button", { name: "Remove" });
    expect(removeButton).toBeDisabled();
  });

  it("submitting a password change calls the API and shows a confirmation", async () => {
    changePasswordMock.mockResolvedValue(undefined);
    const user = userEvent.setup();
    renderAccount();
    await screen.findByLabelText(/Current password/i);

    await user.type(screen.getByLabelText(/Current password/i), "old-password");
    await user.type(screen.getByLabelText(/^New password$/i), "new-password-123");
    await user.type(screen.getByLabelText(/Confirm new password/i), "new-password-123");
    await user.click(screen.getByRole("button", { name: "Change password" }));

    await waitFor(() =>
      expect(changePasswordMock).toHaveBeenCalledWith("old-password", "new-password-123", "new-password-123")
    );
    expect(await screen.findByText(/Password updated/)).toBeInTheDocument();
  });

  it("lists an unlinked provider with a Connect button", async () => {
    getProvidersMock.mockResolvedValue([{ id: 5, name: "google", displayName: "Google", type: 0 }]);

    renderAccount();

    expect(await screen.findByText("Google")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Connect" })).toBeInTheDocument();
  });
});
