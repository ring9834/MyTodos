# ADR-0002: Core technology selection

| | |
|---|---|
| **Status** | Proposed |
| **Date** | 2026-09-29 |
| **Decision area** | Technology & Tooling → **core stack** |
| **Method** | Attribute-Driven Design (selection against drivers); analysed with ATAM concepts |
| **Related** | `docs/requirements.md`: C-1 to C-7, A0, A5, Q1–Q8, NFR-1, NFR-7, NFR-8, P-2, P-5; ADR-0001 §6–§7 |
| **Supersedes** | The earlier draft of this ADR, which assumed a stack without evaluating it |

---

## 1. Context

### 1.1 Scope

This ADR selects the four **interdependent core technologies**:

1. the frontend framework
2. the backend language and framework
3. the cloud platform
4. the data store

These are decided together because each affects the others. For example, the cloud determines the managed database options, and the frontend framework determines the future BFF path from ADR-0001.

Other technology choices are made in the ADR for their own decision area, where their trade-offs are clearest:

| Technology concern | Decided under |
|---|---|
| Edge / TLS termination, CI/CD platform, Terraform and Helm layout | Deployment & Operations |
| Test frameworks and tools | Quality Attributes → Testability |
| Logging, validation and configuration libraries | Cross-cutting Concerns |
| ORM and data-access patterns, migrations | Data Architecture |

### 1.2 What kind of choice each one is

| Concern | Kind | Options allowed |
|---|---|---|
| UI platform | **Constrained** (C-1) | React website, **or** React Native / Flutter mobile app. **Web** is fixed by assumption A5 |
| React framework / tooling | **Open** (C-1 allows "any other supporting libraries") | Any React-based approach |
| Backend | **Constrained** (C-2, depends on role, A0) | .NET; Java if the role specifies it; Node.js / Python / Rails for non-backend roles |
| Cloud | **Constrained** (C-4) | AWS or Azure |
| Data store | **Open** (the brief is silent) | Any |
| Terraform, Kubernetes, Helm | **Mandated** (C-5 to C-7) | Not a selection; the choices *within* them are made under Deployment & Operations |

### 1.3 Selection criteria

| # | Criterion | Derived from | Weight |
|---|---|---|---|
| K1 | **Eligibility:** satisfies the constraints for my role, with no ambiguity | C-1, C-2, C-4, A0 | Gate (must pass) |
| K2 | **Capability fit:** provides what earlier decisions require, e.g. ADR-0001 §7 (vetted password hashing, JWT validation with algorithm pinning, rate limiting, a central query filter) | ADR-0001, Q1–Q8 | High |
| K3 | **Live changeability and my fluency:** I can change it quickly and correctly by hand under observation | P-2, Q5 | High |
| K4 | **Local testability:** the same technology runs in tests and locally | NFR-1, NFR-7 | High |
| K5 | **Operational simplicity** on Kubernetes | C-6, Q4, Q7 | Medium |
| K6 | **Cost** within the free-trial credit | P-5, NFR-8 | Medium |
| K7 | **Evolution:** supports the planned paths (federation/BFF, mobile client, location features) | ADR-0001 §6, requirements §10.3 | Medium |
| K8 | **Maturity and support lifetime** | Production readiness (C-8) | Medium |

Ratings in the tables below: **++** strong, **+** good, **0** neutral, **−** weak, **✗** fails the gate.

---

## 2. Decision summary

| Concern | Decision |
|---|---|
| Frontend | **Next.js (React), used conservatively:** client-side data fetching through the API, no Server Actions, standalone container output (§3.1) |
| Backend | **.NET (current LTS) with ASP.NET Core** |
| Cloud | **Azure** |
| Data store | **PostgreSQL**, as a **managed cloud service** (Azure Database for PostgreSQL – Flexible Server) |
| Follow-on | Managed Kubernetes on Azure means **AKS**; the details are set in Deployment & Operations |

---

## 3. Options and trade-offs

### 3.1 Frontend framework (web UI written in React)

