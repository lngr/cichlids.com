# Barman uses HeadBucket/ListObjectsV2, GetObject/HeadObject, PutObject and multipart uploads.
# CreateMultipartUpload, UploadPart and CompleteMultipartUpload use s3:PutObject permission.
# AbortMultipartUpload releases failed uploads. Bucket administration belongs to the operator.
resource "aws_s3_bucket_policy" "database_writer" {
  bucket = aws_s3_bucket.database_backup.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid       = "WriterBucketRead"
        Effect    = "Allow"
        Principal = { AWS = "arn:aws:iam:::user/p${var.database_writer_primary_project_id}:${var.database_writer_access_key_id}" }
        Action    = ["s3:ListBucket"]
        Resource  = aws_s3_bucket.database_backup.arn
      },
      {
        Sid       = "WriterObjects"
        Effect    = "Allow"
        Principal = { AWS = "arn:aws:iam:::user/p${var.database_writer_primary_project_id}:${var.database_writer_access_key_id}" }
        Action    = ["s3:GetObject", "s3:PutObject", "s3:AbortMultipartUpload"]
        Resource  = "${aws_s3_bucket.database_backup.arn}/*"
      }
    ]
  })
}

# Expiration creates a delete marker; noncurrent expiry removes unlocked versions afterwards.
# Object Lock takes precedence over lifecycle deletion for each protected object version.
resource "aws_s3_bucket_lifecycle_configuration" "database_backup" {
  bucket = aws_s3_bucket.database_backup.id
  rule {
    id     = "database-expiry"
    status = "Enabled"
    filter {
      prefix = ""
    }
    expiration {
      days = var.database_backup_retention_days
    }
    noncurrent_version_expiration {
      noncurrent_days = 1
    }
    abort_incomplete_multipart_upload {
      days_after_initiation = 7
    }
  }
  rule {
    id     = "database-delete-markers"
    status = "Enabled"
    filter {
      prefix = ""
    }
    expiration {
      expired_object_delete_marker = true
    }
  }
  depends_on = [aws_s3_bucket_versioning.database_backup, aws_s3_bucket_object_lock_configuration.database_backup]
}
