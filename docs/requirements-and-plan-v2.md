# Microsoft Relationship Map — Requirements & Development Plan, v2

Supersedes the planning in `MicrosoftProject.docx` and the first backend plan. Development starts
over from the UI mockup; the mockup itself is kept and is the reference for how the product looks
and behaves.

Conventions carried over: **FR** functional, **NFR** non-functional, **R** role, **UI** screen,
**TEC** technical decision, **Q** open question. Priority **M** must (v1), **S** should, **C** could.
Numbering continues from the original document, so nothing is renumbered.

---

## 1. What changed since the first specification

Six things, in order of how much they change the build:

1. **Editing is open.** Everyone with the link may add and remove things, with no approval step.
   This removes the approval workflow that the first plan treated as central, and replaces it with
   backup, history and undo as the safety net.
2. **There is more than one dashboard.** One per department. Dynamics is the first addition, and
   dashboards must be creatable at runtime — not a code change per department.
3. **A person can belong to several dashboards.** One shared relationship score, shown on each
   board they appear on.
4. **The graphics page is per dashboard, and it is clickable.** Clicking a category lists the
   specific people in it with phone number and e-mail address.
5. **Multi-country is the plan.** Denmark is built, tested and perfected first; the finished
   product is then rolled out to the next countries as its own containerised instance per
   country, each with its own database and its own URL.
6. **The importance 0–10 scale is dropped.** The relationship scale is −3…+3 and nothing else.
7. **Relationships are kept alive deliberately.** Every Microsoft person gets an owner, an optional
   monthly reminder, a log of when anyone last reached out, and the customers they cover — and
   **surveys are removed in full**, because the reminder loop does that job closer to the moment the
   information changes.

Already reflected in the mockup on `UI-mockup-v2`: items 2, 3, 4, 6 and 7, plus a Dashboards admin
page and empty phone/e-mail fields. Items 1 and 5 are backend work and are not mocked.

---

## 2. Decided: open editing means open internally

**Q-09 — resolved.** "Everyone with the link can edit" means **everyone at Columbus**: Entra ID
sign-in stays, and every signed-in colleague can add, edit and remove without an approval step.
It does not mean an unauthenticated link.

So, concretely:

| | |
|---|---|
| Sign-in | Entra ID, Columbus accounts only — NFR-02 stands |
| Who can edit | every signed-in colleague, no approval, no gatekeeping |
| What replaces approval | history, one-click undo, recycle bin, backups (§3.4, Stage 2) |
| Every change | carries the name of who made it and when |

Two consequences worth keeping in view:

- **The approval workflow is out** (FR-07, removed in §3.5). It was the centre of the first plan and
  is now replaced by the ability to undo. That is a deliberate trade: friction before the change,
  swapped for reversibility after it.
- **The role model collapses** from four roles to two plus an owner (§3.11). If everyone can edit,
  Moderator and Standard no longer describe different permissions.

Because sign-in is kept, the personal data in the system — named Microsoft employees, judgements
about them, and their contact details once FR-29 lands — stays inside Columbus with an author
against every change. That is what makes NFR-08 (retention, lawful basis, data subject requests)
achievable rather than theoretical.

## 3. Revised requirements

### 3.1 Changed

| ID | Status | What changes |
|---|---|---|
| **FR-07** | **removed** | Pending submissions and approval are gone, not deferred — see §3.5. Open editing plus undo replaces them. |
| **NFR-02** | revised | Entra ID authentication, restricted to Columbus accounts, stands. It no longer gates *editing* — it gates access and gives every change an author. |
| **R-01…R-04** | revised | See §4. Four roles collapse to two plus an owner. |
| **FR-01** | clarified | Score is a small integer with `CHECK (score BETWEEN -3 AND 3)`. The seven written descriptions live in one place and are editable without deployment. No second scale — the 0–10 importance column from the stakeholder deck is not imported. |
| **FR-08** | clarified | Mean is the headline number; median and sum sit behind it. Per person and per domain, per dashboard. |

### 3.2 New — dashboards

