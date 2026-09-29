# ADR-0011: Testing and quality tooling — unit, integration, front-end, end-to-end, performance and CI-integrated tools

| | |
|---|---|
| **Status** | Proposed |
| **Date** | 2026-09-29 |
| **Decision area** | Technology & Tooling → **testing and quality tooling** |
| **Method** | Selection against criteria (as in ADR-0002); analysed with ATAM concepts |
| **Related** | `docs/requirements.md`: NFR-1, NFR-2, NFR-3, NFR-5, NFR-9, Q1–Q8; ADR-0003 (performance budgets); ADR-0005 (test strategy, mocking policy, seams); ADR-0007 (test projects, architecture tests); ADR-0008 (log-capture and error-contract tests); ADR-0009 (contract drift check); ADR-0010 (CI workflows, security scanning) |

---

## 1. Context

### 1.1 What is already decided, and what this ADR decides

ADR-0005 decided the **test strategy**:
- integration tests against real PostgreSQL are the backbone
- real collaborators come first, then hand-written fakes, then a mocking library only at external boundaries
- the front end mocks at the network level
- there is no coverage-percentage target

Several ADRs named candidate tools along the way. **This ADR makes the final selection of every testing and quality tool,** fills the gaps, and states where each one runs in CI.

### 1.2 Selection criteria

| # | Criterion | Why |
|---|---|---|
| K1 | **Fits the strategy** (ADR-0005): real dependencies, behaviour over interaction | The tools must make the chosen strategy easy, not fight it |
| K2 | **Licence and supply chain:** permissive open source, actively maintained, with no commercial-licence surprises | Several popular .NET libraries changed licence or had supply-chain incidents recently (e.g. a mocking library in 2023; an assertion library, a mediator and a mapper that moved to commercial licences in 2025) |
| K3 | **CI-friendly:** headless, deterministic, machine-readable results (TRX / JUnit), fast | NFR-1; the full suite in under 5 minutes (ADR-0005) |
| K4 | **One tool per job:** no overlapping tools | Fewer concepts to explain and maintain (Q5, P-3) |
| K5 | **First-party or de-facto standard** where one exists | Longevity; familiarity to reviewers |
| K6 | **Same language as the code under test** | .NET tools for the API; TypeScript tools for the web |

---

## 2. Decision summary

| Job | Chosen | Rejected / deferred |
|---|---|---|
| **.NET test framework** | **xUnit v3** | NUnit, MSTest, TUnit |
| **Assertions** | **Shouldly** | FluentAssertions (commercial licence from v8); plain `Assert` only |
| **Mocking (sparingly, ADR-0005)** | **NSubstitute** | Moq, FakeItEasy |
| **Fake time** | **`FakeTimeProvider`** (Microsoft.Extensions.TimeProvider.Testing) | Custom clock interfaces |
| **Log capture** | **`FakeLogger` / `FakeLogCollector`** (Microsoft.Extensions.Diagnostics.Testing) | Custom capturing providers |
| **In-process API host** | **`WebApplicationFactory<Program>`** (Microsoft.AspNetCore.Mvc.Testing) | Deploying the API for tests |
| **Real database in tests** | **Testcontainers for .NET** (PostgreSQL module) | EF Core in-memory provider; SQLite; a shared test database |
| **Database reset between tests** | **Respawn** | Recreating the database per test; transaction rollback per test |
| **Test data** | **Hand-written builders** (ADR-0005); **Bogus** only for bulk seed data (performance tests) | AutoFixture |
| **Architecture tests** | **ArchUnitNET** | NetArchTest |
| **Outbound HTTP simulation** (future geocoding) | **WireMock.Net** | Mocking `HttpClient` |
| **Contract snapshots** (optional) | **Verify** for the OpenAPI document and representative JSON (Should) | — |
| **Coverage** (informational) | **coverlet** + **ReportGenerator** (job summary) | A coverage gate |
| **Front-end test runner** | **Vitest** (jsdom environment) | Jest |
| **Front-end component testing** | **React Testing Library** + `@testing-library/user-event` + `@testing-library/jest-dom` | Enzyme; shallow rendering |
| **Network mocking (front end)** | **MSW v2** | Mocking modules (`vi.mock`) for API calls |
| **Accessibility** | **axe-core**: `vitest-axe` in component tests; `@axe-core/playwright` in end-to-end tests | Manual checks only |
| **End-to-end** | **Playwright** | Cypress, Selenium |
| **Performance and load** | **k6** | NBomber, JMeter, BenchmarkDotNet (micro-benchmarks) |
| **Front-end budgets** | **Lighthouse CI** (Web Vitals) + **size-limit** (bundle size) | Manual Lighthouse runs |
| **Manual API exploration** | **`.http` files** in the repo (`Todo.Api.http`), plus the Development-only API reference UI (ADR-0009) | Postman collections |
| **Static checks (.NET)** | Compiler (nullable, warnings as errors), .NET analysers, `dotnet format --verify-no-changes` | StyleCop (overlaps) |
| **Static checks (web)** | TypeScript `strict` (`tsc --noEmit`), **ESLint** (Next.js + typescript-eslint + import-boundary rules), **Prettier** | TSLint |
| **Contract drift** | Build-time OpenAPI generation + `openapi-typescript`, **`git diff --exit-code`** in CI (ADR-0009) | Hand-maintained types |
| **Security scanning** | **gitleaks** (secrets); **Trivy** (images **and** IaC / Kubernetes misconfiguration); **Dependabot** (updates + alerts); **CodeQL** (SAST, where available); `dotnet list package --vulnerable`, `npm audit` | Checkov (overlaps Trivy); commercial SAST |
| **IaC and chart checks** | `terraform fmt -check`, `terraform validate`, **tflint**; `helm lint`, `helm template` + **kubeconform** | `terraform test` (deferred) |
| **CI reporting** | TRX / JUnit results published to the job summary and as PR annotations; Playwright HTML report + traces as artifacts on failure | — |

