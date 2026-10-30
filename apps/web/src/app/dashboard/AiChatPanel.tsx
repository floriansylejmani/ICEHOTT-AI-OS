"use client";

import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { aiApi, type AiConversation, type AiMessage, type AiReply, type AiUsage } from "../../lib/ai-api";
import { ApiError, type WorkspaceRole } from "../../lib/workspace-api";

const card = "rounded-2xl border border-white/10 bg-[#101927] p-5 sm:p-7";
const input = "w-full rounded-xl border border-white/15 bg-[#0b1421] p-4 text-slate-100 focus:border-cyan-300 focus:outline-none focus:ring-2 focus:ring-cyan-300/20";
const action = "rounded-xl bg-cyan-300 px-4 py-2.5 text-sm font-semibold text-slate-950 hover:bg-cyan-200 disabled:cursor-not-allowed disabled:opacity-40";

export default function AiChatPanel({ workspaceId, role }: { workspaceId: string; role: WorkspaceRole }) {
  const [conversations, setConversations] = useState<AiConversation[]>([]);
  const [conversationId, setConversationId] = useState("");
  const [messages, setMessages] = useState<AiMessage[]>([]);
  const [usage, setUsage] = useState<AiUsage | null>(null);
  const [draft, setDraft] = useState("");
  const [chatTitle, setChatTitle] = useState("");
  const [provider, setProvider] = useState("");
  const [message, setMessage] = useState("");
  const [enabled, setEnabled] = useState<boolean | null>(null);
  const [sending, setSending] = useState(false);
  const selected = useRef("");
  const workspace = useRef(workspaceId);
  const canSend = role >= 2;

  const refresh = useCallback(async (wid: string) => {
    try {
      const [items, allowance] = await Promise.all([aiApi.conversations(wid), aiApi.usage(wid)]);
      if (workspace.current !== wid) return;
      setConversations(items);
      setUsage(allowance);
      setEnabled(true);
      setConversationId(old => items.find(c => c.id === old)?.id ?? items[0]?.id ?? "");
    } catch (error) {
      if (workspace.current !== wid) return;
      setEnabled(false);
      setMessage(error instanceof ApiError ? error.message : "Unable to reach AI Core.");
    }
  }, []);

  useEffect(() => {
    workspace.current = workspaceId;
    selected.current = "";
    // Parent mounts this panel with key={workspaceId}; each workspace starts
    // with fresh state and previous workspace messages never carry across.
    void Promise.resolve().then(() => refresh(workspaceId));
  }, [workspaceId, refresh]);

  useEffect(() => {
    selected.current = conversationId;
    if (!conversationId) return;
    void Promise.resolve().then(async () => {
      try {
        const list = await aiApi.messages(workspaceId, conversationId);
        if (workspace.current === workspaceId && selected.current === conversationId)
          setMessages(list);
      } catch {
        if (workspace.current === workspaceId && selected.current === conversationId)
          setMessage("Unable to load this conversation.");
      }
    });
  }, [workspaceId, conversationId]);

  async function createChat(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!canSend || sending) return;
    setSending(true);setMessage("");
    try {
      const created = await aiApi.create(workspaceId, chatTitle.trim() || "New conversation");
      await refresh(workspaceId);
      if (workspace.current === workspaceId) {
        selected.current = created.id;
        setConversationId(created.id);
        setMessages([]);
        setChatTitle("");
        setMessage("Conversation created.");
      }
    } catch (err) {setMessage(err instanceof Error ? err.message : "Unable to create chat.");}
    finally {setSending(false);}
  }

  async function send(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!canSend || sending || !conversationId || !draft.trim() || draft.length > 4000) return;
    setSending(true);setMessage("");
    const wid = workspaceId, cid = conversationId;
    const text = draft.trim();
    const requestId = crypto.randomUUID();
    try {
      const result: AiReply = await aiApi.send(wid, cid, text, requestId);
      if (workspace.current !== wid || selected.current !== cid) return;
      setProvider(result.provider);
      setDraft("");
      setMessages(await aiApi.messages(wid, cid));
      setUsage(await aiApi.usage(wid));
    } catch (err) {
      setMessage(err instanceof Error ? err.message : "Unable to send message.");
      try { if (workspace.current === wid) setUsage(await aiApi.usage(wid)); } catch { /* optional */ }
    } finally {setSending(false);}
  }

  return <section aria-label="AI conversation" className="mt-8 grid items-start gap-6 xl:grid-cols-[.78fr_1.22fr]">
    <div className={card}>
      <p className="text-xs font-semibold uppercase tracking-[.2em] text-cyan-300">AI Core · controlled access</p>
      <h2 className="mt-3 text-xl font-semibold">Conversations</h2>
      <p className="mt-2 text-sm leading-6 text-slate-400">Chats belong to this workspace. All messages and usage are stored server-side.</p>
      {usage && <dl className="mt-6 grid grid-cols-2 gap-3 text-sm">
        <div className="rounded-xl bg-white/5 p-4"><dt className="text-slate-400">Requests today</dt>
          <dd className="mt-2 text-2xl font-semibold">{usage.requestsToday}<span className="text-sm font-normal text-slate-500"> / {usage.dailyRequestLimit}</span></dd></div>
        <div className="rounded-xl bg-white/5 p-4"><dt className="text-slate-400">Reserved units</dt>
          <dd className="mt-2 text-2xl font-semibold">{usage.reservedTokensToday}<span className="text-sm font-normal text-slate-500"> / {usage.dailyTokenLimit}</span></dd></div>
      </dl>}
      {enabled === null && <p role="status" className="mt-5 text-sm text-slate-400">Checking AI Core availability…</p>}
      {enabled === false && <p role="status" className="mt-5 rounded-xl border border-amber-300/20 bg-amber-300/5 p-4 text-sm text-amber-100">AI Core is currently offline or disabled. No external paid model calls are being made.</p>}
      {enabled && <>
        {canSend && <form className="mt-7 space-y-3" onSubmit={createChat}>
          <label className="block text-sm">New chat title
            <input className={input+" mt-2"} value={chatTitle} maxLength={100}
              onChange={e=>setChatTitle(e.target.value)} placeholder="e.g. Sprint planning" />
          </label>
          <button disabled={sending} type="submit" className={action}>New conversation</button>
        </form>}
        <div className="mt-7 space-y-2" aria-label="Saved conversations">
          {!conversations.length && <p className="py-4 text-sm text-slate-400">No conversations yet.</p>}
          {conversations.map(chat => <button key={chat.id} type="button"
            aria-pressed={chat.id === conversationId} onClick={()=>{ selected.current=chat.id; setConversationId(chat.id);setMessages([]);setProvider("");setMessage("");}}
            className={`w-full rounded-xl border p-3 text-left text-sm focus-visible:outline-2 focus-visible:outline-cyan-300 ${chat.id===conversationId?"border-cyan-300/40 bg-cyan-300/10 text-cyan-200":"border-white/10 text-slate-300 hover:bg-white/5"}`}>
            <span className="block truncate font-medium">{chat.title}</span>
            <span className="mt-1 block text-xs text-slate-500">{new Date(chat.updatedAtUtc).toLocaleDateString()}</span>
          </button>)}
        </div>
      </>}
    </div>
    <div className={card}>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div><p className="text-xs font-semibold uppercase tracking-[.2em] text-slate-400">Safe AI chat</p>
          <h2 className="mt-2 text-xl font-semibold">Messages</h2></div>
        <span className="rounded-full border border-white/15 px-3 py-1 text-xs text-slate-400">Tools disabled</span>
      </div>
      <div aria-live="polite" className="mt-3 min-h-6">
        {provider && <p className="text-xs text-cyan-200">Response provider: {provider}</p>}
        {message && <p role="status" className="mt-3 rounded-lg border border-amber-300/20 p-3 text-sm text-slate-200">{message}</p>}
      </div>
      <div aria-label="Conversation messages" className="mt-5 flex min-h-72 max-h-[520px] flex-col gap-4 overflow-y-auto rounded-xl border border-white/10 bg-[#0b1421] p-4">
        {!messages.length && <p className="m-auto max-w-sm text-center text-sm leading-6 text-slate-500">Choose a conversation and type a message. Offline demo responses are clearly labelled.</p>}
        {messages.map(entry => <article key={entry.id} className={`max-w-[92%] rounded-2xl p-4 text-sm leading-6 ${entry.role===1?"ml-auto bg-cyan-300/10 text-cyan-50":"mr-auto border border-white/10 bg-white/5 text-slate-200"}`}>
          <p className="mb-2 text-[11px] font-semibold uppercase tracking-[.14em] text-slate-400">{entry.role===1?"You":"AI response"}</p>
          <p className="whitespace-pre-wrap break-words">{entry.content}</p>
        </article>)}
      </div>
      {canSend && enabled && conversationId && <form onSubmit={send} className="mt-5 space-y-3">
        <label className="block text-sm font-medium">Your message
          <textarea className={input+" mt-2"} rows={4} maxLength={4000} required
            value={draft} onChange={e=>setDraft(e.target.value)} placeholder="Ask about your project…" /></label>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <span className="text-xs text-slate-500">{draft.length} / 4000 characters · 1 request per send</span>
          <button type="submit" disabled={sending || !draft.trim() ||
            (usage !== null && usage.requestsToday >= usage.dailyRequestLimit)}
            className={action}>{sending?"Generating…":"Send message"}</button>
        </div>
      </form>}
      {!canSend && enabled && <p className="mt-5 text-sm text-slate-400">Viewers may read saved conversations but cannot send messages.</p>}
    </div>
  </section>;
}
