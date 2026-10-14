#!/usr/bin/env bash
# Run OpenTofu for one infrastructure tier with all credentials loaded from a single key/value
# env file, instead of exporting a dozen generically-named TF_VAR_/AWS_ variables by hand.
#
# The env file holds real secrets and lives outside the repository; default location is
# ~/.config/cichlids/cichlids.env (template: infra/cichlids.env.example). Override with
# CICHLIDS_ENV=/path/to/file.
#
# Usage:
#     infra/tofu.sh <platform|backup|app> <tofu args...>
# Examples:
#     infra/tofu.sh backup init
#     infra/tofu.sh platform plan
#     infra/tofu.sh app apply
#
# Credential mapping (ADR-0016, ADR-0017):
#   - platform uses the cichlids project's object-storage key (CICHLIDS_S3_PRIMARY_*) for both the
#     provider and the state backend, plus the cloud token and the SSH key;
#   - backup uses the backup project's object-storage key (CICHLIDS_S3_BACKUP_*) for both the
#     provider and the state backend, and NO cloud token;
#   - app uses the cichlids project's object-storage key for the state backend only, its own
#     state passphrase, the node address and pinned host key, and the dedicated CI SSH private key
#     (read from the file CICHLIDS_APP_SSH_PRIVATE_KEY_FILE), and NO cloud token.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ENV_FILE="${CICHLIDS_ENV:-$HOME/.config/cichlids/cichlids.env}"

TIER="${1:-}"
[ -n "$TIER" ] || { echo "usage: infra/tofu.sh <platform|backup|app> <tofu args...>" >&2; exit 2; }
shift
case "$TIER" in
  platform | backup | app) ;;
  *) echo "unknown tier '$TIER' (expected platform, backup or app)" >&2; exit 2 ;;
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

# Shared backend/provider settings. The app tier has its own state passphrase and no provider.
if [ "$TIER" = app ]; then
  require CICHLIDS_APP_STATE_PASSPHRASE
  export TF_VAR_state_encryption_passphrase="$CICHLIDS_APP_STATE_PASSPHRASE"
else
  require CICHLIDS_STATE_PASSPHRASE
  export TF_VAR_state_encryption_passphrase="$CICHLIDS_STATE_PASSPHRASE"
  export TF_VAR_s3_endpoint="${CICHLIDS_S3_ENDPOINT:-https://nbg1.your-objectstorage.com}"
  export TF_VAR_s3_region="${CICHLIDS_S3_REGION:-nbg1}"
fi

case "$TIER" in
  platform)
    require CICHLIDS_HCLOUD_TOKEN \
      CICHLIDS_S3_PRIMARY_ACCESS_KEY CICHLIDS_S3_PRIMARY_SECRET_KEY CICHLIDS_SSH_PUBLIC_KEY
    # The cichlids object-storage key serves both the provider (media-master) and the state backend.
    export TF_VAR_hcloud_token="$CICHLIDS_HCLOUD_TOKEN"
    export TF_VAR_ssh_public_key="$CICHLIDS_SSH_PUBLIC_KEY"
    export TF_VAR_s3_access_key="$CICHLIDS_S3_PRIMARY_ACCESS_KEY"
    export TF_VAR_s3_secret_key="$CICHLIDS_S3_PRIMARY_SECRET_KEY"
    export AWS_ACCESS_KEY_ID="$CICHLIDS_S3_PRIMARY_ACCESS_KEY"
    export AWS_SECRET_ACCESS_KEY="$CICHLIDS_S3_PRIMARY_SECRET_KEY"
    ;;
  app)
    # No cloud token by design. The object-storage key reaches the state backend only.
    require CICHLIDS_S3_PRIMARY_ACCESS_KEY CICHLIDS_S3_PRIMARY_SECRET_KEY \
      CICHLIDS_NODE_IPV4 CICHLIDS_NODE_HOST_KEY CICHLIDS_APP_SSH_PRIVATE_KEY_FILE
    if [ ! -r "$CICHLIDS_APP_SSH_PRIVATE_KEY_FILE" ]; then
      echo "not readable: CICHLIDS_APP_SSH_PRIVATE_KEY_FILE=$CICHLIDS_APP_SSH_PRIVATE_KEY_FILE" >&2
      exit 2
    fi
    export TF_VAR_node_ipv4="$CICHLIDS_NODE_IPV4"
    export TF_VAR_node_host_key="$CICHLIDS_NODE_HOST_KEY"
    TF_VAR_ssh_private_key="$(cat "$CICHLIDS_APP_SSH_PRIVATE_KEY_FILE")"
    export TF_VAR_ssh_private_key
    export AWS_ACCESS_KEY_ID="$CICHLIDS_S3_PRIMARY_ACCESS_KEY"
    export AWS_SECRET_ACCESS_KEY="$CICHLIDS_S3_PRIMARY_SECRET_KEY"
    ;;
  backup)
    # No cloud token by design (ADR-0017). The backup project's key serves both the provider
    # (backup buckets) and the state backend.
    require CICHLIDS_S3_BACKUP_ACCESS_KEY CICHLIDS_S3_BACKUP_SECRET_KEY \
      CICHLIDS_PRIMARY_PROJECT_ID CICHLIDS_S3_DB_WRITER_ACCESS_KEY
    export TF_VAR_database_writer_primary_project_id="$CICHLIDS_PRIMARY_PROJECT_ID"
    export TF_VAR_database_writer_access_key_id="$CICHLIDS_S3_DB_WRITER_ACCESS_KEY"
    unset CICHLIDS_S3_DB_WRITER_SECRET_KEY
    export TF_VAR_s3_access_key="$CICHLIDS_S3_BACKUP_ACCESS_KEY"
    export TF_VAR_s3_secret_key="$CICHLIDS_S3_BACKUP_SECRET_KEY"
    export AWS_ACCESS_KEY_ID="$CICHLIDS_S3_BACKUP_ACCESS_KEY"
    export AWS_SECRET_ACCESS_KEY="$CICHLIDS_S3_BACKUP_SECRET_KEY"
    ;;
esac

exec tofu -chdir="$ROOT/infra/$TIER" "$@"