---

## 3. Options and trade-offs

### 3.1 .NET test framework

| Option | Notes | Verdict |
|---|---|---|
| **xUnit v3** | The de-facto standard for ASP.NET Core (its own tests and docs use it); a new instance per test (isolation); shared fixtures for containers; runs on the new Microsoft Testing Platform as well as VSTest | **Chosen** |
| NUnit | Mature; rich attributes | Equally capable; less common in the ASP.NET Core ecosystem |
| MSTest | First-party | Fewer community examples for this stack |
| TUnit | Modern; source-generated; fast | Young; less proven for a production-readiness demo |

**Conventions:**
- **Shared Testcontainers PostgreSQL per test collection** (`ICollectionFixture`), with the database reset by Respawn between tests. This keeps the integration suite fast (ADR-0005 R2).
- `[Theory]` + `TheoryData` for the table-driven transition matrix (ADR-0005 §3.6).
- Collections run in parallel wherever they don't share a database.

### 3.2 Assertions

| Option | Licence | Verdict |
|---|---|---|
| **Shouldly** | Permissive (BSD-3) | **Chosen:** readable failure messages (`title.ShouldBe("x")`), small API |
| FluentAssertions | **Commercial licence from v8** (v7 remains open source but frozen) | Rejected (K2) |
| AwesomeAssertions (a community fork of FluentAssertions) | Permissive | A viable alternative; the younger project |
| Plain xUnit `Assert` | — | Workable, but with weaker messages |

### 3.3 Real dependencies in tests

| Job | Chosen | Why the alternatives are rejected |
|---|---|---|
| Database | **Testcontainers PostgreSQL**, same major version as production (ADR-0006 §3.12) | The **EF Core in-memory provider** and **SQLite** don't run PostgreSQL's SQL, constraints, `xmin` concurrency or query-filter translation, so tests would pass while production fails (ADR-0005 §3.4) |
| Reset | **Respawn** (deletes rows, keeps the schema; fast) | Recreating the database is slow. A transaction per test hides commit-time behaviour (concurrency, constraints) |
| API host | **`WebApplicationFactory`**: the real pipeline (auth, routing, `[ApiController]`, `TodoExceptionHandler`) in-process | — |
| Authentication | **Real tokens from `POST /api/auth/login`** (ADR-0001) | A fake authentication handler would bypass the security pipeline under test |
| Time | **`FakeTimeProvider`** (token expiry, audit timestamps) | — |
| Logs | **`FakeLogger`**: proves the single `ILogger` call (ADR-0008 §3.1) and that no secrets or addresses are logged | — |

### 3.4 Architecture tests

| Option | Verdict | Why |
|---|---|---|
| **ArchUnitNET** | **Chosen** | Actively maintained; expressive rules; an xUnit integration |
| NetArchTest | Rejected | The original package is less actively maintained (forks exist) |

**Rules to implement** (from ADR-0007 §3.12 and ADR-0008):
- `Todo.Domain` has no references beyond the base library.
- Controllers don't use Infrastructure types.
- `Todos` and `Auth` don't reference each other.
- `ILogger` is used only in `TodoExceptionHandler`.
- `IgnoreQueryFilters` is not used outside tests.

