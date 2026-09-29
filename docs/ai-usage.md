# AI-Enabled Engineering Log

This log records how AI tools were used to build this project, what was accepted, changed or rejected, and how the output was verified. The aim is to show that AI accelerated the work while design decisions, correctness and understanding stayed with me.

> **Honesty rule for this log:** it records what actually happened, including AI mistakes and my own. Placeholders marked _[fill in]_ need completing in my own words; nothing here should claim work or checks I didn't do.

---

## 1. Working principles

1. **I own the decisions.** AI proposes options and I choose. Business assumptions live in `requirements.md`, and technical decisions in ADRs, both in my own reasoning.
2. **Nothing is committed unread.** I read and understand every generated line before committing it. If I can't explain it, it doesn't go in.
3. **Verification over trust.** The gate for AI output is evidence: the source documents, tests, compiler and linters, `terraform plan`, `helm lint`, and running the system. The AI's confidence is not evidence.
4. **Challenge the output.** I question structure and reasoning, not just syntax. For example, is this requirement actually a design decision?
5. **Small, reviewable steps.** One change per prompt, with small commits and clear messages.
6. **No secrets or private data in prompts.** No connection strings, keys, passwords or tokens are pasted into AI tools.
7. **Keep hands-on capability.** I rehearse likely changes by hand, because the interview requires unassisted changes (Q5).

## 2. Workflow

```
Define the task myself  →  Prompt for one small piece  →  Read and question the output
        ↑                                                          ↓
  Log it here  ←  Commit  ←  Verify against evidence (source docs / tests / plan / run)
```

## 3. Tools

| Tool | Used for | Not used for |
|---|---|---|
| Claude (claude.ai chat) | Requirements analysis, document drafting, design discussion, reviewing my own drafts | Final decisions; anything involving secrets |
| _[fill in: IDE assistant, e.g. Copilot / Cursor / Claude Code]_ | _[fill in: e.g. code completion, boilerplate, test scaffolding]_ | _[fill in]_ |

---

## 4. Log

### Phase 0: Planning and requirements analysis (2026-09-29)

**4.1 End-to-end plan**
- **Asked AI to:** produce an end-to-end plan from requirements through to deployment, based on the two brief documents.
- **Accepted:** the phase structure (requirements → architecture → backend → frontend → containers → Terraform → Helm → CI/CD → readiness → rehearsal) and the prioritisation principle of a thin vertical slice through every layer.
- **Changed / rejected:** the AI assumed a stack (React + Vite, single-user, fully permissive state transitions) without asking. I replaced these with my own decisions (see 4.4).
- **Verified by:** checking that every mandated technology in the brief (Terraform, Kubernetes, Helm, CRUD API, tests) appears in the plan.

**4.2 Which documents to prepare**
- **Asked AI to:** recommend which documents to show at the interview.
- **Accepted:** a small set (README, requirements, architecture, ADRs, this log, production-readiness notes), with each document kept to about one page.
- **Changed:** _[fill in, if anything]_

**4.3 Requirements vs design boundary**
- **Asked AI to:** draft `requirements.md` and ADR-0001 (technology stack).
- **Problem I spotted:** the first draft mixed requirements with design. It included the API contract (endpoints, status codes, JSON), the physical data model (UUIDs, index, concurrency token), technology choices in the constraints table, and technology-specific NFRs (e.g. RFC 7807, `helm --atomic`).
- **How I challenged it:** I asked whether the API contract, state model and data model were requirements or design. The resulting test was *"Would the business care if this changed?"* If not, it's design.
- **Outcome:**
  - Requirements now state *what* and *why* only.
  - The state model and conceptual data model stayed, because they are business rules.
  - The API contract and physical data model moved to Phase 1 design.
  - ADR-0001 moved to Phase 1, because an ADR records a design decision.
- **Verified by:** re-reading each section of `requirements.md` against the test above.

