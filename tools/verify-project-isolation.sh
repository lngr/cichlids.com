#!/usr/bin/env bash
# Verifies the ADR-0017 capability boundary (ADR-0014 Layer 2) against live Hetzner Object
# Storage: a project-scoped S3 credential reaches only its own project's buckets. It proves that
# the cichlids object-storage key (the one CI holds) cannot reach the cichlids-backup project,
# neither the immutable backups nor the backup state.
#
# Operator-run: it needs the real credentials and network, so it is NOT a CI gate. Keys are read
# from the same env file as infra/tofu.sh (CICHLIDS_ENV, default ~/.config/cichlids/cichlids.env).
#
# Usage: tools/verify-project-isolation.sh
# Story: task-2.21, task-2.22
set -uo pipefail

ENV_FILE="${CICHLIDS_ENV:-$HOME/.config/cichlids/cichlids.env}"

command -v aws >/dev/null 2>&1 || {
  echo "aws CLI not found. Install it (e.g. brew install awscli) and re-run." >&2
  exit 2
}
[ -f "$ENV_FILE" ] || {
  echo "env file not found: $ENV_FILE (set CICHLIDS_ENV to override)" >&2
  exit 2
}

# Load KEY="value" pairs into this process only.
set -a
# shellcheck disable=SC1090
. "$ENV_FILE"
set +a

ENDPOINT="${CICHLIDS_S3_ENDPOINT:-https://nbg1.your-objectstorage.com}"
REGION="${CICHLIDS_S3_REGION:-nbg1}"

require() {
  local v missing=0
  for v in "$@"; do
    [ -n "${!v:-}" ] || { echo "missing in $ENV_FILE: $v" >&2; missing=1; }
  done
  [ "$missing" -eq 0 ] || exit 2
}
require CICHLIDS_S3_PRIMARY_ACCESS_KEY CICHLIDS_S3_PRIMARY_SECRET_KEY \
  CICHLIDS_S3_BACKUP_ACCESS_KEY CICHLIDS_S3_BACKUP_SECRET_KEY

# can_list <access-key> <secret-key> <bucket>: succeeds (0) iff that credential may list the
# bucket. Output is discarded; only reachability matters.
can_list() {
  AWS_ACCESS_KEY_ID="$1" AWS_SECRET_ACCESS_KEY="$2" \
    aws s3 ls "s3://$3/" --endpoint-url "$ENDPOINT" --region "$REGION" >/dev/null 2>&1
}

rc=0
pass() { printf '  PASS  %s\n' "$1"; }
fail() { printf '  FAIL  %s\n' "$1"; rc=1; }

echo "Layer 2 / ADR-0017 capability boundary, against $ENDPOINT:"

# 1. Positive control: the backup key reaches its own project's bucket.
if can_list "$CICHLIDS_S3_BACKUP_ACCESS_KEY" "$CICHLIDS_S3_BACKUP_SECRET_KEY" cichlids-db-backup; then
  pass "backup key reaches cichlids-db-backup (its own project)"
else
  fail "backup key cannot reach cichlids-db-backup (setup/tooling issue, not a boundary result)"
fi

# 2. Boundary: the cichlids (CI-held) key must NOT reach the immutable backups.
if can_list "$CICHLIDS_S3_PRIMARY_ACCESS_KEY" "$CICHLIDS_S3_PRIMARY_SECRET_KEY" cichlids-db-backup; then
  fail "cichlids key reached cichlids-db-backup: boundary BROKEN"
else
  pass "cichlids key denied on cichlids-db-backup (cannot reach the backups)"
fi

# 3. Boundary: the cichlids key must NOT reach the backup state.
if can_list "$CICHLIDS_S3_PRIMARY_ACCESS_KEY" "$CICHLIDS_S3_PRIMARY_SECRET_KEY" cichlids-backup-tfstate; then
  fail "cichlids key reached cichlids-backup-tfstate: boundary BROKEN"
else
  pass "cichlids key denied on cichlids-backup-tfstate (cannot reach the backup state)"
fi

echo
if [ "$rc" -eq 0 ]; then
  echo "project isolation OK: a project-scoped key reaches only its own project (ADR-0017, ADR-0014 Layer 2)"
else
  echo "project isolation FAILED: the capability boundary did not hold" >&2
fi
exit "$rc"
