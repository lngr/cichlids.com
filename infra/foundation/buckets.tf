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
