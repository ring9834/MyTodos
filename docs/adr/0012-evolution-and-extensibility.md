# ADR-0012: Evolution and extensibility — versioning of APIs, data and artifacts; extension points; evolution register

| | |
|---|---|
| **Status** | Proposed |
| **Date** | 2026-09-29 |
| **Decision area** | **Evolution & Extensibility** |
| **Method** | Attribute-Driven Design (modifiability tactics: anticipate expected changes, defer binding, restrict dependencies); analysed with ATAM concepts |
| **Related** | `docs/requirements.md` §10.3 (deferred items), A1–A10, Q4, Q5; every earlier ADR's "Revisit when" section; especially ADR-0001 §6 (federation), ADR-0004 (scale paths), ADR-0005 §3.6 (new status), ADR-0006 §3.4 (expand/contract), ADR-0009 §3.8 (API evolution rules), ADR-0010 (release and platform), ADR-0011 D5 (deferred MSW) |

---

## 1. Context

### 1.1 Purpose

Every earlier ADR recorded **what it deferred** and **what would bring it back**. This ADR does three things:
1. **Decides how the system evolves safely:** versioning and compatibility rules for the API, the data, tokens, configuration and release artifacts, plus a deprecation policy.
2. **Names the extension points** designed in on purpose, and the speculative extensibility deliberately **not** built.
3. **Consolidates every deferred item into one evolution register,** with measurable triggers, the first step, and the ADRs affected.

### 1.2 Drivers

| Driver | Implication |
|---|---|
| **Q4** Availability | During a rolling deployment, **two versions run at once**: old and new pods, old UI and new API, old app and new schema. Every change must survive that overlap |
| **Q5** Modifiability | Foreseeable changes (a field, a status) must stay cheap |
| **Requirements §10.3** | Many features are deferred, not rejected, so their paths must stay open |
| **Right-sizing** (ADR-0011 §1) | Only build extension points for changes that are genuinely foreseeable; everything else waits for a trigger |

### 1.3 The core compatibility rule

> **Everything must work across one version of overlap: N and N−1.**
> - The schema at release N works with the app at N and N−1.
> - The API at N works with the UI at N and N−1.
> - Tokens issued at N−1 remain valid at N.
> - Configuration from N−1 still starts N.

This single rule is what makes rolling updates (ADR-0010), `helm rollback`, and API-first deploys (ADR-0009) safe.

---

## 2. Decision summary

| Concern | Decision | Rejected / deferred |
|---|---|---|
| **API versioning (now)** | **No version in the URL;** additive-only changes (ADR-0009 §3.8), **enforced in CI by an OpenAPI breaking-change check** (`oasdiff`) | `/api/v1` now; header or media-type versioning |
| **API versioning (when needed)** | **URL-segment versions** (`/api/v2/…`) with **Asp.Versioning**; the unversioned routes remain as **v1 aliases** until sunset | Versioning the whole API when only one resource changes |
| **Deprecation** | Mark it `deprecated` in OpenAPI → `Deprecation` and `Sunset` response headers → confirm zero usage through route metrics (ADR-0008) → remove. **Minimum window:** one release (first-party UI); ≥ 6 months (external clients) | Silent removal |
| **Schema versioning** | **EF Core migrations are the schema version** (`__EFMigrationsHistory`); **forward-only**; **expand/contract** keeps N and N−1 compatible (ADR-0006) | Down-migrations in production; hand-run SQL |
| **Data backfills** | Small backfills inside the migration; **large ones as a separate, idempotent, batched Job**, never inside the migration transaction | Long-running backfills that lock tables during a release |
| **Record versioning** | `version` (`xmin`) is for **concurrency**, not history; **change history is deferred** (an audit table or temporal pattern) with a trigger | Event sourcing |
| **Enums / states** | Stored as strings; **adding a value is additive** (widen `CHECK`, ADR-0005 §3.6); removing one is a contract phase after migrating the data | PostgreSQL `ENUM` types (ADR-0006) |
| **Schema flexibility** | **An explicit schema only:** every attribute is a typed column | EAV tables; `jsonb` "custom fields" "for flexibility" (deferred until a custom-attributes requirement exists) |
| **Token evolution** | Claims are additive; **signing keys carry a key id (`kid`)**, and validation accepts **current + previous** keys, so rotation doesn't sign everyone out | A single, unnamed key (resolves ADR-0001 R1 for planned rotation) |
| **Configuration evolution** | Options have safe defaults; a renamed key is read under **both names for one release** (expand/contract for configuration) | Breaking renames |
| **Client version skew** | The web deploys after the API; hashed assets; **an error boundary that reloads the page when a code chunk from an old build can't be loaded** | Assuming every open tab is on the latest build |
| **Artifact versioning** | **Images: git SHA** (immutable, ADR-0010). **Releases: SemVer git tags** (`vX.Y.Z`) with generated release notes (Conventional Commits). **Helm charts:** SemVer `version`, `appVersion` = SHA. **API contract:** major version only (`info.version: "1"`) | Mutable tags; one version number for everything |
| **Platform lifecycle** | A **version-support calendar** for .NET (LTS), Node (LTS), Kubernetes (N−1), PostgreSQL (major) and chart dependencies, reviewed quarterly; Dependabot for minor and patch versions | Upgrading only when forced |
| **Designed-in extension points** | Nine, listed in §3.8, each already paid for by an earlier decision | — |
| **Deliberately not built** | Plugin architecture, a workflow engine, multi-tenancy plumbing, database-agnostic abstractions, "microservice-ready" ceremony (§3.9) | — |
| **Evolution register** | One table of every deferred item: trigger → first step → ADRs affected → size (§4) | Deferred items scattered across ADRs |

