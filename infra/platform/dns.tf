# Authoritative DNS zone, managed through the project-scoped Hetzner Cloud DNS API. The zone is a
# protected platform resource with both delete_protection (provider-side) and prevent_destroy
# (ADR-0014 Layer 1).
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

# Web and authentication hosts use the node IPv4 address.
# The *.dev wildcard resolves preview hosts for every pull request.
resource "hcloud_zone_rrset" "ingress" {
  for_each = toset(["@", "staging", "*.dev", "auth", "auth.staging"])

  zone    = hcloud_zone.primary.id
  name    = each.value
  type    = "A"
  ttl     = 300
  records = [{ value = hcloud_server.node.ipv4_address }]

  lifecycle {
    prevent_destroy = true
  }
}
