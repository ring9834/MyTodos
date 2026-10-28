# Todo: a location-aware job planner

An API-driven web app for workers to record jobs at customer locations and plan each day's work. Each job (a *todo*) has a title, a state (`todo` → `scheduled` → `done`) and a location (an address, plus an optional map point).

It's built as a **thin, production-minded vertical slice**:
- a Next.js front end
- a .NET API in Clean Architecture layers
- managed PostgreSQL
- AKS, provisioned with Terraform and deployed by Helm from GitHub Actions

---

## Architecture
<p align="center">
<img width="680" height="500" alt="todo_app_architecture_overview_workers" src="https://github.com/user-attachments/assets/362bed70-eb35-4c3b-ba46-1e9d58b97368" />
</p>

**Blue** boxes are the application workloads running in the cluster. **Beige** boxes are platform and external services. Solid arrows show the request path; dashed arrows show supporting flows (images, deployments, secrets, telemetry).

| Concern | Choice |
|---|---|
| Front end | Next.js (React, TypeScript): a static shell with client-side data via TanStack Query; no Server Actions |
| API | ASP.NET Core controllers; Clean Architecture (`Todo.Api` / `Application` / `Domain` / `Infrastructure`) |
| Data | PostgreSQL (Azure Flexible Server), EF Core code-first, `xmin` optimistic concurrency |
| Security | Password sign-in -> JWT in an `HttpOnly`, `Secure`, `SameSite=Strict` cookie; deny by default; owner filter on every query (another user's item -> 404) |
| Errors | Built-in ProblemDetails (RFC 9457) via a single `TodoExceptionHandler`, with a stable `code` |
| Observability | OpenTelemetry instrumentation -> Azure Monitor; the only `ILogger` call is in `TodoExceptionHandler` |
| Hosting | AKS, NGINX Ingress (TLS, `/` -> web, `/api` -> API), migrations as a Helm pre-upgrade hook |
| Delivery | GitHub Actions with Azure OIDC (no stored cloud secrets); SHA-tagged images; rollback on a failed release |

**Guiding principle:** the API is the only place rules and security are enforced, and every abstraction must solve a problem that exists here. So there are no microservices, caches, CQRS read models or repository layer. Each deliberate deferral is recorded with a trigger.

Full detail: [`docs/architecture.md`](docs/architecture.md) · decisions: [`docs/adr/`](docs/adr/README.md)

---

## CI/CD

<p align="center">
<img width="1280" height="700" alt="680750218-af15369d-c947-481e-bcae-73810e6bfe3f" src="https://github.com/user-attachments/assets/e69b143c-e46c-4fc3-8b19-8794ac3b44c1" />
</p>

One workflow (`.github/workflows/ci.yml`) runs on every push:

| Job | What it does |
|---|---|
| **API (build + test)** | Builds the .NET solution; runs unit tests and integration tests against real PostgreSQL (Testcontainers) |
| **Web (lint + build)** | Lints and builds the Next.js app; runs component tests |
| **changes** | Detects which areas changed, so infrastructure jobs run only when needed |
| **helm-check** | Lints and renders the Helm chart |
| **terraform** | Plans and applies infrastructure; **skipped** when `infra/` hasn't changed |
| **build-and-push** | Builds the API and web images, tagged with the git SHA, and pushes them to ACR |
| **deploy-dev** | Signs in to Azure with OIDC, runs `helm upgrade` (migration hook first, **rollback on failure**), then smoke-tests `/health/ready` |

A typical run takes about 4 minutes from push to a deployed `deploy-dev`.

---

## Repository layout

```
.
├── .github/workflows/      CI/CD (ci.yml)
├── api/
│   ├── src/
│   │   ├── Todo.Api/             host: controllers, TodoExceptionHandler, auth, health
│   │   ├── Todo.Application/     use cases (commands/queries + handlers), DTOs, interfaces
│   │   ├── Todo.Domain/          entities, TodoState, transition table, domain exceptions
│   │   └── Todo.Infrastructure/  EF Core, migrations, JWT issuing, password hashing, seeding
│   ├── tests/                    unit tests (no Docker) + integration tests (Testcontainers)
│   ├── Dockerfile
│   └── Todo.slnx
├── web/                    Next.js app: src/app (routes), src/components (feature folders), src/lib
├── deploy/helm/            Helm chart (todo-planner)
├── infra/                  Terraform (AKS, ACR, PostgreSQL, Key Vault, monitoring)
└── docs/                   requirements, architecture, design, ADRs, readiness, AI usage log
```

---

## Getting started

### Prerequisites
- .NET SDK (current LTS), Node.js (LTS), Docker
- For cloud work: Azure CLI, Terraform, kubectl, Helm

### Run locally

```bash
docker compose up          # API, web, PostgreSQL, telemetry dashboard
```

Then open the web app, sign in with a seeded development user, and create, schedule and complete a job.

> Adjust the commands, ports and seeded users here to match `docker-compose.yml` and the seed configuration.

### Run the tests

```bash
# API: unit tests (no Docker needed) and integration tests (Docker required)
dotnet test api/Todo.slnx

# Web: component tests
cd web && npm ci && npm test
```

### Deploy

Deployment is automated: push to `main`, and `ci.yml` builds, pushes and deploys to `deploy-dev`. Infrastructure changes under `infra/` trigger the `terraform` job. Runtime secrets come from Key Vault via the pipeline, never from git.

---

## API at a glance

| Method and path | Purpose |
|---|---|
| `POST /api/auth/login` · `POST /api/auth/logout` · `GET /api/auth/me` | Session |
| `GET /api/todos?state=&date=&page=&pageSize=` | List my jobs (paged) |
| `GET /api/todos/{id}` | Get one |
| `POST /api/todos` | Create |
| `PUT /api/todos/{id}` | Update details (`If-Match` required) |
| `PUT /api/todos/{id}/state` | Change state (`If-Match` required) |
| `DELETE /api/todos/{id}` | Delete |
| `GET /health/live` · `GET /health/ready` | Probes |

Errors are `application/problem+json`, with a machine-readable `code`, e.g. `todo.not_found` (404), `todo.invalid_transition` (409), `todo.version_conflict` (409), `validation_failed` (400). The contract is in the repository's OpenAPI document.

---

## Testing approach

- **Domain:** unit tests for the state machine and business rules, with no infrastructure.
- **API:** integration tests through the real HTTP pipeline against **real PostgreSQL** (Testcontainers), signing in through the **real login endpoint**, with no auth bypass. These cover isolation between users, concurrency (`xmin`) and the error contract.
- **Web:** component tests with Vitest and Testing Library, stubbing `fetch`.
- **Deliberately deferred,** each with a trigger: MSW, browser end-to-end tests (Playwright) and load tests (k6). Full-stack journeys are currently checked by hand after deploys.

---

## Documentation

| Document | Contents |
|---|---|
| [`docs/requirements.md`](docs/requirements.md) | Constraints, assumptions, requirements, quality scenarios, scope |
| [`docs/architecture.md`](docs/architecture.md) | Views, how each quality is achieved, ATAM risk themes |
| [`docs/design.md`](docs/design.md) | Domain rules, API contract, schema, security and deployment details |
| [`docs/adr/`](docs/adr/README.md) | Architecture Decision Records and the decision map |
| [`docs/production-readiness.md`](docs/production-readiness.md) | Readiness checklist with evidence, gaps and next steps |
| [`docs/ai-usage.md`](docs/ai-usage.md) | How AI was used, what was changed or rejected, and how it was verified |

---

## Known limitations

This is sized for a free-trial subscription. Notable gaps, each tracked in [`docs/production-readiness.md`](docs/production-readiness.md):
- a single small node and a single database primary
- no staging environment
- app-managed passwords (no MFA)
- alerts defined but not yet enabled
- automated end-to-end and load tests deferred

The first steps toward real customers would be federated sign-in, private networking with secrets mounted from Key Vault, and enabling the alerts.
