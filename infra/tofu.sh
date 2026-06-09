#!/usr/bin/env bash
# Run OpenTofu for one infrastructure tier with all credentials loaded from a single key/value
# env file, instead of exporting a dozen generically-named TF_VAR_/AWS_ variables by hand.
#
# The env file holds real secrets and lives outside the repository; default location is
# ~/.config/cichlids/cichlids.env (template: infra/cichlids.env.example). Override with
# CICHLIDS_ENV=/path/to/file.
#
# Usage:
#     infra/tofu.sh <platform|backup> <tofu args...>
# Examples:
#     infra/tofu.sh backup init
#     infra/tofu.sh platform plan
#     infra/tofu.sh platform apply
#
# Credential mapping (ADR-0016, ADR-0017):
#   - platform uses the cichlids project's object-storage key (CICHLIDS_S3_PRIMARY_*) for both the
#     provider and the state backend, plus the cloud token (which also authorises the DNS zone)
#     and the SSH key;
#   - backup uses the backup project's object-storage key (CICHLIDS_S3_BACKUP_*) for both the
#     provider and the state backend, and NO cloud token.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ENV_FILE="${CICHLIDS_ENV:-$HOME/.config/cichlids/cichlids.env}"

TIER="${1:-}"
[ -n "$TIER" ] || { echo "usage: infra/tofu.sh <platform|backup> <tofu args...>" >&2; exit 2; }
shift
case "$TIER" in
  platform | backup) ;;
  *) echo "unknown tier '$TIER' (expected platform or backup)" >&2; exit 2 ;;
esac

if [ ! -f "$ENV_FILE" ]; then
  echo "env file not found: $ENV_FILE" >&2
  echo "  copy infra/cichlids.env.example there and fill it in, or set CICHLIDS_ENV." >&2
  exit 2
fi

# Load KEY=VALUE pairs into the environment of this process only.
set -a
# shellcheck disable=SC1090
. "$ENV_FILE"
set +a

require() {
  local missing=0 v
  for v in "$@"; do
    if [ -z "${!v:-}" ]; then
      echo "missing in $ENV_FILE: $v" >&2
      missing=1
    fi
  done
  [ "$missing" -eq 0 ] || exit 2
}

# Shared backend/provider settings (both tiers).
require CICHLIDS_STATE_PASSPHRASE
export TF_VAR_state_encryption_passphrase="$CICHLIDS_STATE_PASSPHRASE"
export TF_VAR_s3_endpoint="${CICHLIDS_S3_ENDPOINT:-https://nbg1.your-objectstorage.com}"
export TF_VAR_s3_region="${CICHLIDS_S3_REGION:-nbg1}"

case "$TIER" in
  platform)
    require CICHLIDS_HCLOUD_TOKEN \
      CICHLIDS_S3_PRIMARY_ACCESS_KEY CICHLIDS_S3_PRIMARY_SECRET_KEY CICHLIDS_SSH_PUBLIC_KEY
    # The cloud token also authorises the project-scoped DNS zone (ADR-0017). The cichlids
    # object-storage key serves both the provider (media-master) and the state backend.
    export TF_VAR_hcloud_token="$CICHLIDS_HCLOUD_TOKEN"
    export TF_VAR_ssh_public_key="$CICHLIDS_SSH_PUBLIC_KEY"
    export TF_VAR_s3_access_key="$CICHLIDS_S3_PRIMARY_ACCESS_KEY"
    export TF_VAR_s3_secret_key="$CICHLIDS_S3_PRIMARY_SECRET_KEY"
    export AWS_ACCESS_KEY_ID="$CICHLIDS_S3_PRIMARY_ACCESS_KEY"
    export AWS_SECRET_ACCESS_KEY="$CICHLIDS_S3_PRIMARY_SECRET_KEY"
    ;;
  backup)
    # No cloud token by design (ADR-0017). The backup project's key serves both the provider
    # (backup buckets) and the state backend.
    require CICHLIDS_S3_BACKUP_ACCESS_KEY CICHLIDS_S3_BACKUP_SECRET_KEY
    export TF_VAR_s3_access_key="$CICHLIDS_S3_BACKUP_ACCESS_KEY"
    export TF_VAR_s3_secret_key="$CICHLIDS_S3_BACKUP_SECRET_KEY"
    export AWS_ACCESS_KEY_ID="$CICHLIDS_S3_BACKUP_ACCESS_KEY"
    export AWS_SECRET_ACCESS_KEY="$CICHLIDS_S3_BACKUP_SECRET_KEY"
    ;;
esac

exec tofu -chdir="$ROOT/infra/$TIER" "$@"
