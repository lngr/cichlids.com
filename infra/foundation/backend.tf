# Remote state on Hetzner Object Storage (S3-compatible), per ADR-0016. The state for this
# stateful tier is the only management handle to the protected resources, so it must be
# durable, lockable, and recoverable rather than a local file.
#
# The state bucket is dedicated and created out-of-band by the operator before `tofu init`;
# it is deliberately NOT one of the buckets this tier manages (media-master/backup), to avoid
# a chicken-and-egg dependency. It has versioning on and Object Lock OFF: an Object-Lock
# retention would make the lock object undeletable and cause stuck locks. Locking uses the
# backend's native S3 lock file (use_lockfile); no separate lock database is needed.
#
# Non-secret backend settings (bucket, endpoint, region) are fixed here for the development
# default and can be overridden at init with `-backend-config`. Access keys are supplied from
# the environment (AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY) at init time, never committed.
terraform {
  backend "s3" {
    bucket = "cichlids-tfstate"
    key    = "foundation/terraform.tfstate"
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

# Native state encryption (defense in depth): OpenTofu state stores secrets in cleartext
# (provider tokens, S3 keys, generated passwords). The passphrase is supplied out-of-band by
# the operator via TF_VAR_state_encryption_passphrase and is never stored in the repo or CI.
# The empty default keeps `validate` runnable without the key; a real plan/apply requires a
# passphrase of at least 16 characters.
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

# Story: task-2.13