---

## 3. Options and trade-offs

### 3.1 API versioning

ADR-0009 §3.8 set the evolution rules (additive only; API before web; tolerant clients). This ADR adds **enforcement** and the **mechanism for when a break is unavoidable**.

**Enforcement: breaking-change detection in CI.**

| Option | Verdict | Why |
|---|---|---|
| **`oasdiff breaking`**: compare the PR's generated `openapi.json` with `main`'s | **Chosen** | Turns "additive only" from a review convention into a failing check. It flags removed endpoints or fields, type changes, new required request fields and removed enum values |
| Review only | Rejected | Easy to miss during live changes |
| Contract tests with consumers (Pact) | Deferred | Worth it with several independent consumers (ADR-0011 §7) |

A PR may *intentionally* break the contract only by adding a `/v2` route (below), which the check then treats as additive.

**Mechanism when a breaking change is needed:**

| Option | Verdict | Why |
|---|---|---|
| **URL segment** (`/api/v2/todos`) with **Asp.Versioning** (`[ApiVersion]`, route constraint) | **Chosen** | Visible in logs, traces and route metrics (useful for deprecation, §3.2); trivial to route at the edge; explicit in the OpenAPI document (one document per version) |
| Header (`api-version: 2`) | Rejected | Invisible in URLs; harder to test and to route |
| Media type (`application/vnd.todo.v2+json`) | Rejected | Unusual for browser clients; awkward with generated types |
| Versioning the whole API at once | Rejected | Version **per resource** when only one changes; unchanged resources stay on v1 |

**Transition:** existing unversioned routes (`/api/todos`) are treated as **v1** and remain as aliases until they are sunset.

**Triggers for introducing the mechanism:** the first unavoidable breaking change, or the first client outside our release cycle (mobile, partner).

### 3.2 Deprecation policy

```
1. Mark it       → [Obsolete] on the action / DTO member → "deprecated: true" in OpenAPI
2. Announce it   → the response carries `Deprecation` (RFC 9745) and `Sunset` (RFC 8594) headers
                   (+ a `Link` to the migration note)
3. Watch it      → route-level request metrics (ADR-0008) show who still calls it
4. Remove it     → only after the sunset date AND zero traffic for the window, in a
                   release whose notes say so
```

| Consumer | Minimum window |
|---|---|
| The first-party web UI (deployed with the API) | **One release:** the UI stops using the element in release N; the API removes it in N+1 |
| External or mobile clients (future) | **≥ 6 months**, and zero traffic observed |

### 3.3 Data and schema versioning

