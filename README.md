<div align="center">

<img src="docs/images/logo.png" width="360" alt="MatNotes" />

**Your notes, in folders – private by default, shared when you decide.**

Rich text, plain text, Markdown, Word, Excel, PDF. Tags, links between notes, export in many formats,
sharing with one person or by link, two-step verification and backups straight to your NAS.
One container plus PostgreSQL, no cloud, no third-party services.

</div>

![The note list with the open note beside it](docs/images/split.png)

---

## What this is about

Prompts you reuse, snippets, meeting notes, the Word file with the contract next to the PDF of the
invoice: things you want to find again and sometimes copy. MatNotes keeps them on your own server,
in folders, with tags, and lets you hand a single note or a whole folder to somebody else – without
ever showing anyone a list of the other people who use the app.

## At a glance

**Notes**
- **Folders** that nest – the menu on the left is the tree of your folders – and **tags** that cut across them
- Every note has a **type**: *rich text* (a small built-in editor), *plain text* (exactly what you typed – ideal for
  prompts), *Markdown*, *Word (.docx)*, *Excel (.xlsx)*, *PDF* or any other file
- Upload a `.txt`, `.md` or `.html` file and it becomes a text note; Word and Excel files get a preview, PDFs are shown in the page
- **Links between notes**: type `[[` and a few letters to link a note, Ctrl + click (or the little bar over the link) opens it, and when you read a note, the button *Linked notes in the text*
  **shows them right where the export would put them**. When you export a note, the linked notes are **copied into the export** at the place of the link –
  but only notes the reader may see
- **List and note side by side, like OneNote**: on a wide window the note opens right next to the list, switchable between *view* and *edit*, and text notes are **saved while you type**. One click switches to the plain list, a phone always gets the list with a page per note
- **Favorites**, search, filters and sorting on every list, a **trash** with 30 days of grace

**Export**
- One note as `.txt`, `.md`, `.html`, `.rtf`, `.docx`, `.pdf` or `.json`; a workbook as `.csv`; a whole folder as a zip
- One engine for all formats, so the result looks the same everywhere
- **History** of a text note (also, and especially, a shared one): MatNotes keeps earlier states when somebody else changes the note, after a break and every half hour – not at every autosave. Compare an old version with the text now, save a named version, put an old one back (undoable)
- **Duplicate** a note (with its tags and attachments) or a whole folder – in the right-click menu, in the note ribbon or on the folder page. **Copy** keeps the design of a rich text note (headings, bold, lists, links) when you paste it into a mail or a document
- **Parameters:** write `{{name}}` or `{{name|default}}` in a note (a prompt with a place for the text it should work on): when the note is copied or exported, MatNotes asks for the values and writes them in – also for the notes that are linked into it. The export dialog opens with Markdown and remembers your last choice
- Attachments go along (a note becomes a zip with an `attachments` directory), and the text can be copied to the clipboard
- **Folder export and import:** a folder with everything inside as readable files, or as a **MatNotes archive** that comes back as it was – types, tags, favorites, attachments and the links between the notes. The import also takes any plain zip: directories become folders, files become notes
- **Drag and drop out of the browser** (Edge, Chrome): drag a note or a folder from MatNotes onto the desktop or into a folder – you get the file (a text note as .html/.md/.txt, a folder as a `.matnotes.zip` archive). Drop such a file on the list of another MatNotes (or the same) and it is imported there

**Sharing and privacy**
- Share a **note or a folder** (with everything inside it)
- With **one user** by their exact user name – read, or read and edit
- With **anyone who has a link** (the anonymous role): read only, optional **password**, optional **end date**, optional ban on downloads
- **A user never sees another user**: no user list, no suggestions, no avatars of others
- **Administrators manage accounts but cannot read the notes of others** – enforced in one place (`AccessService`), covered by tests

**Sign-in and operations**
- Local accounts, roles *Administrator* and *User*, **TOTP** two-step verification with any authenticator app plus recovery codes
- Lock after repeated failures, rate limit per address, sessions you can see and end – and they **survive a restart of the container**
- **Backups** on a schedule (hourly, daily, weekly, monthly or any cron expression) to a **folder or straight to a NAS over SMB**,
  with **retention** (keep the last N, one per day, week and month), history, download and **restore**
- **Light, dark or system theme**, four colour schemes per user, a phone layout, a scrollbar that only shows while you scroll

## Screenshots

### The list with the note beside it

| Reading | Editing, saved while you type |
|---|---|
| ![A note beside the list](docs/images/split.png) | ![The editor beside the list](docs/images/split-edit.png) |

Pick a note and it opens at the right, in the mode you worked in last; *View* and *Edit* switch the mode in place and nothing moves. Type `[[` to link another note; while
reading, *Linked notes in the text* puts each linked note under the paragraph that links it (left picture). A note has the tabs *View*, *Edit* and *Sharing*; a folder *Folder* and *Sharing*. The editor saves after a short pause and says so; if somebody else changed the same
note in the meantime, it stops instead of overwriting their text. The two buttons at the top right choose between this layout and the plain list (remembered per browser).

