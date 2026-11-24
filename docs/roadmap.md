# Feature roadmap

Implementation plan for the next set of FedTech Search features. Work through the phases in
order; each phase should leave `dotnet build`, `dotnet test` and `npm run build` green and be
committed on its own. Read `CLAUDE.md` first for architecture, commands and the enrichment
pipeline.

Goal of all of this: make the site genuinely useful to people hunting federal tech jobs, then
deploy it on AWS (S3 + CloudFront for `web/`, App Runner or ECS Fargate for the API, RDS
Postgres, ingest as a scheduled ECS task via EventBridge, SES for email).

## Ground rules

- **Verify external facts before coding against them.** USAJOBS API fields, Google JobPosting
  rules, SES limits and Angular SSR APIs change. Check current docs (web search) and look at a
  real USAJOBS response before relying on a field name.
- **Schema changes go through EF Core migrations** (Phase 0 sets this up). Never go back to
  `EnsureCreated` for new tables.
- **Extraction changes:** bump `JobEnricher.AlgorithmVersion` (code) or `"version"` in
  `taxonomy.json` (data) so stored jobs are re-enriched. Keep `docs/fields.md` in sync.
- **Facets stay clean.** Any new derived field gets a fixed value list in Core (like
  `Seniority`, `Clearance`) and ordered facets via `InOrder` in `JobSearch`.
- **New query parameters** go on `JobSearchRequest`, are read and written in the Angular URL
  state (`readUrl` / `buildParams` in `web/src/app/app.ts`) so searches stay shareable, and
  are listed in the README API section.
- **Personal data:** emails are the only personal data the app stores. Never log them, store
  tokens hashed, and add a privacy page before alerts go live.
- **Public endpoints that write** (subscribe, feedback) get ASP.NET Core rate limiting and
  input length limits.
- **Tests:** pure logic gets xUnit tests with no database (pattern: `RuleExtractorTests`,
  `LocationQueryTests`). Query logic that needs Postgres gets a Testcontainers-based test
  project (`tests/FedTechSearch.Data.Tests`) created in Phase 0.

---

## Phase 0: Foundations

> **Done** (2026-11-15). Deviation: `/alerts` and `/stats` routes are added in Phases 4 and 5,
> not as empty pages now.

Needed by later phases. Small, but do it first.

1. **Git baseline.** The repo has no commits yet. Make sure `.env` stays ignored, then commit
   the current state.
2. **EF Core migrations.**
   - Add `Microsoft.EntityFrameworkCore.Design` to the API or a small migrations host, and a
     design-time `IDesignTimeDbContextFactory<AppDbContext>`.
   - Create an `InitialCreate` migration matching the current model. Move the raw-SQL GIN
     full-text index (`ix_jobs_search`) into the migration (`migrationBuilder.Sql`).
   - `DbInitializer.EnsureDatabaseAsync` switches from `EnsureCreatedAsync` to
     `MigrateAsync`, keeping the startup retry loop. Only one process should migrate: the
     ingest worker migrates; the API waits for the schema (retry until the `jobs` table
     exists) instead of racing.
   - Document `dotnet ef migrations add <Name> --project src/FedTechSearch.Data` in CLAUDE.md.
3. **Angular router.** Add `provideRouter` with routes: `/` (search, the current `App`
   template moved into a `SearchPage` component), `/jobs/:id` (job page), `/alerts`
   (manage alerts), `/stats`, `/privacy`. Keep search state in query params on `/`.
   Move the large inline template/styles into separate files if that keeps components
   readable.
4. **Data test project.** `tests/FedTechSearch.Data.Tests` with Testcontainers
   (`Testcontainers.PostgreSql`), a fixture that applies migrations and loads
   `tests/fixtures/sample-search.json` through `IngestService.RunFromFileAsync`, and one
   smoke test for `JobSearch.SearchAsync`.

**Done when:** a fresh database is created by migrations, the fixture loads, the existing UI
works at `/`, and both test projects pass.

---

