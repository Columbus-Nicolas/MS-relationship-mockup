# Running the app locally

This is the developer-mode path: a real backend, a real Postgres database holding the synthetic
dataset, and a sign-in that works without a Microsoft Entra app registration. Everything here is
local only.

## Prerequisites

- Docker Desktop (Postgres and Mailpit run as containers)
- .NET SDK 9 — `backend/global.json` pins `9.0.306` with `rollForward: latestFeature`

## One-time setup

```bash
cp .env.example .env
```

Then edit `.env`:

| Key | Value | Why |
|---|---|---|
| `POSTGRES_PASSWORD` | anything | Postgres refuses to start without one |
| `ConnectionStrings__Default` | same password in the `Password=` segment | The API and the container must agree |
| `DEV_MODE` | `true` | Turns on the local sign-in and the boot-time seed |
| `AZUREAD__ALLOWEDEMAILDOMAINS` | *(empty)* | Every seeded account is `@columbusglobal.example`; the default `columbusglobal.com` would reject all of them |

`.env` is gitignored. Do not commit it.

Note that the API parses `.env` itself in Development, so `dotnet run` is the whole command.
**Do not** `set -a; source .env` — the connection string contains semicolons, the shell treats
them as command separators, and the value silently truncates to `Host=localhost`.

## Every time

```bash
docker compose up -d db mail
dotnet run --project backend/src/MsRelationship.Api
```

On boot in dev mode the API applies migrations, seeds the approved dataset from
`Data/seed.json` (23 Microsoft profiles, 9 domains, 14 Columbus users and their relations) and
adds one `dev@columbusglobal.example` account. All of it is idempotent — restart as often as you
like.

Then open `index.html` in a browser and click **Developer sign-in** on the login page. That
button only renders when the API answers `GET /api/dev/session`, which exists only while
`DEV_MODE=true`.

## What dev mode actually changes

| | `DEV_MODE=true` | `DEV_MODE=false` |
|---|---|---|
| Authentication | `X-Dev-Email` header | Microsoft Entra bearer token |
| `/api/dev/session` | returns the dev account | 404 — route not mapped |
| Migrations & seed | applied on boot | never touched by the web process |
| CORS | any origin (the mockup runs from `file://`) | not registered |
| Login page | shows the Developer sign-in block | unchanged from the mockup |

The two authentication schemes are mutually exclusive — dev mode registers its handler *instead
of* Entra, never alongside it — and the API refuses to start if `DEV_MODE=true` outside
Development. See `Auth/DevModeGuard.cs`.

## Calling the API directly

```bash
curl localhost:5080/health
curl localhost:5080/api/dev/session
curl -H "X-Dev-Email: dev@columbusglobal.example" localhost:5080/api/columbus-users
```

Without the header, everything except `/health` returns 401.

## Inspecting the database

```bash
docker compose --profile tools up -d      # pgAdmin on localhost:5050
```

Credentials come from `PGADMIN_EMAIL` / `PGADMIN_PASSWORD` in `.env`. Mailpit's UI is on
`localhost:8025`.

## Microsoft Entra ID

The app registration does not exist yet, so `AZUREAD__TENANTID` / `CLIENTID` / `AUDIENCE` stay
blank and `appsettings.json` carries placeholder GUIDs to keep the app bootable (see the comment
in `Program.cs`). Once a registration exists, fill those three in and set `DEV_MODE=false`.

## Known limitation

The mockup's pages still render from the in-memory `state` object in `index.html`. Dev mode gives
you a real database and a real authenticated session; it does not yet wire Microsoft Profiles,
Domains or Relations to the API, because the read endpoints for those do not exist.
