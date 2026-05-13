import type { Component, JSX } from 'solid-js'
import { For } from 'solid-js'
import {
  FiGitBranch,
  FiZap,
  FiTarget,
  FiClock,
  FiShield,
  FiLayers,
} from 'solid-icons/fi'

type Feature = {
  icon: () => JSX.Element
  title: string
  body: string
  accent: 'violet' | 'cyan' | 'pink'
}

const features: Feature[] = [
  {
    icon: () => <FiGitBranch size={20} />,
    title: 'Stage-based workflows',
    body: 'Variables, environment scoping, parallel + dependent stages, finally blocks. Like a CI pipeline but inside the cluster.',
    accent: 'violet',
  },
  {
    icon: () => <FiZap size={20} />,
    title: 'Parallel by default',
    body: 'One job per VM at a time. Dispatch is non-blocking — fast VMs naturally pick up more work.',
    accent: 'cyan',
  },
  {
    icon: () => <FiTarget size={20} />,
    title: 'VM selectors',
    body: 'Target by nodeName, OS, or tag set. When many match, the controller picks the lowest-loaded VM.',
    accent: 'pink',
  },
  {
    icon: () => <FiClock size={20} />,
    title: 'Duration tracking',
    body: 'Real startTime / completionTime / duration on every job, shown in `kubectl get vmjob` directly.',
    accent: 'violet',
  },
  {
    icon: () => <FiLayers size={20} />,
    title: 'Connection pooling',
    body: 'WinRM connections are pooled per-VM. Handshake cost drops from ~10s to <1s.',
    accent: 'cyan',
  },
  {
    icon: () => <FiShield size={20} />,
    title: 'Credentials done right',
    body: 'Never in YAML. Read from a Kubernetes secret or the VM_ADMIN_PASSWORD env var.',
    accent: 'pink',
  },
]

const accentClasses: Record<Feature['accent'], string> = {
  violet: 'from-violet-500/20 to-violet-500/0 text-violet-300',
  cyan: 'from-cyan-400/20 to-cyan-400/0 text-cyan-300',
  pink: 'from-pink-500/20 to-pink-500/0 text-pink-300',
}

const Features: Component = () => {
  return (
    <section id="features" class="mx-auto max-w-7xl px-6 py-20">
      <div class="mx-auto max-w-3xl text-center">
        <p class="text-xs font-semibold uppercase tracking-[0.2em] text-pink-300">Features</p>
        <h2 class="mt-3 text-4xl font-semibold tracking-tight md:text-5xl">
          Built for production, scoped for clarity.
        </h2>
        <p class="mt-5 text-lg text-slate-300">
          Every feature exists to solve a real pain in running Windows workloads on Kubernetes.
        </p>
      </div>

      <div class="mt-14 grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
        <For each={features}>
          {(f) => (
            <div class="glass group relative overflow-hidden rounded-2xl p-6 transition hover:-translate-y-0.5 hover:border-white/20">
              <div
                aria-hidden
                class={`pointer-events-none absolute -right-12 -top-12 h-40 w-40 rounded-full bg-gradient-to-br ${accentClasses[f.accent]} blur-2xl opacity-60`}
              />
              <div class={`inline-flex h-10 w-10 items-center justify-center rounded-xl bg-white/5 ${accentClasses[f.accent].split(' ').pop()}`}>
                {f.icon()}
              </div>
              <h3 class="mt-4 text-lg font-semibold text-slate-100">{f.title}</h3>
              <p class="mt-2 text-sm leading-relaxed text-slate-400">{f.body}</p>
            </div>
          )}
        </For>
      </div>
    </section>
  )
}

export default Features
