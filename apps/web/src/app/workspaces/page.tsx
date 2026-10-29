"use client";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { FormEvent, useCallback, useEffect, useState } from "react";
import { buttonClass, inputClass, messageClass } from "../components/AuthLayout";

type Workspace = {id:string; slug:string; displayName:string; role:number};
type Member = {userId:string;email:string;role:number};
type Invitation = {id:string;email:string;role:number;expiresAtUtc:string};
const roles:Record<number,string> = {1:"Viewer",2:"Member",3:"Admin",4:"Owner"};
const withJSON = {"content-type":"application/json"};

export default function WorkspacesPage() {
  const router = useRouter();
  const [workspaces,setWorkspaces] = useState<Workspace[]>([]);
  const [selected,setSelected] = useState("");
  const [members,setMembers] = useState<Member[]>([]);
  const [invitations,setInvitations] = useState<Invitation[]>([]);
  const [slug,setSlug] = useState("");
  const [name,setName] = useState("");
  const [inviteEmail,setInviteEmail] = useState("");
  const [inviteRole,setInviteRole] = useState(2);
  const [message,setMessage] = useState("");
  const [busy,setBusy] = useState(false);
  const [loading,setLoading] = useState(true);

  const load = useCallback(async () => {
    const response=await fetch("/api/workspaces", {cache:"no-store",credentials:"same-origin"});
    if(response.status===401) {router.replace("/login");return;}
    if(!response.ok) {setMessage("Workspaces unavailable.");return;}
    const list=(await response.json()) as Workspace[];
    setWorkspaces(list);
    setSelected(current=>list.some(x=>x.id===current)?current:(list[0]?.id??""));
    setLoading(false);
  },[router]);

  const loadMembers=useCallback(async(wid:string)=>{
    if(!wid) {setMembers([]);setInvitations([]);return;}
    const response=await fetch(`/api/workspaces/${wid}/members`,{cache:"no-store"});
    if(response.ok) setMembers((await response.json()) as Member[]);
    const invites=await fetch(`/api/workspaces/${wid}/invitations`,{cache:"no-store"});
    setInvitations(invites.ok?(await invites.json()) as Invitation[]:[]);
  },[]);

  useEffect(()=>{void Promise.resolve().then(load).catch(()=>{setMessage("Cannot connect to API.");setLoading(false);});},[load]);
  useEffect(()=>{void Promise.resolve().then(()=>loadMembers(selected)).catch(()=>setMessage("Member list unavailable."));},[selected,loadMembers]);

  async function create(e:FormEvent<HTMLFormElement>) {
    e.preventDefault();setBusy(true);setMessage("");
    try {
      const response=await fetch("/api/workspaces",{
        method:"POST",headers:withJSON,credentials:"same-origin",
        body:JSON.stringify({slug,name})
      });
      if(!response.ok) {setMessage(response.status===409?"Workspace slug already exists.":"Cannot create workspace. Check slug (lowercase, 3–48) and name.");return;}
      const created=(await response.json()) as Workspace;
      setSlug("");setName("");await load();setSelected(created.id);
      setMessage("Workspace created with your Owner membership.");
    } catch {setMessage("Connection unavailable.");}
    finally {setBusy(false);}
  }

  async function invite(e:FormEvent<HTMLFormElement>) {
    e.preventDefault(); if(!selected)return;setBusy(true);setMessage("");
    try {
      const response=await fetch(`/api/workspaces/${selected}/invitations`,{
        method:"POST",headers:withJSON,credentials:"same-origin",
        body:JSON.stringify({email:inviteEmail,role:inviteRole})
      });
      setMessage(response.ok?"Invitation emailed.":"Could not invite this member.");
      if(response.ok) {setInviteEmail("");await loadMembers(selected);}
    } catch {setMessage("Connection unavailable.");}
    finally {setBusy(false);}
  }

  async function changeRole(id:string,role:number) {
    const response=await fetch(`/api/workspaces/${selected}/members/${id}/role`,{
      method:"PUT",headers:withJSON,body:JSON.stringify({role})
    });
    setMessage(response.ok?"Member role updated.":"Role change denied.");
    if(response.ok) await loadMembers(selected);
  }

  async function removeMember(id:string) {
    if(!window.confirm("Remove this member from the workspace?"))return;
    const response=await fetch(`/api/workspaces/${selected}/members/${id}`,{method:"DELETE"});
    setMessage(response.ok?"Member removed.":"Removal denied.");
    if(response.ok) await loadMembers(selected);
  }

  async function transfer(id:string) {
    if(!window.confirm("Transfer workspace ownership? You will become an Admin."))return;
    const response=await fetch(`/api/workspaces/${selected}/transfer-ownership/${id}`,{method:"POST"});
    setMessage(response.ok?"Ownership transferred.":"Ownership transfer denied.");
    if(response.ok) {await load();await loadMembers(selected);}
  }

  async function revoke(id:string) {
    const response=await fetch(`/api/workspaces/${selected}/invitations/${id}`,{method:"DELETE"});
    setMessage(response.ok?"Invitation revoked.":"Unable to revoke invitation.");
    if(response.ok)await loadMembers(selected);
  }

  async function logout() {
    const response=await fetch("/api/session/logout",{method:"POST",credentials:"same-origin"});
    if(response.ok)router.replace("/login");
    else setMessage("Could not confirm logout. Try again.");
  }

  const active=workspaces.find(x=>x.id===selected);
  const canInvite=!!active&&active.role>=3;
  return <main className="min-h-screen bg-[#070d16] text-slate-100">
    <header className="mx-auto flex max-w-6xl items-center justify-between border-b border-white/10 px-6 py-7">
      <Link href="/" className="text-lg font-bold text-cyan-300">ICEHOTT AI OS</Link>
      <button type="button" onClick={logout} className="rounded-lg border border-white/20 px-4 py-2 text-sm hover:border-white/50">Sign out</button>
    </header>
    <div className="mx-auto max-w-6xl px-6 py-12">
      <p className="text-xs uppercase tracking-[.22em] text-cyan-300">Phase 1 · Identity & tenancy</p>
      <h1 className="mt-3 text-4xl font-semibold">Your workspaces</h1>
      <p className="mt-3 max-w-2xl text-sm text-slate-400">Only workspaces you are a verified member of appear here. Every action checks your permissions on the server.</p>
      {message&&<p role="status" className={messageClass}>{message}</p>}
      <div className="mt-9 grid gap-6 lg:grid-cols-[.85fr_1.15fr]">
        <section className="rounded-2xl border border-white/10 bg-white/[.035] p-6">
          <h2 className="text-xl font-semibold">Workspace directory</h2>
          {loading?<p className="mt-4 text-slate-400">Loading…</p>:
            <div className="mt-5 flex flex-col gap-2">
              {workspaces.length===0&&<p className="text-sm text-slate-400">No workspaces yet.</p>}
              {workspaces.map(w=><button type="button" key={w.id} onClick={()=>setSelected(w.id)}
                className={`rounded-xl border p-4 text-left transition ${selected===w.id?"border-cyan-300/60 bg-cyan-300/10":"border-white/10 hover:border-white/30"}`}>
                <span className="block font-semibold">{w.displayName}</span>
                <span className="mt-1 block text-xs text-slate-400">{w.slug} · {roles[w.role]??"Unknown"}</span>
              </button>)}
            </div>}
          <h3 className="mt-10 text-lg font-semibold">Create a workspace</h3>
          <form onSubmit={create} className="mt-4 space-y-4">
            <label className="block text-sm">Display name<input required maxLength={120} className={inputClass}
              value={name} onChange={e=>setName(e.target.value)}/></label>
            <label className="block text-sm">Unique slug
              <input required minLength={3} maxLength={48} pattern="[a-z0-9][a-z0-9-]*[a-z0-9]"
                placeholder="my-team" className={inputClass} value={slug}
                onChange={e=>setSlug(e.target.value.toLowerCase())}/></label>
            <button type="submit" className={buttonClass} disabled={busy}>Create workspace</button>
          </form>
        </section>
        <section className="rounded-2xl border border-white/10 bg-white/[.035] p-6">
          <h2 className="text-xl font-semibold">Team and access</h2>
          <p className="mt-2 text-sm text-slate-400">{active?.displayName??"Select a workspace"} · {active?roles[active.role]:""}</p>
          <div className="mt-6 space-y-3">
            {members.map(member=><div key={member.userId} className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-white/10 p-4">
              <div><p className="break-all text-sm font-medium">{member.email}</p><p className="mt-1 text-xs text-slate-400">{roles[member.role]??"Unknown"}</p></div>
              {active?.role===4&&member.role!==4&&<div className="flex flex-wrap gap-2">
                <select aria-label={`Role for ${member.email}`} className="rounded-lg border border-white/20 bg-[#101927] px-2 py-2 text-sm" value={member.role}
                  onChange={e=>void changeRole(member.userId,Number(e.target.value))}>
                  <option value={1}>Viewer</option><option value={2}>Member</option><option value={3}>Admin</option>
                </select>
                <button type="button" onClick={()=>void transfer(member.userId)} className="rounded-lg border border-white/20 px-3 py-2 text-xs">Transfer</button>
                <button type="button" onClick={()=>void removeMember(member.userId)} className="rounded-lg border border-red-400/30 px-3 py-2 text-xs text-red-300">Remove</button>
              </div>}
            </div>)}
          </div>
          {canInvite&&<div className="mt-8 border-t border-white/10 pt-6">
            <h3 className="text-lg font-semibold">Invite a teammate</h3>
            <form onSubmit={invite} className="mt-4 space-y-4">
              <label className="block text-sm">Email address<input type="email" required className={inputClass}
                value={inviteEmail} onChange={e=>setInviteEmail(e.target.value)} /></label>
              <label className="block text-sm">Permission
                <select className={inputClass} value={inviteRole} onChange={e=>setInviteRole(Number(e.target.value))}>
                  <option value={1}>Viewer</option><option value={2}>Member</option>
                  {active?.role===4&&<option value={3}>Admin</option>}
                </select>
              </label>
              <button type="submit" className={buttonClass} disabled={busy}>Email invitation</button>
            </form>
            {invitations.length>0&&<div className="mt-7">
              <h4 className="text-sm font-semibold">Pending invitations</h4>
              {invitations.map(inv=><div key={inv.id} className="mt-3 flex items-center justify-between gap-2 text-sm">
                <span className="break-all text-slate-400">{inv.email} · {roles[inv.role]}</span>
                <button className="text-red-300" onClick={()=>void revoke(inv.id)}>Revoke</button>
              </div>)}
            </div>}
          </div>}
        </section>
      </div>
    </div>
  </main>;
}
