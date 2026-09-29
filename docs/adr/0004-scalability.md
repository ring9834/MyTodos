# ADR-0004: Scalability — scale dimensions, API style, service decomposition and autoscaling

| | |
|---|---|
| **Status** | Proposed |
| **Date** | 2026-09-29 |
| **Decision area** | Quality Attributes → **Scalability** |
| **Method** | Attribute-Driven Design; the AKF Scale Cube to structure the options; analysed with ATAM concepts |
| **Related** | `docs/requirements.md`: Q7, Q3, Q4, Q5, NFR-8, P-5, C-3, C-6; ADR-0001 (stateless JWT, S1, S6); ADR-0002 (.NET, Next.js, Azure, PostgreSQL); ADR-0003 (async I/O, one API service, no server caches, connection pooling rule) |

---

## 1. Context

### 1.1 Drivers

| Driver | Summary | Priority |
|---|---|---|
| **Q7** Scalability | Request volume grows to 10× normal (e.g. many users planning their day at the same morning peak). Capacity grows by adding API instances, with no code change and no user-visible errors. Throughput scales roughly linearly from 1 to 3 instances, while Q3 is still met | H/L |
| **Q3** Performance | p95 < 200 ms for the list operation, which must still hold at scale | H/M |
| **Q4** Availability | No failed requests during a rolling deployment. Scaling must not break this | M/M |
| **Q5** Modifiability | The scaling design must not make the code or deployment hard to change live | M/L |
| **NFR-8 / P-5** Cost | Runs within free-trial credit and quotas | Should |
| **C-6** | Hosted on Kubernetes (so serverless platforms are out of scope) | Constraint |

### 1.2 What "scalability" means here

Scalability is more than handling more requests. This ADR considers five dimensions:

| Dimension | Question | Relevance now |
|---|---|---|
| **Load** | Can the system serve more requests per second? | **High** (Q7) |
| **Data volume** | Does it stay fast as items and users accumulate? | Medium (Q3 with 1,000+ items per user) |
| **Users / tenants** | Does it stay correct and isolated as users are added? | Medium (A4, Q1) |
| **Development** | Can more people or features be added without the codebase becoming a bottleneck? | Low now, Medium later (Q5) |
| **Cost** | Does cost grow in proportion to load, not ahead of it? | High (P-5) |

Geographic scale (users on several continents) is out of scope; there is no requirement for it.

### 1.3 Workload assumptions (derived)

The requirements don't quantify "normal" load, so this ADR assumes the figures below to make the design concrete. **They are design assumptions, not requirements**, and must be replaced by measurements.

| Assumption | Value |
|---|---|
| Normal peak | ~20 concurrent active users, ~10 requests/s |
| 10× peak (Q7) | ~200 concurrent active users, ~100 requests/s |
| Read:write ratio | ~5:1 (lists and views vs state changes and edits) |
| Data | ≤ a few thousand items per user; < 1 KB per item |

At these figures, a single, correctly indexed PostgreSQL instance and a handful of small API instances are more than enough. The design therefore focuses on being **able** to scale out cleanly and on knowing where the limits are, not on handling extreme load.

### 1.4 The AKF Scale Cube

The options are framed along three axes:

| Axis | Technique | Example here |
|---|---|---|
| **X: duplication** | Run identical copies behind a load balancer | More API and web replicas |
| **Y: functional decomposition** | Split by function into separate services | An "auth service" and a "todo service" (microservices) |
| **Z: data partitioning** | Split data by a key, with each partition served separately | Shard by owner (tenant) |

---

## 2. Decision summary

| Concern | Decision | Rejected (for now) |
|---|---|---|
| **Scale-cube axis** | **X-axis (horizontal duplication) now.** Z-axis *prepared* (owner on every row and first in every index) but not implemented. Y-axis rejected | Microservices (Y); sharding (Z) |
| **Application tiers** | **Stateless web and API tiers, scaled horizontally** | Vertical scaling of application pods; sticky sessions |
| **Autoscaling** | **Horizontal Pod Autoscaler (HPA) on CPU** for the API (and the web tier), with a **bounded range**: minimum 2, maximum set by the database connection limit and the quota | Vertical Pod Autoscaler; event-driven autoscaling (no queues); node autoscaling in the trial (quota) |
| **Database** | **Scale up (vertical)**, with a single primary; connection pooling sized by a rule; the managed pooler when needed | Read replicas, sharding, a distributed SQL database (all deferred with triggers) |
| **API style** | **RESTful HTTP/JSON API** | GraphQL, gRPC, server-rendered MVC views, OData |
| **Messaging** | **Synchronous request/response only** | A message broker, event-driven processing, real-time push (WebSockets/SignalR) |
| **Service decomposition** | **Modular monolith:** one API deployable with clear internal modules (Auth, Todos) | Microservices; one unstructured codebase |
| **Cloud** | AKS, with capacity changes made through Terraform variables; a single region | Serverless platforms (excluded by C-6); multi-region |

