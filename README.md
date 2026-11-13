# OBRL: Online Book Reading Library

A private, login-only online library. Members read books in a built-in reader and can't download them.
It's built with ASP.NET Core MVC on .NET 10 and PostgreSQL, and runs on one Ubuntu server.

Version 1.1 includes:

- **Sign-in:** login only, accounts are created by admins, and everyone must set up a strong sign-in before using the library: a **passkey** (fingerprint, face or device PIN, phishing-resistant) or an **authenticator app** (password + code). Members can have both, and up to 10 passkeys. A member with only passkeys cannot sign in with the password alone. One active session per account, sign-out after inactivity (Admins choose 5 to 60 minutes; scrolling, typing and page turns count as activity, and a notice comes one minute before), lockout after 5 failed attempts, and passwords that appear in leak lists are refused.
- **Disclaimer:** users must accept it before reading. Admins edit it, and every change creates a new version that users must accept again.
- **Roles:**
  - **User:** reads, subscribes (up to 5 books at a time) and bookmarks.
  - **Contributor:** also uploads books, and reads their own books with no subscription and no end date.
  - **Moderator:** also manages categories and all books, approves uploads, and moderates comments and the message board.
  - **Admin:** also manages users, roles, the disclaimer and the activity log.
- **Categories:** nested to any depth. Members follow categories (a category subscription): the newest books of a followed category and all its sub categories show on their dashboard.
- **Book subscriptions:** to read a book, a member subscribes to it. Subscriptions are free. Users and Contributors can hold up to 5 at a time (`Obrl:MaxSubscribedBooks`), and each lasts 30 days, which Admins change under Admin > Settings (7, 14, 30, 60 or 90 days, or no end date). When a subscription ends or the member unsubscribes, the slot is free again, and an ended book can be subscribed to again. Contributors read the books they uploaded without a subscription; those never end and never use a slot. Moderators and Admins read every book.
- **Uploads:** PDF, DOC, DOCX, ODT, RTF, EPUB, TXT, MD, plus MOBI, AZW3 and FB2 when Calibre is installed.
- **Covers:** the uploader can add their own cover image (JPEG, PNG or WebP). After choosing it, a crop tool opens: move and zoom the picture inside a 2:3 frame, turn, straighten, mirror, and adjust brightness, contrast and colour. The same tool crops a page of the book (the "Crop and adjust" button next to the page number). Without one, they can pick a page of the book as the cover (PDF and Word books; the edit page shows pictures of the pages to pick from once the book is converted). If they do neither, the first page is used, and EPUB and text books use the e-book's own cover or one made from the title. The cover can be changed at any time from the edit page.
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

**[docs/DEPLOY.md](docs/DEPLOY.md) is a complete step-by-step guide** (SSH, securing the VM, installing, first sign-in,
storage on another server over NFS, SMB or SFTP, backups and updates). The short version:

You need a VM with 2 vCPU and 4 GB RAM, with disk of about 3 times the size of your book collection.
You also need a DNS name pointing at it.

```bash
# On the server, from a copy of this repository:
sudo DOMAIN=books.example.com ADMIN_EMAIL=you@example.com bash deploy/install.sh

# Then build and start it on the server (install.sh installs the .NET SDK for this):
sudo bash deploy/update.sh

# Or build on your machine instead (needs the .NET 10 SDK and SSH access):
deploy/publish.sh you@books.example.com
```

`deploy/update.sh` is also how you update later: it gets the newest code from git, builds it, dumps the database,
keeps the running version in `/opt/obrl.previous` and restarts. `sudo bash deploy/update.sh --rollback` goes back.

`install.sh` does the following:

- Installs the .NET 10 SDK (or only the ASP.NET Core runtime with `WITH_SDK=no`), PostgreSQL, nginx, certbot, LibreOffice (headless), Calibre, ufw, fail2ban and restic.
- Creates the `obrl` system user and the folders `/var/lib/obrl` (data), `/etc/obrl` (settings and keys) and `/opt/obrl` (app).
- Generates the master key, the database password and the first admin password. It prints the admin password once.
- Writes `/etc/obrl/obrl.env`, with `Obrl__PasskeyServerDomain` and `AllowedHosts` set to your domain.
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

## Keep the library on another server

