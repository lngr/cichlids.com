#!/usr/bin/env bash
# Verifies ADR-0014 Layer 4 / ADR-0013: a written backup object in a Compliance-mode Object Lock
# bucket cannot be deleted within its retention window, even with the backup credential that holds
# it. Operator-run: needs the real backup-project key and network; NOT a CI gate. Reads the same
# env file as infra/tofu.sh (CICHLIDS_ENV, default ~/.config/cichlids/cichlids.env).
#
# It leaves a tiny test object behind, locked for the bucket's retention window: that lock is the
# proof. The object expires on its own.
#
# Usage: tools/verify-backup-immutability.sh [bucket]   (default: cichlids-db-backup)
# Story: task-2.21
set -uo pipefail

ENV_FILE="${CICHLIDS_ENV:-$HOME/.config/cichlids/cichlids.env}"
BUCKET="${1:-cichlids-db-backup}"
KEY="layer4-immutability-test.txt"

command -v aws >/dev/null 2>&1 || { echo "aws CLI not found (brew install awscli)" >&2; exit 2; }
[ -f "$ENV_FILE" ] || { echo "env file not found: $ENV_FILE (set CICHLIDS_ENV)" >&2; exit 2; }

set -a
# shellcheck disable=SC1090
. "$ENV_FILE"
set +a

ENDPOINT="${CICHLIDS_S3_ENDPOINT:-https://nbg1.your-objectstorage.com}"
REGION="${CICHLIDS_S3_REGION:-nbg1}"
: "${CICHLIDS_S3_BACKUP_ACCESS_KEY:?missing in env}"
: "${CICHLIDS_S3_BACKUP_SECRET_KEY:?missing in env}"
export AWS_ACCESS_KEY_ID="$CICHLIDS_S3_BACKUP_ACCESS_KEY"
export AWS_SECRET_ACCESS_KEY="$CICHLIDS_S3_BACKUP_SECRET_KEY"

# Wrap the CLI so every call carries the Hetzner endpoint/region.
s3() { command aws "$@" --endpoint-url "$ENDPOINT" --region "$REGION"; }

rc=0
echo "Layer 4 / Compliance immutability, bucket $BUCKET:"

# 1. Write a test object; it inherits the bucket's default Compliance retention.
if printf 'layer4 immutability proof\n' | s3 s3 cp - "s3://$BUCKET/$KEY" >/dev/null 2>&1; then
  echo "  ..    wrote test object $KEY"
else
  echo "  FAIL  could not write the test object (PutObject failed)" >&2
  exit 1
fi

# 2. Show the retention that was applied (proves the lock exists).
ret="$(s3 s3api get-object-retention --bucket "$BUCKET" --key "$KEY" 2>/dev/null | tr -d '\n ' || true)"
echo "  ..    object retention: ${ret:-<none reported>}"

# 3. Find the object version (versioning is on) and try to delete that version.
vid="$(s3 s3api list-object-versions --bucket "$BUCKET" --prefix "$KEY" \
        --query 'Versions[0].VersionId' --output text 2>/dev/null || true)"

if s3 s3api delete-object --bucket "$BUCKET" --key "$KEY" --version-id "$vid" >/dev/null 2>&1; then
  echo "  FAIL  the locked version was DELETED: Compliance did not hold"
  rc=1
else
  echo "  PASS  delete of the locked version was refused (Compliance holds)"
fi

echo
if [ "$rc" -eq 0 ]; then
  echo "backup immutability OK: a written backup object cannot be deleted within retention (ADR-0014 Layer 4)"
else
  echo "backup immutability FAILED" >&2
fi
exit "$rc"
