import type { Component } from 'solid-js'
import { FiX, FiCheck } from 'solid-icons/fi'

const Why: Component = () => {
  return (
    <section id="why" class="mx-auto max-w-7xl px-6 py-20">
      <div class="mx-auto max-w-3xl text-center">
        <p class="text-xs font-semibold uppercase tracking-[0.2em] text-violet-300">Why</p>
        <h2 class="mt-3 text-4xl font-semibold tracking-tight md:text-5xl">
          Kubernetes is great at containers. Not everything is a container.
        </h2>
        <p class="mt-5 text-lg text-slate-300">
          Windows-only tooling, GUI-bound tests, legacy installers — workloads that have to run on a full VM.
          This project treats VMs as a first-class Kubernetes resource and gives you the same declarative,
          queue-backed, parallel execution model you already use for pods.
        </p>
      </div>

      <div class="mt-14 grid grid-cols-1 gap-4 md:grid-cols-2">
        <div class="glass rounded-2xl p-6">
          <div class="mb-4 inline-flex items-center gap-2 text-rose-300">
            <FiX /> <span class="text-sm font-semibold uppercase tracking-wider">Without</span>
          </div>
          <ul class="space-y-3 text-slate-300">
            <li class="flex gap-3"><span class="mt-1 h-1.5 w-1.5 shrink-0 rounded-full bg-rose-400" />Hand-rolled job queues, polling agents, brittle PowerShell glue.</li>
            <li class="flex gap-3"><span class="mt-1 h-1.5 w-1.5 shrink-0 rounded-full bg-rose-400" />No visibility once a job leaves your CI — no <code class="font-mono text-xs">kubectl get</code> equivalent.</li>
            <li class="flex gap-3"><span class="mt-1 h-1.5 w-1.5 shrink-0 rounded-full bg-rose-400" />VM utilization swings between idle and overloaded; no fair scheduling.</li>
            <li class="flex gap-3"><span class="mt-1 h-1.5 w-1.5 shrink-0 rounded-full bg-rose-400" />Connection setup on every job — 10s WinRM handshake adds up fast.</li>
          </ul>
        </div>
        <div class="glass rounded-2xl p-6">
          <div class="mb-4 inline-flex items-center gap-2 text-emerald-300">
            <FiCheck /> <span class="text-sm font-semibold uppercase tracking-wider">With k8s-vm-orchestrator</span>
          </div>
          <ul class="space-y-3 text-slate-300">
            <li class="flex gap-3"><span class="mt-1 h-1.5 w-1.5 shrink-0 rounded-full bg-emerald-400" />Submit <code class="font-mono text-xs">VMJob</code> CRs the same way you submit Pods.</li>
            <li class="flex gap-3"><span class="mt-1 h-1.5 w-1.5 shrink-0 rounded-full bg-emerald-400" />Native <code class="font-mono text-xs">kubectl get vmjobs</code> with status, duration, assigned VM.</li>
            <li class="flex gap-3"><span class="mt-1 h-1.5 w-1.5 shrink-0 rounded-full bg-emerald-400" />A controller fans jobs out across the VM pool with selectors and tags.</li>
            <li class="flex gap-3"><span class="mt-1 h-1.5 w-1.5 shrink-0 rounded-full bg-emerald-400" />Pooled WinRM connections — handshake amortized across many jobs.</li>
          </ul>
        </div>
      </div>
    </section>
  )
}

export default Why
