# Credentials are supplied at apply time via TF_VAR_* environment variables held by the
# operator (never committed). The routine token used here is least-privilege; the
# destroy-capable break-glass credential stays out-of-band (ADR-0014 Layer 2).

variable "hcloud_token" {
  description = "Hetzner Cloud API token (routine, least-privilege; no delete rights on protected resources)."
  type        = string
  sensitive   = true
}

variable "hetznerdns_token" {
  description = "Hetzner DNS API token for the authoritative zone."
  type        = string
  sensitive   = true
}

variable "s3_access_key" {
  description = "Access key for Hetzner Object Storage (S3-compatible)."
  type        = string
  sensitive   = true
}

variable "s3_secret_key" {
  description = "Secret key for Hetzner Object Storage (S3-compatible)."
  type        = string
  sensitive   = true
}

variable "project_name" {
  description = "Short project slug used to name and label resources."
  type        = string
  default     = "cichlids"
}

# Development runs on a single small node in an EU location for cost; the production
# node moves to a US location at go-live by overriding these (ADR-0009). Server type and
# location are deliberately variables so scaling or relocating is a one-line change,
# not a re-architecture (ADR-0010).
variable "location" {
  description = "Hetzner location for the node and volume (e.g. nbg1, fsn1, hel1, ash, hil)."
  type        = string
  default     = "nbg1"
}

variable "server_type" {
  description = "Hetzner server type for the single k3s node."
  type        = string
  default     = "cx33"
}

variable "server_image" {
  description = "Base OS image for the node."
  type        = string
  default     = "ubuntu-24.04"
}

variable "ssh_public_key" {
  description = "Operator SSH public key authorised on the node for app-tier provisioning."
  type        = string
}

variable "db_volume_size" {
  description = "Size in GB of the dedicated block volume backing the database (separate, protected stateful resource)."
  type        = number
  default     = 10
}

variable "dns_zone_name" {
  description = "Authoritative DNS zone managed in Hetzner DNS."
  type        = string
  default     = "cichlids.com"
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

variable "media_master_bucket_name" {
  description = "Bucket holding the authoritative master copy of media originals (ADR-0009)."
  type        = string
  default     = "cichlids-media-master"
}

# Object Lock can only be enabled at bucket creation and, once locked with a retention,
# makes objects undeletable for the window. It is left OFF in development (the bucket holds
# throwaway/synthetic data) and turned ON for production at go-live in Governance mode, so a
# privileged break-glass action can still erase objects for GDPR while routine credentials
# cannot (ADR-0013, ADR-0014). The immutable Compliance-mode backup copies live in the separate
# backup project (ADR-0017).
variable "enable_object_lock" {
  description = "Enable S3 Object Lock on the media-master bucket (production posture; off for development)."
  type        = bool
  default     = false
}

variable "media_master_retention_days" {
  description = "Object Lock (Governance) retention window in days for the media-master bucket when object lock is enabled."
  type        = number
  default     = 14
}
