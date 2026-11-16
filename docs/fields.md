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

## Structured fields kept from USAJOBS

Official title, agency, department, sub-agency, locations, states, series, pay plan, low
and high grade, salary range, open and close dates, schedule, appointment type, hiring
paths, summary, duties, qualifications, announcement number, USAJOBS link.

## Adding or fixing a term

1. Edit `taxonomy.json` (patterns are .NET regexes, case-insensitive unless
   `"caseSensitive": true`).
2. Bump `"version"`. Stored jobs are re-enriched on the next ingest.
3. Add a test in `RuleExtractorTests` if the term is tricky.
