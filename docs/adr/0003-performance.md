# ADR-0003: Performance — rendering, state, caching, concurrency and data access

| | |
|---|---|
| **Status** | Proposed |
| **Date** | 2026-09-29 |
| **Decision area** | Quality Attributes → **Performance** |
| **Method** | Attribute-Driven Design, using the SEI performance tactics (control resource demand, manage resources); analysed with ATAM concepts |
| **Related** | `docs/requirements.md`: Q3, Q7, Q5, Q6, FR-7, FR-9, FR-10, NFR-8; ADR-0001 (S4, same-origin); ADR-0002 (Next.js guardrails, PostgreSQL, Azure) |

---

## 1. Context

### 1.1 Drivers

| Driver | Summary | Priority |
|---|---|---|
| **Q3** Performance | A user with 1,000+ items lists them, filtered by state, one page at a time: **p95 < 200 ms** (local environment) | H/M |
| **Q7** Scalability | 10× request volume is handled by adding API instances, while still meeting Q3 | H/L |
| **FR-7** | Change an item's state quickly from the list (the gardener's most frequent action) | Must |
| **FR-10 / FR-11** | Maps: pick a location; view a day's items | Should / Could |
| **Q5** Modifiability | Performance measures must not make the code hard to change live | M/L |
| **NFR-8** Cost | Runs within free-trial credit, which means small, possibly burstable, compute and database tiers | Should |

### 1.2 Workload characteristics

These shape every choice below.

- **Per-user, authenticated data.** No two users see the same list, so shared caching of responses has little value.
- **Small data.** Hundreds to low thousands of items per user; each item is under 1 KB.
- **Read-mostly, but with frequent small writes** (state changes during the day).
- **Short, simple operations.** No long-running or CPU-heavy work in the MVP.
- **Freshness matters.** A job marked done must show as done immediately.

### 1.3 Guiding principle

> **Build in the cheap, proven defaults; measure; add complexity only where a measurement shows a budget is missed.**

The workload is small and simple, so the main risk is not slowness. It is *speculative optimisation* (caches, extra services, clever rendering) that adds failure modes, costs money and slows live changes (Q5) without a measured need.

### 1.4 Performance budgets

Q3 is the only performance requirement given. The other budgets below are **derived** by this ADR so that every layer has a target; they are design choices and can be adjusted.

| Layer | Budget | Source |
|---|---|---|
| API: list items (1,000+ items, filtered, paged) | p95 < **200 ms** | **Q3 (requirement)** |
| API: get, create, update, delete, state change | p95 < 100 ms | Derived |
| Database: list query | < 50 ms at 1,000 items per user | Derived (leaves headroom within Q3) |
| Frontend: Core Web Vitals "good" thresholds | LCP ≤ 2.5 s, INP ≤ 200 ms, CLS ≤ 0.1 | Derived (industry standard) |
| Frontend: initial JavaScript for the list page | ≤ 250 KB gzipped (the map is excluded and loaded on demand) | Derived |

---

## 2. Decision summary

| Layer | Decision | Rejected (for now) |
|---|---|---|
| **Rendering** | **Static app shell + client-side data fetching** (the shell is pre-rendered at build time, and data is fetched in the browser) | SSR per request, ISR, streaming SSR with server components for data |
| **Client state** | **TanStack Query** for server state; URL search parameters for filter state; React local state for UI state | Redux / Redux Toolkit, RTK Query, SWR, hand-written `fetch` + `useEffect`, a global client-state library |
| **Perceived performance** | **Optimistic updates** for state changes; skeleton loading states; previous page kept visible while the next one loads | Optimistic updates for create and edit |
| **Front-end payload** | Code-split and lazy-load the map; long-lived caching of hashed static assets | List virtualisation, infinite scroll |
| **Caching** | **Client cache (TanStack Query) + HTTP caching of static assets only.** API responses are `no-store` | Server-side caches (in-memory, Redis, output caching), CDN, ETag revalidation, Next.js data cache |
| **Concurrency (execution)** | **Asynchronous I/O throughout the API**; many requests are handled concurrently by the framework | Explicit parallelism within a request, background queues or workers |
| **Concurrency (data)** | **Optimistic concurrency control; no locks held; short transactions** (mechanism in the Data Integrity ADR) | Pessimistic locking |
| **Service structure** | **One API service**, scaled horizontally | Microservices |
| **Read/write separation (CQRS)** | **No CQRS.** One data model and one database for reads and writes. Only a *light* separation in code: reads project straight to response models, and writes go through the entity and its state rules (§3.7) | Separate read models, read stores or databases; event sourcing; asynchronous projections |
| **Data access** | Indexes that start with the owner; server-side paging (offset, bounded page size, stable sort); projection to response models; read-only queries without change tracking; bounded timeouts; connection pooling sized to the database's limits | Keyset (cursor) paging, read replicas, materialised views, an ORM-level second-level cache |
| **Cloud** | API and database in the **same region (and zone where possible)**; right-sized tiers; the edge routes `/api/*` **directly** to the API | Multi-region; a CDN in front of the API |
| **Measurement** | A **deterministic query-plan test** for Q3 (ADR-0011 D7); p95 observed from the ASP.NET Core instrumentation metric (`http.server.request.duration`, ADR-0008); the bundle-size budget in CI. **Load testing and Lighthouse CI deferred** (ADR-0011 D7) | Full tracing and APM (deferred with the observability stack) |