- **FR-21 (M)** The system shall support several dashboards, one per department, each with its own
  board and its own graphics page.
- **FR-22 (M)** Dashboards shall be created, renamed and deleted at runtime by an Admin, without a
  deployment. Creating one shall create its pages and menu entries with it.
- **FR-23 (M)** A dashboard's panels shall be derived from the Microsoft domains assigned to it, so
  that adding a domain adds a panel without further configuration. The board holds **eight** panels;
  domains beyond the eighth shall still be counted and listed, and the surplus shall be visible to
  an Admin rather than silently dropped.
- **FR-24 (M)** A Microsoft person shall be able to belong to several dashboards, through belonging
  to domains owned by different dashboards. The relationship score is one score, shared by every
  board the person appears on.
- **FR-25 (M)** Microsoft Profiles shall be filterable by dashboard, and shall show which
  dashboards each person appears on.
- **FR-26 (S)** A dashboard shall only be deletable when it holds no domains, and the landing
  dashboard shall not be deletable at all, so that no profile or relationship can be orphaned.

### 3.3 New — graphics and contact details

- **FR-27 (M)** Each dashboard shall have its own graphics page, counting only that dashboard's
  profiles and relationships.
- **FR-28 (M)** Clicking a category in a chart shall list, directly below the chart, the specific
  people it counts — name, title, relationship score, **phone number** and **e-mail address**.
- **FR-29 (M)** A Microsoft profile shall carry a phone number and an e-mail address. Both may be
  empty, and the system shall never generate, guess or infer them. *(Q-10: where does this data
  come from?)*
- **FR-30 (S)** Export of a dashboard, its graphics and the underlying list, to Excel/CSV and to
  SVG/PDF, per dashboard. *(replaces FR-11 with a per-dashboard scope)*

### 3.4 New — keeping the relationships alive

A score says how good a relationship is. It says nothing about whether anyone has spoken to the
person this year, and that is the half that decays quietly.

- **FR-43 (M)** Each Microsoft person shall have one **relationship owner**: a named Columbus
  person, responsible for reaching out. The owner shall be somebody who already holds a relation to
  that person. A Microsoft person nobody has a relation to therefore has no owner and no reminder,
  and shall be visible as such rather than hidden.
- **FR-44 (M)** The system shall keep a **contact log**: one entry per time somebody reached out,
  recording the date and who registered it. Any signed-in user may add an entry, not only the
  owner — it is a fact about the relationship, not the owner's property. Entries are not deleted by
  the passing of a month; nothing resets.
- **FR-45 (M)** The date of the most recent contact shall be shown on **every** Microsoft person,
  whether or not they carry a reminder. Where the log is empty it shall read **Unknown** — an empty
  log means nobody wrote it down, not that nobody called.
- **FR-46 (M)** Each Microsoft person shall carry a **reminder cadence**: none, monthly, quarterly,
  half-yearly or yearly, set per person. **None is the default.** Only a person with both an owner
  and a cadence enters the reminder loop; everybody else is still tracked under FR-45.
- **FR-47 (M)** On the **first Monday of each month** the system shall send **one mail per owner**,
  listing that owner's people who are overdue or fall due within the coming ten days, with a link
  straight to them. An owner with nothing due gets no mail.
- **FR-48 (M)** A **Relationship Upkeep** page shall show the upkeep of **everyone's**
  relationships, not only the signed-in user's, so that a colleague can pick up a contact that is
  slipping. It shall show owner, cadence, last contact and derived status, be sortable by any
  column in an order the user chooses, and allow a contact to be registered from the row.
- **FR-49 (M)** A Microsoft person shall carry the **customers they cover**, held as records of
  their own and linked many-to-many, not as free text — so that "who at Microsoft touches this
  customer?" is answerable and the names stay searchable. Changing the customers on a person shall
  be a routine edit.
- **FR-50 (S)** A customer shall carry a type — Columbus customer, prospect, or unknown.
- **FR-51 (S)** The upkeep shall also have a **graphics page**, showing the spread of statuses, how
  much each owner is carrying, contacts logged per month, and the share still on track per
  dashboard, scopeable to one dashboard. The table answers who to call today; the graphics answer
  whether anyone is calling at all.
