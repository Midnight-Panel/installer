#!/usr/bin/env bash
#
# Midnight installer bootstrap. Downloads the "midnight" command and starts the installer:
#
#   bash <(curl -fsSL https://raw.githubusercontent.com/Midnight-Panel/installer/main/install.sh)
#
# All arguments are passed on, e.g. "install.sh daemon --ssl --daemon-address node1.example.com -y".
# Environment: MIDNIGHT_CLI_URL (release download base), MIDNIGHT_BUILD_FROM_SOURCE=1 (skip the download)

set -Eeuo pipefail

readonly CLI_URL="${MIDNIGHT_CLI_URL:-https://github.com/Midnight-Panel/installer/releases/latest/download}"
readonly INSTALLER_REPO="${MIDNIGHT_INSTALLER_REPO:-https://github.com/Midnight-Panel/installer.git}"
readonly BRANCH="${MIDNIGHT_BRANCH:-main}"
readonly TARGET="/usr/local/bin/midnight"

fail() { printf '\033[31m✖ %s\033[0m\n' "$*" >&2; exit 1; }
info() { printf '\033[38;2;70;95;255m•\033[0m %s\n' "$*"; }

[[ "$(uname -s)" == "Linux" ]] || fail "Midnight can only be installed on Linux"
[[ $EUID -eq 0 ]] || fail "Run the installer as root (sudo -i)"
command -v curl >/dev/null || fail "curl is missing, install it first"

case "$(uname -m)" in
  x86_64 | amd64) arch="x64" ;;
  aarch64 | arm64) arch="arm64" ;;
  *) fail "Unsupported architecture $(uname -m), only x86_64 and arm64 are supported" ;;
esac

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

download() {
  info "Downloading midnight for linux-$arch"
  curl -fsSL --retry 3 -o "$work/midnight" "$CLI_URL/midnight-linux-$arch" || return 1

  # Verify the checksum when the release provides one
  if curl -fsSL -o "$work/SHA256SUMS" "$CLI_URL/SHA256SUMS" 2>/dev/null; then
    expected="$(awk -v f="midnight-linux-$arch" '$2 == f { print $1 }' "$work/SHA256SUMS")"
    actual="$(sha256sum "$work/midnight" | awk '{ print $1 }')"
    [[ -n "$expected" ]] || fail "SHA256SUMS has no checksum for midnight-linux-$arch"
    [[ "$expected" == "$actual" ]] || fail "Checksum mismatch for midnight-linux-$arch"
  fi
}

# Fallback without a release: build with a temporary .NET SDK (removed afterwards)
build() {
  info "No release available, building midnight from source (takes a minute)"
  command -v git >/dev/null || fail "git is missing, install it first"

  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$work/dotnet-install.sh"
  bash "$work/dotnet-install.sh" --channel 10.0 --install-dir "$work/dotnet" >/dev/null
  git clone -q --depth 1 --branch "$BRANCH" "$INSTALLER_REPO" "$work/installer"

  DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 "$work/dotnet/dotnet" publish "$work/installer/src/Midnight.Cli" \
    -c Release -r "linux-$arch" --self-contained -p:PublishSingleFile=true -o "$work/out" >/dev/null
  mv "$work/out/midnight" "$work/midnight"
}

if [[ "${MIDNIGHT_BUILD_FROM_SOURCE:-0}" == "1" ]] || ! download; then
  build
fi

install -m 755 "$work/midnight" "$TARGET"
info "Installed $("$TARGET" version)"

# exec replaces the shell, so the EXIT trap would never remove the download (and a temporary .NET SDK)
rm -rf "$work"
trap - EXIT

# Started through "curl | bash" stdin is the script, the wizard needs the terminal.
# Only when it can really be opened: without a controlling terminal (cloud-init) /dev/tty exists but fails to open.
if [[ ! -t 0 ]] && ( : </dev/tty ) 2>/dev/null; then
  exec "$TARGET" install "$@" </dev/tty
fi

exec "$TARGET" install "$@"
