# ADR-0011: Testability strategy and testing tooling

The strategy: no generic repository layer; Testcontainers for integration tests; real authentication in tests; one narrow `IClock` port; a deferred front-end mocking library; **no browser automation and no load-test automation for now**. The ADR also selects the testing tools.

| | |
|---|---|
| **Status** | Proposed |
| **Date** | 2026-09-29 |
| **Decision area** | Quality Attributes → **Testability** (strategy) and Technology & Tooling → **testing tools** |
| **Supersedes (in part)** | **ADR-0005 §3.3** (the time seam: `TimeProvider` → `IClock`) and **ADR-0005 §3.4** (front-end network mocking: MSW → `fetch` mocks, with MSW deferred) |
| **Related** | `docs/requirements.md`: NFR-1, Q1, Q2, Q6, Q8, NFR-9; ADR-0003 (deferred caching, a triggered upgrade); ADR-0005 (test strategy, mocking policy); ADR-0006 (`xmin` concurrency); ADR-0007 (layering, `IAppDbContext`, test projects); ADR-0008 (`FakeLogger` tests); ADR-0010 (CI) |

---

## 1. Context

The requirements need:
- **fast, isolated domain tests:** the state machine (Q2) and business rules
- **integration tests against the real database engine:** isolation (Q1), concurrency (Q6), authentication (Q8)
- **testable time-dependent logic**
- **testable front-end components** (NFR-1, NFR-9)

There are real alternatives for each. This ADR records the choices and the reasons, applying the **same right-sizing test used throughout these ADRs** (e.g. ADR-0003 caching and CQRS, ADR-0004 messaging, ADR-0005 patterns):

> **Does the abstraction solve a problem that actually exists here, or does it add indirection for its own sake?**

Section 2 records the five strategy decisions. Section 3 selects the tools that implement them.

---

## 2. Strategy decisions

### D1. No generic repository or port layer over EF Core

**Options:**
- (a) wrap the `DbContext` in `IRepository<T>`-style interfaces so it can be mocked
- (b) use the EF Core context directly, relying on the layering (ADR-0007) for the testability that actually matters

**Decision: (b).**
- The `DbContext` already implements the Unit of Work and Repository patterns. A generic wrapper adds indirection without adding real isolation.
- The logic worth unit-testing, the state machine and business rules, already lives in `Todo.Domain` with **zero EF Core dependency** (ADR-0007).
- A repository layer would re-solve a problem the layering already solves more cheaply. It would also invite the mocked-repository tests ADR-0005 warns against, which skip SQL translation, the owner filter and concurrency.

**Note on `IAppDbContext` (ADR-0007 §3.4):** Application reaches the context through `IAppDbContext`, an interface that **exposes the EF Core `DbSet`s themselves**. It is not a repository: it hides nothing, and it exists only so `Todo.Application` doesn't reference `Todo.Infrastructure` (the dependency rule). Tests never mock it; they use the real context against real PostgreSQL (D2).

### D2. Testcontainers (real PostgreSQL) for integration tests

**Options:**
- (a) EF Core's `UseInMemoryDatabase`
- (b) SQLite in-memory
- (c) **Testcontainers:** a real, ephemeral PostgreSQL per test run
- (d) a shared, long-lived development database

**Decision: (c).**
- **(a) and (b) risk tests passing against behaviour that differs from real PostgreSQL.** Concretely, `xmin`-based optimistic concurrency (ADR-0006, Q6) **doesn't exist in SQLite at all**, so Q6 literally can't be tested honestly against it. The in-memory provider also skips SQL translation, constraints (ADR-0006 §3.6) and the query-filter behaviour Q1 depends on.
- **(d) risks state leaking between test runs** and doesn't work well with parallel CI.
- **Testcontainers gives a real, isolated, disposable PostgreSQL** (the same major version as production, ADR-0006 §3.12). GitHub-hosted `ubuntu-latest` runners have Docker available natively, so it works in CI with no extra set-up.

**Lifecycle:** **one container per test run, shared by all integration tests** (an xUnit v3 assembly fixture), not one per test. This is a real CI-time-budget decision, worth monitoring as the suite grows. Isolation between tests comes from **resetting the data** (Respawn) and from each test creating its own data through builders.

### D3. Authentication in tests uses the real `/api/auth/login` endpoint, with no test-only bypass

