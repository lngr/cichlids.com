#!/usr/bin/env bash
# Mount the platform-created ext4 database volume and guard k3s service startup.
set -euo pipefail
VOLUME_ID="${1:?usage: mount-database.sh <database-volume-id>}"
[[ "$VOLUME_ID" =~ ^[1-9][0-9]*$ ]] || { echo 'Invalid database volume ID' >&2; exit 1; }
DEVICE="/dev/disk/by-id/scsi-0HC_Volume_${VOLUME_ID}"
TARGET=/var/lib/cichlids-db
FSTAB=/etc/fstab
DROPIN=/etc/systemd/system/k3s.service.d
PENDING=/etc/rancher/k3s/config.yaml.restart-required
[[ -b "$DEVICE" ]] || { echo "Database block device missing: $DEVICE" >&2; exit 1; }
[[ "$(blkid -p -s TYPE -o value "$DEVICE")" == ext4 ]] || {
  echo 'Database volume must contain an ext4 filesystem' >&2; exit 1;
}
ENTRY="$DEVICE $TARGET ext4 defaults,nofail 0 2"
EXISTING=$(awk -v device="$DEVICE" -v target="$TARGET" '$1 !~ /^#/ && ($1 == device || $2 == target) {print}' "$FSTAB")
if [[ -n "$EXISTING" && "$EXISTING" != "$ENTRY" ]]; then
  echo 'Conflicting database fstab entry; operator review required' >&2; exit 1
fi
mkdir -p "$TARGET"
if [[ -z "$EXISTING" ]]; then
  printf '\n%s\n' "$ENTRY" >> "$FSTAB"
fi
if ! findmnt -rn -M "$TARGET" >/dev/null; then
  mount "$TARGET"
fi
[[ "$(findmnt -rn -M "$TARGET" -o FSTYPE)" == ext4 ]] &&
  [[ "$(readlink -f "$(findmnt -rn -M "$TARGET" -o SOURCE)")" == "$(readlink -f "$DEVICE")" ]] || {
    echo 'Database mount has an unexpected device or filesystem' >&2; exit 1;
  }
# The static production claim requires 20Gi of filesystem capacity.
CAPACITY=$(df -B1 --output=size "$TARGET" | awk 'NR == 2 {print $1}')
[[ "$CAPACITY" =~ ^[0-9]+$ ]] && (( CAPACITY >= 21474836480 )) || {
  echo 'Database filesystem requires at least 20Gi capacity; operator resize required' >&2; exit 1;
}
mkdir -p "$TARGET/postgres"
mkdir -p "$DROPIN"
DESIRED=$(mktemp)
trap 'rm -f "$DESIRED"' EXIT
cat > "$DESIRED" <<EOF
[Unit]
RequiresMountsFor=$TARGET
BindsTo=var-lib-cichlids\\x2ddb.mount
After=var-lib-cichlids\\x2ddb.mount

[Service]
ExecStartPre=/bin/bash /opt/cichlids-bootstrap/mount-database.sh $VOLUME_ID
EOF
if ! cmp -s "$DESIRED" "$DROPIN/database.conf"; then
  mkdir -p "$(dirname "$PENDING")"
  install -m 600 /dev/null "$PENDING"
  install -m 644 "$DESIRED" "$DROPIN/database.conf"
  systemctl daemon-reload
fi
