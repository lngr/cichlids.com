resource "hcloud_ssh_key" "operator" {
  name       = "${var.project_name}-operator"
  public_key = var.ssh_public_key
  labels     = local.common_labels
}

# The single k3s node. delete_protection refuses provider-side deletion until explicitly
# removed, and prevent_destroy makes destroy/replace fail in OpenTofu (ADR-0014 Layer 1).
# k3s itself is installed by the ephemeral app tier over SSH, so the node can be
# re-provisioned without recreating this protected resource.
resource "hcloud_server" "node" {
  name        = "${var.project_name}-node"
  server_type = var.server_type
  image       = var.server_image
  location    = var.location
  ssh_keys    = [hcloud_ssh_key.operator.id]
  labels      = local.critical_labels

  delete_protection  = true
  rebuild_protection = true

  lifecycle {
    prevent_destroy = true
  }
}

# Dedicated block volume for the database, kept distinct from the node disk so it is an
# independently protected stateful resource (ADR-0013, ADR-0014).
resource "hcloud_volume" "database" {
  name              = "${var.project_name}-db"
  size              = var.db_volume_size
  location          = var.location
  format            = "ext4"
  delete_protection = true
  labels            = local.critical_labels

  lifecycle {
    prevent_destroy = true
  }
}

resource "hcloud_volume_attachment" "database" {
  volume_id = hcloud_volume.database.id
  server_id = hcloud_server.node.id
  automount = false
}
