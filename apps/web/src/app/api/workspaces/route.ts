import { NextRequest } from "next/server";
import { proxyWorkspace } from "@/lib/auth-proxy";
export async function GET(request: NextRequest) {
  return proxyWorkspace(request, "/api/workspaces/", "GET");
}
export async function POST(request: NextRequest) {
  return proxyWorkspace(request, "/api/workspaces/", "POST");
}
