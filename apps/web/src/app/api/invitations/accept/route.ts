import { NextRequest } from "next/server";
import { proxyWorkspace } from "@/lib/auth-proxy";
export async function POST(request: NextRequest) {
  return proxyWorkspace(request, "/api/invitations/accept", "POST");
}
