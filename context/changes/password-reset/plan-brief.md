# Password Reset — Plan Brief

> Full plan: `context/changes/password-reset/plan.md`

## What & Why

Let a registered user who has forgotten their password recover access (FR-020 / roadmap S-08):
request a reset email, then set a new password from a time-limited link. This is the single
permitted v1 transactional-email use — every other email path is a PRD non-goal — so it is built
deliberately narrow (reset-only) to avoid becoming a general email surface.

## Starting Point

The auth backend (`src/Homdutio.Api/Auth/AuthEndpoints.cs`) has register/login/refresh/logout/me on
a stateless JWT + rotating-refresh-token design, with a deliberate anti-enumeration posture (auth
failures return bare 401s). There is **no reset path and no email capability at all** — no
`IEmailSender`, no email SDK package, no email config — and Identity is registered **without**
`AddDefaultTokenProviders()`, so reset-token generation would throw today. The Angular SPA has
mature auth conventions (interceptor allowlist, `mapValidationProblem`, register→login
notice+prefill hand-off) to build on.

## Desired End State

From `/login`, "Forgot password?" leads to an email-request screen that always shows the same
generic confirmation. A matching account receives an Azure Communication Services email linking to a
same-origin `/reset-password?email=…&token=…` page valid for 1 hour. Setting a new password (Identity
policy enforced) revokes every active session for that account and redirects to `/login` with a
"Password updated — please log in" notice and the email prefilled. Unknown emails, expired/invalid
tokens, and email-send failures never leak whether an account exists.

## Key Decisions Made

| Decision                       | Choice                                              | Why (1 sentence)                                                            | Source |
| ------------------------------ | --------------------------------------------------- | --------------------------------------------------------------------------- | ------ |
| Email provider                 | Azure Communication Services Email                  | Switched from SendGrid 2026-06-25; first-party Azure, managed-domain sender, managed-identity auth (no third-party account, no connection string). | User 2026-06-25 |
| Token mechanism                | Identity built-in DataProtector token               | Stateless, no migration, standard path, self-validating + self-expiring.    | Plan   |
| Token lifetime                 | 1 hour                                              | Limits exposure of a leaked/forwarded reset link.                           | Plan   |
| Unknown-email response         | Always 200, generic message                         | No account enumeration — matches the existing login/refresh stance.         | Plan   |
| Sessions on reset              | Revoke all refresh-token families                   | A reset (often post-compromise) must boot any attacker session.             | Plan   |
| Post-reset UX                  | Redirect to `/login` (notice + prefilled email)     | Reuses the register→login pattern; confirms the new credential works.       | Plan   |
| Email-send failure             | Log, still return 200                               | Preserves enumeration safety; no background-job infra in the stack.         | Plan   |
| Abuse protection               | Built-in fixed-window rate limiter on the endpoint  | First-party middleware caps email bombing / ACS send quota; no new dep.     | Plan   |
| Reset-link base URL            | Configurable `AppBaseUrl` (not Host-derived)        | Same-origin in prod, env-portable, avoids Host-header redirect spoofing.    | Plan   |

## Scope

**In scope:** Azure Communication Services `IEmailSender` abstraction + dev/test fake + config; `AddDefaultTokenProviders`
+ 1-hour lifetime; `POST /api/auth/forgot-password` (always-200) and `POST /api/auth/reset-password`
(revoke sessions on success); fixed-window rate limit; SPA request + set-new-password screens,
routing, login link, interceptor allowlist, shared password validator; integration + vitest tests.

**Out of scope:** logged-in "change password"; email confirmation / invite / notification emails;
a general email framework; background jobs; custom email-domain auth; prod ACS resource/DNS provisioning;
a custom reset-token table.

## Architecture / Approach

Backend: a scoped `IEmailSender` (ACS impl + no-op fake selected when no endpoint is set) behind
which the endpoints compose a reset link from `AppBaseUrl`. `forgot-password` generates an Identity
reset token only for existing users, Base64Url-encodes it into the link, sends, and always returns
200. `reset-password` decodes the token, calls `ResetPasswordAsync`, and on success uses the existing
`RefreshTokenService` family-revocation to log out all sessions. The SPA adds two unguarded
components that consume these endpoints with the established interceptor/validation conventions.

## Phases at a Glance

| Phase                          | What it delivers                                          | Key risk                                                        |
| ------------------------------ | --------------------------------------------------------- | --------------------------------------------------------------- |
| 1. Email infrastructure        | ACS `IEmailSender` + fake + config, no UI behavior        | Secret hygiene; making the live sender cleanly fake-swappable.  |
| 2. Backend reset endpoints     | Both endpoints + token provider + rate limit + revocation | Forgetting `AddDefaultTokenProviders`; token URL-encoding; enumeration leaks. |
| 3. SPA reset flow              | Request + set-password screens, routing, login link       | Interceptor allowlist omission; query-param token handling; ≤400px UX. |

**Prerequisites:** S-01 (done). An ACS resource + Email resource + connected domain + the host
managed identity granted access to the ACS resource (Entra ID auth, no connection string) are an
operational prerequisite for end-to-end manual testing (not for build/automated tests, which use a fake).
**Estimated effort:** ~2–3 sessions across 3 phases (well-trodden Identity + SPA patterns; the one
genuinely new thing is the ACS Email integration).

## Open Risks & Assumptions

- **ACS deliverability / managed domain** — emails sent from the Azure-managed domain may land in
  spam without a custom verified domain; accepted for v1, revisit only if deliverability bites.
- **`AddDefaultTokenProviders` is a hard runtime prerequisite** — reset throws without it; covered in
  Phase 2 and the Critical Implementation Details.
- **Reset token is not URL-safe** — must Base64Url-encode in the link and decode in the handler; a
  mismatch shows as a generic "invalid token" and is easy to misdiagnose.
- **IP-based rate limiting is coarse** — shared NAT/proxy clients share a bucket; threshold chosen to
  be forgiving for humans while still capping abuse.

## Success Criteria (Summary)

- A user can request a reset, receive an ACS email, set a new password from the link, and log in.
- Unknown emails, expired/invalid tokens, and send failures all yield the same generic responses
  (no account enumeration), and a successful reset logs out all other sessions.
- `dotnet test` and `npm test` are green across the new endpoints, session-revocation, rate limit,
  and both SPA components.
