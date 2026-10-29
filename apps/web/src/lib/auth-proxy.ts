import { NextRequest, NextResponse } from "next/server";

const ACCESS = "icehott_access";
const REFRESH = "icehott_refresh";
type Tokens = { accessToken: string; refreshToken: string; expiresAtUtc: string };

export function backendUrl(path: string): string {
  const base = process.env.ICEHOTT_API_BASE_URL ??
    (process.env.NODE_ENV === "production" ? undefined : "http://127.0.0.1:5035");
  if (!base) throw new Error("ICEHOTT_API_BASE_URL is required in production");
  const url = new URL(base);
  if (!["http:", "https:"].includes(url.protocol) || url.username || url.password ||
    (process.env.NODE_ENV === "production" && url.protocol !== "https:"))
    throw new Error("Invalid backend API URL");
  if (!path.startsWith("/api/") || path.includes("..") || path.includes("?"))
    throw new Error("Invalid API path");
  return new URL(path, url).toString();
}

export function allowedOrigin(request: NextRequest): boolean {
  const origin = request.headers.get("origin");
  if (origin && origin !== request.nextUrl.origin) return false;
  const site = request.headers.get("sec-fetch-site");
  return !site || site === "same-origin" || site === "none";
}

export function forbidden() {
  return NextResponse.json({ error: "Request rejected" }, { status: 403 });
}

export function setSession(response: NextResponse, tokens: Tokens): NextResponse {
  const common = {
    httpOnly: true as const, secure: process.env.NODE_ENV === "production",
    sameSite: "strict" as const, path: "/",
  };
  response.cookies.set(ACCESS, tokens.accessToken, { ...common, maxAge: 10 * 60 });
  response.cookies.set(REFRESH, tokens.refreshToken, { ...common, maxAge: 14 * 24 * 3600 });
  response.headers.set("cache-control", "no-store");
  return response;
}

export function clearSession(response: NextResponse): NextResponse {
  response.cookies.set(ACCESS, "", { path: "/", maxAge: 0 });
  response.cookies.set(REFRESH, "", { path: "/", maxAge: 0 });
  response.headers.set("cache-control", "no-store");
  return response;
}

export async function backendPost(path: string, input: unknown): Promise<Response> {
  return fetch(backendUrl(path), {
    method: "POST", cache: "no-store",
    headers: { "content-type": "application/json" }, body: JSON.stringify(input),
  });
}

export async function relay(response: Response): Promise<NextResponse> {
  const body = await response.text();
  return new NextResponse(body || null, {
    status: response.status,
    headers: {
      "content-type": response.headers.get("content-type") || "application/json",
      "cache-control": "no-store",
    },
  });
}

export async function postPublic(request: NextRequest, path: string) {
  if (!allowedOrigin(request)) return forbidden();
  const size = Number(request.headers.get("content-length") || 0);
  if (size > 8192) return NextResponse.json({ error: "Payload too large" }, { status: 413 });
  let body: unknown;
  try {
    const raw = await request.text();
    if (raw.length > 8192) return NextResponse.json({ error: "Payload too large" }, { status: 413 });
    body = JSON.parse(raw);
  } catch {
    return NextResponse.json({ error: "Invalid request" }, { status: 400 });
  }
  try {
    return await relay(await backendPost(path, body));
  } catch {
    return NextResponse.json({ error: "Backend unavailable" }, { status: 503 });
  }
}

export async function callAuthorized(
  request: NextRequest,
  path: string,
  method: "GET" | "POST" | "PUT" | "DELETE" = "GET",
  body?: string
): Promise<NextResponse> {
  if (method !== "GET" && !allowedOrigin(request)) return forbidden();
  let access = request.cookies.get(ACCESS)?.value;
  const refresh = request.cookies.get(REFRESH)?.value;
  let rotated: Tokens | null = null;
  const attempt = (token: string | undefined) => fetch(backendUrl(path), {
    method, cache: "no-store", headers: {
      ...(token ? { authorization: `Bearer ${token}` } : {}),
      ...(body ? { "content-type": "application/json" } : {}),
    }, ...(body ? { body } : {}),
  });
  try {
    let result = access ? await attempt(access) : new Response(null, { status: 401 });
    if (result.status === 401 && refresh) {
      const renewal = await backendPost("/api/auth/refresh", { refreshToken: refresh });
      if (renewal.ok) {
        rotated = (await renewal.json()) as Tokens;
        access = rotated.accessToken;
        result = await attempt(access);
      } else {
        return clearSession(NextResponse.json({ error: "Session expired" }, { status: 401 }));
      }
    }
    const outgoing = await relay(result);
    return rotated ? setSession(outgoing, rotated) : outgoing;
  } catch {
    return NextResponse.json({ error: "Backend unavailable" }, { status: 503 });
  }
}

export async function proxyWorkspace(
  request: NextRequest, path: string, method: "GET" | "POST" | "PUT" | "DELETE"
) {
  if (method !== "GET" && !allowedOrigin(request)) return forbidden();
  if (method === "GET" || method === "DELETE") return callAuthorized(request, path, method);
  const size = Number(request.headers.get("content-length") || 0);
  if (size > 8192) return NextResponse.json({ error: "Payload too large" }, { status: 413 });
  let body: string;
  try {
    body = await request.text();
    if (body.length > 8192) return NextResponse.json({ error: "Payload too large" }, { status: 413 });
    if (body.length > 0) JSON.parse(body);
  } catch {
    return NextResponse.json({ error: "Invalid JSON" }, { status: 400 });
  }
  return callAuthorized(request, path, method, body);
}