**Decision:** integration tests authenticate as **real seeded test users through the actual login endpoint** (ADR-0001, ADR-0009), not a special "skip auth" test mode or a fake authentication handler.

**Why:** a bypass would mean Q1's owner-isolation tests and Q8's authentication tests **never exercise the thing they claim to verify**: cookie or bearer handling, token validation, the current-user resolution, and the owner filter bound to it.

### D4. One narrow port: `IClock`

**Options:**
- (a) call `DateTime.UtcNow` / `DateTimeOffset.UtcNow` directly wherever it's needed
- (b) a single **`IClock`** interface (`DateTimeOffset UtcNow { get; }`), injected wherever the current time is read
- (c) .NET's built-in `TimeProvider` abstraction (with `FakeTimeProvider` in tests), as the earlier ADR-0005 proposed

**Decision: (b), narrowly.**

```csharp
// Todo.Application/Common/Interfaces/IClock.cs
public interface IClock { DateTimeOffset UtcNow { get; } }

// Todo.Infrastructure/Time/SystemClock.cs
public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

// tests: a two-line hand-written fake (ADR-0005: hand-written fakes before libraries)
public sealed class FakeClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow { get; set; } = now; }
```

**Where it's used:** wherever the current time is read, and nowhere else:
- audit timestamps (`created_at` / `updated_at`, ADR-0006)
- token issuing (`iat` / `exp`, ADR-0001)
- any future date-relative rule, e.g. "overdue" relative to `scheduledFor`

**Domain methods receive times as parameters** (ADR-0008 §3.10), so the domain itself stays free of ports.

**Why not (a):** untestable wall-clock dependence would force tests to sleep, or make them flaky around boundaries such as token expiry.

**Why not (c):** `TimeProvider` is first-party and a reasonable choice. But it is a broad abstract class (timers, timestamps, time zones), and this code needs only "now". A one-member interface states the need exactly, and its fake is two lines.

**This is deliberately not a general pattern.** It is the one place where wall-clock dependence would otherwise make tests sleep or become flaky. **No other ports are added for testability.**
- `ICurrentUser`, `IPasswordHasher` and `ITokenIssuer` exist for security and layering reasons (ADR-0001, ADR-0007), not for mocking.
- Options and typed `HttpClient` are framework mechanisms, not ports.

*Framework components* (JWT validation, the rate limiter) keep their own internal clocks. Tests of token expiry therefore issue tokens through `ITokenIssuer` with a `FakeClock` set in the past, and let the real validation reject them.

### D5. Front end: mock `fetch` directly; defer MSW

**Options:**
- (a) hand-mock `fetch` per test (`vi.stubGlobal("fetch", vi.fn())`)
- (b) adopt MSW (Mock Service Worker) for realistic network-layer interception

**Decision: (a) for now,** with (b) as a **deferred, triggered upgrade**. This is the same pattern as ADR-0003 (caching and CQRS) and ADR-0004 (messaging): don't adopt pre-emptively for a component set this small.

**How it stays consistent with ADR-0005 ("mock at the boundary, not modules"):**
- Tests **stub the global `fetch`** only (`vi.stubGlobal("fetch", vi.fn())`). Components still run their **real TanStack Query hooks and the real fetch wrapper** (`web/src/lib/api`), so request building, `If-Match` headers and ProblemDetails parsing are all exercised.
- **Hooks and API modules are never mocked** with `vi.mock`. The only justified module mock is the Leaflet map stub (§3.5).
- **Small typed helpers** in `web/src/test-setup.ts` build responses, e.g. `jsonResponse(body, init)` and `problemResponse(status, code, errors?)`, so tests stay short and the ADR-0008 error contract is used consistently.

**Trigger for adopting MSW:** once **enough test files need consistent, shared request mocking that the hand-rolled mocks start actively hurting maintainability.** Concrete signals:
- the same endpoint's mock is duplicated across several test files
- mocks drift from the OpenAPI contract
- network mocking is also wanted in the browser during development

Handlers could then be derived from the generated OpenAPI types (ADR-0009). MSW is also the natural upgrade if front-end tests need to simulate **more complex request/response sequences** than per-test stubs express well.

### D6. No end-to-end browser automation (Playwright, Cypress, …); full-stack journeys are checked manually

