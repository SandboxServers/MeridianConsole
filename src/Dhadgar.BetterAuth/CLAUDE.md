# BetterAuth Service

Social OAuth authentication using the Better Auth SDK.

## Tech Stack

- **Node.js/Express** (not .NET — the `.csproj` is a NoTargets shim so `dotnet build` covers it)
- Better Auth SDK (`better-auth`), `jose` for ES256 exchange tokens, `pg`
- PostgreSQL (shared with Identity, no schema isolation — see issue #120 P2)

## Port

5130

## Status

Implemented. Social providers configured conditionally on env presence: Facebook, Google,
Discord, Twitch, GitHub, Apple, plus Microsoft via genericOAuth with WIF client assertion
(no static secret). No passwordless plugins (magic link / OTP / passkey) are configured.

## Key Files

- `src/auth.js` — Better Auth config, providers, account linking, cookies
- `src/server.js` — Express host, migrations at boot, secrets from Secrets service, CORS
- `src/exchange.js` — ES256 exchange token minting (60s TTL, single-use JTI); redeemed at Identity `/exchange`

## Notes

- Not orchestrated by the Aspire AppHost — run via `npm start` or `docker-compose.services.yml`
- Cookie domain is hardcoded to `meridianconsole.com`; localhost login fails (beta roadmap Phase 1)
- Requires `DATABASE_URL`, `BETTER_AUTH_SECRET`, `EXCHANGE_TOKEN_PRIVATE_KEY` (PKCS8); Identity needs the matching public key
- No test project exists for this service
