# Authoritative DNS and ingress records use the platform node IPv4 address.
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
