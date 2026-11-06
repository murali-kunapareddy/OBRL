# Deploy OBRL to an Ubuntu server, step by step

This guide takes you from a fresh Ubuntu VM to OBRL running at `https://books.example.com`, with its books on a
separate storage server, nightly backups and one-command updates. Every command is meant to be copied and pasted.

## Before you start

### The values used in this guide

Replace these everywhere if yours are different.

| What | Value in this guide |
| --- | --- |
| Web address | `books.example.com` |
| VM (runs the app and database) | `203.0.113.10` |
| Storage server (keeps the books) | `198.51.100.20` |
| Your Linux user on the VM | `libadmin` (you create it in step 3) |
| Your email (first admin account, certificate notices) | `you@example.com` |

### What only you can provide

Collect these first. Nothing in the guide can work them out for you.

1. **How to log in to the VM the first time**: the root password (or the user name and password, often `ubuntu`)
   from your VM provider's welcome email or control panel.
2. **Access to the DNS settings** of the domain that `books.example.com` belongs to, at whichever company runs its DNS.
3. **The storage server details**, from your storage provider's control panel or welcome email:
   - what you bought: a full server you log in to with root (a VPS or dedicated server), or a storage space
     (often called storage box, backup space, NAS or "FTP backup");
   - which ways in it offers: NFS, SMB (also called CIFS or Samba), SFTP/SSH, FTP, WebDAV;
   - the user name, password and port, and the share name or folder path;
   - whether the provider takes snapshots or backups of it.
4. **An email address** for the first admin account and for certificate expiry notices.
5. **A password manager** (Bitwarden, 1Password, KeePass) to keep the admin password, the master key and the backup
   password. Losing the master key means losing every book.

### Where each command runs

