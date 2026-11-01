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

- `src/auth.js` — Better Auth config, providers, account linking
- `src/config.js` — pure helpers (HF-02): trusted-origin parsing and fail-closed checks, the CORS origin rule, cookie options (`buildAdvancedOptions`)
- `src/server.js` — Express host, migrations at boot, secrets from Secrets service, CORS
- `src/exchange.js` — ES256 exchange token minting (60s TTL, single-use JTI); redeemed at Identity `/exchange`

## Notes

- Not orchestrated by the Aspire AppHost — run via `npm start` or `docker-compose.services.yml`
- Cookies are host-only (HF-02, D-48): no `Domain` attribute, cookie prefix `meridian` (`__Secure-meridian.session_token`). Localhost login needs the Gateway and Panel on the same host.
- Requires `DATABASE_URL`, `BETTER_AUTH_SECRET`, `EXCHANGE_TOKEN_PRIVATE_KEY` (PKCS8); Identity needs the matching public key
- Requires `BETTER_AUTH_TRUSTED_ORIGINS` (comma-separated exact origins) unless `NODE_ENV=development`; startup fails closed without it (HF-02, D-49)
- Tests: `npm test` (node:test, `test/config.test.js`) covers `src/config.js`; CI runs it in `_dotnet-build-test.yml`. There is no .NET test project.
