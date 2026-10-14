# Credentials are supplied at apply time via TF_VAR_* environment variables held by the
# operator (never committed). This tier deliberately has NO Hetzner Cloud token: the backup
# project holds no cloud resources, only object storage, so a write-scoped object-storage
# credential is all that ever reaches it (ADR-0017).

variable "s3_access_key" {
  description = "Access key for the backup project's Hetzner Object Storage (S3-compatible)."
  type        = string
  sensitive   = true
}

variable "s3_secret_key" {
  description = "Secret key for the backup project's Hetzner Object Storage (S3-compatible)."
  type        = string
  sensitive   = true
}

variable "project_name" {
  description = "Short slug used to label resources, naming the backup project they belong to."
  type        = string
  default     = "cichlids-backup"
}

variable "s3_endpoint" {
  description = "Hetzner Object Storage S3 endpoint for the chosen location."
  type        = string
  default     = "https://nbg1.your-objectstorage.com"
}

variable "s3_region" {
  description = "Region label sent to the S3 API (Hetzner ignores it but the AWS provider requires one)."
  type        = string
  default     = "nbg1"
}

variable "database_backup_bucket_name" {
  description = "Bucket holding the database PITR/WAL backups (ADR-0013)."
  type        = string
  default     = "cichlids-db-backup"
}

variable "media_master_backup_bucket_name" {
  description = "Bucket holding the off-master immutable copy of the media originals (ADR-0009, ADR-0013)."
  type        = string
  default     = "cichlids-media-backup"
}

# Object Lock can only be enabled at bucket creation and, once locked with a retention, makes
# objects undeletable for the window. It is on by default so the backup buckets carry the
# production posture from creation, in Compliance mode for both: not even a privileged credential
# can shorten or delete a locked object (ADR-0013, ADR-0014 Layer 1).
variable "enable_object_lock" {
  description = "Enable S3 Object Lock (Compliance) on the backup buckets. On by default: the backups are immutable from creation."
  type        = bool
  default     = true
}

variable "database_backup_retention_days" {
  description = "Database Compliance lock and expiry in days: 400 covers monthly backups for 12 months with margin and at least 14 days of PITR (ADR-0013)."
  type        = number
  default     = 400
  validation {
    condition     = var.database_backup_retention_days >= 400 && floor(var.database_backup_retention_days) == var.database_backup_retention_days
    error_message = "Database backup retention must be an integer of at least 400 days."
  }
}

variable "media_master_backup_retention_days" {
  description = "Object Lock (Compliance) retention window in days for the media-master backup bucket when object lock is enabled (ADR-0013)."
  type        = number
  default     = 14
}

variable "database_writer_primary_project_id" {
  description = "Numeric Hetzner project ID of cichlids, the primary project owning the cluster writer key."
  type        = string
  validation {
    condition     = can(regex("^[0-9]+$", var.database_writer_primary_project_id))
    error_message = "Use the numeric primary project ID."
  }
}

variable "database_writer_access_key_id" {
  description = "Public access key ID of the dedicated primary-project cluster writer; its secret never enters this tier."
  type        = string
  validation {
    condition     = can(regex("^[A-Za-z0-9]+$", var.database_writer_access_key_id))
    error_message = "Use the dedicated writer access key ID."
  }
}
