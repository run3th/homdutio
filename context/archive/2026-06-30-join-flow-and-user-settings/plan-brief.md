# Join-Household Flow + User Settings — Plan Brief

> Full plan: `context/changes/join-flow-and-user-settings/plan.md`

## What & Why

Deliver two designed features from the updated templates: a redesigned invite **join-household flow** (three clear screens instead of utilitarian branches) and **user settings** (edit display name, upload/remove a profile photo). Today the join page is functional-but-plain, names show only as initials, and there's no way to set a photo or change your name after signup.

## Starting Point

The invite/join backend and a `/join/:token` page already exist and branch on auth + membership. Display names live only on `ApplicationUser` and are resolved at fetch time everywhere (cards, comments, members) — never denormalized. There is **no avatar storage of any kind**, and the header/menu show the user's email, not their display name.

## Desired End State

Invitees get a branded join experience that names the inviter and the household and adapts to logged-out / ready-to-join / already-a-member. Users can open Settings from the avatar menu to rename themselves and set a cropped profile photo, and that name + photo appear everywhere they show up — header, menu, cards, comments, members, and the join inviter badge — with renames propagating to existing cards/comments automatically.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Avatar storage & serving | Image bytes in Azure SQL + anonymous cached `GET /api/users/{id}/avatar` | No new infra; `<img>` works without a bearer token; versioned URLs make the 4s poll cache-cheap | Plan |
| Photo capture UX | Draggable zoom/crop via `ngx-image-cropper` | Lets users frame their face; outputs a small resized blob | Plan |
| Sequencing | One change, join flow first, then settings + avatar | Ships the safe restyle early; avatars light up everywhere once the heavy phase lands | Plan |
| Identity surface | Promote DisplayName (+ avatar) everywhere; extend `/api/auth/me` | Consistent identity; matches how cards/members already use DisplayName | Plan |
| Invite preview | Add inviter name + inviter id (avatar ref) | Drives both join screens per the design | Plan |
| Rename propagation | Free — names resolved at fetch, nothing denormalized | No migration/backfill needed | Research |
| Testing depth | Backend endpoint tests + key Angular component specs | Covers risky contracts + stateful UI without standing up E2E | Plan |

## Scope

**In scope:** three join screens (`joinLoggedOut`/`join`/`joinTaken`); inviter in the preview; a shared `UserAvatarComponent`; Settings modal (name + photo); profile-update, avatar upload/remove/serve endpoints; avatar column + migration; versioned `avatarUrl` on five DTOs; avatar rendering on all user surfaces.

**Out of scope:** Azure Blob/CDN; server-side image processing; changes to invite-mint/accept behavior; multi-household; real-time push; gravatar; any historical-text backfill (none needed).

## Architecture / Approach

A reusable `UserAvatarComponent` (img-when-URL, colored-initial otherwise) is introduced in Phase 1 and adopted on every surface, so Phase 3 is a single swap to real URLs. Avatars are stored as bytes on `ApplicationUser` and served by an anonymous endpoint with ETag/`Cache-Control`; DTOs carry a **versioned** `/api/users/{id}/avatar?v=<version>` so caching is aggressive and a new upload busts it. Display-name edits need no propagation work because names are already resolved per-fetch.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Join flow redesign | 3 join screens + inviter in preview + shared avatar component | Correct screen selection across auth/membership states |
| 2. Display-name editing | Settings modal (name) + DisplayName/avatar in `/api/auth/me` + header/menu | `/me` contract change; current-user state wiring |
| 3. Avatar upload & everywhere | Avatar column/migration, upload/serve/delete, versioned `avatarUrl` on 5 DTOs, cropper UI, render everywhere | New file-upload/serving path; cross-surface rendering; cache-busting |

**Prerequisites:** none beyond the running app + dev DB; `ngx-image-cropper` added in Phase 3.
**Estimated effort:** ~3 sessions, one per phase.

## Open Risks & Assumptions

- Anonymous avatar GET exposes a user's photo to anyone holding their GUID — accepted (GUIDs aren't enumerable; invitees were deliberately invited).
- Storing image bytes in SQL is fine at household scale; the upload size cap + client resize keep rows small.
- Other users see name/photo changes on the next 4s poll, not instantly (no push) — acceptable.
- Single-household model is unchanged; "already a member" means "already has a household".

## Success Criteria (Summary)

- All three join states render per the design and route correctly.
- A user can rename themselves and set/remove a cropped photo from Settings, reflected across every surface.
- Renames reach existing cards/comments on the next fetch; removed photos fall back to initials; cached photos don't flicker and fresh uploads bust the cache.
