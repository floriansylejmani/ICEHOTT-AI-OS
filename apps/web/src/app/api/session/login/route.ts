import { NextRequest, NextResponse } from "next/server";
import { allowedOrigin, backendPost, forbidden, relay, setSession } from "@/lib/auth-proxy";

export async function POST(request: NextRequest) {
  if (!allowedOrigin(request)) return forbidden();
  if (Number(request.headers.get("content-length") || 0) > 8192)
    return NextResponse.json({ error: "Payload too large" }, { status: 413 });
  let body: unknown;
  try {
    const raw = await request.text();
    if (raw.length > 8192) return NextResponse.json({ error: "Payload too large" }, { status: 413 });
    body = JSON.parse(raw);
  } catch {
    return NextResponse.json({ error: "Invalid JSON" }, { status: 400 });
  }
  try {
    const result = await backendPost("/api/auth/login", body);
    if (!result.ok) return relay(result);
    const tokens = await result.json();
    return setSession(NextResponse.json({ authenticated: true }), tokens);
  } catch {
    return NextResponse.json({ error: "Login unavailable" }, { status: 503 });
  }
}
