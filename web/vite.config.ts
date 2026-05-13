import { defineConfig } from 'vite'
import solid from 'vite-plugin-solid'
import tailwindcss from '@tailwindcss/vite'

// Repo name = the part after the / in `username/k8s-vm-orchestrator`.
// GitHub Pages serves project sites at https://<user>.github.io/<repo>/,
// so all asset URLs need this prefix in production.
const repoName = 'k8s-vm-orchestrator'

export default defineConfig(({ command }) => ({
  base: command === 'build' ? `/${repoName}/` : '/',
  plugins: [solid(), tailwindcss()],
  build: {
    target: 'es2020',
    cssMinify: 'lightningcss',
  },
}))
