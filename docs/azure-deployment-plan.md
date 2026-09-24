# Azure test deployment: plan, and where we left off

*Written 24 Sep 2026, to continue from here. Nothing in Azure has been created
yet apart from the empty resource group.*

## Where we are

- **Branch `ponytail_dev`**, everything committed.
  - `README.md` explains how to run, seed, back up, restore and query the app.
  - The root `index.html` is the untouched design mockup. The app is `app/`.
- **Locally the app is complete and tested.** It runs in Docker Compose: `db`, `app`, `backup`, and `auth` (oauth2-proxy).
  - **Sign-in:** Microsoft Entra ID, @columbusglobal.com only, then a separate "Create your account" page on first sign-in.
  - **Roles:** two, Admin and Standard, with the same access to the data. Admin-only: roles, e-mails, deleting users, undo, the Backups page.
  - **History:** every change is recorded, and an Admin can undo one.
  - **Backups:** in the app (list, back up now, restore), plus daily dumps.
  - **Live updates:** a check every 20 s, only while the tab is visible.
  - **Skills:** a dropdown, to which people can add skills.
  - **Departments:** Data & AI and Dynamics.
  - **SQL access:** read-only, from VS Code (`db/reader.sql`, `127.0.0.1:5432`).
- **Your local stack** runs in dev mode (`docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d db app backup`). It holds the starting data (102 Microsoft people, 17 domains, 2 boards, 70 customers) and one Columbus profile, yours.
- **Azure**
  - Subscription **C-DNA-FASTTRACK-DEV** (`4479b4a4-…`) in the **Columbus IUR A/S** directory (`1fdf3400-…`). You are **Contributor** there, as a guest with your columbusglobal.com account.
  - Resource group **`rg-dna-ms-mapping`**, Sweden Central, exists and is empty.
  - The services we need are enabled: Container Apps, PostgreSQL, Container Registry, logging, Storage.
  - PostgreSQL **18** and the **B1ms** size are offered in Sweden Central.

## Before we start: not code

1. **Entra app registration.** This is still the blocker. The README ("Sign-in") has what to ask IT for.
   - You can't register apps in Columbus Global yourself.
   - Ask for the registration now with the local redirect URI (`http://localhost:8080/oauth2/callback`). The Azure one is added in step 3 of the deployment, once the Container Apps environment has given us an address. As an owner of the registration you can add it yourself.
   - **Do not deploy with dev sign-in:** on Azure, anyone could type any Columbus address and get in.
2. **Sign the CLI in to the IUR directory:** `az login --tenant 1fdf3400-c721-4fc4-84ca-7163d6ba4399`. The directory requires multi-factor sign-in for creating resources.
3. **What Contributor can't do:** create role assignments. So:
   - **Registry:** the Container App pulls images with the registry's own admin login, stored as a secret, instead of a managed identity with AcrPull.
   - **Secrets** go in Container Apps' secret store, not Key Vault. Key Vault would need an Owner to grant access.

   An Owner can switch both to managed identity later.

## Decisions to confirm tomorrow (recommendation first)

| # | Question | Recommendation | Alternative |
|---|---|---|---|
| 1 | Backups | Azure's own backups (daily, restore to any point in the last 7 days) **plus** the in-app Backups page kept working: dumps go to an Azure file share, and a scheduled job makes the daily dump, as locally | Only Azure's backups, with the Backups page hidden on Azure |
| 2 | Who can reach the database (SQL, seed) | Firewall: Azure's own services plus your IP only | A private network via Columbus VPN or a jump host. Stricter, more set-up |
| 3 | Who can reach the app while testing | Everyone with a @columbusglobal.com account | Also limited to Columbus office or VPN IP addresses (Container Apps IP restrictions) |
| 4 | Starting data | Load `db/seed.sql` once from your Mac | Start empty |
| 5 | How it's deployed | One **Bicep** file (`deploy/main.bicep`) and one `az` command. Re-runnable, and `az group delete` removes everything | A shell script of `az` commands |

## Target setup (names follow the `rg-dna-*` / CAF pattern)

| Resource | Name | Size / settings |
|---|---|---|
| Container registry | `crdnamsmapping` | Basic, admin login on (see above). The image is built in Azure with `az acr build` |
| Log Analytics workspace | `log-dna-ms-mapping` | Pay-as-you-go, 30 days |
| Container Apps environment | `cae-dna-ms-mapping` | Consumption |
| Container App | `ca-dna-ms-mapping` | Two containers in one app, **min 0 / max 1** copies. External HTTPS ingress, target port 4180, the proxy |
| – container `auth` | | `quay.io/oauth2-proxy/oauth2-proxy:v7.15.4`, 0.25 vCPU / 0.5 GiB, upstream `http://localhost:8080/` |
| – container `app` | | our image, 0.25 vCPU / 0.5 GiB, `/backups` mounted from the file share |
| PostgreSQL Flexible Server | `psql-dna-ms-mapping` | v18, Burstable **B1ms**, 32 GiB, 7-day backups, no high availability, TLS required, admin login `app`, database `app` |
| Storage account and file share | `stdnamsmapping` / `backups` | For the in-app dumps (decision 1) |
| Container Apps job | `caj-dna-ms-mapping-backup` | Cron `0 2 * * *`: `pg_dump` to the share, keeping 14 days (the same commands as the local `backup` service) |

