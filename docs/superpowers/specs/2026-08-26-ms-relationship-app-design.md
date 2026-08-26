# Microsoft Relationship Map — Design

**Date:** 2026-08-26
**Status:** Draft for review
**Supersedes on conflict:** nothing. `index.html` remains the visual authority.

---

## 1. Purpose

Build the production application described by `Requirements.pdf`, reproducing the
approved mockup `index.html` exactly, on Postgres + ASP.NET Core (.NET 9) + React/TypeScript,
hosted on Azure.

`index.html` is fully up to date and outranks `Requirements.pdf` wherever the two disagree.
Where the mockup has settled a question the document still lists as open, the mockup wins and
the document is what needs revising.

## 2. Decisions carried in from brainstorming

| # | Decision |
|---|---|
| D-1 | Target is the real app (TEC-01..03), not a further evolution of the mockup. |
| D-2 | Mean is the headline score; median and sum both appear in the hover. FR-08 is already satisfied — see `scoreStat` (`index.html:1037`). |
| D-3 | v1 scope beyond the must-haves: archive-instead-of-delete, and Excel/CSV export. |
| D-4 | Entra ID authenticates, but membership is a **closed list** — an account absent from `columbus_users` is refused. |
| D-5 | Frontend fidelity by construction: the mockup's stylesheet ships verbatim, components emit identical class names and DOM. |
| D-6 | Versioning is an **append-only history table**, not bitemporal rows and not named snapshots. |

D-5 and D-6 were my recommendations, adopted without an explicit yes. Both are called out in
§13 (OD-1, OD-2) so they can be reversed cheaply.

## 3. Why D-5 is a constraint and not a preference

`ecoCss()` builds the SVG export by scraping `document.styleSheets` at runtime and
regex-matching selector *text* for `.eco|.pcard|.pdot|.pav|.pname|.prole|.pmeta|.pmid|.ptip|.ppick`
(`index.html:1596`). Any tooling that hashes or scopes class names — CSS Modules,
styled-components, Tailwind's utility rewrite — silently breaks Export SVG at runtime, with no
build error. The stylesheet therefore ships global and unhashed, and class names are a contract.

The dashboard is frozen. Its imperative code (`ecoDrawLinks`, `ecoFit`, `ecoSvgString`,
`ecoSlotStyle`) ports across essentially verbatim inside one component driven by a ref, rather
than being reinterpreted as declarative React.

## 4. Architecture

```
┌─────────────────────────────┐     ┌──────────────────────────────┐
│ React 19 + TS (Vite)        │────▶│ ASP.NET Core 9 Web API       │
│ MSAL browser auth           │ JWT │ JWT bearer (Entra)           │
│ global app.css (verbatim)   │     │ EF Core 9                    │
│ scoring.ts (ported math)    │     └──────────────┬───────────────┘
└─────────────────────────────┘                    │
                                                   ▼
                                       ┌──────────────────────┐
                                       │ Postgres 16          │
                                       └──────────────────────┘
                                       Mailpit (dev) / ACS (prod)
```

**The API returns raw entities; the frontend derives every statistic.** `meanOf`, `medianOf`,
`msStats`, `cbStats`, `domainStats`, `bandOf` port verbatim from the mockup into
`src/lib/scoring.ts`. This is deliberate: it is the cheapest guarantee that the production
numbers match the approved mockup exactly, because it is the same arithmetic on the same shapes.
It is sound at this scale (23 Microsoft profiles, ~10 Columbus users, low hundreds of relations).
Revisit if profiles exceed ~2,000 (OD-4).

**Local dev** follows the conventions already recorded in `.env`: API on `:5080`, Vite frontend
with `VITE_USE_MSW=true` for a mock service worker, and `docker compose` providing Postgres,
Mailpit and (behind `--profile tools`) pgAdmin.

## 5. Data model

Taxonomies are rows, not enums or schema — this is what satisfies NFR-01. Adding a domain,
group, source or department is an insert, requiring neither migration nor redeployment.

```
columbus_users        ms_profiles              ms_domains
──────────────        ───────────              ──────────
id                    id                       id
entra_object_id       name                     name
email (unique)        title                    owner
name                  email                    description
title                 organization             is_system
department_id ────┐   identity_key (computed)  sort_order
skills text[]     │   group_id ──────┐
role              │   source_id ─────┼──┐      ms_profile_domains
status            │   notes          │  │      ───────────────────
archived_at       │   is_tentative   │  │      ms_profile_id
last_survey_at    │   merged_into_id │  │      domain_id
                  │                  │  │
     cb_departments◀┘      ms_groups◀┘  └▶ms_sources

relations                     relation_history (append-only)
─────────                     ────────────────
id                            id
columbus_user_id              columbus_user_id
ms_profile_id                 ms_profile_id
score  (-3..3)                old_score / new_score
note                          old_note  / new_note
updated_at                    change_type
UNIQUE(columbus_user_id,      changed_by_user_id
       ms_profile_id)         changed_at
                              submission_id (nullable)

submissions                   submission_items         survey_schedule (singleton)
───────────                   ────────────────         ───────────────
id                            id                       enabled
columbus_user_id              submission_id            cadence
submitted_at                  ms_profile_id            last_sent_at
status                        action (upsert|remove)
decided_by_user_id            new_score / new_note
decided_at                    prev_score / prev_note
```

