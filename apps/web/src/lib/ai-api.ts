import { ApiError } from "./workspace-api";

export type AiConversation = { id: string; title: string; updatedAtUtc: string };
export type AiMessage = { id: string; role: 1 | 2; content: string; createdAtUtc: string };
export type AiUsage = { requestsToday: number; dailyRequestLimit: number; reservedTokensToday: number; dailyTokenLimit: number };
export type AiReply = { conversationId: string; requestId: string; traceId: string; answer: string; usedTokens: number; provider: string };
const workspacePrefix = (id: string) => `/api/workspaces/${encodeURIComponent(id)}/ai`;

async function request<T>(url: string, method: "GET" | "POST" = "GET", payload?: unknown): Promise<T> {
  const response = await fetch(url, {
    method, credentials: "same-origin", cache: "no-store",
    headers: payload === undefined ? undefined : { "content-type": "application/json" },
    ...(payload === undefined ? {} : { body: JSON.stringify(payload) })
  });
  if (!response.ok) throw new ApiError(response.status,
    response.status === 401 ? "Session expired. Sign in again." :
    response.status === 403 ? "Your role does not allow this action." :
    response.status === 404 ? "AI Core is disabled for this environment, or the conversation is unavailable." :
    response.status === 409 ? "The request was already admitted. Start a new message if it failed." :
    response.status === 429 ? "Daily AI allowance reached." :
    response.status === 503 ? "AI runtime unavailable. Your request may have consumed one allowance." :
    "AI request failed.");
  return response.json() as Promise<T>;
}

export const aiApi = {
  conversations: (workspaceId: string) =>
    request<AiConversation[]>(`${workspacePrefix(workspaceId)}/conversations`),
  create: (workspaceId: string, title: string) =>
    request<AiConversation>(`${workspacePrefix(workspaceId)}/conversations`, "POST", { title }),
  messages: (workspaceId: string, conversationId: string) =>
    request<AiMessage[]>(`${workspacePrefix(workspaceId)}/conversations/${encodeURIComponent(conversationId)}/messages`),
  usage: (workspaceId: string) => request<AiUsage>(`${workspacePrefix(workspaceId)}/usage`),
  send: (workspaceId: string, conversationId: string, message: string, requestId: string) =>
    request<AiReply>(`${workspacePrefix(workspaceId)}/conversations/${encodeURIComponent(conversationId)}/send`,
      "POST", { message, requestId })
};
