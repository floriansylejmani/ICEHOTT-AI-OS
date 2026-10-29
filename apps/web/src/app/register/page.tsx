"use client";
import Link from "next/link";
import { FormEvent, useState } from "react";
import AuthLayout, { buttonClass, inputClass, messageClass } from "../components/AuthLayout";

export default function RegisterPage() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();setBusy(true);setMessage("");
    try {
      const response = await fetch("/api/session/register", {
        method: "POST", headers: { "content-type": "application/json" },
        credentials: "same-origin", body: JSON.stringify({ email, password }),
      });
      setMessage(response.ok ? "Check your email for the verification link. You must verify before signing in." :
        response.status === 404 ? "Public registration is currently disabled. Contact your administrator." :
          response.status === 429 ? "Too many attempts. Please try later." :
          "Registration unavailable or details invalid.");
    } catch {setMessage("Connection unavailable.");}
    finally {setBusy(false);}
  }
  return <AuthLayout title="Create your account" subtitle="Registration is restricted until administrators enable it.">
    <form onSubmit={submit} className="space-y-5">
      <label className="block text-sm">Email
        <input className={inputClass} type="email" required value={email} maxLength={254}
          autoComplete="email" onChange={e => setEmail(e.target.value)} />
      </label>
      <label className="block text-sm">Password
        <input className={inputClass} type="password" required minLength={12} maxLength={256}
          autoComplete="new-password" value={password} onChange={e => setPassword(e.target.value)} />
        <span className="mt-2 block text-xs text-slate-400">At least 12 characters including letters and numbers.</span>
      </label>
      <button type="submit" disabled={busy} className={buttonClass}>{busy ? "Submitting…" : "Request account"}</button>
    </form>
    {message && <p role="status" className={messageClass}>{message}</p>}
    <p className="mt-6 text-sm text-cyan-300"><Link href="/login">Back to sign in</Link></p>
  </AuthLayout>;
}
