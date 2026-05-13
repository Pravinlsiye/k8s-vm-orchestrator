# k8s-vm-orchestrator — product page

SolidJS + Vite + TailwindCSS + motion-one. Glass-themed landing page that explains the project.

Deployed at: `https://<github-user>.github.io/k8s-vm-orchestrator/` (via the workflow at `.github/workflows/deploy-pages.yml`).

## Develop

```bash
npm install
npm run dev
```

## Build

```bash
npm run build
npm run preview
```

## Deploy

Pushes to `main` that touch `web/**` trigger `deploy-pages.yml`. First time only, enable Pages in repo settings:

> Settings → Pages → Build and deployment → Source: **GitHub Actions**

## Layout

```
web/
├── index.html
├── vite.config.ts          # base="/k8s-vm-orchestrator/" in build
├── src/
│   ├── index.css           # Tailwind v4 + theme tokens + glass utilities
│   ├── index.tsx           # entry
│   ├── App.tsx             # page shell
│   ├── config.ts           # GitHub URL, tagline, etc.
│   └── components/
│       ├── Nav.tsx
│       ├── Hero.tsx
│       ├── Why.tsx
│       ├── Architecture.tsx
│       ├── Features.tsx
│       ├── CodeExamples.tsx
│       ├── QuickStart.tsx
│       ├── TechStack.tsx
│       └── Footer.tsx
```
