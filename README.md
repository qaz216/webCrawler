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
| Database       | PostgreSQL 16, Npgsql + Dapper, plain SQL migrations        |
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
  Crawler.Infrastructure/  # SQL migrations + Dapper store, RabbitMQ topology/publisher/consumer,
                           # HttpPageFetcher (+ resilience), outbox dispatcher
  Crawler.Api/             # Service A: REST endpoints, health checks, CORS, ProblemDetails
  Crawler.Worker/          # Service B: consumer host, outbox dispatcher, health endpoint
  Crawler.Contracts/       # Message DTOs shared by API and worker (versioned)
web/                       # React app
tests/
  Crawler.UnitTests/           # Normalization, ratio, link extraction, tree building, retry/DLQ policy
  Crawler.IntegrationTests/    # Fixture-site crawl, idempotency, API and worker end-to-end
                               # against Testcontainers PostgreSQL + RabbitMQ
docker-compose.yml
```

The dependency direction is `Api/Worker → Infrastructure → Application → Domain`. The domain project has no framework dependencies, so the logic that matters most (normalization and the ratio) is trivial to unit test.

### Key architectural choices

- **One message per page, not one per job.** The worker consumes `CrawlPageTask` messages. Each processed page publishes tasks for its newly discovered same-domain children at `depth + 1`. Benefits:
  - Pages are crawled in parallel: 4 at a time per worker process, and across worker instances. To scale with `docker compose up --scale worker=3`, first remove the worker's fixed host port mapping.
  - A slow or failing page retries alone instead of restarting the whole job.
  - Idempotency, retries and the DLQ apply at a meaningful granularity.
- **The worker owns writes for crawl results. The API owns job creation and all reads.** Both share one database through the Infrastructure layer. For a two-service system, a shared schema is pragmatic. A stricter split (the worker emits `PageCrawled` events and the API projects them) is listed under next steps.
- **Transactional outbox.** Page results, edges, newly claimed child pages and the outgoing child tasks are written in **one DB transaction**. A dispatcher publishes the outbox rows to RabbitMQ afterwards. This closes the "committed to the DB but crashed before publishing" gap that would silently lose parts of the tree.
- **The DB is the source of truth for de-duplication**, enforced with unique constraints rather than in-memory sets. Multiple worker instances and redeliveries are therefore safe.
- **Dapper and hand-written SQL instead of EF Core.** The correctness-critical writes are `ON CONFLICT DO NOTHING`, `UPDATE … RETURNING`, `INSERT … SELECT … LIMIT` and `FOR UPDATE`. In SQL they're explicit and reviewable, whereas EF would hide them or need raw SQL anyway. Migrations are numbered `.sql` files embedded in the Infrastructure assembly and applied by a ~50-line migrator.

## Running locally

Prerequisites: Docker Desktop (or Docker Engine + Compose v2).

```bash
docker compose up -d --build
```

| Service              | URL                                   |
|----------------------|---------------------------------------|
| **React UI**         | **http://localhost:3000**             |
| Crawl API (+Swagger) | http://localhost:8080/swagger         |
| API health           | http://localhost:8080/health/ready    |
| Worker health        | http://localhost:8081/health/ready    |
| RabbitMQ management  | http://localhost:15672 (guest/guest)  |
| PostgreSQL           | localhost:5432 (crawler/crawler)      |

Database migrations (the embedded `.sql` scripts) are applied automatically when the API or the worker starts, under a Postgres advisory lock so concurrent starts are safe. Each service waits and retries until the database is reachable. That's fine for local and demo use, but not for production. Docker Compose healthchecks gate start order: the API and worker wait for Postgres and RabbitMQ to be healthy.

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

The dev UI is at http://localhost:5173.

From the command line (or open http://localhost:8080/swagger):

```bash
curl -X POST http://localhost:8080/api/jobs -H "Content-Type: application/json" -d '{"url":"https://books.toscrape.com/","maxDepth":1}'
```

Run the tests (Docker must be running for Testcontainers):

```bash
dotnet test
```

Alternatively, run them inside a Linux SDK container, as CI would. This is useful where local policy blocks freshly built test DLLs; Windows Smart App Control does this intermittently, and the error is "An Application Control policy has blocked this file":

```bash
docker run --rm -v "$PWD:/src:ro" -v /var/run/docker.sock:/var/run/docker.sock -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal mcr.microsoft.com/dotnet/sdk:8.0 sh /src/scripts/test-in-docker.sh
```

## API

All routes live under `/api`, and Swagger UI is at `/swagger`. Errors use RFC 7807 `ProblemDetails`: validation failures are `400` with per-field `errors`, an unknown job is `404`, and canceling a finished job is `409`. Every problem includes a `correlationId`. Responses echo `X-Correlation-Id`: send one to tie your request to the API's logs, or the API generates one. Enums are serialized as strings. Code: [`JobEndpoints.cs`](src/Crawler.Api/Endpoints/JobEndpoints.cs).

| Method | Route                          | Description                                                     |
|--------|--------------------------------|-----------------------------------------------------------------|
| POST   | `/api/jobs`                    | Create a job. Body `{ "url": string, "maxDepth"?: number }` → `202 { "jobId" }` |
| GET    | `/api/jobs/{jobId}`            | Status, timestamps, failure reason, progress counters            |
| GET    | `/api/jobs/{jobId}/tree`       | Hierarchical result (nested nodes)                               |
| GET    | `/api/jobs?page=1&pageSize=20` | History, most recent first, paginated                            |
| POST   | `/api/jobs/{jobId}/cancel`     | Cancel a Pending or Running job (optional feature)              |
| GET    | `/health/live`, `/health/ready`| Liveness and readiness (DB + broker)                             |

Validation for `POST /api/jobs`:
- `url` must be an `http`/`https` URL. As in a browser's address bar, a bare host such as `google.com` or `example.com:8443/docs` gets `https://` added. Input that names another scheme (`mailto:`, `ftp://`) or is a relative path is rejected.
- `maxDepth` defaults to `2` and must be between `0` and `5`.
- `maxPages` is a server-side setting (default `200`, configurable through `Crawler__MaxPagesPerJob`).

