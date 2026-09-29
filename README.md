# Architecture Decision Records: index and decision map

This folder holds the project's Architecture Decision Records (ADRs). Each ADR records one significant decision: its context, the options considered, the decision, and its consequences. The decisions are organised into nine **decision areas**.

## Conventions

- **Numbers are chronological and never reused.** A number records *when* a decision was made, and the decision area is metadata. If a decision changes, a new ADR supersedes the old one, and the old one is kept.
- **Status values:** Proposed → Accepted → (Superseded by ADR-nnnn | Deprecated).
- **Drivers vs decision areas.** Quality attributes, functional requirements and constraints (`docs/requirements.md`) are the *drivers*. The eight areas below are where decisions are *made* in response to them. The Quality Attributes area chooses the **tactics** for each attribute; the other areas choose the **mechanisms** that implement them. Each ADR lists the constraints it places on other areas.
- **Method.** Attribute-Driven Design (ADD) chooses what to decide and in which order (highest-priority drivers first). ATAM concepts are used to analyse each decision: sensitivity points, trade-offs, risks and non-risks.

## Decision map

| Area | Key questions | Main drivers | ADR | Status |
|---|---|---|---|---|
| **1. Quality attributes** | | | | |
| → Security | How do users sign in? How is a session carried? How is ownership enforced? Which defence layers apply? | Q1, Q8, NFR-5, NFR-6 | [0001](0001-security-authentication-and-authorization.md) | Proposed |
| → Scalability | Scale dimensions (AKF cube); horizontal vs vertical; autoscaling; API style; messaging; monolith vs microservices; database limits | Q7, Q3, Q4 | [0004](0004-scalability.md) | Proposed |
| → Performance | Rendering pattern; client state; caching layers; concurrency; paging, indexing, query shape; cloud placement | Q3, Q7, FR-7 | [0003](0003-performance.md) | Proposed |
| → Availability | Replicas, probes, rolling updates, disruption budgets, rollback | Q4, NFR-6 | Covered by [0004](0004-scalability.md) and [0010](0010-deployment-and-operations.md) | Proposed (no separate ADR) |
| → Data integrity | Concurrency control; where state rules are enforced | Q2, Q6, BR-1 to BR-6 | Covered by [0005](0005-testability-and-maintainability.md) §3.6, [0006](0006-data-architecture.md), [0009](0009-communication-and-interaction.md) §3.5 | Proposed (no separate ADR) |
| → Testability and maintainability | SOLID; code organisation; DI; test seams; mocking policy; test strategy; designing for change (adding a status) | NFR-1, NFR-2, Q5, Q2 | [0005](0005-testability-and-maintainability.md) | Proposed (§3.2 superseded by 0007; time seam and front-end MSW superseded by [0011](0011-testability-strategy-and-testing-tooling.md)) |
| **2. Data architecture** | Data access (EF Core vs Dapper); code-first vs database-first; running and evolving migrations; physical model; integrity; concurrency; roles; seeding; lifecycle and personal data | BR-1 to BR-6, Q1, Q3, Q4, Q5, Q6 | [0006](0006-data-architecture.md) | Proposed |
| **3. Deployment & operations** | Hosting (AKS vs ACA/ACI/App Service); edge (NGINX Ingress vs Gateway API vs App Gateway vs APIM); TLS; CI/CD (GitHub Actions vs Azure Pipelines); pipelines; images; Helm; Terraform; secrets delivery; environments; release strategy; operations | C-4 to C-7, Q4, NFR-4, NFR-5, NFR-10 | [0010](0010-deployment-and-operations.md) | Proposed |
| **4. Component & structural** | Containers; Clean Architecture projects (Api / Application / Domain / Infrastructure); modules as feature folders per layer; `IAppDbContext`; plain handlers; controller-based RESTful API; rich domain; front-end feature folders | Q5, Q2, P-3, NFR-1 | [0007](0007-component-and-structural.md) | Proposed (supersedes 0005 §3.2) |
| **5. Communication & interaction** | Protocols per link; REST level 2; endpoint catalogue and status codes; representation rules; `ETag`/`If-Match`; idempotency and retries; content negotiation; versioning and evolution; OpenAPI contract and drift check | C-3, Q1, Q2, Q4, Q6, NFR-3 | [0009](0009-communication-and-interaction.md) | Proposed |
| **6. Cross-cutting concerns** | Observability (logging, correlation, metrics, tracing, alerting); error taxonomy and contract; exception strategy; validation; configuration and secrets; feature flags; health checks | NFR-2, NFR-3, NFR-5, NFR-6, Q3, Q4 | [0008](0008-cross-cutting-concerns.md) | Proposed |
| **7. Technology & tooling** | Core stack (frontend, backend, cloud, data store); testing and quality tooling; other tools chosen in their own area's ADR | C-1, C-2, C-4, A0, ADR-0001 §7, NFR-1 | [0002](0002-technology-stack.md), [0011](0011-testability-strategy-and-testing-tooling.md) | Proposed |
| **8. Evolution & extensibility** | The N / N−1 compatibility rule; API versioning (enforced additive now; URL segment later); deprecation; schema and data versioning; token key rotation; configuration and client skew; artifact and platform versioning; extension points; evolution register | Q4, Q5, requirements §10.3 | [0012](0012-evolution-and-extensibility.md) | Proposed |

