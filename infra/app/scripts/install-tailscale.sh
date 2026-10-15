#!/usr/bin/env bash
# Install the checksum-pinned client and reconcile optional Headscale enrollment.
set -euo pipefail
NODE_IPV4="${1:?node IPv4 required}"
AUTH_FILE="${2:?credential file required}"
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"; rm -f "$AUTH_FILE"' EXIT
python3 - "$NODE_IPV4" <<'PY'
import ipaddress
import sys
ipaddress.IPv4Address(sys.argv[1])
PY
VERSION=1.104.1
SHA256=108d1d96ecf410d305571e173516f27038f870919a9b89c914a167c6d33a4528
[[ $(uname -m) == x86_64 ]] || { echo 'Pinned client requires amd64' >&2; exit 1; }
if ! command -v tailscale >/dev/null || [[ $(tailscale version | head -1) != "$VERSION" ]]; then
  curl --fail --silent --show-error --location "https://pkgs.tailscale.com/stable/tailscale_${VERSION}_amd64.tgz" -o "$WORK/client.tgz"
  printf '%s  %s\n' "$SHA256" "$WORK/client.tgz" | sha256sum --check --status
  tar -xzf "$WORK/client.tgz" -C "$WORK"
  install -m 755 "$WORK/tailscale_${VERSION}_amd64/tailscale" /usr/local/bin/tailscale
  install -m 755 "$WORK/tailscale_${VERSION}_amd64/tailscaled" /usr/local/bin/tailscaled
  RESTART=true
else
  RESTART=false
fi
install -d -m 700 /var/lib/tailscale
cat > "$WORK/tailscaled.service" <<'UNIT'
[Unit]
Description=Tailscale node connectivity
Wants=network-online.target
After=network-online.target

[Service]
Environment=TS_NO_LOGS_NO_SUPPORT=true
ExecStart=/usr/local/bin/tailscaled --state=/var/lib/tailscale/tailscaled.state --socket=/run/tailscale/tailscaled.sock --port=41641
RuntimeDirectory=tailscale
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
UNIT
if ! cmp -s "$WORK/tailscaled.service" /etc/systemd/system/tailscaled.service; then
  install -m 644 "$WORK/tailscaled.service" /etc/systemd/system/tailscaled.service
  systemctl daemon-reload
  RESTART=true
fi
systemctl enable --now tailscaled
if "$RESTART"; then systemctl restart tailscaled; fi
STATE=''
for _ in $(seq 1 30); do
  if STATE=$(tailscale status --json 2>/dev/null | python3 -c 'import json,sys; print(json.load(sys.stdin)["BackendState"])' 2>/dev/null); then
    break
  fi
  sleep 1
done
[[ -n "$STATE" ]] || { echo 'tailscaled socket is unavailable' >&2; exit 1; }
if [[ "$STATE" != Running && ! -s "$AUTH_FILE" ]]; then
  echo 'No enrollment key supplied; tailnet membership unchanged'
  exit 0
fi
chmod 600 "$AUTH_FILE"
printf 'net.ipv4.ip_forward = 1\n' > /etc/sysctl.d/99-cichlids-tailnet.conf
sysctl -p /etc/sysctl.d/99-cichlids-tailnet.conf >/dev/null
if [[ "$STATE" == Running ]]; then
  CONTROL=$(tailscale debug prefs | python3 -c 'import json,sys; print(json.load(sys.stdin)["ControlURL"])')
  [[ "$CONTROL" == https://headscale.cichlids.com ]] || { echo 'Node belongs to another control server' >&2; exit 1; }
  tailscale set --advertise-routes="$NODE_IPV4/32" --accept-dns=false --accept-routes=false --ssh=false --snat-subnet-routes=false
else
  tailscale up --login-server=https://headscale.cichlids.com --auth-key="file:$AUTH_FILE" \
    --advertise-routes="$NODE_IPV4/32" --accept-dns=false --accept-routes=false --ssh=false --snat-subnet-routes=false --timeout=60s
fi