**Secrets** (Container Apps secrets, passed to Bicep as secure parameters):
- the database password, and the `DATABASE_URL` built from it;
- `OAUTH2_PROXY_CLIENT_SECRET` and `OAUTH2_PROXY_COOKIE_SECRET`;
- the registry password.

**Why max 1 copy:** migrations run at start-up without a lock (see the `ponytail:` note in `migrate()`). One copy is also the cheapest.

## Code changes

1. **Sign-in proxy settings from the environment.** `docker-compose.yml` hard-codes `OAUTH2_PROXY_REDIRECT_URL`, `OAUTH2_PROXY_UPSTREAMS` and `OAUTH2_PROXY_COOKIE_SECURE=false`. On Azure they become `https://<app address>/oauth2/callback`, `http://localhost:8080/` and `true`, set in Bicep. The local values stay as they are.
2. **Encrypted database connection.** On Azure `DATABASE_URL` ends in `?sslmode=require`. **Check** that node-pg trusts Azure's certificate chain by default (DigiCert G2 / Microsoft RSA 2017). If not, add the CA bundle to the image. `pg_dump` and `pg_restore` use the same URL and need no change.
3. **`db/reader.sql`** works unchanged as long as the Azure admin login is called `app` (the plan above).
4. **The daily backup command** lives inline in `docker-compose.yml`. Put the one-shot version in a small shell script (`db/backup.sh`), so the local service and the Azure job run the same commands.
5. **README:** an "Azure" section covering deploy, redeploy, seed, SQL access, cost, and how to stop and delete it.
6. **Optional** tidy-up from today: `tmpfs: /var/lib/postgresql` on the local `backup` service, so it stops creating an unused anonymous volume each time it's recreated.

## Deployment steps (in order)

1. `az acr build -r crdnamsmapping -t ms-mapping:<git sha> app/`. The registry must exist first, so the first Bicep run creates the registry, logging and environment.
2. `az deployment group create -g rg-dna-ms-mapping -f deploy/main.bicep -p …secrets…`
3. Read the app's address (`https://ca-dna-ms-mapping.<id>.swedencentral.azurecontainerapps.io`). Add `https://…/oauth2/callback` to the Entra registration (IT, or you as owner).
4. Allow your IP in the database firewall. Load the seed from your Mac with `docker run --rm -i postgres:18-alpine psql "<DATABASE_URL>" -v ON_ERROR_STOP=1 < db/seed.sql`, after the app has started once and created the schema. Then run `db/reader.sql` the same way.
5. Open the app, sign in, create your account as Admin (`ADMIN_EMAILS` = your address), and try it.

## Verification on Azure

- **Sign-in**
  - A real Microsoft sign-in, with the account picker.
  - A new account lands on the Create your account page, then enters the app.
  - A non-Columbus account is refused.
- **Features**
  - Add and edit profiles, score from the dashboard, then undo one change in History as an Admin.
  - "Back up now" makes a file in the share. Restore it and the data comes back. Tomorrow's daily dump appears in the list.
  - With two browsers, a change shows up in the other within 20 s.
- **Database:** VS Code connects with the server's address, the `reader` login and SSL `require`, and can read but not write.
- **Access:** the app container isn't reachable from the internet; only the proxy's port is exposed.
- **Cost:** look at Cost Management after a day or two. The app should scale to zero overnight.

## Rough cost for the test (check the Azure price calculator)

| Item | Per month |
|---|---|
| PostgreSQL B1ms + 32 GiB | about €15 to €20. It can be **stopped** when not in use, and restarts itself after 7 days |
| Container registry Basic | about €5 |
| Container Apps | most likely within the free grant, because it scales to zero. The first visit after a quiet period takes a few seconds |
| Log Analytics, storage and the job | a few euros at most |

**To stop everything:** `az group delete -n rg-dna-ms-mapping` (the resource group is only ours).

## Open questions and risks

- **Database sign-in with Entra, as a guest** in the IUR directory. Not needed for the test, since the password logins suffice. Try it later if we want Entra sign-in for SQL.
- **Azure file share permissions:** the app runs as uid 1000. Check that the mounted share is writable (Container Apps mounts it world-writable by default).
- **The certificate chain for `sslmode=require`,** see code change 2.
- **The IUR directory's policies:** NIST, ASC and EU AI Act are audit-only, so they don't block anything. Multi-factor sign-in is enforced for resource writes.

## Also noted today (not Azure)

- **An unexplained failure:** once, "Create account" in an automated test didn't create the account. Five re-runs, including the exact same steps, couldn't reproduce it. Watch for it.
- **Leftovers from earlier iterations** on this Mac, not from this app: 14 stray anonymous Docker volumes (15 to 16 Sep) and old stopped `mail` (Mailpit) containers. Clean them up whenever you like.
- **Two things from the missing-features list are still open:** managing customers (add, rename, type), and editable groups and sources.
