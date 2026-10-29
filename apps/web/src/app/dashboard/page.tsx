"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import {
  ApiError, workspaceApi, roleLabel, statusLabel,
  type Workspace, type WorkspaceOverview, type Project, type ProjectInput,
  type ProjectStatus,
} from "../../lib/workspace-api";

type Tab = "overview" | "projects" | "settings";
const btn = "rounded-xl bg-cyan-300 px-4 py-2.5 text-sm font-semibold text-slate-950 transition hover:bg-cyan-200 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-cyan-300 disabled:opacity-40";
const field = "mt-2 w-full rounded-xl border border-white/15 bg-[#121e2e] px-3 py-3 text-sm text-slate-100 placeholder:text-slate-500 focus:border-cyan-300 focus:outline-none focus:ring-2 focus:ring-cyan-300/20";
const panel = "rounded-2xl border border-white/10 bg-[#101927] p-5 shadow-xl shadow-black/10 sm:p-7";
const subtitle = "text-xs font-medium uppercase tracking-[.2em] text-slate-400";
const emptyProject: ProjectInput = { name: "", description: "", status: 1 };
const statusAccent: Record<ProjectStatus, string> = {
  1: "bg-slate-400/10 text-slate-300 border-slate-400/20",
  2: "bg-cyan-400/10 text-cyan-200 border-cyan-400/20",
  3: "bg-amber-400/10 text-amber-200 border-amber-400/20",
  4: "bg-emerald-400/10 text-emerald-200 border-emerald-400/20",
  5: "bg-slate-400/10 text-slate-300 border-slate-400/20"
};
function formatDate(date: string): string {
  return new Intl.DateTimeFormat("en", { month: "short", day: "numeric", year: "numeric" }).format(new Date(date));
}
function Stat({ label, value, detail }: { label: string; value: number; detail: string }) {
  return <div className={panel}>
    <p className={subtitle}>{label}</p>
    <p className="mt-5 text-4xl font-semibold tracking-tight text-white" data-testid="stat-value">{value}</p>
    <p className="mt-2 text-xs text-slate-400">{detail}</p>
  </div>;
}
export default function DashboardPage() {
  const router = useRouter();
  const [workspaces, setWorkspaces] = useState<Workspace[]>([]);
  const [workspaceId, setWorkspaceId] = useState("");
  const [tab, setTab] = useState<Tab>("overview");
  const [overview, setOverview] = useState<WorkspaceOverview | null>(null);
  const [projects, setProjects] = useState<Project[]>([]);
  const [draft, setDraft] = useState<ProjectInput>(emptyProject);
  const [editing, setEditing] = useState<string | null>(null);
  const [newName, setNewName] = useState("");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const currentWorkspace = useRef("");

  function errorMessage(err: unknown) {
    if (err instanceof ApiError && err.status === 401) { router.replace("/login"); return; }
    setMessage(err instanceof Error ? err.message : "Unable to load workspace.");
  }
  const loadWorkspaces = useCallback(async () => {
    try {
      const list = await workspaceApi.list();
      setWorkspaces(list);
      setWorkspaceId(old => list.find(w => w.id === old)?.id ?? list[0]?.id ?? "");
      setLoading(false);
    } catch (error) {
      setLoading(false);
      if (error instanceof ApiError && error.status === 401) router.replace("/login");
      else setMessage("Unable to load your workspaces.");
    }
  }, [router]);

  const loadWorkspace = useCallback(async (id: string) => {
    if (!id) { setOverview(null); setProjects([]); return; }
    try {
      const [summary, items] = await Promise.all([workspaceApi.overview(id), workspaceApi.projects(id)]);
      if (currentWorkspace.current !== id) return;
      setOverview(summary);
      setProjects(items);
      setNewName(summary.name);
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) router.replace("/login");
      else setMessage("Unable to load workspace data.");
    }
  }, [router]);

  useEffect(() => { void Promise.resolve().then(loadWorkspaces); }, [loadWorkspaces]);
  useEffect(() => { currentWorkspace.current = workspaceId; void Promise.resolve().then(() => loadWorkspace(workspaceId)); }, [workspaceId, loadWorkspace]);

  async function createOrUpdate(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!workspaceId) return;
    setBusy(true); setMessage("");
    try {
      if (editing) {
        await workspaceApi.updateProject(workspaceId, editing, draft);
        setMessage("Project updated.");
      } else {
        await workspaceApi.createProject(workspaceId, draft);
        setMessage("Project created.");
      }
      setEditing(null); setDraft(emptyProject);
      await loadWorkspace(workspaceId);
    } catch (err) { errorMessage(err); }
    finally { setBusy(false); }
  }
  async function archiveProject(projectId: string) {
    if (!workspaceId || !window.confirm("Archive this project? It will be hidden from the active board.")) return;
    setBusy(true); setMessage("");
    try {
      await workspaceApi.archiveProject(workspaceId, projectId);
      setMessage("Project archived.");
      await loadWorkspace(workspaceId);
    } catch (err) { errorMessage(err); }
    finally { setBusy(false); }
  }
  async function renameWorkspace(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!workspaceId) return;
    setBusy(true); setMessage("");
    try {
      await workspaceApi.rename(workspaceId, newName);
      await Promise.all([loadWorkspaces(), loadWorkspace(workspaceId)]);
      setMessage("Workspace settings saved.");
    } catch (err) { errorMessage(err); }
    finally { setBusy(false); }
  }
  function startEdit(project: Project) {
    setDraft({ name: project.name, description: project.description, status: project.status });
    setEditing(project.id);
    setTab("projects");
    window.scrollTo({ top: 0, behavior: "smooth" });
  }
  async function logout() {
    setBusy(true);
    try {
      const response = await fetch("/api/session/logout", { method: "POST", credentials: "same-origin" });
      if (response.ok) router.replace("/login");
      else setMessage("Could not confirm logout. Please retry.");
    } catch { setMessage("Connection unavailable."); }
    finally { setBusy(false); }
  }
  const active = workspaces.find(w => w.id === workspaceId);
  const role = overview?.role ?? active?.role ?? 1;
  const canWrite = role >= 2, canManage = role >= 3;
  const counts = { planned: projects.filter(p => p.status === 1).length, active: projects.filter(p => p.status === 2).length,
    blocked: projects.filter(p => p.status === 3).length, done: projects.filter(p => p.status === 4).length };

  return <div className="min-h-screen bg-[#080f19] text-slate-100">
    <div className="mx-auto flex min-h-screen max-w-[1560px] flex-col lg:flex-row">
      <aside className="border-b border-white/10 bg-[#0c1623] px-5 py-5 lg:sticky lg:top-0 lg:flex lg:h-screen lg:w-64 lg:shrink-0 lg:flex-col lg:border-b-0 lg:border-r lg:px-6 lg:py-9">
        <div className="flex items-center justify-between lg:block">
          <Link href="/" className="text-xl font-black tracking-tight text-cyan-300">ICEHOTT <span className="font-normal text-slate-300">AI OS</span></Link>
          <span className="rounded-lg border border-cyan-300/20 px-2 py-1 text-[10px] font-semibold tracking-widest text-cyan-200">WORKSPACE</span>
        </div>
        <p className="mt-8 hidden text-xs font-bold uppercase tracking-[.2em] text-slate-500 lg:block">Navigation</p>
        <nav aria-label="Workspace navigation" className="mt-4 flex gap-2 overflow-x-auto lg:flex-col">
          {([["overview","Overview"],["projects","Projects"],["settings","Settings"]] as const).map(([id,label]) =>
            <button key={id} type="button" aria-current={tab === id ? "page" : undefined}
              onClick={() => { setTab(id); setMessage(""); }} className={`shrink-0 rounded-xl px-4 py-3 text-left text-sm transition focus-visible:outline-2 focus-visible:outline-cyan-300 ${tab === id ? "bg-cyan-300/10 font-semibold text-cyan-200" : "text-slate-400 hover:bg-white/5 hover:text-white"}`}>
              {label}
            </button>)}
          <Link href="/workspaces" className="shrink-0 rounded-xl px-4 py-3 text-sm text-slate-400 hover:bg-white/5 hover:text-white">Team & access</Link>
        </nav>
        <div className="mt-auto hidden border-t border-white/10 pt-6 lg:block">
          <p className="text-xs text-slate-500">Signed in · Phase 2 workspace</p>
          <button type="button" disabled={busy} onClick={logout} className="mt-4 text-sm text-slate-300 hover:text-cyan-200">Sign out</button>
        </div>
      </aside>
      <main id="main" className="min-w-0 flex-1 px-5 py-8 sm:px-8 lg:px-12 lg:py-10">
        <div className="flex flex-wrap items-start justify-between gap-5">
          <div>
            <p className={subtitle}>ICEHOTT / Workspace command center</p>
            <h1 className="mt-3 text-3xl font-semibold tracking-tight sm:text-4xl">
              {tab === "overview" ? "Workspace overview" : tab === "projects" ? "Projects" : "Workspace settings"}
            </h1>
            <p className="mt-2 text-sm text-slate-400">A focused space for your team, projects and progress.</p>
          </div>
          <div className="flex flex-wrap items-end gap-3">
            <label className="block text-xs font-medium text-slate-400">Workspace
              <select aria-label="Choose workspace" className="mt-2 block min-w-48 rounded-xl border border-white/15 bg-[#121e2e] px-4 py-3 text-sm text-white focus:border-cyan-300"
                value={workspaceId} onChange={event => { currentWorkspace.current = event.target.value; setWorkspaceId(event.target.value); setOverview(null); setProjects([]); setEditing(null); setDraft(emptyProject); }}>
                {workspaces.map(w => <option key={w.id} value={w.id}>{w.displayName}</option>)}
                {!workspaces.length && <option value="">No workspaces</option>}
              </select>
            </label>
            <button type="button" disabled={busy} onClick={logout} className="rounded-xl border border-white/15 px-4 py-3 text-sm hover:border-cyan-300 lg:hidden">Sign out</button>
          </div>
        </div>
        <div aria-live="polite" className="min-h-6">
          {message && <p role="status" className="mt-6 rounded-xl border border-cyan-300/20 bg-cyan-300/5 p-4 text-sm text-slate-200">{message}</p>}
        </div>
        {loading ? <p role="status" className="mt-10 text-slate-400">Loading your workspaces…</p> :
        !active ? <section className={panel+" mt-8"}>
          <h2 className="text-2xl font-semibold">Start with your first workspace</h2>
          <p className="mt-3 text-slate-400">Create a workspace to access projects, team permissions and your dashboard.</p>
          <Link href="/workspaces" className="mt-5 inline-block rounded-xl bg-cyan-300 px-5 py-3 font-semibold text-slate-950">Create workspace</Link>
        </section> :
        <>
          <div className="mt-3 flex items-center gap-2 text-xs text-slate-400">
            <span className="rounded-full border border-cyan-300/20 bg-cyan-300/5 px-3 py-1 font-semibold text-cyan-200">{roleLabel[role]}</span>
            <span>{active.slug}</span>
          </div>
          {tab === "overview" && <div className="mt-8 space-y-6">
            <section aria-label="Workspace statistics" className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
              <Stat label="Projects" value={overview?.projectCount ?? 0} detail="Non-archived projects" />
              <Stat label="In progress" value={overview?.activeProjects ?? 0} detail="Currently active" />
              <Stat label="Completed" value={overview?.completedProjects ?? 0} detail="Delivered projects" />
              <Stat label="Team members" value={overview?.memberCount ?? 0} detail="Active workspace roles" />
            </section>
            <div className="grid gap-6 xl:grid-cols-[1.25fr_.75fr]">
              <section className={panel}>
                <div className="flex items-center justify-between gap-3">
                  <div><p className={subtitle}>Project portfolio</p><h2 className="mt-2 text-xl font-semibold">Recent work</h2></div>
                  <button type="button" onClick={() => setTab("projects")} className="text-sm font-semibold text-cyan-300 hover:underline">View projects</button>
                </div>
                <div className="mt-6 space-y-3">
                  {!projects.length && <p className="rounded-xl border border-dashed border-white/15 px-5 py-8 text-sm text-slate-400">No projects yet. Add one from the Projects tab.</p>}
                  {projects.slice(0,5).map(project => <div key={project.id} className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-white/10 p-4">
                    <div className="min-w-0"><p className="truncate font-medium">{project.name}</p><p className="mt-1 text-xs text-slate-500">Updated {formatDate(project.updatedAtUtc)}</p></div>
                    <span className={`rounded-full border px-3 py-1 text-xs ${statusAccent[project.status]}`}>{statusLabel[project.status]}</span>
                  </div>)}
                </div>
                <div className="mt-7 grid grid-cols-4 gap-2 text-center text-xs">
                  {([["Planned", counts.planned],["Active", counts.active],["Blocked", counts.blocked],["Done", counts.done]] as const).map(([label,count]) =>
                    <div key={label} className="rounded-lg bg-white/5 p-3"><div className="text-lg font-semibold text-white">{count}</div><div className="mt-1 text-slate-400">{label}</div></div>)}
                </div>
              </section>
              <section className={panel}>
                <p className={subtitle}>Audit activity</p>
                <h2 className="mt-2 text-xl font-semibold">Latest changes</h2>
                <ol className="mt-6 space-y-5">
                  {!overview?.recentActivity.length && <li className="text-sm text-slate-400">No activity recorded.</li>}
                  {overview?.recentActivity.map((event,i) => <li key={event.happenedAtUtc+"-"+i} className="border-l-2 border-cyan-300/25 pl-4">
                    <p className="text-sm font-medium capitalize">{event.action.replaceAll(".", " ").replaceAll("_", " ")}</p>
                    <p className="mt-1 text-xs text-slate-500">{formatDate(event.happenedAtUtc)}</p>
                  </li>)}
                </ol>
              </section>
            </div>
            <div className={panel+" flex flex-wrap items-center justify-between gap-4"}>
              <div><p className="text-lg font-semibold">Team access</p><p className="mt-1 text-sm text-slate-400">Invite colleagues and manage workspace roles.</p></div>
              <Link href="/workspaces" className={btn}>Manage team</Link>
            </div>
          </div>}
          {tab === "projects" && <div className="mt-8 grid items-start gap-6 xl:grid-cols-[.9fr_1.1fr]">
            <section className={panel}>
              <h2 className="text-xl font-semibold">{editing ? "Edit project" : "Create project"}</h2>
              <p className="mt-2 text-sm text-slate-400">Projects stay inside your current workspace.</p>
              {canWrite ? <form className="mt-6 space-y-5" onSubmit={createOrUpdate}>
                <label className="block text-sm">Project name
                  <input required maxLength={120} className={field} value={draft.name} placeholder="e.g. Customer portal"
                    onChange={e => setDraft({...draft,name:e.target.value})} /></label>
                <label className="block text-sm">Description
                  <textarea maxLength={2000} rows={5} className={field} value={draft.description} placeholder="What are we delivering?"
                    onChange={e => setDraft({...draft,description:e.target.value})} /></label>
                <label className="block text-sm">Status
                  <select className={field} value={draft.status} onChange={e => setDraft({...draft,status:Number(e.target.value) as ProjectStatus})}>
                    {([1,2,3,4] as ProjectStatus[]).map(x => <option key={x} value={x}>{statusLabel[x]}</option>)}
                  </select></label>
                <button type="submit" disabled={busy} className={btn}>{editing ? "Save changes" : "Create project"}</button>
                {editing && <button type="button" onClick={() => {setEditing(null);setDraft(emptyProject);}} className="ml-4 text-sm text-slate-300 hover:text-white">Cancel editing</button>}
              </form> : <p className="mt-5 rounded-xl border border-white/10 p-4 text-sm text-slate-400">Your Viewer role can read projects but cannot change them.</p>}
            </section>
            <section className={panel}>
              <div className="flex items-center justify-between"><h2 className="text-xl font-semibold">All projects</h2><span className="text-xs text-slate-400">{projects.length} total</span></div>
              <div className="mt-6 space-y-4">
                {!projects.length && <p className="rounded-xl border border-dashed border-white/15 p-6 text-sm text-slate-400">The project board is empty.</p>}
                {projects.map(project => <article key={project.id} className="rounded-xl border border-white/10 bg-white/[.025] p-5">
                  <div className="flex flex-wrap items-center justify-between gap-3">
                    <h3 className="min-w-0 break-words text-base font-semibold">{project.name}</h3>
                    <span className={`rounded-full border px-3 py-1 text-xs ${statusAccent[project.status]}`}>{statusLabel[project.status]}</span>
                  </div>
                  {project.description && <p className="mt-3 whitespace-pre-wrap break-words text-sm leading-6 text-slate-400">{project.description}</p>}
                  <div className="mt-5 flex flex-wrap items-center justify-between gap-3 border-t border-white/10 pt-4">
                    <time className="text-xs text-slate-500" dateTime={project.updatedAtUtc}>Updated {formatDate(project.updatedAtUtc)}</time>
                    <div className="flex gap-4">
                      {canWrite && <button type="button" onClick={() => startEdit(project)} className="text-sm text-cyan-300 hover:underline">Edit</button>}
                      {canManage && <button disabled={busy} type="button" onClick={() => void archiveProject(project.id)} className="text-sm text-red-300 hover:underline">Archive</button>}
                    </div>
                  </div>
                </article>)}
              </div>
            </section>
          </div>}
          {tab === "settings" && <div className="mt-8 grid gap-6 xl:grid-cols-[1fr_.85fr]">
            <section className={panel}>
              <h2 className="text-xl font-semibold">Workspace profile</h2>
              <p className="mt-2 text-sm text-slate-400">Update the public-facing name. The unique slug remains immutable.</p>
              <form className="mt-8 space-y-5" onSubmit={renameWorkspace}>
                <label className="block text-sm">Workspace name<input className={field} required disabled={!canManage}
                  maxLength={120} value={newName} onChange={e => setNewName(e.target.value)} /></label>
                <label className="block text-sm">Workspace slug
                  <input className={field+" cursor-not-allowed opacity-60"} readOnly value={active.slug} /></label>
                <button className={btn} type="submit" disabled={busy || !canManage}>Save settings</button>
                {!canManage && <p className="text-sm text-slate-400">Admin or Owner access is required to edit workspace settings.</p>}
              </form>
            </section>
            <section className={panel}>
              <h2 className="text-xl font-semibold">Permissions & security</h2>
              <dl className="mt-6 divide-y divide-white/10 text-sm">
                <div className="flex justify-between gap-4 py-4"><dt className="text-slate-400">Your role</dt><dd>{roleLabel[role]}</dd></div>
                <div className="flex justify-between gap-4 py-4"><dt className="text-slate-400">Team members</dt><dd>{overview?.memberCount ?? 0}</dd></div>
                {canManage && <div className="flex justify-between gap-4 py-4"><dt className="text-slate-400">Pending invitations</dt><dd>{overview?.pendingInvitations ?? 0}</dd></div>}
                <div className="flex justify-between gap-4 py-4"><dt className="text-slate-400">Isolation</dt><dd className="text-emerald-300">Server enforced</dd></div>
              </dl>
              <Link href="/workspaces" className="mt-5 inline-block text-sm font-semibold text-cyan-300 hover:underline">Manage team permissions</Link>
              <p className="mt-6 text-xs leading-5 text-slate-500">Billing and AI provider controls are not active in this phase.</p>
            </section>
          </div>}
        </>}
      </main>
    </div>
  </div>;
}
