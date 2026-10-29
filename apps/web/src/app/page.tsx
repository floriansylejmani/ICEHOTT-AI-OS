const modules = [
  { number: "01", title: "Identity & Workspaces", detail: "Secure accounts, roles, isolated teams", phase: "Phase 1" },
  { number: "02", title: "AI Copilot", detail: "Provider-backed chat with controlled access", phase: "Phase 3" },
  { number: "03", title: "Knowledge & RAG", detail: "Documents, embeddings and grounded answers", phase: "Phase 4" },
  { number: "04", title: "Tools & Agents", detail: "Permissioned execution and human approvals", phase: "Phase 5" },
  { number: "05", title: "Workflow Studio", detail: "Durable automations and step-by-step runs", phase: "Phase 6" },
  { number: "06", title: "SaaS & Billing", detail: "Verified plans, usage and optional checkout", phase: "Phases 8–9" },
] as const;

export default function Home() {
  return (
    <main className="min-h-screen bg-[#070d16] text-[#eff4fc]">
      <div className="pointer-events-none fixed inset-0 opacity-40" aria-hidden="true"
        style={{ backgroundImage: "radial-gradient(ellipse at 70% 5%, rgba(51,122,255,.16), transparent 44%), radial-gradient(ellipse at 10% 70%, rgba(23,203,167,.08), transparent 40%)" }} />
      <div className="relative mx-auto w-full max-w-7xl px-5 sm:px-8 lg:px-12">
        <header className="flex items-center justify-between border-b border-white/10 py-7">
          <a href="#" className="flex items-center gap-3" aria-label="ICEHOTT AI OS home">
            <span className="flex size-10 items-center justify-center rounded-xl border border-cyan-300/30 bg-cyan-300/10 text-sm font-black text-cyan-300">I/O</span>
            <span className="text-lg font-bold tracking-tight">ICEHOTT <span className="font-medium text-slate-400">AI OS</span></span>
          </a>
          <nav className="hidden gap-7 text-sm text-slate-400 sm:flex" aria-label="Main navigation">
            <a href="#platform" className="transition hover:text-white">Platform</a>
            <a href="#roadmap" className="transition hover:text-white">Roadmap</a>
            <a href="#principles" className="transition hover:text-white">Principles</a>
          </nav>
          <span className="rounded-full border border-emerald-300/25 bg-emerald-300/10 px-3 py-1.5 text-xs font-semibold text-emerald-300">
            Phase 0 · Foundation
          </span>
        </header>

        <section className="grid gap-10 pb-20 pt-24 lg:grid-cols-[1.25fr_.75fr] lg:items-center lg:pb-28 lg:pt-32">
          <div>
            <div className="mb-7 inline-flex items-center gap-3 rounded-full border border-white/10 bg-white/5 px-4 py-2 text-xs font-medium uppercase tracking-[.18em] text-slate-300">
              <span className="size-2 rounded-full bg-cyan-300" /> New architecture · Built from zero
            </div>
            <h1 className="max-w-4xl text-5xl font-semibold leading-[1.08] tracking-[-.055em] sm:text-6xl lg:text-[5.25rem]">
              One workspace.<br />
              <span className="bg-gradient-to-r from-cyan-300 via-blue-400 to-violet-400 bg-clip-text text-transparent">Unlimited potential.</span>
            </h1>
            <p className="mt-7 max-w-xl text-lg leading-8 text-slate-400">
              An AI operating system for teams: knowledge, agents, tools, workflows and artifacts — built on secure, tenant-isolated foundations.
            </p>
            <div className="mt-9 flex flex-wrap items-center gap-4">
              <a href="#platform" className="rounded-xl bg-cyan-300 px-6 py-3 font-semibold text-slate-950 transition hover:bg-cyan-200">Explore the platform</a>
              <a href="#roadmap" className="rounded-xl border border-white/15 px-6 py-3 font-semibold text-slate-200 transition hover:border-white/40 hover:bg-white/5">View build roadmap</a>
              <a href="/login" className="rounded-xl border border-cyan-300/30 px-6 py-3 font-semibold text-cyan-200 transition hover:bg-cyan-300/10">Sign in</a><a href="/dashboard" className="rounded-xl border border-white/15 px-6 py-3 font-semibold text-slate-200 transition hover:bg-white/10">Dashboard</a>
            </div>
            <p className="mt-6 text-xs text-slate-500">Development preview only. Authentication, AI actions and paid billing are not yet enabled.</p>
          </div>
          <div className="relative overflow-hidden rounded-[2rem] border border-white/10 bg-white/[.035] p-6 shadow-[0_24px_100px_rgba(0,0,0,.26)] sm:p-8">
            <div className="mb-8 flex items-center justify-between">
              <span className="text-sm font-semibold text-slate-200">Build overview</span>
              <span className="rounded-md bg-white/10 px-2.5 py-1 text-[11px] text-slate-300">V2.0 · Preview</span>
            </div>
            <div className="mb-4 rounded-2xl border border-cyan-400/20 bg-cyan-300/[.06] p-5">
              <div className="mb-4 flex items-center gap-3"><span className="size-2 rounded-full bg-cyan-300" /><span className="text-sm text-cyan-200">Currently building</span></div>
              <p className="text-2xl font-semibold">Platform Foundation</p>
              <p className="mt-2 text-sm text-slate-400">Clean API architecture, AI runtime, frontend and local infrastructure.</p>
              <div className="mt-5 h-2 overflow-hidden rounded-full bg-white/10"><div className="h-full w-[8%] rounded-full bg-gradient-to-r from-cyan-300 to-blue-400" /></div>
              <p className="mt-3 text-right text-xs text-slate-400">Phase 0 of 12 · illustrative progress</p>
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div className="rounded-xl border border-white/10 bg-white/5 p-4"><p className="text-2xl font-semibold">12</p><p className="mt-1 text-xs text-slate-400">Planned phases</p></div>
              <div className="rounded-xl border border-white/10 bg-white/5 p-4"><p className="text-2xl font-semibold">3</p><p className="mt-1 text-xs text-slate-400">Primary runtimes</p></div>
            </div>
          </div>
        </section>

        <section id="platform" className="border-t border-white/10 py-20">
          <div className="mb-10 flex flex-col justify-between gap-5 sm:flex-row sm:items-end">
            <div><p className="mb-3 text-xs font-bold uppercase tracking-[.2em] text-cyan-300">The platform</p><h2 className="text-3xl font-semibold tracking-tight sm:text-4xl">Everything, designed to work together.</h2></div>
            <p className="max-w-sm text-sm leading-6 text-slate-400">Modules below represent the planned product. Their features are not yet available in this foundation preview.</p>
          </div>
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            {modules.map((item) => (
              <article key={item.number} className="group min-h-52 rounded-2xl border border-white/10 bg-white/[.025] p-6 transition hover:border-cyan-300/30 hover:bg-white/[.04]">
                <div className="flex items-center justify-between"><span className="font-mono text-sm text-cyan-300">{item.number} /</span><span className="rounded-md border border-white/10 px-2.5 py-1 text-[11px] text-slate-400">Planned</span></div>
                <h3 className="mt-9 text-xl font-semibold">{item.title}</h3>
                <p className="mt-2 text-sm text-slate-400">{item.detail}</p>
                <p className="mt-5 text-xs font-medium text-cyan-300/90">{item.phase}</p>
              </article>
            ))}
          </div>
        </section>
        <section id="roadmap" className="border-t border-white/10 py-16">
          <p className="mb-3 text-xs font-bold uppercase tracking-[.2em] text-cyan-300">Build roadmap</p>
          <h2 className="text-3xl font-semibold tracking-tight">Foundation first. Features second.</h2>
          <div className="mt-8 grid gap-3 md:grid-cols-3">
            <div className="rounded-xl border border-emerald-400/25 bg-emerald-400/5 p-5"><p className="text-sm font-semibold text-emerald-300">NOW · Phase 0</p><p className="mt-2 text-slate-300">Architecture and automated tests</p></div>
            <div className="rounded-xl border border-white/10 bg-white/[.025] p-5"><p className="text-sm font-semibold text-slate-200">NEXT · Phases 1–7</p><p className="mt-2 text-slate-400">Identity, AI, RAG, tools and workflows</p></div>
            <div className="rounded-xl border border-white/10 bg-white/[.025] p-5"><p className="text-sm font-semibold text-slate-200">LATER · Phases 8–12</p><p className="mt-2 text-slate-400">SaaS, optional payments and production readiness</p></div>
          </div>
        </section>
        <footer id="principles" className="flex flex-col gap-3 border-t border-white/10 py-8 text-xs text-slate-500 sm:flex-row sm:justify-between">
          <p>© ICEHOTT AI OS · Clean rebuild</p><p>Private by design · Secure by default · No live charges</p>
        </footer>
      </div>
    </main>
  );
}
