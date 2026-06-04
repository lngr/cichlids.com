# Authoritative master copy of media originals, independent of any serving provider so
# the serving store can be re-seeded after a provider loss/ban (ADR-0009). Versioning is
# always on; Object Lock (Governance) is enabled for production via var.enable_object_lock.
resource "aws_s3_bucket" "media_master" {
  bucket              = var.media_master_bucket_name
  object_lock_enabled = var.enable_object_lock
  tags                = local.critical_labels

  lifecycle {
    prevent_destroy = true
  }
}

resource "aws_s3_bucket_versioning" "media_master" {
  bucket = aws_s3_bucket.media_master.id
  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_object_lock_configuration" "media_master" {
  count  = var.enable_object_lock ? 1 : 0
  bucket = aws_s3_bucket.media_master.id
  rule {
    default_retention {
      mode = "GOVERNANCE"
      days = var.media_master_retention_days
    }
  }
}

# Database PITR backups. Object Lock (Compliance) makes backups immutable for the
# retention window — not even a privileged credential can shorten or delete them
# (ADR-0013 retention, ADR-0014 Layer 1/4).
resource "aws_s3_bucket" "backup" {
  bucket              = var.backup_bucket_name
  object_lock_enabled = var.enable_object_lock
  tags                = local.critical_labels

  lifecycle {
    prevent_destroy = true
  }
}

resource "aws_s3_bucket_versioning" "backup" {
  bucket = aws_s3_bucket.backup.id
  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_object_lock_configuration" "backup" {
  count  = var.enable_object_lock ? 1 : 0
  bucket = aws_s3_bucket.backup.id
  rule {
    default_retention {
      mode = "COMPLIANCE"
      days = var.backup_retention_days
    }
  }
}