---

## 3. Options and trade-offs

### 3.1 Rendering pattern (front end)

| Option | How it works | Fit for this app |
|---|---|---|
| Client-side rendering only (CSR) | An empty page; everything is rendered in the browser | + Simple; − blank page until the JavaScript loads |
| **Static shell + CSR data (chosen)** | Layout and UI chrome are pre-rendered to HTML at build time and served instantly; data is fetched from the browser and shown with skeletons | ++ Fast first paint; the shell is fully cacheable; one data path through the API; no server-side cookie handling (ADR-0001 S4); matches the ADR-0002 guardrail |
| Server-side rendering per request (SSR) | Each request renders HTML with data on the server | − The data is per-user, so nothing can be shared or cached. It adds a web → API hop on the server, needs cookie forwarding (ADR-0001 S4), and puts load on the Node.js server. Its main benefit, SEO, is irrelevant behind sign-in |
| Static generation with incremental regeneration (ISR) | Pages are regenerated periodically | ✗ Built for public, shared content; unusable for per-user data |
| Streaming SSR with React Server Components fetching data | The server streams HTML as data arrives | − Same drawbacks as SSR, plus more complex caching semantics and a second data path to secure. A poor fit for live changes (Q5) |
| Partial prerendering (a static shell with dynamic holes rendered on the server) | A hybrid | 0 Similar user experience to the chosen option, but it depends on newer, evolving framework features and on server-side data access |

**Trade-off accepted:** the first view shows a skeleton for one API round trip before the data appears. With a p95 under 200 ms, this is barely noticeable, and it keeps a single, secured data path.

### 3.2 State management (front end)

The key distinction is between two kinds of state:
- **Server state:** data owned by the API (items, the current user). It can go stale, needs refetching, and is shared across components.
- **Client state:** UI-only data (which dialog is open, form inputs, filters).

Almost all of this app's state is **server state**.

| Option | Pros | Cons | Verdict |
|---|---|---|---|
| **TanStack Query** | Built for server state: caching, deduplicating identical requests, background refetch, stale-while-revalidate, mutations with optimistic updates and rollback, pagination helpers, invalidation. Very little code | Not a general client-state store (none is needed here) | **Chosen** |
| Redux Toolkit (+ RTK Query) | RTK Query offers similar server-state features; excellent dev tools | Adds a global store, slices and more concepts that this app doesn't need. More code to change live (Q5) | Rejected. Revisit if complex, shared client-side state appears |
| SWR | Lightweight stale-while-revalidate | Weaker mutation, optimistic-update and rollback support | Rejected |
| Hand-written `fetch` + `useEffect` | No dependency | Reinvents caching, deduplication, race handling and error states, with typical bugs | Rejected |
| Zustand / Jotai / React context for global state | Simple global state | No need yet; filters live in the URL, and everything else is local | Not needed |

**Supporting decisions:**
- **Filter state in URL search parameters** (`?state=scheduled&date=…&page=2`). The view can be shared and bookmarked, the back button works, and filters feed straight into the query key.
- **Query key design:** `['todos', { state, date, page }]`. Every distinct filter combination is cached separately, and after any mutation `invalidateQueries(['todos'])` refreshes all of them.
- **`staleTime` ≈ 30 s** (a sensitivity point, S5). Quick navigation between views reuses cached data without refetching, and a mutation always invalidates.
- **Keep the previous page visible** (placeholder data) while the next page loads, so the list doesn't flicker.
- **Mutations return the updated item**, so the cache can be updated without an extra GET.

### 3.3 Perceived performance

