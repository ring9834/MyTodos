# Production-readiness checklist

**Working definition:** a team could deploy this repeatedly, know when it breaks, roll it back, trust it against baseline threats, and change it confidently.

**Status:** `todo` | `partial` | `done` | `deferred`.
- Every row needs evidence: a file, test, CI run or log.
- Every `partial` or `deferred` row needs a one-line reason, and every `deferred` row a **trigger** for picking it up.
- Update this file in the same PR that changes a row.

**† = evidence to confirm.** The item was decided or built during the project, but its evidence link still needs checking against the repository before the row can be marked `done`.

**Last reviewed:** 2026-10-22, against the `deploy-dev` environment (AKS, trial subscription).

## Summary

| Status | Count | Meaning |
|---|---|---|
| `done` | 11 | Evidence exists |
| `partial` | 24 | Built or decided, with a stated gap |
| `todo` | 10 | Not started |
| `deferred` | 4 | Deliberately postponed, with a trigger |

---

## Correctness

| Item | Status | Evidence | Notes / reason |
|---|---|---|---|
| Domain state-machine unit tests (legal and illegal transitions) | partial† | `api/tests/Todo.UnitTests` | Confirm a `[Theory]` covers all 9 from→to pairs, plus a completeness test (every `TodoState` has an entry in the transition table) |
| API integration tests against real PostgreSQL | done | `api/tests/Todo.IntegrationTests` (Testcontainers + `WebApplicationFactory`) | Real PostgreSQL, not in-memory or SQLite: `xmin` concurrency can't be tested honestly on SQLite. Tests sign in through the real login endpoint, with no auth bypass |
| Error contract: ProblemDetails, stable `code`, no stack traces, no `traceId` | done | `Todo.Api/Middleware/TodoExceptionHandler.cs`; `TodoExceptionHandlerTests.cs` | Maps not-found → 404, illegal transition → 409, stale version → 409, guard failure → 400 with `errors[field]`, anything else → 500. Framework exception messages never reach the client |
| Frontend component tests (forms, loading / empty / error) | partial† | `web/src/**/*.test.tsx` (Vitest + Testing Library, `vi.stubGlobal("fetch", vi.fn())`) | Confirm the list's loading, empty and error states and the form's validation are all covered |
| Network-level request mocking (MSW) | deferred | Per-test `fetch` stubs | Simpler at this scope. **Trigger:** duplicated endpoint mocks across files, mocks drifting from the contract, or multi-step request sequences |
| End-to-end browser tests | deferred | A manual journey (register / sign in → create todo → … → log out), checked by hand in the deployed environment during deployment debugging | Automated testing stops at the component and integration levels. **Trigger:** manual checks become frequent, or a regression reaches production → Playwright |
| Load / performance tests | deferred | — | Interim: the query-plan test (see Data) + the observed p95 in telemetry. **Trigger:** before real traffic, a latency complaint, or a data-access change → k6 |

## Security

| Item | Status | Evidence | Notes / reason |
|---|---|---|---|
| JWT validation configured (issuer, audience, expiry, algorithm pinned) | partial† | Auth configuration in `Program.cs`; sign-in works in `deploy-dev` | Add negative tests: expired, tampered signature, wrong audience, `alg: none` → 401 |
| Owner isolation: user A can't read or modify user B's todo (tested) | partial† | Integration tests through the real login; `KeyNotFoundException` → 404 | Confirm the test covers GET, PUT, the state change, DELETE **and** the list. The 404 (not 403) is deliberate, so existence never leaks |
| Server-side input validation | done | Domain guard clauses → `ArgumentException` → 400 with `errors[field]` (`TodoExceptionHandler`) | The server is authoritative; the UI mirrors the rules for instant feedback |
| Session cookie is `HttpOnly`, `SameSite=Strict`, and `Secure` outside Development | done | Verified via a test failure: the browser doesn't send the `Secure` cookie without real TLS | The dependency is real, not theoretical: **TLS at the ingress is a hard prerequisite for any non-Development environment** |
| TLS at ingress (HTTPS redirect, HSTS) | partial† | NGINX Ingress + cert-manager / Let's Encrypt (`deploy/helm/todo-planner`) | Confirm the `Certificate` is Ready and HTTP redirects to HTTPS in `deploy-dev`. The ingress-nginx upstream is retired, so the plan is the AKS-managed add-on, then Gateway API |
| CSRF protection | done | Same origin (`/api` behind the same host), `SameSite=Strict`, JSON-only bodies, no CORS configured | By design: no anti-forgery tokens needed |
| Login rate limiting | partial† | Rate limiter on the sign-in endpoint | Confirm a test asserts 429 and a `Retry-After` header |
| No secrets in git; secret scanning on | partial | Secrets reach pods via the pipeline → Kubernetes Secret `todo-secrets`; values masked in workflow logs (`::add-mask::`) | Turn on GitHub secret scanning and push protection, or add gitleaks to CI |
| Containers non-root, read-only root filesystem | partial† | Chiseled .NET runtime image (non-root by default) | Confirm the `securityContext` (`runAsNonRoot`, `readOnlyRootFilesystem`, drop all capabilities) on the API, web and migration Job |
| Dependency + image vulnerability scanning in CI | todo | — | Dependabot (NuGet, npm, Actions, base images) + a Trivy image scan that fails on critical findings |
| Least-privilege database access | deferred | — | The API and the migration Job share one connection string. **Trigger:** before any real customer data → separate migration and runtime roles and secrets |
| Cluster and state hardening | todo | — | From the threat review: protect the Terraform state (it holds generated passwords); disable AKS local accounts; move workloads out of the `default` namespace to scope RBAC |

