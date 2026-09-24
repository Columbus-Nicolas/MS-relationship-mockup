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

The app starts **empty**. To load the mockup's data (the real Microsoft Denmark
intake list; the Columbus users, scores and notes are placeholders), run once,
into an empty database, after the app has started:

```sh
docker compose exec -T db psql -U app -d app -v ON_ERROR_STOP=1 < db/seed.sql
```

## Sign-in

Microsoft Entra ID, Columbus Global directory, **@columbusglobal.com accounts
only**. The `auth` service (oauth2-proxy) does the sign-in and is the only way
in: the app itself has no published port. It hands the signed-in user to the
app, which adds them to Columbus Profiles on their first sign-in with the
**Standard** role - or **Admin** for the e-mails in `ADMIN_EMAILS`. A profile an
admin added beforehand with the same e-mail is used as it is, role and all.

Two roles, **Admin** and **Standard**, with the same access for now: everyone
who signs in sees every page and can change everything. To make some of it
admin-only later, take `'standard'` out of `ADMIN` in `app/server.js` and
`canEdit()` in `app/index.html`, and out of the pages' `roles` lists.

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

**Dev override** (`docker-compose.dev.yml`): no Microsoft sign-in; you are
`DEV_USER_EMAIL` (default Mette Kirkegaard, Admin in the seed data, and on an
empty database). Try another role:

```sh
DEV_USER_EMAIL=line.aagaard@columbusglobal.example docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d app
```

## Changing the schema

Add the next numbered file to `app/migrations/` (`002_what_it_does.sql`, …) and
rebuild the app. On start-up it applies each file it has not applied yet, in
name order, each in its own transaction, and records it in `schema_migrations`.
A failing file rolls back and the app does not start — `docker compose logs app`
says why. Never edit a file that has already been applied.

## Backups

The `backup` service dumps the database at start-up and every 24 hours into the
`backups` volume, keeping 14 days. It is on the same machine as the database, so
copy dumps somewhere else for anything that matters.

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
range) inside a transaction it rolls back, so it is safe on real data.

## Reset

```sh
docker compose down -v              # deletes the database AND the backups volume
```
