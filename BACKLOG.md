# Backlog

What is still to do. Three kinds: **Feature**, **Bug**, **Other**. Move an entry to the commit message (and delete it here) when it is done.
Priority: **P1** soon, **P2** next, **P3** some day.

## Bug

| Prio | What | Notes |
|---|---|---|
| P3 | The menu list scrolls by about 40 px at a window height of 900 px, and nothing shows that there is one more item (*System*) below | Reachable by scrolling (checked); a fade or a smaller gap would make it visible. The scroll region is part of the toolkit (`mat-nav__scroll`): change it in MatDesign |

## Feature

| Prio | What | Notes |
|---|---|---|
| P2 | Search inside the content of notes (not only titles) | The search text of every note already exists (`NotePreview`); needs an index or `ILIKE`/full text search in PostgreSQL |
| P2 | Mail: password reset and a note of a new share | Needs an SMTP setting on the System page; today an administrator sets a new password |
| P3 | Version history of a note | Restore an older text |
| P3 | Import: a folder or a zip of files/markdown at once | The reverse of the folder export |
| P3 | Autosave for the title and tags of file notes (Word, Excel, PDF) | Today they have the Save button only |
| P3 | Keep the toolbar count and the unread state of the list in step while typing in the pane | The rows refresh after each save; the count in the toolbar is updated, filters are not re-applied to a title that no longer matches |
| P3 | Notes can be moved between folders by drag and drop | Today through the edit page |
| P3 | Sign in with an external provider (OIDC) | Local accounts and TOTP stay |
| P3 | Offline / PWA (the manifest exists, no service worker yet) | |

## Other

| Prio | What | Notes |
|---|---|---|
| P1 | Create the GitHub repository, push `main` and `dev`, check the first run of the GitHub action | `.github/workflows/build.yml` never ran; the compose file in the README needs the image `ghcr.io/real-ttx/matnote` |
| P1 | Make the package `ghcr.io/real-ttx/matnote` public (or add a pull secret) | Otherwise `docker compose up` cannot pull it |
| P2 | Test a restore of a backup on a second, empty machine (with and without the keys) | Verified in the dev stack and against Samba, not yet across machines |
| P2 | Check the licence of QuestPDF (Community) for the intended use | Named in the README |
| P2 | Check the side by side layout in dark mode and between 1100 and 1300 px, and with a PDF, Word and Excel note in the pane | Tried with rich text, plain text and a new note at 1360 px (light), a phone, and the list layout |
| P2 | Tests for the autosave handler (stamp conflict, new note with and without title) and for the layout cookie | Only checked by hand in the browser so far |
| P3 | Tests in the GitHub action (`dotnet test`) before the image is built | 482 tests pass locally |
| P3 | Update `README.md` screenshots when the UI changes | `docs/images`, see the `screenshots` skill |
