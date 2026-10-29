"use client";
import Link from "next/link";
import { useState } from "react";
import AuthLayout, { buttonClass, messageClass } from "../components/AuthLayout";

export default function VerifyAction({ token }: { token: string }) {
  const [status,setStatus]=useState("");
  const [busy,setBusy]=useState(false);
  async function verify() {
    if(!token) {setStatus("The verification link is invalid.");return;}
    setBusy(true);
    try {
      const response=await fetch("/api/session/verify-email", {
        method:"POST", headers:{"content-type":"application/json"},
        credentials:"same-origin",body:JSON.stringify({token})
      });
      setStatus(response.ok?"Email verified. You can now sign in.":"The link has expired or was already used.");
    } catch {setStatus("Unable to connect.");}
    finally {setBusy(false);}
  }
  return <AuthLayout title="Verify your email" subtitle="Confirm your email address to activate your account.">
    <button className={buttonClass} type="button" disabled={busy||!token} onClick={verify}>
      {busy?"Verifying…":"Verify email"}
    </button>
    {status&&<p role="status" className={messageClass}>{status}</p>}
    <p className="mt-6 text-sm text-cyan-300"><Link href="/login">Go to sign in</Link></p>
  </AuthLayout>;
}