- **FR-52 (S)** Every chart on that page shall drill the way the board graphics do (FR-28): clicking
  a column or bar lists the people behind it directly underneath, and a contact shall be
  registrable from that list — so a chart is somewhere work starts, not only somewhere it is
  counted.

**Superseded by the above:** FR-37 (a passive "last confirmed" date) is absorbed into FR-45, which
is the same need made active and therefore better.

### 3.5 Removed — surveys and approval

Surveys are **removed in full**, not deferred. Open editing plus the monthly reminder loop does the
job a quarterly survey was there to do, and does it closer to the moment the information changes.

| ID | What goes |
|---|---|
| **FR-06** | Sending out surveys that map relationships |
| **FR-07** | Pending submissions and the approval step (already dropped to *could* by open editing; now gone) |
| **FR-15** | Scheduling of surveys |
| **FR-42** | Surveys as a reminder to edit — replaced by FR-47 |

This also removes the Pending Submissions screen, the survey buttons and cadence card on Columbus
Profiles, the "last survey" column, and the submit-for-approval step on My Relations, which now
saves straight through.

### 3.6 New — open editing, and what makes it safe

- **FR-31 (M)** Any signed-in user shall be able to add, edit and remove Microsoft profiles,
  domains, people and relationships, without an approval step.
- **FR-32 (M)** Every change shall be recorded append-only, with the acting user, the timestamp,
  and the value before and after.
- **FR-33 (M)** Any single recorded change shall be revertible from the interface by an Admin —
  one-click undo of one change, not only a full restore.
- **FR-34 (M)** The system shall take automatic backups on a schedule and allow an Admin to restore
  the data set to an earlier point in time. *(NFR-06 sets the numbers.)*
- **FR-35 (M)** Deletion shall be soft: deleted profiles, domains and relationships shall be
  recoverable for a defined period through a recycle bin, and only then purged.
- **FR-36 (S)** A change feed shall show what changed recently, and by whom, across the data set.
  Open editing is only safe if it is observable.
- ~~**FR-37 (S)**~~ *Superseded.* The passive "last confirmed" date is replaced by the contact log
  and last-contact date in FR-44 and FR-45 — the same need, made active.

### 3.7 New — countries

The country boundary is the **deployment**: one instance of the application per country, each with
its own database, its own URL and its own release. A country's instance holds only that country's
data, so there is nothing to scope inside the application.

- **FR-38 (M)** Each deployed instance shall serve exactly one country, and shall be configured
  with that country's name for display in the interface and in exports.
- **FR-39 (M)** An instance shall have no route to another country's data: separate database,
  separate credentials, separate Entra app registration and sign-in URL. Isolation is a property of
  the deployment, not a filter in the code.
- **FR-40 (C)** A cross-country roll-up for management, built from exports out of each country's
  instance rather than from a shared database.

### 3.8 New — getting data in

- **FR-41 (S)** It shall be possible to import a list of Microsoft people from a file or a paste,
  with a preview that shows which rows match existing people (FR-19) before anything is written.
  Both departments mapped so far arrived as a slide deck; the next one will too.
- ~~**FR-42 (S)**~~ *Removed with the rest of surveys — see §3.5.* The monthly reminder in FR-47 is
  what now brings people back to their relations.

### 3.9 Non-functional

- **NFR-05 (M)** Each country's instance shall be independently deployable, upgradeable and
  restorable, without a release to one country requiring a release to another.
- **NFR-06 (M)** Backups: at least daily, retained 30 days, restorable to any point within the last
  7 days. A restore shall be exercised at least once before go-live — an untested backup is not a
  backup.
- **NFR-07 (M)** The change history shall be complete enough to answer "who changed this, when, and
  what did it say before" for any field, without reading application logs.
- **NFR-08 (M)** Retention and GDPR: relationship data for a Microsoft person who has left shall be
  purged after a defined period; the lawful basis and the handling of a data subject request shall
  be documented before real data enters the system. There shall be no free-text field about a person
  beyond the structured note already specified.