| Technique | Decision | Why |
|---|---|---|
| **Optimistic updates for state changes** (FR-7) | ✅ | The most frequent action feels instant. On error, including a concurrency conflict (Q6), the change rolls back and the user is told why |
| Optimistic updates for create and edit | ❌ | These involve validation (BR-1, BR-5) that the server may reject, and rolling back a form is confusing. A short spinner is honest and simple |
| Skeletons instead of spinners for lists | ✅ | They avoid layout shift (CLS) and feel faster |
| Lazy-load the map (FR-10, FR-11) | ✅ | Leaflet and its tiles are heavy. They are loaded only when a map is shown (dynamic import, client-only) |
| List virtualisation (e.g. react-window) | ❌ | A page of at most 100 rows renders cheaply; virtualisation adds complexity for no measured gain |
| Infinite scroll | ❌ | Numbered pages are simpler, work with URL state, and are easier to test |

### 3.4 Caching: where, and where not

| Layer | Decision | Reasoning |
|---|---|---|
| **Browser: static assets** (JS, CSS, fonts with hashed filenames) | ✅ `Cache-Control: public, max-age=31536000, immutable` | The content hash changes whenever the file changes, so these can be cached forever safely. This is the largest, cheapest win |
| **Browser: HTML shell** | ✅ Short cache or revalidation | It must pick up new deployments |
| **Browser: API responses** | ❌ `Cache-Control: no-store` | Per-user, authenticated data where freshness matters. It also keeps personal data (addresses) out of shared and disk caches (security, ADR-0001) |
| **Client in-memory cache (TanStack Query)** | ✅ The primary cache | Removes repeat requests within a session, with precise invalidation after each mutation |
| **ETag / conditional GET** | ❌ for reads | Little benefit for small payloads. (Version checks for writes belong to the Data Integrity ADR) |
| **CDN** | ❌ Deferred | One region and few users; static assets are already cached by browsers. Revisit for a geographically spread user base |
| **Server output caching / response caching** | ❌ | Responses are per-user, so the cache hit rate would be near zero, with a risk of serving one user's data to another |
| **Server in-memory cache (per instance)** | ❌ | With several replicas, each instance's cache goes stale independently after writes on another instance, causing inconsistent reads. Invalidation across replicas isn't worth it for queries already served by an index |
| **Distributed cache (e.g. Redis)** | ❌ Deferred | An extra component, cost and failure mode. PostgreSQL already caches hot data in memory. Revisit only if Q3 is missed **after** indexing and query tuning; the framework's hybrid cache would then be the entry point |
| **Next.js data / route caches for API data** | ❌ Not used | Data is fetched client-side (§3.1), so these don't apply. That also avoids complex framework caching semantics |
| **Database buffer cache** | ✅ (built in) | PostgreSQL keeps frequently used pages in memory automatically; the working set here is tiny |

### 3.5 Concurrency

"Concurrency" has two meanings here, and each needs its own decision.

**Execution concurrency (doing work at the same time):**

| Option | Decision | Why |
|---|---|---|
| **Asynchronous I/O throughout the API** (async/await down to the database; no blocking calls) | ✅ | While a request waits on the database, its thread serves other requests. This is the single most important scalability default for an I/O-bound API (Q7). Request cancellation is passed through, so abandoned requests stop their database work |
| Many concurrent requests per instance | ✅ (built into the framework) | — |
| Explicit parallelism within a request (e.g. running two queries in parallel) | ❌ | Each request runs one or two small queries; parallelising them saves little and complicates code and database connection use |
| Background queues and workers (message broker) | ❌ Deferred | There is no long-running work in the MVP. Revisit for geocoding (FR-12) or route optimisation (FR-16), which are slow, external or CPU-heavy |

**Data concurrency (two writers on the same item):**

| Option | Decision | Why |
|---|---|---|
| **Optimistic concurrency** (detect a conflict at write time using a version) | ✅ | No locks are held, so readers and writers never block each other. Conflicts are rare (one owner per item). Satisfies Q6 |
| Pessimistic locking (lock the row while editing) | ❌ | Holds locks across user think-time, which hurts throughput and risks abandoned locks |

The mechanism (version column, status code) is decided in the Data Integrity ADR; this ADR fixes the performance-relevant principle: **no locks held; short transactions.**

### 3.6 API and service structure

