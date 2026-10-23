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
                             UpdateDate, sent as text: too big for a script number) refuses to overwrite a newer save (409). The Save button stays. The split page is full width (no max width) and its list is
                             a compact <ul class="mn-notelist"> (the list layout keeps the table). In the pane (no cards) the edit mode mirrors the view, top to bottom: pane bar (title: an input in edit mode, form="note-form";
                             autosave state, Save now, mode switch, Delete), details (view: type/folder/tags; edit: folder/tags/favorite in one line), ribbon (view: the action buttons; edit: the formatting bar), text.
                             mn.mode (cookie, set from the pane by app.js) = the mode the next note opens in. Links: editor.js has [[ (list after the cursor), Ctrl+click / the little bar over a link (open,
                             remove), chips for plain text. "Linked notes in the text" (View) is rendered by the server where the export puts them (INotePreview.RenderWithLinkedAsync, cookie mn.inline).
                             contextmenu.js (every page with the menu): right click / menu key on a note row or a folder entry of the menu opens a .mat-menu with what the pages already offer (open, edit, share,
                             favorite, new subfolder ...): it decides nothing, the server checks every address. A row says what is allowed with data-own / data-can-edit / data-fav / data-file = "true" (Razor writes
                             data-x="" for a null, so test the value, never hasAttribute). filedrop.js (Notes/Index): "Upload files" and files dropped on the list make one note per file in the open folder (the same POST as
                             the new-note form, Kind=File). Attachments: any number of files per note (tables NoteAttachment + NoteAttachmentFile, AttachmentService; page Notes/Attachment = download GET, handlers
                             Upload/Delete, JSON; Public/Attachment for link shares that allow exports); partial _Attachments under the text in View and Edit (same place), attachments.js uploads (button or drop on
                             the page) and removes; a note has at most 50, each up to the upload limit; always delivered as a download. Exports take them along when asked (ExportOptions.IncludeAttachments, checkbox "Include the attachments" / the switch of the folder export): one note becomes a zip (the file + attachments/), a folder zip gets "<Note> - attachments/" next to each note; the attachments of linked notes that are copied in are not included. "Copy to clipboard" in the export dialog (app.js, data-export-copy; also in the link share) fetches the same export as text (Txt, Markdown, Html, Rtf, Csv, Json; hidden for Word/PDF) and copies it; a web page is copied formatted (body as text/html, whole page as text/plain). A note can have any number of link shares (each its own address, password, end,
                             label): Shares/Edit lists them with copy buttons.
                             Own notes from read-only ones (a shared collection of snippets stays read-only and is never copied): compose.js ("Add to a note" in the note ribbon and the right-click menu) puts a LINK to the
                             note at the end of one of the person's own text notes or of a new one (POST Notes/Reference, JSON; the target must be the person's own text note, Lookup?mine=true lists them); the page and
                             the export show the current text of the snippet where the link is. The target picked last is offered first (localStorage mn.compose.target).
  Controls/                  ONE code base per control: <mn-field>, <mn-buttons>, <mn-table>, <mn-tabs>/<mn-tab>; partials _Toolbar, _Pager, _FormSummary in Pages/Shared
                             A link to a FOLDER (Public/Share) looks like a file explorer: folder tree of the share at the left (_PublicTree, only folders inside the shared one; hidden below 900 px), an address bar, then
                             the content of the open folder, folders first, then the notes (.mn-files; the type and date move under the name on a phone); a note opens beside the same tree. ?folder= outside the share falls back to its top.
                             Folder export / import (Folders/Export, Folders/Import; Services/Export/FolderArchive + Services/FolderImporter): "MatNotes archive" = the readable zip (notes as .html/.md/.txt, files as they are,
                             "<Note> - attachments/") + matnotes-folder.json (ids, parents, types, tags, favorites, attachments). FolderArchive.Read turns ANY zip into an ImportPlan (pure, tested: with the manifest it
                             restores, without it directories = folders, files = notes; limits: 20000 entries, 1 GB, one file up to the upload limit; names with .. or a leading dot are refused); FolderImporter writes the
                             plan as NEW notes of the signed-in person (SaveAsync: sanitized, searchable, tags), links point at the new ids in a second step, links out of the archive lose their target, a folder deeper
                             than 12 levels is merged into the one above, a plain zip with loose files goes into a folder named like the zip. Dates are not kept (the import is a new save).
                             Drag OUT (dragout.js): a note row or a folder of the menu starts a drag with DownloadURL (Chromium saves the file on the desktop / in a folder: a text note as .html/.md/.txt through
                             Notes/Export?raw=true, a file note as itself, a folder as <name>.matnotes.zip through Folders/Export?download=true&mode=Archive) and the address as text/uri-list. Dropped on a list
                             (filedrop.js) a .matnotes.zip is imported there, other files become notes. Between two MatNotes instances the file on the desktop is the vehicle (a page cannot read the other instance).
                             Parameters: {{name}} / {{name|default}} in a text (Services/Export/Parameters.cs, params.js). Copy (the text as shown, linked notes included), export as a file (the dialog asks, the values go
                             as p_<name>=value into ExportOptions.Parameters and are written into every format incl. the copied-in notes) and Copy to clipboard ask for the values first (last values offered again,
                             localStorage mn.params; cancel = nothing happens). A note without placeholders is not touched; a placeholder split by formatting (bold inside) is not found. The export dialog opens with Markdown
                             and remembers format, linked and attachments (localStorage mn.export). The rich text editor (editor.js) keeps the kind of the block whose text stays when a deletion joins two blocks, and Backspace
                             at the start of a heading/quote/code block makes it a paragraph.
                             Duplicate (Services/DuplicateService, duplicate.js; POST Notes/Duplicate, Folders/Duplicate, JSON): only what the person OWNS (a shared note is built on with links, not copied). A note: "(copy)" after the
                             title (then "(copy 2)" ...), same folder, tags, attachments, file bytes; a folder: the road of an export + import kept in memory (FolderArchive + FolderImporter with RootName and KeepOtherLinks: links
                             inside the folder point at the copies, links to other notes stay). Buttons: note ribbon, folder page, right-click menu, and in the edit mode the pane bar (an icon) and the button row of the page (duplicate.js saves what is unsaved first: window.mnSaveNow; a note that was new takes its id from body[data-note-id]). Paste in the rich text editor (editor.js pasteHtml) keeps the formatting in the editor's own terms: the tags of NoteSanitizer (bold, italic, underline, strike, h1-h4, lists, quote, code, links http(s)/mailto and note links, tables, hr); a style of Word/Google Docs (font-weight, font-style, text-decoration) becomes the tag, the <b style="font-weight:normal"> wrapper of Google Docs is no bold, h5/h6 become h4; fonts, colours, classes, pictures, scripts go. Ctrl+Shift+V pastes plain text. The Copy button of a rich text note copies the DESIGN too (text/html + text/plain,
                             app.js copyRich: links lose the embed layout; without the clipboard api the selection of an offscreen copy is copied).
                             History (Services/NoteVersionService + VersionPolicy, Pages/Notes/History; table NoteVersion): the state a save of a TEXT note replaces is kept as a version when the policy says so - the first
                             change, somebody else is about to replace it, a pause of 5 minutes came before this change (= a new sitting), 30 minutes since the last snapshot; never twice within a minute; NOT at every
                             autosave. Kept: 80 automatic + 50 saved-on-purpose per note. A person can save a version with a name, compare one with the text now (line diff of the plain text, TextDiff), see how it
                             looked and put it back (what is there now is kept first as "before a restore"; the usual save runs, so editors see a 409 and reload). Only for someone who may edit; "who" is You / the
                             owner by name / "Another editor" (editors never learn each other's names; the owner sees names). History is a TAB (and an icon in the pane bar), never a button.
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
- **Tabs or buttons?** What is a part of one item and has a page of its own is a **tab** (`_ItemTabs`: a note has View | Edit | Sharing, a folder Folder | Sharing; in the note pane the same three are the switch of
  the pane bar). What only does something (copy, export, favorite, download, delete, back) is a **button**. Never a button that really opens another aspect of the item.
- A card with `mat-card--flush` (a list or table runs edge to edge) has NO padding in its body: text in it (an empty message) needs `mn-pad` on the body, or a `mat-empty` block.
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
