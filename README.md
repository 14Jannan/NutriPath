# NutriPath

AI-assisted, real-world-data-backed nutrition tracker. React Native (Expo)
frontend, ASP.NET Core backend, PostgreSQL, real nutrition data from USDA
FoodData Central and Open Food Facts plus a curated Sri Lankan food set.

## Structure

- `frontend/NutriPath.Mobile` — Expo/React Native (TypeScript) app
- `frontend/NutriPath.Admin` — admin web portal (Vite, React, TypeScript)
- `backend/` — ASP.NET Core Web API (.NET 10) + EF Core + PostgreSQL
- `tests/` — xUnit unit and integration tests
- `data/ingestion/` — food data sync scripts and seed files
- `docs/` — architecture notes, ERD, phase-by-phase build log
- `infrastructure/docker/` — Dockerfiles and docker-compose

## Status

Under active phase-by-phase development. See `docs/phase-0-architecture.md`.

See [Nutrition assistant setup and evaluation](docs/ai-nutrition-phase1.md)
for knowledge migration, trusted CLI commands, and local embeddings.

## Admin portal

`frontend/NutriPath.Admin` is the web portal for administrators: users, administrators and roles,
AI providers, encrypted API keys, runtime AI configuration, usage analytics, an AI playground,
the knowledge review workflow, embeddings, the food catalogue and USDA sync, background jobs,
system health and audit logs. Sign-in requires an authenticator app (TOTP).

```bash
cd frontend/NutriPath.Admin && npm ci && npm run dev   # proxies /api to http://localhost:5092
cd backend/NutriPath.Api && dotnet run -- admin bootstrap   # first SuperAdmin
```

See [Admin portal](docs/admin-portal.md) for security, roles, API key encryption, setup and rollback.

## Multilingual assistant (English, Tamil, Sinhala)

The assistant answers in English, Tamil or Sinhala, including code-mixed and
transliterated questions. Retrieval uses the multilingual embedding model
`intfloat/multilingual-e5-small` (local Python worker in
`infrastructure/embeddings/`) against the reviewed English knowledge base, and
answers come from Groq (default) or Google Gemini (`gemini-3.5-flash-lite`),
selected with `AI:Provider`. The chat screen has a reply-language selector.

Gemini's free tier may use submitted data to improve Google's products, so a
privacy guard limits Gemini to synthetic `@nutripath.test` accounts unless
real-user processing is explicitly approved. Keep `Gemini:ApiKey` in user
secrets or the `Gemini__ApiKey` environment variable.

See [Multilingual AI architecture](docs/multilingual-ai.md) for setup,
configuration, reindexing, evaluation results, privacy notes and rollback.

## Authentication deployment

- On iOS and Android the app keeps both tokens in Expo SecureStore. On web the
  access token lives in memory and the API stores the refresh token in a
  host-only, HttpOnly cookie scoped to `/api/auth`. The first load of the
  updated web app removes legacy tokens from localStorage. Users from the
  previous web flow may need to sign in again.
- Set `EXPO_PUBLIC_API_BASE_URL` to the HTTPS API URL for production builds.
  Configure the API's exact browser frontend origins with
  `WebAuth__AllowedOrigins__0`, `WebAuth__AllowedOrigins__1`, and so on, with
  no trailing slash. The legacy `AllowedOrigin` setting still accepts one
  origin. The API uses the same allowlist for credentialed CORS and CSRF
  validation. Cookie-authenticated login, refresh, and logout also require
  the `X-NutriPath-Web` header, which browser forms cannot supply.
- The production API requires HTTPS and always sets a Secure refresh cookie.
  For TLS termination outside the API, configure the trusted proxy IPs using
  `Https__KnownProxies__0` and subsequent indices, and have the proxy set
  `X-Forwarded-Proto`. The API uses `SameSite=Lax` by default. For truly
  cross-site HTTPS deployments, set `WebAuth__CrossSiteCookie=true` to use
  `SameSite=None; Secure`. Browsers may block those cookies, so deploy the
  frontend and API on the same site when possible.
- For local Expo web development, use `http://localhost:8081` with
  `http://localhost:5092`. The API also allows Expo's legacy
  `http://localhost:19006` origin in Development. Use the same hostname on
  both sides and add any alternate frontend URL explicitly.
- The JWT signing secret comes from `Jwt__Key` in production. Defaults are
  15 minutes for access tokens and 30 days for refresh tokens, configurable
  through `Jwt__AccessTokenMinutes` and `Jwt__RefreshTokenDays`.

Logout blocks new authenticated requests immediately. The app waits for an
active rotation and asks the API to revoke the latest refresh token before
discarding local credentials. Browsers with Web Locks serialize cookie
updates across tabs on the same frontend origin. If the request fails, local logout still
completes and server revocation is unconfirmed. Web keeps a non-secret
sign-out marker and retries revocation before cookie restoration on the next
load when browser storage is available. The app cannot expire an HttpOnly
cookie while the API is unreachable.

The database stores one current refresh-token hash per account. Signing in
again replaces the previous refresh credential. Rotation rejects older
credentials, and the database checks the current hash when updating it.
The model has no history to identify replay or revoke a replacement token
based only on an older credential. An already-issued access JWT remains
valid until it expires, including in another tab. No database migration is
needed.
