# Remote state on Hetzner Object Storage (S3-compatible), per ADR-0016. The app tier keeps its own
# state object in the platform state bucket, separate from the platform tier's key. The bucket is
# created out-of-band. Access keys come from the environment (AWS_ACCESS_KEY_ID /
# AWS_SECRET_ACCESS_KEY) at init time, never committed.
terraform {
  backend "s3" {
    bucket = "cichlids-platform-tfstate"
    key    = "app/terraform.tfstate"
    region = "nbg1"

    endpoints = {
      s3 = "https://nbg1.your-objectstorage.com"
    }

    # Hetzner Object Storage is S3-compatible but not AWS: skip the AWS-only validations and
    # use path-style addressing so the standard S3 backend talks to the Hetzner endpoint.
    skip_credentials_validation = true
    skip_requesting_account_id  = true
    skip_metadata_api_check     = true
    skip_region_validation      = true
    use_path_style              = true

    # Native S3 state locking via a lock object in the same bucket (OpenTofu >= 1.10).
    use_lockfile = true
  }
}

# Native state encryption: the state holds the SSH private key passed to the connection blocks.
# The tier has its own passphrase, supplied out-of-band via TF_VAR_state_encryption_passphrase
# and never shared with another tier. The empty default keeps `validate` runnable without the
# key; a real plan/apply requires a passphrase of at least 16 characters.
terraform {
  encryption {
    key_provider "pbkdf2" "operator" {
      passphrase = var.state_encryption_passphrase
    }

    method "aes_gcm" "operator" {
      keys = key_provider.pbkdf2.operator
    }

    state {
      method = method.aes_gcm.operator
    }

    plan {
      method = method.aes_gcm.operator
    }
  }
}

variable "state_encryption_passphrase" {
  description = "Passphrase for native OpenTofu state encryption of the app tier (>= 16 chars at apply time). Supplied out-of-band via TF_VAR_state_encryption_passphrase; never committed (ADR-0016)."
  type        = string
  sensitive   = true
  default     = ""
}
