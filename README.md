# Architecture Decision Records: index and decision map

This folder holds the project's Architecture Decision Records (ADRs). Each ADR records one significant decision: its context, the options considered, the decision, and its consequences. The decisions are organised into nine **decision areas**.

## Conventions

- **Numbers are chronological and never reused.** A number records *when* a decision was made, and the decision area is metadata. If a decision changes, a new ADR supersedes the old one, and the old one is kept.
- **Status values:** Proposed → Accepted → (Superseded by ADR-nnnn | Deprecated).
- **Drivers vs decision areas.** Quality attributes, functional requirements and constraints (`docs/requirements.md`) are the *drivers*. The nine areas below are where decisions are *made* in response to them. The Quality Attributes area chooses the **tactics** for each attribute; the other areas choose the **mechanisms** that implement them. Each ADR lists the constraints it places on other areas.
- **Method.** Attribute-Driven Design (ADD) chooses what to decide and in which order (highest-priority drivers first). ATAM concepts are used to analyse each decision: sensitivity points, trade-offs, risks and non-risks.

## Decision map

| Area | Key questions | Main drivers | ADR | Status |
|---|---|---|---|---|
| **1. Quality attributes** | | | | |
| → Security | How do users sign in? How is a session carried? How is ownership enforced? Which defence layers apply? | Q1, Q8, NFR-5, NFR-6 | [0001](0001-security-authentication-and-authorization.md) | Proposed |
| → Scalability | Scale dimensions (AKF cube); horizontal vs vertical; autoscaling; API style; messaging; monolith vs microservices; database limits | Q7, Q3, Q4 | [0004](0004-scalability.md) | Proposed |
| → Performance | Rendering pattern; client state; caching layers; concurrency; paging, indexing, query shape; cloud placement | Q3, Q7, FR-7 | [0003](0003-performance.md) | Proposed |
| → Availability | Replicas, probes, rolling updates, disruption budgets, rollback | Q4, NFR-6 | — | Planned |
| → Data integrity | Concurrency control; where state rules are enforced | Q2, Q6, BR-1 to BR-6 | — | Planned |
| → Testability and maintainability | SOLID; code organisation; DI; test seams; mocking policy; test strategy; designing for change (adding a status) | NFR-1, NFR-2, Q5, Q2 | [0005](0005-testability-and-maintainability.md) | Proposed (§3.2 superseded by 0007) |
| **2. Inside vs. outside the system** | What is the system boundary? Who are the actors and external systems? Where are the trust boundaries? | FR-1 to FR-11, C-3, C-4 | — | Planned (trust boundaries started in 0001) |
| **3. Data architecture** | Data access (EF Core vs Dapper); code-first vs database-first; running and evolving migrations; physical model; integrity; concurrency; roles; seeding; lifecycle and personal data | BR-1 to BR-6, Q1, Q3, Q4, Q5, Q6 | [0006](0006-data-architecture.md) | Proposed |
| **4. Deployment & operations** | Hosting (AKS vs ACA/ACI/App Service); edge (NGINX Ingress vs Gateway API vs App Gateway vs APIM); TLS; CI/CD (GitHub Actions vs Azure Pipelines); pipelines; images; Helm; Terraform; secrets delivery; environments; release strategy; operations | C-4 to C-7, Q4, NFR-4, NFR-5, NFR-10 | [0010](0010-deployment-and-operations.md) | Proposed |
| **5. Component & structural** | Containers; Clean Architecture projects (Api / Application / Domain / Infrastructure); modules as feature folders per layer; `IAppDbContext`; plain handlers; controller-based RESTful API; rich domain; front-end feature folders | Q5, Q2, P-3, NFR-1 | [0007](0007-component-and-structural.md) | Proposed (supersedes 0005 §3.2) |
| **6. Communication & interaction** | Protocols per link; REST level 2; endpoint catalogue and status codes; representation rules; `ETag`/`If-Match`; idempotency and retries; content negotiation; versioning and evolution; OpenAPI contract and drift check | C-3, Q1, Q2, Q4, Q6, NFR-3 | [0009](0009-communication-and-interaction.md) | Proposed |
| **7. Cross-cutting concerns** | Observability (logging, correlation, metrics, tracing, alerting); error taxonomy and contract; exception strategy; validation; configuration and secrets; feature flags; health checks | NFR-2, NFR-3, NFR-5, NFR-6, Q3, Q4 | [0008](0008-cross-cutting-concerns.md) | Proposed |
| **8. Technology & tooling** | Core stack (frontend, backend, cloud, data store); testing and quality tooling; other tools chosen in their own area's ADR | C-1, C-2, C-4, A0, ADR-0001 §7, NFR-1 | [0002](0002-technology-stack.md), [0011](0011-testing-and-quality-tooling.md) | Proposed |
| **9. Evolution & extensibility** | Federated sign-in; mobile client; customers; API versioning; what is designed to be easy to change | Deferred items (requirements §10.3) | — | Planned |

