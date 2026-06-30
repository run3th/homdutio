---
change_id: join-flow-and-user-settings
title: Invite join-household flow screens + user settings (display name & profile photo)
status: archived
created: 2026-06-30
updated: 2026-06-30
archived_at: 2026-06-30T20:11:36Z
---

## Notes

I modified auth and pro templates, we need implement adjustments:

Auth — join-household flow (Homdutio Auth Pro)

joinLoggedOut — invite landing for a logged-out user: inviter + household, "Log in to continue", "No account? Create one".
join — logged-in user ready to join: inviter avatar + house badge, "Accept & join {household}", "No thanks, maybe later".
joinTaken — already a member: calm (non-error) "You're already in" + "Go to your board".
New tweakable props: startScreen, household, inviter.

App — user settings (Homdutio Pro)

"Settings" item added to the avatar menu (above Log out).
Settings modal: edit display name + upload / remove profile photo (with preview).
Uploaded avatar shows everywhere the user appears (header, menu, cards, comments, members); renaming also updates existing cards and comments.
