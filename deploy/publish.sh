#!/usr/bin/env bash
# Build OBRL and copy it to the server, then restart the service.
# Usage: deploy/publish.sh user@your-server
# Needs the .NET 10 SDK locally and SSH access with sudo on the server.
set -euo pipefail
TARGET="${1:?Usage: deploy/publish.sh user@server}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

dotnet publish "$ROOT/src/Obrl.Web/Obrl.Web.csproj" -c Release -r linux-x64 --self-contained false -o "$ROOT/.publish"
rsync -az --delete "$ROOT/.publish/" "$TARGET:/tmp/obrl-publish/"
ssh "$TARGET" 'sudo rsync -a --delete /tmp/obrl-publish/ /opt/obrl/ && sudo chown -R root:root /opt/obrl && sudo systemctl restart obrl && sleep 3 && systemctl --no-pager --lines=5 status obrl'
