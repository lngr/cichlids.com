# Outputs consumed by the app tier (over a data source / remote state) to provision k3s
# and the in-cluster workloads without the app tier referencing the protected resources
# directly (ADR-0014 Layer 2).
output "node_ipv4" {
  description = "Public IPv4 address of the k3s node."
  value       = hcloud_server.node.ipv4_address
}

output "node_id" {
  description = "Hetzner Cloud ID of the node."
  value       = hcloud_server.node.id
}

output "database_volume_id" {
  description = "Hetzner Cloud ID of the database block volume."
  value       = hcloud_volume.database.id
}

output "media_master_bucket" {
  description = "Name of the media-master bucket."
  value       = aws_s3_bucket.media_master.bucket
}

output "dns_zone_id" {
  description = "Hetzner DNS zone ID."
  value       = hetznerdns_zone.primary.id
}

output "dns_zone_nameservers" {
  description = "Nameservers to delegate the domain to."
  value       = hetznerdns_zone.primary.ns
}
