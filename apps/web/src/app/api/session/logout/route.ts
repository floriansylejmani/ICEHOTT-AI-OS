import { NextRequest, NextResponse } from "next/server";
import { allowedOrigin, callAuthorized, clearSession, forbidden } from "@/lib/auth-proxy";

export async function POST(request: NextRequest) {
  if (!allowedOrigin(request)) return forbidden();
  const result = await callAuthorized(request, "/api/auth/logout", "POST");
  // Revocation occurs in the API. Even if it is unreachable, clear browser cookies.
  if (result.status !== 204 && result.status !== 401)
    return NextResponse.json({ error: "Logout could not be confirmed" }, { status: 503 });
  return clearSession(new NextResponse(null, { status: 204 }));
}