| Criterion | **A. React SPA** (Vite build, static files) | **B. Next.js** (React framework with a server) | C. React Router framework mode (formerly Remix) |
|---|---|---|---|
| K1 Eligibility | ✅ | ✅ (React-based; C-1 allows supporting libraries) | ✅ |
| K2 Capability fit | + Headers and route guards need the static server or edge | + Headers, middleware and rewrites built in | + Similar to B |
| K3 Fluency and live changes | ++ One mental model: everything runs in the browser | + if fluent; − if not: server vs client components, caching rules | 0 Less common; smaller community |
| K4 Local testability | ++ Vitest + Testing Library; nothing server-side to mock | + Same tools for client components; server parts need extra care | + |
| K5 Operational simplicity | ++ Static files; a tiny non-root web server container | 0 A Node.js server process: more memory, a runtime to patch, probes to configure | 0 Same as B |
| K6 Cost | ++ Minimal resources | + Modest resources | + |
| K7 Evolution (BFF for federation, ADR-0001 §6) | − Needs an extra server component later | ++ Can host the BFF directly | + Can host a BFF |
| K8 Maturity | ++ | ++ Very widely used | + |
| Security surface | ++ No server-side code in the web tier | − Server-side features add surface. 2025 saw serious advisories in Next.js middleware and React Server Components, so patching discipline matters | − Similar to B |
| Not needed here | — | SSR/SEO benefits are irrelevant: the app is behind sign-in | Same |

**Decision: B (Next.js), used conservatively.**

A and B are close. A is simpler to run and has a smaller attack surface. B wins on the evolution path (it can host the future BFF) and on alignment with my stated preference and familiarity (K3). K3 carries high weight, because the interview requires unassisted live changes.

To keep B's costs low, these **guardrails** apply:

| Guardrail | Why |
|---|---|
| **No Server Actions and no server-side calls to the database.** All data goes through the API (`/api/*`) | The API remains the **only** enforcement point for authorization (ADR-0001 §4.4). Server Actions would create a second, unguarded API surface |
| Data fetching happens in client components (e.g. TanStack Query) | One data path; the cookie is sent by the browser (ADR-0001 S4); no server-side cookie forwarding needed |
| Middleware is for UX redirects only, never for authorization | ADR-0001 §4.4, reinforced by the 2025 middleware-bypass advisory |
| `output: 'standalone'` container, running as non-root | A small image; consistent with ADR-0001 layer 7 |
| Dependency updates are automated and security advisories monitored | Server-side framework surface (see Risks) |

**What would flip this decision:** if I'm not already fluent in Next.js, choose **A**. Its costs (the BFF comes later) are smaller than the risk of struggling with an unfamiliar framework live.

### 3.2 Backend language and framework

| Criterion | **A. .NET / ASP.NET Core** | B. Java / Spring Boot | C. Node.js (e.g. NestJS/Fastify) | D. Python (FastAPI/Django) | E. Ruby on Rails |
|---|---|---|---|---|---|
| K1 Eligibility | ✅ Allowed for **every** role | ✅ only if the role specifies Java | ⚠️ Only for non-backend roles | ⚠️ Same as C | ⚠️ Same as C |
| K2 Capability fit (ADR-0001 §7) | ++ All built in: `PasswordHasher`, JWT bearer, rate limiter, EF Core global query filters | ++ Spring Security; Hibernate filters | + Via libraries | + Via libraries | + Via gems |
| K3 Live changeability | ++ Compile-time safety catches mistakes before running; minimal APIs are concise | + Safe but more ceremony | + Fast, but less type safety at runtime (unless strict TS) | + | + |
| K4 Local testability | ++ `WebApplicationFactory` + Testcontainers: real HTTP, real database | ++ Spring Boot Test + Testcontainers | + | + | + |
| K5 Operational simplicity | + A small container; fast start-up | 0 Higher memory and start-up (matters on a small trial node) | ++ Light | + | 0 |
| K8 Maturity and support | ++ LTS release with a 3-year support window | ++ | ++ | ++ | + |

