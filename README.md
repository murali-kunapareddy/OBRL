# OBRL: Online Book Reading Library

A private, login-only online library. Members read books in a built-in reader and can't download them.
It's built with ASP.NET Core MVC on .NET 10 and PostgreSQL, and runs on one Ubuntu server.

Phases 1 and 2 (this version) include:

- **Sign-in:** login only, accounts are created by admins, and everyone must set up a strong sign-in before using the library: a **passkey** (fingerprint, face or device PIN, phishing-resistant) or an **authenticator app** (password + code). Members can have both, and up to 10 passkeys. A member with only passkeys cannot sign in with the password alone. One active session per account, sign-out after inactivity (Admins choose 5 to 60 minutes; scrolling, typing and page turns count as activity, and a notice comes one minute before), lockout after 5 failed attempts, and passwords that appear in leak lists are refused.
- **Disclaimer:** users must accept it before reading. Admins edit it, and every change creates a new version that users must accept again.
- **Roles:**
  - **User:** reads, subscribes and bookmarks.
  - **Contributor:** also uploads books.
  - **Moderator:** also manages categories and all books, approves uploads, and moderates comments and the message board.
  - **Admin:** also manages users, roles, the disclaimer and the activity log.
- **Categories:** nested to any depth. Subscribing to a category includes all its sub categories. Users can also subscribe to single books. Subscriptions are free.
- **Uploads:** PDF, DOC, DOCX, ODT, RTF, EPUB, TXT, MD, plus MOBI, AZW3 and FB2 when Calibre is installed.
- **Reader:**
  - **Page mode** (PDF and Word): watermarked page images with zoom, fit to width, fit to page and full screen.
  - **Text mode** (EPUB and TXT): reflowing text with font size, font choice, line spacing and width, plus a table of contents.
  - **Both modes:** bookmarks with notes, automatic resume, light, sepia and dark themes, keyboard and swipe navigation.
- **Dashboard:** the latest message board topics, continue reading, bookmarked books, new books in your subscriptions, most read this month (distinct readers over 30 days) and top rated (a weighted average, so one 5-star vote does not beat many 4-star votes).
- **Book approval:** books from Contributors wait in the Moderator's review queue before readers see them. A rejected book goes back to the uploader with a note, and editing it resubmits it. Moderator and Admin uploads go live straight away, and Moderators can take any book down.
- **Ratings and comments:** readers of a book rate it from 1 to 5 stars and comment, with one level of replies. Members delete their own comments and report others. Reports land in the review queue, where a Moderator keeps or hides the comment.
- **Message board:** every member can start topics and reply. Moderators pin, lock and hide topics and replies.
- **Search:** PostgreSQL full-text search over title, author and description (web-search syntax such as `"exact phrase"` and `-word`), plus partial matches in title and author.
- **Anti-spam:** posting, rating and reporting are limited to 10 a minute and 120 an hour per member. Member text is always shown as plain text.
- **History:** sign-ins, failed sign-ins, uploads, books opened, subscriptions, bookmarks, ratings, comments, reports, approvals, board posts, moderator actions, rate-limit hits and access denials go to the `ActivityEvents` table.

Phase 3 adds analytics.

## How books are protected

| Measure | What it does |
| --- | --- |
| Encrypted storage | Every stored file (originals, converted PDFs, text sections, images, page cache) is AES-256-GCM encrypted. The key is in `/etc/obrl/master.key`. |
| No file ever sent | The browser gets one page image or one text section at a time. There is no PDF, EPUB or DOCX to save or open elsewhere. |
| Per-user watermark | Each page image has the reader's email, account id and the time printed on it. Text mode overlays the reader's name. |
| Signed page links | Page and image links are tied to one user, one book and one page, and expire after 60 seconds. |
| Access check on every request | Pages are only served for books the user is subscribed to. |
| Rate limits | 60 pages per minute and 600 per hour per user in the app, plus a per-IP limit in nginx. |
| No copy, print or right-click | This is only a deterrent. |

