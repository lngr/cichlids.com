output "database_backup_bucket" {
  description = "Name of the database PITR/WAL backup bucket."
  value       = aws_s3_bucket.database_backup.bucket
}

output "media_master_backup_bucket" {
  description = "Name of the off-master media-originals backup bucket."
  value       = aws_s3_bucket.media_master_backup.bucket
}