`relations.score` carries a `CHECK (score BETWEEN -3 AND 3)`. The seven written descriptions
(FR-01) live in the frontend `SCORE_LEVELS` constant, ported verbatim from `index.html:721` —
they are presentation, not data, and duplicating them server-side invites drift.

## 6. Identity, deduplication, merge (FR-18, FR-19, FR-20)

**FR-18 — canonical identity key.** `ms_profiles.identity_key` is a stored generated column:

- `lower(trim(email))` when email is present
- otherwise `lower(trim(name)) || '|' || lower(trim(coalesce(organization,'')))`

`organization` does not exist in the mockup and is new. It defaults to `'Microsoft'`, which is
correct for all 23 seeded profiles and keeps the name+org fallback meaningful.

A partial unique index enforces uniqueness over live records only:
`CREATE UNIQUE INDEX ... ON ms_profiles (identity_key) WHERE merged_into_id IS NULL`.

**FR-19 — match before create.** `POST /api/ms-profiles/match` takes a candidate name/email/org
and returns ranked possible matches: exact `identity_key` hit first, then case-insensitive email,
then trigram similarity on name (`pg_trgm`) above 0.4. The create endpoint refuses an exact
`identity_key` collision with `409 Conflict` and the colliding record in the body, so the UI can
offer it instead of creating a duplicate. This applies to Admin creation and to any survey-driven
creation path.

**FR-20 — merge.** `POST /api/ms-profiles/{survivorId}/merge` with `{ "mergeIds": [...] }`, in one
transaction:

1. Re-point `relations`, `relation_history` and `submission_items` at the survivor.
2. On a relation collision — the same Columbus user related to both records — **keep the higher
   score**, and write a `relation_history` row of type `merge_discarded` recording the losing
   value, so nothing is silently dropped.
3. Union `ms_profile_domains`.
4. Fill blank survivor fields from the merged record; never overwrite a non-blank survivor field.
5. Set `merged_into_id` on the losers. Rows are **tombstoned, never deleted**, so stale links and
   history references still resolve.

## 7. Versioning and history (FR-09)

`relation_history` is append-only. Every score or note change writes one row: who changed it,
when, old → new, and the approving submission where one exists. `change_type` is one of
`created | updated | removed | merge_moved | merge_discarded | user_archived`.

This satisfies FR-09, gives FR-20 the history it must move, and is the substrate any later
GDPR/audit requirement (Q-07) would build on. It does **not** give "the map as of last quarter" —
that is FR-16 (snapshots), out of scope for v1, and reachable later by replaying history.

**Process owner.** FR-09's "defined process owner" is role-based. `ms_domains.owner` already
holds it per domain. The board-level `owner:'owner not yet assigned'` string (`index.html:1305`)
becomes a configured value — but the dashboard is frozen, so this is a config substitution at the
export/render boundary, not a layout change.

## 8. Lifecycle

**Leavers.** `deleteColumbusUser` currently hard-deletes the user, their relations *and* their
submissions (`index.html:2497`), while Graphic View carries a KPI reading "Held by one person —
lost if that person leaves" (`index.html:1904`). The app measures the risk its own delete button
causes. Replaced by:

- `POST /api/columbus-users/{id}/archive` sets `status='archived'`, `archived_at=now()`, and
  writes `user_archived` history rows. **Relations are retained.**
- Archived users are hidden from Columbus Profiles by default behind an "Include archived" toggle.
- Their relations remain visible in the Microsoft profile drawer, labelled *former employee*.
- Archived relations are **excluded from coverage** — coverage means a live connection — but
  **included in mean, median and sum**, which describe the relationship as last known.

That split is a judgement call and is recorded as OD-3.

**Super Admin transfer (R-01).** `POST /api/columbus-users/{id}/promote-super-admin`, callable by
the current Super Admin only, atomically demotes the caller to Admin and promotes the target.
Separately, the role dropdown must stop offering Super Admin as an ordinary assignment — today an
Admin can promote anyone to it (`index.html:2157`).

## 9. AuthN / AuthZ

MSAL in the browser, JWT bearer validation in the API against the Columbus tenant. Two gates on
every request, both server-side:

1. Email domain must match `AZUREAD__ALLOWEDEMAILDOMAINS` (`columbusglobal.com`). Empty means
   allow-any and is honoured in Development only — the convention already recorded in `.env`.
2. The `oid` claim must resolve to a live `columbus_users` row. No row, or `status='archived'`,
   means `403` (D-4, closed list).

Roles map to policies: `CanEdit` = Admin | SuperAdmin; `CanAdminister` = Admin | SuperAdmin;
Moderator gets read plus its own My Relations; Standard gets My Relations only. **The mockup's
role switcher is a demo control and is not built.**

## 10. Surveys