`GET /api/jobs/{jobId}` returns:

```json
{
  "jobId": "97ff2bea-…", "url": "https://books.toscrape.com/", "status": "Running",
  "maxDepth": 1, "maxPages": 200,
  "createdAt": "2026-10-07T15:43:29.94Z", "startedAt": "2026-10-07T15:43:30.12Z", "completedAt": null,
  "failureReason": null,
  "progress": { "discovered": 74, "completed": 40, "failed": 1, "pending": 33, "percent": 55.4 }
}
```

`progress` drives the progress bar. `percent` is finished ÷ discovered. The total isn't known up front and grows as links are found, so the percentage can move backwards. That's honest rather than a fake estimate. The tree endpoint works while a job is running and returns the partial tree.

Tree response: `{ "jobId", "status", "root": node }`. Each node has this shape:

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
- **If the start page redirects to another host, that host becomes the starting domain.** For example, `google.com` → `www.google.com`. The site has told us where it lives, and without this rule such a crawl would stop at its first page. The job stores the effective domain (`startingDomain` in the API, shown in the UI as "www.google.com (redirected from google.com)"). The rule applies to the start page only: any other page that redirects off the starting domain is `Skipped`.
- Depth: the root is depth `0`. Children are enqueued only while `depth < maxDepth`.
- **HTML only.** A response is parsed only if its `Content-Type` is `text/html` or `application/xhtml+xml`. Anything else is stored as `Skipped (non-HTML)` with no links.
- Redirects are followed (max 5). The **final** URL is the base for resolving relative links. Apart from the start page (above), redirects that leave the starting domain are not followed further.
- **Max pages per job: 200** by default. The cap is enforced atomically in the DB (see below), so concurrent workers cannot overshoot it.

**Normalization** (`UrlNormalizer`, the key to de-duplication)
1. Resolve relative links against the page base URL, honouring a `<base href>` element if present.
2. Ignore `mailto:`, `tel:`, `javascript:`, `data:` and any other non-`http(s)` scheme.
3. Drop the fragment (`#section`). A link that is only `#anchor` therefore normalizes to the page itself.
4. Lowercase the scheme and host. Remove default ports (`:80`, `:443`).
5. An empty path becomes `/`. The path's case and trailing slash are otherwise preserved, because servers may treat them differently.
6. The query string is kept as-is. It is not re-ordered and tracking parameters are not stripped. Aliases this creates are caught by content de-duplication instead (below).

**Content de-duplication** (`ContentFingerprint`)

