# Columbus · Microsoft Relationship Map

Who at Columbus knows whom at Microsoft Denmark, and how well. One page
(`app/index.html`), a small Node API (`app/server.js`) and Postgres, run with
Docker Compose. The root `index.html` is the design mockup the app was copied
from; it is kept untouched as the reference.

## Run it

With Microsoft sign-in: copy `.env.example` to `.env`, fill in the values from
the Entra app registration (see Sign-in), then

```sh
docker compose up --build -d        # http://localhost:8080
```

Without Microsoft sign-in - local work, or before the app registration exists -
use the dev override. It needs no `.env`:

```sh
docker compose -f docker-compose.yml -f docker-compose.dev.yml up --build -d db app backup
```

The app starts **empty**. To load the starting data - the Microsoft Denmark
intake list, the domains, the two boards and the customers; no Columbus people,
they add themselves - run once, into an empty database, after the app has
started:

```sh
docker compose exec -T db psql -U app -d app -v ON_ERROR_STOP=1 < db/seed.sql
```

## Sign-in

Microsoft Entra ID, Columbus Global directory, **@columbusglobal.com accounts
only**. The `auth` service (oauth2-proxy) does the sign-in and is the only way
in: the app itself has no published port. You click *Sign in with Microsoft
Entra ID* and pick your account at Microsoft (it always asks). The first time,
you then land on a separate **Create your account** page (`#/create-account`) -
name, title, department, skills picked from a dropdown (add one that is missing
and it is offered to everybody), phone - and nothing else works until it is
filled in. That makes your entry in Columbus Profiles, as **Standard**, or
**Admin** for the e-mails in `ADMIN_EMAILS`. A profile an admin added beforehand with the same e-mail is
used as it is, so that person skips the page.

Two roles, **Admin** and **Standard**, with the same access to the data for
now: everyone sees every page and can change everything. **Admin-only:**
changing roles, changing someone's e-mail (it is how they sign in), deleting
users, undoing changes in History, and the Backups page. To make more of it
admin-only later, take `'standard'` out of `ADMIN` in `app/server.js` and
`canEdit()` in `app/index.html`, and out of the pages' `roles` lists.

Changes others make show up within 20 seconds: an open tab asks the server
every 20 s whether anything changed (a few bytes) and only then reloads the
data. A tab in the background asks nothing.

**The app registration** is created by an Entra admin (ordinary users cannot
register apps in Columbus Global). What to ask for:

- Name: *Columbus MS Relationship Mapping*; single tenant (Columbus Global only).
- Platform **Web**, redirect URI `http://localhost:8080/oauth2/callback`
  (add the Azure address once the app is deployed there).
- A **client secret** (e.g. 12 months), handed over securely.
- Microsoft Graph delegated permissions `openid`, `profile`, `email`
  (plus the default `User.Read`), with **admin consent** granted.
- Token configuration: optional claim **`email`** on the ID token.
- *Assignment required*: **No** - the proxy enforces the domain.
- Add the app's maintainer as an **owner** of the registration.

**Dev override** (`docker-compose.dev.yml`): no Microsoft sign-in. The login
page asks for a work e-mail instead - any @columbusglobal.com address, no
password - so you can be anybody; sign out to be someone else. The addresses in
`ADMIN_EMAILS` (default `dev.user@columbusglobal.com`) become Admin when they
create their account:

```sh
ADMIN_EMAILS=you@columbusglobal.com docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d app
```

## Changing the schema

Add the next numbered file to `app/migrations/` (`005_what_it_does.sql`, …) and
rebuild the app. On start-up it applies each file it has not applied yet, in
name order, each in its own transaction, and records it in `schema_migrations`.
A failing file rolls back and the app does not start — `docker compose logs app`
says why. Never edit a file that has already been applied.

## History and undo

Every change anybody makes is kept: the **History** page lists them newest
first - who, when, what - and opens each to the fields it changed. An **Admin**
can undo one. That puts back everything it did, a deleted profile with its
relations and links included, but only if nothing in it has changed since and
nothing added later depends on it; otherwise it says which to undo first. An
undo is a change in History too, so it can be undone in turn.

## Backups

The `backup` service dumps the database at start-up and every 24 hours into the
`backups` volume, keeping 14 days. It is on the same machine as the database, so
copy dumps somewhere else for anything that matters.

**In the app** (Admin only), the **Backups** page lists the dumps, takes one on
request, and restores one: it checks the dump was made by the same version of
the app, backs up the current state first (so the restore can be reversed from
the same page), restores in one transaction and notes it in History. A dump
from an older version is refused there - restore it on the command line below.

```sh
docker compose exec backup ls -l /backups                          # list
docker compose exec backup sh -c 'pg_dump -Fc -f /backups/app-manual-$(date +%F-%H%M).dump'   # back up now
docker compose cp backup:/backups ./backups                        # copy them out (git-ignored)
```

Restore one (this replaces everything in the database):

```sh
docker compose stop app
docker compose exec backup sh -c 'dropdb --force app && createdb app && pg_restore -d app /backups/<file>.dump'
docker compose start app
```

## Checks

```sh
docker compose exec -T db psql -U app -d app -v ON_ERROR_STOP=1 < db/check.sql
```

Asserts the rules the database enforces (owners, cascades, Unmarked, score
range) and that History and undo work (undo of a delete with its cascades,
refused when changed since or depended on, never twice), inside a transaction
it rolls back, so it is safe on real data.

## Reset

```sh
docker compose down -v              # deletes the database AND the backups volume
```
