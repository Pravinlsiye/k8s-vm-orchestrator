import { onMount, type Component } from 'solid-js'
import { animate, stagger } from 'motion'
import { FiArrowRight, FiBookOpen } from 'solid-icons/fi'
import { config } from '../config'

const Hero: Component = () => {
  let badgeRef!: HTMLDivElement
  let h1Ref!: HTMLHeadingElement
  let pRef!: HTMLParagraphElement
  let ctaRef!: HTMLDivElement
  let cardRef!: HTMLDivElement

  onMount(() => {
    animate(
      [badgeRef, h1Ref, pRef, ctaRef],
      { opacity: [0, 1], y: [16, 0] },
      { duration: 0.7, delay: stagger(0.08), ease: [0.22, 1, 0.36, 1] },
    )
    animate(
      cardRef,
      { opacity: [0, 1], y: [24, 0], scale: [0.98, 1] },
      { duration: 0.9, delay: 0.3, ease: [0.22, 1, 0.36, 1] },
    )
  })

  return (
    <section id="top" class="relative mx-auto max-w-7xl px-6 pt-20 pb-24 md:pt-28 md:pb-32">
      <div
        aria-hidden
        class="pointer-events-none absolute inset-x-0 -top-10 -z-10 flex justify-center"
      >
        <div class="h-[420px] w-[820px] rounded-full bg-gradient-to-tr from-violet-600/30 via-cyan-400/20 to-pink-500/25 blur-3xl" />
      </div>

      <div class="grid grid-cols-1 items-center gap-10 lg:grid-cols-12">
        <div class="lg:col-span-7">
          <div ref={badgeRef!} class="glass inline-flex items-center gap-2 rounded-full px-3 py-1 text-xs text-slate-300 opacity-0">
            <span class="h-2 w-2 rounded-full bg-emerald-400" />
            <span>Open source · MIT</span>
            <span class="text-slate-500">·</span>
            <span class="font-mono text-[10px] uppercase tracking-wider text-slate-400">v0.1</span>
          </div>

          <h1
            ref={h1Ref!}
            class="mt-6 font-display text-5xl font-semibold tracking-tight md:text-6xl lg:text-7xl opacity-0"
          >
            <span class="block">Kubernetes-native</span>
            <span class="block">jobs on <span class="gradient-text">Windows VMs</span>.</span>
          </h1>

          <p
            ref={pRef!}
            class="mt-6 max-w-2xl text-lg leading-relaxed text-slate-300 opacity-0"
          >
            {config.description}
          </p>

          <div ref={ctaRef!} class="mt-9 flex flex-wrap items-center gap-3 opacity-0">
            <a
              href="#quickstart"
              class="group inline-flex items-center gap-2 rounded-xl bg-gradient-to-tr from-violet-500 to-cyan-400 px-5 py-3 text-sm font-semibold text-slate-950 transition hover:shadow-[0_10px_40px_-10px_rgba(124,92,255,0.7)]"
            >
              Get started
              <FiArrowRight class="transition group-hover:translate-x-0.5" />
            </a>
            <a
              href={config.docsUrl}
              target="_blank"
              rel="noopener noreferrer"
              class="inline-flex items-center gap-2 rounded-xl border border-white/10 bg-white/5 px-5 py-3 text-sm font-medium text-slate-200 transition hover:bg-white/10"
            >
              <FiBookOpen />
              Read the docs
            </a>
          </div>

          <dl class="mt-12 grid max-w-xl grid-cols-3 gap-6 text-sm">
            <Stat label="Concurrent VMs" value="∞" sub="bounded by pool" />
            <Stat label="Job assign latency" value="< 5s" sub="reconcile loop" />
            <Stat label="WinRM handshake" value="~1s" sub="pooled" />
          </dl>
        </div>

        <div class="lg:col-span-5">
          <div ref={cardRef!} class="glass-strong relative rounded-2xl p-1 opacity-0 ring-glow">
            <div class="rounded-[14px] bg-[#0c0c14]/80 p-5">
              <div class="flex items-center gap-2 border-b border-white/5 pb-3">
                <div class="flex gap-1.5">
                  <span class="h-2.5 w-2.5 rounded-full bg-rose-400/70" />
                  <span class="h-2.5 w-2.5 rounded-full bg-amber-300/70" />
                  <span class="h-2.5 w-2.5 rounded-full bg-emerald-400/70" />
                </div>
                <span class="ml-2 font-mono text-xs text-slate-400">POST /api/vmjobs</span>
              </div>
              <pre class="mt-3 overflow-x-auto font-mono text-[12.5px] leading-relaxed text-slate-200">
{`{
  "name": "build-and-test",
  "vmSelector": { "os": "windows", "tags": { "role": "build" } },
  "stages": [
    { "name": "build", "steps": [
        { "task": "PowerShell",
          "inputs": { "script": "msbuild /t:Build" } } ] },
    { "name": "test",  "dependsOn": ["build"], "steps": [
        { "task": "PowerShell",
          "inputs": { "script": "vstest.console **\\*.Tests.dll" } } ] }
  ]
}`}
              </pre>
              <div class="mt-3 flex items-center justify-between border-t border-white/5 pt-3">
                <span class="font-mono text-[11px] text-emerald-400">→ 201 Created · phase: Pending</span>
                <span class="font-mono text-[11px] text-slate-400">assigned: vm-build-02</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  )
}

const Stat: Component<{ label: string; value: string; sub: string }> = (props) => (
  <div>
    <dt class="text-xs uppercase tracking-wider text-slate-500">{props.label}</dt>
    <dd class="mt-1 text-2xl font-semibold text-slate-100">{props.value}</dd>
    <dd class="text-xs text-slate-400">{props.sub}</dd>
  </div>
)

export default Hero