### 3.5 Front-end testing

| Job | Options | Decision and why |
|---|---|---|
| Runner | **Vitest** vs Jest | **Vitest:** fast, native TypeScript/ESM, a Jest-compatible API; supported in the Next.js testing docs |
| Environment | **jsdom** vs happy-dom | **jsdom:** the most complete DOM emulation, and the fewest surprises |
| Component tests | **React Testing Library** + user-event + jest-dom | Tests what users see and do, and queries by role and label (NFR-9) |
| Network | **MSW v2** | Components run their real TanStack Query hooks and fetch wrapper against realistic HTTP (ADR-0005) |
| Accessibility | **vitest-axe** | Automated WCAG checks on rendered components |

**Limits, and how they're handled:**
- **Async Server Components can't be unit-tested with Vitest.** This fits the design: data views are client components (ADR-0002 guardrails), and routes are thin (ADR-0007).
- **Leaflet maps don't render meaningfully in jsdom.** Map components (FR-10, FR-11) are covered by Playwright. In component tests the map is replaced by a stub, the one justified module mock.

### 3.6 End-to-end: Playwright vs Cypress

| Criterion | **Playwright** | Cypress |
|---|---|---|
| Browsers | Chromium, Firefox, WebKit | Chromium-family + Firefox (WebKit experimental) |
| Parallelism in CI | Built in (workers, sharding) | Paid dashboard or plugins |
| Debugging failures | **Traces** (DOM, network, console per step) as CI artifacts | Screenshots and videos |
| Language | TypeScript, same as the web | TypeScript |
| Multiple tabs / origins | Supported | Limited |

**Decision: Playwright.**

**Scope:** a small smoke journey (sign in → create → schedule → complete → delete), plus an accessibility scan with `@axe-core/playwright`. These run against **the local Docker Compose stack in CI**, and **against production after deployment** (ADR-0010 smoke tests).

**Flakiness controls:**
- `retries: 1` in CI only
- web-first assertions (no sleeps)
- traces captured on the first retry

### 3.7 Performance and load: k6

| Option | Verdict | Why |
|---|---|---|
| **k6** | **Chosen** | Scripted in JavaScript; **thresholds as pass/fail criteria** (e.g. `p(95)<200`); CLI-friendly; the same scripts run locally, in CI and against the cloud |
| NBomber | Rejected | Capable (.NET), but k6 thresholds and ecosystem suit the Q3/Q4/Q7 scenarios better |
| JMeter | Rejected | Heavy GUI-oriented tooling |
| BenchmarkDotNet | Rejected | Micro-benchmarks; the drivers are end-to-end latency budgets |

**Scripts** (in `perf/`):

| Script | Checks | Threshold |
|---|---|---|
| `list-todos.js` | Q3: the list endpoint with 1,000+ seeded items (seeded with Bogus) | `http_req_duration{route:list} p(95) < 200 ms` |
| `rolling-deploy.js` | Q4: steady traffic during `helm upgrade` | `http_req_failed == 0` |
| `scale-out.js` | Q7: throughput at 1 vs 3 API replicas | Near-linear; Q3 still met |

**Not a PR gate.** Shared CI runners are too noisy for reliable latency thresholds. k6 runs **nightly and on demand** against the Compose stack, and **manually** against the cloud (ADR-0003 R2).

### 3.8 Front-end budgets

| Tool | Budget (ADR-0003) | Runs |
|---|---|---|
| **size-limit** | Initial JS for the list page ≤ 250 KB gzipped | PR gate (fast and deterministic) |
| **Lighthouse CI** | LCP ≤ 2.5 s, CLS ≤ 0.1 (lab); INP is tracked as a field metric later (ADR-0008) | Nightly, against the Compose stack |

### 3.9 Security and infrastructure scanning in CI

| Tool | Scans | Runs |
|---|---|---|
| **gitleaks** | Secrets in commits | PR gate |
| **Dependabot** | Outdated and vulnerable dependencies (NuGet, npm, GitHub Actions, Docker base images) | Scheduled PRs + alerts |
| `dotnet list package --vulnerable`, `npm audit --audit-level=high` | Known vulnerable packages | PR gate |
| **Trivy** (`image`) | Container image vulnerabilities | After the image build; fail on critical |
| **Trivy** (`config`) | Terraform, Kubernetes manifests (rendered Helm) and Dockerfile misconfigurations | PR gate |
| **CodeQL** | Static analysis for C# and TypeScript | PR gate **where the repository's plan includes it** (it's free for public repositories). Otherwise the analysers + ESLint remain the static layer (R2) |
| **tflint** | Terraform best practices and provider-specific errors | PR gate |
| **kubeconform** | Rendered Helm manifests against the Kubernetes schemas | PR gate |

