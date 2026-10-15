# Private staging and previews

Headscale coordinates the operator's devices. Its embedded DERP is the only
relay in the DERP map; UDP 3478 supplies STUN through k3s ServiceLB. The public
control endpoint is `https://headscale.cichlids.com`. Traefik terminates TLS and
cert-manager issues its certificate with HTTP-01. No cloud DNS credential is
needed inside the cluster.

The add-on uses Headscale 0.29.5, pinned by image digest. The node client is
Tailscale 1.104.1 from the official static archive with a checked SHA-256.
The backup sidecar uses Python 3.14.8 Alpine and the configuration init
container uses BusyBox 1.37.0-musl, both pinned by digest.

## Traffic and access

The node advertises its own public IPv4 as a `/32` subnet route. Approving
that route lets devices use public DNS for `staging.cichlids.com` and every
`pr-<number>.dev.cichlids.com`. Preview creation needs no private DNS update.
Linux clients must enable `--accept-routes=true`; the mobile and desktop
clients must accept subnet routes. The node uses `--accept-routes=false` and
`--accept-dns=false`. IPv4 forwarding is enabled, and subnet SNAT is disabled
so forwarded requests retain the client's tailnet address.

The route applies to all services at that IPv4, including public production,
Headscale and operator SSH on a connected client. Tailscale's own transport
and control traffic uses its underlay connections. Public DNS serves A
records; do not publish an AAAA for private hosts without an equivalent
routed and tested IPv6 path.

Traefik's `tailnet-only@file` Middleware permits `100.64.0.0/10` and
`fd7a:115c:a1e0::/48`. It uses the connection source address, with no forwarded
header IP strategy. The staging web Ingress owns both `/` and `/api`; the API
has no separate staging Ingress. The preview placeholder overlay applies the
same middleware to each generated host. Production Ingresses are public.
Keycloak's dedicated authentication hosts have their own public Ingresses.

The Traefik LoadBalancer Service uses `externalTrafficPolicy: Local`.
k3s ServiceLB and kube-proxy preserve the source address for the local
endpoint on the single node. The k3s configuration has no `node-external-ip`;
k3s documents a source-IP limitation when that option is combined with NAT.
Traefik trusts no caller-supplied header for its allowlist. The rule applies
only to the private workload Ingresses. The HTTP redirect excludes
`/.well-known/acme-challenge/`, and cert-manager creates separate public
solver Ingresses with no allowlist.

The `/32` design combines the documented subnet routing behavior with Linux
local address delivery. The local Docker test exercises the router's own
underlay address, Headscale, two kernel-mode Tailscale nodes and Traefik,
including the source address used by the middleware and a forced embedded
DERP relay connection with direct WireGuard traffic blocked. ServiceLB and public
ACME issuance require operator validation on the deployed cluster.

## Operator setup

1. Apply the platform DNS change for `headscale.cichlids.com`. Confirm public
   A resolution to the node. Permit TCP 80/443 and UDP 3478 at the provider
   and host firewall. UDP 41641 permits direct WireGuard connections to the
   node. Keep the existing public operator SSH access and SSH keys available.
2. Apply the app tier with `tailscale_auth_key` empty. It installs the pinned
   client without enrollment, prepares `/var/lib/cichlids-headscale` on the
   node disk with UID/GID 65532 and mode 0700, and labels the k3s node for the
   static PV. No tailnet connection is required for k3s or Argo CD bootstrap.
3. Sync Traefik, cert-manager and the Headscale add-on. Wait for readiness:

   ```sh
   kubectl -n headscale rollout status statefulset/headscale
   kubectl -n headscale wait --for=condition=Ready certificate/headscale-tls --timeout=300s
   kubectl -n headscale get service/headscale-stun
   ```

4. Create the first user. Commands use the in-pod Unix socket; no public
   administrative API credential is necessary. Record the numeric user ID:

   ```sh
   kubectl -n headscale exec headscale-0 -c headscale -- /ko-app/headscale --config /var/lib/headscale/config.yaml users create operator
   kubectl -n headscale exec headscale-0 -c headscale -- /ko-app/headscale --config /var/lib/headscale/config.yaml users list
   kubectl -n headscale exec headscale-0 -c headscale -- /ko-app/headscale --config /var/lib/headscale/config.yaml preauthkeys create --user 1 --expiration 1h
   ```