**Options:**
- (a) Playwright or Cypress journeys against the local stack and after each deploy
- (b) **stop automated testing at the component level (front end) and the integration level (back end),** and check full-stack journeys by hand

**Decision: (b), for now.** Automated testing covers two levels:
- **Front end:** components with Vitest + Testing Library, with the real hooks and fetch wrapper against a stubbed `fetch` (D5).
- **Back end:** integration tests through the real HTTP pipeline with `WebApplicationFactory` + Testcontainers + the real login (D2, D3).

There is **no automated test that drives a real browser through the full stack.** That verification is done **manually**: the key journeys are checked by hand in the browser, locally and after deployments, as they have been throughout the project's deployment work.

**Why this is right-sized here:**
- The behaviour worth protecting (rules, isolation, concurrency, the error contract) is already covered below the browser, at lower cost and with less flakiness.
- The full-stack *wiring* (ingress, TLS, routing, cookies) is covered by the scripted `curl` smoke checks after each deploy (ADR-0010 §3.4), which don't need a browser.
- A browser suite adds a runtime, browser installs in CI, flakiness management and maintenance for a small UI.

**What is checked manually** (the release checklist in `docs/runbook.md`):

| Journey | Checks |
|---|---|
| Sign in → list → create → schedule → complete → delete → sign out | The whole happy path through the real browser, ingress and database |
| Two users (`alice` / `bob`) | Neither sees the other's items in the UI (the API-level isolation is already automated) |
| Map picker and day map (FR-10, FR-11) | Leaflet renders; picking a point sets the coordinates |
| Keyboard-only use of the list and form (NFR-9) | Complements the automated axe checks at component level |
| An open tab across a deploy (ADR-0012 §3.6) | Stale-chunk reload works |

**Trigger to introduce Playwright** (the preferred tool if adopted, for its traces and parallelism):
- the manual checklist becomes frequent or time-consuming
- a regression reaches production that component and integration tests couldn't catch
- more than one person changes the UI

### D7. No automated load or performance testing (k6, Lighthouse CI) for now

**Options:**
- (a) scripted load tests (k6) with thresholds, and Lighthouse CI for Web Vitals
- (b) **verify the performance-related scenarios with deterministic checks, telemetry and manual drills,** and defer load-test automation

**Decision: (b), for now.**

| Scenario | How it's verified instead |
|---|---|
| **Q3** list p95 < 200 ms | A **deterministic integration test**: 1,000+ seeded items; the list query's plan (`EXPLAIN`) uses the owner-first index; results are bounded by the page size. Plus the **observed p95** of `http.server.request.duration` for the list route in telemetry (ADR-0008) during manual use |
| **Q4** zero failed requests during a rollout | A **manual deployment drill**: a simple request loop (e.g. `curl` in a shell loop) during `helm upgrade`, counting non-2xx responses; plus a deliberately failing release to confirm `--atomic` rolls back |
| **Q7** scale-out | **Configuration review** (HPA bounds, statelessness checklist, ADR-0004 §3.1) + a **manual scale drill** (`kubectl scale` to 3 replicas; delete a pod while using the app). Throughput measurement is deferred |
| Front-end budgets (ADR-0003) | **size-limit** stays in CI (build-time, deterministic). Web Vitals are checked with a manual Lighthouse run in the browser's developer tools |

**Why:**
- At MVP volume, the performance design rests on structural choices (indexes, paging, no N+1, async I/O) that can be verified **deterministically**.
- Latency thresholds measured on shared CI runners or a burstable trial cluster would be noisy (ADR-0003 R1, R2).

**Trigger to introduce k6** (and Lighthouse CI):
- before real production traffic
- a latency complaint, or the telemetry p95 approaching its budget
- a change to the data-access or scaling design (e.g. a cache, a read replica, new indexes)

---

## 3. Tooling selection

### 3.1 Criteria

| # | Criterion | Why |
|---|---|---|
| K1 | **Implements the §2 strategy** without fighting it | The tools serve the decisions, not the reverse |
| K2 | **Licence and supply chain:** permissive open source, actively maintained, no commercial-licence surprises | Several popular .NET libraries changed licence or had supply-chain incidents recently (a mocking library in 2023; an assertion library, a mediator and a mapper moved to commercial licences in 2025) |
| K3 | **CI-friendly:** headless, deterministic, machine-readable results | NFR-1; the PR pipeline in about 5 minutes |
| K4 | **One tool per job** | Fewer concepts to explain and maintain (Q5, P-3) |
| K5 | **The same right-sizing test** as §1 | No tool adopted "just in case" |

