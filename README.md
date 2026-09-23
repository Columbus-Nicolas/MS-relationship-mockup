# Columbus · Microsoft Relationship Map

Who at Columbus knows whom at Microsoft Denmark, and how well. One page
(`app/index.html`), a small Node API (`app/server.js`) and Postgres, run with
Docker Compose. The root `index.html` is the design mockup the app was copied
from; it is kept untouched as the reference.

## Run it

```sh
docker compose up --build -d        # http://localhost:8080
```

The app starts **empty**. To load the mockup's data (the real Microsoft Denmark
intake list; the Columbus users, scores and notes are placeholders), run once,
into an empty database, after the app has started:

```sh
docker compose exec -T db psql -U app -d app -v ON_ERROR_STOP=1 < db/seed.sql
```

## Sign-in

Development only: everyone is the Columbus user whose e-mail is
`DEV_USER_EMAIL` (default Mette Kirkegaard, Admin), with that user's role.
With no such user yet (an empty database) you act as Super Admin. The app only
listens on localhost for this reason. Try another role:

```sh
DEV_USER_EMAIL=line.aagaard@columbusglobal.example docker compose up -d app
```

Admin and Super Admin change data; Moderator reads everything; Standard sees
Upkeep and My Relations, and is sent only their own relations.

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