This guide uses **[MobaXterm](https://mobaxterm.mobatek.net/)** (free Home Edition) on Windows to connect to the
servers.

- Steps marked **On your PC (MobaXterm)** are clicks in MobaXterm, or commands in its local terminal
  (**Start local terminal** on the home tab).
- Blocks marked **On the VM** run in a MobaXterm SSH session to the VM (step 2 sets it up).
- Blocks marked **On the storage server** run in an SSH session to the storage server (only in Option A of step 9).

**Copy and paste in MobaXterm.** Selecting text with the mouse copies it. **Right-click** pastes it (if right-click
opens a menu instead, choose **Paste**, or press **Shift+Insert**). When pasting several lines, MobaXterm may ask
whether to paste them all: choose **Yes**.

When a command asks for a password, typing shows nothing. That is normal: type it and press Enter.

**Keep the connection alive.** Some steps run for 20 minutes. In MobaXterm, open **Settings > Configuration > SSH**
and tick **SSH keepalive**, then click OK. Step 5 also runs inside `tmux`, so the work goes on even if the connection
drops.

## Step 1. Point the web address at the VM

Do this first, because DNS changes can take from a few minutes to a few hours to reach everyone.

1. Sign in where the domain's DNS is managed. To find out where that is:

   **On your PC (MobaXterm local terminal)**

   ```bash
   nslookup -type=ns example.com
   ```

   The `nameserver =` lines name the DNS company (for example `*.cloudflare.com`, `*.domaincontrol.com` for GoDaddy,
   `*.registrar-servers.com` for Namecheap, or your hosting provider).
2. Add (or edit) a record:
   - **Type:** `A`
   - **Name / Host:** `books` (some panels want the full `books.example.com`)
   - **Value / Points to:** `203.0.113.10`
   - **TTL:** 300 seconds, or Auto
   - On Cloudflare, set the cloud icon to **DNS only** (grey). OBRL's own protections and the certificate work best
     without Cloudflare's proxy in front.
3. If there is an `AAAA` record or a `CNAME` for `books`, delete it, unless the AAAA value is the VM's own IPv6 address.
4. Check it (repeat until it shows the VM's address):

   **On your PC (MobaXterm local terminal)**

   ```bash
   nslookup books.example.com
   ```

   You want to see `Address: 203.0.113.10` in the answer part (the last lines).

## Step 2. Connect to the VM for the first time

**On your PC (MobaXterm)**

1. Click **Session** (top left), then **SSH**.
2. **Remote host:** `203.0.113.10`. Tick **Specify username** and type `root` (or the user your provider gave you,
   such as `ubuntu`). **Port:** `22`.
3. Click **OK**. The first time, MobaXterm may ask you to accept the server's key: click **Accept**.
4. Type the password from the provider and press Enter. If MobaXterm offers to save the password, choose **No**
   (step 3 replaces it with a key).

The session is now listed under **User sessions** on the left, so next time you only double-click it.

You are in when the prompt changes to something like `root@vm:~#`. Check the system:

**On the VM**

```bash
lsb_release -ds        # should say Ubuntu 26.04 (24.04 also works)
nproc                  # CPU cores: 2 or more
free -h                # memory: 4 GB or more is best
df -h /                # free disk space on the VM
```

If the commands below say `Permission denied`, put `sudo ` in front of them (you are not root).

## Step 3. Make the VM safe

### 3.1 Install updates

**On the VM**

```bash
sudo apt update && sudo apt full-upgrade -y
sudo timedatectl set-timezone Asia/Kolkata     # your time zone; list them with: timedatectl list-timezones
```

If a purple screen asks which services to restart, press Enter to accept.

### 3.2 Create your own user

Working as root all the time is risky. Create a user that can use `sudo`:

**On the VM**

```bash
sudo adduser libadmin            # choose a strong password; the other questions can be left empty (press Enter)
sudo usermod -aG sudo libadmin
```

### 3.3 Sign in with a key instead of a password

**On your PC (MobaXterm)**: create a key once.

1. Open **Tools > MobaKeyGen (SSH key generator)**.
2. Under **Parameters** at the bottom, choose **EdDSA** with **Ed25519**.
3. Click **Generate** and move the mouse over the empty area until it finishes.
4. **Key comment:** `obrl-vm`. **Key passphrase** and **Confirm passphrase:** a passphrase of your choice (it
   protects the key if your PC is stolen).
5. Click **Save private key** and save it as `Documents\SSH\obrl-vm.ppk` (make the folder if needed). Keep this
   file private and back it up; without it you cannot log in once step 3.4 turns passwords off.
6. Click inside the box **Public key for pasting into OpenSSH authorized_keys file** at the top, press **Ctrl+A**
   and **Ctrl+C**. It is one line that starts with `ssh-ed25519`. Leave MobaKeyGen open for now.

**On the VM** (still connected from step 2):

```bash
sudo install -d -m 700 -o libadmin -g libadmin /home/libadmin/.ssh
sudo nano /home/libadmin/.ssh/authorized_keys
```

Right-click to paste the line, then save with **Ctrl+O**, **Enter**, and leave with **Ctrl+X**.

```bash
sudo chown libadmin:libadmin /home/libadmin/.ssh/authorized_keys
sudo chmod 600 /home/libadmin/.ssh/authorized_keys
```

**Test it with a new session, keeping the first one open.** On your PC (MobaXterm):

1. **Session > SSH**. **Remote host:** `203.0.113.10`, tick **Specify username:** `libadmin`, **Port:** `22`.
2. Open the **Advanced SSH settings** tab, tick **Use private key**, and pick `Documents\SSH\obrl-vm.ppk`.
3. Optional: on the **Bookmark settings** tab, set **Session name** to `OBRL VM`.
4. Click **OK**.

It should ask only for the key's passphrase, not the Linux password. Then check that `sudo` works:

```bash
sudo whoami          # asks for libadmin's Linux password, then prints: root
```

### 3.4 Turn off password and root logins

Only do this once step 3.3 worked. **On the VM** (as libadmin):

```bash
sudo tee /etc/ssh/sshd_config.d/10-obrl.conf > /dev/null <<'EOF'
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin no
EOF
sudo sshd -t && sudo systemctl restart ssh
```

`sshd -t` prints nothing when the settings are fine. Before closing anything, double-click the `OBRL VM` (libadmin)
session on the left to open one more and check that it still logs in. From now on, always use that session; the
first `root` session no longer works, and you can delete it (right-click it, **Delete session**).

### 3.5 Turn on the firewall

**On the VM**

```bash
sudo ufw allow OpenSSH
sudo ufw --force enable
sudo ufw status            # shows OpenSSH ALLOW
```

The installer opens ports 80 and 443 for the website later. If your VM provider's control panel also has a firewall
(sometimes called "security group"), allow incoming TCP 22, 80 and 443 there.

### 3.6 Add swap if the VM has less than 4 GB of memory

Converting Word files with LibreOffice needs memory. If `free -h` showed less than 4 GB total and `Swap: 0B`:

**On the VM**

```bash
sudo fallocate -l 4G /swapfile && sudo chmod 600 /swapfile
sudo mkswap /swapfile && sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
free -h                    # Swap now shows 4.0Gi
```

### 3.7 Restart once

```bash
sudo reboot
```

Wait a minute, then double-click the `OBRL VM` session to connect again (or press **R** in the old tab to reconnect).

## Step 4. Get OBRL onto the VM

**On the VM**

```bash
sudo apt install -y git
cd ~
git clone https://github.com/murali-kunapareddy/OBRL.git
cd OBRL
git log -1 --oneline        # shows the newest change, so you know it worked
```

If GitHub asks for a user name and password, the repository is private: use your GitHub user name and, as the
password, a token from GitHub > Settings > Developer settings > Personal access tokens > Fine-grained tokens
(give it read-only "Contents" access to this repository).

## Step 5. Install everything the app needs

One script installs and sets up .NET, PostgreSQL, nginx, the HTTPS certificate, LibreOffice, Calibre, the
firewall rules, fail2ban, the service and the nightly backup. Make sure step 1's check shows the VM's address first,
or the certificate part fails (you can repeat it later).

**On the VM**

```bash
tmux new -s obrl
```

The screen clears and a green bar appears at the bottom: you are inside `tmux`, which keeps the commands running
even if MobaXterm disconnects. (If it does, reconnect and type `tmux attach -t obrl` to get back.) Now run:

```bash
cd ~/OBRL
sudo DOMAIN=books.example.com ADMIN_EMAIL=you@example.com bash deploy/install.sh
```

It takes 10 to 20 minutes. Near the end it prints:

```
   First admin: you@example.com / AbCdEf...-Obrl
```

**Copy that password into your password manager now** (select it with the mouse to copy). It is shown only once.
(If you missed it: `sudo grep Seed__AdminPassword /etc/obrl/obrl.env`.) Type `exit` to leave `tmux` when it is done.

### What the script did

| Part | What was set up |
| --- | --- |
| .NET | The .NET 10 SDK, which includes the ASP.NET Core runtime, from Ubuntu's archive (or Canonical's `ppa:dotnet/backports` when the archive lacks it). The SDK lets the update script build new versions on the VM. |
| PostgreSQL | The database server, a database called `obrl` and a database user `obrl` with a random password (kept in `/etc/obrl/db.password`). The app creates its tables when it first starts. |
| Folders | `/opt/obrl` (the app), `/var/lib/obrl` (books and data), `/etc/obrl` (settings and keys), `/var/backups/obrl` (database dumps), and a system user `obrl` that runs the app and cannot log in. |
| Master key | `/etc/obrl/master.key`, which encrypts every stored book. |
| Settings | `/etc/obrl/obrl.env` (see step 6). |
| Service | `obrl.service`, which starts the app at boot and restarts it if it stops. It listens only on `127.0.0.1:5000`, so it can only be reached through nginx. |
| nginx | The website for `books.example.com`, passing requests to the app, with upload size and rate limits. |
| HTTPS | A free Let's Encrypt certificate that renews itself, and a redirect from `http://` to `https://`. |
| Security | Firewall open for SSH, HTTP and HTTPS only; fail2ban bans addresses that keep failing SSH logins; automatic security updates. |
| Backups | A nightly database dump at 02:30 (step 10 adds the off-server copy). |

Check the pieces:

```bash
dotnet --list-runtimes       # shows Microsoft.AspNetCore.App 10.0.x
psql --version               # 16 or newer
sudo -u postgres psql -lqt | cut -d'|' -f1 | grep -w obrl     # prints: obrl
sudo nginx -t                # "syntax is ok" and "test is successful"
sudo certbot certificates    # shows a certificate for books.example.com
```

If `certbot certificates` shows nothing, the certificate step failed, usually because DNS was not ready or port 80
is closed in the provider's firewall. Fix that, then run:

```bash
sudo certbot --nginx --redirect -d books.example.com -m you@example.com --agree-tos
```

### If you prefer to set up PostgreSQL yourself

The script already did this; it is shown only so you know what it is. To create the database by hand:

```bash
sudo apt install -y postgresql
sudo -u postgres psql -c "CREATE ROLE obrl LOGIN PASSWORD 'a-long-random-password';"
sudo -u postgres createdb -O obrl obrl
```

and put the same password in the `ConnectionStrings__Default` line of `/etc/obrl/obrl.env`.

## Step 6. Know where the settings and secrets live

All settings are in one file that only root and the app can read:

**On the VM**

```bash
sudo cat /etc/obrl/obrl.env
```

| Line | Meaning |
| --- | --- |
| `ConnectionStrings__Default=...` | How the app reaches PostgreSQL, including the database password. |
| `Obrl__StorageRoot=/var/lib/obrl` | Where the encrypted books are. Step 9 changes it to the storage server. |
| `Obrl__KeyFile=/etc/obrl/master.key` | The master key. |
| `Obrl__PasskeyServerDomain=books.example.com` | The domain passkeys are tied to. Never change it after members add passkeys. |
| `Obrl__PublicUrl=https://books.example.com` | The address used in links inside emails. Older installs don't have it; add it (step 6.1). |
| `AllowedHosts=books.example.com` | The app answers only to this name. |
| `Obrl__Seed__AdminEmail` / `Obrl__Seed__AdminPassword` | Create the first admin. Removed in step 8. |

Other settings you may add (one per line, `__` instead of `:`) are listed in the Configuration table of the README,
for example `Obrl__CopyrightHolder=Your Name` or `Obrl__MaxUploadMegabytes=300`
(the second one also needs `client_max_body_size` raised in `/etc/nginx/sites-available/obrl`).

To change a setting:

```bash
sudo nano /etc/obrl/obrl.env           # edit, Ctrl+O, Enter, Ctrl+X
sudo systemctl restart obrl
```

Never put this file, the master key or passwords in git or in a chat.

### 6.1 Email (optional, from version 1.1 of 6 November 2026)

OBRL can send email through any SMTP mail server: the mailbox provider of your domain (Zoho, Google Workspace,
Microsoft 365, your hosting company), or a sending service such as Brevo, Mailgun or Amazon SES. With email set up:

- Moderators and Admins get an email when a book is uploaded.
- New members get an email with their temporary password and a sign-in link. At first sign-in they choose their own
  password, then set up an authenticator app or a passkey. The same email goes out when an Admin sets a new temporary password.

Without these lines nothing is emailed and everything else works as before: the Admin hands over the temporary
password, and the member still has to change it at first sign-in.

From your mail provider you need: the SMTP server name and port, a user name and password (often an "app password"
or "SMTP key", not your normal password), and a sender address the provider lets you send from, e.g.
`library@example.com`.

**On the VM**

```bash
sudo nano /etc/obrl/obrl.env
```

Add these lines with your values (Ctrl+O, Enter, Ctrl+X to save):

```
Obrl__PublicUrl=https://books.example.com
Obrl__Email__Host=smtp.example.com
Obrl__Email__Port=587
Obrl__Email__Username=library@example.com
Obrl__Email__Password=the-smtp-password
Obrl__Email__FromAddress=library@example.com
Obrl__Email__FromName=Book Library
```

Then:

```bash
sudo systemctl restart obrl
```

| Line | Meaning |
| --- | --- |
| `Obrl__PublicUrl` | The address members open. Links in emails start with it. |
| `Obrl__Email__Host` / `Port` | The SMTP server. Port 587 (STARTTLS) works with almost every provider; 465 is SSL. |
| `Obrl__Email__Security` | Leave it out (Auto: SSL on 465, STARTTLS otherwise). `None` only for a mail relay on the same VM. |
| `Obrl__Email__Username` / `Password` | Sign-in for the SMTP server. Leave both out for a relay that needs none. |
| `Obrl__Email__FromAddress` / `FromName` | Sender address and the name shown in the inbox. |

Check it: sign in as Admin, open **Admin > Settings**, and press **Send me a test email** in the Email box.
The same box turns the two kinds of email on and off. If the test fails, the message on the page says why; the usual
causes are a wrong password, a sender address the provider does not accept, or the VM's outgoing port being blocked
(some cloud providers block 25 and sometimes 587: ask them, or use 465 or 2525 if your provider offers it). The log
has the details: `sudo journalctl -u obrl -n 50`.

To keep emails out of spam folders, add the SPF and DKIM records your mail provider gives you to the DNS of your
domain (they are usually on the provider's "domain setup" page).

## Step 7. Build and start the app

**On the VM**

```bash
cd ~/OBRL
sudo bash deploy/update.sh
```

The first build downloads packages and takes a few minutes. It ends with `OBRL is up.`
If it shows log lines instead, see Troubleshooting at the end.

```bash
systemctl status obrl --no-pager       # "active (running)" in green; press q if it waits
```

## Step 8. First sign-in

1. Open `https://books.example.com` in your browser. You should see the OBRL sign-in page with a padlock in the
   address bar.
2. Sign in with `you@example.com` and the password from step 5.
3. OBRL asks you to set up a strong sign-in. Either:
   - **Passkey:** follow the browser's prompt (fingerprint, face, or device PIN), or
   - **Authenticator app:** scan the QR code with Google Authenticator, Microsoft Authenticator, Aegis or similar,
     type the 6-digit code, and **save the recovery codes** in your password manager.
4. Read and accept the disclaimer.
5. Remove the first-admin lines from the settings, since they are not needed any more:

   **On the VM**

   ```bash
   sudo sed -i '/^Obrl__Seed__/d' /etc/obrl/obrl.env
   sudo systemctl restart obrl
   ```

6. In OBRL, go to **Admin > Settings** to choose the sign-out time and loan period, **Admin > Disclaimer** to word
   the terms your way, and **Admin > Users** to create accounts for your members. Create categories under
   **Categories**.

**Real visitor addresses (forwarded headers).** nginx tells the app the visitor's real address and that the
connection was HTTPS, and the app trusts this only from nginx on the same machine. Nothing needs setting up. To check
it, open **Admin > Activity log**: your sign-in should show your own internet address (search "what is my IP"
to compare), not `127.0.0.1`.

Don't upload books yet if you are moving them to the storage server in step 9; it is quicker to move an empty library.

## Step 9. Keep the books on the storage server

The app and database stay on the VM. The books (already encrypted with the master key, which never leaves the VM)
go to a folder on the storage server that the VM mounts at `/mnt/obrl-library`. The page cache and the sign-in keys
stay on the VM's own disk so reading stays fast.

### 9.1 Find out what the storage server offers

First, look at the storage provider's control panel or welcome email (see "What only you can provide"). Then test
from the VM which ways in are open:

**On the VM**

```bash
sudo apt install -y netcat-openbsd nfs-common cifs-utils smbclient sshfs
for p in 22 23 445 2049; do nc -zvw3 198.51.100.20 $p; done
```

Each line says `succeeded` (open) or `timed out` / `refused` (closed):

| Open port | What it means | Check it with |
| --- | --- | --- |
| 22 (or 23) | SSH or SFTP | `sftp USERNAME@198.51.100.20` (add `-P 23` for port 23). If it logs in, type `pwd` to see your folder, then `bye`. If plain `ssh USERNAME@198.51.100.20` gives you a normal shell where `sudo whoami` says `root`, it is a full server. |
| 445 | SMB (CIFS, Samba) | `smbclient -L //198.51.100.20 -U USERNAME` lists the share names. |
| 2049 | NFS | `showmount -e 198.51.100.20` lists the folders shared with you (it may say nothing for NFS version 4 only; the provider's panel then has the path). |

Then pick one:

| What you have | Use |
| --- | --- |
| A full Linux server where you are root | **Option A: WireGuard + NFS** (fast and encrypted). Option D also works and needs fewer steps. |
| A storage space with NFS shared to your VM's address | **Option B: NFS** |
| A storage space with SMB | **Option C: SMB** |
| A storage space with only SFTP/SSH | **Option D: SFTP (sshfs)**, or **Option E: rclone** if you already use rclone with it |
| Only FTP or WebDAV | Not suitable for a live library. Ask the provider to enable SFTP, or use it only for backups. |

### 9.2 Preparation (all options)

**On the VM**

```bash
id obrl                          # note the numbers, for example uid=998(obrl) gid=998(obrl)
sudo mkdir -p /mnt/obrl-library
```

In the commands below, replace `998` with your `uid` and `gid` numbers.

### Option A: a full server where you are root (WireGuard + NFS)

NFS itself is not encrypted, so it runs inside a private WireGuard tunnel between the two servers.

**On the storage server**: in MobaXterm, open a new **Session > SSH** to `198.51.100.20` with the user name the
provider gave you (as in step 2), then run:

```bash
sudo apt update && sudo apt install -y wireguard nfs-kernel-server
sudo sh -c 'umask 077; wg genkey > /etc/wireguard/storage.key; wg pubkey < /etc/wireguard/storage.key > /etc/wireguard/storage.pub'
sudo cat /etc/wireguard/storage.pub        # copy this line: the STORAGE public key
```

**On the VM**

```bash
sudo apt install -y wireguard
sudo sh -c 'umask 077; wg genkey > /etc/wireguard/vm.key; wg pubkey < /etc/wireguard/vm.key > /etc/wireguard/vm.pub'
sudo cat /etc/wireguard/vm.pub             # copy this line: the VM public key
```

**On the storage server**: create the tunnel (paste the VM public key where shown).

```bash
sudo tee /etc/wireguard/wg0.conf > /dev/null <<'EOF'
[Interface]
Address = 10.66.0.1/24
ListenPort = 51820
PostUp = wg set %i private-key /etc/wireguard/storage.key

[Peer]
PublicKey = PASTE_THE_VM_PUBLIC_KEY_HERE
AllowedIPs = 10.66.0.2/32
EOF
sudo nano /etc/wireguard/wg0.conf          # replace PASTE_THE_VM_PUBLIC_KEY_HERE, save
sudo systemctl enable --now wg-quick@wg0
```

Share a folder with the VM through the tunnel only:

```bash
sudo mkdir -p /srv/obrl-library
sudo chown 998:998 /srv/obrl-library && sudo chmod 750 /srv/obrl-library
echo '/srv/obrl-library 10.66.0.2(rw,sync,no_subtree_check,root_squash)' | sudo tee -a /etc/exports
sudo exportfs -ra
sudo ufw allow OpenSSH
sudo ufw allow from 203.0.113.10 to any port 51820 proto udp
sudo ufw allow in on wg0 from 10.66.0.2 to any port 2049 proto tcp
sudo ufw --force enable
```

**On the VM**: the other end of the tunnel (paste the STORAGE public key).

```bash
sudo tee /etc/wireguard/wg0.conf > /dev/null <<'EOF'
[Interface]
Address = 10.66.0.2/24
PostUp = wg set %i private-key /etc/wireguard/vm.key

[Peer]
PublicKey = PASTE_THE_STORAGE_PUBLIC_KEY_HERE
Endpoint = 198.51.100.20:51820
AllowedIPs = 10.66.0.1/32
PersistentKeepalive = 25
EOF
sudo nano /etc/wireguard/wg0.conf          # replace PASTE_THE_STORAGE_PUBLIC_KEY_HERE, save
sudo systemctl enable --now wg-quick@wg0
ping -c 3 10.66.0.1                        # replies mean the tunnel works
```

Add the mount:

```bash
echo '10.66.0.1:/srv/obrl-library  /mnt/obrl-library  nfs4  rw,hard,noatime,_netdev,nofail,x-systemd.requires=wg-quick@wg0.service  0 0' | sudo tee -a /etc/fstab
```

Go to 9.3.

### Option B: NFS from the storage provider

Ask the provider to share the folder with `203.0.113.10` only. NFS traffic is not encrypted; the books are encrypted
anyway, but cover thumbnails are not, so prefer A or D if you can. Use the path from `showmount -e` or the panel
(here `/export/obrl`). The folder must belong to uid and gid 998 on the storage side; ask the provider if you cannot
set this yourself.

**On the VM**

```bash
echo '198.51.100.20:/export/obrl  /mnt/obrl-library  nfs4  rw,hard,noatime,_netdev,nofail  0 0' | sudo tee -a /etc/fstab
```

(If NFS version 4 is refused, change `nfs4` to `nfs` and add `,vers=3` after `nofail`.) Go to 9.3.

### Option C: SMB (CIFS, Samba)

Use the share name from `smbclient -L` (here `obrl`). Store the login in a file only root can read:

**On the VM**

```bash
sudo install -m 600 /dev/null /etc/obrl/smb.cred
sudo nano /etc/obrl/smb.cred
```

Type these two lines with your details, then save:

```
username=YOUR_STORAGE_USERNAME
password=YOUR_STORAGE_PASSWORD
```

```bash
echo '//198.51.100.20/obrl  /mnt/obrl-library  cifs  credentials=/etc/obrl/smb.cred,vers=3.1.1,seal,uid=998,gid=998,file_mode=0640,dir_mode=0750,_netdev,nofail  0 0' | sudo tee -a /etc/fstab
```

`seal` encrypts the traffic. If mounting fails with "Operation not supported", change `vers=3.1.1` to `vers=3.0`;
if it still fails, remove `,seal` (the connection is then not encrypted, though the books themselves still are).
Go to 9.3.

### Option D: SFTP only (sshfs)

The VM logs in to the storage server with its own key and shows the remote folder as a normal folder.
Replace `USERNAME` with your storage user name, and `22` with the SFTP port if it is different (often 23).

**On the VM**

```bash
sudo install -d -m 700 /root/.ssh
sudo ssh-keygen -t ed25519 -N '' -C obrl-vm -f /root/.ssh/obrl-storage
sudo cat /root/.ssh/obrl-storage.pub
```

Put that public key on the storage server. Either add it in the provider's panel (look for "SSH keys"), or run this,
which asks for the storage password once:

```bash
sudo ssh-copy-id -i /root/.ssh/obrl-storage.pub -p 22 USERNAME@198.51.100.20
```

Test the key and create the library folder. The first time, answer `yes` to the fingerprint question:

```bash
sudo sftp -i /root/.ssh/obrl-storage -P 22 USERNAME@198.51.100.20
```

At the `sftp>` prompt type these, one per line:

```
pwd
mkdir obrl-library
bye
```

`pwd` printed your folder on the storage server, for example `/home/USERNAME` (some providers print just `/`).
The library path is that plus `/obrl-library`, for example `/home/USERNAME/obrl-library`. Add the mount:

```bash
echo 'USERNAME@198.51.100.20:/home/USERNAME/obrl-library  /mnt/obrl-library  fuse.sshfs  _netdev,nofail,allow_other,default_permissions,uid=998,gid=998,IdentityFile=/root/.ssh/obrl-storage,port=22,reconnect,ServerAliveInterval=15,ServerAliveCountMax=3,workaround=rename  0 0' | sudo tee -a /etc/fstab
sudo nano /etc/fstab        # check the last line: user name, path, port, 998 numbers
```

### Option E: rclone (for example, when another app such as Immich already uses it)

rclone can mount an SFTP, SMB, WebDAV or cloud storage remote like a folder. Run these as root (`sudo -i` first).
List the remotes you already have (here `serverbyt:`); `rclone config` creates one if you have none:

```bash
rclone listremotes
rclone mkdir serverbyt:obrl-library
```

On a hosting account, keep the library **outside** `public_html` (anything inside it can be downloaded from the web).
`serverbyt:obrl-library` is in the account's home folder, next to `public_html`.

If the remote is SFTP and rclone prints "No host key validation is being performed", make it check the server
(use the remote's `host` and `port`, shown by `rclone config show serverbyt`):

```bash
ssh-keyscan -p 22 HOST_FROM_THE_CONFIG >> /root/.ssh/known_hosts
rclone config update serverbyt known_hosts_file /root/.ssh/known_hosts
```

Add the mount. `vfs_cache_mode=writes` is required: OBRL saves each file under a temporary name and renames it,
which rclone only handles reliably in this mode.

```bash
ln -sf "$(command -v rclone)" /sbin/mount.rclone
install -d -m 700 /var/cache/rclone-obrl
echo "serverbyt:obrl-library  /mnt/obrl-library  rclone  rw,_netdev,nofail,allow_other,uid=$(id -u obrl),gid=$(id -g obrl),umask=027,vfs_cache_mode=writes,config=$(rclone config file | tail -n1),cache_dir=/var/cache/rclone-obrl,args2env  0 0" >> /etc/fstab
tail -n 1 /etc/fstab
```

rclone saves new uploads on the VM first and sends them to the storage a moment later; after a power cut it finishes
sending them at the next mount. Go to 9.3.

### 9.3 Mount it and check it

**On the VM**

```bash
sudo systemctl daemon-reload
sudo mount /mnt/obrl-library
findmnt /mnt/obrl-library                 # shows the storage server as the source
sudo -u obrl touch /mnt/obrl-library/.write-test && sudo -u obrl rm /mnt/obrl-library/.write-test && echo "OBRL can write"
```

If you see `Permission denied`, the folder on the storage server is not owned by the 998 numbers (Options A and B),
or the `uid=`/`gid=` numbers in `/etc/fstab` are wrong (C and D).

What the mount options do:

- `nofail`: if the storage server is down when the VM starts, the VM still starts normally (you can still log in).
- `_netdev`: wait for the network before mounting.
- `hard` (NFS): if the storage server pauses, file reads wait and then continue instead of failing.

### 9.4 Move the library and tell OBRL to use it

**On the VM**

```bash
sudo systemctl stop obrl
sudo -u obrl rsync -rlt --info=progress2 --exclude cache --exclude tmp --exclude keys /var/lib/obrl/ /mnt/obrl-library/
sudo sed -i 's|^Obrl__StorageRoot=.*|Obrl__StorageRoot=/mnt/obrl-library|' /etc/obrl/obrl.env
grep -q '^Obrl__CacheRoot=' /etc/obrl/obrl.env || echo 'Obrl__CacheRoot=/var/lib/obrl' | sudo tee -a /etc/obrl/obrl.env
grep -E 'StorageRoot|CacheRoot' /etc/obrl/obrl.env
```

The last command should show `Obrl__StorageRoot=/mnt/obrl-library` and `Obrl__CacheRoot=/var/lib/obrl`.

Now the **"require the mount" setting**: it lets the app write to the share, starts the app only when the share is
mounted, and stops it if the share goes away, so OBRL never writes books into the empty `/mnt/obrl-library` folder on
the VM's own disk:

```bash
# ~ must be your own user's home: if you are logged in as root, write /home/libadmin/OBRL instead of ~/OBRL
sudo install -D -m 644 ~/OBRL/deploy/obrl-remote-storage.conf /etc/systemd/system/obrl.service.d/remote-storage.conf
sudo systemctl daemon-reload
sudo systemctl start obrl
systemctl status obrl --no-pager
```

Open the site and check that the library and any covers still show. Then test the safety net:

```bash
sudo systemctl stop "$(systemd-escape -p --suffix=mount /mnt/obrl-library)"
systemctl is-active obrl        # prints: inactive  (the app stopped with the share)
sudo mount /mnt/obrl-library && sudo systemctl start obrl
systemctl is-active obrl        # prints: active
```

Finally restart the whole VM once (`sudo reboot`), reconnect, and check `findmnt /mnt/obrl-library` and
`systemctl is-active obrl`.

**If the storage server was down at boot**, the VM starts but OBRL does not. When the storage server is back:

```bash
sudo mount /mnt/obrl-library && sudo systemctl start obrl
```

## Step 10. Backups

### 10.1 What happens already

Every night at 02:30 the VM dumps the database to `/var/backups/obrl` and keeps 7 days. That copy is on the VM itself,
so it does not help if the VM is lost. The steps below copy it elsewhere.

### 10.2 Save the master key now

**On the VM**

```bash
sudo cat /etc/obrl/master.key
```

Copy the line into your password manager as "OBRL master key". Without it no book can ever be read again, and it is
the one thing a backup of the storage server cannot replace.

### 10.3 Copy backups off the VM with restic

restic makes encrypted, deduplicated backups.

**If you used rclone (Option E),** restic can use the same remote, so skip the SSH part below: run
`sudo rclone mkdir serverbyt:obrl-backups`, and in `backup.env` use `RESTIC_REPOSITORY=rclone:serverbyt:obrl-backups`
instead of the `sftp:` line. Everything else is the same.

Otherwise, this example sends them to the storage server over SFTP (for Option A
or D; for B and C, ask the provider whether SFTP is also available, or use another place restic supports, such as
Backblaze B2 or another server).

**On the VM** (Option D already made `/root/.ssh/obrl-storage`; for other options, run the first three commands of
Option D, and the `sftp` test below them, first.) Tell root's SSH how to reach the storage server:

```bash
sudo tee -a /root/.ssh/config > /dev/null <<'EOF'
Host obrl-storage
    HostName 198.51.100.20
    User USERNAME
    Port 22
    IdentityFile /root/.ssh/obrl-storage
EOF
sudo chmod 600 /root/.ssh/config
sudo nano /root/.ssh/config     # replace USERNAME (and the port if it is not 22)
echo 'mkdir obrl-backups' | sudo sftp -b - obrl-storage
```

Create a backup password and the backup settings:

```bash
openssl rand -base64 32         # copy this into your password manager as "OBRL backup password"
sudo install -m 600 /dev/null /etc/obrl/backup.env
sudo nano /etc/obrl/backup.env
```

Type these lines (with the password you just made):

```
RESTIC_REPOSITORY=sftp:obrl-storage:obrl-backups
RESTIC_PASSWORD=PASTE_THE_BACKUP_PASSWORD
BACKUP_BOOKS=no
```

`BACKUP_BOOKS=no` is right when the books are on the storage server and this backup goes to the same server: copying
them to where they already are adds nothing. Then the books' only safety is the storage provider's snapshots, so
ask the provider whether they take them. If they don't, send the restic backup to a second place instead and
remove `BACKUP_BOOKS=no` so the books are backed up too.

Run a backup now and look at it:

```bash
sudo systemctl start obrl-backup
sudo journalctl -u obrl-backup -n 20 --no-pager      # ends with "snapshot ... saved"
sudo bash -c 'set -a; . /etc/obrl/backup.env; restic snapshots'
```

### 10.4 Restoring

Practise this once, so you know it works.

- **The database, from the VM's own nightly dumps:**

  First list the dumps, newest first, and pick one:

  ```bash
  sudo ls -lt /var/backups/obrl/
  ```

  Put its name in the first line below (replace the example name), then paste the whole block. It does nothing if the
  file does not exist, and saves the current database first, so a wrong choice can be undone.

  ```bash
  DUMP=/var/backups/obrl/obrl-20261106-121232.dump
  if sudo test -s "$DUMP"; then
    sudo systemctl stop obrl
    sudo -u postgres pg_dump -Fc obrl | sudo tee "/var/backups/obrl/obrl-before-restore-$(date +%Y%m%d-%H%M%S).dump" > /dev/null
    sudo -u postgres dropdb obrl
    sudo -u postgres createdb -O obrl obrl
    sudo cat "$DUMP" | sudo -u postgres pg_restore -d obrl
    sudo systemctl start obrl
    echo "Restored $DUMP"
  else
    echo "No dump called $DUMP: nothing was changed."
  fi
  ```

- **Files from restic** (database dumps, `/etc/obrl` with the keys and settings, and the books if they are backed up):

  ```bash
  sudo bash -c 'set -a; . /etc/obrl/backup.env; restic restore latest --target /root/restore'
  sudo ls /root/restore
  ```

  On a brand new VM, do steps 2 to 5 first, then copy `/root/restore/etc/obrl/*` back to `/etc/obrl/`, restore the
  newest dump as above, and start OBRL.

## Step 11. Updating to a new version

When there is a new version on GitHub:

**On the VM**

```bash
cd ~/OBRL
sudo bash deploy/update.sh
```

It gets the newest code, builds it, saves a database dump (`obrl-before-update-*.dump` in `/var/backups/obrl`),
keeps the running version in `/opt/obrl.previous`, swaps in the new one, restarts and checks that the site answers.
The footer of every page shows the version; click it to see what changed.

- **New settings:** the version history (footer) says when a version needs a new line in `/etc/obrl/obrl.env`.
  Version 1.1 of 6 November 2026 adds the optional email settings: see step 6.1.
- **A particular version or branch:** `sudo bash deploy/update.sh v1.2` (a tag) or `sudo bash deploy/update.sh some-branch`.
- **Go back if something is wrong:** `sudo bash deploy/update.sh --rollback`. This puts the previous app back. If the
  new version changed the database and the old one then fails to start, restore the `obrl-before-update-*` dump
  (step 10.4).

Ubuntu installs security updates by itself. About once a month, also run:

```bash
sudo apt update && sudo apt full-upgrade -y
[ -f /var/run/reboot-required ] && sudo reboot
```

The HTTPS certificate renews itself; `sudo certbot renew --dry-run` checks that renewal works.

## Everyday commands

| To | Run on the VM |
| --- | --- |
| See if OBRL is running | `systemctl status obrl --no-pager` |
| Watch the app log live (Ctrl+C to stop) | `sudo journalctl -u obrl -f` |
| Restart OBRL | `sudo systemctl restart obrl` |
| Back up now | `sudo systemctl start obrl-backup` |
| Check the storage mount | `findmnt /mnt/obrl-library` |
| See banned SSH addresses | `sudo fail2ban-client status sshd` |
| Free disk space | `df -h / /mnt/obrl-library` |

## Troubleshooting

| What you see | What to check |
| --- | --- |
| Log says `Failed to bind to address http://127.0.0.1:5000: address already in use` | Another program (often a Docker container) uses port 5000; `sudo ss -ltnp 'sport = :5000'` names it. Move OBRL to a free port, see "Use another port" below. |
| **502 Bad Gateway** | The app is not running. `systemctl status obrl` and `sudo journalctl -u obrl -n 100 --no-pager` show why. |
| **400 Bad Request** or "Invalid hostname" | `AllowedHosts` in `/etc/obrl/obrl.env` must be exactly the address you open. |
| Browser warns the site is not secure | The certificate is missing: see the end of step 5. |
| OBRL inactive after a restart, with remote storage | The share did not mount: `findmnt /mnt/obrl-library`, then `sudo mount -v /mnt/obrl-library` shows the error. Is the storage server up? For Option A, `ping 10.66.0.1`. |
| `Permission denied` in the log under `/mnt/obrl-library` | Owner numbers do not match `id obrl` (see 9.3). |
| Passkey sign-in fails | `Obrl__PasskeyServerDomain` must be exactly `books.example.com`. |
| Word uploads stay "processing" or fail | `soffice --version` should print a version; look for "LibreOffice" in the app log. |
| Uploads over 200 MB are refused | Raise both `Obrl__MaxUploadMegabytes` and nginx's `client_max_body_size`, then `sudo systemctl reload nginx` and restart OBRL. |
| `update.sh` says "OBRL did not answer" | Read the log lines it printed; `sudo bash deploy/update.sh --rollback` restores the previous version. |
| Locked out of SSH | Use the VM provider's web console (in their control panel) to log in and undo the last change. |

### Use another port

OBRL listens on `127.0.0.1:5000`, which only nginx on the same server can reach. If another program already uses that
port, pick a free one (here 5050; `sudo ss -ltnp 'sport = :5050'` should print only the header line) and run:

```bash
sudo systemctl edit obrl --drop-in=port --stdin <<'EOF'
[Service]
ExecStart=
ExecStart=/usr/bin/dotnet /opt/obrl/Obrl.Web.dll --urls http://127.0.0.1:5050
EOF
sudo sed -i 's|127.0.0.1:5000|127.0.0.1:5050|g' /etc/nginx/sites-available/obrl
sudo nginx -t && sudo systemctl reload nginx
sudo systemctl restart obrl
```

The setting is kept in `/etc/systemd/system/obrl.service.d/port.conf`, so updates keep it, and `deploy/update.sh`
checks whichever port is set there.