---

## 3. Options and trade-offs

### 3.1 Statelessness: the precondition for horizontal scaling

The X-axis only works if **any request can go to any replica**. This checklist makes that explicit, and each item is a sensitivity point (S1).

| State | Where it lives | Stateless? |
|---|---|---|
| User session | JWT in a cookie; validated by any replica with the shared signing key (ADR-0001) | ✅ |
| Server-side caches | None (ADR-0003) | ✅ |
| Files | None written; read-only root filesystem (ADR-0001 layer 7) | ✅ |
| Logs and telemetry | Exported through OpenTelemetry, with stdout as the fallback; nothing stored locally (ADR-0008) | ✅ |
| Configuration and secrets | Environment variables and platform secrets; identical across replicas | ✅ |
| Rate-limiter counters | Held **per instance** | ⚠️ Effective limit = N × configured limit (S5) |
| Database migrations | Must run **once per release**, not in every replica at start-up | ✅ if run as a separate job (Deployment ADR) |
| Web-tier (Next.js) state | No sessions, no Server Actions (ADR-0002 guardrails); every replica runs the same image and build | ✅ |
| Framework key material (e.g. data-protection keys) | Not used by the chosen auth design. If a feature ever needs it, the keys must be shared across replicas | ✅ (watch point) |

**No sticky sessions.** Load balancing is plain round-robin, so a replica can be removed at any time without users noticing (Q4).

### 3.2 Horizontal vs vertical scaling, and autoscaling

| Tier | Scaling | Why |
|---|---|---|
| **API** | **Horizontal**, via HPA | Stateless and I/O-bound (async, ADR-0003), so capacity grows with replica count. Several replicas also give availability (Q4) |
| **Web (Next.js)** | **Horizontal**, via HPA | Stateless. Load is light: a static shell plus client-side data fetching (ADR-0003) |
| **Database** | **Vertical** (a larger tier) | One primary keeps the model simple and strongly consistent. Scaling up is a Terraform variable change. The limits are covered in §3.6 |
| **Cluster nodes** | Fixed in the trial; **cluster autoscaler** in production | Trial vCPU quotas cap the node count anyway. Production would add nodes when pods can't be scheduled |

**Autoscaling decisions:**

| Setting | Decision | Why |
|---|---|---|
| Mechanism | HPA | Built into Kubernetes; no extra components |
| Signal | **CPU utilisation, target ~70%** | Available without a metrics pipeline. Caveat: an I/O-bound API may saturate on database waits before CPU rises (S3). Request-rate or latency signals are the upgrade path once custom metrics exist |
| Minimum replicas | **2** (API and web) | Availability: survives one pod failure and supports rolling updates (Q4) |
| Maximum replicas (API) | **Bounded by the database connection limit** (§3.6) and the quota | Unbounded scaling would exhaust database connections and turn a load spike into errors |
| Scale-down | Stabilisation window (e.g. 5 minutes) | Avoids flapping between replica counts |
| Resource requests and limits | Set from load-test measurements | HPA percentages are relative to requests, so wrong requests mean wrong scaling |
| Vertical Pod Autoscaler | ❌ | It conflicts with a CPU-based HPA on the same pods |
| Event-driven autoscaling (e.g. KEDA) | ❌ | There are no queues or events to scale on (§3.4) |

**Scaling lag:** a new pod takes time to pull its image, start and pass its readiness probe. That lag is covered by keeping headroom: the minimum of 2 replicas and a 70% target leave room to absorb a burst while new pods start.

### 3.3 API style

"Which API style scales best?" is really a question of *how predictable and controllable the load is*. It also has to fit the brief: an API-driven app with a React UI (C-1, C-3).