**Decision: A (.NET, current LTS, ASP.NET Core).** It is the only option that passes K1 regardless of role, and every capability ADR-0001 needs is built into the framework, which means less code to explain. **If the role specifies Java, this row is superseded by B;** the rest of this ADR is unaffected.

### 3.3 Cloud platform

| Criterion | **A. Azure** | B. AWS |
|---|---|---|
| K1 Eligibility | ✅ | ✅ |
| K5 Managed Kubernetes | + AKS; the free tier has no control-plane charge | + EKS; the control plane is billed hourly |
| K6 Cost within the trial | ++ The interview notes explicitly suggest a 30-day Azure trial is sufficient | 0 Free-plan credits exist, but the EKS control plane consumes them continuously |
| K2 Required services | ++ AKS, managed PostgreSQL, container registry, workload identity federation for CI | ++ EKS, RDS, ECR, OIDC for CI |
| Terraform support | ++ `azurerm` provider | ++ `aws` provider |
| Risk | − Trial subscriptions have tight vCPU quotas | − Cost exposure outside free limits |

**Decision: A (Azure).** The deciding factors are the interview notes' own guidance and cost. The two platforms are otherwise functionally equivalent for this system, and nothing in ADR-0001 depends on the choice.

### 3.4 Data store

| Criterion | **A. PostgreSQL** | B. Azure SQL / SQL Server | C. Cosmos DB (document) | D. MySQL |
|---|---|---|---|---|
| Data shape fit | ++ Relational: users → items; filter, sort and page queries (Q3) | ++ Relational | − Document model; the relational queries and ownership filter are less natural | ++ Relational |
| K2 Capability fit | ++ Built-in row-version column for concurrency (Q6); indexes; a mature EF Core provider | ++ Rowversion; first-class EF Core | 0 ETags for concurrency; its EF Core provider is more limited | + |
| K4 Local testability | ++ The same engine in a lightweight container for tests | 0 The SQL Server container is heavy (~2 GB RAM) | − Emulator is heavy | ++ |
| K6 Cost | + Small burstable managed tier | + Serverless and free-offer tiers exist | − Throughput-based pricing is easy to overspend | + |
| K7 Evolution | ++ **PostGIS** for location features (nearby jobs, route ordering, FR-16); portable to any cloud | + Spatial types; Azure-centric | 0 Geospatial queries exist | + Spatial support |
| Lock-in | ++ Open source; managed on every cloud | 0 | − Azure-specific | ++ |

**Decision: A (PostgreSQL).** It fits the relational shape, is identical in tests and in production (K4), and has the strongest evolution story for a *location*-based app (PostGIS).

**Hosting model: managed service vs in-cluster.**

| Option | Pros | Cons |
|---|---|---|
| **Managed (Azure Database for PostgreSQL – Flexible Server)** | Backups, patching and storage are handled; the database lives outside the cluster's lifecycle, so a cluster rebuild can't lose data | A cost per hour; network access needs configuring (ADR-0001 R5) |
| In-cluster (a StatefulSet via a Helm chart) | Cheaper; everything is in Helm | We own backups, upgrades and persistent volumes; data is tied to the cluster; not production-ready without significant work |

**Decision: managed.** Production readiness (C-8) outweighs the small cost. Local development and tests still use a PostgreSQL container.

---

## 4. ATAM analysis

### 4.1 Sensitivity points

| # | Sensitivity point | Attributes affected |
|---|---|---|
| S1 | **Next.js usage discipline.** Security (single enforcement point) and modifiability both depend on keeping to the guardrails in §3.1 | Security, Modifiability |
| S2 | **Database tier size.** Q3 (p95 < 200 ms) and Q7 depend on it, because the API scales out but the database scales only up | Performance, Scalability, Cost |
| S3 | **Trial vCPU quota.** Node count and size, and therefore replica count and Q4 drills, depend on it | Availability, Scalability |
| S4 | **My fluency with each technology** | Modifiability (Q5), interview outcome |

