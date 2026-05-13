import type { Component } from 'solid-js'
import { FiGithub } from 'solid-icons/fi'
import { config } from '../config'

const Nav: Component = () => {
  return (
    <header class="sticky top-0 z-50 w-full">
      <div class="mx-auto max-w-7xl px-6 pt-4">
        <nav class="glass flex items-center justify-between rounded-2xl px-4 py-3">
          <a href="#top" class="flex items-center gap-2">
            <span class="inline-block h-7 w-7 rounded-lg bg-gradient-to-br from-violet-500 via-cyan-400 to-pink-400" />
            <span class="font-semibold tracking-tight">k8s-vm-orchestrator</span>
          </a>
          <div class="hidden items-center gap-1 text-sm text-slate-300 md:flex">
            <a href="#why" class="rounded-md px-3 py-1.5 hover:bg-white/5">Why</a>
            <a href="#architecture" class="rounded-md px-3 py-1.5 hover:bg-white/5">Architecture</a>
            <a href="#features" class="rounded-md px-3 py-1.5 hover:bg-white/5">Features</a>
            <a href="#api" class="rounded-md px-3 py-1.5 hover:bg-white/5">API</a>
            <a href="#quickstart" class="rounded-md px-3 py-1.5 hover:bg-white/5">Quick start</a>
          </div>
          <a
            href={config.githubUrl}
            target="_blank"
            rel="noopener noreferrer"
            class="flex items-center gap-2 rounded-xl border border-white/10 bg-white/5 px-3 py-1.5 text-sm transition hover:bg-white/10"
          >
            <FiGithub size={16} />
            <span class="hidden sm:inline">GitHub</span>
          </a>
        </nav>
      </div>
    </header>
  )
}

export default Nav
