// @vitest-environment node
import { afterEach, describe, expect, it, vi } from "vitest";
import { NextRequest, NextResponse } from "next/server";
import { allowedOrigin, backendUrl, clearSession, setSession } from "../lib/auth-proxy";

afterEach(() => vi.unstubAllEnvs());

describe("server-only browser session boundary", () => {
  it("blocks cross-site POST origin and fetch metadata", () => {
    const crossSite = new NextRequest("http://localhost:3000/api/session/login", {
      method: "POST",
      headers: { origin: "https://other.example", "sec-fetch-site": "cross-site" }
    });
    expect(allowedOrigin(crossSite)).toBe(false);
    const sameOrigin = new NextRequest("http://localhost:3000/api/session/login", {
      method: "POST",
      headers: { origin: "http://localhost:3000", "sec-fetch-site": "same-origin" }
    });
    expect(allowedOrigin(sameOrigin)).toBe(true);
  });

  it("never permits arbitrary destinations or plaintext production API", () => {
    vi.stubEnv("ICEHOTT_API_BASE_URL", "https://api.example.test");
    expect(backendUrl("/api/auth/login")).toBe("https://api.example.test/api/auth/login");
    expect(() => backendUrl("/api/../admin")).toThrow();
    expect(() => backendUrl("https://evil.example/api/auth/login")).toThrow();
    vi.stubEnv("NODE_ENV", "production");
    vi.stubEnv("ICEHOTT_API_BASE_URL", "http://api.example.test");
    expect(() => backendUrl("/api/auth/login")).toThrow();
  });

  it("stores access and refresh in HttpOnly Strict Secure cookies and clears both", () => {
    vi.stubEnv("NODE_ENV", "production");
    const response = setSession(NextResponse.json({ authenticated: true }), {
      accessToken: "test-access", refreshToken: "test-refresh", expiresAtUtc: ""
    });
    expect(response.cookies.get("icehott_access")?.value).toBe("test-access");
    expect(response.cookies.get("icehott_refresh")?.value).toBe("test-refresh");
    const cookie = response.headers.get("set-cookie") || "";
    expect(cookie).toContain("HttpOnly");
    expect(cookie).toContain("Secure");
    expect(cookie).toContain("SameSite=strict");
    expect(response.headers.get("cache-control")).toBe("no-store");
    const clear = clearSession(NextResponse.json({ done: true }));
    expect(clear.cookies.get("icehott_access")?.value).toBe("");
    expect(clear.cookies.get("icehott_refresh")?.value).toBe("");
  });
});
