# Columbus &middot; Microsoft Relationship Map — UI mockup

A clickable UI mockup of the app that gives Columbus a shared, always-current overview of the
relationships to key people at Microsoft Denmark — where we have strong connections, where we are
missing them, and how that changes when roles and organisations shift on both sides.

This is a **presentation mockup**. Opened on its own it has no backend, no authentication and no
persistence — everything you see is fictional. There is now also a real backend it can sign in
against for local development; see *Running it against the backend* below.

## How to open it

Double-click `index.html`, or drag it into a browser. No install, no build step, no internet
connection needed.

On the login screen: press **Continue** (or the Entra ID button). Any e-mail — or none at all —
signs you in as *Mette Kirkegaard* with the **Admin** role.

## Running it against the backend (local development)

The sections above describe the standalone mockup. There is also a real ASP.NET Core backend in
`backend/`, and a developer mode that lets you sign in and run against a real Postgres database
seeded with this same synthetic dataset.

```bash
cp .env.example .env          # then set POSTGRES_PASSWORD, the matching Password= in
                              # ConnectionStrings__Default, and DEV_MODE=true
docker compose up -d db mail
dotnet run --project backend/src/MsRelationship.Api
```

Then open `index.html` and click **Developer sign-in** on the login page. That button appears
only while the API is running with `DEV_MODE=true` — it is rendered from a live call to
`GET /api/dev/session`, a route that exists only in dev mode.

On boot, dev mode applies migrations and seeds the approved dataset (23 Microsoft profiles, 9
domains, 14 Columbus users and their relations), then adds a `dev@columbusglobal.example`
Super Admin account to sign in as. All of it is idempotent.

Dev mode is an authentication bypass — it accepts an `X-Dev-Email` header instead of an Entra
token — so the API refuses to start if it is enabled outside Development. Full details, including
what changes between dev mode on and off, are in [docs/local-setup.md](docs/local-setup.md).

**The pages still render from the in-memory `state` object.** Dev mode gives you a real database
and a real authenticated session; it does not yet wire Microsoft Profiles, Domains or Relations
to the API, because the read endpoints for those do not exist.

## Demo path for a presentation

1. **Login** — placeholder, mention that the real thing is Microsoft Entra ID restricted to Columbus mail.
2. **Dashboard** — the ecosystem map. Columbus in the middle, the Microsoft groups around it, dotted
   spokes to each. Hover a group to isolate it, click a `?` to score that relationship, use *Only
   unscored* to show the white space. **Export SVG** or **Print** to get the board out for a deck.
3. **Microsoft Profiles** — search, filter by domain/group/source, filter on *No connection yet* to show
   the gaps. Click a row for the drawer: who at Columbus knows this person, and how well.
4. **Microsoft Domains** — the categories live separately from the people, so Microsoft can be
   re-organised without touching the relationship data. Coverage bar per domain.
5. **Columbus Profiles** — expand a row to see that person's approved relations. Change a role, send a
   survey. The Super Admin row is locked.
6. **Viewing as → Standard** (top right) — the menu collapses to *My Relations*. Adjust a score, add a
   contact, remove one, then **Submit for approval**.
7. **Viewing as → Admin** — the submission is now on **Pending Submissions**, with a before → after diff.
   Adjust a score if you disagree, then approve — and see the change land on Microsoft Profiles.
8. **Viewing as → Moderator** — same overview, edit controls disabled, Domains and Submissions hidden.

The **role switcher** in the top bar is a demo control. In the real app your role comes from your
account; it is here so all four roles can be shown in one sitting.

## What the mockup covers

The left menu is grouped into three sections. A section disappears entirely when the current role has
no pages in it — a Standard user only sees *Submissions & Relations*.