URL normalization can't know that `/` and `/index.html`, `?utm_source=x` and no query, or `/page` and `/page/` serve the same page. Rules that guessed (such as "strip `index.html`") would be wrong for some servers. So after fetching, the worker fingerprints the HTML with SHA-256 (line endings and surrounding whitespace ignored).
- The **first** page of a job with a given fingerprint is the original.
- A later page with the same fingerprint is stored with status **`Duplicate`** and `duplicate_of_page_id` pointing at the original. Its links are not recorded or followed, and it has no ratio of its own, because the original's applies.
- Duplicates **stay in the tree** as a node showing "duplicate of …", so you can see where that link led. They're counted separately (`progress.duplicates`).
- It's race-safe: completions are serialized per job by the job row lock, and a partial unique index on `(job_id, content_hash) WHERE status = 'Completed'` guarantees a single original. A test processes two aliases concurrently and asserts exactly one original.

On books.toscrape.com, `/index.html` is recorded as a duplicate of `/` instead of crawling the home page twice.

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
| `crawl.pages`            | quorum queue   | `CrawlPageTask` work queue (routing key `page`). `x-delivery-limit = 10` dead-letters messages that keep crashing consumers before they ack. |
| `crawl.pages.retry.5000ms`   | queue, TTL 5s  | Delay queue with no consumers. When the TTL expires, messages dead-letter back to `crawl` / `page`. |
| `crawl.pages.retry.30000ms`  | queue, TTL 30s | Second delay tier                                              |
| `crawl.pages.retry.120000ms` | queue, TTL 2m  | Third delay tier                                               |
| `crawl.dlx` → `crawl.pages.dlq` | direct exchange + quorum queue | Poison and exhausted messages              |

The delay is part of each retry queue's name. Changing the tiers (`RabbitMq:RetryDelays`) therefore creates new queues instead of clashing with the arguments of existing ones. The API and every worker declare the topology on startup; declarations are idempotent. Code: [`RabbitMqTopology.cs`](src/Crawler.Infrastructure/Messaging/RabbitMqTopology.cs).

Consumers use manual acks, `prefetch = 10` and 4 concurrent handlers per worker process. Publishing uses publisher confirms with `mandatory`, so a publish counts as done only once the broker has accepted it. Every delivery ends in exactly one way:
- **ack** (processed, or a duplicate);
- **re-publish to a retry tier, then ack**;
- **publish to the DLQ, then ack**.

The follow-up publish is always confirmed before the ack. A crash between the two causes a redelivery, never a lost message. If the follow-up publish itself fails (broker trouble), the delivery is nacked and requeued. Code: [`PageTaskConsumer.cs`](src/Crawler.Infrastructure/Messaging/PageTaskConsumer.cs).

The **outbox dispatcher** polls `outbox_messages` every 250 ms. It takes rows with `FOR UPDATE SKIP LOCKED`, so the API and several workers can all dispatch without double-sending, publishes them with confirms, then marks them sent in the same transaction.

### Message schema

`CrawlPageTask` (v1), JSON body. AMQP properties: `message-id` (= pageId), `correlation-id` (= jobId), `type` (`crawl.page-task.v1`), `content-type`. Headers: `x-attempt` (absent on the first delivery). Dead-lettered copies also carry `x-error`, `x-error-type` and `x-dead-lettered-at`.