| Option | Decision | Why |
|---|---|---|
| **One API service, scaled horizontally** | ✅ | A single, simple CRUD domain. Every request is served in-process, with no service-to-service network hops |
| Microservices | ❌ | Each extra hop adds latency and a failure point, and distributed data needs coordination. No part of the domain has different scaling needs. (Structural reasons are recorded in the Component & Structural ADR) |
| CQRS / separate read models | ❌ | See §3.7 |
| Native ahead-of-time compilation | ❌ | Its main benefit is start-up time, which doesn't matter for long-running pods, and the ORM's support for it is limited |
| **Dedicated state-change operation** (realised as `PUT /api/todos/{id}/state`, ADR-0009 §3.3) | ✅ (input to Communication & Interaction) | A tiny payload for the most frequent action, and a clear place to enforce the transition rules (Q2) |
| **Compression of text responses** | ✅ at the edge | Smaller transfers for JSON and static assets. BREACH-style attacks are a non-risk because response bodies carry no secrets (tokens travel in cookies) |

### 3.7 Read/write separation (CQRS)

**Question:** should reads be separated from writes, as in Command Query Responsibility Segregation (CQRS), for read performance?

**Short answer:** no. CQRS solves problems this system doesn't have, and it would work against the freshness this app needs.

CQRS is a spectrum, not a single choice:

| Level | What it means | Decision | Why |
|---|---|---|---|
| **0. Single model** | One model and one code path for reads and writes | — | The starting point |
| **1. Command/query separation in code** | Same database and tables. Reads use lightweight query code that projects straight to response models; writes load the entity, apply the rules and save | ✅ **Adopted (light)** | It costs nothing and helps performance: reads skip entity loading and change tracking (§3.8). Writes keep the state rules (Q2) in one place. The code-structure details (e.g. whether to use separate handler classes) belong to the Component & Structural ADR |
| **2. Separate read model in the same database** | Denormalised read tables or materialised views, updated alongside writes | ❌ | Read and write shapes are identical: one table, no joins, no aggregation. There is nothing to denormalise |
| **3. Separate read store** (a read replica, or a different store such as a search index or cache) | Reads served from a copy of the data | ❌ | Data is small, and reads are already served by an index (target < 50 ms). A copy adds cost, infrastructure and **replication lag** |
| **4. Full CQRS with asynchronous projections** (often with event sourcing and a message broker) | Writes emit events; separate processes build read models | ❌ | Eventual consistency conflicts with the domain: a job marked done must show as done immediately. It also brings a broker, projection code, replay and versioning; a new field would touch far more places (Q5) |

**Why the higher levels don't pay off here:**

- **No read/write asymmetry.** CQRS helps when reads vastly outnumber writes *and* need different shapes (dashboards, search, reports). Here, reads and writes both use the same single-table, per-user shape, and writes are frequent (state changes all day).
- **The read path is already fast.** An owner-first index, paging, projection and a client cache meet Q3 without a second model. The database is not the bottleneck at this scale (R3 covers the limit).
- **Freshness and read-your-writes.** With separate read models, a user can mark a job done and then see it as not done until the projection catches up. That would break the optimistic update flow (§3.3) and the concurrency handling (Q6).
- **Modifiability and cost.** More moving parts to explain and change live (Q5), and more infrastructure on a trial budget (NFR-8).

