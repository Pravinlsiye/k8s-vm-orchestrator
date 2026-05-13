import type { Component } from 'solid-js'
import { FiGithub, FiBookOpen } from 'solid-icons/fi'
import { config } from '../config'

const Footer: Component = () => {
  return (
    <footer class="border-t border-white/5">
      <div class="mx-auto flex max-w-7xl flex-col items-center justify-between gap-4 px-6 py-10 md:flex-row">
        <div class="flex items-center gap-3 text-sm text-slate-400">
          <span class="inline-block h-6 w-6 rounded-md bg-gradient-to-br from-violet-500 via-cyan-400 to-pink-400" />
          <span class="font-semibold text-slate-200">{config.projectName}</span>
          <span>·</span>
          <span>MIT</span>
        </div>
        <div class="flex items-center gap-2">
          <a
            href={config.githubUrl}
            target="_blank"
            rel="noopener noreferrer"
            class="inline-flex items-center gap-2 rounded-lg border border-white/10 bg-white/5 px-3 py-1.5 text-sm text-slate-300 transition hover:bg-white/10"
          >
            <FiGithub /> GitHub
          </a>
          <a
            href={config.docsUrl}
            target="_blank"
            rel="noopener noreferrer"
            class="inline-flex items-center gap-2 rounded-lg border border-white/10 bg-white/5 px-3 py-1.5 text-sm text-slate-300 transition hover:bg-white/10"
          >
            <FiBookOpen /> Docs
          </a>
        </div>
      </div>
    </footer>
  )
}

export default Footer
