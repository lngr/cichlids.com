# Foundation (stateful) tier: the rarely-changed cloud resources whose loss would be
# catastrophic — the compute node and its disk, the database volume, the object-storage
# buckets, and the DNS zone. Kept in its own state and applied out-of-band by the
# operator, separate from the ephemeral app tier (ADR-0010, ADR-0014 Layer 2).
# Story: task-2.2
terraform {
  required_version = ">= 1.10.0"

  required_providers {
    hcloud = {
      source  = "hetznercloud/hcloud"
      version = "~> 1.49"
    }
    hetznerdns = {
      source  = "germanbrew/hetznerdns"
      version = ">= 3.0.0"
    }
    # Hetzner Object Storage is S3-compatible and is managed through the AWS provider
    # pointed at the Hetzner endpoint (provider portability per ADR-0009).
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }
}