| Menu section | Spec page | Route | Roles | State |
|---|---|---|---|---|
| — | Page 1 — Login | `#/login` | all | Placeholder, accepts anything |
| Dashboards | Page 2 — Dashboard | `#/dashboard` | Admin, Moderator | Ecosystem map — hub & spoke board, scoring, filters, export |
| Dashboards | Graphic View | `#/graphic-view` | Admin, Moderator | **Intentionally empty** — placeholder for the visual map |
| Administration | Page 3 — Microsoft Profiles | `#/ms-profiles` | Admin, Moderator | Table, search, filters, sort, detail drawer, add/edit/delete |
| Administration | Page 3.5 — Microsoft Domains | `#/ms-domains` | Admin | Table with aggregates, add/edit/delete |
| Administration | Page 4 — Columbus Profiles | `#/columbus-profiles` | Admin, Moderator | Table, expandable relations, roles, surveys, add/delete |
| Submissions & Relations | Page 5 — Pending Submissions | `#/submissions` | Admin | Before/after diff, adjust, approve, reject |
| Submissions & Relations | Page 6 — My Relations | `#/my-relations` | all | Own relations, score picker, add/remove, submit |

### The ecosystem map (Dashboard)

A single-screen, hub-and-spoke board: Columbus at the centre, eight colour-coded Microsoft groups
around it, dotted spokes from the hub to each group. It is the slide you present — who we know, how
well, and where the white space is.

The whole board is rendered from one config object (`ECO` in `index.html`): organisation, header
metadata, the groups with their accent colour, icon, layout and ring slot, and the people inside them.
Nothing about the layout is written into the markup, so panels can be reordered, recoloured or
refilled by editing that object alone.

| Piece | Behaviour |
|---|---|
| Ring slots | `left-1/2`, `centre-top/bottom`, `right-1/2`, `bottom-1/2/3` — a panel declares a slot, the slot table decides where it lands |
| Panel body | `grid-2` (2×2 cards), `rows` (full-width lines) or `bare` (no card chrome) |
| Spokes | Drawn in SVG behind the panels, in the group's accent, aimed at each panel's real rendered edge |
| Strength dot | −3…+3 in the legend colours; unscored people show a dashed `?`. Click one to score it |
| Filters | *Only unscored* / *Only +2 and stronger* — non-matching people **dim** rather than disappear, so the shape of the map never changes |
| Hover | The group's spoke brightens, the other seven panels drop to 70% |
| Export | **Export SVG** downloads the board as a vector file; **Print** lays it out on one landscape page |
| Responsive | ≥1100px the full ring, scaled to fit; 700–1100px the spokes drop and the panels reflow into two columns under a hub banner; <700px a single column |

Two deliberate departures from the brief, both forced:

- **Canvas is 1150 × 862, not 1150 × 630.** Two rows of 2×2 person cards plus a header strip and the
  bottom band do not fit in 630 units at the specified type and avatar sizes. Since the brief also says
  panels must grow rather than truncate text, the canvas grew instead. Aspect ratio is still fixed and
  the board still scales to fit.
- **No PNG export.** Rasterising HTML in the browser means painting it through an SVG `foreignObject`,
  and every engine taints the canvas when you do — `toBlob()` is refused. A real PNG needs a rendering
  library, which would break the one-file, no-dependency rule. The SVG is a true vector export and
  Print gives a PDF.

The strength dot is 16px rather than 14px, so that `+3` and `-2` stay legible inside it.

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
| Dashboard | Ecosystem map over its own fictional config, edits held in memory | Same board, fed from the relationship data |

The reset-on-refresh behaviour is deliberate: it keeps the mockup a single dependency-free file, and
every demo starts from the same clean state.

## Structure

Everything lives in `index.html` — markup, styles and logic — in numbered sections:
constants, seed data, helpers, router + shell, then one section per page, then the drawer/modals,
mutations and event handling. The colour palette and the score ramp are CSS variables at the top of
the `<style>` block, so re-theming to the exact Columbus brand colours is a one-place change.

All names are invented. No real Microsoft or Columbus employees are represented.
