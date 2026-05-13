import { createSignal, type Component } from 'solid-js'
import { FiCopy, FiCheck } from 'solid-icons/fi'

type Step = {
  num: string
  title: string
  body: string
  code?: string
}

const steps: Step[] = [
  {
    num: '01',
    title: 'Install CRDs and RBAC',
    body: 'VMJob and VMNode custom resources plus the controller service account.',
    code: `kubectl apply -f deploy/kubernetes/crds/
kubectl apply -f deploy/kubernetes/rbac/`,
  },
  {
    num: '02',
    title: 'Create the VM credentials secret',
    body: 'Out-of-band — never commit credentials.',
    code: `kubectl create secret generic vmjob-credentials \\
  --from-literal=username=$VM_ADMIN_USERNAME \\
  --from-literal=password=$VM_ADMIN_PASSWORD`,
  },
  {
    num: '03',
    title: 'Deploy the controller + API',
    body: 'Replace <YOUR_ACR_NAME> with your registry first.',
    code: `kubectl apply -f deploy/kubernetes/deployments/`,
  },
  {
    num: '04',
    title: 'Submit your first job',
    body: 'Either via REST or kubectl. Watch the status update in real time.',
    code: `./scripts/tests/submit-job.ps1 -Command "Get-Date" -Wait
kubectl get vmjobs`,
  },
]

const CodeRow: Component<{ code: string }> = (props) => {
  const [copied, setCopied] = createSignal(false)
  const copy = () => {
    navigator.clipboard.writeText(props.code)
    setCopied(true)
    setTimeout(() => setCopied(false), 1500)
  }
  return (
    <div class="relative mt-3">
      <pre class="overflow-x-auto rounded-lg border border-white/10 bg-[#0c0c14]/80 p-4 font-mono text-[12.5px] leading-relaxed text-slate-200">
        {props.code}
      </pre>
      <button
        type="button"
        onClick={copy}
        class="absolute right-2 top-2 inline-flex items-center gap-1 rounded-md border border-white/10 bg-white/5 px-2 py-1 text-[11px] text-slate-300 transition hover:bg-white/10"
      >
        {copied() ? <FiCheck class="text-emerald-400" /> : <FiCopy />}
        {copied() ? 'Copied' : 'Copy'}
      </button>
    </div>
  )
}

const QuickStart: Component = () => {
  return (
    <section id="quickstart" class="mx-auto max-w-7xl px-6 py-20">
      <div class="mx-auto max-w-3xl text-center">
        <p class="text-xs font-semibold uppercase tracking-[0.2em] text-cyan-300">Quick start</p>
        <h2 class="mt-3 text-4xl font-semibold tracking-tight md:text-5xl">
          From clone to first job in four commands.
        </h2>
        <p class="mt-5 text-lg text-slate-300">
          Cloud path is one PowerShell script (<code class="font-mono text-sm">setup-all-cloud.ps1</code>) — provisions VMs, AKS, ACR, and registers everything.
        </p>
      </div>

      <ol class="mt-14 grid grid-cols-1 gap-4 md:grid-cols-2">
        {steps.map((s) => (
          <li class="glass rounded-2xl p-6">
            <div class="flex items-baseline justify-between">
              <span class="font-mono text-xs uppercase tracking-wider text-violet-300">Step {s.num}</span>
              <span class="font-mono text-[10px] text-slate-500">k8s-vm-orchestrator</span>
            </div>
            <h3 class="mt-2 text-lg font-semibold">{s.title}</h3>
            <p class="mt-1 text-sm text-slate-400">{s.body}</p>
            {s.code && <CodeRow code={s.code} />}
          </li>
        ))}
      </ol>
    </section>
  )
}

export default QuickStart