- **NFR-09 (S)** The interface shall stay usable at roughly 2,000 Microsoft profiles in an instance.
  Statistics are computed client-side; past that, they move to the server.

### 3.10 Technical decisions

| ID | Decision |
|---|---|
| TEC-01 | Postgres. Unchanged. |
| TEC-02 | Azure hosted. Unchanged. |
| **TEC-03** | Framework: **open — decide in Stage 0.** Pick by who maintains it: ASP.NET Core if the maintainers are C# developers, Next.js/TypeScript if the mockup's own stack is the team's, FastAPI if the team is Python-first. All three are adequate; the wrong answer is the one nobody on the team can read. |
| **TEC-04 (M)** | **One containerised instance per country, hosted for that country, on its own URL** (`dk.` / `no.` / `us.`), each with its own database. Denmark is built, tested and perfected first; the finished image is what the next country is rolled out from. |
| **TEC-04a (M)** | Nothing in the application is country-aware. There is no country column, no country filter and no country switcher: an instance is one country's system, and the country's name is configuration. This keeps the code the same everywhere, so what Denmark has tested is exactly what the next country gets. |
| **TEC-05 (M)** | Containerise the application as a single image, promoted through dev/test/prod and then reused per country. Standing up a country means: provision its database, deploy the image, register its hostname in Entra, point DNS at it, and add it to the backup and monitoring schedules. That is the per-country setup, and it is what gives each country its own tested copy on its own release. |
| **TEC-06 (M)** | CI/CD from the first stage: build, test and deploy on every merge, infrastructure as code, no secrets in the repository. The first iteration reached a working API with no deployment path; that gap is closed early this time. The pipeline and the infrastructure definition take the target country as a parameter, so the second country is a run of the same pipeline rather than a hand-built environment. |
| **TEC-07 (M)** | Dashboards, domains, the score scale and the role of each user are **data, not code**. This is the measurable form of the old "agile backend" (NFR-01): those changes need no migration and no deployment. Adding a *field* to a profile does need both, and that is accepted. |

### 3.11 Roles, revised

Open editing makes a four-role model mostly decorative — if everyone can edit, Standard and
Moderator no longer describe different permissions.

| ID | Role | Can |
|---|---|---|
| **R-01** | Super Admin | Everything an Admin can, plus transferring this role. Cannot be deleted or edited by others. |
| **R-02** | Admin | Manage dashboards, domains and users; restore backups; undo any change; purge the recycle bin. |
| **R-03** | Editor *(default)* | Everything else: add, edit and remove profiles, domains' contents and relationships — their own and other people's. |
| ~~R-04~~ | *(Moderator, Standard)* | Dropped. A read-only role can return later as an explicit `Viewer` if a real need appears. |

Roles are assigned in Entra ID app roles so that a leaver loses access without a second system to
maintain. Everyone not explicitly assigned is an Editor.

---

## 4. Development stages

### 4.1 How this work is cut, and why

There are two ways to slice it, and it is worth being explicit about which one this plan uses.

**By layer** — a backend stage, then a frontend stage, then features on top. This is how the first
iteration was planned, and the result is instructive: 40 commits, 8 migrations and 143 passing tests
that produced a working API and *nothing a stakeholder could click*. The frontend stage never
started. The layers were identified correctly; the sequencing meant there was nothing to show or
correct for weeks, and no feedback until the most expensive decisions were already made.

**By capability** — each stage crosses both layers and ends with something that runs. This is what
the plan below uses, for one reason: relationship mapping is a product where the interface *is* the
requirement. The board is the deliverable. A stage that cannot be shown cannot be validated.

But cutting by capability does not make the layers disappear, and a plan that hides them cannot be
staffed or estimated. So each stage below names its **backend**, **frontend** and **infra/ops** work
separately. Read the plan down the columns and the traditional stages are still there —
Stage 1 is the backend foundation, Stage 3 is the frontend foundation — they are just placed so
that something is demonstrable at the end of each.

One hard ordering rule: **nothing that writes data ships before the thing that can undo it.**