Trivy does two jobs (images and IaC), which is why Checkov isn't added (K4).

### 3.10 How the tools run in CI

| Stage | Trigger | Tools | Budget |
|---|---|---|---|
| **Static** | Every PR / push | Compiler + analysers, `dotnet format`, `tsc`, ESLint, Prettier, tflint, `terraform validate`, `helm lint` + kubeconform, gitleaks, Trivy config | < 1 min |
| **Unit** | Every PR / push | xUnit (`Todo.UnitTests`, including ArchUnitNET), Vitest (components + MSW + axe), size-limit | < 1 min |
| **Integration** | Every PR / push | xUnit (`Todo.IntegrationTests`): `WebApplicationFactory` + Testcontainers + Respawn + `FakeLogger`; the EF pending-model check; the OpenAPI + TS drift check | < 3 min |
| **End-to-end** | PRs to `main` | Playwright + axe against Docker Compose | < 3 min |
| **Post-deploy** | After `deploy.yml` | Smoke checks (ADR-0010) + the Playwright smoke journey against production | < 2 min |
| **Nightly / on demand** | Schedule, manual | k6 (Q3, Q7), Lighthouse CI, coverage report | — |
| **Supply chain** | Image build; schedule | Trivy image, Dependabot, CodeQL | — |

**Reporting:**
- `dotnet test` writes TRX; Vitest and Playwright write JUnit. All are published to the **job summary with PR annotations** on failing tests.
- The coverage summary (ReportGenerator) is informational only; there is no gate (ADR-0005).
- Playwright traces and HTML reports are uploaded **as artifacts on failure**.

### 3.11 Verification map: which tool proves which requirement

| Requirement | Tool(s) |
|---|---|
| Q1 isolation; Q8 authentication; Q6 concurrency; Q2 transitions (API level) | xUnit + `WebApplicationFactory` + Testcontainers + real tokens |
| Q2 transition matrix; BR-1 to BR-5 (domain) | xUnit `[Theory]` unit tests |
| Q3 list p95 | k6 `list-todos.js` |
| Q4 zero-downtime deploys | k6 `rolling-deploy.js` during `helm upgrade` |
| Q5 modifiability | A timed manual drill (no tool) |
| Q7 scale-out | k6 `scale-out.js` + HPA observation |
| NFR-2 / NFR-3 validation and error contract | xUnit integration tests (ProblemDetails, `code`, no `traceId`) |
| NFR-5 security baseline | gitleaks, Trivy, Dependabot, CodeQL / analysers, integration tests |
| NFR-6 no secrets in logs; single `ILogger` call | `FakeLogger` integration test + ArchUnitNET rule |
| NFR-9 accessibility | vitest-axe + `@axe-core/playwright` |
| ADR-0003 front-end budgets | size-limit (PR) + Lighthouse CI (nightly) |
| ADR-0007 dependency rules | ArchUnitNET |
| ADR-0009 contract consistency | OpenAPI + TS drift check; Verify snapshots (Should) |
| NFR-4 reproducibility; ADR-0010 infrastructure | tflint, `terraform validate` / `plan`, kubeconform, Trivy config |

### 3.12 Where the tests live

```
api/tests/
├── Todo.UnitTests/            # xUnit + Shouldly; domain, mapping, exception-handler table; ArchUnitNET
└── Todo.IntegrationTests/     # xUnit + WebApplicationFactory + Testcontainers + Respawn + FakeLogger
api/src/Todo.Api/Todo.Api.http # manual requests for exploration and the live demo
web/src/**/**.test.tsx         # Vitest + Testing Library + MSW + vitest-axe (co-located)
web/e2e/                       # Playwright specs + axe
perf/                          # k6 scripts + Bogus-based seeding helper
```

---

## 4. ATAM analysis

### 4.1 Sensitivity points

| # | Sensitivity point | Attributes affected |
|---|---|---|
| S1 | **Docker availability** (Testcontainers, the Compose stack for end-to-end tests) | Testability (ADR-0005 R1) |
| S2 | **Suite runtime** (shared containers, Respawn, parallel collections) | Feedback speed (NFR-1) |
| S3 | **End-to-end flakiness** | Trust in CI |
| S4 | **Tool licences and maintenance** | Supply chain, longevity (K2) |
| S5 | **Noise on shared runners** for latency measurements | Validity of performance results |

