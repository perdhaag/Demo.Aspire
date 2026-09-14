#!/usr/bin/env bash
# Generate the cluster's Postgres/Redis/RabbitMQ passwords and write them straight into
# deploy/apps/demo-kino/secrets.enc.yaml, SOPS-encrypted.
#
# The plaintext never touches the disk: it is piped into sops. The chart wants the same
# three passwords repeated per service (each service's Secret is rendered independently),
# which is why the same value appears several times below.
#
# Re-running this rotates the passwords. Postgres will not accept a changed POSTGRES_PASSWORD
# against an existing data directory, but nothing in this deployment has a volume, so the
# next pod start re-initialises from scratch anyway.
set -euo pipefail
cd "$(dirname "$0")/.."

command -v sops >/dev/null || { echo "sops is not installed -- see docs/gitops.md"; exit 1; }

gen() { head -c 32 /dev/urandom | base64 | tr -d '/+=' | head -c 24; }
PG=$(gen); REDIS=$(gen); RABBIT=$(gen)

# Dumped as a single values.yaml blob because that is the shape HelmRelease.valuesFrom
# reads: one Secret key holding a Helm values document.
VALUES=$(cat <<YAML
secrets:
  postgres:
    postgres_password: "$PG"
  cache:
    cache_password: "$REDIS"
  messaging:
    messaging_password: "$RABBIT"
  screenings:
    postgres_password: "$PG"
    cache_password: "$REDIS"
    messaging_password: "$RABBIT"
  bookings:
    postgres_password: "$PG"
    cache_password: "$REDIS"
    messaging_password: "$RABBIT"
  payments:
    postgres_password: "$PG"
    cache_password: "$REDIS"
    messaging_password: "$RABBIT"
  notifications:
    cache_password: "$REDIS"
    messaging_password: "$RABBIT"
  gateway:
    cache_password: "$REDIS"
YAML
)

# --filename-override is what makes the .sops.yaml creation rule match: sops picks the
# encryption key by the *path* of the file, and the real input here is a pipe.
OUT=deploy/apps/demo-kino/secrets.enc.yaml
python3 - "$VALUES" <<'PY' | sops --encrypt --filename-override "$OUT" --input-type yaml --output-type yaml /dev/stdin > "$OUT.tmp"
import sys, json
values = sys.argv[1]
print(json.dumps({
    "apiVersion": "v1",
    "kind": "Secret",
    "metadata": {"name": "demo-kino-secrets", "namespace": "demo-kino"},
    "type": "Opaque",
    "stringData": {"values.yaml": values},
}))
PY

mv "$OUT.tmp" "$OUT"
echo "==> $OUT written (encrypted; safe to commit)"
