#!/usr/bin/env bash
# Gives the container user access to /dev/kvm, so the Android emulator runs with hardware
# acceleration. The device belongs to the host's KVM group; the script adds a group with that
# numeric id (named kvm-device-<gid> when the id has no name in the container) and adds the user.
# Idempotent: it runs on creation and on every start of the devcontainer, so a container created
# earlier and a changed group id on the host are covered too. A new group membership applies to
# new login sessions.
set -euo pipefail

[ -c /dev/kvm ] || exit 0

kvm_gid="$(stat -c '%g' /dev/kvm)"
kvm_group="$(getent group "$kvm_gid" | cut -d: -f1 || true)"
if [ -z "$kvm_group" ]; then
  kvm_group="kvm-device-$kvm_gid"
  sudo groupadd --gid "$kvm_gid" "$kvm_group"
fi
if ! id -nG "$(id -un)" | tr ' ' '\n' | grep -qx "$kvm_group"; then
  sudo usermod --append --groups "$kvm_group" "$(id -un)"
  echo "KVM group $kvm_group configured. Reconnect the devcontainer to refresh the group membership."
fi