Like Immich, OBRL keeps its library as plain folders, so the library can sit on a storage server with plenty of
space while the app and database stay on the VPS. Everything in it except the cover thumbnails is AES-256-GCM
encrypted with the master key, which stays on the VPS, so the storage server only ever holds unreadable files.

**Recommended: an NFS share** (or SMB/CIFS if the storage server is a Windows or Synology box), mounted on the VPS
and set as `Obrl:StorageRoot`, with the page cache kept on the VPS's own disk. It needs no code or app changes, and
a low-latency link (same data centre, or a private network such as WireGuard or Tailscale) keeps reading fast.
Books are read once per page and then served from the local page cache.

1. On the storage server, export a folder to the VPS only, for example in `/etc/exports`:
   `/srv/obrl-library  10.0.0.5(rw,sync,no_subtree_check,root_squash)`, then `exportfs -ra`.
   Make the folder owned by the same numeric user and group id as `obrl` on the VPS (`id obrl` shows them).
2. On the VPS:

   ```bash
   sudo apt install nfs-common            # cifs-utils for SMB
   sudo mkdir -p /mnt/obrl-library
   # /etc/fstab
   storage.example.lan:/srv/obrl-library  /mnt/obrl-library  nfs4  rw,hard,noatime,_netdev,nofail  0 0
   # SMB instead:
   # //storage.example.lan/obrl  /mnt/obrl-library  cifs  credentials=/etc/obrl/smb.cred,uid=obrl,gid=obrl,file_mode=0640,dir_mode=0750,_netdev,nofail  0 0
   sudo mount /mnt/obrl-library
   ```

3. Stop OBRL, copy the current library across (skip the cache, which rebuilds itself) and point OBRL at it:

   ```bash
   sudo systemctl stop obrl
   # As obrl, not root: NFS's root_squash and sshfs don't let root write there as obrl.
   sudo -u obrl rsync -rlt --info=progress2 --exclude cache --exclude tmp --exclude keys /var/lib/obrl/ /mnt/obrl-library/
   # /etc/obrl/obrl.env
   Obrl__StorageRoot=/mnt/obrl-library
   Obrl__CacheRoot=/var/lib/obrl
   ```

4. Let the service write there, and only start once the share is mounted (so OBRL never writes into the empty
   mount-point folder if the storage server is down):

   ```bash
   sudo install -D -m 644 deploy/obrl-remote-storage.conf /etc/systemd/system/obrl.service.d/remote-storage.conf
   sudo systemctl daemon-reload && sudo systemctl start obrl
   ```

5. Backups: `deploy/backup.sh` now reads the storage path from `/etc/obrl/obrl.env`. If the storage server keeps
   its own snapshots, add `BACKUP_BOOKS=no` to `/etc/obrl/backup.env` to back up only the database and keys from the
   VPS. Either way, keep a copy of `/etc/obrl/master.key` off both servers.

**Object storage (S3, MinIO, Backblaze B2, Wasabi)** would also work, but OBRL reads and writes files directly, so it
would need a storage layer in the code. A quick alternative is mounting a bucket with `rclone mount` and treating it
like the share above, but rclone's file cache makes writes less reliable than NFS. Choose NFS or SMB unless your
storage is only available as a bucket.

## Languages

The interface is available in English, Telugu (తెలుగు) and Kannada (ಕನ್ನಡ). Members pick a language from the
switcher in the top bar (or under the sign-in form). The choice is saved on their account and follows them to every
device they sign in on; before signing in, the browser's language is used. Book content is never translated.

**Book details in other languages.** When uploading or editing a book, the title, author and description can be
entered in every interface language (one tab per language; English is required, the others are optional). Readers see
the details in the language they picked, and any field left empty shows the English text instead. Shelves, the library
(sorted by the title the reader sees), book pages and the reader all use it, and search matches the details in every
language. They are stored in the `BookTranslations` table, one row per book and language.