## Phase 1: Freshness and "who can apply" (quick wins, little schema)

> **Done** (2026-11-15), delivered with the filter redesign: category chips above the results open a
> sectioned Filters dialog. It also added Exclude, Experience (role type, promotion potential),
> Salary & grade (GS range), Commitment (schedule, appointment, travel) and Department filters.

### 1a. Freshness filters and badges

- `JobSearchRequest.PostedWithinDays` (1, 3, 7, 14): `OpenDate >= now - n days`
  (fall back to `FirstSeen` when `OpenDate` is null).
- `JobSearchRequest.ClosingWithinDays` (3, 7): `CloseDate <= now + n days`.
- New derived flag `MayCloseEarly` on `Job`/`EnrichedFields`: regex over summary, duties,
  requirements and the raw "how to apply" text for applicant-cap language such as
  "first 100 applications", "may close early", "closes once ... applications are received".
  Check real postings for the exact phrasing. Add the flag to the `flags` facet
  ("May close early") and bump `AlgorithmVersion`.
- UI: a "Posted" select in the filter column; card badges "New" (opened in the last 2 days),
  "Closes in N days" (N <= 5, warn color), "May close early".

### 1b. Plain-language "who can apply"

`HiringPaths` is already stored (lowercase USAJOBS codes). Turn it into a facet.

- Core: `HiringPathLabels` with a fixed, ordered map from code to label, e.g.
  `public` Open to the public, `vet` Veterans, `mspouse` Military spouses,
  `disability` Individuals with disabilities (Schedule A), `graduates` Recent graduates,
  `student` Students, `nguard` National Guard and Reserves, `native` Native Americans,
  `peace` Peace Corps and AmeriCorps alumni, `fed-competitive` / `fed-excepted` /
  `fed-internal-search` / `fed-transition` Current or former federal employees (group these
  under one label unless users need the distinction), `land` Land and base management,
  `overseas` Family of overseas employees, `ses` Senior executives,
  `special-authorities` Special authorities. Confirm the code list against current USAJOBS
  docs; unknown codes fall back to the raw code.
