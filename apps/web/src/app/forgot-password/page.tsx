"use client";
import Link from "next/link";
import { FormEvent, useState } from "react";
import AuthLayout, { buttonClass, inputClass, messageClass } from "../components/AuthLayout";

export default function ForgotPasswordPage() {
  const [email,setEmail] = useState("");
  const [message,setMessage] = useState("");
  const [busy,setBusy] = useState(false);
  async function submit(e:FormEvent<HTMLFormElement>) {
    e.preventDefault();setBusy(true);
    try {
      await fetch("/api/session/request-password-reset", {
        method:"POST",headers:{"content-type":"application/json"},
        credentials:"same-origin",body:JSON.stringify({email})
      });
    } catch { /* Use the same non-enumerating confirmation. */ }
    setMessage("If the account exists and is verified, a reset link will be sent.");
    setBusy(false);
  }
  return <AuthLayout title="Recover your account" subtitle="We'll email a one-time password reset link.">
    <form onSubmit={submit} className="space-y-5">
      <label className="block text-sm">Account email
        <input className={inputClass} type="email" required value={email}
          onChange={e=>setEmail(e.target.value)} autoComplete="email" />
      </label>
      <button className={buttonClass} type="submit" disabled={busy}>Request reset</button>
    </form>
    {message && <p role="status" className={messageClass}>{message}</p>}
    <p className="mt-6 text-sm text-cyan-300"><Link href="/login">Back to sign in</Link></p>
  </AuthLayout>;
}
