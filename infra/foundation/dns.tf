# Authoritative DNS zone. Records (ingress hosts, the *.dev wildcard for preview
# environments) are managed where they belong with the workloads that need them; the
# zone itself is a protected foundation resource (ADR-0014 Layer 1).
resource "hetznerdns_zone" "primary" {
  name = var.dns_zone_name
  ttl  = 3600

  lifecycle {
    prevent_destroy = true
  }
}
