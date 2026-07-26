# Panel - Main Control Plane UI

Primary user interface for server management.

## Tech Stack
- Astro 5 (SSR via `@astrojs/node`) + React 18 islands + Tailwind 3 + TypeScript
- Auth via `@dhadgar/shared-auth` (`file:../Dhadgar.SharedAuth`)
- A couple of vestigial `.razor` files remain from the old Blazor era (not built; safe to delete)

## Status
Scaffolding: login/callback/logout/dashboard pages exist and real OAuth works, but the
dashboard shows hardcoded zeros (the fully built API client in `src/lib/auth/api.ts` is
never called) and the `/servers`, `/nodes`, `/settings` nav links 404. See beta roadmap
Phase 4 in `docs/PROJECT-STATE.md`.

## Notes
- SSR output (`dist/server/entry.mjs`, port 4321) — Dockerfile present; not an SWA target
- No `.env.production` (prod Gateway URL arrives only via Docker build args)
- No lint script and no JS/TS tests
