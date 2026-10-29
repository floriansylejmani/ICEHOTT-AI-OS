import { beforeEach, afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import AiChatPanel from "../app/dashboard/AiChatPanel";
import { ApiError } from "../lib/workspace-api";

const api = vi.hoisted(() => ({
  conversations: vi.fn(), create: vi.fn(), messages: vi.fn(), usage: vi.fn(), send: vi.fn(),
}));
vi.mock("../lib/ai-api", () => ({ aiApi: api }));

const wid="aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa";
const cid="bbbbbbbb-bbbb-4bbb-bbbb-bbbbbbbbbbbb";
const first={id:cid,title:"Product ideation",updatedAtUtc:"2026-10-29T00:00:00Z"};
const usage={requestsToday:1,dailyRequestLimit:30,reservedTokensToday:800,dailyTokenLimit:30000};

beforeEach(() => {
  vi.resetAllMocks();
  api.conversations.mockResolvedValue([first]);
  api.messages.mockResolvedValue([{id:"cccccccc-cccc-4ccc-cccc-cccccccccccc",role:2,content:"DEMO MODE — response",createdAtUtc:first.updatedAtUtc}]);
  api.usage.mockResolvedValue(usage);
  api.create.mockResolvedValue(first);
  api.send.mockResolvedValue({conversationId:cid,requestId:"fake",traceId:"other",answer:"DEMO MODE",usedTokens:100,provider:"demo-offline"});
});
afterEach(cleanup);

describe("Phase 3 safe AI browser interface", () => {
  it("shows saved messages and quota but prevents Viewer creation or sends", async () => {
    render(<AiChatPanel workspaceId={wid} role={1} />);
    expect(await screen.findByText("DEMO MODE — response")).toBeTruthy();
    expect(screen.getByText("Product ideation")).toBeTruthy();
    expect(screen.getByText("Requests today")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "New conversation" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Send message" })).toBeNull();
    expect(screen.getByText(/Viewers may read/)).toBeTruthy();
  });

  it("lets Member send through typed API with unique request identifier", async () => {
    render(<AiChatPanel workspaceId={wid} role={2} />);
    await screen.findByText("DEMO MODE — response");
    const field=screen.getByRole("textbox", {name:"Your message"});
    fireEvent.change(field,{target:{value:"Plan a feature"}});
    fireEvent.click(screen.getByRole("button",{name:"Send message"}));
    await waitFor(() => expect(api.send).toHaveBeenCalled());
    const args=api.send.mock.calls[0];
    expect(args[0]).toBe(wid);
    expect(args[1]).toBe(cid);
    expect(args[2]).toBe("Plan a feature");
    expect(args[3]).toMatch(/^[0-9a-f-]{36}$/);
    expect(await screen.findByText("Response provider: demo-offline")).toBeTruthy();
  });

  it("does not present an unavailable AI runtime as a real model", async () => {
    api.conversations.mockRejectedValue(new ApiError(404, "AI Core is disabled"));
    render(<AiChatPanel workspaceId={wid} role={4} />);
    expect(await screen.findByText(/AI Core is currently offline or disabled/)).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Send message" })).toBeNull();
  });
});