**Limits:** no website can stop screenshots or a phone camera, and the watermark is what makes a leak traceable.
Text-mode books (EPUB, TXT) are real HTML in the page, so a skilled user can copy text with browser developer tools.
Page-mode books are images only.

## Run it locally (development)

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and PostgreSQL 16 or newer. LibreOffice is needed for Word files, and Calibre is optional.

```bash
# 1. Database (example for a local PostgreSQL)
sudo -u postgres psql -c "CREATE ROLE obrl LOGIN PASSWORD 'obrl-dev' CREATEDB;"
sudo -u postgres createdb -O obrl obrl_dev

# 2. Run (applies migrations and creates the dev admin from appsettings.Development.json)
cd src/Obrl.Web
dotnet run
```

Open http://localhost:5102 and sign in as `admin@obrl.local` / `Dev-only reading pass 2026`.
You'll set up an authenticator app (any TOTP app works) or add a passkey, and accept the disclaimer. Passkeys need a secure origin: `http://localhost` works for development (not `127.0.0.1`), and production needs HTTPS.
Local data (encrypted books and the dev master key) goes to `.data/`, which git ignores.

```bash
dotnet test        # from the repository root
```

## Deploy to an Ubuntu VM

You need a VM with 2 vCPU and 4 GB RAM, with disk of about 3 times the size of your book collection.
You also need a DNS name pointing at it.

```bash
# On the server, from a copy of this repository:
sudo DOMAIN=books.example.com ADMIN_EMAIL=you@example.com bash deploy/install.sh

# From your machine (needs the .NET 10 SDK and SSH access):
deploy/publish.sh you@books.example.com
```

`install.sh` does the following:

- Installs the ASP.NET Core runtime, PostgreSQL, nginx, certbot, LibreOffice (headless), Calibre, ufw, fail2ban and restic.
- Creates the `obrl` system user and the folders `/var/lib/obrl` (data), `/etc/obrl` (settings and keys) and `/opt/obrl` (app).
- Generates the master key, the database password and the first admin password. It prints the admin password once.
- Installs the systemd service, the nightly backup timer and the nginx site, then gets an HTTPS certificate.

After the first sign-in, remove the `Obrl__Seed__*` lines from `/etc/obrl/obrl.env`.

**Back up `/etc/obrl/master.key` somewhere off the server.** Without it, no stored book can be read.
`deploy/backup.sh` runs nightly. It dumps the database and, if you set `RESTIC_REPOSITORY` and `RESTIC_PASSWORD` in `/etc/obrl/backup.env`, copies the database, books and keys into an encrypted restic repository.

Useful commands:

```bash
systemctl status obrl
journalctl -u obrl -f
sudo systemctl start obrl-backup     # run a backup now
```

## Languages

The interface is available in English, Telugu (తెలుగు) and Kannada (ಕನ್ನಡ). Members pick a language from the
switcher in the top bar (or under the sign-in form). The choice is saved on their account and follows them to every
device they sign in on; before signing in, the browser's language is used. Book content is never translated.

Text lives in `src/Obrl.Web/Resources/SharedResource.{code}.resx`. Each entry's name is the English text, so English
needs no file and anything not yet translated falls back to English. Views use `L["..."]` for plain text and `H["..."]`
for text that contains HTML.

**To add a language** (Hindi, for example):

