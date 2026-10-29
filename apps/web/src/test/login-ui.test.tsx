import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import LoginPage from "../app/login/page";
import RegisterPage from "../app/register/page";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() })
}));
afterEach(cleanup);

describe("Phase 1 authentication UI", () => {
  it("shows a labelled password form and recovery navigation", () => {
    render(<LoginPage />);
    expect(screen.getByRole("heading", { name: "Welcome back" })).toBeTruthy();
    expect(screen.getByLabelText("Email")).toBeTruthy();
    const password = screen.getByLabelText("Password") as HTMLInputElement;
    expect(password.type).toBe("password");
    expect(screen.getByRole("link", { name: "Forgot password?" }).getAttribute("href"))
      .toBe("/forgot-password");
  });

  it("describes restricted registration and password requirements", () => {
    render(<RegisterPage />);
    expect(screen.getByText(/Registration is restricted/)).toBeTruthy();
    const field = screen.getByLabelText(/Password/) as HTMLInputElement;
    expect(field.minLength).toBe(12);
    expect(field.type).toBe("password");
  });
});
