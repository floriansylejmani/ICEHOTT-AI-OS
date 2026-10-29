import { NextRequest, NextResponse } from "next/server";
import { proxyWorkspace } from "@/lib/auth-proxy";

type Params = { params: Promise<{ workspaceId: string; path: string[] }> };
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

async function route(request: NextRequest, { params }: Params,
  method: "GET" | "POST" | "PUT" | "DELETE") {
  const { workspaceId, path } = await params;
  if (!guid.test(workspaceId) || !Array.isArray(path)) return new NextResponse(null, { status: 404 });
  const valid =
    (path.length === 1 && path[0] === "overview" && method === "GET") ||
    (path.length === 1 && path[0] === "settings" && method === "PUT") ||
    (path.length === 1 && path[0] === "projects" && (method === "GET" || method === "POST")) ||
    (path.length === 2 && path[0] === "projects" && guid.test(path[1]) &&
      (method === "PUT" || method === "DELETE")) ||
    (path.length === 1 && (path[0] === "members" || path[0] === "invitations") &&
      (method === "GET" || (path[0] === "invitations" && method === "POST"))) ||
    (path.length === 2 && path[0] === "invitations" && guid.test(path[1]) && method === "DELETE") ||
    (path.length === 2 && path[0] === "members" && guid.test(path[1]) && method === "DELETE") ||
    (path.length === 3 && path[0] === "members" && guid.test(path[1]) &&
      path[2] === "role" && method === "PUT") ||
    (path.length === 2 && path[0] === "transfer-ownership" && guid.test(path[1]) && method === "POST");
  if (!valid) return new NextResponse(null, { status: 404 });
  return proxyWorkspace(request, `/api/workspaces/${workspaceId}/${path.join("/")}`, method);
}
export async function GET(request: NextRequest, context: Params) { return route(request, context, "GET"); }
export async function POST(request: NextRequest, context: Params) { return route(request, context, "POST"); }
export async function PUT(request: NextRequest, context: Params) { return route(request, context, "PUT"); }
export async function DELETE(request: NextRequest, context: Params) { return route(request, context, "DELETE"); }
