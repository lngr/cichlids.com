# Backup (isolated) tier: the immutable backups of last resort — the database PITR/WAL backups
# and an off-master copy of the media originals. It lives in its own Hetzner project so no cloud
# token can reach it (ADR-0017); the tier therefore manages only object-storage buckets through
# the AWS provider pointed at the backup project's Hetzner endpoint, and declares no Hetzner
# Cloud provider at all (ADR-0009 provider portability, ADR-0013 durability).
# Story: task-2.16
terraform {
  required_version = ">= 1.12.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
}
