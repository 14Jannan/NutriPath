# NutriPath Admin Portal

A web portal for operating NutriPath: user accounts, administrators, AI providers and API keys,
the nutrition knowledge base, the food catalogue, background jobs, system health and the audit log.

- Frontend: `frontend/NutriPath.Admin` (Vite, React 19, TypeScript, Tailwind CSS 4, TanStack Query and Table, Radix UI, Recharts)
- Backend: the existing API, under `/api/admin/*` (`backend/NutriPath.Api/Controllers/Admin`, `Services/Admin`)
- Database: additive migration `AddAdminPortal`
- Tests: `tests/NutriPath.Api.Tests/AdminPortalTests.cs`, `AdminManagementTests.cs` (backend) and `frontend/NutriPath.Admin/src/test` (Vitest)

## Architecture

```mermaid
flowchart LR
  subgraph Browser
    P[Admin portal SPA]
  end
  subgraph Phone
    M[NutriPath mobile app]
  end
  P -- "/api/admin/* (same origin)" --> API
  M -- "/api/*" --> API
  API[ASP.NET Core API] --> DB[(PostgreSQL + pgvector)]
  API --> E[Embedding worker]
  API --> G[Gemini / Groq]
  API --> U[USDA FoodData Central]
  W[BackgroundJobWorker] --> DB
  W --> E
  W --> U
```

The portal is a static single-page app. It talks to the same API process as the mobile app, but
through a separate set of endpoints, a separate token audience and a separate authorization model.
Every page is also protected on the server: hiding a menu item is a convenience, not a control.

### Serving it

Serve the portal and the API from **one origin** (for example `https://admin.nutripath.example`
serving `dist/` for `/` and proxying `/api` to the API). The refresh cookie is `SameSite=Strict`
and scoped to `/api/admin/auth`, so no CORS configuration is needed. In development, Vite proxies
`/api` to `http://localhost:5092` (override with `NUTRIPATH_API_URL`).

```bash
cd frontend/NutriPath.Admin
npm ci
npm run dev        # http://localhost:5173
npm run build      # static files in dist/
```

Set `Admin:PortalUrl` on the API to the portal's public origin so invitation links point at it.

## Security model

```mermaid
sequenceDiagram
  participant B as Browser
  participant A as API
  B->>A: POST /api/admin/auth/login (email, password)
  A-->>B: MFA ticket (5 min) [+ enrolment secret on first sign-in]
  B->>A: POST /api/admin/auth/mfa/verify (ticket, TOTP or recovery code)
  A-->>B: access token (10 min, in memory) + HttpOnly refresh cookie (session ≤ 12 h)
  B->>A: GET /api/admin/... Authorization: Bearer
  Note over A: audience, security stamp, session, suspension and permission checked on every request
  B->>A: POST /api/admin/auth/refresh (cookie + X-NutriPath-Admin: 1)
  A-->>B: new access token (refresh token rotated)
```

| Control | How |
| --- | --- |
| Separate admin tokens | Audience `NutriPath.Admin`, scheme `AdminBearer`. Consumer tokens are rejected by admin endpoints and vice versa. |
| MFA is mandatory | TOTP (RFC 6238, SHA-1, 30 s, 6 digits, ±1 step) with replay prevention. Admin tokens are issued only after the second factor. The QR code is rendered in the browser; the secret is never sent to a third party. |
| Recovery codes | 10 single-use codes, stored as SHA-256 hashes, shown once. |
| Step-up for sensitive actions | Replacing, rolling back or revoking API keys and regenerating recovery codes need a second factor within the last 15 minutes (`reauth_required` → password + TOTP dialog). |
| Token storage | Access token only in memory. Refresh token in an `HttpOnly`, `Secure` (on HTTPS), `SameSite=Strict` cookie limited to `/api/admin/auth`. Nothing in `localStorage` except the theme and the sidebar state. |
| CSRF | Refresh and logout need the `X-NutriPath-Admin: 1` header and a same-origin or allow-listed `Origin`. |
| Instant revocation | A security stamp (`sv` claim) is checked on every request (cached 15 s). Suspension, role changes, MFA reset, password reset and session revocation take effect at once. |
| Brute force | Lockout after 5 failed sign-ins for 15 minutes; the auth rate limit applies to admin sign-in too. |
| Safe administration | Nobody can change their own roles or suspend themselves; the last active SuperAdmin can't be removed; only SuperAdmins grant SuperAdmin; consumer accounts can't be promoted (administrators use separate accounts, created by invitation or the CLI). |
| Audit | Append-only `AdminAuditLogs`: actor, roles, action, resource, outcome, correlation ID, reason. Denied attempts are logged. Secrets, passwords and health data are never written. CSV exports neutralise spreadsheet formulas. |
| Privacy | The portal shows account metadata and usage counts, not meal logs or assistant conversations. AI metrics store provider, model, language, tokens, latency and outcome only. The playground uses fictional profiles only. |

### API keys (Gemini, Groq, USDA)

- Keys pasted in the portal are tested against the provider with a free request (fixed, allow-listed URLs) and only a working key is stored and activated. A failing key leaves the current key in place.
- Stored keys are encrypted with **AES-256-GCM**. Each ciphertext is bound to its provider and version as associated data. Encryption keys are versioned and come from configuration, separate from the chat-message key:

  ```text
  Credentials:EncryptionKeys:1 = <base64 of 32 random bytes>
  Credentials:ActiveKeyVersion = 1
  ```

  Generate one with `openssl rand -base64 32`. Keep it in user secrets or the deployment secret store, never in `appsettings.json` or the repository.