**Where this stands today.** The mockup on `UI-mockup-v2` now carries every agreed feature — two
dashboards plus a self-service third, the graphics and their drill-downs, open editing, the upkeep
loop, the customers, and no surveys. Nothing in the plan below is waiting on another mockup round.
What is waiting is Stage 0: four decisions, and a frozen reference (§4.3).

### 4.2 The stages at a glance

| # | Stage | Backend | Frontend | Infra & ops |
|---|---|---|---|---|
| 0 | Decide & set up | Health endpoint, Entra token validation | Sign-in screen, nothing behind it | **Most of the stage:** repo, environments, Postgres, CI/CD, IaC |
| 1 | Data model & history | **Most of the stage:** schema (incl. owner, contact log, customers), migrations, identity/merge, history, CRUD API | — | Migration runner in the pipeline |
| 2 | Backup, undo, recycle bin | Undo endpoint, soft delete, restore tooling | Small admin screens: history list, recycle bin | Backup schedule, retention, rehearsed restore |
| 3 | Shell & dashboard registry | Dashboards CRUD, menu/permissions endpoint | **Most of the stage:** app shell, verbatim CSS lift, routing, MSAL, `scoring.ts` | Static hosting, cache headers |
| 4 | Administration over live data | Search, filters, contact fields, owner + cadence, contact log, customers, import + dedup | Microsoft Profiles, Domains, Columbus people, Dashboards, Relationship Upkeep table | — |
| 5 | The boards | Read model per dashboard, score write path | Board port: ring, panels, dots, filters, SVG export, print | — |
| 6 | Graphics, drill-downs & the reminder | Aggregates, people-behind-a-bar endpoints, the monthly digest job | Board graphics, upkeep graphics, all drill-downs, change feed, export | Scheduled job + outbound mail |
| 7 | Roll out to the next country | Country name as configuration | — | **Most of the stage:** provision the country's database and instance from the tested image, hostname, DNS, backups, monitoring |

**Where the two tracks run in parallel.** Stage 1 is backend-only and Stage 3's frontend work has no
backend dependency — the mockup already defines the markup, the stylesheet and the arithmetic. So a
frontend developer starts Stage 3 while a backend developer is in Stage 1, and they meet in Stage 4,
which is the first stage that genuinely needs both. Stage 2 also overlaps Stage 3. Stages 5 and 6
are frontend-heavy with thin backend work; Stage 7 is the reverse.

### 4.3 Stage detail

#### Stage 0 — Decide and set up *(days, not weeks)*

Three things, in this order:

**1. Freeze the mockup as the reference.** Tag the commit on `UI-mockup-v2` that everyone agrees is
the target, and treat later mockup changes as changes to the spec rather than as the spec. Without
that line the build aims at a moving picture, and "does it match the mockup?" stops being a question
anyone can answer.

**2. Take the four decisions that block a start:**

| | |
|---|---|
| **TEC-03** | The framework. Pick by who will maintain it, not by which is best (§3.10). |
| **Q-06** | Who owns this specification, and who owns the data. |
| **Q-13** | Who may restore a backup and undo other people's changes. |
| **Q-08** | Whether a Microsoft work e-mail is reliably available, since it decides the identity key. |

Q-10 (where contact data comes from) can wait for Stage 4, Q-14 (a read-only role) for Stage 3, and
Q-12 (the second country) for Stage 7 — none of them holds up a start.

**3. Stand up the plumbing:** repository, three environments, CI/CD, infrastructure as code,
Postgres, and a deployed page behind Entra sign-in.

**Exit:** a signed-in blank page on a real Azure URL, put there by a pipeline. Every later stage
deploys the same way, so deployment is never a separate project.

#### Stage 1 — The data model, with history from the first write

*The backend foundation.* Dashboards, domains, Microsoft profiles, Columbus people, relationships —
plus the three the upkeep round added: the **owner** on a profile with the rule that they must hold
a relation (FR-43), the **contact log** (FR-44), and **customers** as records of their own linked
many-to-many (FR-49). And the append-only history table (FR-32) written by the same code path as
every mutation, so no write can bypass it. Identity key, match-before-create and merge (FR-18–20) belong
here: cheap now, expensive once duplicates exist. The schema holds one country's data, because the
instance is one country's system (TEC-04) - so there is no country column to carry.

