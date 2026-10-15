# Story: task-2.4
# Installs single-node k3s on the foundation node and bootstraps Argo CD over SSH. The node is
# created by the platform tier without cloud-init and is protected against replacement, so a
# change to the bootstrap re-runs these provisioners on the running node. The scripts are
# idempotent and run again whenever the pinned version or a file changes.

locals {
  remote_dir = "/opt/cichlids-bootstrap"
}

resource "terraform_data" "k3s" {
  triggers_replace = [
    filesha256("${path.module}/main.tf"),
    var.k3s_version,
    var.database_volume_id,
    filesha256("${path.module}/scripts/mount-database.sh"),
    filesha256("${path.module}/../../apps/postgres/storage.yaml"),
    filesha256("${path.module}/scripts/install-k3s.sh"),
  ]

  connection {
    type        = "ssh"
    host        = var.node_ipv4
    user        = var.ssh_user
    private_key = var.ssh_private_key
    host_key    = var.node_host_key
    timeout     = "5m"
  }

  provisioner "remote-exec" {
    inline = ["mkdir -p ${local.remote_dir}"]
  }

  provisioner "file" {
    source      = "${path.module}/scripts/install-k3s.sh"
    destination = "${local.remote_dir}/install-k3s.sh"
  }

  provisioner "file" {
    source      = "${path.module}/scripts/mount-database.sh"
    destination = "${local.remote_dir}/mount-database.sh"
  }

  provisioner "file" {
    source      = "${path.module}/../../apps/postgres/storage.yaml"
    destination = "${local.remote_dir}/storage.yaml"
  }

  provisioner "remote-exec" {
    inline = [
      "bash ${local.remote_dir}/mount-database.sh '${var.database_volume_id}'",
      "install -d -m 700 -o 65532 -g 65532 /var/lib/cichlids-headscale",
      "bash ${local.remote_dir}/install-k3s.sh '${var.k3s_version}'",
      "KUBECONFIG=/etc/rancher/k3s/k3s.yaml kubectl label node --all cichlids.com/database-volume=true cichlids.com/headscale-state=true --overwrite",
      "KUBECONFIG=/etc/rancher/k3s/k3s.yaml kubectl apply -f ${local.remote_dir}/storage.yaml",
    ]
  }
}

resource "terraform_data" "argocd" {
  depends_on = [terraform_data.k3s]

  triggers_replace = [
    var.argocd_version,
    filesha256("${path.module}/scripts/install-argocd.sh"),
    filesha256("${path.module}/../../clusters/cichlids/root.yaml"),
    sha256(join("", [for file in sort(tolist(fileset("${path.module}/argocd", "*.yaml"))) : "${file}:${filesha256("${path.module}/argocd/${file}")}"])),
  ]

  connection {
    type        = "ssh"
    host        = var.node_ipv4
    user        = var.ssh_user
    private_key = var.ssh_private_key
    host_key    = var.node_host_key
    timeout     = "5m"
  }

  provisioner "remote-exec" {
    inline = ["mkdir -p ${local.remote_dir}/argocd"]
  }

  provisioner "file" {
    source      = "${path.module}/scripts/install-argocd.sh"
    destination = "${local.remote_dir}/install-argocd.sh"
  }

  provisioner "file" {
    source      = "${path.module}/../../clusters/cichlids/root.yaml"
    destination = "${local.remote_dir}/root.yaml"
  }

  provisioner "file" {
    source      = "${path.module}/argocd/"
    destination = "${local.remote_dir}/argocd"
  }

  provisioner "remote-exec" {
    inline = ["bash ${local.remote_dir}/install-argocd.sh '${var.argocd_version}' ${local.remote_dir}/root.yaml ${local.remote_dir}/argocd"]
  }
}