5. Supply that single-use pre-auth key as the sensitive app tier variable
   `tailscale_auth_key`. Enter it interactively to keep it out of shell history:

   ```sh
   read -rsp 'Headscale node pre-auth key: ' TF_VAR_tailscale_auth_key
   echo
   export TF_VAR_tailscale_auth_key
   tofu -chdir=infra/app apply
   unset TF_VAR_tailscale_auth_key
   ```

   Use the encrypted app state and operator credentials described in
   [infra/README.md](../../infra/README.md). OpenTofu sensitive values are
   stored in that encrypted state. The remote file is in a mode-0700 directory
   and the script removes it on exit. A failed SSH transfer can leave that
   file behind; remove `/opt/cichlids-bootstrap/tailnet/auth-key` over SSH
   before retrying. An enrolled node reconciles routes without consuming a
   second pre-auth key. An empty variable leaves existing membership intact.
6. Inspect the advertised route, identify the node and approve exactly its
   public IPv4 `/32`. Set `NODE_IPV4` and `NODE_ID` from the platform output
   and Headscale node list:

   ```sh
   kubectl -n headscale exec headscale-0 -c headscale -- /ko-app/headscale --config /var/lib/headscale/config.yaml nodes list-routes
   kubectl -n headscale exec headscale-0 -c headscale -- /ko-app/headscale --config /var/lib/headscale/config.yaml nodes approve-routes --identifier "$NODE_ID" --routes "${NODE_IPV4}/32"
   ```

7. Join operator devices with a separate single-use key for each device, or
   use interactive registration:

   ```sh
   sudo tailscale up --login-server https://headscale.cichlids.com --accept-routes=true
   ```

   Follow the displayed registration URL. Authorize its authentication ID
   through the pod's `headscale auth register --auth-id <id> --user operator`
   command. With a pre-auth key stored in a private local file, add
   `--auth-key=file:/absolute/path/to/key` and remove the file afterwards.
   On iOS, Android, macOS or Windows, configure the custom control server in
   the Tailscale client and enable subnet routes. Use an account with access
   to the VPN client settings.
8. Verify HTTPS staging and two preview hostnames from an enrolled device.
   Test from an independent device outside the tailnet: private HTTPS must
   return 403, production HTTPS must load, HTTP ordinary paths must redirect.
   During certificate issuance, the active Challenge token must return 200
   over public HTTP with its key authorization. Inspect Traefik access logs
   to confirm `100.x` on private requests. Test with a forged
   `X-Forwarded-For: 100.64.0.1` from outside: access must return 403.
   Run `tailscale netcheck` and `tailscale status` to confirm only the
   cichlids DERP region is offered. Verify public SSH from a device with
   Tailscale disabled.

The default Headscale policy permits enrolled devices to communicate. Route
approval is manual. Enrollment keys must be limited to trusted operators.

## Key rotation

Create fresh short-lived single-use pre-auth keys for new enrollment.
`headscale preauthkeys list --user 1` shows their IDs;
`headscale preauthkeys expire --id <id>` revokes an unused key. Revoking a
pre-auth key does not revoke an enrolled device. Use
`headscale nodes expire --identifier <id>` to require device authentication,
or `headscale nodes delete --identifier <id>` to remove it. Re-enroll the node
with a new key through the app tier and check route approval. Client machine
and WireGuard keys are managed by Tailscale in `/var/lib/tailscale`.

Headscale creates its Noise and DERP keys on the PVC at first start. Preserve
these server identity keys during routine upgrades. Replacing either key is
a coordinated identity change: pause reconciliation, stop Headscale, archive
the state, remove only the selected key and restart, then reauthenticate
clients and verify the relay. Plan this as an operator maintenance action.

## State, backup and recovery