### Notes and the folder tree

![A folder with its notes](docs/images/folder.png)

Notes sit in folders; tags, favorites and the trash are one click away. A number next to a folder or a tag counts its notes.

### A note, and the editor

| A note with a link to another note | The rich text editor |
|---|---|
| ![A note](docs/images/note.png) | ![The editor](docs/images/editor.png) |

Links to other notes are part of the text. A plain text note stays exactly as typed, with a *Copy* button for prompts.

### Export

![Export a note](docs/images/export.png)

Pick a format, and decide whether the linked notes are copied in.

### Sharing

| With a user or by link | A link with password and end date |
|---|---|
| ![Share a note](docs/images/share.png) | ![A link share](docs/images/share-link.png) |

| Shared by me | Shared with me (what the other person sees) |
|---|---|
| ![Shared by me](docs/images/shares.png) | ![Shared with me](docs/images/shared-with-me.png) |

You find everything you shared on one page and can stop a share at any time. The person you shared with sees the display name of the sharer – and nobody else.

### Two-step verification

![TOTP setup with a QR code](docs/images/totp.png)

Scan the QR code with any authenticator app. Ten recovery codes are shown once and stored only as hashes.

### Administration

| Users | System |
|---|---|
| ![User management](docs/images/users.png) | ![System page](docs/images/system.png) |

Administrators create accounts, reset passwords and see *how many* notes a person has – never what is in them.

### Backups

| Backups and history | Schedules with retention |
|---|---|
| ![Backups](docs/images/backups.png) | ![Schedules](docs/images/schedules.png) |

### Trash, light theme, dark theme and phone

| Trash | Light | Dark |
|---|---|---|
| ![Trash](docs/images/trash.png) | ![Light theme](docs/images/light.png) | ![Dark theme](docs/images/note-dark.png) |

| Phone | Phone menu | Sign-in |
|---|---|---|
| ![A list on a phone](docs/images/phone-list.png) | ![The menu on a phone](docs/images/phone-menu.png) | ![Sign-in](docs/images/login.png) |

## Quick start

Ready-made images are published to the GitHub Container Registry:

| Tag | Built from | Use it for |
|---|---|---|
| `ghcr.io/real-ttx/matnote:latest` (also `:release`) | `main` | releases |
| `ghcr.io/real-ttx/matnote:<major>.<minor>.<build>-<yyyyMMdd>` | `main` | one fixed release, e.g. `0.1.3-20261018` |
| `ghcr.io/real-ttx/matnote:nightly` | `dev` | the newest features |

The images are public (no sign-in to pull) and exist for `linux/amd64` and `linux/arm64`.

### 1. Just run it

Copy this into `docker-compose.yml` and start it – nothing else needed:

```yaml
name: matnote

services:
  matnote:
    image: ghcr.io/real-ttx/matnote:latest
    container_name: matnote
    restart: unless-stopped
    ports:
      - "2301:2301"
    environment:
      TZ: Europe/Berlin
      MATNOTE_DB: "Host=postgres;Port=5432;Database=matnote;Username=matnote;Password=${MATNOTE_DB_PASSWORD:-matnote}"
    volumes:
      - matnote-data:/data
    depends_on:
      postgres:
        condition: service_healthy

  postgres:
    image: postgres:17
    container_name: matnote-db
    restart: unless-stopped
    environment:
      POSTGRES_DB: matnote
      POSTGRES_USER: matnote
      POSTGRES_PASSWORD: ${MATNOTE_DB_PASSWORD:-matnote}
    volumes:
      - matnote-db:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U matnote -d matnote"]
      interval: 5s
      timeout: 5s
      retries: 20

volumes:
  matnote-data:
  matnote-db:
```

```bash
docker compose up -d
```

Open **http://localhost:2301**. There is no default account: the first visit starts the **setup assistant**, which asks for the
name and password of the administrator. Health check: `/health`. The two volumes keep everything (see below), so an update is just
`docker compose pull && docker compose up -d`.

The database is **not published** – only the app can reach it – so the default password `matnote` is only visible inside the stack.
To use another one, start with `MATNOTE_DB_PASSWORD=... docker compose up -d` (or put it into a `.env` file) **before the first start**.

| Service | What | Port |
|---|---|---|
| `matnote` | the app (ASP.NET Core, Razor Pages) | **2301** |
| `postgres` | the database (PostgreSQL 17) | – |

### 2. With backups to your NAS

Backups are made by the app itself (*Administration › Backups*), so there is nothing else to install. A **target** is where they go:

**A NAS over SMB – no mount needed.** Choose *SMB share* as the target and enter host, share, user and password (a Synology, TrueNAS,
a Windows share, Samba). The password is stored encrypted with the data protection keys.

**A folder** – a disk of this machine, or a NAS that is mounted into the container. Add one line to the `matnote` service and choose *Folder* with the path `/backups`:

```yaml
    volumes:
      - matnote-data:/data
      # Left of the colon is your machine or NAS, right of it what MatNotes sees.
      - /srv/matnote-backups:/backups
```