- `JobSearchRequest.HiringPath` (string[], OR'ed) filtering `HiringPaths` with array overlap.
- Facet `hiringPath` returned with labels (keep the code as the filter value; return
  `{ value: code, label, count }` or a separate label map in `/api/meta`).
- UI: "Who can apply" facet near the top of the filter column; each card shows one plain
  line, e.g. "Open to: the public, veterans, Schedule A". Add a short info tooltip/link for
  Schedule A explaining it is a non-competitive hiring path for people with disabilities.

**Tests:** freshness filters, label mapping, `MayCloseEarly` regex positives and negatives.

---

## Phase 2: Radius search ("within 50 miles of Houston, TX")

> **Done** (2026-11-15). Cities default to 25 miles (Exact city / 10 / 25 / 50 / 100), cards show
> distance, and there is a "Nearest" sort. Deviation: places are resolved with a live query
> over `job_locations` (about 3.4k rows) instead of a `places` view, which isn't needed at this size.

Text matching on location names misses nearby duty stations (Ellington Field, Sugar Land
for Houston). USAJOBS gives `Latitude`/`Longitude` per `PositionLocation`; the mapper drops
them today.

**Data model (migration):**
- New table `job_locations`: `Id`, `JobId` (FK, cascade), `Name`, `City`, `State`,
  `Latitude`, `Longitude`. Index on `(Latitude, Longitude)` and on `JobId`.
  Keep `Job.Locations`/`States` arrays as they are (facets and existing filters use them).
- `RawJob.GeoLocations` list filled by `UsaJobsMapper` (skip 0/0 and missing coordinates).
- `IngestService` replaces a job's location rows when its content hash changes.

**Resolving the user's place without a paid geocoder:**
- Build a lookup from the data itself: average lat/lon per normalized "City, State" over
  `job_locations` (materialized view or a `places` table refreshed after each ingest).
  `LocationQuery` already normalizes "Houston, TX" to "Houston, Texas"; reuse it.
- If the place is unknown, fall back to the current text match and tell the UI
  (`response.locationMode = "text"`), so it can say "showing exact matches for X".

**Query:**
- `JobSearchRequest.RadiusMiles` (10, 25, 50, 100; default 25 when a city is given,
  ignored for state-only searches).
- Bounding-box prefilter on lat/lon, then haversine distance in SQL. Plain SQL math is
  enough at this data size; only reach for PostGIS/earthdistance if profiling says so.
  If the translation is awkward in LINQ, use a raw-SQL subquery that returns matching
  `JobId`s and compose it into the existing `IQueryable`.
- Remote jobs still match when `includeRemote` is on (current behavior).
- Optional: return the nearest distance per result so cards can show "12 mi away".

**UI:** a radius select next to the location box; show distance on cards when available.

**Tests:** haversine unit test (Houston to Sugar Land is about 20 miles), place lookup,
a Testcontainers test that a 50-mile Houston search returns an Ellington Field fixture job
(add one to the fixture) and a 10-mile search does not.

---

## Phase 3: "Wrong tag?" feedback

> **Done** (2026-11-15). Also added an `/admin/feedback` page that reads the summary with the admin
> key, and a same-day duplicate guard (same client, job and report are stored once).

Turns real users into a tuning loop for `taxonomy.json`.

- Table `tag_feedback`: `Id`, `JobId`, `Field` (`roleFamily`, `tech`, `workingTitle`,
  `seniority`, `clearance`, `workArrangement`), `CurrentValue`, `SuggestedValue` (optional),
  `Note` (optional, max 500 chars), `CreatedAt`, `TaxonomyVersion`, `ClientHash`
  (hash of IP + daily salt, for abuse control only, never the raw IP).
- `POST /api/jobs/{id}/feedback`, validated against known fields and taxonomy values,
  rate limited per client (e.g. 20/hour).
- `GET /api/admin/feedback/summary` protected by an `Admin:ApiKey` header check: counts
  grouped by field + current value + suggested value, newest examples with job links.
  This is the report used to fix patterns.
- UI: small "Wrong tag?" link in the expanded card; a compact form to pick the field,
  the wrong value and optionally the right one. Confirm with a thank-you message.

**Tests:** validation rejects unknown fields/values; summary grouping.

---

## Phase 4: Saved searches with email alerts

> **Done** (2026-11-24). Digests go out at 7:00 AM Eastern (daily) and Monday 7:00 AM (weekly) from an
> `AlertWorker` that checks every 15 minutes, rather than right after each ingest. Each saved search
> is confirmed separately (`saved_searches.ConfirmedAt`), so nobody can attach searches to another
> address. Both senders are built (SMTP for MailPit, SES v2 with raw MIME); SES is verified only once
> a sending domain exists.

The biggest reason for people to come back. Email-only, no passwords.

**Data (migration):**
- `subscribers`: `Id`, `Email` (unique, normalized lowercase), `ConfirmedAt`,
  `CreatedAt`, `UnsubscribedAt`.
- `saved_searches`: `Id`, `SubscriberId`, `Name`, `QueryString` (the canonical search query
  string, same format as the UI URL), `Frequency` (`Daily`, `Weekly`), `LastNotifiedAt`,
  `CreatedAt`, `PausedAt`.
- `auth_tokens`: `Id`, `SubscriberId`, `TokenHash`, `Purpose` (`Confirm`, `Manage`),
  `ExpiresAt`, `UsedAt`. Tokens are random 32 bytes, emailed once, stored as SHA-256.

**Flow:**
1. "Email me new matches" button on the search page sends the current query string and an
   email to `POST /api/alerts`. Always respond the same way whether or not the email exists
   (no account enumeration). Rate limit hard.
2. Double opt-in: a confirm link (`/alerts/confirm?token=`) activates the subscriber and the
   saved search. Nothing is sent before confirmation.
3. Every email has a manage link (magic link, `Manage` token) to `/alerts` where the user can
   rename, pause, change frequency or delete searches, and a one-click unsubscribe link plus
   `List-Unsubscribe` / `List-Unsubscribe-Post` headers.

**Sending:**
- `IEmailSender` in Data with `SesEmailSender` (AWS SDK `AmazonSimpleEmailServiceV2`) and a
  dev sender. Add MailPit to `docker-compose.yml` for local testing and an SMTP dev sender
  pointed at it.
- `AlertService` runs in the ingest worker after a successful API pull: for each active
  saved search due by frequency, parse the query string into a `JobSearchRequest`, add
  "first seen since `LastNotifiedAt`", run `JobSearch`, and send one digest email if there
  are results (cap at ~20 jobs with a "see all" link). Update `LastNotifiedAt` only after a
  successful send. Make it idempotent if the worker restarts mid-run.
- Plain, accessible HTML email plus a text part. Each job shows working title, official
  title, agency, location, salary, closing date and the USAJOBS link.

**AWS notes:** SES starts in sandbox mode (only verified recipients) until production
access is granted; the sending domain needs SPF, DKIM and DMARC. Check current SES docs.

**Privacy:** `/privacy` page (what is stored, why, how to delete) linked from the alert
form and every email. Deleting the last saved search offers to delete the subscriber.

**Tests:** query string round-trip into `JobSearchRequest`; due-date logic for daily vs
weekly; tokens are single-use and expire; a Testcontainers test that a new matching job
produces exactly one digest and a second run produces none.

---

## Phase 5: Public stats page

Useful for job hunters, and shareable content for launch posts.

- Table `daily_stats`: `Date`, `Dimension` (`tech`, `roleFamily`, `agency`, `state`,
  `seniority`, `clearance`, `work`), `Value`, `ActiveCount`, `NewCount`. Primary key
  `(Date, Dimension, Value)`. The ingest worker upserts today's snapshot after each run.
- `GET /api/stats?dimension=tech&days=90` returns current top values and a daily series
  for the top N. Cache responses for an hour (output caching).
- `/stats` page: "Top tech in federal postings", "Agencies hiring the most engineers",
  "Remote share by role", "New postings this week". Charts as simple accessible SVG or CSS
  bars with visible numbers (no heavy chart library unless needed). Each chart links into a
  pre-filtered search.

**Tests:** snapshot upsert is idempotent per day; stats endpoint shape.

---

## Phase 6: Job pages Google can index

The SPA renders in the browser, so crawlers see little. Goal: each active job has a real
HTML page with JobPosting structured data, so listings can appear in Google's job search.

**Approach (pick after checking current options):**
- Preferred: the API serves server-rendered job pages at `/jobs/{id}` (minimal API returning
  HTML from a small template; no Razor Pages needed) with title, meta description,
  canonical URL, Open Graph tags, the full job content, and JSON-LD `JobPosting`
  (`title`, `description`, `datePosted`, `validThrough`, `hiringOrganization`,
  `jobLocation` or `jobLocationType: TELECOMMUTE` + `applicantLocationRequirements` for
  remote, `baseSalary`, `employmentType`, `identifier`, `directApply: false`). The page
  loads the Angular app or links to it for interactive search. CloudFront routes
  `/jobs/*` to the API and everything else to S3.
- Alternative: Angular SSR (`@angular/ssr`) with a Node server. More moving parts on AWS.
- Closed or retired jobs return 410 or drop the structured data, per Google's guidance.
- `GET /sitemap.xml` listing active job pages and `/stats`; `robots.txt` pointing to it.
- Verify the markup with Google's Rich Results Test and follow Google's current JobPosting
  content policies (they change; check before shipping).

**Tests:** JSON-LD serialization for an on-site job, a remote job and a closed job; sitemap
contains only active jobs.

---

## After the roadmap

- AWS infrastructure as code (Terraform or CDK) for the deployment described at the top,
  with GitHub Actions CI running build, tests and migrations.
- Revisit the LLM pass for AWS: rules-only at launch, Amazon Bedrock with a small model
  later if title quality needs it.
