"use client";
import Link from "next/link";
import {useState} from "react";
import AuthLayout, {buttonClass,inputClass,messageClass} from "../components/AuthLayout";
export default function AcceptAction({token}:{token:string}) {
  const [status,setStatus]=useState("");
  const [busy,setBusy]=useState(false);
  const [password,setPassword]=useState("");
  async function accept() {
    setBusy(true);
    try {
      const response=await fetch("/api/invitations/accept",{
        method:"POST",headers:{"content-type":"application/json"},
        credentials:"same-origin",body:JSON.stringify({token})
      });
      if(response.ok) setStatus("Invitation accepted. Open your workspaces.");
      else if(response.status===401) setStatus("Please sign in with the verified email address that was invited.");
      else setStatus("The invitation is invalid, revoked or expired.");
    } catch {setStatus("Connection unavailable.");}
    finally {setBusy(false);}
  }
  async function registerFromInvite() {
    if(!token || !password) return;
    setBusy(true);
    try {
      const response=await fetch("/api/session/register-with-invitation", {
        method:"POST",headers:{"content-type":"application/json"},
        credentials:"same-origin",body:JSON.stringify({token,password})
      });
      setStatus(response.ok?"Your verified account was created. Sign in to continue.":
        "Could not create account. The invitation may be expired, used or linked to an existing account.");
      setPassword("");
    } catch {setStatus("Connection unavailable.");}
    finally {setBusy(false);}
  }
  return <AuthLayout title="Join a workspace" subtitle="Sign in with the same verified email address that received this invitation.">
    <button className={buttonClass} type="button" disabled={!token||busy} onClick={accept}>
      {busy?"Accepting…":"Accept invitation"}
    </button>
    <section className="mt-8 border-t border-white/15 pt-6">
      <h2 className="text-lg font-semibold">New user? Create an invited account</h2>
      <p className="mt-2 text-sm text-slate-400">Your invitation email proves mailbox ownership; public registration does not need to be enabled.</p>
      <label className="mt-4 block text-sm">Choose password
        <input type="password" autoComplete="new-password" minLength={12} maxLength={256} required
          value={password} onChange={e=>setPassword(e.target.value)} className={inputClass}/>
      </label>
      <button type="button" onClick={registerFromInvite} disabled={busy||!token||password.length<12}
        className={buttonClass+" mt-4"}>Create account from invitation</button>
    </section>
    {status&&<p role="status" className={messageClass}>{status}</p>}
    <div className="mt-6 flex gap-6 text-sm text-cyan-300">
      <Link href="/login">Sign in</Link><Link href="/workspaces">My workspaces</Link>
    </div>
  </AuthLayout>;
}
