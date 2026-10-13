variable "node_ipv4" {
  description = "Public IPv4 address of the foundation node."
  type        = string
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
