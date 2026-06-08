# AWS provider pointed at the backup project's Hetzner Object Storage. This tier holds NO
# Hetzner Cloud token: the backup project's capability boundary (ADR-0017) means automation
# never receives a cloud token for it, and only a write-scoped object-storage credential reaches
# these buckets. Compliance-mode immutability then prevents deletion even by that credential.
# The credential-validation and metadata lookups specific to real AWS are skipped so the
# S3-compatible endpoint is used purely over the standard S3 API (ADR-0009).
provider "aws" {
  access_key = var.s3_access_key
  secret_key = var.s3_secret_key
  region     = var.s3_region

  s3_use_path_style           = true
  skip_credentials_validation = true
  skip_requesting_account_id  = true
  skip_metadata_api_check     = true
  skip_region_validation      = true

  endpoints {
    s3 = var.s3_endpoint
  }
}
