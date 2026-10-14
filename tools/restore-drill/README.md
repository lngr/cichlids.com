# Synthetic database restore drill

The drill verifies TASK-2.10 with synthetic tables in an ephemeral kind cluster.
It uses the repository's CloudNativePG 1.30.1, Barman Cloud plugin 0.15.1 and
cert-manager manifests, and the PostgreSQL 18.6 base in `apps/postgres`.
The source overlay selects kind's `standard` storage, a 1Gi claim and database
`drill`. Disposable Cluster metadata permits teardown. The ObjectStore and
plugin backup settings come from the production PostgreSQL overlay.

## Run locally

Requirements: Docker, Python 3.12 with venv support and the pinned core tools
from `tools/install-toolchain.sh` (kind, kubectl and kustomize).

```sh
python3 -m venv /tmp/cichlids-restore-drill-venv
/tmp/cichlids-restore-drill-venv/bin/pip install -r tools/restore-drill/requirements.txt
/tmp/cichlids-restore-drill-venv/bin/python tools/restore-drill/test_drill.py
/tmp/cichlids-restore-drill-venv/bin/python -u tools/restore-drill/drill.py
/tmp/cichlids-restore-drill-venv/bin/python -u tools/restore-drill/drill.py --break-backup
/tmp/cichlids-restore-drill-venv/bin/python tools/restore-drill/check-cleanup.py
```

The normal invocation exits 0 after matching each table's row count and ordered
row checksum. `--break-backup` deletes the base archives while preserving backup
metadata and WAL. Recovery must fail with `RESTORE FAILED` and exit nonzero.
A setup failure also exits nonzero, so the CI negative check requires the archive
deletion and recovery-failure reports as evidence.

The source initializes 2,000 tanks, 6,000 observations and 8,000 measurements.
An on-demand plugin base backup uses the production ScheduledBackup's method
and plugin configuration. Inserts, updates and deletes follow the base backup.
A named restore point and explicit WAL switch define the recovery target; the
drill waits for that segment in S3. A fresh Cluster recovers the selected backup
through `externalClusters.plugin` and replays WAL to the named restore point.
The target has no writer or streaming connection to the source.

`RESTORE DURATION` measures elapsed monotonic time from target creation through
Cluster readiness and the successful fingerprint comparison. It gives an RTO
indication for the synthetic data and local machine. The production RTO target
of four hours requires measurement on the production restore-test environment.

## Isolation and cleanup

Each invocation creates a randomly named `restore-drill-*` Docker network, kind
cluster and digest-pinned RustFS container. S3 publishes a Docker-assigned free
port on `127.0.0.1`. The database pods reach only this store through its private
network address. Temporary random credentials and a private kubeconfig live
outside Git. Every kubectl call specifies that kubeconfig and kind context.
The store uses ephemeral container storage and has no Compliance lock.

An exit cleanup block also handles exceptions, SIGINT and SIGTERM. It deletes
the target Cluster and verifies its absence while the API is available, deletes
kind, removes the S3 container and its anonymous volumes, and removes the network.
It checks kind and Docker inventories for surviving resources. A cleanup error
makes the drill fail. SIGKILL or host failure cannot execute process cleanup;
the independent CI cleanup check detects surviving drill resources.

The monthly/manual `restore-drill.yml` workflow runs both modes, verifies cleanup
in an `always()` step and uploads their logs. It uses synthetic data and temporary
local credentials. Existing compose services, development databases and legacy
data are outside the drill's resource ownership.

## Production restore-test environment

The production restore-test environment is out-of-band and operator-only, as
specified in [ADR-0013](../../docs/adr/0013-backup-and-disaster-recovery.md).
Agents and CI have no access to it or its backup credentials. The operator
restores real production backups into a separate, strictly access-controlled,
ephemeral environment, validates recovery and removes it. Real PII exists there
only transiently. The synthetic CI drill verifies the recovery mechanism;
production data validation and independent-provider-copy verification require
the operator's separate drill.

## Recovery contract sources

- [Barman Cloud 0.15.1 plugin recovery example](https://github.com/cloudnative-pg/plugin-barman-cloud/blob/v0.15.1/hack/examples/cluster-restore.yaml)
- [CloudNativePG 1.30.1 recovery targets](https://github.com/cloudnative-pg/cloudnative-pg/blob/v1.30.1/docs/src/recovery.md)
