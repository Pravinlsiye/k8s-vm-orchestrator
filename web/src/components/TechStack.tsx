import { For, type Component } from 'solid-js'

const stack = [
  { label: '.NET 8', sub: 'API + Controller' },
  { label: 'Kubernetes', sub: 'CRDs + RBAC' },
  { label: 'WinRM', sub: 'VM execution' },
  { label: 'PowerShell', sub: 'job runtime' },
  { label: 'Azure / AKS', sub: 'reference infra' },
  { label: 'Terraform', sub: 'provisioning' },
  { label: 'xUnit · Moq', sub: 'unit tests' },
]

const TechStack: Component = () => {
  return (
    <section class="mx-auto max-w-7xl px-6 pb-20">
      <div class="glass rounded-2xl p-6">
        <div class="flex flex-col items-start justify-between gap-4 md:flex-row md:items-center">
          <div>
            <p class="text-xs font-semibold uppercase tracking-[0.2em] text-violet-300">Stack</p>
            <h3 class="mt-2 text-2xl font-semibold">Built on what already works.</h3>
          </div>
          <p class="max-w-md text-sm text-slate-400">
            No new query language, no new agent protocol. PowerShell, Kubernetes APIs, WinRM. Glue, not reinvention.
          </p>
        </div>
        <div class="mt-6 flex flex-wrap gap-2">
          <For each={stack}>
            {(t) => (
              <span class="inline-flex items-baseline gap-2 rounded-full border border-white/10 bg-white/5 px-3 py-1.5 text-sm">
                <span class="font-semibold text-slate-100">{t.label}</span>
                <span class="text-xs text-slate-400">{t.sub}</span>
              </span>
            )}
          </For>
        </div>
      </div>
    </section>
  )
}

export default TechStack