| Option | Scalability characteristics | Other fit | Verdict |
|---|---|---|---|
| **REST over HTTP/JSON** | Stateless by design; each endpoint has a known, bounded cost (paged lists, single-item operations), so capacity planning and rate limiting per endpoint are straightforward; standard HTTP semantics (idempotent PUT and DELETE, cache headers) | A natural fit for CRUD (C-3); simple to test; any client can use it (web now, mobile later) | **Chosen** |
| GraphQL | One endpoint with client-defined queries, so the **cost per request is unpredictable** and needs query-complexity limits and depth limits; resolver N+1 problems need batching; per-operation rate limiting and HTTP caching are harder | Shines when many clients need different shapes of richly related data. Here there is one resource, one client and flat data | Rejected. Revisit if several clients with varied data needs appear |
| gRPC | Efficient binary protocol with streaming | Browsers need a gRPC-Web proxy; best for service-to-service calls, and there are none | Rejected |
| Server-rendered MVC views (traditional ASP.NET MVC / Razor pages) | Scales like any stateless web app | **Conflicts with the brief:** the UI must be React (C-1) and API-driven (C-3) | Rejected |
| OData | Flexible queries over REST | The same unpredictable-cost problem as GraphQL; more surface than needed | Rejected |

**Controllers vs minimal APIs** are two ways of *implementing* a REST API in ASP.NET Core. Both scale the same, because the runtime pipeline is the same. The choice is about code organisation and modifiability, so it belongs to the Component & Structural ADR.

**REST design rules that support scale** (input to Communication & Interaction):
- Every list operation is paged, with a maximum page size (ADR-0003).
- No unbounded operations, such as "return everything" or "bulk update everything".
- PUT and DELETE are idempotent, so they are safe to retry. Idempotency keys for POST are deferred.
- Responses are small and flat, with no deep nesting.

### 3.4 Messaging and asynchrony

| Option | What it gives | Verdict |
|---|---|---|
| **Synchronous request/response** | Simple, immediately consistent, easy to reason about and test | **Chosen.** Every MVP operation is a short CRUD call (ADR-0003) |
| Message broker (e.g. a managed queue service or RabbitMQ) with background workers | **Load levelling** (absorbs spikes by queueing work), decoupling, retries | ❌ Deferred. There is no slow or deferrable work to queue, and it would add eventual consistency, a broker to run, and failure modes (poison messages, duplicates) |
| Event-driven architecture / event streaming | Decoupled integration between many services | ❌ There is only one service, so there are no consumers |
| Real-time push (WebSockets / SignalR) to sync devices | Instant updates across a user's devices | ❌ Deferred. Refetching on window focus plus a short `staleTime` is enough (ADR-0003). If added, scale-out needs a **backplane**, which is itself a scalability sensitivity point |

**What would bring messaging in:** geocoding (FR-12), route optimisation (FR-16), notifications, or integration with other systems. These are slow, external or deferrable, which is exactly what queues are for. The **transactional outbox** pattern would then be used, so that a database change and its message never diverge.

### 3.5 Service decomposition: monolith, modular monolith or microservices

| Option | Scalability | Other factors | Verdict |
|---|---|---|---|
| Unstructured monolith | Scales on the X-axis like any stateless app | Boundaries erode; hard to extract anything later; weaker Q5 | Rejected |
| **Modular monolith** (one deployable; internal modules with explicit boundaries, e.g. `Auth` and `Todos`, each owning its own code and data access) | Scales on the X-axis. **Y-axis is kept possible:** a module with clean boundaries can be extracted into a service later if its load profile diverges | One build, one deployment, in-process calls (fast, and no partial failures); simple to test and change live | **Chosen** |
| Microservices | Independent scaling per service (Y-axis) | Every call becomes a network call: added latency (ADR-0003), partial failures, distributed data consistency, service discovery, versioning and N pipelines. A team of one with a single domain gets none of the benefits (independent teams, independent release cadence, divergent load profiles) | Rejected |
| Serverless functions / managed container apps | Scale to zero; per-request scaling | **Excluded by C-6** (Kubernetes is required) | Rejected (constraint) |

**Note:** the web tier (Next.js) and the API are two separate deployables, but that is a **tiered** architecture (presentation and service), not microservices. Each tier scales independently on the X-axis.

### 3.6 Database scalability

The database is the one tier that does not scale out, so its limits define the system's ceiling (ADR-0003 R3).

**Bounded replica rule.** The API's maximum replica count must satisfy:

```
max API replicas × pool size per replica  ≤  database connection limit − reserved connections
```

Worked example, with **illustrative numbers** (check the actual limit of the chosen tier): a limit of 50 connections, 10 reserved for admin and migrations, and a pool of 10 per replica gives **at most 4 API replicas**. The HPA maximum is set from this, not from guesswork.

**Scaling path, cheapest first.** Each step is taken only when a measurement shows it's needed:

| Step | Technique | Trigger |
|---|---|---|
| 1 | **Correct indexes, paging, projection** (ADR-0003) | Done by design |
| 2 | **Scale the tier up** (more vCPU and memory; this also raises the connection limit) | Database CPU or memory saturated; the connection ceiling reached |
| 3 | **Connection pooler** (the managed service's built-in pooler, or a sidecar) | More API replicas needed than the connection limit allows. The built-in pooler may not be available on the smallest burstable tiers |
| 4 | **Read replica(s)** | Read load exceeds what the primary can serve, and some staleness is acceptable for that read path (conflicts with read-your-writes; see ADR-0003 §3.7) |
| 5 | **Partition by owner** (Z-axis): table partitioning, then sharding | Data volume or write load beyond a single primary. `owner` is already the natural partition key, and every query already filters on it |
| 6 | Distributed SQL | Only at a scale far beyond this system's horizon |

**Data growth:** completed (`done`) items accumulate forever. An archive or retention policy is a Data Architecture concern. It is not needed at this scale, but it is noted as the first data-volume lever.

### 3.7 Front end

| Aspect | Decision |
|---|---|
| Static shell and hashed assets | Cached by browsers indefinitely (ADR-0003), so repeat visits barely touch the web tier |
| Client cache (TanStack Query) | Reduces API calls per user (deduplication, `staleTime`) |
| Web tier | Stateless Next.js replicas, scaled horizontally; guardrails keep server-side work minimal (ADR-0002) |
| Image optimisation | Not used for user content (there is none). If added, the framework's on-the-fly image optimisation is CPU-heavy and caches per instance; a pre-built or CDN approach would be needed |
| CDN | Deferred. It becomes worthwhile when static-asset traffic dominates or users are geographically spread |

### 3.8 Cloud and platform

| Aspect | Decision |
|---|---|
| Kubernetes | AKS: the API and web as Deployments with HPA; the database outside the cluster (ADR-0002) |
| Capacity as code | Node size and count, database tier and HPA bounds are **Terraform and Helm values**, so scaling is a reviewed change, never a portal click (NFR-4) |
| Quotas | Trial vCPU quotas are a **hard ceiling** on nodes and therefore on replicas (ADR-0002 R2). Recorded, not worked around |
| Node pools | One pool in the trial. Production: a separate system pool and user pool, across availability zones |
| Region | Single region, co-located with the database (ADR-0003) |
| Cost scaling | A minimum of 2 replicas per tier keeps the idle cost low; capacity is added only under load |

### 3.9 Other scalability considerations

| Concern | Decision |
|---|---|
| **Graceful shutdown** | Pods stop accepting new requests, finish in-flight ones, then exit. Scale-down and rolling updates therefore never cut requests (Q4) |
| **Readiness vs liveness** | Readiness includes a database check, so a pod only receives traffic when it can serve it. Liveness does not include the database, so a database outage doesn't trigger a restart storm |
| **Timeouts** | Bounded database and request timeouts (ADR-0003), so slow work can't pile up and exhaust threads or connections under load |
| **Rate limiting** | Per instance for the MVP (S5). A distributed limiter, or rate limiting at the edge, is the path when precise limits matter |
| **Migrations** | Run once per release as a separate job, never concurrently from every replica |
| **Development scalability** | Module boundaries (§3.5), a consistent feature-folder structure and a fast test suite let more features and people be added without a proportional slow-down |

---

## 4. ATAM analysis

### 4.1 Sensitivity points

| # | Sensitivity point | Attributes affected |
|---|---|---|
| S1 | **Statelessness** of the web and API tiers (§3.1). One piece of hidden per-instance state breaks X-axis scaling | Scalability, Availability, Correctness |
| S2 | **The database connection limit**, which bounds the maximum API replica count | Scalability, Availability |
| S3 | **The HPA signal and target.** CPU may not reflect I/O-bound saturation | Scalability, Performance, Cost |
| S4 | **Resource requests.** The HPA calculates percentages from them | Scalability, Cost |
| S5 | **Per-instance rate limiting.** The effective limit grows with the replica count | Security, Scalability |
| S6 | **Trial vCPU quota**, a hard ceiling | Scalability, Availability |

### 4.2 Trade-offs

| # | Trade-off | Chosen side | Accepted cost |
|---|---|---|---|
| T1 | Horizontal application tiers + a vertical database | Simplicity; strong consistency; one source of truth | The database is the ceiling (R1) |
| T2 | REST vs GraphQL | Predictable, bounded cost per request; simple to rate-limit and test | Clients can't shape their own queries; a future multi-client scenario may want more flexibility |
| T3 | Synchronous only vs messaging | Immediate consistency; fewer components | No load levelling; a spike must be absorbed by replicas |
| T4 | Modular monolith vs microservices | Latency, simplicity, one deployable, live changeability | No independent scaling per module (acceptable: the load profile is uniform) |
| T5 | CPU-based HPA vs custom metrics | No metrics pipeline needed | A less accurate scaling signal for I/O-bound load (S3) |
| T6 | A minimum of 2 replicas vs 1 | Availability during failures and deployments | Double the idle cost of each tier |
| T7 | A bounded HPA maximum vs unbounded | Protects the database from connection exhaustion | Beyond the ceiling, extra load raises latency instead of adding capacity |

### 4.3 Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | The single database primary is the scalability ceiling | The scaling path in §3.6; the bounded replica rule; measure the database under load test |
| R2 | The trial quota prevents demonstrating 3 API replicas plus a 2-replica web tier on one small node | Small resource requests; demonstrate Q7 at the largest replica count the quota allows, and document it |
| R3 | CPU-based HPA reacts late or not at all when the API is waiting on the database | Load-test to see whether CPU tracks latency; plan custom metrics if not |
| R4 | Burstable CPU credits (ADR-0003 R1) run out under sustained load: throttled pods show high CPU, the HPA adds replicas, but the real limit is credits or the database | Short load tests; watch CPU credits; fixed-performance tiers in production |
| R5 | Scaling lag during a sudden spike | Headroom from minimum replicas and the 70% target; fast start-up; readiness probes |

### 4.4 Non-risks

| # | Non-risk | Because |
|---|---|---|
| N1 | Session affinity problems | There are no server-side sessions; JWTs are validated by any replica |
| N2 | Cache coherence across replicas | There are no server-side caches (ADR-0003) |
| N3 | Coordination between services | There is one API service; calls between modules are in-process |
| N4 | Unbounded requests overwhelming the API | Every list is paged, with a maximum page size |

---

## 5. Consequences

**Positive**
- Q7 is met by adding replicas, with no code changes; the steps are known and bounded.
- Every deferred option (read replicas, sharding, messaging, microservices, GraphQL) has an explicit trigger and a known path.
- The design keeps the system simple enough to change live (Q5).

**Negative / follow-ups**
- The database tier and connection limit must be chosen together with the HPA maximum. The bounded replica rule links the Data and Deployment decisions.
- Measured numbers must replace the assumed workload figures (§1.3).

### Impacts on other decision areas

| Decision area | Constraint or input from this decision |
|---|---|
| **Deployment & operations** | HPA on the API and web tiers (min 2, bounded max, CPU ~70%, scale-down stabilisation); resource requests set from load tests; migrations as a one-off job; graceful shutdown; readiness includes the database, liveness does not; capacity as Terraform and Helm values; cluster autoscaler in production |
| **Data architecture** | A single primary; owner as the partition key on every table and first in every index (Z-axis readiness); pool size per replica; a retention policy noted for later |
| **Communication & interaction** | REST over HTTP/JSON; every list paged with a maximum page size; no unbounded operations; idempotent PUT and DELETE |
| **Component & structural** | A modular monolith with explicit modules (`Auth`, `Todos`); controllers vs minimal APIs decided there |
| **Cross-cutting concerns** | Nothing stored per instance; shared key material only through configuration; per-instance rate limiting recorded |
| **Technology & tooling** | Kubernetes HPA; a load-test tool (e.g. k6, as in ADR-0003) |
| **Evolution & extensibility** | Triggers and paths for read replicas, partitioning, messaging with an outbox, GraphQL, real-time push with a backplane, and module extraction |

---

## 6. Verification

| Check | How | Driver |
|---|---|---|
| Throughput scales with replicas | Load test at 1 and 3 API replicas; compare throughput at the Q3 latency target | Q7 |
| The HPA scales out and back in | Apply load; watch replicas rise to the bound and fall back after the stabilisation window | Q7 |
| Statelessness | During the load test, requests carrying the same token succeed on every replica; delete a pod mid-test and confirm there are no failed requests | S1, Q4 |
| Connection ceiling respected | At maximum replicas under load, the number of active database connections stays below the limit | S2 |
| Graceful shutdown | Scale down during load; confirm there are no failed requests | Q4 |

## 7. Revisit when

- Measured load or data volume approaches the database ceiling (§3.6 path).
- Slow, external or deferrable work arrives. Introduce a broker, workers and an outbox (§3.4).
- Several clients with different data needs appear. Reconsider GraphQL (§3.3).
- One module's load profile diverges from the rest. Extract it (Y-axis, §3.5).
- Real-time multi-device updates become a requirement. Add push with a backplane (§3.4).
- Users become geographically spread. Consider a CDN, then multiple regions.