**Categories in other languages.** Moderators enter a category's name and description the same way: English is
required and the other languages are optional, with empty fields showing the English text. The library tree, the
upload form, breadcrumbs and book pages show each category in the reader's language, sorted by the name they see.
The Categories page lists the English names with the other languages beside them. They are stored in the
`CategoryTranslations` table.

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
| `Obrl:StorageRoot` | `/var/lib/obrl` | Encrypted books, covers, page cache, data protection keys. Can be a mounted share on another server (see below) |
| `Obrl:CacheRoot` | empty | Page cache, conversion scratch space and the data protection (sign-in) keys. Empty uses `StorageRoot`. Set it to a local folder when `StorageRoot` is on another server, so the keys never leave the VPS |
| `Obrl:MaxSubscribedBooks` | `5` | Most books a User or Contributor can be subscribed to at once |
| `Obrl:KeyFile` | `/etc/obrl/master.key` | 32-byte master key (base64). Created on first start if missing. |
| `Obrl:MaxUploadMegabytes` | `200` | Largest upload |
| `Obrl:PageWidths` | `800, 1200, 1600` | Widths pages are rendered at |
| `Obrl:PageUrlLifetimeSeconds` | `60` | How long a signed page link works |
| `Obrl:ReaderRequestsPerMinute` / `PerHour` | `60` / `600` | Per-user reader limits |
| `Obrl:IdleTimeoutMinutes` | `30` | Sign-out after inactivity, until an Admin picks 5, 10, 15, 30 or 60 minutes under Admin > Settings (the Admin's choice is stored in the database and wins) |
| `Obrl:PasskeyServerDomain` | empty | Domain passkeys are bound to, e.g. `library.example.org`. Empty uses the site's host name. Set it before members add passkeys: changing it later makes their passkeys stop working |
| `Obrl:LibreOfficePath` / `CalibreConvertPath` | `soffice` / `ebook-convert` | Conversion tools |
| `Obrl:Seed:AdminEmail` / `AdminPassword` | | Creates the first admin when none exists |
| `Obrl:CopyrightHolder` | `OBRL` | Name after "©" in the footer |
| `Obrl:PublicUrl` | empty | Address members open, e.g. `https://books.example.org`, used for links in emails. Empty uses the address of the request that sent the email |
| `Obrl:Email:Host` / `Port` | empty / `587` | SMTP server. Email is off until `Host` and `FromAddress` are set |
| `Obrl:Email:Security` | `Auto` | `Auto` (SSL on port 465, STARTTLS otherwise), `StartTls`, `SslOnConnect` or `None` |
| `Obrl:Email:Username` / `Password` | empty | SMTP sign-in. Put the password in `/etc/obrl/obrl.env` (`Obrl__Email__Password=...`), never in `appsettings.json` |
| `Obrl:Email:FromAddress` / `FromName` | empty / `OBRL` | Sender of every email |

Which emails go out (new uploads to Moderators and Admins; sign-in details to new members) is chosen under
Admin > Settings, where Admins can also send themselves a test email. The book language list and publishers are kept
under Admin > Book lists.

## Footer and version

Every page ends with a footer showing the copyright, a link to **Terms & Conditions** (`/terms`, open to everyone,
also before signing in) and the version. Terms & Conditions shows the current disclaimer, the text members accept
before reading, so Admins keep editing it in one place: Admin > Disclaimer.

The version reads `major.minor.build`, for example `v1.1.126D67F3B0D9`. Click it to open the **version history**
(`/versions`, also open before signing in), a summary of every release. The entries are in
`src/Obrl.Web/Services/ReleaseNotes.cs`; add one at the top for each release, and its text to the language files.

- `major.minor` is `<ObrlVersion>` in `src/Obrl.Web/Obrl.Web.csproj` (now `1.1`). Change it by hand for a release.
- `build` is the build time in UTC written as `yyyyMMddHHmmss` and converted to hexadecimal
  (`20261104023944` → `126D67E87988`). Hover over it to see the time; to decode it yourself, convert the hex back to
  decimal. To pin a build number, pass `-p:ObrlBuildTimestamp=20261104023944` to `dotnet build` or `dotnet publish`.

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
| Cropper.js 1.6.2 | MIT | Cover crop and adjust tool (`wwwroot/lib/cropperjs`) |
| Chart.js 4.5.1 | MIT | Library at a glance charts (`wwwroot/lib/chartjs`) |
| qrcode-generator | MIT | Authenticator QR code |
| Inter, Literata | SIL OFL 1.1 | Fonts |
| SecLists NCSC password list | MIT | Leaked-password check |
| LibreOffice | MPL-2.0 | Word to PDF (separate process) |
| Calibre | GPL-3.0 | MOBI/AZW3/FB2 to EPUB (separate process, optional) |
| nginx | BSD-2 | Reverse proxy |
