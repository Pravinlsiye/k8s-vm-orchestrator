import { createSignal, onMount, For, type Component } from 'solid-js'
import { createHighlighterCore } from 'shiki/core'
import { createOnigurumaEngine } from 'shiki/engine/oniguruma'

type Example = {
  id: string
  label: string
  lang: 'json' | 'yaml' | 'powershell'
  code: string
  note: string
}

const examples: Example[] = [
  {
    id: 'single',
    label: 'Single command',
    lang: 'json',
    note: 'POST /api/vmjobs — minimal form.',
    code: `{
  "command": "Get-Date"
}`,
  },
  {
    id: 'multi',
    label: 'Multi-command',
    lang: 'json',
    note: 'Run a sequence of PowerShell commands.',
    code: `{
  "name": "setup",
  "commands": [
    "New-Item -Path C:\\\\app -ItemType Directory -Force",
    "Write-Output 'ready' | Out-File C:\\\\app\\\\status.txt"
  ]
}`,
  },
  {
    id: 'stages',
    label: 'Stage workflow',
    lang: 'json',
    note: 'Variables, dependencies, finally block.',
    code: `{
  "name": "deploy-app",
  "variables": { "appName": "MyApp", "version": "1.0.0" },
  "stages": [
    {
      "name": "prepare",
      "steps": [
        { "task": "PowerShell",
          "inputs": { "script": "New-Item -Path 'C:\\\\Apps\\\\$(variables.appName)' -ItemType Directory -Force" } }
      ]
    },
    {
      "name": "deploy",
      "dependsOn": ["prepare"],
      "steps": [
        { "task": "PowerShell",
          "workingDirectory": "C:\\\\Apps\\\\$(variables.appName)",
          "inputs": { "script": "Write-Host 'Deploying $(variables.appName) v$(variables.version)'" } }
      ]
    }
  ],
  "finally": {
    "steps": [
      { "task": "PowerShell", "inputs": { "script": "Write-Host 'cleanup'" } }
    ]
  }
}`,
  },
  {
    id: 'yaml',
    label: 'Direct CRD',
    lang: 'yaml',
    note: 'Skip the API — apply a VMJob YAML straight to the cluster.',
    code: `apiVersion: orchestrator.vmjobs.io/v1
kind: VMJob
metadata:
  name: hello-vm
spec:
  vmSelector:
    os: windows
    tags: { role: worker }
  command: powershell.exe
  args: ["-Command", "Write-Host \\\"Hello from $env:COMPUTERNAME\\\""]
  timeout: "2m"`,
  },
  {
    id: 'ps',
    label: 'PowerShell helper',
    lang: 'powershell',
    note: 'scripts/tests/submit-job.ps1 — submit + tail status.',
    code: `./scripts/tests/submit-job.ps1 -Command "Get-Date" -Wait

./scripts/tests/submit-job.ps1 \`
  -JobFile examples/api/stage-job.json \`
  -VMSelector '{"os":"windows","tags":{"role":"build"}}' \`
  -Wait`,
  },
]

const CodeExamples: Component = () => {
  const [active, setActive] = createSignal(examples[0].id)
  const [highlighted, setHighlighted] = createSignal<Record<string, string>>({})

  onMount(async () => {
    // Load only the grammars + theme we actually use to keep the bundle small.
    const highlighter = await createHighlighterCore({
      themes: [import('@shikijs/themes/github-dark-default')],
      langs: [
        import('@shikijs/langs/json'),
        import('@shikijs/langs/yaml'),
        import('@shikijs/langs/powershell'),
      ],
      engine: createOnigurumaEngine(import('shiki/wasm')),
    })
    const map: Record<string, string> = {}
    for (const e of examples) {
      map[e.id] = highlighter.codeToHtml(e.code, {
        lang: e.lang,
        theme: 'github-dark-default',
      })
    }
    setHighlighted(map)
  })

  const current = () => examples.find((e) => e.id === active())!

  return (
    <section id="api" class="mx-auto max-w-7xl px-6 py-20">
      <div class="mx-auto max-w-3xl text-center">
        <p class="text-xs font-semibold uppercase tracking-[0.2em] text-violet-300">API</p>
        <h2 class="mt-3 text-4xl font-semibold tracking-tight md:text-5xl">
          One job. Five ways to ship it.
        </h2>
        <p class="mt-5 text-lg text-slate-300">
          Pick the simplest format that fits. Scale up to full stage-based workflows when you need them.
        </p>
      </div>

      <div class="mt-12 grid grid-cols-1 gap-6 lg:grid-cols-12">
        <div class="lg:col-span-3">
          <div class="glass rounded-2xl p-2">
            <For each={examples}>
              {(e) => (
                <button
                  type="button"
                  onClick={() => setActive(e.id)}
                  class={`flex w-full items-center justify-between rounded-xl px-3 py-2 text-left text-sm transition ${
                    active() === e.id
                      ? 'bg-white/10 text-slate-100'
                      : 'text-slate-400 hover:bg-white/5 hover:text-slate-200'
                  }`}
                >
                  <span class="font-medium">{e.label}</span>
                  <span class="font-mono text-[10px] uppercase tracking-wider text-slate-500">{e.lang}</span>
                </button>
              )}
            </For>
          </div>
        </div>

        <div class="lg:col-span-9">
          <div class="glass-strong overflow-hidden rounded-2xl">
            <div class="flex items-center justify-between border-b border-white/5 px-5 py-3">
              <div class="flex items-center gap-2">
                <div class="flex gap-1.5">
                  <span class="h-2.5 w-2.5 rounded-full bg-rose-400/70" />
                  <span class="h-2.5 w-2.5 rounded-full bg-amber-300/70" />
                  <span class="h-2.5 w-2.5 rounded-full bg-emerald-400/70" />
                </div>
                <span class="ml-2 font-mono text-xs text-slate-400">{current().label}</span>
              </div>
              <span class="font-mono text-[11px] text-slate-500">{current().note}</span>
            </div>
            <div
              class="code-block"
              innerHTML={highlighted()[current().id] ?? `<pre>${escapeHtml(current().code)}</pre>`}
            />
          </div>
        </div>
      </div>
    </section>
  )
}

function escapeHtml(s: string) {
  return s.replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' })[c]!)
}

export default CodeExamples