```json
{
  "schemaVersion": 1,
  "messageId": "6f1c…",              // = pageId: unique per (job, normalized URL)
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
| `UNIQUE (job_id, url)` on `pages` (the URL is stored normalized). Child pages are **claimed** with `INSERT … ON CONFLICT DO NOTHING RETURNING id`, and a task is queued **only for rows the insert returned**. | Re-processing the same URL within a job, both from different parents and from redelivery |
| `UNIQUE (from_page_id, to_url)` on `page_links`. Edges are inserted with `ON CONFLICT DO NOTHING`. | Duplicate edges when a task is processed twice |
| A **page lease**. Processing starts with `UPDATE pages SET status='Processing', lease_until=now()+'2 min' WHERE id=@id AND (status='Pending' OR (status='Processing' AND lease_until < now()))` (and the job is still active). If no row was updated, the store works out why: page already finished → **ack and skip**; leased by another consumer → **transient** (retry later); job canceled → **ack and drop**; page unknown → **poison**. | Two consumers working the same task at once, and duplicates of a page that's already finished |
| On a transient failure the handler **releases** the lease (back to `Pending`) before rethrowing, so the retried delivery can take the page at once. If the release itself fails, the lease simply expires. | A retried delivery being blocked by its own earlier attempt |
| The page result, edges, child claims, their outbox tasks, the job counters and job completion commit in **one transaction**. Its first statement moves the page to its final status only `WHERE status IN ('Pending','Processing')`. If that touches 0 rows, the transaction stops: it was a duplicate. | Partial writes. A crash before commit means redelivery redoes everything safely. A crash after commit means redelivery hits the guard and is skipped. |
| The outbox message id is the **page id**, so it's unique per (job, URL) and `ON CONFLICT DO NOTHING` protects it. The dispatcher marks rows sent only after the publisher confirm. A duplicate publish is absorbed by the lease and status guards. | Duplicate publishes from the outbox dispatcher |
| Job completion is a conditional update: `… SET status='Completed' WHERE id=@id AND status IN ('Pending','Running') AND pages_completed + pages_failed >= pages_discovered`. | Double completion, and racing workers |
| Content de-dup: under the same job lock, a Completed page whose HTML fingerprint matches an existing original becomes `Duplicate` (not expanded). `UNIQUE (job_id, content_hash) WHERE status='Completed'` backs this up. | Crawling the same page twice under different URLs, including when the aliases are processed concurrently |
| The page cap: the completing transaction takes `SELECT … FROM crawl_jobs … FOR UPDATE`, then claims at most `max_pages - pages_discovered` new URLs (`INSERT … SELECT … WHERE NOT EXISTS … LIMIT @remaining`). Child claiming is therefore serialized per job, and only per job: different jobs never block each other. | Overshooting the cap under concurrency |

These guarantees are covered by integration tests against a real PostgreSQL in [`tests/Crawler.IntegrationTests`](tests/Crawler.IntegrationTests): sequential duplicate delivery, 5 concurrent deliveries of the same task, two same-content aliases processed concurrently, the page cap, transient retry, canceled jobs and poison messages.

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

The full schema, with comments, is in the [migrations](src/Crawler.Infrastructure/Persistence/Migrations): [`0001_initial_schema.sql`](src/Crawler.Infrastructure/Persistence/Migrations/0001_initial_schema.sql) and [`0002_content_dedup.sql`](src/Crawler.Infrastructure/Persistence/Migrations/0002_content_dedup.sql). In summary:

```sql
crawl_jobs(
  id uuid PK, url text, root_host text, max_depth int, max_pages int,
  status text CHECK (...), failure_reason text,
  created_at, started_at, completed_at timestamptz,
  pages_discovered int, pages_completed int,      -- completed includes Skipped
  pages_failed int, pages_duplicate int
)
  INDEX ix_crawl_jobs_created_at (created_at DESC, id DESC)   -- history, keyset-friendly

pages(
  id uuid PK, job_id uuid FK, url text,           -- url is the normalized URL
  depth int, parent_page_id uuid NULL,            -- first discoverer → tree shape
  status text CHECK (...), http_status int, content_type text, error text,
  domain_link_ratio numeric(5,4), outgoing_link_count int,
  content_hash text NULL, duplicate_of_page_id uuid NULL,   -- content de-dup
  attempts int, lease_until timestamptz, discovered_at, finished_at timestamptz
)
  UNIQUE ux_pages_job_url (job_id, url)           -- URL de-dup key; also serves the tree query
  UNIQUE ux_pages_job_content_hash (job_id, content_hash) WHERE status = 'Completed'   -- content de-dup

page_links(                                       -- every outgoing edge, incl. external
  id bigint identity PK, job_id uuid FK, from_page_id uuid FK,
  to_url text, to_page_id uuid NULL,              -- set when the target is a page of this job
  is_internal bool
)
  UNIQUE ux_page_links_from_to (from_page_id, to_url)
  INDEX ix_page_links_job (job_id)

outbox_messages(id uuid PK, message_type text, payload jsonb, correlation_id uuid, created_at, sent_at NULL)
  INDEX ix_outbox_messages_unsent (created_at) WHERE sent_at IS NULL   -- partial index