**What would bring it back:**
- Cross-user reporting or analytics (e.g. an operations dashboard over all gardeners' jobs), which needs aggregated read shapes the write model doesn't have.
- Location queries at scale (e.g. "jobs near me" or route ordering, FR-16) that benefit from a spatially indexed or denormalised projection.
- Measured read load that a correctly indexed primary cannot serve, and that a read replica (level 3) also cannot serve.

Even then, the next step would be level 2 or 3, not level 4.

### 3.8 Database

| Tactic | Decision | Why |
|---|---|---|
| **Indexes that start with the owner** | ✅ Every list query is served by an index whose first column is the owner, followed by the filter and sort columns (e.g. owner → state → scheduled date). The exact index definitions belong in the Data Architecture ADR | The owner filter (ADR-0001) applies to *every* query, so an index without it is useless. This is the most important database decision for Q3 |
| **Server-side paging: offset + bounded page size** | ✅ Default 50, maximum 100; stable sort (scheduled date, created date, id) | Simple, works with page numbers and URL state, and is fast at this data size |
| Keyset (cursor) paging | ❌ Deferred | Better for very deep pages or very large tables; not needed at thousands of rows |
| **Total count for the pager** | ✅ | Cheap at this size through the owner index. A sensitivity point at large scale (S3) |
| **Projection to response models; read-only queries without change tracking** | ✅ | Reads only the columns needed and skips change-tracking overhead |
| Avoid N+1 queries | ✅ (by design) | List queries load no related entities |
| **Bounded timeouts** (database command timeout, e.g. 5 s) | ✅ | Slow queries fail fast instead of piling up and exhausting the connection pool |
| **Connection pooling sized to the database's limits** | ✅ Rule: *replicas × pool size per replica ≤ the database's connection limit, minus headroom* | Small managed tiers allow few connections. This is a key interaction with Q7 (S2). The built-in connection pooler of the managed service is the path if more replicas are needed |
| Read replicas | ❌ | No read-scaling need |
| Materialised views / denormalisation | ❌ | Queries are simple, single-table lookups |

### 3.9 Cloud

| Tactic | Decision | Why |
|---|---|---|
| **Co-locate the API and the database** in the same region, and the same availability zone where possible | ✅ | The database round trip happens on every request, and cross-zone or cross-region latency adds directly to Q3 |
| **Region close to the users** | ✅ | Every browser round trip crosses this distance |
| **The edge routes `/api/*` directly to the API**, not through Next.js rewrites | ✅ (input to Communication & Interaction) | Avoids an extra network hop and keeps load off the Node.js server; the same-origin requirement (ADR-0001 S4) is still met |
| **Burstable VM and database tiers** (trial budget) | ⚠️ Accepted, with a caveat | Cheap, but sustained load uses up CPU credits and then throttles, which can blow the p95 budget under sustained load. See R1 |
| Multi-region, geo-distributed database | ❌ | No requirement; a large cost and complexity increase |
| Autoscaling | ✅ (mechanism in the Scalability ADR) | Performance supplies the signal: scale on CPU, and on latency if it's available |

---

## 4. ATAM analysis

### 4.1 Sensitivity points

| # | Sensitivity point | Attributes affected |
|---|---|---|
| S1 | **Index design.** Q3 depends almost entirely on list queries being served by an owner-first index | Performance |
| S2 | **Connection pool size × replicas vs the database connection limit.** Scaling out without resizing pools exhausts connections and causes errors | Performance, Scalability (Q7), Availability |
| S3 | **Page size and total count.** They bound response size and query cost | Performance |
| S4 | **Network hop count** (edge → API directly, or via Next.js) | Performance, Scalability of the web tier |
| S5 | **Client cache `staleTime`.** The balance between fewer requests and fresher data | Performance vs data freshness |
| S6 | **Burstable CPU credits** on compute and database | Performance consistency, Cost |

### 4.2 Trade-offs

| # | Trade-off | Chosen side | Accepted cost |
|---|---|---|---|
| T1 | Static shell + client data vs SSR | Simplicity, a single secured data path, a cacheable shell | One round trip before data appears (skeleton shown) |
| T2 | No server-side cache vs a server cache | Simplicity, consistency across replicas, no extra component | Every list request reaches the database (cheap with the index) |
| T3 | Client cache `staleTime` 30 s vs always refetching | Fewer requests; instant navigation | Changes made on another device can take up to 30 s to appear, unless refetched on window focus |
| T4 | Offset paging vs keyset paging | Simplicity; page numbers in the URL | Slower on very deep pages (not reached at this scale) |
| T5 | Optimistic updates for state changes only | Instant feel for the most frequent action | Rollback logic, and a moment of "wrong" state on a conflict |
| T6 | Burstable tiers vs fixed-performance tiers | Fits the trial budget | Performance can degrade under sustained load (R1) |
| T7 | One service vs several | Latency, simplicity, modifiability | Everything scales together (acceptable: the whole domain has a uniform load profile) |
| T8 | One data model vs CQRS read models | Immediate consistency (read-your-writes), simplicity, modifiability | Reads and writes share one database; future reporting would need a new read path (§3.7 triggers) |

### 4.3 Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | Burstable tiers throttle under sustained load, and the p95 budgets are missed in the cloud | Monitor CPU credits and the list-route p95 in telemetry; document the result; a fixed-performance tier for production; load tests when ADR-0011 D7's trigger fires |
| R2 | Q3 is specified "locally", so meeting it locally says little about the cloud | Also measure in the cloud and record both figures; the difference is the network and tier cost |
| R3 | The database becomes the bottleneck for Q7, because it scales up, not out | Right-size the tier; pool sizing rule (S2); built-in connection pooler; read replicas as a later step |
| R4 | Routing `/api/*` through Next.js by accident (e.g. via rewrites) adds a hop and loads the Node.js server | Record edge routing explicitly in the Communication & Interaction ADR; check it in the walking skeleton |
| R5 | Missing index or an unbounded query slips in during live changes | Integration test asserting the Q3 budget with seeded data; query-plan check in review |

### 4.4 Non-risks

| # | Non-risk | Because |
|---|---|---|
| N1 | JSON serialisation cost | Small payloads (≤ 100 items of < 1 KB); the framework's serialiser is fast |
| N2 | N+1 queries | List queries load no related entities |
| N3 | Front-end rendering cost of the list | At most 100 simple rows per page |
| N4 | Leaking one user's data through caches | API responses are `no-store` and never cached server-side |
| N5 | Lock contention | Optimistic concurrency; no locks held across requests |
| N6 | Reads slowed by write-side overhead without CQRS | Reads already bypass entity loading and change tracking (CQRS level 1, §3.7) |

---

## 5. Consequences

**Positive**
- A small number of proven defaults covers every budget, with nothing extra to operate.
- The front end has one data path, which is consistent with the security decisions (ADR-0001) and with the Next.js guardrails (ADR-0002).
- Every rejected option has a named trigger that would bring it back, so the design can grow on evidence.

**Negative / follow-ups**
- The Q3 budget depends on the index and pool decisions being carried out correctly in the Data Architecture and Deployment ADRs.
- Burstable-tier behaviour (R1) must be observed and documented, not assumed.

### Impacts on other decision areas

| Decision area | Constraint or input from this decision |
|---|---|
| **Data architecture** | Owner-first indexes for every list query; stable sort columns; optimistic concurrency (mechanism to choose); command timeouts |
| **Communication & interaction** | Server-side paging parameters (page, page size ≤ 100) and total count; a dedicated state-change operation; mutations return the updated item; `Cache-Control: no-store` on API responses; the edge routes `/api/*` directly to the API |
| **Component & structural** | One API service; command/query separation in code (CQRS level 1, §3.7), realised as `Commands/` and `Queries/` folders in `Todo.Application` (ADR-0007); client components for data; filter state in the URL; the map as a separately loaded component |
| **Deployment & operations** | API and database co-located; connection pool sizing rule; compression and static-asset caching at the edge; resource requests sized from observed usage (load tests deferred, ADR-0011 D7) |
| **Scalability (quality attribute)** | Stateless, async API; the database as the scaling limit; pool sizing interacts with replica count |
| **Technology & tooling** | TanStack Query; size-limit for the bundle budget; load testing (k6) and Lighthouse CI deferred (ADR-0011 D7) |
| **Cross-cutting concerns** | Per-request duration from automatic instrumentation (spans and metrics), not log lines (ADR-0008 §3.1) |
| **Evolution & extensibility** | Triggers for a distributed cache, a CDN, keyset paging, background workers and read replicas |

---

## 6. Verification

| Check | How | Budget |
|---|---|---|
| Q3: list latency | **Deterministic integration test:** seed 1,000+ items for one user; assert the list query's plan uses the owner-first index and results are bounded by the page size. **Observed p95** of the list route in telemetry during manual use (ADR-0011 D7) | < 200 ms (observed) |
| Database query plan | Review the query plan for the list query; confirm it uses the owner-first index | < 50 ms |
| Q7: throughput scaling | HPA configuration review + a manual scale drill; throughput measurement deferred (ADR-0011 D7) | Near-linear scaling (to be measured when load tests are introduced) |
| Front-end budgets | size-limit in CI; a manual Lighthouse run against the list page (Lighthouse CI deferred, ADR-0011 D7) | LCP ≤ 2.5 s, INP ≤ 200 ms, CLS ≤ 0.1; initial JS ≤ 250 KB gzipped |
| Caching headers | Integration test: API responses carry `no-store`; hashed static assets carry `immutable` | — |
| Optimistic rollback | UI test: a failed state change (including a conflict) restores the previous state and shows a message | — |

## 7. Revisit when

- A measured budget is missed after indexing and query tuning. Only then consider a distributed cache.
- Users become geographically spread. Consider a CDN for static assets.
- Tables grow to hundreds of thousands of rows per user, or deep paging becomes common. Consider keyset paging.
- Slow or external work arrives (geocoding, route optimisation). Introduce background workers and a queue.
- Cross-user reporting, spatial queries at scale, or measured read load beyond what an indexed primary and a read replica can serve. Consider CQRS level 2 or 3 (§3.7).