### 4.2 Trade-offs

| # | Trade-off | Chosen side | Accepted cost |
|---|---|---|---|
| T1 | Next.js vs a static SPA | Evolution (BFF) and fluency | A server runtime to operate and patch; a larger attack surface (R1) |
| T2 | A managed database vs in-cluster | Production readiness; data outlives the cluster | Hourly cost; network setup |
| T3 | .NET vs the lighter Node/Python options | Eligibility for every role; built-in security capabilities; type safety | A slightly heavier runtime than Node |
| T4 | PostgreSQL vs Azure SQL | Portability, lightweight tests, PostGIS | Less "native" Azure tooling |

### 4.3 Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | A server-side framework vulnerability in Next.js | Guardrails (§3.1); automated dependency updates; the API is the only enforcement point, so a web-tier flaw can't bypass authorization |
| R2 | The trial quota blocks the planned cluster size | Provision infrastructure early; the smallest viable node size; a fallback region |
| R3 | The role turns out to require Java | Only the backend row changes; the tactics in ADR-0001 are stack-neutral |
| R4 | The database is a single point of failure on a burstable tier | Accepted for the MVP; HA or zone-redundant options noted for production |

### 4.4 Non-risks

| # | Non-risk | Because |
|---|---|---|
| N1 | ADR-0001's capabilities are unavailable in the stack | All are built into ASP.NET Core and EF Core |
| N2 | Tests diverge from production behaviour because of the database | The same PostgreSQL engine runs in tests (Testcontainers) and in production |
| N3 | Cloud lock-in blocks a move to AWS | Kubernetes, Helm, PostgreSQL and a standards-based API are all portable; only Terraform modules would change |

---

## 5. Consequences

**Positive**
- Every constraint is met for any role type, except the Java-specific case (R3).
- The security tactics of ADR-0001 map onto built-in framework features.
- Tests run against the real database engine.
- A clear location-feature path exists (PostGIS).

**Negative / follow-ups**
- The web tier is a Node.js server, not static files. The Deployment ADR must provide it with probes, resources and patching.
- ADR-0001's Appendix A can now be read as the chosen realisation (backend: .NET column; frontend: the Next.js option).

### Impacts on other decision areas

| Decision area | Constraint or input from this decision |
|---|---|
| **Deployment & operations** | AKS; Azure Database for PostgreSQL – Flexible Server; two application images (a Next.js standalone image and a .NET image); edge/TLS and CI/CD tools still to choose |
| **Data architecture** | PostgreSQL; ORM and migration approach still to choose (EF Core is the natural fit) |
| **Communication & interaction** | Same-origin routing can use the edge or Next.js rewrites; still to choose |
| **Component & structural** | Next.js App Router with client-side data fetching; API structure still to choose |
| **Testability** | .NET: xUnit, `WebApplicationFactory`, Testcontainers. Web: Vitest, Testing Library. To be confirmed in the Testability ADR |
| **Evolution & extensibility** | BFF hosted in Next.js (ADR-0001 §6); PostGIS for location features |

---

## 6. Verification: a walking skeleton

Before building features, prove the stack works end to end with the thinnest possible slice. This retires R1–R2 early.

| Check | Proves |
|---|---|
| A Next.js page calls `/api/health` through same-origin routing | The frontend, routing and API are wired together |
| An API integration test runs against Testcontainers PostgreSQL | K4 |
| Both images build, run as non-root, and pass their probes locally | K5 |
| `terraform apply` provisions AKS and PostgreSQL within the trial quota | R2, K6 |
| The skeleton deploys to AKS with Helm and responds over the public entry point | The end-to-end path |

## 7. Revisit when

- The role requires Java (R3).
- Next.js fluency proves weaker than expected during the walking skeleton. Switch to the static SPA while the cost is still low.
- Location features become a priority (enable PostGIS), or scale needs outgrow a single database instance.