### 3.2 Selected tools

| Job | Chosen | Rejected / deferred |
|---|---|---|
| **.NET test framework** | **xUnit v3** (assembly fixture for the shared container, D2; `[Theory]` for the transition matrix) | NUnit, MSTest, TUnit |
| **Assertions** | **Shouldly** | FluentAssertions (commercial licence from v8) |
| **Mocking library** (only at external boundaries, ADR-0005) | **NSubstitute**, rarely needed | Moq, FakeItEasy |
| **Time** | **`IClock` + hand-written `FakeClock`** (D4) | `FakeTimeProvider` (superseded) |
| **Log capture** | **`FakeLogger` / `FakeLogCollector`** (Microsoft.Extensions.Diagnostics.Testing), for ADR-0008's single-`ILogger` and no-secrets tests | Custom capture providers |
| **In-process API host** | **`WebApplicationFactory<Program>`**: the real pipeline, including authentication, `[ApiController]` and `TodoExceptionHandler` | — |
| **Real database** | **Testcontainers for .NET** (PostgreSQL module), **one container per run** (D2) | EF in-memory, SQLite, a shared development database |
| **Reset between tests** | **Respawn** (deletes rows, keeps the schema) | A container per test (slow); a transaction per test (hides commit-time behaviour such as `xmin` conflicts) |
| **Authentication in tests** | **The real login endpoint** with seeded users (D3) | Fake authentication handlers; bypass flags |
| **Test data** | **Hand-written builders**; a simple loop for the 1,000+ items in the Q3 query-plan test | AutoFixture; Bogus (not needed without load tests) |
| **Architecture tests** | **ArchUnitNET** | NetArchTest |
| **Outbound HTTP simulation** (future geocoding) | **WireMock.Net** (added when the first outbound dependency arrives) | Mocking `HttpClient` |
| **Coverage** (informational) | **coverlet** + **ReportGenerator** | A coverage gate (ADR-0005) |
| **Front-end runner** | **Vitest** (jsdom) | Jest |
| **Front-end components** | **React Testing Library** + user-event + jest-dom | Enzyme; shallow rendering |
| **Front-end network** | **`vi.stubGlobal("fetch", vi.fn())`** + typed response helpers (D5) | **MSW: deferred** with a trigger; `vi.mock` of hooks or API modules |
| **Accessibility** | **vitest-axe** (components) + a manual keyboard check (D6) | `@axe-core/playwright` (deferred with Playwright) |
| **End-to-end / browser automation** | **None for now:** manual journey checks (D6); scripted `curl` smoke checks after deploy (ADR-0010) | Playwright (**deferred**, the preferred tool when triggered); Cypress, Selenium |
| **Performance and load** | **None automated for now:** a deterministic query-plan test for Q3, telemetry p95, manual drills for Q4 and Q7 (D7) | k6 (**deferred**, the preferred tool when triggered); NBomber, JMeter, BenchmarkDotNet |
| **Front-end budgets** | **size-limit** (PR); a manual Lighthouse run for Web Vitals | Lighthouse CI (**deferred** with D7) |
| **Manual API exploration** | **`Todo.Api.http`** + the Development-only API reference UI | Postman collections |
| **Static checks** | .NET: compiler (nullable, warnings as errors), analysers, `dotnet format`. Web: `tsc --noEmit`, ESLint (with import-boundary rules), Prettier | StyleCop (overlaps) |
| **Contract drift** | OpenAPI (build time) + `openapi-typescript` + `git diff --exit-code` (ADR-0009) | Hand-maintained types |
| **Breaking API changes** | **`oasdiff breaking`** against `main`'s `openapi.json` (ADR-0012 §3.1) | Review only |
| **Security** | **gitleaks**, **Trivy** (images and IaC), **Dependabot**, **CodeQL** (where the plan includes it), `dotnet list package --vulnerable`, `npm audit` | Checkov (overlaps Trivy) |
| **IaC and charts** | `terraform fmt` / `validate`, **tflint**; `helm lint` + **kubeconform** | `terraform test` (deferred) |

### 3.3 Back-end test design