| Aspect | Decision | Why |
|---|---|---|
| **Schema version** | EF Core migrations, recorded in `__EFMigrationsHistory`; named with timestamps; committed and reviewed as SQL (ADR-0006) | The migration history *is* the version history, so there's nothing extra to maintain |
| **Compatibility window** | **Schema N works with app N and N−1** (§1.3), through expand/contract (ADR-0006 §3.4) | Rolling updates and application rollbacks |
| **Direction** | Forward-only in production | `helm rollback` is safe *because* the schema stays compatible |
| **Backfills** | Small (≤ thousands of rows): inside the migration. **Large:** a separate, idempotent, batched Job between the expand and contract releases, which is restartable | Avoids long locks and failed releases |
| **Restores** | A point-in-time restore returns the schema *as it was then*. The migration Job then runs forward to the current version before the app uses it | Restores are just "an older version" moving forward |
| **Record history** | Not kept. `created_at` / `updated_at` + `version` for concurrency only (ADR-0006) | No audit requirement yet |
| **Enums** | Additive: add a value → widen `CHECK` → the UI shows unknown values raw (ADR-0005 §3.6). Removal: stop writing it → migrate the rows → narrow `CHECK` in a later release | The same N / N−1 rule |
| **Personal-data evolution** | New personal fields go through the ADR-0006 §3.11 rules (minimise, never log) before they're added | Privacy doesn't regress as the model grows |

**Example: evolving A6 (day → time-slot scheduling):**

| Release | Change |
|---|---|
| N (expand) | Add a nullable `scheduled_at timestamptz`; the API accepts an optional `scheduledAt`; responses include both fields |
| N+1 (backfill) | A Job sets `scheduled_at` from `scheduled_for` + a default time + the user's time zone |
| N+2 (contract) | The UI uses `scheduledAt`; `scheduledFor` is deprecated (§3.2) and later removed |

### 3.4 Token and key evolution

| Aspect | Decision |
|---|---|
| Claims | Additive only; validators ignore unknown claims |
| **Signing keys** | Each key has a **`kid`**. Tokens are signed with the *current* key; validation accepts the **current and previous** keys. **Planned rotation:** add a new key → switch signing → remove the old key after the token lifetime (8 h, ADR-0001) → **nobody is signed out** |
| Emergency revocation | Unchanged: remove all old keys immediately, which signs everyone out (ADR-0001 R1) |
| Federation | Replaces the issuer; `kid`-based validation is exactly how IdP-issued tokens are validated, so the change is configuration (ADR-0001 §6) |

### 3.5 Configuration evolution

| Rule | Why |
|---|---|
| Every option has a safe default, or fails fast if it's required (ADR-0008) | New settings never break old deployments silently |
| **Renaming a key:** read the new name, fall back to the old name for one release, then remove the fallback | The same N / N−1 rule as schema and API |
| Removed settings are ignored, not errors | Old ConfigMaps don't block new releases |

### 3.6 Client version skew (front end)

| Situation | Handling |
|---|---|
| An old UI (open tab) talks to a new API | Additive API changes + tolerant readers (ADR-0009) |
| A new UI talks to an old API | Prevented: the **API deploys before the web** (ADR-0010) |
| An open tab requests a code chunk from a build that no longer exists | The Next.js error boundary detects a chunk-load failure and **reloads the page once**, fetching the current build |
| The static shell is cached | Hashed assets are immutable; the HTML shell is revalidated (ADR-0003) |

### 3.7 Artifact and platform versioning

| Artifact | Scheme | Why |
|---|---|---|
| Container images | **Git SHA** | Immutable and traceable (ADR-0010) |
| Releases | **SemVer git tags** `vX.Y.Z`; release notes generated from Conventional Commits | Human-readable history; "what changed in this deploy" |
| Helm charts | SemVer `version` (bumped when the chart changes); `appVersion` = SHA | Chart evolution is separate from app evolution |
| API contract | `info.version` = **major only** (`"1"`), changed only with a new URL version | The API's version changes far less often than the app's |
| Database schema | The migration name (timestamp) | §3.3 |

**Platform lifecycle calendar** (in `docs/runbook.md`, reviewed quarterly):

