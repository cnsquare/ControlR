# EnkrOma deployment

Self-contained Docker Compose deployment for the EnkrOma-branded server (ControlR fork): the server,
Postgres, and the Aspire telemetry dashboard.

The server runs on plain HTTP (`:8080` in the container, `:5120` on the host). Put a TLS-terminating
reverse proxy — Caddy, nginx, or Cloudflare — in front of it at your public URL.

## Prerequisites

- Docker + Docker Compose on the host.
- PowerShell (`pwsh`) on the machine that builds the image (branding is a build-time step).
- This repo checked out. `origin` still points at the upstream project — don't push there; fork first
  if you want a CI pipeline later.

## 1. Build the EnkrOma image

The shipped upstream image (`bitbound/controlr:latest`) has **neither EnkrOma branding nor the
re-enrollment features** — you must build your own.

```bash
deploy/enkroma/build-image.sh 1.0.0
```

This checks out the noVNC submodule, applies branding from `.branding/enkroma.json` (which bakes in
the fleet enrollment secret, so re-enrollment is on by default), and builds `enkroma/controlr:1.0.0`.

> The branding step edits source files in your working tree. Run it on a clean checkout, or
> `git restore .` afterward. A fresh clone or CI checkout is the cleanest place to build.

## 2. Configure

```bash
cd deploy/enkroma
cp .env.example .env
# edit .env — at minimum set the database password, PublicBaseUrl, and Aspire token
```

Required in `.env`:

| Key | Notes |
|---|---|
| `ENKROMA_IMAGE` | The tag you built in step 1. |
| `ControlR_POSTGRES_USER` / `ControlR_POSTGRES_PASSWORD` | Change from the examples. |
| `ControlR_AppOptions__PublicBaseUrl` | e.g. `https://control.enkroma.com`. Used for outbound links **and** it's the host agents dial. |
| `ControlR_ASPIRE_BROWSER_TOKEN` | Long random token for the telemetry dashboard. |

If TLS is terminated at Cloudflare, set `ControlR_AppOptions__EnableCloudflareProxySupport=true`.
Email confirmation is on by default — configure the `Smtp*` values, or set
`ControlR_AppOptions__RequireUserEmailConfirmation=false`.

## 3. Run

```bash
docker compose up -d
docker compose logs -f enkroma
```

First login: either register the first user in the UI (auto-promoted to admin), or uncomment the
`ControlR_Bootstrap__Admin*` lines in `.env` to seed an admin on first startup.

## Updating

```bash
deploy/enkroma/build-image.sh 1.0.1      # build a new tag
#   set ENKROMA_IMAGE=enkroma/controlr:1.0.1 in .env
docker compose up -d                     # recreates the server container; DB volume is preserved
```

## Backups

All durable state — devices, users, data-protection keys, and the server-generated **active fleet
secrets** — lives in the `enkroma-data` Postgres volume. Back it up regularly.

If the database is ever lost and rebuilt, agents recover automatically using the **baked-in bootstrap
fleet secret** (compiled into the branded build), then re-adopt freshly generated active secrets on
their next heartbeat. That is why the bootstrap secret is baked in. You can rotate the active secret
any time from **Tenant Settings → Rotate Fleet Secret**.

## Full option reference

Only the high-value settings are surfaced here. The complete list of `ControlR_*` environment
variables — Docker Secrets, key-protection certificates, OAuth logins, CORS, decommissioning, and
more — is documented inline in [`../../docker-compose/docker-compose.yml`](../../docker-compose/docker-compose.yml).
Add any of them to this folder's `docker-compose.yml` or `.env` as needed.