**Exit:** an API that holds the mockup's full data set, with every change attributable to a person
and a time. Verified by tests over a real Postgres, not a mock.

#### Stage 2 — Backup, undo and the recycle bin

Before anyone can edit, the net has to be under them: scheduled backups, point-in-time restore,
one-click undo of a single change, soft delete with a recycle bin (FR-33–35), and a restore actually
rehearsed (NFR-06).

**Exit:** a demonstration — delete something important and undo it; restore yesterday's data set
into a test environment.

*This is where the first plan had the approval workflow. Open editing does not remove the need for a
safety net; it moves it from before the change to after it.*

#### Stage 3 — The application shell and the dashboard registry

*The frontend foundation.* The mockup becomes an application: its stylesheet and class names ship
as they are, plus routing, Entra sign-in, and the left menu generated from the dashboards in the
database. The Dashboards admin page is part of this stage — it is what makes a department
self-service.

**Exit:** a colleague signs in, creates a dashboard, and sees its two empty pages appear in the menu.

*Constraint carried over from the mockup: the SVG export builds itself by reading the page's own
stylesheet and matching class names. Any tooling that hashes or scopes class names — CSS Modules,
styled-components, Tailwind's rewrite — breaks the export at runtime with no build error. The
stylesheet ships global and unhashed, and class names are a contract.*

#### Stage 4 — Administration over live data

Microsoft Profiles with search, filters and the dashboard filter (FR-25); Microsoft Domains;
Columbus people; contact details (FR-29); owner, cadence and the contact log (FR-43–46); the
customers each person covers (FR-49–50); import with dedup preview (FR-41). The first stage that
needs both tracks, and the stage where the app becomes more useful than the spreadsheet it replaces.

**Exit:** the Dynamics data set is entered, imported or merged *through the interface* — not seeded
by a script — and the six `(to confirm)` items from the deck are resolved by a human in the app.

#### Stage 5 — The boards

The hub-and-spoke board per dashboard, fed from the database: derived panels (FR-23), the
eight-panel ceiling, scoring straight from a dot, the filters, SVG export and print.

**Exit:** the board a stakeholder sees in a meeting is rendering live data.

#### Stage 6 — The graphics, the drill-down and the upkeep loop

KPIs, breakdowns and distribution per dashboard (FR-27), the click-through to the people behind a
bar with phone and e-mail (FR-28), the change feed (FR-36), per-dashboard export (FR-30) — and the
upkeep half: the Relationship Upkeep page (FR-48), its graphics (FR-51) and the monthly digest mail (FR-47), which is the
only scheduled job in the product now that surveys are gone.

**Exit:** "who do we not know in this domain, how do I reach the ones we do, and who has nobody
spoken to since the spring" are all answered in two clicks — and the first Monday mail goes out.

#### Stage 7 — Roll out to the next country

Denmark is finished, in use and corrected by real use before this stage begins - that is the point
of the order. Then the tested image is deployed again for the next country: its own database, its
own hostname registered in Entra, its own DNS record, its own backup and monitoring schedule
(TEC-04, TEC-05). The country's own people create their own dashboards and domains from the
interface, the way Denmark did.

Nothing in the application changes for this stage. If something does have to change, that is the
signal that it belonged in Stages 1-6 and should be fixed in Denmark first, so every country keeps
running the same tested product.

**Exit:** a second country live on its own URL, from the same image as Denmark, with its own data
and its own release it can be upgraded on independently.

**There is no survey stage.** Surveys were removed in full (§3.5). What keeps the data fresh is open
editing plus the monthly reminder loop, which asks a named person about a named relationship at the
moment it is slipping, rather than asking everybody about everything once a quarter.

---

### 4.4 Which screen specifies which stage

