#!/usr/bin/env bash
# Regenerate deploy/chart from the app host model.
#
# `aspire publish` renders AppHost.cs into a Helm chart. Three things it emits are not
# wanted in git, so they are stripped here rather than by hand:
#
#   * values.k8s.yaml   -- the environment overlay, including generated Postgres/Redis/
#                          RabbitMQ passwords in plaintext. The cluster gets those from
#                          deploy/apps/demo-kino/secrets.enc.yaml instead.
#   * templates/web/    -- the bun dev server. It is dev-time only (the gateway serves
#                          the bundled UI from its own wwwroot), so nothing builds or
#                          pushes a `web` image for the cluster to pull.
#   * web.Dockerfile    -- same reason.
#
# The image tags in values.yaml stay at their meaningless `:latest` defaults: the real
# refs live in the HelmRelease, where Flux's image automation rewrites them. That split is
# what keeps regenerating the chart from fighting with the deploy loop.
set -euo pipefail
cd "$(dirname "$0")/.."

STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT

echo "==> aspire publish"
aspire publish --output-path "$STAGE" "$@"

echo "==> stripping dev-only and secret-bearing output"
rm -f  "$STAGE/values.k8s.yaml" "$STAGE/web.Dockerfile" "$STAGE/web.Dockerfile.dockerignore"
rm -rf "$STAGE/templates/web"

python3 - "$STAGE/values.yaml" <<'PY'
import re, sys
path = sys.argv[1]
out, skip = [], False
for line in open(path).read().splitlines(True):
    if re.match(r"^  web:\s*$", line):
        skip = True
        continue
    if skip:
        if line.strip() == "" or re.match(r"^    \S", line):
            continue
        skip = False
    out.append(line)
open(path, "w").write("".join(out))
PY

echo "==> syncing into deploy/chart"
rsync -a --delete "$STAGE/" deploy/chart/

helm lint deploy/chart --values deploy/chart/values.yaml >/dev/null
echo "==> deploy/chart updated; review 'git diff deploy/chart' before committing"