## Suggested order for the decisions

ADD takes the highest-priority drivers first, then the decisions that everything else depends on:

1. **Technology & tooling:** core stack, ADR-0002 (done).
2. **Component & structural:** containers and internal structure ([ADR-0007](0007-component-and-structural.md), done).
3. **Communication & interaction:** the API contract ([ADR-0009](0009-communication-and-interaction.md), done).
4. **Data architecture** ([ADR-0006](0006-data-architecture.md), done), together with **data integrity** (Q2, Q6: covered by ADR-0005 §3.6, ADR-0006 and ADR-0009 §3.5) and **performance** (Q3, [ADR-0003](0003-performance.md), done).
5. **Deployment & operations** ([ADR-0010](0010-deployment-and-operations.md), done), together with **availability** (Q4: covered by ADR-0004 and ADR-0010) and **scalability** (Q7, [ADR-0004](0004-scalability.md), done).
6. **Cross-cutting concerns** ([ADR-0008](0008-cross-cutting-concerns.md), done), **testability** and **maintainability** ([ADR-0005](0005-testability-and-maintainability.md) and [ADR-0011](0011-testability-strategy-and-testing-tooling.md), done).
7. **Evolution & extensibility:** versioning rules, extension points and the evolution register ([ADR-0012](0012-evolution-and-extensibility.md), done).

Once every area has been decided, the ATAM analysis from each ADR is combined into `docs/architecture.md`. It also covers the risks that span areas: risk themes that appear in several ADRs.

## Technology summary

The whole stack in one place, with the ADR that chose each technology.

| Technology | Kind | Chosen in |
|---|---|---|
| Terraform, Kubernetes, Helm | Mandated by the brief (C-5 to C-7) | — |
| Next.js (React), used with guardrails | Open choice within C-1 | [0002](0002-technology-stack.md) |
| .NET / ASP.NET Core | Constrained choice (C-2) | [0002](0002-technology-stack.md) |
| Azure (AKS) | Constrained choice (C-4) | [0002](0002-technology-stack.md) |
| PostgreSQL (managed) | Open choice | [0002](0002-technology-stack.md) |
| TanStack Query (server state); URL parameters for filters | Open choice | [0003](0003-performance.md) |
| Load testing (e.g. k6); front-end budget checks (e.g. Lighthouse CI) | Open choice | [0003](0003-performance.md) |
| xUnit v3, Shouldly, NSubstitute (rarely), `IClock` + `FakeClock`, `FakeLogger`, `WebApplicationFactory`, Testcontainers (one container per run), Respawn, ArchUnitNET; Vitest (jsdom), Testing Library, `fetch` spies (**MSW deferred**), vitest-axe, Playwright (+ axe); k6; Lighthouse CI; size-limit; gitleaks, Trivy, Dependabot, CodeQL, tflint, kubeconform | Open choice | [0011](0011-testability-strategy-and-testing-tooling.md) |
| `oasdiff` (OpenAPI breaking-change check); Asp.Versioning (when a v2 is needed); SemVer release tags + Conventional Commits | Open choice | [0012](0012-evolution-and-extensibility.md) |
| Built-in .NET DI container | Open choice | [0005](0005-testability-and-maintainability.md) |
| ASP.NET Core MVC controllers (`[ApiController]`) | Open choice | [0007](0007-component-and-structural.md) |
| REST over HTTP/JSON (API style) | Open choice | [0004](0004-scalability.md) |
| Kubernetes Horizontal Pod Autoscaler | Open choice (within mandated Kubernetes) | [0004](0004-scalability.md) |
| HTTPS/HTTP/2 at the edge; `ETag`/`If-Match`; ASP.NET Core OpenAPI (build-time) + `openapi-typescript` | Open choice | [0009](0009-communication-and-interaction.md) |
| AKS (Cilium network policy, workload identity); NGINX Ingress (`nginx.ingress.kubernetes.io`, AKS application routing add-on); cert-manager + Let's Encrypt; GitHub Actions (OIDC); ACR; Key Vault (pipeline secrets); two Helm charts | Constrained / open choices | [0010](0010-deployment-and-operations.md) |
| EF Core + Npgsql; code-first migrations run as a migration bundle; snake_case naming convention | Open choice | [0006](0006-data-architecture.md) |
| Built-in .NET logging (JSON console); OpenTelemetry (OTLP / Azure Monitor, export off by default); Aspire dashboard (local); ASP.NET Core built-in ProblemDetails (RFC 9457) + `IExceptionHandler` (`TodoExceptionHandler`); `Microsoft.FeatureManagement` (when needed) | Open choice | [0008](0008-cross-cutting-concerns.md) |
