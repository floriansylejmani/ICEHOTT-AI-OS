import Link from "next/link";
import type { ReactNode } from "react";
export default function AuthLayout({ title, subtitle, children }: {
  title: string; subtitle: string; children: ReactNode
}) {
  return <main className="min-h-screen bg-[#070d16] text-slate-100">
    <header className="mx-auto flex max-w-5xl items-center justify-between border-b border-white/10 px-6 py-7">
      <Link href="/" className="text-lg font-bold tracking-tight text-cyan-300">ICEHOTT <span className="font-normal text-slate-300">AI OS</span></Link>
      <span className="rounded-full border border-white/15 px-3 py-1 text-xs text-slate-400">Secure workspace</span>
    </header>
    <section className="mx-auto flex max-w-lg flex-col justify-center px-6 py-20">
      <div className="rounded-3xl border border-white/10 bg-white/[.04] p-8 shadow-[0_30px_100px_rgba(0,0,0,.25)] sm:p-10">
        <h1 className="text-3xl font-semibold tracking-tight">{title}</h1>
        <p className="mt-3 text-sm leading-6 text-slate-400">{subtitle}</p>
        <div className="mt-8">{children}</div>
      </div>
    </section>
  </main>;
}
export const inputClass = "mt-2 w-full rounded-xl border border-white/20 bg-[#101927] px-4 py-3 text-white outline-none focus:border-cyan-300 focus:ring-2 focus:ring-cyan-300/20";
export const buttonClass = "w-full rounded-xl bg-cyan-300 px-5 py-3 font-semibold text-slate-950 hover:bg-cyan-200 disabled:cursor-not-allowed disabled:opacity-50";
export const messageClass = "mt-5 rounded-xl border border-white/15 bg-white/5 p-4 text-sm leading-6 text-slate-200";