**4.4 My assumptions and quality attribute scenarios**
- **Input:** I supplied my own assumptions (A1–A7) and quality attribute scenarios (Q1–Q7). _[fill in: written by me / drafted with another tool and edited by me]_
- **Where my decisions overrode the AI's earlier draft:**

  | Topic | AI's draft | My decision | Why |
  |---|---|---|---|
  | Users | Single user, auth deferred | Multi-user from day one; every item has an owner | Cheap now, expensive to retrofit; gives a real security scenario |
  | State transitions | All transitions allowed | `todo → scheduled → done`, plus unschedule; nothing leaves `done` | Simplest rule that supports the domain; clear and testable |
  | Frontend | React + Vite | Preference for Next.js (React-based, satisfies C-1); **not yet decided**, open in ADR-0002 | _[fill in your reason]_ |
  | NFR style | Flat NFR list | Measurable quality attribute scenarios with priorities | Testable, and ranked by importance and risk |

- **Asked AI to:** rethink and regenerate `requirements.md` around my assumptions and scenarios.
- **Accepted from the AI's review:**
  - It found that the brief allows **creation in any state, including `done`**. So creation is not a transition, and the state diagram now has three entry points.
  - It filled in my incomplete **Q7 (scalability)**.
  - It added **Q8 (unauthenticated access)**, a gap in my scenarios once multi-user was in scope.
  - It kept design details out of my scenarios (`xmin`, index, status codes, `helm --atomic`) while preserving the intent, e.g. "indistinguishable from not found".
  - It added a risk for credentials sent over plain HTTP while HTTPS is deferred.
- **Verified by:**
  - _[✓ when done]_ I checked the "creation in any state" claim against the source text: the brief says to allow creation *"with … a state of 'todo', 'scheduled' and 'done'"*.
  - _[✓ when done]_ I re-read both brief documents line by line against constraints C-1 to C-8 and P-1 to P-7, to confirm nothing was missed or invented.
  - _[✓ when done]_ I checked that every Q and FR appears in the traceability matrix.
