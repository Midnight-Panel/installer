# Midnight Installer

Installer and command line tool for [Midnight Panel](https://github.com/Midnight-Panel/panel) and the
[Midnight Demon](https://github.com/Midnight-Panel/demon). One C# binary, `midnight`, does both: it runs the
installation wizard and manages everything afterwards.

## Installation

On a fresh Linux server (Debian/Ubuntu or the RHEL family, x86_64 or arm64) as root:

```bash
bash <(curl -fsSL https://raw.githubusercontent.com/Midnight-Panel/installer/main/install.sh)
```

The bootstrap script downloads `midnight` from the latest release (checksum verified) and starts `midnight install`.
Without a release it builds the binary from source with a temporary .NET SDK.

The wizard asks what to install: **Panel**, **Demon** or **both**.

| | Panel | Demon |
|---|---|---|
| SSL | Domain required, Caddy gets the certificate automatically | Domain required, certbot (renewed automatically) |
| Without SSL | IP or domain | IP or domain |
| Asks for | URL, port, admin email, username, password, public registration | URL, port, configuration from the panel |
| Runs as | Docker Compose stack in `/opt/midnight/panel` (panel, PostgreSQL 17, Caddy) | systemd service `midnight-demon` |

Before installing, the wizard checks the domain's DNS against the public IP of the server and shows a summary.
Every step shows a spinner with its duration, failures show the end of the log (`/var/log/midnight-installer.log`).
Passwords never end up in the log or on the command line.

### Unattended

All questions have options, `-y` skips the confirmation:

```bash
MIDNIGHT_ADMIN_PASSWORD='...' midnight install panel -y --ssl --panel-address panel.example.com --admin-email admin@example.com
midnight install demon -y --ssl --demon-address node1.example.com --demon-config ./config.json
```

`midnight install --help` lists all options. `--dry-run` shows the whole flow without changing anything (also on
macOS), the commands are only written to the log.

## The `midnight` command

| Command | |
|---|---|
| `midnight status` | Health, containers, service, certificate expiry |
| `midnight start\|stop\|restart [panel\|demon]` | Without a target: everything installed |
| `midnight logs <panel\|demon\|db\|proxy\|installer> [-f] [-n 200]` | Logs |
| `midnight update [panel\|demon]` | Pulls the latest source, rebuilds and restarts |
| `midnight configure demon [--file config.json]` | Paste the configuration from the node's setup tab |
| `midnight config <panel\|demon> [--edit]` | Show (secrets hidden) or edit the configuration |
| `midnight create-admin` | Another administrator |
| `midnight backup [dir]` | Database dump and panel storage |
| `midnight doctor` | Checks the installation and tells how to fix problems |
| `midnight maintenance on\|off\|status` | Every visitor sees the maintenance page (also on automatically during `midnight update`) |
| `midnight ssl renew` | Renews the demon certificate now |
| `midnight uninstall <panel\|demon> [--delete-data]` | Removes it, data is kept unless asked |
| `midnight self-update` / `midnight version` | |

## Environment

| Variable | Default |
|---|---|
| `MIDNIGHT_PANEL_REPO` | `https://github.com/Midnight-Panel/panel.git` |
| `MIDNIGHT_DEMON_REPO` | `https://github.com/Midnight-Panel/demon.git` |
| `MIDNIGHT_BRANCH` | `main` |
| `MIDNIGHT_CLI_URL` | `https://github.com/Midnight-Panel/installer/releases/latest/download` |
| `MIDNIGHT_BUILD_FROM_SOURCE` | `0`, `1` makes `install.sh` skip the download |

`midnight install --source <dir>` installs from a local checkout containing `panel/` and `demon/`.

## Files

| Path | |
|---|---|
| `/usr/local/bin/midnight` | This tool |
| `/etc/midnight/midnight.json` | What is installed and how |
| `/opt/midnight/src` | Panel and demon sources |
| `/opt/midnight/panel` | `docker-compose.yml`, `Caddyfile`, `.env` (database password) |
| `/etc/midnight/config.json` | Demon configuration |
| `/var/lib/midnight` | Server data of the demon |

## Development

```bash
dotnet build Midnight.Installer.slnx
dotnet test Midnight.Installer.slnx
dotnet run --project src/Midnight.Cli -- install --dry-run
```

Pushing a tag `v*` builds `midnight-linux-x64` and `midnight-linux-arm64` and publishes them with `SHA256SUMS`
(`.github/workflows/release.yml`).

## License

MIT
