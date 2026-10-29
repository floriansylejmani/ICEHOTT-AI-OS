import { NextRequest } from "next/server";
import { postPublic } from "@/lib/auth-proxy";
export async function POST(request: NextRequest) {
  return postPublic(request, "/api/auth/register-with-invitation");
}