| Aspect | Decision |
|---|---|
| Projects | `Todo.UnitTests` (Domain, mapping, the exception-handler table, ArchUnitNET rules; **no Docker**) and `Todo.IntegrationTests` (`WebApplicationFactory` + Testcontainers + Respawn + `FakeLogger`), per ADR-0007 |
| Domain tests | Plain xUnit with no fakes: the transition matrix as a `[Theory]` over every (from, to) pair + the completeness test (ADR-0005 §3.6); guard-clause tests for BR-1 to BR-5 |
| Integration fixture | **One PostgreSQL container per run** (an assembly fixture) → migrations applied once → **Respawn** resets the data before each test, keeping seeded users (D3) |
| Time | `FakeClock` registered in `WebApplicationFactory` for time-sensitive tests (audit timestamps, token expiry) |
| Authentication helper | `LoginAsync("alice")` calls `POST /api/auth/login` and returns an authenticated `HttpClient` (cookie or bearer), so `alice` / `bob` isolation tests read naturally |
| Parallelism | Collections run in parallel where they don't share the database; database tests are serialised against the shared container, or use separate databases in the same container if the suite grows |

### 3.4 Front-end test design

| Aspect | Decision |
|---|---|
| Location | Co-located `*.test.tsx` in each feature folder (ADR-0007 §3.11) |
| Network | `vi.stubGlobal("fetch", vi.fn())` in each test, with `jsonResponse` / `problemResponse` helpers from `test-setup.ts`. Assertions check both **the rendered outcome** and **the request made** (method, URL, `If-Match`, body) |
| What is real | Components, TanStack Query hooks, the fetch wrapper, the form schemas |
| What is never mocked | Hooks and API modules (no `vi.mock` for them) |
| Queries | By role and label (Testing Library), which also enforces NFR-9 |

### 3.5 Limits, and how they're handled

| Limit | Handling |
|---|---|
| Async Server Components can't be unit-tested with Vitest | Data views are client components (ADR-0002 guardrails); routes are thin (ADR-0007) |
| Leaflet maps don't render meaningfully in jsdom | Replaced by a stub in component tests (the one justified module mock); **checked manually** in the browser (D6) |
| Docker is required for integration tests | `Todo.UnitTests` runs without Docker; Docker is on the demo checklist |
| No test drives a real browser through the full stack | The D6 manual checklist + the post-deploy `curl` smoke checks |

### 3.6 Where the tools run in CI

| Stage | Trigger | Tools | Budget |
|---|---|---|---|
| **Static** | Every PR / push | Compiler + analysers, `dotnet format`, `tsc`, ESLint, Prettier, tflint, `terraform validate`, `helm lint` + kubeconform, gitleaks, Trivy config, `oasdiff breaking` | < 1 min |
| **Unit** | Every PR / push | xUnit (`Todo.UnitTests`, including ArchUnitNET), Vitest (components + stubbed `fetch` + axe), size-limit | < 1 min |
| **Integration** | Every PR / push | xUnit (`Todo.IntegrationTests`): one Testcontainers PostgreSQL per run + Respawn + real login + `FakeLogger`; the **Q3 query-plan test**; EF pending-model check; OpenAPI and TS drift check | < 3 min (**monitor as the suite grows**, D2) |
| **Post-deploy** | After `deploy.yml` | Scripted `curl` smoke checks (ADR-0010) | < 1 min |
| **Manual (release checklist)** | After deploys that change the UI or the flows | The D6 journeys; the D7 drills when the deployment or scaling set-up changes | ~10 min |
| **Scheduled** | Schedule | Coverage report | — |
| **Supply chain** | Image build; schedule | Trivy image, Dependabot, CodeQL | — |

**Reporting:** TRX / JUnit results go to the job summary with PR annotations.

### 3.7 Verification map: which tool proves which requirement