| Component | Policy |
|---|---|
| .NET | Stay on an **LTS** release; move to the next LTS within about 6 months of its release |
| Node.js | Active LTS; upgrade before the current line reaches end of life |
| Kubernetes (AKS) | Automatic patch upgrades (ADR-0010); a planned minor upgrade to stay within supported versions (**N−1 target**) |
| PostgreSQL | A planned major upgrade before end of support (the managed service supports in-place major upgrades); **rehearse with a restored copy** |
| Helm dependencies, controllers, cert-manager | Pinned; upgraded deliberately. The ingress controller follows ADR-0010 R8 (migration to Gateway API) |
| Libraries | Dependabot for minor and patch versions; major versions reviewed (including licence changes, ADR-0011 K2) |

### 3.8 Extension points designed in on purpose

Each one is **already paid for** by an earlier decision. None was added "for flexibility" alone.

| # | Extension point | Enables | Cost to use | Source |
|---|---|---|---|---|
| E1 | **The transition table + `allowedTransitions`** | New statuses and workflow rules (e.g. `in_progress`) | ~5 files, < 30 min | ADR-0005 §3.6 |
| E2 | **`ITokenIssuer` + JWT bearer validation** | Federated sign-in (Entra External ID, BFF) | Swap one implementation + configuration | ADR-0001 §6 |
| E3 | **Owner on every row, first in every index** | Multi-tenancy (owner → organisation), partitioning / sharding (Z-axis), row-level security | A migration + a filter change | ADR-0004 §3.6, ADR-0006 |
| E4 | **Module boundaries** (`Auth`, `Todos`; id-only references) | New modules (e.g. `Customers`); extracting a module into a service | New feature folders; extraction is mechanical | ADR-0004 §3.5, ADR-0007 |
| E5 | **The OpenAPI contract + generated types** | New clients (mobile, partners); APIM import | Generate a client | ADR-0009 |
| E6 | **Configuration-driven behaviour** (options, feature flags when needed) | Dark launches; environment differences | Configuration only | ADR-0008 |
| E7 | **Location stored as address + point** | PostGIS (`geography(Point)`), "near me", routing | An expand/contract migration | ADR-0002, ADR-0006 |
| E8 | **Outbound HTTP through typed clients + resilience** | Geocoding and other integrations | An adapter + WireMock tests | ADR-0008, ADR-0011 |
| E9 | **Standard routing resources** (Ingress → Gateway API) | Changing the edge controller; canary weights | `ingress2gateway` conversion | ADR-0010 |

### 3.9 Speculative extensibility deliberately not built

| Not built | Why | What would justify it |
|---|---|---|
| A plugin architecture | No third-party extension requirement | External developers extending the product |
| **A configurable workflow engine** (statuses in the database, an admin UI) | Status rules need code and requirements decisions anyway (ADR-0005 §3.6 D) | Non-developers changing workflows often, or different workflows per tenant |
| **Custom fields** (EAV or `jsonb` attributes) | Every attribute is known; an explicit schema is clearer, faster and validated | Users defining their own attributes |
| Multi-tenancy plumbing (tenant resolution, per-tenant config) | Single-owner model (A4); E3 keeps the path open | Organisations or teams of gardeners |
| A database-agnostic data layer | PostgreSQL chosen for its features (`xmin`, PostGIS); abstracting them away would remove the value | A requirement to support several databases |
| "Microservice-ready" ceremony (message contracts, service discovery) | A modular monolith with clean boundaries already keeps extraction possible (E4) | ADR-0004's extraction triggers |
| A GraphQL layer "for future clients" | REST + OpenAPI serves every known client | Several clients with divergent data needs |

---

## 4. Evolution register

Every deferred item in one place. **Triggers are observable.** Each item has a first step, and **size** is a rough effort: S ≈ hours, M ≈ days, L ≈ weeks.

