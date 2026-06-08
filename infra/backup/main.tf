# Common labels. Every backup bucket carries critical="true" so the fail-closed plan-diff
# policy gate can refuse any delete/replace of it independently of which state tier it lives in
# (ADR-0014 Layer 1/3). tier="backup" marks the isolated backup project (ADR-0017).
locals {
  common_labels = {
    project    = var.project_name
    managed-by = "opentofu"
    tier       = "backup"
  }

  critical_labels = merge(local.common_labels, {
    critical = "true"
  })
}
