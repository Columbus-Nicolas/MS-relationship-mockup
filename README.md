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
| Dashboards | Page 2 — Dashboard · Data & AI | `#/dashboard` | Admin, Moderator | Ecosystem map — hub & spoke board, scoring, filters, export |
| Dashboards | Graphics · Data & AI | `#/graphic-view` | Admin, Moderator | KPIs, breakdowns, score distribution, click a bar for the people behind it |
| Dashboards | Dashboard · Dynamics | `#/dashboard-dynamics` | Admin, Moderator | The same board over the Business Applications organisation |
| Dashboards | Graphics · Dynamics | `#/graphic-view-dynamics` | Admin, Moderator | The same graphics, counted over the Dynamics board only |
| Administration | Dashboards | `#/dashboards` | Admin | Add, rename and delete dashboards; panels come from the domains |
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
| Responsive | ≥1100px the full ring, scaled so the **whole board fits the window at once** — the largest scale that fits both the width and the height left below the toolbar, centred, with the leftover split evenly; 700–1100px the spokes drop and the panels reflow into two columns under a hub banner; <700px a single column |
| Layout pass | Vertical positions are settled after render from measured heights (`ecoLayout`): the panel above the hub hangs from the circle and grows upward, anything that still does not fit pushes what is below it down, and the canvas ends where the content does |

Two deliberate departures from the brief, both forced:

- **Canvas is 1150 wide and at least 862 tall, not 1150 × 630.** Two rows of 2×2 person cards plus a
  header strip and the bottom band do not fit in 630 units at the specified type and avatar sizes.
  Since the brief also says panels must grow rather than truncate text, the canvas grew instead — and
  because panels really do grow, the height is now measured rather than fixed: 862 for the Data & AI
  board, more when a board's content needs it. The width is fixed, so the board still scales as one
  piece and the SVG export carries the measured height. A taller board therefore fits the screen at a
  smaller scale — the board is one picture with one aspect ratio, so filling the width and showing the
  whole thing cannot both be true at once, and showing the whole thing wins.
- **No PNG export.** Rasterising HTML in the browser means painting it through an SVG `foreignObject`,
  and every engine taints the canvas when you do — `toBlob()` is refused. A real PNG needs a rendering
  library, which would break the one-file, no-dependency rule. The SVG is a true vector export and
  Print gives a PDF.

The strength dot is 16px rather than 14px, so that `+3` and `-2` stay legible inside it.

### Two dashboards

A dashboard is one department's view of Microsoft. Each has its own board and its own graphics
page, and the pair sits together in the left menu with the graphics indented under its dashboard.

Adding a board is a key in `ECOS` plus a set of domains carrying that board's `dept` — the ring,
the panels, the export and the graphics are the same code for every board. Which board a page
shows comes from the route, through `activeBoard()`.

| | Data & AI | Dynamics |
|---|---|---|
| Board | `#/dashboard` | `#/dashboard-dynamics` |
| Graphics | `#/graphic-view` | `#/graphic-view-dynamics` |
| Domains | `d1`–`d8` | `dy1`–`dy8` |
| Contacts | 23, fictional | 85, drafted from the deck |
| State | approved mockup content | **draft** — six data points still marked `(to confirm)` |

**A person can sit on both boards.** Membership runs through the domains, so ticking domains from
two boards is all it takes — there is no second field to keep in sync, and the person keeps one
score that both boards show. Six people do today: Nina Due, Christian Koch-Bentzen, Sarah McKenna,
Bo Larsen, Jesper N. Pedersen and Bo Kaaber Brandt. That is why the dashboard filter on Microsoft
Profiles adds up to more than the total.

Panels holding a long tail carry a `max`. The board shows the scored people first and counts the
rest (`+31 more`), so a 35-name account team cannot grow down through the panel below it — the
full list lives in Microsoft Profiles and in the graphics drill-down.

### Adding a dashboard

**Dashboards** under Administration adds, renames and deletes them, the same way domains and people
are managed. Creating one gives it a board and a graphics page in the left menu immediately, and
lands you on it — empty, with the next step spelled out: point domains at it on Microsoft Domains.

The two seeded boards keep hand-tuned panel layouts in `ECOS` (accent, icon, slot and cap chosen
per panel). A board added from the UI has no entry there and **derives its panels from its
domains** instead: the first eight fill the ring's slots in the order they were created, taking
accent, icon and layout from `ECO_DERIVED_SLOTS`. Derived panels are rebuilt on every read, so a
domain that is added, renamed or reassigned shows up without anything having to invalidate a cache.

Two limits worth knowing:

- **Eight panels per board.** The ring has nine slots and the ninth is the collaboration block.
  Domains beyond the eighth still count on the graphics page and in Microsoft Profiles, but get no
  panel — the Dashboards page names the boards where that happens.
- **A dashboard must be empty to be deleted,** and the first one cannot be deleted at all — it is
  the landing page. So no domain or relationship can be orphaned by deleting a board.

Renaming changes the heading, the hub, the menu entries and every label that mentions the board.
The two routes are built from the id and stay put, so links already shared keep working.

### Clicking into a chart

On either graphics page, a bar is a control: clicking it lists the people it counts directly under
the chart, with **Phone** and **E-mail** as columns. Both are empty. The source material holds no
contact details, and filling them with plausible-looking values next to real names would read as
data rather than as a gap. The fields exist on the profile and in the edit form, ready for a real
source.

### Where the Dynamics data comes from

`Microsoft Denmark Organization_tbj_020726.pptx` (Thomas Johansson, 27 May 2026) — three documents
in one file: hand-built relationship boards with −3…+3 scores and owner initials, four pages of a
generated org chart, and a flat stakeholder table. 86 unique people after resolving aliases and
duplicates; 23 of the relations carry a score read off the boards. Where the deck names an owner
but shows no score, the relation is recorded as **0 (Neutral)**. People with no owner get no
relation at all, so they stay unscored on the board and show up under *Only unscored*.

Six things the deck could not settle are in the data as a best guess marked `(to confirm)`:
Bo Brandt versus Bo Kaaber Brandt, Bo Larsen's owner, Sarah McKenna versus Sarah McGeller, the
alias `JESHRI`, Juliane Maack's placement, and the one box holding both Bo Larsen and Jesper
Pedersen. The importance 0–10 column from the stakeholder table is deliberately not imported —
the scale is −3…+3 and nothing else.

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
| Data | 102 Microsoft contacts, 26 Columbus users, 70 relations — the Data & AI board is fictional, the Dynamics board is a draft read out of Thomas' deck | Real data from the backend |
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