The mockup is the specification for everything visible, so each stage has a page to build against
and to be judged by. This is the list to check a stage off with: *does it look and behave like this
screen, with real data behind it?*

| Stage | Build against | The part that is easy to get wrong |
|---|---|---|
| 1 | The seed data in `index.html` — its shape *is* the schema | Identity, match-before-create and merge, and a history row for every write |
| 2 | — nothing visible; it is the net under the rest | A restore that has actually been rehearsed, not just configured |
| 3 | The shell: left menu, role switcher, login | The stylesheet ships global and unhashed, or Export SVG breaks silently |
| 4 | Microsoft Profiles, Microsoft Domains, Columbus Profiles, Dashboards, Relationship Upkeep | The owner picker offering only people who hold a relation; the customer list staying a list, not a text field |
| 5 | Dashboard - Data & AI and Dashboard - Dynamics | Panels grow, so the layout settles after render; the eight-panel ceiling; the board fits the screen |
| 6 | Graphics - Data & AI, Graphics - Dynamics, Graphics - Upkeep | Every chart drills to the people behind it, and the drill-down can register a contact |
| 7 | The same product, second instance | Nothing in the application changes; if it does, it belonged in 1-6 |

Three behaviours in the mockup are decisions rather than decoration, and are easy to lose in
translation:

- **"Unknown", never "Never".** An empty contact log means nobody wrote it down, not that nobody
  called. The same reasoning applies to an empty phone number and an unscored relationship.
- **Somebody with no relation has no owner and gets no mail**, but still appears everywhere else.
  The gap is the finding; hiding it would defeat the page.
- **Panels and charts are places work starts.** A strength dot is scored from the board, a contact
  is registered from inside a chart's drill-down. If the build turns those into read-only displays
  with editing somewhere else, it will be correct and nobody will use it.

---

## 5. Open questions

| ID | Question | Blocks |
|---|---|---|
| **Q-10** | Where do phone numbers and e-mail addresses come from, and who is allowed to see them? (FR-29) | Stage 4 |
| **Q-12** | Which country is second, and when — and where is each country's instance hosted? (TEC-04) | Stage 7 |
| Q-13 | Who may restore a backup and undo other people's changes — Admin only, or a named few? (FR-33–34) | Stage 2 |
| Q-14 | Is a read-only `Viewer` role needed for anyone outside the editing group? (R-04) | Stage 3 |
| Q-08 | Is a work e-mail reliably available for Microsoft contacts, or is name + organisation the real identity key? | Stage 1 |
| Q-06 | Who owns this specification, and who owns the data? | now |

**Settled, for the record:** Q-11 — surveys are removed in full, replaced by the owner and the
monthly reminder loop (§3.4, §3.5). The country model — one containerised instance per country, each with
its own database and URL, Denmark built and perfected first and the finished image rolled out from
there (TEC-04). Q-09 — open editing means open internally, behind Entra sign-in (§2).
Q-01 — the score is a small integer per relationship, summed and averaged on read, not vector data.
Q-07 — GDPR handling is now NFR-08 and inside the scope of v1. Q-02 — "agile backend" is now
TEC-07: dashboards, domains, the score scale and roles change without a migration; adding a field
does not.

---

## 6. What to keep from the first iteration

The code on `first_iteration` is not thrown away — it is read. Four things in it are worth more than
the time it would take to rediscover them:

1. **The schema shape** — taxonomies as rows, snake_case naming, the relation/history split.
2. **Identity, match and merge** — the hard part of FR-18–20, with tests over a real Postgres.
3. **The append-only history**, enforced structurally rather than by convention.
4. **The Entra integration**, including the domain gate.

What does *not* carry over: the approval workflow (superseded by §3.4), the closed membership list
(superseded by open editing), and the assumption of a single dashboard.

Two loose ends from that branch that still need action regardless of the restart:

- `.env` containing `POSTGRES_PASSWORD` and `PGADMIN_PASSWORD` is in git history on the `UI-mockup`
  branch. **Rotate those passwords**, whether or not the history is rewritten.
- There was no CI and no deployment path. TEC-06 closes that in Stage 0.
