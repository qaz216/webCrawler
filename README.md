# webCrawler

A job-based web crawler made of event-driven microservices. A user submits a URL. A worker crawls the site asynchronously through a message broker and stores the results in SQL. A React UI shows progress, the page tree with each page's **Domain Link Ratio**, and the history of past jobs.

> **Status:** design and plan. This README describes the approach being implemented. Sections marked _(planned)_ are not built yet. As the work lands, the README will be updated to match the code.

---

## Contents

1. [Tech stack](#tech-stack)
2. [Architecture](#architecture)
3. [Running locally](#running-locally)
4. [API](#api)
5. [Crawling rules and assumptions](#crawling-rules-and-assumptions)
6. [Messaging](#messaging) (schema, idempotency, retries, DLQ)
7. [Data model and performance](#data-model-and-performance)
8. [Frontend](#frontend)
9. [Observability](#observability)
10. [Testing](#testing)
11. [Priorities, cuts and next steps](#priorities-cuts-and-next-steps)
12. [Known limitations](#known-limitations)

---

## Tech stack

| Concern        | Choice                                                     |
|----------------|------------------------------------------------------------|
| Backend        | .NET 8, ASP.NET Core (minimal APIs), `BackgroundService` worker |
| Broker         | RabbitMQ (with the management plugin)                       |
| Database       | PostgreSQL 16, EF Core 8 (Npgsql)                           |
| HTML parsing   | AngleSharp                                                  |
| HTTP resilience| `Microsoft.Extensions.Http.Resilience` (Polly v8)           |
| Logging        | Serilog, structured JSON to the console                     |
| Frontend       | React + TypeScript + Vite, React Router, TanStack Query     |
| Tests          | xUnit, Testcontainers (PostgreSQL)                          |
| Local runtime  | Docker Compose                                              |
| CI             | GitHub Actions: build and test                              |

## Architecture

```
 ┌──────────┐  HTTP   ┌──────────────────┐  publish CrawlPageTask   ┌──────────────┐
 │ React UI │ ──────▶ │ Service A        │ ───────────────────────▶ │  RabbitMQ    │
 │ (Vite)   │ ◀────── │ Crawl API        │                          │  crawl.pages │
 └──────────┘ polling │ (orchestrator)   │                          └──────┬───────┘
                      └────────┬─────────┘                                 │ consume
                               │ read jobs/pages/edges                     ▼
                               │                                 ┌──────────────────┐
                               ▼                                 │ Service B        │
                      ┌──────────────────┐   write pages/edges   │ Crawl Worker     │
                      │   PostgreSQL     │ ◀──────────────────── │ fetch, parse,    │
                      │                  │   + outbox            │ compute metrics  │
                      └──────────────────┘                       └────────┬─────────┘
                                                                          │ publish child
                                                                          ▼ tasks (via outbox)
                                                                     RabbitMQ
```

### Solution layout

```
src/
  Crawler.Domain/          # Pure logic with no I/O: UrlNormalizer, DomainLinkRatio, LinkExtractor,
                           # entities (CrawlJob, Page, PageLink), JobStatus state machine
  Crawler.Application/     # Use cases: CreateJob, GetJob, GetTree, ListHistory, CancelJob,
                           # ProcessPageTask; ports (interfaces) for repositories, publisher, fetcher
  Crawler.Infrastructure/  # EF Core + migrations, RabbitMQ topology/publisher/consumer,
                           # HttpPageFetcher (+ resilience), outbox dispatcher
  Crawler.Api/             # Service A: REST endpoints, health checks, CORS, ProblemDetails
  Crawler.Worker/          # Service B: consumer host, outbox dispatcher, health endpoint
  Crawler.Contracts/       # Message DTOs shared by API and worker (versioned)
web/                       # React app
tests/
  Crawler.Domain.Tests/        # Unit tests: normalization, ratio, link extraction
  Crawler.IntegrationTests/    # Fixture-site crawl against Testcontainers Postgres
docker-compose.yml
```

The dependency direction is `Api/Worker → Infrastructure → Application → Domain`. The domain project has no framework dependencies, so the logic that matters most (normalization and the ratio) is trivial to unit test.

### Key architectural choices

- **One message per page, not one per job.** The worker consumes `CrawlPageTask` messages. Each processed page publishes tasks for its newly discovered same-domain children at `depth + 1`. Benefits:
  - Pages are crawled in parallel across worker instances (scale with `docker compose up --scale worker=3`).
  - A slow or failing page retries alone instead of restarting the whole job.
  - Idempotency, retries and the DLQ apply at a meaningful granularity.
- **The worker owns writes for crawl results. The API owns job creation and all reads.** Both share one database through the Infrastructure layer. For a two-service system, a shared schema is pragmatic. A stricter split (the worker emits `PageCrawled` events and the API projects them) is listed under next steps.
- **Transactional outbox.** Page results, edges, newly claimed child pages and the outgoing child tasks are written in **one DB transaction**. A dispatcher publishes the outbox rows to RabbitMQ afterwards. This closes the "committed to the DB but crashed before publishing" gap that would silently lose parts of the tree.
- **The DB is the source of truth for de-duplication**, enforced with unique constraints rather than in-memory sets. Multiple worker instances and redeliveries are therefore safe.

## Running locally

_(planned. Target experience below.)_

Prerequisites: Docker Desktop (or Docker Engine + Compose v2).

```bash
docker compose up --build
```

| Service              | URL                                   |
|----------------------|---------------------------------------|
| React UI             | http://localhost:3000                 |
| Crawl API (+Swagger) | http://localhost:8080/swagger         |
| API health           | http://localhost:8080/health/ready    |
| Worker health        | http://localhost:8081/health/ready    |
| RabbitMQ management  | http://localhost:15672 (guest/guest)  |
| PostgreSQL           | localhost:5432 (crawler/crawler)      |

EF Core migrations are applied automatically on API startup (fine for local and demo use, but not for production). Docker Compose healthchecks gate start order: the API and worker wait for Postgres and RabbitMQ to be healthy.

To develop without containers for the apps:

```bash
docker compose up -d postgres rabbitmq
```

```bash
dotnet run --project src/Crawler.Api
```

```bash
dotnet run --project src/Crawler.Worker
```

```bash
cd web && npm install && npm run dev
```

Run the tests (Docker must be running for Testcontainers):

```bash
dotnet test
```

## API

All routes live under `/api`. Errors use RFC 7807 `ProblemDetails`.

| Method | Route                          | Description                                                     |
|--------|--------------------------------|-----------------------------------------------------------------|
| POST   | `/api/jobs`                    | Create a job. Body `{ "url": string, "maxDepth"?: number }` → `202 { "jobId" }` |
| GET    | `/api/jobs/{jobId}`            | Status, timestamps, failure reason, progress counters            |
| GET    | `/api/jobs/{jobId}/tree`       | Hierarchical result (nested nodes)                               |
| GET    | `/api/jobs?page=1&pageSize=20` | History, most recent first, paginated                            |
| POST   | `/api/jobs/{jobId}/cancel`     | Cancel a Pending or Running job (optional feature)              |
| GET    | `/health/live`, `/health/ready`| Liveness and readiness (DB + broker)                             |

Validation for `POST /api/jobs`:
- `url` must be an absolute `http`/`https` URL.
- `maxDepth` defaults to `2` and must be between `0` and `5`.
- `maxPages` is a server-side setting (default `200`, configurable through `Crawler__MaxPagesPerJob`).

The job summary includes `pagesDiscovered`, `pagesCompleted` and `pagesFailed`, which drive the progress bar. Progress is `completed / discovered`. This is honest about the fact that the total is unknown up front.

Tree node shape:

```json
{
  "url": "https://example.com/about",
  "depth": 1,
  "status": "Completed",
  "httpStatus": 200,
  "domainLinkRatio": 0.75,
  "outgoingLinkCount": 12,
  "children": [ /* nodes */ ]
}
```

## Crawling rules and assumptions

**Scope**
- Only links whose host equals the **starting domain** are followed. The starting domain is the host of the job URL, compared case-insensitively and exactly, so `www.example.com` ≠ `example.com` and subdomains count as external. External links are recorded and counted for the ratio but never fetched.
- Depth: the root is depth `0`. Children are enqueued only while `depth < maxDepth`.
- **HTML only.** A response is parsed only if its `Content-Type` is `text/html` or `application/xhtml+xml`. Anything else is stored as `Skipped (non-HTML)` with no links.
- Redirects are followed (max 5). The **final** URL is the base for resolving relative links. Redirects that leave the starting domain are not followed further.
- **Max pages per job: 200** by default. The cap is enforced atomically in the DB (see below), so concurrent workers cannot overshoot it.

**Normalization** (`UrlNormalizer`, the key to de-duplication)
1. Resolve relative links against the page base URL, honouring a `<base href>` element if present.
2. Ignore `mailto:`, `tel:`, `javascript:`, `data:` and any other non-`http(s)` scheme.
3. Drop the fragment (`#section`). A link that is only `#anchor` therefore normalizes to the page itself.
4. Lowercase the scheme and host. Remove default ports (`:80`, `:443`).
5. An empty path becomes `/`. The path's case and trailing slash are otherwise preserved, because servers may treat them differently.
6. The query string is kept as-is. It is not re-ordered and tracking parameters are not stripped. This is a documented simplification.

**Domain Link Ratio**

```
ratio = (# outgoing links whose host == starting domain) / (total # outgoing links)
```

- "Outgoing links" means the page's **distinct normalized http(s)** links, after the filtering above. A link repeated in the nav and the footer counts once.
- A self-link (including a bare `#anchor`) counts as an internal link.
- Zero outgoing links → ratio `0`.
- The ratio is computed per crawled page and stored, not recomputed on read.

**HTTP**
- `HttpClient` timeout of 10s per attempt, with an overall budget of 30s per page.
- Retries: 3 attempts with exponential backoff and jitter, on transient failures only (see below).
- A `User-Agent` that identifies the crawler. Response bodies are capped at 2 MB.

## Messaging

### Topology (RabbitMQ)

| Name                     | Type           | Purpose                                                        |
|--------------------------|----------------|----------------------------------------------------------------|
| `crawl`                  | direct exchange| Main exchange                                                  |
| `crawl.pages`            | quorum queue   | `CrawlPageTask` work queue (routing key `page`)                |
| `crawl.pages.retry.5s`   | queue, TTL 5s  | Delay queue that dead-letters back to `crawl` / `page`         |
| `crawl.pages.retry.30s`  | queue, TTL 30s | Second delay tier                                              |
| `crawl.pages.retry.2m`   | queue, TTL 2m  | Third delay tier                                               |
| `crawl.dlx` → `crawl.pages.dlq` | direct exchange + queue | Poison and exhausted messages                     |

Consumers use manual acks, `prefetch = 10`, and publisher confirms on publish.

### Message schema

`CrawlPageTask` (v1), JSON body. The headers are `message-id`, `correlation-id` (= jobId), `x-attempt` and `x-schema-version`.

```json
{
  "schemaVersion": 1,
  "messageId": "6f1c…",              // deterministic: hash(jobId + normalizedUrl)
  "jobId": "3b0e…",
  "pageId": "a91d…",                 // row already claimed in `pages`
  "url": "https://example.com/about",
  "normalizedUrl": "https://example.com/about",
  "depth": 1,
  "maxDepth": 2,
  "rootHost": "example.com",
  "parentPageId": "11aa…",           // null for the root
  "enqueuedAt": "2026-10-07T12:00:00Z"
}
```

The API publishes the root task when a job is created, writing the job row, the root page row and the outbox row in one transaction. The worker publishes child tasks through the same outbox mechanism.

### Idempotency strategy

The system assumes at-least-once delivery. Every write is safe to repeat:

| Mechanism | Protects against |
|-----------|------------------|
| `UNIQUE (job_id, normalized_url)` on `pages`. A child page is **claimed** with `INSERT … ON CONFLICT DO NOTHING RETURNING id`, and a task is enqueued **only if the insert returned a row**. | Re-processing the same URL within a job, both from different parents and from redelivery |
| `UNIQUE (from_page_id, to_normalized_url)` on `page_links`. Edges are inserted with `ON CONFLICT DO NOTHING`. | Duplicate edges when a task is processed twice |
| A page state guard. Processing starts with `UPDATE pages SET status='Processing', lease_until=now()+interval '2 min' WHERE id=@id AND (status='Pending' OR (status='Processing' AND lease_until < now()))`. Zero rows updated → **ack and skip**. | Two consumers working the same task at once, and duplicates of a page that's already finished |
| Results, edges, child claims, the job counter updates and outbox rows commit in **one transaction** that also sets the page to `Completed`. | Partial writes. A crash before commit means redelivery redoes everything safely. A crash after commit means redelivery hits the guard and is skipped. |
| The outbox uses a deterministic `messageId` (job + URL), and the dispatcher marks rows sent only after the publisher confirm. A duplicate publish is absorbed by the guard above. | Duplicate publishes from the outbox dispatcher |
| Job completion is a conditional update: `UPDATE crawl_jobs SET status='Completed' … WHERE id=@id AND status='Running' AND pages_pending = 0`. | Double completion, and racing workers |
| The max-pages cap is an atomic counter: `UPDATE crawl_jobs SET pages_discovered = pages_discovered + 1 WHERE id=@id AND pages_discovered < max_pages RETURNING …` | Overshooting the cap under concurrency |

### Retry policy

Retries happen at two levels.

1. **In-process HTTP retries** (resilience pipeline): 3 attempts, exponential backoff from 500 ms with jitter. These cover quick blips without touching the broker.
2. **Message-level retries** (delay queues): if handling still fails with a **transient** error, the message is re-published to the next delay tier (5s → 30s → 2m) with `x-attempt + 1`, then acked. After the 3rd tier it goes to the DLQ.

**Transient (retry):**
- HTTP `408`, `429` (honouring `Retry-After`), `5xx`
- timeouts, `HttpRequestException` for DNS, socket or connection reset
- PostgreSQL transient errors: connection failure, serialization failure, deadlock (`40001`, `40P01`)
- broker publish failures

**Permanent (no retry, not a message failure):** HTTP `4xx` other than `408`/`429`, non-HTML content, and invalid URLs. These are a normal **result**: the page is stored as `Failed` (with the status and reason) or `Skipped`, the message is acked, and the job continues. Only a failure of the **root** page fails the whole job.

### DLQ handling

These go to `crawl.pages.dlq`:
- **Poison messages:** JSON that can't be deserialized, an unknown `schemaVersion`, missing required fields, or a `jobId` that doesn't exist. They go to the DLQ immediately with no retries.
- **Exhausted messages:** transient failures still failing after the last delay tier.
- **Unexpected exceptions:** after one retry tier, to avoid hot-looping on a bug.

DLQ'd messages carry `x-death` plus an `x-error` header (exception type and message). The worker logs them at `Error` with the jobId. When a page task is dead-lettered, its page is marked `Failed` with reason `dead-lettered` so the job can still finish. There is no automatic replay. A human inspects messages in the RabbitMQ UI and can move them back with a shovel. A small replay endpoint is listed under next steps.

### Cancellation

`POST /api/jobs/{id}/cancel` sets the status to `Canceled` (conditionally, from `Pending`/`Running`). Workers check the job status at the start of each task and ack-and-drop tasks for canceled jobs. In-flight pages finish but publish no children.

## Data model and performance

```sql
crawl_jobs(
  id uuid PK, url text, root_host text, max_depth int, max_pages int,
  status text, failure_reason text,
  created_at timestamptz, started_at timestamptz, completed_at timestamptz,
  pages_discovered int, pages_completed int, pages_failed int, pages_pending int,
  row_version xmin                                -- optimistic concurrency
)
  INDEX ix_jobs_created_at (created_at DESC, id)  -- history, keyset-friendly

pages(
  id uuid PK, job_id uuid FK, url text, normalized_url text, depth int,
  parent_page_id uuid NULL,                       -- first discoverer → tree shape
  status text, http_status int, content_type text, error text,
  domain_link_ratio numeric(5,4), outgoing_link_count int,
  lease_until timestamptz, fetched_at timestamptz
)
  UNIQUE ux_pages_job_url (job_id, normalized_url)
  INDEX ix_pages_job_parent (job_id, parent_page_id)

page_links(                                       -- every outgoing edge, incl. external
  id bigserial PK, job_id uuid, from_page_id uuid FK,
  to_normalized_url text, to_page_id uuid NULL,   -- set when the target is a crawled page
  is_internal bool
)
  UNIQUE ux_links_from_to (from_page_id, to_normalized_url)
  INDEX ix_links_job (job_id)

outbox_messages(id uuid PK, type text, payload jsonb, created_at, sent_at NULL)
  INDEX ix_outbox_unsent (created_at) WHERE sent_at IS NULL   -- partial index
```

Notes:
- **Tree vs graph.** The site is a graph. The UI tree is the BFS spanning tree given by `pages.parent_page_id`, where the first page to claim a URL becomes its parent. All edges stay in `page_links` for the ratio and for future graph views. The tree is loaded with **one query** (`WHERE job_id = @id`) and assembled in memory in O(n). With the 200-page cap there are no recursive CTEs and no N+1 queries.
- **History pagination.** Offset paging is used for simplicity at this scale. The index already supports keyset (`created_at, id`) paging for when tables grow.
- **Progress counters** are denormalized on `crawl_jobs` and updated in the page transaction, so status polling is a single primary-key lookup.
- **Long URLs.** Unique indexes on `text` work in Postgres up to the btree size limit. URLs longer than 2,048 chars are rejected at normalization. Hashing (`sha256(normalized_url)`) is the fallback if that turns out to be too restrictive.

## Frontend

React + TypeScript + Vite, built to static files and served by nginx in Compose.

- **Start Crawl** (`/`): URL input (validated), optional `maxDepth`. Submitting navigates to `/jobs/:id`.
- **Job Details** (`/jobs/:id`): status badge, timestamps, failure reason, and a progress bar fed by counters. It polls every 1.5s through TanStack Query `refetchInterval` while the job is `Pending`/`Running`, then stops. When complete it shows a collapsible tree: each node has the URL, HTTP status and the ratio as a percentage, with failed or skipped pages marked. A Cancel button appears while the job is running.
- **History** (`/history`): a paginated table (URL, status, created, completed). Clicking a row opens Job Details.
- Every screen has explicit loading, error (with retry) and empty states. The API base URL comes from `VITE_API_URL`.

## Observability

- **Structured logs:** Serilog JSON to stdout. Every log line inside message handling carries `JobId`, `PageId`, `MessageId`, `Attempt` and `CorrelationId` through `ILogger.BeginScope`. The API adds `JobId` once it's known and returns `X-Correlation-Id`. The correlation ID travels in message headers, so one job can be followed across both services with a single filter.
- **Health:** `/health/live` checks that the process is up. `/health/ready` checks Postgres and RabbitMQ through `AspNetCore.HealthChecks.*`. The worker hosts a minimal Kestrel endpoint just for health. Compose healthchecks use these endpoints.
- _(Next step)_ OpenTelemetry traces with W3C trace context propagated through message headers, plus metrics such as pages/sec, retry count and DLQ depth.

## Testing

| Layer | What | How |
|-------|------|-----|
| Unit | `UrlNormalizer`: relative paths (`../x`, `./x`, `/x`, `//host/x`), `<base href>`, fragments and bare `#`, ignored schemes, case and default port, empty path | xUnit `[Theory]` tables |
| Unit | `DomainLinkRatio`: all internal, all external, mixed, zero links → 0, duplicates counted once, self-links, subdomain = external | xUnit |
| Unit | `LinkExtractor`: anchors, malformed hrefs, non-HTML content | Fixture HTML strings |
| Integration | Crawl a **local fixture site**: a static HTML folder served by a fake `HttpMessageHandler`, including a cycle, a duplicate link, an external link, a non-HTML file and a 500. The expected tree, ratios and statuses are asserted against real Postgres. | Testcontainers Postgres plus an in-memory publisher that feeds tasks straight back to the handler |
| Integration | **Idempotency:** deliver the same `CrawlPageTask` twice (and concurrently) and assert no duplicate `pages`/`page_links` rows and correct counters | Same harness |
| CI | GitHub Actions: `dotnet build`, `dotnet test`, `npm ci && npm run build` | `.github/workflows/ci.yml` |

## Priorities, cuts and next steps

This is a time-boxed (~4h) assignment. The order follows the rubric weights and the riskiest parts first.

**Implemented first, and why**
1. **Domain logic and unit tests** (normalization, ratio, link extraction). This is the correctness core (30% of the rubric). It's cheap to get right in isolation and everything else depends on it.
2. **DB schema with unique constraints, and the idempotent page handler.** These are the hard guarantees (idempotency, de-dup, the cap). They're easiest to design in from the start and painful to retrofit.
3. **RabbitMQ topology with retry tiers and the DLQ, plus the outbox.** This is the event-driven robustness the assignment calls out.
4. **API endpoints and health checks**, then **Docker Compose**, so the whole system runs with one command.
5. **The React UI.** The workflow comes first and styling is kept minimal.
6. **The fixture-based integration test and the CI workflow.**

**Cut (or kept minimal), and why**
- **SSE/SignalR push.** Polling meets the requirement and has fewer moving parts.
- **robots.txt, per-host rate limiting and politeness delays.** These are important for a real crawler but outside the evaluated scope. Only same-host crawling with a page cap is in place.
- **Authentication and multi-tenancy.** Not requested.
- **A separate read model or event projection.** The API reads the worker's tables directly. Documented as a trade-off.
- **A rich tree UI** (virtualization, search). 200 nodes don't need it.
- **Query-string canonicalization** (sorting params, stripping `utm_*`). Risky to get wrong and not required.

**Next, with more time**
- SignalR or SSE progress push to replace polling.
- robots.txt, a per-domain concurrency limit and crawl-delay handling. Also a canonical `<link rel="canonical">` option.
- A DLQ replay endpoint and admin view. Alerting on DLQ depth.
- OpenTelemetry tracing across API → broker → worker, and Prometheus metrics.
- The worker emitting domain events (`PageCrawled`, `JobCompleted`) and the API projecting them, so each service owns its data.
- Keyset pagination for history. Partitioning or archiving of `page_links` for large jobs.
- Load test with a large fixture site. Tuning of prefetch and worker concurrency.
- Contract tests for message schemas, plus a schema-versioning policy.

## Known limitations

- JavaScript-rendered links are not discovered, because there's no headless browser.
- `www.` and bare domains are treated as different hosts by design. Subdomains are external.
- The tree shows one parent per page (the first discoverer). Other inbound links exist only in `page_links`.
- The job completes when there are no pending pages. If a worker dies mid-page, the page waits for its 2-minute lease to expire and for redelivery, which delays completion.
- Migrations run on API startup. This is not suitable for multi-instance production deploys.
- Default credentials in `docker-compose.yml` are for local use only.
