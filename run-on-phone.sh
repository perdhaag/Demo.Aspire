#!/usr/bin/env bash
# Start Demo Kino and publish it to the tailnet so it can be used from a phone.
#
# Works around two things specific to this machine:
#   * the login session predates the `docker` group, so docker is reached via `newgrp`
#   * /usr/bin/aspire resolves the system SDK (10.0.111), which cannot build ASP.NET Core
#     projects, so the build is done with the mise SDK first and the CLI is told --no-build
set -euo pipefail
cd "$(dirname "$0")"

if [[ "${INSIDE_NEWGRP:-}" != "1" ]]; then
    if id -nG | grep -qw docker; then
        INSIDE_NEWGRP=1 exec "$0" "$@"
    fi
    echo "==> re-entering with the docker group"
    exec newgrp docker <<< "INSIDE_NEWGRP=1 exec '$PWD/$(basename "$0")' $*"
fi

echo "==> building with the mise SDK ($(dotnet --version))"
dotnet build src/Demo.Aspire.AppHost/Demo.Aspire.AppHost.csproj -v q --nologo

echo "==> starting the app host"
ASPIRE_ALLOW_UNSECURED_TRANSPORT=true \
dotnet run --project src/Demo.Aspire.AppHost/Demo.Aspire.AppHost.csproj --no-build &
APPHOST=$!
trap 'kill $APPHOST 2>/dev/null || true' EXIT INT TERM

echo "==> waiting for the gateway on :5100"
until curl -sf -o /dev/null --max-time 2 http://127.0.0.1:5100/health; do
    kill -0 $APPHOST 2>/dev/null || { echo "app host exited"; exit 1; }
    sleep 1
done

# Mailpit's published port is allocated fresh on every run, so the proxy is repointed
# each time rather than assumed.
MAILPORT=$(docker ps --filter 'name=mail-' --format '{{.Ports}}' \
           | grep -o '127.0.0.1:[0-9]*->8025/tcp' | cut -d: -f2 | cut -d- -f1)

# Port 80 as well as 443: a phone browser given a bare hostname tries http:// first,
# and with nothing on :80 it hangs rather than falling back to https.
tailscale serve --bg --http=80   http://127.0.0.1:5100        >/dev/null
tailscale serve --bg --https=443 http://127.0.0.1:5100        >/dev/null
tailscale serve --bg --https=8443 https+insecure://localhost:17217 >/dev/null
[[ -n "$MAILPORT" ]] && tailscale serve --bg --https=8025 "http://127.0.0.1:$MAILPORT" >/dev/null

HOSTNAME_TS=$(tailscale status --json | grep -m1 '"DNSName"' | cut -d'"' -f4 | sed 's/\.$//')
cat <<EOF

  Demo Kino   http://$HOSTNAME_TS/   (also https://)
  Dashboard   https://$HOSTNAME_TS:8443/   (token is in the app host output above)
  Inbox       https://$HOSTNAME_TS:8025/

  Ctrl+C stops the app. The tailnet routes stay up; remove them with:
    tailscale serve --http=80 off && tailscale serve --https=443 off && tailscale serve --https=8443 off && tailscale serve --https=8025 off

EOF
wait $APPHOST