## Suggested order for the remaining decisions

ADD takes the highest-priority drivers first, then the decisions that everything else depends on:

1. **Inside vs. outside:** the context diagram, completing the trust boundaries from 0001.
2. **Technology & tooling:** core stack, ADR-0002 (done).
3. **Component & structural:** containers and internal structure.
4. **Communication & interaction:** the API contract.
5. **Data architecture**, together with **data integrity** (Q2, Q6) and **performance** (Q3).
6. **Deployment & operations**, together with **availability** (Q4) and **scalability** (Q7).
7. **Cross-cutting concerns**, **testability**, **maintainability**.
8. **Evolution & extensibility:** mostly a summary of the evolution paths recorded in earlier ADRs.

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
| xUnit v3, Shouldly, NSubstitute (sparingly), `FakeTimeProvider`, `FakeLogger`, `WebApplicationFactory`, Testcontainers, Respawn, ArchUnitNET, WireMock.Net; Vitest (jsdom), Testing Library, MSW v2, vitest-axe, Playwright (+ axe); k6; Lighthouse CI; size-limit; gitleaks, Trivy, Dependabot, CodeQL, tflint, kubeconform | Open choice | [0011](0011-testing-and-quality-tooling.md) |
| Built-in .NET DI container | Open choice | [0005](0005-testability-and-maintainability.md) |
| ASP.NET Core MVC controllers (`[ApiController]`) | Open choice | [0007](0007-component-and-structural.md) |
| REST over HTTP/JSON (API style) | Open choice | [0004](0004-scalability.md) |
| Kubernetes Horizontal Pod Autoscaler | Open choice (within mandated Kubernetes) | [0004](0004-scalability.md) |
| HTTPS/HTTP/2 at the edge; `ETag`/`If-Match`; ASP.NET Core OpenAPI (build-time) + `openapi-typescript` | Open choice | [0009](0009-communication-and-interaction.md) |
| AKS (Cilium network policy, workload identity); NGINX Ingress (`nginx.ingress.kubernetes.io`, AKS application routing add-on); cert-manager + Let's Encrypt; GitHub Actions (OIDC); ACR; Key Vault (pipeline secrets); two Helm charts | Constrained / open choices | [0010](0010-deployment-and-operations.md) |
| EF Core + Npgsql; code-first migrations run as a migration bundle; snake_case naming convention | Open choice | [0006](0006-data-architecture.md) |
| Built-in .NET logging (JSON console); OpenTelemetry (OTLP / Azure Monitor, export off by default); Aspire dashboard (local); ASP.NET Core built-in ProblemDetails (RFC 9457) + `IExceptionHandler` (`TodoExceptionHandler`); `Microsoft.FeatureManagement` (when needed) | Open choice | [0008](0008-cross-cutting-concerns.md) |
