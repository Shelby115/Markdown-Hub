import { render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it } from "vitest";
import { AuthCallback } from "./AuthCallback";

describe("AuthCallback", () => {
  beforeEach(() => {
    localStorage.clear();
    window.location.hash = "";
  });

  it("stores the token from the URL fragment and navigates home", async () => {
    window.location.hash = "#token=abc.def.ghi";

    render(
      <MemoryRouter initialEntries={["/auth/callback"]}>
        <Routes>
          <Route path="/auth/callback" element={<AuthCallback />} />
          <Route path="/" element={<div>Home</div>} />
        </Routes>
      </MemoryRouter>
    );

    await waitFor(() => expect(screen.getByText("Home")).toBeInTheDocument());
    expect(localStorage.getItem("authToken")).toBe("abc.def.ghi");
  });

  it("shows the error message carried in the fragment on failure", async () => {
    window.location.hash = "#error=" + encodeURIComponent("Invalid username or password.");

    render(
      <MemoryRouter initialEntries={["/auth/callback"]}>
        <AuthCallback />
      </MemoryRouter>
    );

    expect(await screen.findByText("Sign-in failed")).toBeInTheDocument();
    expect(await screen.findByText("Invalid username or password.")).toBeInTheDocument();
    expect(localStorage.getItem("authToken")).toBeNull();
  });
});
