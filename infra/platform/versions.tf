# Platform tier: the cichlids project's cloud resources (the compute node and its disk, the
# database volume, the media-master bucket, and the DNS zone), managed via OpenTofu under GitOps.
# Protected against accidental destruction (ADR-0014) and recoverable from the immutable backups
# in the separate backup project (ADR-0017).
# Story: task-2.2, task-2.22
terraform {
  required_version = ">= 1.12.0"

  required_providers {
    # The hcloud provider manages both the compute resources and the DNS zone: Hetzner DNS is
    # part of the project-scoped Cloud API, so the project token also authorises DNS and no
    # separate DNS provider or account-level token is needed (ADR-0017).
    hcloud = {
      source  = "hetznercloud/hcloud"
      version = "~> 1.64"
    }
    # Hetzner Object Storage is S3-compatible and is managed through the AWS provider
    # pointed at the Hetzner endpoint (provider portability per ADR-0009).
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
}