```

Notes:
- **Tree vs graph.** The site is a graph. The UI tree is the BFS spanning tree given by `pages.parent_page_id`, where the first page to claim a URL becomes its parent. All edges stay in `page_links` for the ratio and for future graph views. The tree is loaded with **one query** (`WHERE job_id = @id`) and assembled in memory in O(n). With the 200-page cap there are no recursive CTEs and no N+1 queries.
- **History pagination.** Offset paging is used for simplicity at this scale. The index already supports keyset (`created_at, id`) paging for when tables grow.
- **Progress counters** are denormalized on `crawl_jobs` and updated in the page transaction, so status polling is a single primary-key lookup.
- **Long URLs.** Unique indexes on `text` work in Postgres up to the btree size limit. URLs longer than 2,048 chars are rejected at normalization. Hashing (`sha256(normalized_url)`) is the fallback if that turns out to be too restrictive.

## Frontend

React 19 + TypeScript + Vite, React Router, and TanStack Query for fetching, caching and polling. In Compose, the app is built to static files and served by nginx, which also proxies `/api` to the API container. The browser therefore sees one origin and needs no CORS. In development, Vite's dev server does the same proxying. Code: [`web/src`](web/src).

- **Start Crawl** (`/`): URL and optional `maxDepth`. The API is the single source of validation, and its per-field errors (from problem details) are shown under the matching inputs. A successful submit navigates straight to `/jobs/:id`.
- **Job Details** (`/jobs/:id`):
  - Status badge, timestamps, a live duration, and the failure reason if the job failed.
  - A progress bar with counts: finished/discovered, crawled, failed, duplicates, pending.
  - A Cancel button while the job runs.
  - Polling: the status every 1.5 s and the tree every 3 s, but only while the job is `Pending`/`Running`. The tree's query key includes the job status, so the final tree loads the moment the job completes. Partial results show while the crawl runs.
- **Page tree:**
  - Collapsible nodes, with Expand all / Collapse all.
  - Each page shows its path, status, HTTP code, the **Domain Link Ratio** as a percentage with a small bar, and its link count.
  - Failed and skipped pages show their reason.
  - **Duplicates** show "Same content as `/`, not crawled again". Clicking the original expands the tree to it and highlights it.
- **History** (`/history`): a paginated table (URL, status, created, duration, pages). The page number is kept in the URL (`?page=2`). Clicking a row opens Job Details. The table auto-refreshes while any listed job is still running.
- **States:** every screen has explicit loading, error (with retry, plus the server's correlation id for log lookup) and empty states. An unknown job id gets a "Job not found" page. A failed background poll keeps the last data and shows a warning instead of blanking the page.
- **Tests:** Vitest + Testing Library cover the formatting and tree helpers, the tree component (expand/collapse, duplicate links) and the Start Crawl form against a mocked API (success, field validation errors, API unreachable).

```bash
cd web
```

```bash
npm install
```

```bash
npm run dev
```

The dev server runs at http://localhost:5173, with `/api` proxied to `localhost:8080`. `npm test` runs the tests and `npm run build` type-checks and builds.

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
| Unit | `FailurePolicy`: transient → each delay tier → DLQ; poison → DLQ at once; unexpected → one retry | xUnit |
| End-to-end | The **real worker host** (outbox dispatcher, RabbitMQ consumer, handler) crawls the fixture site through a real broker. A page that's always down goes through all retry tiers into the DLQ and fails the job. A malformed message goes straight to the DLQ. | Testcontainers RabbitMQ + PostgreSQL |
| Frontend | Formatting and tree helpers, the tree component (expand/collapse, duplicate → original), and the Start Crawl form against a mocked API | Vitest + Testing Library (`cd web && npm test`) |
| CI | GitHub Actions: `dotnet build`, `dotnet test`, `npm ci && npm run build` | `.github/workflows/ci.yml` _(planned)_ |

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
- Migrations run when the API and worker start. This is not suitable for multi-instance production deploys.
- Default credentials in `docker-compose.yml` are for local use only.
- Content de-duplication needs an exact match. Pages that embed per-request values (timestamps, CSRF tokens, ads) get different fingerprints and are crawled as separate pages. An alias is still fetched once before it can be recognized, so it costs one request, but it is never expanded.
- A redirect's target URL isn't recorded as a page of its own. If another page links directly to that target, it's fetched again. Content de-duplication then records it as a `Duplicate`, so this costs a request but no repeated crawl.
- `www.` and bare domains are only unified through the start-page redirect rule. A site that serves both hosts without redirecting is crawled on the host you entered.
- Canceling stops new work, but pages that were queued stay `Pending` in the tree. In-flight pages finish.
- A dead-lettered page is marked `Failed` by the worker. If the database is also down at that moment, the page stays `Processing` and its job never completes. A reaper for expired leases would fix this.
