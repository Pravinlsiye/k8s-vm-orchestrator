import type { Component } from 'solid-js'

const Box: Component<{ title: string; subtitle?: string; tone?: 'violet' | 'cyan' | 'pink' | 'slate' }>
  = (props) => {
    const tones: Record<string, string> = {
      violet: 'from-violet-500/30 to-violet-500/5 border-violet-400/30',
      cyan: 'from-cyan-400/30 to-cyan-400/5 border-cyan-300/30',
      pink: 'from-pink-500/30 to-pink-500/5 border-pink-400/30',
      slate: 'from-slate-400/20 to-slate-400/5 border-slate-300/20',
    }
    const tone = tones[props.tone ?? 'slate']
    return (
      <div class={`relative rounded-xl border bg-gradient-to-br ${tone} px-4 py-3 backdrop-blur`}>
        <div class="text-sm font-semibold text-slate-100">{props.title}</div>
        {props.subtitle && (
          <div class="mt-0.5 font-mono text-[11px] text-slate-300/80">{props.subtitle}</div>
        )}
      </div>
    )
  }

const Arrow: Component<{ label?: string; vertical?: boolean }> = (props) => (
  <div class={props.vertical ? 'flex flex-col items-center py-2' : 'flex items-center gap-2 px-2'}>
    <div class={props.vertical ? 'h-8 w-px bg-gradient-to-b from-white/40 to-white/0' : 'h-px w-8 bg-gradient-to-r from-white/40 to-white/0'} />
    {props.label && (
      <span class="font-mono text-[10px] uppercase tracking-wider text-slate-400">
        {props.label}
      </span>
    )}
    <div class={props.vertical ? 'h-8 w-px bg-gradient-to-b from-white/0 to-white/40' : 'h-px w-8 bg-gradient-to-r from-white/0 to-white/40'} />
  </div>
)

const Architecture: Component = () => {
  return (
    <section id="architecture" class="relative mx-auto max-w-7xl px-6 py-20">
      <div class="mx-auto max-w-3xl text-center">
        <p class="text-xs font-semibold uppercase tracking-[0.2em] text-cyan-300">Architecture</p>
        <h2 class="mt-3 text-4xl font-semibold tracking-tight md:text-5xl">Three pieces. No magic.</h2>
        <p class="mt-5 text-lg text-slate-300">
          An API generates the PowerShell. A controller dispatches it. A pool of VMs runs it.
        </p>
      </div>

      <div class="mt-14 glass-strong rounded-3xl p-6 md:p-10">
        <div class="flex flex-col items-stretch gap-6">
          <div class="flex justify-center">
            <Box title="Client" subtitle="curl · kubectl · CI" />
          </div>
          <Arrow vertical label="HTTP / REST" />
          <div class="flex justify-center">
            <Box tone="violet" title="VMJob API" subtitle="src/Api · .NET 8" />
          </div>
          <Arrow vertical label="Kubernetes API" />
          <div class="rounded-2xl border border-white/10 bg-white/[0.02] p-6">
            <div class="mb-4 text-center font-mono text-[11px] uppercase tracking-wider text-slate-400">
              Kubernetes cluster
            </div>
            <div class="flex flex-col items-stretch gap-4">
              <div class="flex justify-center">
                <Box tone="cyan" title="VMJob Controller" subtitle="reconcile · monitor · vm-state" />
              </div>
              <Arrow vertical label="WinRM" />
              <div class="grid grid-cols-2 gap-3 md:grid-cols-4">
                <Box tone="pink" title="VMNode 1" subtitle="windows · idle" />
                <Box tone="pink" title="VMNode 2" subtitle="windows · busy" />
                <Box tone="pink" title="VMNode 3" subtitle="windows · idle" />
                <Box tone="pink" title="VMNode N" subtitle="…" />
              </div>
            </div>
          </div>
        </div>

        <div class="mt-10 grid grid-cols-1 gap-4 text-sm text-slate-300 md:grid-cols-3">
          <Loop title="Reconcile" period="5s" desc="Find pending VMJobs, match free VMNodes, dispatch." />
          <Loop title="Monitor" period="3s" desc="Poll running jobs, update status, clean up completed." />
          <Loop title="VM state" period="30s" desc="Refresh heartbeat / availability, recover stuck VMs." />
        </div>
      </div>
    </section>
  )
}

const Loop: Component<{ title: string; period: string; desc: string }> = (props) => (
  <div class="rounded-xl border border-white/10 bg-white/[0.03] p-4">
    <div class="flex items-baseline justify-between">
      <h4 class="font-semibold">{props.title}</h4>
      <span class="font-mono text-xs text-slate-400">{props.period}</span>
    </div>
    <p class="mt-2 text-sm text-slate-400">{props.desc}</p>
  </div>
)

export default Architecture