| # | Evolution | Trigger | First step | ADRs affected | Size |
|---|---|---|---|---|---|
| V1 | **Federated sign-in + BFF** | Real users / real customer data | Register with Entra External ID; point JWT validation at it; add a BFF in Next.js | 0001, 0002, 0010 | M |
| V2 | **HTTPS-only hardening beyond the MVP** (private DB, Key Vault CSI, row-level security) | Real customer data | Private PostgreSQL access; the CSI driver with workload identity; RLS policies | 0001, 0006, 0010 | M |
| V3 | **Mobile or partner client** | A client outside our release cycle | Asp.Versioning (§3.1); `Idempotency-Key` on POST; refresh tokens | 0001, 0009 | M |
| V4 | **A new status** (e.g. `in_progress`) | A workflow requirement | Enum + table rows + guard tests + a UI label (E1) | 0005, 0006 | S |
| V5 | **Time-slot scheduling** (A6) | "Jobs at specific times" requirement | The expand/backfill/contract plan in §3.3 | 0006, 0009 | M |
| V6 | **Customers as an entity** (A8, FR-14) | Customer reporting or reuse | A new `Customers` module; `todo_items.customer_id` (nullable, then backfilled) | 0006, 0007, 0009 | M |
| V7 | **Geocoding / address search** (FR-12) | Users need to search addresses | An outbound adapter + resilience handler + WireMock tests; a queue if slow | 0004, 0008, 0011 | M |
| V8 | **Location features** (near me, route order, FR-16) | A prioritised location feature | Enable PostGIS; `geography(Point)` via expand/contract; a spatial index | 0002, 0003, 0006 | M–L |
| V9 | **Organisations / multi-tenancy** | Teams of gardeners sharing jobs | Owner → organisation membership; the filter change; RLS | 0001, 0004, 0006 | L |
| V10 | **Real-time multi-device sync** | Stale data complaints despite refetch on focus | Server-sent events + a backplane | 0004, 0009 | M |
| V11 | **Offline use** (A7) | Field use without connectivity | PWA + local store + a sync / conflict strategy | 0003, 0009 | L |
| V12 | **Read scaling** (caching, replicas, CQRS level 2–3) | A measured budget missed after indexing | Measure → distributed cache or read replica | 0003, 0004 | M |
| V13 | **Data volume** (archiving, partitioning) | Tables beyond ~10⁶ rows per owner, or slow queries | A retention policy → partitioning by owner | 0004, 0006 | M |
| V14 | **Change history / audit trail** | An audit or compliance requirement | An audit table written by the interceptor | 0006, 0008 | M |
| V15 | **Edge: Ingress → Gateway API** | The add-on's support window ends, or canary is needed | `ingress2gateway`; a new controller | 0010 | S–M |
| V16 | **Canary releases, staging, GitOps** | Real traffic; more than one team | Staging values file; NGINX / Gateway canary weights; Flux | 0010 | M |
| V17 | **Observability beyond export** (dashboards, alerts, front-end real-user monitoring) | Going beyond the demo | Enable the defined alert rules; add web RUM | 0008 | S |
| V18 | **Authentication audit log** | Real users (ADR-0008 R10) | A dedicated audit sink for sign-in events | 0001, 0008 | S |
| V19 | **MSW for front-end tests** | The ADR-0011 D5 trigger | Handlers generated from the OpenAPI types | 0011 | S |
| V20 | **Localisation** | Non-English users | Map error `code`s and UI strings to translations | 0008, 0009 | M |

**Keeping the register current:**
- Every new ADR adds its deferred items here.
- A deferred item that is picked up gets its own ADR, which supersedes the relevant rows.
- The register is reviewed with the platform calendar (quarterly).

---

## 5. ATAM analysis

### 5.1 Sensitivity points

| # | Sensitivity point | Attributes affected |
|---|---|---|
| S1 | **The N / N−1 compatibility rule** across the API, schema, tokens and configuration | Availability (Q4), Modifiability |
| S2 | **The OpenAPI breaking-change check** | Availability during rollouts; client stability |
| S3 | **The choice of extension points** (§3.8 vs §3.9) | Modifiability vs simplicity |
| S4 | **Key rotation with `kid`** | Security, Availability (no forced sign-out) |
| S5 | **Keeping the evolution register current** | Evolvability as a whole |

### 5.2 Trade-offs

