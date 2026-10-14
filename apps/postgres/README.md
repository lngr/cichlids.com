# PostgreSQL and object-store backup

The CloudNativePG Cluster runs one PostgreSQL instance. Production uses the
protected database volume through the `database-volume` StorageClass. Staging
and previews use k3s `local-path` storage on the node disk.
The database requests 250m CPU and 512Mi memory. Production also requests 100m CPU
and 128Mi for the Barman sidecar. The CloudNativePG controller requests 100m CPU
and 100Mi; the plugin controller requests 50m CPU and 128Mi. These requests fit a
CX33 node alongside the cluster services. A single node provides no availability
replica. Production and staging request 20Gi each; confirm free space on the
database volume for production and on the node disk for staging.

## Production storage

The app-tier bootstrap mounts the platform-created ext4 volume at
`/var/lib/cichlids-db` and creates its `postgres` directory only after verifying
the mounted device and filesystem. It applies `apps/postgres/storage.yaml`
before bootstrapping Argo CD. This file is owned by the app bootstrap, outside
the environment ApplicationSets. Changes to it rerun the app provisioner.

`database-volume` uses `kubernetes.io/no-provisioner` with
`WaitForFirstConsumer`. The static local PV `postgres-production` exposes
`/var/lib/cichlids-db/postgres` with node affinity for
`cichlids.com/database-volume=true`, the label set by bootstrap on the single
node. The production Cluster selects this PV through `pvcTemplate.volumeName`.
Both the StorageClass and PV specify `Retain`. PVC deletion releases the PV;
the database files remain on the protected volume and reuse requires operator
review. Kubernetes local volumes require node affinity, as described in the
[local-volume documentation](https://kubernetes.io/docs/concepts/storage/volumes/#local).
[Retain recovery](https://kubernetes.io/docs/concepts/storage/persistent-volumes/#retain)
is a manual operation.

A static PV suits one instance on one node and needs no storage controller or
cleanup helper. The PV advertises 20Gi, matching the production claim; this is
binding capacity, not a filesystem quota. Confirm actual free space with
`df -h /var/lib/cichlids-db`. Bootstrap refuses filesystems smaller than 20Gi.
The platform size default is 10 GB; the infrastructure runbook describes
operator-controlled volume and ext4 growth to at least 30 GB before deployment.
Additional instances, replacement-instance PVCs,
a separate WAL volume or online capacity expansion require an explicit storage
plan and additional PVs. An occupied or Released PV prevents a new claim from
binding. Review the preserved data and claim identity before any manual rebind;
bootstrap never clears a claim reference or deletes data.

The k3s systemd drop-in requires the database mount and verifies its ext4 device
before each service start. Loss of the mount stops k3s. The fstab `nofail` option
allows the node to boot for operator repair. See the
[infrastructure procedure](../../infra/README.md#app-ephemeral-tier) for the volume
ID, app apply and mount checks.

For an existing production database, schedule operator-controlled downtime.
A storageClass change does not migrate a bound PVC. First inspect the current
Cluster, PVCs, PVs and backup status. Verify a restorable base backup and WAL,
then stop application writers and record the final recovery point. Bootstrap
the volume and static PV. Recover production onto a fresh claim using the
CloudNativePG recovery bootstrap and the production ObjectStore in an
operator-only environment. Coordinate the protected Cluster/PVC replacement
and its name with the manifest's explicit PV selection; never start `initdb`
on existing production data. Retain the source PVC and disk until database
contents, application reads/writes and backup archiving pass validation.
Any removal of finalizers, PVCs or source data requires an explicit operator
review under ADR-0014.

After deployment, inspect:

```sh
kubectl get storageclass database-volume
kubectl get pv postgres-production -o yaml
kubectl -n cichlids-prod get pvc -o wide
kubectl -n cichlids-prod get cluster postgres -o yaml
```

Confirm the production claim is Bound to `postgres-production`, its class is
`database-volume`, the PV path is on the expected ext4 mount, and reclaim policy
is `Retain`. Runtime binding and a backup restore require operator verification.

## Controllers and protection

CloudNativePG **v1.30.1** and Barman Cloud plugin **v0.15.1** have singleton
Applications in the `cichlids-addons` project, both targeting `cnpg-system`.
The plugin uses cert-manager certificates. Argo CD uses server-side apply for the
large CRDs and ignores controller-managed webhook CA bundles. The CloudNativePG
Application owns the shared namespace. The plugin owns its ObjectStore CRD,
certificates and Issuer. Release URLs, source hashes and copy hashes are in
`tools/addon-sources.json`; the updater reproduces the copies. The plugin's
public `SIDECAR_IMAGE` setting uses a ConfigMap. Production and staging are
automatically discovered by the environment ApplicationSets.

Persistent Cluster resources have `Prune=false,Delete=false` and the Argo CD
resource finalizer. Stateful overlay annotations select the protected Application
policy. Persistent namespaces have the same sync options and their owning
Application has the resource finalizer. CloudNativePG's `pvcTemplate` is a
PersistentVolumeClaimSpec: it accepts no metadata or finalizers. The Cluster's
`inheritedMetadata` applies the sync options to generated PVCs. Kubernetes PVC
admission adds `kubernetes.io/pvc-protection`. The operator verifies this on the
created PVC; the static schema cannot demonstrate admission behavior. Deliberate
Cluster deletion requires explicit removal of its resource finalizer and review
of the protected PVCs.

## Production backup contract

The `database-backup` ObjectStore writes to `s3://cichlids-db-backup/` at
`https://nbg1.your-objectstorage.com`. Adjust these non-secret values together with
the backup tier's bucket and location when using another endpoint. A dedicated
key from the primary `cichlids` project references the `postgres-backup-writer`
Secret. Backup-project credentials remain operator-only.

The plugin is the WAL archiver. `archive_timeout: 300s` bounds segment switching;
continuous successful archiving is required for the five-minute RPO target.
ScheduledBackup uses six-field cron in UTC: daily at 02:00, monthly on day 1 at
03:00. The daily schedule requests an immediate first backup. Backup resources
have no schedule owner reference so deleting a schedule preserves their records.
The plugin has no retention policy and performs no retention deletion.

The bucket's Compliance lock is 400 days by default, covering twelve monthly
backups with margin. Daily base backups and WAL also receive this lock, so the
PITR history exceeds the required fourteen days after enough history accumulates.
A backup's actual recoverability requires a successful base backup and unbroken
WAL. Retention increases storage cost because daily backups and WAL share the
longest lock. Lifecycle expiration creates delete markers after 400 days;
unlocked noncurrent versions expire after one additional day and empty delete
markers expire. Incomplete multipart uploads expire after seven days. Compliance
locks take precedence over lifecycle removal. Existing versions retain their own
lock deadlines; a default-retention change applies to newly written versions.

Hetzner supports these lifecycle actions, with only `NoncurrentDays` for
noncurrent expiration. Its encryption support is SSE-C only. The manifests
configure neither SSE-S3 nor SSE-KMS, and configure no SSE-C encryption key.
Backups therefore have no configured at-rest encryption. The bucket is private
to the operator and the cluster writer key, with HTTPS transport. The independent
provider copy and restore drill require separate operational verification.

The bucket policy grants exactly:

| Permission | Barman calls |
| --- | --- |
| `s3:ListBucket` | `HeadBucket` connectivity/existence and `ListObjectsV2` for archive checks and restore discovery |
| `s3:GetObject` | `GetObject` restores and `HeadObject` WAL existence checks |
| `s3:PutObject` | WAL, backup metadata, base backups, `CreateMultipartUpload`, `UploadPart`, `CompleteMultipartUpload` |
| `s3:AbortMultipartUpload` | Cancel failed multipart uploads |

There is no separate IAM `HeadObject` action. Barman's upload/restore code uses no
multipart listing calls, so `ListMultipartUploadParts` and
`ListBucketMultipartUploads` are absent. Delete and bucket administration actions
are absent. The principal is
`arn:aws:iam:::user/p<primary-project-id>:<writer-access-key-id>`. The access key ID
is public configuration; its secret key never enters the backup tier.

## Staging and previews

Each environment has its own Cluster from the same base and initializes an empty
`cichlids` database with `initdb`. These databases never restore production data
and receive no production backup credentials, ObjectStore or backup schedule.
Synthetic seeding is specified in TASK-2.17.

Previews use a disposable 1Gi PVC per PR namespace. The quota permits one PVC and
1Gi of storage; service overlays can introduce no other persistent resources.
The preview patch removes Cluster finalizers and inherited Argo CD deletion
protection to permit PR teardown. Kubernetes manages the native PVC finalizer.
The preview Application and namespace Application retain their teardown
finalizers. This disposable database exception is limited to the postgres preview
overlay and requires empty initialization and absence of production access.

## Operator procedure

Perform these steps on the operator workstation. The credential file is
`~/.config/cichlids/cichlids.env`, or the path in `CICHLIDS_ENV`. Keep mode 0600.
The [infrastructure runbook](../../infra/README.md) defines the backup-project
credential and state encryption. These commands require real infrastructure;
CI runs offline substitutes and manifest validation only.

1. Create a dedicated S3 key pair in the **primary `cichlids` project**. Record the
   numeric primary project ID as `CICHLIDS_PRIMARY_PROJECT_ID`, the key ID as
   `CICHLIDS_S3_DB_WRITER_ACCESS_KEY` and its secret as
   `CICHLIDS_S3_DB_WRITER_SECRET_KEY`. Keep the backup-project key with the operator.
   Primary-project keys have default access to that project's own buckets; this
   backup bucket policy constrains cross-project access only.
2. The wrapper maps the project ID and public writer ID to
   `database_writer_primary_project_id` and `database_writer_access_key_id` in
   the backup tier, and removes the writer secret from the OpenTofu environment.
   Direct OpenTofu callers set only these two `TF_VAR_*` values in addition to
   the existing operator credentials and state passphrase.
3. Review and apply the backup tier:

   ```sh
   infra/tofu.sh backup init
   infra/tofu.sh backup plan
   infra/tofu.sh backup apply
   ```

   Confirm the private bucket, Object Lock at creation, Compliance default,
   lifecycle rules and writer policy. Existing buckets must have Object Lock
   enabled at creation. A migration never recreates a protected bucket implicitly.
4. Run the permission proof:

   ```sh
   tools/verify-backup-writer-policy.sh cichlids-db-backup
   ```

   The operator key reads the existing policy, lifecycle and lock settings. All
   write/read probes and configuration replay probes use the cluster writer key.
   Put and get must succeed; delete and each configuration update must return
   `AccessDenied`. A unique small proof object remains locked until expiry.
5. Create the production Secret. Confirm the intended Kubernetes context and
   that `cichlids-prod` exists. Build a temporary credential file outside Git:

   ```sh
   set -a
   . "${CICHLIDS_ENV:-$HOME/.config/cichlids/cichlids.env}"
   set +a
   umask 077
   WRITER_SECRET_FILE="$(mktemp)"
   trap 'rm -f "$WRITER_SECRET_FILE"' EXIT
   printf 'ACCESS_KEY_ID=%s\nACCESS_SECRET_KEY=%s\n' \
     "$CICHLIDS_S3_DB_WRITER_ACCESS_KEY" "$CICHLIDS_S3_DB_WRITER_SECRET_KEY" \
     > "$WRITER_SECRET_FILE"
   kubectl config current-context
   ```

   For direct provisioning:

   ```sh
   kubectl -n cichlids-prod create secret generic postgres-backup-writer \
     --from-env-file="$WRITER_SECRET_FILE" --dry-run=client -o yaml | kubectl apply -f -
   rm -f "$WRITER_SECRET_FILE"
   trap - EXIT
   ```

   For GitOps, first configure the operator recipient and `argocd/sops-age` using
   [SOPS age key](../../infra/README.md#sops-age-key). The repository's fixture
   recipient is unsuitable for production. Encrypt directly from the pipe:

   ```sh
   kubectl -n cichlids-prod create secret generic postgres-backup-writer \
     --from-env-file="$WRITER_SECRET_FILE" --dry-run=client -o yaml | \
     sops --encrypt \
       --filename-override apps/postgres/overlays/prod/backup-writer.sops.yaml \
       /dev/stdin > apps/postgres/overlays/prod/backup-writer.sops.yaml
   rm -f "$WRITER_SECRET_FILE"
   trap - EXIT
   ```

   Add a `ksops-generator.yaml` beside that encrypted file using
   `tools/fixtures/secrets/ksops-generator.yaml` as the shape and
   `files: [backup-writer.sops.yaml]`. Reference it through `generators` in the
   production Kustomization. Verify decryption with the operator key:

   ```sh
   export SOPS_AGE_KEY_FILE="$HOME/.config/cichlids/age/keys.txt"
   kustomize build --enable-alpha-plugins --enable-exec apps/postgres/overlays/prod >/dev/null
   python3 tools/check-secret-manifests.py
   ```

   Commit only the encrypted file, generator and Kustomization. Keep decrypted
   output out of logs. Default offline rendering requires the operator key once
   this production generator is present; use the same isolated decryption setup
   as the SOPS workflow.

After the Secret exists, verify cert-manager readiness, the controller rollouts,
Argo CD sync and database readiness. Inspect generated PVC annotations and native
finalizers, completed daily/monthly backups and successful WAL archiving. Perform
a PITR restore in an operator-only environment with the same read-capable key;
use a distinct archive server name for a restored writer. Measure RPO and RTO.
The static checks provide no evidence of these runtime properties.

## Sources

- [CloudNativePG v1.30.1](https://github.com/cloudnative-pg/cloudnative-pg/releases/tag/v1.30.1)
- [Barman Cloud plugin v0.15.1](https://github.com/cloudnative-pg/plugin-barman-cloud/releases/tag/v0.15.1)
- [Plugin usage](https://cloudnative-pg.io/plugin-barman-cloud/docs/usage/)
- [Barman 3.20.1 S3 implementation](https://github.com/EnterpriseDB/barman/blob/4f7da94f5c341db6b879deb3afe914fb63c13835/src/barman/cloud_providers/aws_s3.py)
- [Multipart permissions](https://docs.aws.amazon.com/AmazonS3/latest/userguide/mpuoverview.html#mpuAndPermissions)
- [Hetzner supported actions](https://docs.hetzner.com/storage/object-storage/supported-actions/)
- [Hetzner lifecycle rules](https://docs.hetzner.com/de/storage/object-storage/howto-protect-objects/manage-lifecycle/)
