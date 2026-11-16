# FedTech Search

Federal tech jobs, searchable by what the job actually is.

On USAJOBS almost every tech role is filed under series 2210 with a title like
"IT Specialist (SYSADMIN)", and the real job is buried in the duties text. FedTech Search
pulls announcements from the official USAJOBS API, reads the duties, and turns each posting
into fields people can filter on: real role, working title, tech stack, seniority, clearance,
and work arrangement.

| USAJOBS says | FedTech Search shows |
|---|---|
| IT Specialist (APPSW), GS-12/13 | Full Stack Software Developer · C#, .NET, Angular, SQL Server · Mid to Senior · Telework |
| Information Technology Specialist (SYSADMIN/NETWORK), GS-13 | Senior DevOps / Cloud Engineer · Kubernetes, Terraform, AWS · Secret |
| Computer Scientist, ZP-3/4 | Data Science & AI/ML · PyTorch, Python, Go |

## How it works

```
USAJOBS API ──> Ingest worker ──> Postgres ──> Search API ──> Angular UI
                   │
                   ├─ Rules (always): taxonomy.json patterns score role families,
                   │  tag tech, map grades to seniority, normalize clearance
                   └─ LLM (optional): working title + tie-breaker when rules are unsure
```

- **Rules first.** `src/FedTechSearch.Core/Taxonomy/taxonomy.json` holds the specialty codes,
  14 role families and about 120 tech terms as regex patterns. Adding a technology is a JSON
  edit, not a code change.
- **Duties beat titles.** A parenthetical like `(SYSADMIN)` is one signal among many. A posting
  that says SYSADMIN but describes Kubernetes, Terraform and CI/CD lands in DevOps & Cloud.
- **LLM only where it helps.** In `LowConfidence` mode the LLM is asked only when the rules
  are unsure or the posting has no "serves as a ..." sentence to take a title from. Its
  answers are accepted only when they match the taxonomy, so facets stay clean. It talks to
  any OpenAI-compatible endpoint and defaults to a local Ollama server, so job text stays on
  your machine.
- **Cheap re-runs.** Each job stores a content hash. Unchanged postings skip enrichment (and
  LLM calls) on the next pull. Jobs that close or disappear are retired automatically.

See [docs/fields.md](docs/fields.md) for every field and how it is derived.

## Run it

You need the .NET 10 SDK, Node 22 and Docker.

### 1. Database

```bash
docker compose up -d db
```

### 2. Load jobs

Without an API key, load the sample fixture:

```bash
dotnet run --project src/FedTechSearch.Ingest -- --fixture tests/fixtures/sample-search.json
```

With a free key from https://developer.usajobs.gov/apirequest/ :

```bash
export UsaJobs__ApiKey=your-key
export UsaJobs__UserAgentEmail=you@example.com
dotnet run --project src/FedTechSearch.Ingest -- --once
```

Leave off `--once` to keep it running on a schedule (`Ingest:IntervalHours`, default 6).

To turn on the LLM with a local model:

```bash
ollama pull llama3.1:8b
export Llm__Mode=LowConfidence
```

### 3. API and UI

```bash
dotnet run --project src/FedTechSearch.Api --urls http://localhost:5080
cd web && npm install && npm start      # http://localhost:4200
```

### Everything in Docker

```bash
cp .env.example .env    # add your key
docker compose up --build
docker compose run --rm ingest --fixture /fixtures/sample-search.json   # optional sample data
```

UI on http://localhost:8080, API on http://localhost:5080.

## API

| Endpoint | What it does |
|---|---|
| `GET /api/jobs` | Search with filters; returns results plus facet counts |
| `GET /api/jobs/{id}` | Full job, including duties |
| `GET /api/meta` | Taxonomy lists for building filter UIs |

Filters repeat a key for several values. Values inside one filter are OR'ed, except `tech`,
which requires all of them:

```
/api/jobs?q=kubernetes&roleFamily=DevOps%20%26%20Cloud&tech=AWS&tech=Terraform&work=Remote&minSalary=120000
```

`location` takes free text such as `Houston, TX, US` or `Washington, DC`. State abbreviations
and a trailing `US`/`USA` are understood. A city searches **25 miles** around it by default;
`radius` takes `10`, `25`, `50`, `100`, or `0` for the exact city name. A state (`MD`) matches the
whole state. Remote jobs are included unless `includeRemote=false`. `sort=nearest` orders a radius
search by distance, and each item gets `distanceMiles`:

```
/api/jobs?location=Houston,%20TX&radius=50&sort=nearest&includeRemote=false
```

The response's `location` block says what happened: `{ "mode": "radius", "place": "Houston, Texas",
"radiusMiles": 50 }`. `mode` is `text` when the city is unknown (we fall back to exact name
matching) or `radius=0`, and `state` for state searches.

Other filters: `seniority`, `clearance`, `specialty`, `state`, `agency`, `series`,
`directHire`, `openToPublic`, `supervisory`, `includeClosed`, `sort`
(`relevance`, `newest`, `closing`, `salary`), `page`, `pageSize`.

| Filter | Values |
|---|---|
| `postedWithinDays` / `closingWithinDays` | 1, 3, 7, 14 / 3, 7 (other values are ignored) |
| `mayCloseEarly` | `true`: the posting caps applicants ("first 100 applicants") |
| `hiringPath` | Plain labels, e.g. `Veterans`, `Military spouses`, `Federal employees` (see `/api/meta`) |
| `department`, `schedule`, `appointment`, `travel` | Facet values, repeatable |
| `minGrade` / `maxGrade` | GS grade range; keeps GS/GG/GL/GM jobs whose grade span overlaps it |
| `minPromotionPotential` | Lowest acceptable career-ladder ceiling, e.g. `13` |
| `excludeAgency`, `excludeTech` | Repeatable; drops jobs that match any |
| `excludeKeyword` | Words to leave out (web-search syntax) |

## Tests

```bash
dotnet test
```

The tests run the real mapper and extractor over `tests/fixtures/sample-search.json`,
including traps like "react to changing priorities" and "the Spring semester" that must not
become tech tags.

## Project layout

```
src/FedTechSearch.Core     taxonomy, rule extractor, hybrid enricher (no dependencies)
src/FedTechSearch.Data     EF Core + Postgres, USAJOBS client, LLM client, ingest, search
src/FedTechSearch.Ingest   worker: scheduled pulls, --once, --fixture
src/FedTechSearch.Api      minimal API
web/                       Angular UI (filters live in the URL, so searches are shareable)
```

## Notes

- The schema is created on startup with `EnsureCreated`. Switch to EF migrations before
  changing the schema on a database with real data.
- USAJOBS asks API users to identify themselves with the email the key was issued to.
  Keep pulls to a few times a day.
