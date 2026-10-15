variable "node_ipv4" {
  description = "Public IPv4 address of the foundation node."
  type        = string

  validation {
    condition     = can(regex("^[0-9.]+$", var.node_ipv4)) && can(cidrnetmask("${var.node_ipv4}/32"))
    error_message = "node_ipv4 must be a valid IPv4 address."
  }
}

variable "node_host_key" {
  description = "Pinned SSH host key of the node, as the public key line (ssh-ed25519 AAAA...) without the host prefix. Every connection verifies it."
  type        = string
}

variable "ssh_private_key" {
  description = "Private key of the dedicated CI key pair that is authorised on the node."
  type        = string
  sensitive   = true
}

variable "ssh_user" {
  description = "SSH user on the node."
  type        = string
  default     = "root"
}

variable "k3s_version" {
  description = "Pinned k3s release."
  type        = string
  default     = "v1.36.5+k3s1"
}

variable "argocd_version" {
  description = "Pinned Argo CD release."
  type        = string
  default     = "v3.5.5"
}

variable "database_volume_id" {
  description = "Numeric Hetzner ID of the attached, platform-formatted ext4 database volume."
  type        = string

  validation {
    condition     = can(regex("^[1-9][0-9]*$", var.database_volume_id))
    error_message = "database_volume_id must be a positive numeric volume ID."
  }
}

variable "tailscale_auth_key" {
  description = "Headscale pre-auth key for optional node enrollment. Empty skips enrollment."
  type        = string
  sensitive   = true
  default     = ""
}