| Requirement | Tool(s) |
|---|---|
| Q1 isolation; Q8 authentication | xUnit + `WebApplicationFactory` + Testcontainers + **the real login** (D2, D3) |
| Q6 concurrency (`xmin`) | xUnit + Testcontainers (only possible against real PostgreSQL, D2) |
| Q2 transition matrix; BR-1 to BR-5 | xUnit `[Theory]` domain tests (no infrastructure, D1) |
| Time-dependent behaviour (audit timestamps, token expiry) | `FakeClock` (D4) |
| Q3 list p95 | Query-plan integration test (deterministic) + observed p95 in telemetry (D7) |
| Q4 zero-downtime deploys | Manual deployment drill: request loop during `helm upgrade` + a failing-release drill (D7) |
| Q7 scale-out | HPA configuration review + manual scale drill; throughput measurement deferred (D7) |
| Full-stack journeys; FR-10 / FR-11 maps | Manual release checklist (D6); `curl` smoke after deploy |
| Q5 modifiability | A timed manual drill |
| NFR-2 / NFR-3 validation and error contract | xUnit integration tests (ProblemDetails, `code`, no `traceId`); front-end `problemResponse` tests |
| NFR-5 security baseline | gitleaks, Trivy, Dependabot, CodeQL / analysers, integration tests |
| NFR-6 no secrets in logs; single `ILogger` call | `FakeLogger` + ArchUnitNET |
| NFR-9 accessibility | vitest-axe + a manual keyboard check (D6) |
| ADR-0003 front-end budgets | size-limit (CI) + a manual Lighthouse run (D7) |
| ADR-0007 dependency rules | ArchUnitNET |
| ADR-0009 contract consistency | OpenAPI + TS drift check |
| NFR-4 / ADR-0010 infrastructure | tflint, `terraform validate` / `plan`, kubeconform, Trivy config |

---

## 4. ATAM analysis

### 4.1 Sensitivity points

| # | Sensitivity point | Attributes affected |
|---|---|---|
| S1 | **Docker availability** (Testcontainers, Compose for end-to-end tests) | Testability |
| S2 | **Container lifecycle** (one per run) and reset speed | CI time budget |
| S3 | **Discipline in `fetch` mocks** (typed helpers; no module mocks) | Front-end test maintainability |
| S4 | **`IClock` reaching every place that reads the time** | Test determinism |
| S5 | Tool licences and maintenance | Supply chain (K2) |

### 4.2 Trade-offs

| # | Trade-off | Chosen side | Accepted cost |
|---|---|---|---|
| T1 | EF Core context directly (via `IAppDbContext`) vs a repository layer | No extra indirection; full EF features; tests that mean something | Application is coupled to EF Core (ADR-0007 T3) |
| T2 | Real PostgreSQL vs in-memory / SQLite | Honest tests of Q1 and Q6 | Docker needed; slower than in-memory |
| T3 | One container per run vs per test | CI speed | Tests share an engine; isolation relies on Respawn and per-test data |
| T4 | Real login vs a test authentication bypass | Security tests exercise the real pipeline | Slightly slower test set-up |
| T5 | `IClock` vs `TimeProvider` | A one-member port that states exactly what's needed; a trivial fake | A custom interface instead of the first-party abstraction; framework components keep their own clocks |
| T6 | `fetch` mocks vs MSW | No extra library for a small component set | Hand-rolled mocks may duplicate; MSW adopted at the trigger |
| T7 | Manual full-stack journeys vs browser automation (D6) | No browser runtime, flakiness or suite maintenance for a small UI | Full-stack regressions are caught by a person, not by CI (R7) |
| T8 | Deterministic checks + telemetry + drills vs load-test automation (D7) | No noisy thresholds on shared runners or a burstable cluster | Throughput and latency under load aren't measured automatically (R8) |

### 4.3 Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | Docker isn't running during the interview | Unit tests run without Docker; checklist |
| R2 | The integration suite outgrows its budget with one shared container | Monitor the stage time; move to parallel collections with a database per collection in the same container |
| R3 | `fetch` mocks drift from the API contract | Typed helpers built on the generated OpenAPI types; this drift is itself an MSW trigger (D5) |
| R4 | Code reads `DateTimeOffset.UtcNow` directly, bypassing `IClock` | An ArchUnitNET or analyser rule banning direct `UtcNow` / `Now` calls outside `SystemClock` |
| R5 | CodeQL isn't available for a private repository | Analysers + warnings as errors + ESLint as the static layer |
| R6 | A tool changes licence | Permissive, first-party or widely adopted tools; Dependabot surfaces major versions for review |
| R7 | **A full-stack regression** (routing, cookies, a UI flow) **is only found by hand,** or not at all | Post-deploy `curl` smoke checks; the manual release checklist; component and integration tests cover the logic; the D6 trigger |
| R8 | **A performance regression goes unnoticed** until users feel it | The Q3 query-plan test catches the most likely cause (a lost index or an unbounded query); telemetry p95 for the list route; the defined latency alert (ADR-0008, V17); the D7 trigger |

