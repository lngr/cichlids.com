# Authoritative DNS zone, managed through the project-scoped Hetzner Cloud DNS API. Records
# (ingress hosts, the *.dev wildcard for preview environments) are managed where they belong
# with the workloads that need them; the zone itself is a protected platform resource with
# both delete_protection (provider-side) and prevent_destroy (ADR-0014 Layer 1).
resource "hcloud_zone" "primary" {
  name = var.dns_zone_name
  mode = "primary"
  ttl  = 3600

  delete_protection = true
  labels            = local.critical_labels

  lifecycle {
    prevent_destroy = true
  }
}
