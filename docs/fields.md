# Search fields

Every job has the structured fields USAJOBS provides, plus fields derived from the posting
text. Derived fields come from the rules in `RuleExtractor` and `taxonomy.json`, with an
optional LLM pass (see the README).

## Derived fields

| Field | Values | How it is derived |
|---|---|---|
| `roleFamily` | Software Engineering, DevOps & Cloud, Data Engineering & Analytics, Data Science & AI/ML, Cybersecurity, Network Engineering, Systems Administration, IT Support, Database Administration, Enterprise Architecture, IT Project & Product Management, IT Policy & Governance, Web & UX Design, Computer Engineering, General IT | Each family is scored: +3 if the posting's specialty code belongs to it, +3 if the series does, +4 per title pattern hit (parenthetical removed), +1 per distinct duty-text pattern hit (capped at 6). Highest score wins; below 3 is General IT. |
| `roleConfidence` | 0 to 1 | Winner's score divided by winner plus runner-up. Under 0.65, or a winning score under 5, counts as low confidence. |
| `workingTitle` | e.g. "Full Stack Software Developer" | 1) a "serves as a ..." sentence in the posting, 2) the LLM, 3) the family's default title with a level prefix (Senior, Lead, Principal, Supervisory). |
| `workingTitleSource` | posting, llm, rules | Where the working title came from. |
| `specialtyCodes` / `specialties` | APPSW / Applications Software, INFOSEC, SYSADMIN, NETWORK, DATAMGT, INET, ENTARCH, PLCYPLN, CUSTSPT, OS, ... | Parentheticals in the title (split on `/`), else "The official title of this position is ... (X)". Agencies invent their own codes; unknown ones are ignored. |
| `tech` | ~120 terms in 9 categories (Languages, Frameworks, Cloud, DevOps & Containers, Infrastructure, Data, AI & ML, Security, Practices) | Regex patterns per term over title, summary, duties and qualifications. Ambiguous words (React, REST, Helm, Rust, Go, Spring) use case-sensitive or context patterns. |
| `seniority` | Entry, Junior, Mid, Senior, Lead, Principal (a range) | GS/GG/GL/GM grades: 7 and below Entry, 9-10 Junior, 11-12 Mid, 13 Senior, 14 Lead, 15 Principal. A GS-11/12/13 ladder spans Mid to Senior. Other pay plans use salary bands. |
| `supervisory` | true/false | "Supervisory" in the title, or duties like "supervises a staff". |
| `workArrangement` | Remote, Telework eligible, On-site | USAJOBS RemoteIndicator, a "remote job" location, then TeleworkEligible. |
| `clearance` | None, Public Trust, Confidential, Secret, Top Secret, TS/SCI, DOE Q/L | USAJOBS SecurityClearance text, normalized; falls back to the posting text. |
| `directHire` | true/false | "Direct hire" anywhere in the posting. Direct hire skips the category rating process, so these move faster. |
| `mayCloseEarly` | true/false | Applicant-cap wording ("limited to the first 100 applicants", "may close early") in the summary, duties, requirements or How to Apply. Routine "by the cut-off/closing date" wording does not count. |
| `hiringPaths` (labels) | Open to the public, Veterans, Military spouses, Individuals with disabilities (Schedule A), Federal employees, Students, Recent graduates, National Guard and Reserves, ... | USAJOBS `HiringPath` codes mapped to plain labels (`HiringPath` in Core). The four `fed-*` codes share the "Federal employees" label. Unknown codes show as-is. |
| `scheduleType` | Full-time, Part-time, Shift work, Intermittent, Job sharing, Multiple schedules | `PositionSchedule[].Code`. The `Name` text is usually blank or free text, so only the code is used. |
| `appointmentType` | Permanent, Term, Temporary, Detail or temporary promotion, Internship or recent graduates, Multiple, Other | `PositionOfferingType[].Code`, grouped (seasonal, summer and intermittent count as Temporary). |
| `travelRequirement` | Not required, Occasional, 25% or less, 50% or less, 75% or less, 76% or more | `TravelCode`. |
| `promotionPotential` | 1 to 15 | `PromotionPotential`; values outside 1-15 (e.g. "00") are dropped. |
| `openToPublic` | true/false | HiringPath includes `public`. False means status candidates only (current or former federal employees, and similar). |

## Readable qualifications (`qualificationInfo`)

The qualifications text (`QualificationSummary`, about 510 words on average) is mostly OPM
boilerplate. `QualificationParser` (Core) turns it into a structured view stored as jsonb:
`parsed`, `whoMayApply`, `blocks[]` (one per grade: `grade`, `equivalentGrade`,
`experienceLabel`, `experienceDuties[]`, `education { requirement, fields[], alternatives[] }`),
`standardCompetencies[]` and `notes[]`. The original `qualifications` text is never changed.

**No paraphrasing.** Qualifications decide whether HR screens someone out, so every duty, field of
study and note must be a verbatim span of the source. The only edits allowed are trimming
whitespace, leading filler ("which includes", "Examples of qualifying experience…:"), trailing
punctuation and connectives, and capitalizing the first letter. `Verbatim.Contains` checks every
item against the text it came from (case-insensitive, whitespace collapsed); items that fail are
dropped and logged at ingest. The LLM is never used here.

- **Grades:** one block per grade heading ("To qualify at the GS-14 level", "For GS-12:"), else the
  grade in a section title ("GS-2210-11") or the posting's own grade.
- **Duties:** the list after the first specialized-experience intro, split on numbering, sentences
  (after a colon) and semicolons. A comma list is split only when every piece is a whole "-ing"
  phrase of two or more words, so "Designing, developing, and maintaining X" stays one duty.
- **Education:** the "OR Education:" path, or `Details.Education` when it holds a degree and a list
  of fields.
- **Competencies:** the four standard 2210 definitions, matched by heading or by two distinctive
  phrases, are shown as chips and excluded from duty extraction.
- **SES/ST postings:** numbered Technical Qualifications ("TQ 1: Title: text") are kept verbatim,
  and Executive Core Qualifications are matched by name or phrase and shown as chips.
- **Parsed** is true when at least one grade block has duties, or the posting has TQs; otherwise the UI shows the original
  text broken into paragraphs at section markers. About half of live postings parse.

Patterns live in `QualificationPatterns`; tune them there, not in the parser.

## Duty locations and radius search

Each job's `PositionLocation` entries that carry real coordinates are stored in `job_locations`
(name, town, state, country, latitude, longitude). Remote and "negotiable" placeholders and 0/0
coordinates are skipped. The town is the segment before the state, so "Pentagon, Arlington,
Virginia" is Arlington. Rows are replaced only when a job's location set changes
(`LocationsHash`).

A searched city is placed using our own data, with no geocoding service: the average
coordinates of stored locations with that town and state (with no state, the state with the
most postings). A radius search matches jobs with any duty location within the circle
(haversine distance, prefiltered by a lat/lon box), plus exact name matches as a fallback.
Cities with no stored coordinates fall back to exact name matching.

## Structured fields kept from USAJOBS

Official title, agency, department, sub-agency, locations, states, series, pay plan, low
and high grade, salary range, open and close dates, schedule, appointment type, hiring
paths, summary, duties, qualifications, announcement number, USAJOBS link.

## Adding or fixing a term

1. Edit `taxonomy.json` (patterns are .NET regexes, case-insensitive unless
   `"caseSensitive": true`).
2. Bump `"version"`. Stored jobs are re-enriched on the next ingest.
3. Add a test in `RuleExtractorTests` if the term is tricky.
