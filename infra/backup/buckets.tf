# Database PITR/WAL backups. Object Lock (Compliance) makes backups immutable for the retention
# window — not even a privileged credential can shorten or delete them (ADR-0013 retention,
# ADR-0014 Layer 1/4). Versioning is always on; the lock is enabled for production via
# var.enable_object_lock (it can only be set at bucket creation).
# No tags: Hetzner Object Storage does not implement bucket tagging (PutBucketTagging returns 501).
# The bucket is identified by name and is critical to the policy gate by resource type.
resource "aws_s3_bucket" "database_backup" {
  bucket              = var.database_backup_bucket_name
  object_lock_enabled = var.enable_object_lock

  lifecycle {
    prevent_destroy = true
  }
}

resource "aws_s3_bucket_versioning" "database_backup" {
  bucket = aws_s3_bucket.database_backup.id
  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_object_lock_configuration" "database_backup" {
  count  = var.enable_object_lock ? 1 : 0
  bucket = aws_s3_bucket.database_backup.id
  rule {
    default_retention {
      mode = "COMPLIANCE"
      days = var.database_backup_retention_days
    }
  }
}

# Off-master immutable copy of the media originals, independent of the live media-master in the
# primary project, so the originals survive even the loss of that whole project (ADR-0009 master
# copy, ADR-0013 durability). Compliance-mode immutability, unlike the live master's Governance
# mode, makes it undeletable for the retention window even by a privileged credential.
resource "aws_s3_bucket" "media_master_backup" {
  bucket              = var.media_master_backup_bucket_name
  object_lock_enabled = var.enable_object_lock

  lifecycle {
    prevent_destroy = true
  }
}

resource "aws_s3_bucket_versioning" "media_master_backup" {
  bucket = aws_s3_bucket.media_master_backup.id
  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_object_lock_configuration" "media_master_backup" {
  count  = var.enable_object_lock ? 1 : 0
  bucket = aws_s3_bucket.media_master_backup.id
  rule {
    default_retention {
      mode = "COMPLIANCE"
      days = var.media_master_backup_retention_days
    }
  }
}