## Reliability

| Item | Status | Evidence | Notes / reason |
|---|---|---|---|
| Liveness / readiness / startup probes | partial† | `deploy/helm/todo-planner` | Readiness must include the database check; liveness must not, or a database outage causes restart storms |
| ≥2 replicas + PodDisruptionBudget | partial† | Chart values | The trial quota allows a single small node, so a node failure is still an outage (accepted for the trial) |
| Resource requests and limits | partial | Migration Job: requests 50m CPU / 128Mi, limit 256Mi (`kubectl describe job todo-migrate`) | Confirm on the API and web Deployments; size them from observed usage |
| Graceful shutdown | todo† | — | A `preStop` delay + `terminationGracePeriodSeconds`, so the ingress stops routing before pods exit |
| Backward-compatible migrations | partial | Migration Job as a Helm pre-upgrade hook; expand/contract policy | The first deploy failed with `42P07: relation "Todos" already exists`: the migration history and the schema had diverged. The hook + rollback kept users safe. Rules adopted: no `EnsureCreated()`; never regenerate an applied migration; review the generated SQL |

## Observability

| Item | Status | Evidence | Notes / reason |
|---|---|---|---|
| Structured logs with correlation ID | done | W3C trace context via `Activity`; every log record and span carries `TraceId` | Free from the framework, with no custom work. The trace id is deliberately **not** returned in error responses |
| Single `ILogger` call (in `TodoExceptionHandler`); one record per exception | partial | `TodoExceptionHandler`; unit test asserts exactly one record at the mapped level; framework diagnostics suppressed (`SuppressDiagnosticsCallback`) | Add an architecture test so no other class can depend on `ILogger` |
| Health endpoints | partial† | `/health/live`, `/health/ready` | Confirm they're not routed publicly by the ingress |
| Metrics / traces (OpenTelemetry) | partial | OpenTelemetry SDK active (visible in the migration Job's output); Azure Monitor exporter via `APPLICATIONINSIGHTS_CONNECTION_STRING` (Key Vault → `todo-secrets`) | Instrumentation is in place; no dashboards yet (the backend choice is deferred per ADR-0018; trigger: a real dashboarding or alerting need). Also remove the OpenTelemetry console exporter and drop EF Core command logs to Warning outside Development, since they currently duplicate stdout |
| Alerting | partial† | A cost budget alert (Terraform) | Error-rate, latency and readiness alerts are defined but not enabled |

## Deployability

| Item | Status | Evidence | Notes / reason |
|---|---|---|---|
| CI on every PR (build, test, lint, scan) | partial | `.github/workflows/ci.yml` (build + tests) | Add lint and format checks, a secret scan, Trivy, and `oasdiff` for breaking API changes |
| Images tagged by commit SHA | done | `rushtododevacr.azurecr.io/todo-api:da83eec5386e…` | Immutable tags, never `latest`, so every deploy and rollback is traceable |
| Deploy with `helm upgrade --rollback-on-failure` + smoke test | partial | The `deploy-dev` job | Helm 4 renamed `--atomic` to `--rollback-on-failure`. Add a post-deploy `curl` smoke test: `GET /` → 200, `GET /api/auth/me` → 401 problem+json, TLS valid |
| Rollback drill performed | done | The `deploy-dev` run on 2026-09-30: the migration hook failed and **the release rolled back automatically**; the running version kept serving | Unplanned but real. Still to do: a deliberate failing-readiness release + a manual `helm rollback` |
| No long-lived cloud secrets in CI (OIDC) | done | `azure/login@v2` with OIDC; the federated credential's subject matches GitHub's ID-based subject claim | A subject mismatch (`AADSTS700213`) was found and fixed during setup |
| Terraform remote state | partial† | `infra/terraform` | Confirm the remote backend with locking, and that state storage is private (see Security) |
| Static asset cache-control headers / CDN | partial | Next.js hashed assets are served as immutable by default; API responses are `no-store` | CDN deferred: single region, few users |

## Data

| Item | Status | Evidence | Notes / reason |
|---|---|---|---|
| Managed PostgreSQL with backups | partial | Azure Database for PostgreSQL Flexible Server (automated backups, point-in-time restore) | A restore drill hasn't been performed |
| Migrations tested in CI | todo† | — | Apply all migrations to an empty Testcontainers database; run `dotnet ef migrations has-pending-model-changes` |
| Indexes match query patterns | todo | — | The initial migration doesn't yet show an owner-first index. Add `(OwnerId, State, ScheduledFor)` + a deterministic query-plan test (Q3) |
| Integrity rules in the database (`CHECK`, foreign keys) | todo | — | Add `CHECK` constraints for state and scheduled-needs-date, a foreign key to users, and lengths aligned with validation, while the schema is still disposable |
| Personal data kept out of logs | done | The single log call records route template, status and code only; Npgsql `Include Error Detail=false` | Addresses and coordinates are never logged |

## Maintainability

| Item | Status | Evidence | Notes / reason |
|---|---|---|---|
| OpenAPI contract in repo; client types generated | partial | `openapi.yaml` (referenced by the API's error mapping) | Confirm the TypeScript types are generated from it, and add a CI drift check |
| Lint / format enforced in CI | todo | — | `dotnet format --verify-no-changes`, ESLint, Prettier, `tsc --noEmit` |
| ADRs for significant decisions | done | `docs/adr/README.md` (index) + ADRs; `requirements.md`, `architecture.md`, `design.md`, `ai-usage.md` | Includes deliberate deferrals, each with a trigger |
| Breaking API change detection | todo | — | `oasdiff breaking` against `main` in CI |

## Operability

| Item | Status | Evidence | Notes / reason |
|---|---|---|---|
| README quickstart; productive from a cold start in under 5 minutes | todo | — | Prerequisites → `docker compose up` → sign in with a seeded user → run the tests |
| One-command local stack | partial† | Docker Compose (API, web, PostgreSQL, Aspire dashboard) | Confirm it works from a fresh clone |
| Runbook (crash loop, database unreachable, bad deploy) | todo | — | Seed it from incidents already handled: the OIDC subject mismatch, the migration history mismatch, and the `Secure` cookie without TLS |
| Cost control | partial | Trial-sized tiers; budget alert†; AKS and PostgreSQL stopped between demos | — |

---

## Definition of Done (per change)

- [ ] Acceptance criteria met
- [ ] Tests added or updated; CI green
- [ ] Lint and format clean; no new high or critical vulnerabilities
- [ ] OpenAPI updated if the API changed, and the change is additive (no breaking change)
- [ ] Migration is backward-compatible if the schema changed; generated SQL reviewed; no `EnsureCreated()`
- [ ] No new `ILogger` usage outside `TodoExceptionHandler`; no secrets or personal data in logs
- [ ] Deployed to `deploy-dev` via the pipeline; smoke checks pass; manual journey checked if the UI or flows changed
- [ ] This checklist and the ADRs updated if a row or decision changed

## What I would do next

The highest-value gaps, in order:

1. **Confirm TLS end to end in `deploy-dev`.** The `Secure` cookie depends on it; that's proven by a test failure.
2. **Fix migration hygiene.** Resolve the history mismatch, then add the owner-first index, the database constraints and the CI migration check while the schema is still disposable.
3. **Add security scanning to CI:** secret scanning, Dependabot and a Trivy image scan. Cheap, and currently missing.
4. **Harden access:** protect the Terraform state, disable AKS local accounts, use a dedicated namespace, and split the migration and runtime database credentials.
5. **Tidy observability:** remove the console exporter, set EF Core logs to Warning, add the `ILogger` architecture test, and enable the defined alerts.
6. **Run the planned drills:** a deliberate failing-readiness rollback, a request loop during `helm upgrade`, and a database restore.
7. **Write the runbook and README quickstart,** starting from the three incidents already met.

**Deliberately deferred, each with a trigger:**
- MSW, Playwright and k6 (testing)
- least-privilege database roles (before real data)
- dashboards (a real alerting need)
- CDN (a geographically spread user base)
- staging, Key Vault CSI and federated sign-in (before real customers)
