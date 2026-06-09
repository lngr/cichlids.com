# Remote state on Hetzner Object Storage (S3-compatible), per ADR-0016. The backup tier's state
# lives in its own out-of-band bucket in the backup project, so the operator-held backup setup
# stays isolated from the CI-managed cichlids state. The state bucket is created by hand and is
# not one of the buckets this tier manages (the two backup buckets), avoiding a chicken-and-egg
# dependency. Both the backend and the provider use the backup project's object-storage key
# (AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY at init). Versioning on, Object Lock OFF (a retention
# would make the lock object undeletable); locking uses the backend's native S3 lock file.
terraform {
  backend "s3" {
    bucket = "cichlids-backup-tfstate"
    key    = "backup/terraform.tfstate"
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

# Native state encryption (defense in depth): OpenTofu state stores secrets in cleartext (the
# object-storage keys). The passphrase is supplied out-of-band by the operator via
# TF_VAR_state_encryption_passphrase and is never stored in the repo or CI. The empty default
# keeps `validate` runnable without the key; a real plan/apply requires a passphrase of at least
# 16 characters.
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
  description = "Passphrase for native OpenTofu state encryption (>= 16 chars at apply time). Supplied out-of-band via TF_VAR_state_encryption_passphrase; never committed (ADR-0016)."
  type        = string
  sensitive   = true
  default     = ""
}