### 4.4 Non-risks

| # | Non-risk | Because |
|---|---|---|
| N1 | Tests passing while production queries fail | Real PostgreSQL; no in-memory provider; no mocked ORM |
| N2 | Security tests that don't test security | The real login; no bypass |
| N3 | Flaky time-dependent tests | `FakeClock`; no sleeps |
| N4 | UI tests breaking on refactors | Testing Library by role and label; only `fetch` is stubbed |

---

## 5. Consequences

- **Backend build:** inject the EF Core context (through `IAppDbContext`) directly. There are no repository interfaces to build first.
- **One small `IClock` / `SystemClock` pair,** and nothing more speculative. Add the `FakeClock` to the test projects.
- **The integration test project needs a Testcontainers PostgreSQL fixture reused across the run,** not one per test, for speed. This is a real CI-time-budget decision, worth monitoring once the suite grows.
- **No new front-end mocking library yet;** add MSW only when the D5 trigger is hit.
- **No Playwright, k6 or Lighthouse CI in the repository or CI for now** (D6, D7). A manual release checklist (in `docs/runbook.md`) and the `curl` smoke checks cover full-stack verification. The Q3 query-plan test replaces the latency measurement.
- **ADR-0005 §3.3 (time) and §3.4 (front-end MSW) are superseded,** and the related wording in ADR-0005, ADR-0006, ADR-0007 and ADR-0008 is updated.

### Impacts on other decision areas

| Decision area | Constraint or input from this decision |
|---|---|
| **Testability and maintainability (ADR-0005)** | The time seam becomes `IClock`; front-end network mocking becomes `fetch` spies (MSW deferred); tools finalised here |
| **Data (ADR-0006)** | The audit interceptor uses `IClock` |
| **Component & structural (ADR-0007)** | `IClock` in `Application/Common/Interfaces`, `SystemClock` in `Infrastructure/Time`; `test-setup.ts` holds the `fetch` response helpers; ArchUnitNET rules, including the ban on direct `UtcNow` |
| **Cross-cutting (ADR-0008)** | "Now" comes from `IClock`; `FakeLogger` for the logging tests |
| **Deployment (ADR-0010)** | CI stages as §3.6 (no end-to-end or load stages); Docker on runners; the integration stage time is monitored; `curl` smoke only after deploy |
| **Performance and scalability (ADR-0003, ADR-0004)** | Q3 verified by the query-plan test + telemetry; Q4 and Q7 by manual drills; load tests deferred |
| **Evolution (ADR-0012)** | Browser automation and load-test automation added to the evolution register with their triggers |

---

## 6. Verification

| Check | How |
|---|---|
| Q6 is tested against real PostgreSQL `xmin` | Integration test: two updates with the same `If-Match` → the second gets 409 |
| Q1 / Q8 go through the real login | Review: no fake authentication handler or bypass flag exists in the test projects |
| No direct wall-clock reads | Architecture or analyser rule (R4) |
| No repository layer | Review: no `IRepository<T>`; `IAppDbContext` exposes `DbSet`s only |
| Front-end tests don't mock hooks or API modules | Review / lint rule: `vi.mock` is used only for the map stub |
| Q3 is protected without load tests | The query-plan integration test fails if the list query stops using the owner-first index |
| Manual checks actually happen | The release checklist is ticked in the release notes for deploys that change the UI |
| The PR pipeline stays within budget | CI timing; the integration stage in particular (D2) |

## 7. Revisit when

- **The MSW trigger (D5) is hit:** duplicated endpoint mocks, contract drift in mocks, or more complex request/response sequences than per-test stubs express well.
- **The Playwright trigger (D6) is hit:** frequent manual checks, a full-stack regression escaping to production, or several people changing the UI.
- **The k6 trigger (D7) is hit:** before real production traffic, a latency complaint, or a data-access or scaling change.
- The integration stage exceeds its budget. Parallelise with a database per collection.
- Code needs timers or delays, not just "now". Reconsider `TimeProvider` (D4 option c).
- Several API consumers appear. Add contract tests (e.g. Pact).
- The team grows. Consider mutation testing (Stryker.NET) for the domain.