| # | Trade-off | Chosen side | Accepted cost |
|---|---|---|---|
| T1 | No URL versioning now vs `/v1` now | Less ceremony; one client | The v1 alias step when v2 arrives |
| T2 | Forward-only migrations vs reversible | Safe rollbacks through compatibility | Mistakes are fixed forwards |
| T3 | An explicit schema vs `jsonb` / EAV flexibility | Validation, clarity, performance | New attributes need migrations |
| T4 | Named extension points vs general extensibility | Low cost now; cheap foreseen changes | Unforeseen changes cost more (accepted: YAGNI) |
| T5 | SHA images + SemVer releases vs one scheme | Traceability *and* readability | Two version identifiers to relate (the release notes list the SHA) |
| T6 | Validating current + previous keys vs a single key | Rotation without sign-out | A slightly longer window for a leaked old key (bounded by the token lifetime) |

### 5.3 Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | A breaking API change slips through during live work | `oasdiff breaking` in CI; the committed `openapi.json` diff in review |
| R2 | A large backfill locks tables during a release | The §3.3 rule: a separate, batched, idempotent Job |
| R3 | Client version skew breaks open tabs after a deploy | Chunk-load reload in the error boundary; additive API |
| R4 | Platform components drift out of support (.NET, Node, Kubernetes, PostgreSQL, ingress controller) | The quarterly calendar; Dependabot; ADR-0010 R8 |
| R5 | The evolution register goes stale | Updated with every ADR; reviewed quarterly |
| R6 | Deprecations are removed while still in use | Remove only at zero traffic, as shown by route metrics |

### 5.4 Non-risks

| # | Non-risk | Because |
|---|---|---|
| N1 | Adding optional fields or a new status | Additive by design (E1, tolerant readers) |
| N2 | Rolling back the application after a migration | The N / N−1 schema compatibility |
| N3 | Swapping the identity provider | E2: configuration + one implementation |

---

## 6. Consequences

**Positive**
- One rule (N / N−1) explains how every part of the system (API, schema, tokens, configuration) evolves safely.
- "Additive only" is enforced by a tool, not just by memory.
- Every deferred feature has an observable trigger and a known first step.
- Only nine extension points exist, each already justified by an earlier decision; speculative flexibility is explicitly rejected.

**Negative / follow-ups**
- Add `oasdiff` to CI (ADR-0011) and the `kid` rotation to the auth implementation (ADR-0001).
- Keep the platform calendar and the evolution register in `docs/runbook.md` and here.

### Impacts on other decision areas

| Decision area | Constraint or input from this decision |
|---|---|
| **Security (ADR-0001)** | `kid` on signing keys; validation accepts current + previous keys; planned rotation without sign-out |
| **Data (ADR-0006)** | The backfill rule; restore → migrate forward; the enum removal procedure; no `jsonb` custom fields |
| **Cross-cutting (ADR-0008)** | Route metrics used to confirm deprecated endpoints have zero traffic; configuration key renames read both names for one release |
| **Communication (ADR-0009)** | Asp.Versioning (URL segment) when a break is needed; `Deprecation` / `Sunset` headers; `deprecated` in OpenAPI; per-resource versioning |
| **Deployment (ADR-0010)** | SemVer release tags + release notes; chart versioning; the platform lifecycle calendar |
| **Testing (ADR-0011)** | `oasdiff breaking` in the static CI stage; the previous release's integration tests against the new schema (Should) |
| **Web (ADR-0007)** | A chunk-load-error reload in the Next.js error boundary |

---

## 7. Verification

| Check | How |
|---|---|
| No unintended breaking API change | `oasdiff breaking` against `main` in CI; fails the PR |
| Schema N works with app N−1 | Run the previous release's integration tests against the new schema (Should); expand/contract review |
| Rotating the signing key doesn't sign users out | Integration test: a token signed with the previous `kid` is accepted after rotation; one with a removed key is rejected |
| A deprecated endpoint carries its headers | Integration test for `Deprecation` / `Sunset` (when the first deprecation exists) |
| Adding a status stays cheap (E1) | The ADR-0005 drill: under 30 minutes, about 5 files |
| Open tabs survive a deploy | Manual: keep a tab open, deploy, navigate; the page reloads once on a stale chunk |
| The register is current | Quarterly review; every ADR's "Revisit when" appears in §4 |

## 8. Revisit when

- **The first client outside our release cycle appears.** Introduce URL versioning (§3.1) and the external deprecation window.
- **An audit or compliance requirement arrives.** Add change history (V14), and reconsider event-style records.
- **Any register item's trigger fires.** Write its ADR; update the register.
