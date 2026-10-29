"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { FormEvent, useState } from "react";
import AuthLayout, { buttonClass, inputClass, messageClass } from "../components/AuthLayout";

export default function LoginPage() {
  const router = useRouter();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError("");
    try {
      const response = await fetch("/api/session/login", {
        method: "POST", headers: { "content-type": "application/json" },
        credentials: "same-origin", body: JSON.stringify({ email, password }),
      });
      if (!response.ok) {
        setError(response.status === 401 ? "Invalid credentials or unverified email." : "Unable to sign in. Try again.");
        return;
      }
      router.replace("/dashboard");
    } catch { setError("Connection unavailable. Try again."); }
    finally { setBusy(false); }
  }
  return (
    <AuthLayout title="Welcome back" subtitle="Sign in to your secure AI workspace.">
      <form onSubmit={submit} className="space-y-5">
        <label className="block text-sm font-medium">Email
          <input className={inputClass} type="email" autoComplete="username" value={email}
            onChange={e => setEmail(e.target.value)} required maxLength={254} />
        </label>
        <label className="block text-sm font-medium">Password
          <input className={inputClass} type="password" autoComplete="current-password" value={password}
            onChange={e => setPassword(e.target.value)} required />
        </label>
        <button className={buttonClass} type="submit" disabled={busy}>{busy ? "Signing in…" : "Sign in"}</button>
      </form>
      {error && <p role="alert" className={messageClass}>{error}</p>}
      <div className="mt-7 flex justify-between text-sm text-cyan-300">
        <Link href="/forgot-password">Forgot password?</Link>
        <Link href="/register">Request account</Link>
      </div>
    </AuthLayout>
  );
}
