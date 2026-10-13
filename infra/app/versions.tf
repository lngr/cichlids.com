# Story: task-2.4
# App tier: the software on the foundation node (k3s and the Argo CD bootstrap), applied over SSH
# with the built-in terraform_data resource. It needs no provider and holds no cloud credential,
# so routine automation cannot destroy cloud resources through it (ADR-0014 Layer 2).
terraform {
  required_version = ">= 1.12.0"
}