- Only the last four characters are ever returned. Keys never appear in logs, the audit log, the frontend bundle or error messages.
- Precedence: a portal-managed key wins over the environment key (`Gemini:ApiKey`, `Groq:ApiKey`, `Usda:ApiKey`). Once a provider has any portal-managed key, the environment key isn't used again, so revoking a leaked key can't silently fall back to an old one.
- History supports reactivating an earlier version (tested first). Revoked versions can never be reactivated.

**Rotating the encryption key:** add `Credentials:EncryptionKeys:2`, set `ActiveKeyVersion` to 2, then replace each API key in the portal (new versions are encrypted with key 2). Remove key 1 only after no active or inactive credential uses it.

## Roles and permissions

The matrix is defined in code (`Models/Admin/AdminRoles.cs`) and shown read-only on the Roles page.

| Permission | SuperAdmin | UserAdmin | AIManager | NutritionReviewer | SupportAdmin |
| --- | :-: | :-: | :-: | :-: | :-: |
| dashboard.read | ✓ | ✓ | ✓ | ✓ | ✓ |
| users.read | ✓ | ✓ | | | ✓ |
| users.manage, users.suspend, users.revoke_sessions, users.export | ✓ | ✓ | | | |
| admins.read, admins.manage, admins.assign_roles | ✓ | | | | |
| ai.providers.read, ai.providers.manage, ai.models.manage, ai.evaluate | ✓ | | ✓ | | |
| ai.secrets.rotate | ✓ | | | | |
| ai.usage.read | ✓ | ✓ | ✓ | | |
| ai.usage.manage | ✓ | ✓ | | | |
| knowledge.read, knowledge.edit | ✓ | | ✓ | ✓ | |
| knowledge.create, knowledge.reindex | ✓ | | ✓ | | |
| knowledge.review, knowledge.approve, knowledge.archive | ✓ | | | ✓ | |
| foods.read, foods.manage, foods.sync | ✓ | | | ✓ | |
| system.monitor, system.jobs.manage | ✓ | | ✓ | | |
| system.settings.manage | ✓ | | | | |
| audit.read | ✓ | ✓ | | | |
| audit.export | ✓ | | | | |

## Knowledge workflow

```mermaid
stateDiagram-v2
  [*] --> Draft
  Draft --> PendingReview: submit
  PendingReview --> Approved: approve (not the author)
  PendingReview --> Rejected: reject (reason)
  Rejected --> PendingReview: edit + submit
  Approved --> Draft: revise → new version
  Approved --> Archived: archive (reason)
  Archived --> Draft: restore as new draft
```

Only Approved documents are retrieved by the assistant. Approving queues an indexing job; approving
a revision archives the version it supersedes. Content can be imported from `.txt`, `.md` or `.json`
(max 256 KB). Chunks are previewed and duplicates detected before saving.

## Runtime AI configuration

Provider, models, Gemini generation settings, languages and default usage limits can be changed at
runtime. Each save is a new version (optimistic concurrency) with history and rollback, and applies
to new requests within seconds. Blank fields use the deployment defaults. The privacy routing that
decides which users may reach free-tier Gemini is **not** editable in the portal. It only changes
through deployment configuration after a privacy review.

## Setting up the first administrator

1. Apply the migration (back up first):

   ```bash
   dotnet ef database update --project backend/NutriPath.Api
   ```

2. Configure `Credentials:EncryptionKeys:1`, `Credentials:ActiveKeyVersion` and `Admin:PortalUrl`.
3. Create the first SuperAdmin from the server (refused once one exists):

   ```bash
   cd backend/NutriPath.Api
   dotnet run -- admin bootstrap        # prompts for email and password (min 12 characters)
   # non-interactive: NUTRIPATH_ADMIN_EMAIL=... NUTRIPATH_ADMIN_PASSWORD=... dotnet run -- admin bootstrap
   ```

4. Sign in to the portal, scan the QR code with an authenticator app and save the recovery codes.
5. Invite other administrators from **Administrators → Invite** and send the one-time link through a trusted channel.

Other CLI commands: `admin list`, `admin grant EMAIL ROLE`, `admin reset-mfa EMAIL` (for a locked-out SuperAdmin).

## Mobile app behaviour

If an administrator account signs in to the mobile app, the app shows an **Administrator Access**
screen ("You are using an administrator account. Please access the NutriPath Admin Portal to securely
manage the application.") with **Open Admin Portal** and **Sign out**. The check uses `isAdmin` from
the server on sign-in and on every session restore. The screen is rendered outside the navigator,
so deep links and notifications can't open consumer screens. The portal URL comes only from
`EXPO_PUBLIC_ADMIN_PORTAL_URL` and must be HTTPS (plain HTTP is accepted only for local hosts in
development builds).

## Rollback

- **Frontend:** stop serving `frontend/NutriPath.Admin/dist`. Nothing else depends on it.
- **Backend:** the migration is additive (new tables and nullable or defaulted columns). To roll back the code, deploy the previous build; the extra columns are ignored. To remove the schema, run `dotnet ef database update AddPreferredLanguage` (the previous migration) **after a backup**. This drops admin roles, sessions, audit logs, managed API keys and knowledge workflow metadata.
- **API keys:** if a portal-managed key misbehaves, reactivate the previous version from **API keys**. Environment keys are not used again once a provider has portal-managed keys.

## Not implemented yet

These parts of the original specification are not built. The portal only lists modules that work.

- PDF knowledge import (no stable, permissively licensed PDF parser chosen yet)
- Evaluation runs UI, prompt version management, terminology (lexicon) management
- Feedback review and safety incident queues (and the mobile feedback UI they need)
- Notifications service, feature flags, general system settings page
- Account deletion and data-export request workflows
- Playwright end-to-end tests and screenshots
