provider "hcloud" {
  token = var.hcloud_token
}

provider "hetznerdns" {
  api_token = var.hetznerdns_token
}

# AWS provider pointed at Hetzner Object Storage. The credential-validation and
# metadata lookups specific to real AWS are skipped so the S3-compatible endpoint is
# used purely over the standard S3 API (ADR-0009 provider portability).
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