A statically bound 1Gi Retain PV uses `/var/lib/cichlids-headscale` on the
node disk. Namespace, StatefulSet, PV, PVC and StorageClass have
`Prune=false,Delete=false`. The stateful add-on Application has the resources
finalizer, disabled automated pruning and `Prune=false,Delete=false` on the
Application itself. Removing its manifest preserves the data. The local PV
requires the labeled node; moving it to another node requires copying state
and reviewing affinity.

The init container renders the Git-managed configuration to the PVC on each
pod start. Headscale generates private keys itself. The backup sidecar uses
SQLite's online backup API, verifies integrity and atomically publishes a
mode-0600 tar archive containing the database, configuration and both private
keys every six hours. It keeps seven successful snapshots on the PVC and
retries failed snapshots after one minute. Check sidecar logs and archive
age; local snapshots share the node's failure domain. Disk space is shared
with the node filesystem; a local PV's declared capacity is no filesystem
quota. Monitor free space and snapshot age.

Copy a completed archive to the operator workstation daily and after changing
users, keys or route approval. Store the copy encrypted in the operator's
backup system, with a separate copy outside the node. The keys grant server
identity, so restrict access as for database credentials:

```sh
kubectl -n headscale logs headscale-0 -c backup --tail=20
kubectl -n headscale exec headscale-0 -c backup -- python3 -c 'from pathlib import Path; print(sorted(Path("/var/lib/headscale/backups").glob("*.tar.gz"))[-1].name)'
# Set ARCHIVE to the printed filename and BACKUP_DIR to a private local directory.
umask 077
kubectl -n headscale cp -c backup "headscale-0:/var/lib/headscale/backups/${ARCHIVE}" "${BACKUP_DIR}/${ARCHIVE}"
```

For recovery, connect by the public node IPv4 and operator SSH key. If that
workstation has the `/32` route active during a tailnet outage, disable
Tailscale or subnet route acceptance there first. SSH authentication, sshd
and platform SSH keys are independent of Headscale. Use the node's local
k3s kubeconfig to inspect Headscale, certificates, ServiceLB and tailscaled.

To restore an archive, pause automatic sync on the `root` and `headscale`
Applications and scale the Headscale StatefulSet to zero. Preserve the damaged
state for diagnosis. Extract the trusted archive into the empty state path,
set owner UID/GID 65532, directory mode 0700 and file mode 0600. Do not copy
old WAL/SHM files alongside the restored database. Bring Headscale up, verify
health, users, routes and relay, and resume both Applications' automatic sync.
The init container derives configuration from Git, so restore configuration
changes through the same GitOps source. Rebuild a lost node through the
platform/app tiers, restore the server state and re-enroll its Tailscale
client. Verify the `/32` route after any public IP change.

## Local verification and sources

```sh
./tools/check-cluster-manifests.sh
python3 infra/app/scripts/install-tailscale.test.py
python3 tools/check-tailnet-local.test.py
```

The Docker test requires a Linux Docker daemon with privileged containers and
`/dev/net/tun`. It uses a private benchmark subnet and a locally trusted test
certificate. It creates disposable containers and removes them on completion.
It exercises ingress behavior and routing; k3s ServiceLB, public DNS, real
Let's Encrypt issuance and operator SSH are deployment checks.

- [Headscale release and versioned configuration](https://github.com/juanfont/headscale/releases/tag/v0.29.5)
- [Headscale configuration fields](https://github.com/juanfont/headscale/blob/v0.29.5/config-example.yaml)
- [Headscale embedded DERP](https://headscale.net/stable/ref/derp/)
- [Tailscale subnet routing and route acceptance](https://tailscale.com/docs/features/subnet-routers)
- [Tailscale CLI flags](https://tailscale.com/docs/reference/tailscale-cli)
- [Official static client archives](https://pkgs.tailscale.com/stable/)
- [k3s ServiceLB and source IP limitation](https://docs.k3s.io/networking/networking-services)
- [Traefik IP allowlist](https://doc.traefik.io/traefik/reference/routing-configuration/http/middlewares/ipallowlist/)
- [Headscale wildcard DNS request](https://github.com/juanfont/headscale/issues/1840): extra records do not supply dynamic wildcard preview resolution.
