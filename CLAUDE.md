# CLAUDE.md

Guidance for working in this repository. Keep it in sync with the code: update it whenever architecture, interfaces or conventions change.

## What MatNotes is

A multi-user notes app. Notes live in **folders** and have a **type**: rich text, plain text, markdown, Word (DOCX), Excel (XLSX), PDF or any other file. Notes can carry **tags**,
can be **exported** in other formats and can **link to other notes** (when a note is exported, the linked notes are copied into it). Notes and folders are **shared** with one
user (read or edit) or with anyone who has a link (anonymous, read only, optional password and expiry). Sign-in is local, with optional **TOTP** (authenticator app + recovery
codes). An administrator manages accounts and the **backups** (schedules with retention, to a folder or straight to a NAS over SMB).

**Privacy is the product.** A user never sees another user in the UI: no user list, no autocomplete, no avatars of others. Sharing with a user needs their exact user name. An
administrator manages accounts but has **no access to the notes of others** (`AccessService`). A shared note shows the display name of the sharer, nothing else.

Stack: ASP.NET Core on **.NET 10**, C#, **Razor Pages**, EF Core + **PostgreSQL**, the **MatUi** toolkit of the sister project MatDesign (CSS + one script + an icon sprite,
copied into `wwwroot/matui`, plain JavaScript, no build step). Docker-first. Web port **2301**. UI language: English.

## Build / run / test

- **Build:** `dotnet build MatNote.slnx` (note: `.slnx`). SDK 10 (`global.json`). Expected: 0 warnings, 0 errors.
- **Tests:** `dotnet test` (xunit; no Docker needed for the unit tests).
- **Run / test the app: always rebuild the container and redeploy the stack** (project rule): `./scripts/redeploy.ps1` (`-Clean` also removes the volumes: empty database, setup
  assistant again). Dev stack: `docker-compose.dev.yml` = `matnote` + `postgres` + `pgadmin`; app on **http://localhost:2301**, pgAdmin on 2302 (no sign-in, database password
  `matnote`). CSS, JS and cshtml are baked into the image: rebuild after a change. The dev network has an explicit subnet (`10.253.23.0/24`): this PC runs out of Docker address pools.
- **Release stack:** `docker-compose.yml` = `matnote` + `postgres`, image `ghcr.io/real-ttx/matnote`, two volumes (`matnote-data`, `matnote-db`). Keep it that simple.
- **Real tests are end to end against the container** (Playwright with the installed Edge, see the `screenshots` skill): look at the screenshot instead of trusting computed styles.
  Check 390 px (phone) and dark mode for every new page. Git Bash: `MSYS_NO_PATHCONV=1` for arguments that start with `/`. Write files with the editor tools, not with heredocs that
  contain apostrophes or backticks.
- **Migrations:** `dotnet ef migrations add <Name> -p src/MatNote -o Data/Migrations` (a design-time factory exists: no database needed). They are applied at start-up.

## Map

```
src/MatNote
  Program.cs                 composition root: services, cookie auth, razor pages conventions, middleware
  Infrastructure.cs          DatabaseInfo, HealthCheck (--healthcheck for Docker), Database.MigrateWithRetry, SetupRedirect, ForcePasswordChange, SecurityHeaders (CSP: no inline script/style),
                             MaintenanceService (old sessions, trash), EmptyStringBinderProvider
  Data/                      Entities.cs (all tables), AppDbContext.cs (PascalCase tables, audit columns, soft delete in SaveChanges), Migrations/
  Services/Config/           AppPaths (/data/config, keys, tmp), ConfigStore<T> (one JSON file), AppConfig + SecretProtector + Clock
  Services/Auth/             CurrentUser, PasswordService, TotpService (RFC 6238 + recovery codes + QR), SessionService (sessions in the DB, cookie carries the token), LoginService, UserService
  Services/AccessService.cs  who may see/change what (owner, shared read/edit, link shares), FolderTree
  Services/Export/           contracts of the exporter/preview (Contracts.cs) and their implementation: every text note is read once into a small document model (Doc*.cs) and written to txt, md, html, rtf, docx, pdf;
                             links to other notes are inlined on that model (only through the NoteResolver: never a note the person may not read); NotePreview = sanitizer (HtmlSanitizer), RenderText, Docx/Xlsx previews, search text;
                             INoteFileStore reads file bytes and tag names; PDF generation is serialized by a lock (QuestPDF breaks when two run at once); registered by AddExport()
  Services/Backup/           backups: BackupConfig (config/backup.json: targets, schedules), IBackupTarget (FolderTarget, SmbTarget over SMBLibrary), Retention (pure), ScheduleCalculator (friendly fields -> cron,
                             next run), BackupArchive (zip: manifest.json, database.dump, config/, keys/), BackupService (one gate for backups AND restore), BackupScheduler (30 s loop), RestoreService
                             (pg_restore --single-transaction); registered by BackupRegistration.AddBackup(); pages in Pages/Admin/Backups (Backups, Runs, Schedules/, Targets/, Restore)
  Services/                  Ui (icons, logo, helpers), MenuService (folder tree of the menu), Assets (sprites, hash), ListModels (toolbar, pager), Features.cs (DI registration)
  Pages/Notes/Index          the list. Layout "split" (default, from 1100 px up): list at the left, the note in an <iframe name=note-pane> at the right; layout "list": a page per note (the cookie mn.layout,
                             set by ?layout=). The frame loads Notes/View, Notes/Edit or Notes/Pane with ?embed=1: _ViewStart then picks _LayoutEmbed (no menu). AppPageModel.Embed/EmbedValue/EmbedQuery keep
                             embed=1 in addresses and redirects (Edit.cshtml also posts a hidden embed field, because a button with its own formaction loses the query). embed.js sends the list a
                             postMessage after each load (which note is shown): app.js marks the row, keeps ?id= and fetches the rows again. Links that leave the note get target=_top.
                             autosave.js (Edit, text notes): posts the form to the handler Autosave after 1.2 s, JSON back; a new note is created by the first save with a title; Stamp (microseconds of
                             UpdateDate) refuses to overwrite a newer save (409). The Save button stays.
  Controls/                  ONE code base per control: <mn-field>, <mn-buttons>, <mn-table>, <mn-tabs>/<mn-tab>; partials _Toolbar, _Pager, _FormSummary in Pages/Shared
  Pages/                     Razor Pages: Account/ (sign-in, TOTP step, setup, account, security), Notes/, Folders/, Tags/, Shares/, Trash/, Public/ (link shares, anonymous), Admin/ (Users/, Backups/, Settings/ = the System page; the folder is not called System because the namespace would hide System.*)
  wwwroot/matui/             the toolkit (copied from MatDesign: never edit by hand, update by copying again)
  wwwroot/css/app.css        the accent (orange) and the few app-only styles (prefix mn-);  wwwroot/js/app.js, editor.js: the few app-only behaviours
tests/MatNote.Tests          unit tests
```