| System | Shares are usually under | Example line |
|---|---|---|
| Synology | `/volume1` | `- /volume1/backups/matnote:/backups` |
| TrueNAS SCALE | `/mnt/<pool>` | `- /mnt/tank/backups/matnote:/backups` |
| Unraid | `/mnt/user` | `- /mnt/user/backups/matnote:/backups` |
| QNAP | `/share` | `- /share/Backups/matnote:/backups` |
| plain Linux | anywhere | `- /srv/matnote-backups:/backups` |

A **schedule** says what runs when, to which target and how long a backup is kept. One backup is **one zip**: the database (`pg_dump`),
the JSON configuration and – if you tick it – the encryption keys. Those keys are needed to read two-step secrets and NAS passwords after a
restore on **another machine**; the archive then contains everything, so protect the target like the server. *Run now* makes a backup at once,
*Restore* brings one back (after a safety backup of the current state, which retention never deletes).

### 3. Behind a reverse proxy

Put Caddy, nginx or Traefik in front and set the **public address** on *Administration › System*, so that shared links get the right
address. `X-Forwarded-For`, `-Proto` and `-Host` are understood. Terminate TLS at the proxy.

### 4. From source

```bash
docker compose up -d --build                              # release stack, built from this folder
docker compose -f docker-compose.dev.yml up -d --build    # development stack (with pgAdmin)
```

### Settings that matter

| Variable | Default | Meaning |
|---|---|---|
| `TZ` | `Europe/Berlin` (in the compose file) | Time zone of dates and of the backup schedules |
| `MATNOTE_DB` | – (set in the compose file) | Connection string of the PostgreSQL database |
| `MATNOTE_DB_PASSWORD` | `matnote` | Used by the compose files for both the app and the database |
| `MATNOTE_DATA` | `/data` | Data directory (configuration, keys) |
| `ASPNETCORE_URLS` | `http://+:2301` | Address inside the container |

Everything else is set in the app, on *Administration › System*: whether people may create an account themselves, how long a session lasts,
the largest file, how long the trash keeps a note, the sign-in lock, the public address.

### The volumes

```
matnote-data  →  /data
├─ config/       app.json, backup.json (JSON configuration)
├─ keys/         data protection keys (sessions and secrets survive restarts)
└─ tmp/          uploads in progress, export and backup staging

matnote-db    →  /var/lib/postgresql/data   (the database)
```

PostgreSQL holds the logic: `User`, `UserSession`, `Folder`, `Note`, `NoteFile`, `Tag`, `NoteTag`, `NoteLink`, `Share`, `BackupRun` – PascalCase,
key `Id` (BIGINT), every row with `CreateDate`, `CreateUserId`, `UpdateDate`, `UpdateUserId`, deletable rows with `UpdateState` (0 deleted, 1 created, 2 updated).
Back up **both** volumes – or let the app do it on a schedule.

## Development

Building and testing happens through the container:

```powershell
./scripts/redeploy.ps1          # rebuild the image and redeploy the dev stack: always do this to test a change
./scripts/redeploy.ps1 -Clean   # the same with empty volumes: the setup assistant again
```

The dev stack (`docker-compose.dev.yml`) is `matnote` + `postgres` + **pgAdmin** (http://localhost:2302, no sign-in, database password `matnote`).
`dotnet build MatNote.slnx` and `dotnet test` need only the .NET 10 SDK; the unit tests need no container.
[CLAUDE.md](CLAUDE.md) describes the architecture and the conventions.

## How it is built

- **ASP.NET Core 10** with **Razor Pages**, **EF Core** on **PostgreSQL 17**, migrations applied at start-up
- The UI is the **MatUi** toolkit of [MatDesign](../MatDesign) (CSS, one script, an icon sprite), plain JavaScript, no build step; a strict
  content security policy without inline script or style
- Passwords with PBKDF2, TOTP (RFC 6238) implemented in the app, secrets protected with the data protection keys, sessions stored in the database
- One export engine (document model → txt, md, html, rtf, docx, pdf, json, csv) with [QuestPDF](https://www.questpdf.com) (Community licence – check that it applies to you),
  `HtmlSanitizer`, `PdfPig`, `ClosedXML` and the Open XML SDK
- Backups with `pg_dump`/`pg_restore` 17, targets for a folder and for SMB (`SMBLibrary`), retention as a pure, tested function
- The toolkit files in `src/MatNote/wwwroot/matui` are copied from MatDesign – update them by copying again, never edit them

## Branches & versioning

| Branch | Purpose | Version |
|---|---|---|
| `main` | Release | `<major>.<minor>.<build>-<yyyyMMdd>` |
| `dev` | Development | `nightly-<build>-<yyyyMMdd>` |
| local | – | `local-<yyyyMMdd>` |

`Major` and `Minor` live in [`Directory.Build.props`](Directory.Build.props), the build number comes from the GitHub action (`github.run_number`).
A push to `main` or `dev` builds the image and publishes it to the GitHub Container Registry. The version is shown at the bottom of the menu.
