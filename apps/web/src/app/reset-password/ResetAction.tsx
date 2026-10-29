"use client";
import Link from "next/link";
import { FormEvent, useState } from "react";
import AuthLayout, {buttonClass,inputClass,messageClass} from "../components/AuthLayout";
export default function ResetAction({token}:{token:string}) {
  const [password,setPassword]=useState("");
  const [busy,setBusy]=useState(false);
  const [status,setStatus]=useState("");
  async function submit(e:FormEvent<HTMLFormElement>) {
    e.preventDefault();setBusy(true);
    try {
      const response=await fetch("/api/session/reset-password",{
        method:"POST",headers:{"content-type":"application/json"},
        credentials:"same-origin",body:JSON.stringify({token,newPassword:password})
      });
      setStatus(response.ok?"Password changed. Existing sessions were revoked. Sign in again.":
        "The link is expired, invalid or your password does not meet the requirements.");
      setPassword("");
    } catch {setStatus("Connection unavailable.");}
    finally {setBusy(false);}
  }
  return <AuthLayout title="Set a new password" subtitle="This one-time recovery link expires after 20 minutes.">
    <form onSubmit={submit} className="space-y-5">
      <label className="block text-sm">New password
        <input className={inputClass} type="password" autoComplete="new-password"
          required minLength={12} maxLength={256} value={password}
          onChange={e=>setPassword(e.target.value)} />
      </label>
      <button className={buttonClass} disabled={!token||busy} type="submit">Change password</button>
    </form>
    {status&&<p role="status" className={messageClass}>{status}</p>}
    <p className="mt-6 text-sm text-cyan-300"><Link href="/login">Go to sign in</Link></p>
  </AuthLayout>;
}
