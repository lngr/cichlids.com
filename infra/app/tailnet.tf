resource "terraform_data" "tailnet" {
  depends_on = [terraform_data.k3s]

  triggers_replace = [
    filesha256("${path.module}/tailnet.tf"),
    var.node_ipv4,
    sha256(var.tailscale_auth_key),
    filesha256("${path.module}/scripts/install-tailscale.sh"),
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
    inline = ["install -d -m 700 ${local.remote_dir}/tailnet"]
  }

  provisioner "file" {
    source      = "${path.module}/scripts/install-tailscale.sh"
    destination = "${local.remote_dir}/tailnet/install-tailscale.sh"
  }

  provisioner "file" {
    content     = var.tailscale_auth_key
    destination = "${local.remote_dir}/tailnet/auth-key"
  }

  provisioner "remote-exec" {
    inline = ["bash ${local.remote_dir}/tailnet/install-tailscale.sh '${var.node_ipv4}' ${local.remote_dir}/tailnet/auth-key"]
  }
}