- **Open decisions (mine to make, not the AI's):**
  - _[ ]_ Can `todo` go straight to `done`? (currently: no)
  - _[ ]_ Can a `done` item be edited or deleted? (currently A9: delete only)
  - _[ ]_ Is scheduling by date, or by date and time? (currently: date)

### Phase 1: Architecture and design

**Decision map and ADR-0001 (Quality Attributes → Security)**
- **Asked AI to:** organise Phase 1 around my decision areas, and make the first decision: Quality Attributes → Security.
- **Accepted:** the tactics (app-managed credentials; JWT carried in an `HttpOnly` cookie; deny by default; a central owner filter; eight defence layers), the ATAM analysis, and the NFR-10 (HTTPS) promotion.
- **Problem I spotted:** the ADR described Next.js, .NET, PostgreSQL, Azure/AKS and GitHub Actions as a "baseline fixed by the constraints". The brief fixes none of them: it says only "React (and any supporting libraries)", an allowed backend language, AWS or Azure, and Kubernetes. It also cited requirements A5 for Next.js, but A5 doesn't say that.
- **Outcome:** ADR-0001 was rewritten to be technology-neutral (tactics only), with a table separating what is fixed from what is open. Stack-specific mechanisms moved to a conditional appendix. The stack references in ADR-0002 and the ADR index were corrected.
- **Verified by:** _[✓ when done]_ re-reading C-1 to C-6 in the brief against ADR-0001 §1.2.
- **My call still pending:** _[ ]_ the 8-hour token lifetime (T5): agree or change?

**ADR-0002 (Technology & Tooling → core stack)**
- **Asked AI to:** rewrite ADR-0002 as a selection within the constraints, weighing the options for the frontend, backend, cloud and data store.
- **Accepted:** the three kinds of choice (mandated / constrained / open); the selection criteria (K1–K8); one ADR for the core stack, with other tools decided in their own areas.
- **Key judgement:** the frontend choice was close. The AI's analysis rated a static SPA as simpler and lower-risk; Next.js was chosen on my fluency and the future BFF path, with guardrails (no Server Actions; the API remains the only enforcement point). _[fill in: confirm that you are genuinely fluent in Next.js; if not, choose the SPA]_
- **Verified by:** _[✓ when done]_ the walking skeleton in ADR-0002 §6.

**ADR-0003 (Quality Attributes → Performance)**
- **Asked AI to:** decide the performance approach across the front end, API, database and cloud, including rendering pattern, caching, concurrency, and TanStack Query vs Redux.
- **Accepted:** the principle "proven defaults, measure, then optimise"; static shell + client data fetching; TanStack Query; client-side and static-asset caching only; async I/O; one API service; owner-first indexes.
- **Checked:** the derived budgets (other than Q3) are marked as design choices, not requirements. _[fill in: do you agree with them?]_
- **Verified by:** _[✓ when done]_ the Q3 query-plan test and the telemetry p95 check in ADR-0003 §6 (load tests deferred, ADR-0011 D7).
- **Follow-up question I raised (ADR-0003 §3.7):** does read performance need CQRS? Added ADR-0003 §3.7. The answer is no, with the CQRS spectrum explained: only light command/query separation in code is adopted, and separate read models are rejected because of freshness (read-your-writes), identical read and write shapes, and modifiability. Explicit triggers to revisit are recorded.

**ADR-0004 (Quality Attributes → Scalability)**
- **Asked AI to:** decide the scalability approach across the front end, back end, database and cloud, including REST vs GraphQL vs MVC, async messaging, horizontal vs vertical scaling and autoscaling, and monolith vs microservices.
- **Accepted:** X-axis scaling of stateless tiers; a vertical database with a bounded replica rule; CPU-based HPA (min 2, bounded max); REST; synchronous only; a modular monolith.
- **Checked:** the workload figures in §1.3 are marked as assumptions, not requirements; the connection-limit numbers are illustrative. _[fill in: replace with the real tier limit once chosen]_
- **Verified by:** _[✓ when done]_ the manual scale drill, HPA review and statelessness check in ADR-0004 §6.

**ADR-0005 (Quality Attributes → Testability and maintainability)**
- **Asked AI to:** decide on SOLID, DI, Moq and mocking, design patterns, and whether "add a status easily" belongs in scope.
- **Accepted:** pragmatic SOLID at real seams; vertical slices; no repository, MediatR or AutoMapper; the built-in DI container; real database in tests; mocking only at external boundaries (NSubstitute); a declarative transition table with `allowedTransitions` served by the API.
- **Checked:** the claims about Moq's 2023 incident and the MediatR / AutoMapper licence change. _[✓ when you've verified these yourself: interviewers may ask]_
- **Verified by:** _[✓ when done]_ the timed drills (add a field; add a status) in ADR-0005 §6. **Do the add-a-status drill by hand: it's a likely live task.**

**ADR-0006 (Data Architecture)**
- **Asked AI to:** decide on EF Core vs Dapper, code-first vs database-first, and PostgreSQL vs SQL Server / Azure SQL.
- **Accepted:** EF Core (automatic owner filter and concurrency); code-first with review of the generated SQL; a migration bundle run as a Kubernetes Job; expand / contract migrations; database constraints alongside validation; UUID v7 keys; `xmin` as the concurrency token; separate database roles.
- **Not reopened:** the data store choice (ADR-0002 §3.4), which is referenced instead to avoid two sources of truth.
- **Issue surfaced:** BR-1 says non-scheduled items have no date, so `done` items would lose their scheduled day. _[decide and update requirements.md]_
- **Review rule adopted:** I review the generated SQL of every migration, including AI-generated ones, before merging.

