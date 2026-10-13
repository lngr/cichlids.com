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

# The ingress hosts (apex, staging) and the *.dev wildcard of the preview environments point to
# the node, so a preview host resolves without a DNS change per pull request.
resource "hcloud_zone_rrset" "ingress" {
  for_each = toset(["@", "staging", "*.dev"])

  zone    = hcloud_zone.primary.id
  name    = each.value
  type    = "A"
  ttl     = 300
  records = [{ value = hcloud_server.node.ipv4_address }]

  lifecycle {
    prevent_destroy = true
  }
}