1. Copy `Resources/SharedResource.te.resx` to `Resources/SharedResource.hi.resx` and replace every value
   (Visual Studio's resource editor shows both columns). Keep `{0}`-style placeholders and HTML tags as they are.
2. Add `new("hi", "हिन्दी")` to `Languages.All` in `src/Obrl.Web/Localization/Languages.cs`.
3. Run `dotnet test`: it lists any entries the new file is missing and any translation that lost a placeholder or tag.

When you add new interface text, wrap it in `L["..."]` and add the entry to every language file; the same test
catches anything left out. Text that reaches the interface at run time (Identity's own messages, script text, role
names) is listed in `Localization/ExtraText.cs`.

## Configuration

Set these in `appsettings.json`, or as environment variables in `/etc/obrl/obrl.env` (use `__` instead of `:`).

| Setting | Default | Meaning |
| --- | --- | --- |
| `ConnectionStrings:Default` | | PostgreSQL connection string |
| `Obrl:StorageRoot` | `/var/lib/obrl` | Encrypted books, covers, page cache, data protection keys |
| `Obrl:KeyFile` | `/etc/obrl/master.key` | 32-byte master key (base64). Created on first start if missing. |
| `Obrl:MaxUploadMegabytes` | `200` | Largest upload |
| `Obrl:PageWidths` | `800, 1200, 1600` | Widths pages are rendered at |
| `Obrl:PageUrlLifetimeSeconds` | `60` | How long a signed page link works |
| `Obrl:ReaderRequestsPerMinute` / `PerHour` | `60` / `600` | Per-user reader limits |
| `Obrl:IdleTimeoutMinutes` | `30` | Sign-out after inactivity, until an Admin picks 5, 10, 15, 30 or 60 minutes under Admin > Settings (the Admin's choice is stored in the database and wins) |
| `Obrl:PasskeyServerDomain` | empty | Domain passkeys are bound to, e.g. `library.example.org`. Empty uses the site's host name. Set it before members add passkeys: changing it later makes their passkeys stop working |
| `Obrl:LibreOfficePath` / `CalibreConvertPath` | `soffice` / `ebook-convert` | Conversion tools |
| `Obrl:Seed:AdminEmail` / `AdminPassword` | | Creates the first admin when none exists |

## Project layout

```
src/Obrl.Web/
  Controllers/        MVC controllers (Library, Books, Reader, Uploads, Categories, Admin, ...)
  Domain/             Entities and roles
  Data/               EF Core DbContext, migrations, seeding
  Localization/       Interface languages, translated validation and Identity messages
  Resources/          Translations (SharedResource.{language}.resx)
  Security/           Session rules (single session, required 2FA, disclaimer), sign-in manager, password check
  Services/           Access rules, encryption, signed URLs, storage, activity log
  Services/Ingest/    Upload conversion (LibreOffice, Calibre, EPUB/TXT import) and the background worker
  Services/Rendering/ Page rendering (PDFium), watermarking and covers (SkiaSharp)
  Views/, wwwroot/    Razor views, themes (css/site.css), reader (js/reader.js, css/reader.css)
tests/Obrl.Tests/     xUnit tests
deploy/               install.sh, publish.sh, systemd units, nginx config, backup script
```

**Themes:** each theme is one block of CSS variables at the top of `wwwroot/css/site.css` (Light, Dark and Sepia).
To add a theme, copy a block, change the colours, and add a button with `data-theme-choice`.

## Open-source components

| Component | Licence | Use |
| --- | --- | --- |
| .NET, ASP.NET Core, EF Core | MIT | Runtime, web app, data access |
| Npgsql | PostgreSQL | PostgreSQL driver |
| PostgreSQL | PostgreSQL | Database |
| PDFtoImage, PDFium | MIT, BSD-3/Apache-2 | PDF page rendering |
| SkiaSharp | MIT | Watermarks, covers |
| VersOne.Epub | Unlicense | EPUB reading |
| HtmlSanitizer, AngleSharp | MIT | Cleaning EPUB HTML |
| Bootstrap, Bootstrap Icons | MIT | UI |
| qrcode-generator | MIT | Authenticator QR code |
| Inter, Literata | SIL OFL 1.1 | Fonts |
| SecLists NCSC password list | MIT | Leaked-password check |
| LibreOffice | MPL-2.0 | Word to PDF (separate process) |
| Calibre | GPL-3.0 | MOBI/AZW3/FB2 to EPUB (separate process, optional) |
| nginx | BSD-2 | Reverse proxy |