### 4.2 Trade-offs

| # | Trade-off | Chosen side | Accepted cost |
|---|---|---|---|
| T1 | Real PostgreSQL vs an in-memory database | Tests that reflect production | Docker needed; slower than in-memory |
| T2 | Shouldly vs FluentAssertions | A permissive licence | A smaller assertion vocabulary |
| T3 | Playwright vs Cypress | Multi-browser, built-in parallelism, traces | — |
| T4 | k6 nightly vs as a PR gate | Reliable results; fast PRs | Performance regressions are caught within a day, not per PR |
| T5 | Trivy for images and IaC vs specialised tools | One tool, two jobs (K4) | Less depth than a dedicated policy engine |
| T6 | ArchUnitNET vs NetArchTest | Maintenance activity | — |
| T7 | No coverage gate | Tests aimed at rules and risks (ADR-0005) | A number some reviewers expect (explain the choice) |

### 4.3 Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | Docker isn't running during the interview, so the integration suite can't run | `Todo.UnitTests` runs without Docker (ADR-0007); check Docker in the demo checklist |
| R2 | CodeQL isn't available for a private repository without an extra plan | .NET analysers + warnings as errors + ESLint as the static layer; CodeQL where available |
| R3 | Playwright tests become flaky | Web-first assertions; one retry in CI; traces; keep the end-to-end scope small |
| R4 | A tool changes licence (as FluentAssertions, MediatR and AutoMapper did) | Prefer first-party or permissive tools; Dependabot surfaces major-version changes for review |
| R5 | Map components are untested in jsdom | Covered by Playwright; the map stub in component tests is documented |

### 4.4 Non-risks

| # | Non-risk | Because |
|---|---|---|
| N1 | Tests passing while production queries fail | Real PostgreSQL; no in-memory provider; no mocked ORM |
| N2 | UI tests breaking on refactors | Testing Library queries by role and label; MSW at the network level |
| N3 | Overlapping, redundant tools | One tool per job (K4) |

---

## 5. Consequences

**Positive**
- Every requirement and ADR verification row maps to a named tool (§3.11).
- All tools are free, permissive and CI-friendly; two first-party testing packages (`FakeTimeProvider`, `FakeLogger`) replace custom test code.
- Fast PR feedback: static + unit + integration + end-to-end within the 5-minute budget, with slower performance checks run nightly.

**Negative / follow-ups**
- Docker is required for the integration and end-to-end stages.
- Performance regressions are detected nightly, not per PR.
- ADR-0005 and ADR-0007's "e.g." tool mentions are finalised here.

### Impacts on other decision areas

| Decision area | Constraint or input from this decision |
|---|---|
| **Testability (ADR-0005)** | Tool choices finalised: xUnit v3, Shouldly, NSubstitute, `FakeTimeProvider`, Testcontainers, Respawn, Vitest, Testing Library, MSW, Playwright |
| **Component & structural (ADR-0007)** | ArchUnitNET for the dependency rules; test project layout confirmed; `Todo.Api.http` in the API project |
| **Cross-cutting (ADR-0008)** | `FakeLogger` for the single-`ILogger` and no-secrets tests |
| **Deployment (ADR-0010)** | CI stages and budgets as §3.10; Docker available on runners; Playwright post-deploy smoke; Trivy for images and IaC; nightly k6 and Lighthouse workflows |
| **Performance / Scalability (ADR-0003, ADR-0004)** | k6 scripts with thresholds for Q3, Q4 and Q7; size-limit and Lighthouse CI budgets |

---

## 6. Verification

| Check | How |
|---|---|
| The PR pipeline finishes within budget | CI timing: static + unit + integration + end-to-end < 5–6 min |
| Results are visible in PRs | A failing test appears as a PR annotation, with details in the job summary |
| Every §3.11 row has at least one test | Traceability review against `requirements.md` §12 |
| No tool with a restrictive licence | Review of the direct dependencies of the test projects and `web/package.json` |
| Playwright failures are debuggable | Traces uploaded on failure |

## 7. Revisit when

- The team grows. Consider mutation testing (Stryker.NET) for the domain, and visual regression testing.
- Several clients consume the API. Add consumer-driven contract tests (e.g. Pact), or property-based API tests generated from the OpenAPI document.
- Performance regressions slip through between nightly runs. Run k6 on dedicated runners as a gate before releases.
- Terraform modules multiply. Add `terraform test` for module-level tests.
