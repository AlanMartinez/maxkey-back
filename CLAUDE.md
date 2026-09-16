# maxkeys backend — deploy notes

## fly.toml is the real prod config

The root `fly.toml` (app `maxkeys` on Fly.io — the `app = 'maxkey-back'` line at
the top is stale, always deploy with `-a maxkeys`) is the **only** source of
truth for production config. `deploy/fly.toml` is an unfilled skeleton with
placeholder values — never deploy from it.

`flyctl deploy` replaces the app's `[env]` block wholesale, not merged. Every
key below has been silently dropped from `fly.toml` multiple times across PR
merges, breaking prod each time (CORS, admin login, Mercado Pago redirects,
migrations). Before merging any PR that touches `fly.toml`, or right after a
deploy, confirm all of these are still present:

- `[deploy] release_command = 'dotnet Maxkeys.Api.dll --migrate'` — without
  this, EF Core migrations never run automatically and endpoints start
  throwing `42703: column ... does not exist` after a schema change ships.
- `[env] Cors__AllowedOrigins__0/__1` — frontend origins (Vercel + chekeys.com).
- `[env] Auth__Mode/Issuer/Audience/JwksUrl` — Supabase JWT validation; without
  these, no one can log in.
- `[env] Frontend__BaseUrl` — Mercado Pago `back_urls`/`auto_return` target.
- `[env] Payments__NotificationUrl` — must point at `https://maxkeys.fly.dev/...`,
  not the stray unused `maxkey-back.fly.dev` app.

Secrets (`Payments__AccessToken`, `Payments__WebhookSecret`, `Keys__EncryptionKey`,
`ConnectionStrings__Default`, `Auth__AdminSubs__*`) live in `fly secrets`, not
`fly.toml` — those survive deploys independently and are not at risk here.