**ADR-0007 (Component & Structural)**
- **Asked AI to:** decide on Api → Application → Domain → Infrastructure layering, modular monolith vs microservices, and related structure.
- **First draft (rejected by me):** the AI proposed logical layers inside vertical slices, in a single project, following its own earlier ADR-0005.
- **My decision:** Clean Architecture with four projects (`Todo.Api`, `Todo.Application`, `Todo.Domain`, `Todo.Infrastructure`), which is what my repository already had, plus feature folders under `web/src/components/`. _[fill in your reasons in your own words, e.g. a compiler-enforced dependency rule, familiarity to reviewers]_
- **How the conflict was handled:** ADR-0007 was revised with a change log, and ADR-0005 §3.2 was marked superseded rather than left contradicting it. ADR-0005's guardrails (no MediatR, AutoMapper or repository) were kept to limit ceremony, and the Q5 change-cost target was updated honestly (about 14 files instead of about 8).
- **Verified by:** _[✓ when done]_ the timed add-a-field drill following ADR-0007 §3.10.

**ADR-0008 (Cross-cutting concerns)**
- **Asked AI to:** decide on observability (logging, metrics, tracing, alerting), error handling and exception strategy, configuration and feature flags.
- **Accepted:** built-in logging with JSON output and strict content rules; OpenTelemetry instrumented now with export off by default (respecting the requirements' deferral of advanced monitoring); RFC 9457 ProblemDetails with a stable `code`; outcomes vs exceptions; FluentValidation through one endpoint filter; fail-fast options validation; no feature flags yet.
- **Checked:** the derived SLOs (99.5%, <1% errors) are marked as design choices, not requirements. _[fill in: agree?]_
- **Verified by:** _[✓ when done]_ the log-capture test (no secrets or addresses in logs) and the no-stack-trace test in ADR-0008 §6.
- **Changed by me:** replaced the AI's outcome types and FluentValidation filter with **ASP.NET Core's built-in ProblemDetails** (`AddProblemDetails()`, RFC 9457) and **one `IExceptionHandler` (`TodoExceptionHandler`)** mapping `KeyNotFoundException` → 404, `InvalidTodoTransitionException` → 409, `DbUpdateConcurrencyException` → 409, `ArgumentException` → 400, and anything else → 500. This uses the framework's mechanism instead of custom error middleware, and validation lives in domain guard clauses (one source of rules).
- **Trade-off I accepted, with safeguards:** mapping broad framework exception types can misclassify bugs (ADR-0008 R6).
- **Changed by me:** no `traceId` in error responses, because OpenTelemetry already correlates logs, traces and metrics. The framework adds `traceId` by default, so it's removed in `CustomizeProblemDetails`, and a test guards it. Accepted cost: a user's error report can't be matched directly to its trace (ADR-0008 R8).
- **Changed by me:** no `ILogger` in application code. Automatic instrumentation earns its keep at instrumented library boundaries, and a single, centralised `ILogger` call at the one true chokepoint (`TodoExceptionHandler`) covers what instrumentation structurally can't reach: application-level exceptions with no span of their own.
- **Consequences worked through with AI:**
  - Telemetry export must now be on wherever the system runs (it's required configuration in Production and validated at start-up), because spans and metrics are the only signal for normal traffic.
  - HTTP request logging was removed.
  - ADR-0001's detection layer moved from per-user sign-in logs to sign-in-route metrics.
  - An architecture test enforces the single-`ILogger` rule.
  - New risks were recorded: R9 (blind if export is off) and R10 (weaker sign-in detection).
- **Changed by me (ADR-0007):** a controller-based RESTful API (`[ApiController]` controllers, one per resource) instead of minimal APIs. My reasoning: _[fill in: e.g. familiar conventions, class-level routing and authorization]_. Worked through with AI:
  - With Clean Architecture, controllers are just the HTTP adapter over Application handlers.
  - Handlers are injected per action with `[FromServices]`.
  - `[ProducesResponseType]` keeps the OpenAPI types accurate.
  - `[ApiController]`'s automatic 400s must follow the ADR-0008 error contract.

**ADR-0009 (Communication & interaction)**
- **Asked AI to:** decide protocol choices and request/response consistency.
- **Accepted:** protocols per link; REST level 2 with `allowedTransitions` as the one affordance; the endpoint catalogue; `PUT /todos/{id}/state` for lifecycle, separate from the details update; representation rules (camelCase, lowercase enums, explicit nulls, a collection envelope); `ETag`/`If-Match` required on updates (428 if missing); no URL versioning, with additive-only evolution and API-first deploys; OpenAPI generated and committed, with a CI drift check.
- **Naming confirmed by me:** `/todos/{id}/state` (a resource, a noun) rather than `/todos/{id}/transition` (an action or event, RPC style). "Transition" stays an internal domain term (`TodoTransitions`, `InvalidTodoTransitionException`, `allowedTransitions`).

**ADR-0010 (Deployment & operations)**
- **Asked AI to:** decide on CI/CD, GitHub Actions vs Azure Pipelines, App Gateway vs APIM vs ingress controller, AKS vs ACA/ACI/App Service, OpenAPI, environment strategy and release strategy.
- **Accepted:**
  - AKS (the only option meeting C-6/C-7)
  - ~~Gateway API with Traefik~~, replaced by my decision below (APIM rejected; Application Gateway for Containers deferred)
  - cert-manager with Let's Encrypt, on a free Azure DNS label
  - GitHub Actions with OIDC and push-based Helm deploys
  - four workflows, with two app charts for API-first ordering
  - Key Vault as the pipeline's secret source, with Kubernetes Secrets created outside Helm
  - local + CI + prod environments, with staging one values file away
  - rolling updates with `--atomic`, and canary deferred
- **Changed by me:** the edge uses Kubernetes `Ingress` with NGINX (`nginx.ingress.kubernetes.io` annotations). _[fill in your reason, e.g. familiarity with the annotations]_
  - The AI flagged that the community ingress-nginx project is retired, so no more security fixes (R8).
  - We chose the **Microsoft-managed AKS application routing add-on** as the controller: same annotations, patched with AKS.
  - Gateway API is recorded as the migration target.
  - _[✓ confirm the add-on's current support window]_
- **To verify myself before the interview:** _[✓]_ run the full pipeline end to end (R7); _[✓]_ a deliberately failing release rolls back; _[✓]_ start the cluster and database well ahead (R6).

**ADR-0011 (Testability strategy and testing tooling)**
- **First draft (AI):** a tool selection that included `FakeTimeProvider` and MSW.
- **Regenerated from my own ADR draft:**
  1. no generic repository layer
  2. Testcontainers over in-memory, SQLite or a shared database, because Q6's `xmin` can't be tested honestly against SQLite
  3. the real login in tests, with no auth bypass
  4. **one narrow `IClock` port** instead of `TimeProvider`
  5. **`fetch` mocks now, MSW deferred with a trigger**

  My right-sizing test: *does the abstraction solve a problem that exists here, or add indirection for its own sake?*
- **Reconciled by AI:**
  - my numbering (Q8–Q11, ADR-0003/0004/0007…) was mapped to this repository's requirements and ADRs
  - `IAppDbContext` (ADR-0007) is kept as a non-repository interface, to preserve the dependency rule
  - ADR-0005's time seam and front-end MSW were marked superseded, and ADR-0006, ADR-0007 and ADR-0008 updated
  - a rule banning direct `UtcNow` calls was added (R4)
- **To check myself:** _[✓]_ the licence claims (FluentAssertions v8; CodeQL availability for my plan); _[✓]_ the integration stage time with one shared container.

**ADR-0012 (Evolution & extensibility)**
- **Asked AI to:** decide versioning of APIs and data, and other evolution concerns for this project.
- **Accepted:**
  - one compatibility rule, N / N−1, for the API, schema, tokens and configuration
  - `oasdiff breaking` in CI, which enforces "additive only"
  - URL-segment versioning with Asp.Versioning when a break is unavoidable
  - a deprecation policy (`Deprecation` / `Sunset` headers; removal at zero traffic)
  - forward-only migrations with large backfills as separate Jobs
  - `kid`-based key rotation without signing users out
  - chunk-load reload for client version skew
  - SHA images + SemVer release tags
  - a quarterly platform lifecycle calendar
  - nine named extension points, and an explicit list of speculative extensibility **not** built
  - a 20-item evolution register with observable triggers
- **To check myself:** _[✓]_ the RFC numbers for the `Deprecation` (RFC 9745) and `Sunset` (RFC 8594) headers; _[fill in: which register items I'd mention if asked "what would you do next?"]_

**architecture.md (Phase 1 wrap-up)**
- **Asked AI to:** generate `architecture.md` once all decisions were made.
- **Contents:**
  - the system at a glance
  - drivers and the utility tree
  - eight design principles distilled from the ADRs
  - container, component, deployment, data and runtime views
  - a quality-attribute → tactic → mechanism → test table
  - a one-line summary per ADR
  - consolidated ATAM analysis, grouping the individual risks into seven risk themes
  - open items
  - requirements → design traceability
- **Rule stated in the document:** if it disagrees with an ADR, the ADR wins.
- **To do myself:** _[✓]_ check every diagram against the code as it's built; _[✓]_ resolve open items O1–O10 (§9).

**Decision by me: no Playwright and no k6 this time (ADR-0011 D6, D7)**
- **What's explicitly deferred, and why:**
  - **MSW:** front-end component tests stub `fetch` directly (`vi.stubGlobal("fetch", vi.fn())`) rather than intercepting at the network layer. This is simpler for the current scope, and MSW is the natural upgrade for more complex request/response sequences.
  - **No end-to-end browser automation** (Playwright, Cypress, …). Automated testing stops at the component level on the front end (Vitest + Testing Library) and the integration level on the back end (Testcontainers + `WebApplicationFactory`). There's no automated test that drives a real browser through the full stack; those journeys are verified manually, as they have been throughout the deployment work.
  - **No load-test automation** (k6, Lighthouse CI). Q3 is verified by a deterministic query-plan test plus telemetry; Q4 and Q7 by manual drills.
- **Consequential updates:**
  - ADR-0011 gained D6 and D7, updated CI stages and verification map, and new risks R7 and R8.
  - ADR-0003, 0004, 0005, 0010 and 0012 were updated; the register gained V21 (Playwright) and V22 (k6) with triggers.
  - `requirements.md` §5.3, §10.3 and §12 were updated.
  - `architecture.md` gained risk theme RT8.
- **Note:** my manual checks mention a "register" flow, but `requirements.md` A10 says users are pre-seeded (no self-registration). _[decide: update A10 / FR-13, or rename the flow]_

**design.md**
- **Asked AI to:** create `design.md` from `requirements.md` and `architecture.md`.
- **Contents:**
  - domain types, the state matrix and guard and rule tables
  - use cases with their steps
  - the API catalogue, representations and error sources
  - the physical schema (DDL + indexes) and EF configuration
  - security details (JWT, `kid`, cookie, rate limit, headers)
  - options and the `Program.cs` pipeline
  - front-end routes, feature folders, data layer and forms
  - Helm, Terraform, secrets and workflows
  - the test catalogue and traceability
- **Decisions made at design level, marked *(initial)* or noted:**
  - rate-limit values
  - index column order
  - react-hook-form + zod
  - clearing `scheduledFor` on unschedule and complete, per BR-1 as written (O2)
  - rejecting `scheduledFor` for non-scheduled states
- **To check myself:** _[✓]_ the DDL matches the generated migration SQL; _[✓]_ O2 and O11 resolved.
- **Deliberate deviation to be ready to explain:** a stale `If-Match` returns **409**, not HTTP's 412, so all conflicts share one status and path through `TodoExceptionHandler` (T3). _[decide: keep 409, or switch to 412]_ _[decide: keep the broad types, or throw domain subclasses such as `TodoNotFoundException : KeyNotFoundException`]_