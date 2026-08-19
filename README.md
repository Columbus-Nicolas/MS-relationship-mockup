# Columbus &middot; Microsoft Relationship Map — UI mockup

A clickable UI mockup of the app that gives Columbus a shared, always-current overview of the
relationships to key people at Microsoft Denmark — where we have strong connections, where we are
missing them, and how that changes when roles and organisations shift on both sides.

This is a **presentation mockup**, not a product. There is no backend, no authentication and no
persistence. Everything you see is fictional.

## How to open it

Double-click `index.html`, or drag it into a browser. No install, no build step, no internet
connection needed.

On the login screen: press **Continue** (or the Entra ID button). Any e-mail — or none at all —
signs you in as *Mette Kirkegaard* with the **Admin** role.

## Demo path for a presentation

1. **Login** — placeholder, mention that the real thing is Microsoft Entra ID restricted to Columbus mail.
2. **Microsoft Profiles** — search, filter by domain/group/source, filter on *No connection yet* to show
   the gaps. Click a row for the drawer: who at Columbus knows this person, and how well.
3. **Microsoft Domains** — the categories live separately from the people, so Microsoft can be
   re-organised without touching the relationship data. Coverage bar per domain.
4. **Columbus Profiles** — expand a row to see that person's approved relations. Change a role, send a
   survey. The Super Admin row is locked.
5. **Viewing as → Standard** (top right) — the menu collapses to *My Relations*. Adjust a score, add a
   contact, remove one, then **Submit for approval**.
6. **Viewing as → Admin** — the submission is now on **Pending Submissions**, with a before → after diff.
   Adjust a score if you disagree, then approve — and see the change land on Microsoft Profiles.
7. **Viewing as → Moderator** — same overview, edit controls disabled, Domains and Submissions hidden.

The **role switcher** in the top bar is a demo control. In the real app your role comes from your
account; it is here so all four roles can be shown in one sitting.

## What the mockup covers

| Spec page | Route | Roles | State |
|---|---|---|---|
| Page 1 — Login | `#/login` | all | Placeholder, accepts anything |
| Page 2 — Dashboard | `#/dashboard` | Admin, Moderator | **Intentionally empty** — in the menu, placeholder content |
| Page 3 — Microsoft Profiles | `#/ms-profiles` | Admin, Moderator | Table, search, filters, sort, detail drawer, add/edit/delete |
| Page 3.5 — Microsoft Domains | `#/ms-domains` | Admin | Table with aggregates, add/edit/delete |
| Page 4 — Columbus Profiles | `#/columbus-profiles` | Admin, Moderator | Table, expandable relations, roles, surveys, add/delete |
| Page 5 — Pending Submissions | `#/submissions` | Admin | Before/after diff, adjust, approve, reject |
| Page 6 — My Relations | `#/my-relations` | all | Own relations, score picker, add/remove, submit |

### Relationship scale

| Score | Label | Meaning |
|---|---|---|
| +3 | Trusted advisor | They reach out to us proactively — we are a first call |
| +2 | Strong | Regular contact, mutual trust, ongoing dialogue |
| +1 | Positive | Occasional contact, good rapport |
| 0 | Neutral | Aware of each other, no real working relationship |
| -1 | Weak | Limited or one-sided contact |
| -2 | Strained | Friction or lost momentum |
| -3 | Damaged | Actively avoids working with us |

A Columbus employee can know many Microsoft people and a Microsoft person can be known by many
Columbus employees. Microsoft Profiles shows the **sum** of all scores for that person (the "vector
with a sum" from the requirements); Columbus Profiles shows the average per employee.

## Mocked vs. real

| | In this mockup | In the real app |
|---|---|---|
| Login | Accepts anything | Microsoft Entra ID, Columbus mail only |
| Data | ~20 fictional Microsoft contacts, 14 fictional Columbus users, 32 relations | Real data from the backend |
| Persistence | In memory — **a page refresh resets everything to the seed data** | Database, with versioning |
| Surveys | "Send survey" only stamps a date and shows a toast | Real survey sent and collected |
| Role | Chosen in the top bar | Comes from the signed-in account |
| Dashboard | Empty placeholder | Slide 1 of the stakeholder-map deck |

The reset-on-refresh behaviour is deliberate: it keeps the mockup a single dependency-free file, and
every demo starts from the same clean state.

## Structure

Everything lives in `index.html` — markup, styles and logic — in numbered sections:
constants, seed data, helpers, router + shell, then one section per page, then the drawer/modals,
mutations and event handling. The colour palette and the score ramp are CSS variables at the top of
the `<style>` block, so re-theming to the exact Columbus brand colours is a one-place change.

All names are invented. No real Microsoft or Columbus employees are represented.
