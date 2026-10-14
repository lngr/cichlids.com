#!/usr/bin/env bash
# Operator probe for the primary-project database writer key.
# Reads CICHLIDS_ENV (default ~/.config/cichlids/cichlids.env).
# Configuration probes replay the operator's existing values to preserve bucket settings.
# A unique test object inherits Compliance retention and expires through bucket lifecycle.
set -euo pipefail

ENV_FILE="${CICHLIDS_ENV:-$HOME/.config/cichlids/cichlids.env}"
BUCKET="${1:-cichlids-db-backup}"
command -v aws >/dev/null || { echo 'aws CLI not found' >&2; exit 2; }
[ -f "$ENV_FILE" ] || { echo "env file not found: $ENV_FILE" >&2; exit 2; }
set -a
# shellcheck disable=SC1090
. "$ENV_FILE"
set +a
: "${CICHLIDS_S3_DB_WRITER_ACCESS_KEY:?missing in env}"
: "${CICHLIDS_S3_DB_WRITER_SECRET_KEY:?missing in env}"
: "${CICHLIDS_S3_BACKUP_ACCESS_KEY:?missing in env}"
: "${CICHLIDS_S3_BACKUP_SECRET_KEY:?missing in env}"
ENDPOINT="${CICHLIDS_S3_ENDPOINT:-https://nbg1.your-objectstorage.com}"
REGION="${CICHLIDS_S3_REGION:-nbg1}"
export AWS_EC2_METADATA_DISABLED=true
unset AWS_SESSION_TOKEN AWS_PROFILE AWS_DEFAULT_PROFILE
export AWS_ACCESS_KEY_ID="$CICHLIDS_S3_BACKUP_ACCESS_KEY"
export AWS_SECRET_ACCESS_KEY="$CICHLIDS_S3_BACKUP_SECRET_KEY"
s3() { command aws "$@" --endpoint-url "$ENDPOINT" --region "$REGION"; }

umask 077
PROBE_DIR="$(mktemp -d)"
trap 'rm -rf "$PROBE_DIR"' EXIT
s3 s3api get-bucket-policy --bucket "$BUCKET" > "$PROBE_DIR/policy-response.json"
python3 - "$PROBE_DIR" <<'PY'
import json,sys
from pathlib import Path
p=Path(sys.argv[1])
p.joinpath('policy.json').write_text(json.loads(p.joinpath('policy-response.json').read_text())['Policy'])
PY
s3 s3api get-bucket-lifecycle-configuration --bucket "$BUCKET" > "$PROBE_DIR/lifecycle-response.json"
s3 s3api get-object-lock-configuration --bucket "$BUCKET" > "$PROBE_DIR/lock-response.json"
python3 - "$PROBE_DIR" <<'PY'
import json,sys
from pathlib import Path
p=Path(sys.argv[1])
for source,target,key in [('lifecycle-response.json','lifecycle.json','Rules'),
                          ('lock-response.json','lock.json','ObjectLockConfiguration')]:
    response=json.loads(p.joinpath(source).read_text())
    value={'Rules':response['Rules']} if key=='Rules' else response[key]
    p.joinpath(target).write_text(json.dumps(value))
PY
export AWS_ACCESS_KEY_ID="$CICHLIDS_S3_DB_WRITER_ACCESS_KEY"
export AWS_SECRET_ACCESS_KEY="$CICHLIDS_S3_DB_WRITER_SECRET_KEY"
unset CICHLIDS_S3_BACKUP_ACCESS_KEY CICHLIDS_S3_BACKUP_SECRET_KEY
KEY="writer-policy-probes/$(date -u +%Y%m%dT%H%M%SZ)-$(basename "$PROBE_DIR").txt"
printf 'writer policy proof\n' > "$PROBE_DIR/input"
s3 s3api put-object --bucket "$BUCKET" --key "$KEY" --body "$PROBE_DIR/input" >/dev/null
s3 s3api get-object --bucket "$BUCKET" --key "$KEY" "$PROBE_DIR/output" >/dev/null
cmp "$PROBE_DIR/input" "$PROBE_DIR/output"
echo 'PASS PutObject and GetObject with the cluster key'

rc=0
refused() {
  local label="$1"
  shift
  if s3 "$@" > "$PROBE_DIR/probe-output" 2> "$PROBE_DIR/probe-error"; then
    echo "FAIL $label is allowed" >&2
    rc=1
  elif grep -Eq 'AccessDenied|Access Denied' "$PROBE_DIR/probe-error"; then
    echo "PASS $label refused (AccessDenied)"
  else
    echo "FAIL $label did not return AccessDenied" >&2
    cat "$PROBE_DIR/probe-error" >&2
    rc=1
  fi
}
refused DeleteObject s3api delete-object --bucket "$BUCKET" --key "$KEY"
refused PutBucketLifecycleConfiguration s3api put-bucket-lifecycle-configuration \
  --bucket "$BUCKET" --lifecycle-configuration "file://$PROBE_DIR/lifecycle.json"
refused PutObjectLockConfiguration s3api put-object-lock-configuration \
  --bucket "$BUCKET" --object-lock-configuration "file://$PROBE_DIR/lock.json"
refused PutBucketPolicy s3api put-bucket-policy --bucket "$BUCKET" --policy "file://$PROBE_DIR/policy.json"
if [ "$rc" -eq 0 ]; then
  echo 'backup writer policy OK (put/get allowed; delete and configuration denied)'
fi
exit "$rc"
