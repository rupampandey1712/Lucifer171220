# 17. CI/CD Architecture (GitHub Actions)

```mermaid
flowchart LR
    PR[Pull request] --> B[build<br/>dotnet build -warnaserror<br/>npm ci + tsc + eslint]
    B --> U[unit + architecture tests<br/>vitest]
    U --> I[integration + API tests<br/>Testcontainers on runner]
    I --> C[contract check<br/>OpenAPI export, TS client diff, oasdiff]
    B --> S[security<br/>CodeQL · gitleaks · dependency audit]
    C & S --> D[docker build<br/>api · workers · web]
    D --> T[Trivy scan + SBOM]
    T --> E2E[Playwright smoke<br/>docker compose]
    E2E --> OK{{PR checks green}}

    MAIN[Merge to main] --> PUSH[push images to ACR<br/>tag = git sha, OIDC login]
    PUSH --> MIG_S[migration job → staging]
    MIG_S --> DEP_S[deploy Container Apps revision → staging<br/>SWA staging]
    DEP_S --> SMOKE[smoke tests + ZAP baseline + k6 short]
    SMOKE --> GATE{{GitHub Environment 'production'<br/>required reviewers}}
    GATE --> MIG_P[migration job → prod]
    MIG_P --> DEP_P[deploy new revision at 0% → health → shift 10% → 100%]
    DEP_P --> VERIFY[post-deploy smoke; auto-rollback = traffic back to previous revision]
```

| Workflow | Trigger | Notes |
|---|---|---|
| `backend.yml` | PR/push touching `StaySphere/src/**`, `tests/**` | `setup-dotnet` 10.x, NuGet cache, test results + coverage artifacts |
| `frontend.yml` | PR/push touching `StaySphere/web/**` | Node 22, npm cache, Vitest, build |
| `e2e.yml` | PR (smoke), nightly (full) | compose up, Playwright, traces uploaded on failure |
| `infra.yml` | PR touching `infrastructure/bicep/**` | `bicep build`, lint, `what-if` against staging |
| `deploy.yml` | push to `main`, manual | OIDC federated credential to Azure (no stored secrets), environments `staging` → `production` with approval |
| `codeql.yml` | PR + weekly | C# + JS/TS |

- **No long-lived cloud secrets in GitHub**: `azure/login` with OIDC; ACR push through role assignment.
- **Build once, promote**: the same image digest moves staging → production.
- **Migrations** are a separate, gated job ([Azure doc](azure-architecture.md#safe-database-migrations)).
- Concurrency groups prevent overlapping deploys to an environment.