Mailpit in `.env` establishes that surveys are emailed, not merely in-app. A survey send writes
`last_survey_at` and dispatches a mail with a deep link to My Relations; the recipient
authenticates normally and edits there. There is no anonymous survey form — that would conflict
with D-4.

`survey_schedule` is a singleton row driving the cadence card (`index.html:2203`). Automatic
sending is a hosted background service that checks daily whether `last_sent_at + cadence` has
passed.

**Gap:** FR-19 says a Microsoft person may be created "via a survey", but My Relations only offers
a picker of existing profiles (`index.html:2841`), so no such path exists. Adding one means new UI
on a page the mockup has already approved. Recorded as OD-5, deferred.

## 11. Export (FR-11)

Server-side `GET /api/exports/ms-profiles.xlsx` and `/api/exports/columbus-profiles.xlsx` via
ClosedXML, honouring the caller's current filters as query parameters so the export matches what
is on screen. CSV via the same endpoints with `Accept: text/csv`.

FR-17 (a real date stamped on SVG/PDF exports, replacing the hardcoded `'August 2026'`) is **out
of v1 scope** by D-3.

## 12. Testing

- **Backend:** xUnit. Integration tests over a real Postgres via Testcontainers for anything
  touching identity, merge, archive or history — these are the requirements with the most ways to
  be subtly wrong, and they are not meaningfully testable against a mock.
- **Frontend:** Vitest over `src/lib/scoring.ts`, asserted against values computed from the
  mockup's own seed data, so a fidelity regression fails a test rather than being noticed by eye.
- **Fidelity:** one Playwright smoke test per page asserting the expected root class names are
  present — cheap insurance for the D-5 contract, since a renamed class breaks SVG export silently.

## 13. Open decisions

| # | Question | Current assumption |
|---|---|---|
| OD-1 | Approach A (verbatim stylesheet lift) — recommended but never explicitly approved. | Proceeding on A. |
| OD-2 | Versioning depth — deferred during brainstorming. | Append-only history (D-6). |
| OD-3 | Do an archived leaver's relations count toward mean/coverage? | In mean/median/sum; out of coverage. |
| OD-4 | Client-side statistics ceiling. | Fine to ~2,000 profiles; revisit beyond. |
| OD-5 | Can a survey introduce a new Microsoft person (FR-19)? | No path in v1; needs new UI. |
| OD-6 | `.env` is tracked in git with `POSTGRES_PASSWORD` / `PGADMIN_PASSWORD` (commit `015ba58`), as is `.DS_Store`. | Untrack going forward; history rewrite is the user's call. |
| OD-7 | Q-07 GDPR retention/audit — unaddressed in both documents. | Out of v1; history table is the substrate. |

## 14. Delivery decomposition

Too large for one plan. Five, each producing working, testable software:

| Plan | Scope |
|---|---|
| **1** | Backend foundation: docker compose, schema, taxonomies, identity/dedup/merge, archive, history, auth. Ships a working API. |
| 2 | Frontend shell: Vite, verbatim CSS lift, MSAL, routing, role guards, ported `scoring.ts`. |
| 3 | Admin pages: Microsoft Profiles, Microsoft Domains, Columbus Profiles. |
| 4 | Workflow: My Relations, Pending Submissions, survey schedule and mail. |
| 5 | Dashboards: frozen board port, Graphic View, Excel/CSV export. |

Plan 1 is written. Later plans are written after their predecessor lands, so that real API shapes
inform them rather than guesses.

## 15. Traceability

| Requirement | Where |
|---|---|
| FR-01 | `relations.score` + CHECK; `SCORE_LEVELS` verbatim (Plan 2) |
| FR-02 | `relations` unique pair, both directions many (Plan 1) |
| FR-03, FR-04 | CRUD (Plans 1, 3) |
| FR-05 | `ms_profile_domains` join; domains a separate table (Plan 1) |
| FR-06 | Mail + `last_survey_at` (Plan 4) |
| FR-07 | `submissions` / `submission_items` (Plans 1, 4) |
| FR-08 | Already satisfied by the mockup (D-2) |
| FR-09 | `relation_history` (Plan 1, §7) |
| FR-10 | Policies (Plan 1, §9) |
| FR-11 | ClosedXML exports (Plan 5) |
| FR-12, FR-13 | Already built in Graphic View; ported (Plan 5) |
| FR-14, FR-16, FR-17 | Out of v1 scope |
| FR-15 | `survey_schedule` + background service (Plan 4) |
| FR-18, FR-19, FR-20 | §6 (Plan 1) |
| NFR-01 | Taxonomies as rows (Plan 1, §5) |
| NFR-02 | §9 (Plan 1) |
| NFR-03 | D-5 verbatim lift (Plan 2) |
| R-01 | Transfer endpoint (Plan 1, §8) |
| R-02..R-04 | Policies (Plan 1, §9) |
| UI-00..UI-07 | Plans 2–5 |
| Q-01, Q-04, Q-05 | Answered by the mockup; `Requirements.pdf` needs revising |
| Q-07 | OD-7, out of v1 |