## Conventions

**Database (PostgreSQL = logic, JSON files in /data/config = configuration)**
- Tables are PascalCase and singular (`User`, `UserSession`, `Note`), the key is always `Id` BIGINT, every record has `CreateDate`, `CreateUserId`, `UpdateDate`, `UpdateUserId`
  (filled by `AppDbContext.SaveChanges`). A record with `UpdateState` is deleted softly (0 deleted, 1 created, 2 updated): `db.Remove(x)` becomes `UpdateState = 0`, a query filter hides it.
  A table with a secret in its address has a `Token` (uuid), e.g. `UserSession.Token`, `Share.Token`.
- Dates are UTC in the database and shown with `Clock.Format` (zone from `TZ`/the System page).

**Security**
- Every query for notes goes through `AccessService` (`VisibleNotesAsync`, `FindNoteAsync`). A note the person may not see answers 404, exactly like one that does not exist.
- The CSP forbids inline script and inline style; `matui.js` needs none. Rich text is sanitized on save and on render. Uploaded files are served as downloads (only PDF inline, with `sandbox`).
- Passwords: PBKDF2 (ASP.NET `PasswordHasher`); TOTP secrets and NAS passwords are protected with the data protection keys (`/data/keys`); sessions are rows in `UserSession`.

**UI (MatDesign rules, see `../MatDesign/CLAUDE.md` and its `docs/ADOPTING.md`)**
- Pages are built only from `mat-*` classes: no inline styles, no inline scripts, no event-handler attributes, no literal colours outside `app.css`'s accent. App-only classes start with `mn-`.
- CRUD = a list page and a **separate** edit page (no modal editing). A list has its **toolbar above** (search, filter, sort: `_Toolbar` + `ToolbarModel`), the table, the **pager directly under**
  it (`_Pager`), then the list actions left-aligned (`mat-actions`). **Delete lives on the edit page**, separated by `mat-btn-row__spacer`. Buttons: positive to negative (`Save`, `Back`, space, `Delete`) via `<mn-buttons>`.
- Every `td` has `data-label` (rows become cards on a phone). Use icons (`Ui.Icon("name")`, names in `wwwroot/matui/matui-icons.svg` and `wwwroot/img/app-icons.svg`; a missing name is a bug).
- Dependent fields only appear when needed (`show-if` / `data-show-if`). Account, sign out and the theme button are at the bottom left of the menu.
- Controls have one code base and can appear several times on a page (ids come from the model: `ToolbarModel.Id`, `mn-field id=`).
- Mobile first: check 390 px. Scrollbars are the toolkit's (visible only while scrolling).

**Backups**
- A backup file is `matnote_<scheduleId>_<yyyyMMdd-HHmmssZ>.zip` (`BackupFileName`; every name from a page is checked against it). Retention only touches the files of its own schedule. The archive can hold the
  data protection keys (`IncludeKeys`): then it holds everything and the target must be protected like the server. A restore makes a safety backup first (schedule id `prerestore`, never deleted by retention).
- `pg_dump`/`pg_restore` are version 17 from the PGDG repository (the Ubuntu base image only has 16, which refuses a 17 server). Non-default SMB ports work through a subclass of `SMB2Client`.
- A form with a second submit handler (`formaction="?handler=Test"`) needs `asp-page="..."` on the form: otherwise after that post the form's own action keeps the handler.

**Code**
- Microsoft C# guidelines, file-scoped namespaces, `.editorconfig`. Not too many nested types; several business functions in one class are fine. English identifiers and comments.
- Posted texts are never `null` (`EmptyStringBinderProvider`); ask `string.IsNullOrWhiteSpace`.

**Git**
- Commit and push **under the user's name**; never add a Claude/Anthropic co-author trailer or "generated with" line (global rule of the user; it overrides any default).
- Branches: `main` = release, `dev` = development. Versions: release `<major>.<minor>.<build>-<date>`, development `nightly-<build>-<date>`, local `local-<date>` (`Directory.Build.props`,
  `.github/workflows/build.yml` builds and pushes the image to ghcr.io).
