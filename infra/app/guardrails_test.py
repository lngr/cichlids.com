#!/usr/bin/env python3
"""Asserts the app tier (ephemeral tier, ADR-0010, ADR-0014 Layer 2) stays separate from the
foundation tiers and holds no destroy-capable credential: no cloud or object-storage provider,
no data blocks, no remote state lookups, no variable that carries a token, a remote backend with
its own state key and encrypted state, and every SSH connection verifying the pinned node host
key. Run before the tier exists it fails (red); with the tier in place it passes (green).

Parses the .tf sources directly so it needs no credentials and runs offline in CI.
`--self-test` exercises the checks against inline fixtures so the guard cannot silently rot.
Story: task-2.4
"""
import glob
import os
import re
import sys
import subprocess

HERE = os.path.dirname(os.path.abspath(__file__))

FORBIDDEN_PROVIDERS = ("hcloud", "aws")
REQUIRED_STATE_KEY = "app/terraform.tfstate"
CONNECTION_REQUIREMENTS = {
    "host_key": "var.node_host_key",
    "host": "var.node_ipv4",
    "private_key": "var.ssh_private_key",
    "user": "var.ssh_user",
}


def read_tf(directory):
    text = []
    for path in sorted(glob.glob(os.path.join(directory, "*.tf"))):
        with open(path, encoding="utf-8") as fh:
            text.append(fh.read())
    return "\n".join(text)


def strip_comments(text):
    return re.sub(r"(?m)^\s*(#|//).*$", "", text)


def blocks(text, header_regex):
    """Return the bodies of every brace-matched block whose header matches header_regex."""
    bodies = []
    for m in re.finditer(header_regex + r"\s*\{", text):
        depth, i = 1, m.end()
        while i < len(text) and depth > 0:
            if text[i] == "{":
                depth += 1
            elif text[i] == "}":
                depth -= 1
            i += 1
        bodies.append(text[m.end():i - 1])
    return bodies


def check(text):
    text = strip_comments(text)
    failures = []

    for prov in FORBIDDEN_PROVIDERS:
        if re.search(r'provider\s+"' + prov + r'"', text):
            failures.append(f'provider "{prov}" block present (the app tier holds no cloud or storage credential)')
        for body in blocks(text, r"required_providers"):
            if re.search(r"(?m)^\s*" + prov + r"\s*=", body):
                failures.append(f"required_providers lists {prov}")

    if re.search(r'(?m)^\s*data\s+"', text):
        failures.append("data block present (the app tier references no foundation data)")
    if "terraform_remote_state" in text:
        failures.append("terraform_remote_state present (the app tier reads no other tier's state)")

    for m in re.finditer(r'variable\s+"([^"]+)"', text):
        if "token" in m.group(1).lower():
            failures.append(f'variable "{m.group(1)}" looks like a token')

    for addr in re.findall(r"\b(?:hcloud|aws)_[a-z0-9_]+\.[a-z0-9_]+", text):
        failures.append(f"foundation resource address referenced: {addr}")

    backend = blocks(text, r'backend\s+"s3"')
    if not backend:
        failures.append('no remote backend "s3" block')
    elif not re.search(r'key\s*=\s*"' + re.escape(REQUIRED_STATE_KEY) + '"', backend[0]):
        failures.append(f"backend state key is not {REQUIRED_STATE_KEY}")

    if not blocks(text, r"\bencryption") or "state_encryption_passphrase" not in text:
        failures.append("native state encryption block with state_encryption_passphrase is missing")

    connections = blocks(text, r"\bconnection")
    if not connections:
        failures.append("no connection block found")
    for body in connections:
        for attr, want in CONNECTION_REQUIREMENTS.items():
            if not re.search(r"(?m)^\s*" + attr + r"\s*=\s*" + re.escape(want) + r"\s*$", body):
                failures.append(f"connection block lacks {attr} = {want}")
        if not re.search(r"(?m)^\s*timeout\s*=", body):
            failures.append("connection block lacks a timeout")

    k3s = blocks(text, r'resource\s+"terraform_data"\s+"k3s"')
    if not k3s or not all(part in k3s[0] for part in [
            'var.database_volume_id', 'scripts/mount-database.sh', 'postgres/storage.yaml',
            'mount-database.sh', 'storage.yaml']):
        failures.append("database mount and static storage bootstrap are missing")
    elif not (0 <= k3s[0].find('bash ${local.remote_dir}/mount-database.sh') < k3s[0].find('bash ${local.remote_dir}/install-k3s.sh')):
        failures.append("database mount must precede k3s installation")
    return failures


GOOD = '''
terraform {
  backend "s3" {
    bucket = "b"
    key    = "app/terraform.tfstate"
  }
  encryption {
    key_provider "pbkdf2" "operator" {
      passphrase = var.state_encryption_passphrase
    }
  }
  required_providers {
    other = { source = "x/y" }
  }
}
variable "ssh_private_key" {}
resource "terraform_data" "k3s" {
  triggers_replace = [var.database_volume_id, "scripts/mount-database.sh", "postgres/storage.yaml"]
  provisioner "remote-exec" {
    inline = ["bash ${local.remote_dir}/mount-database.sh", "bash ${local.remote_dir}/install-k3s.sh", "storage.yaml"]
  }
  connection {
    type        = "ssh"
    host        = var.node_ipv4
    user        = var.ssh_user
    private_key = var.ssh_private_key
    host_key    = var.node_host_key
    timeout     = "5m"
  }
}
'''


def self_test():
    cases = [
        ("good", GOOD, 0),
        ("missing mount", GOOD.replace('scripts/mount-database.sh', 'scripts/other.sh'), 1),
        ("missing mount invocation", GOOD.replace('bash ${local.remote_dir}/mount-database.sh', 'echo mount-database.sh'), 1),
        ("missing volume input", GOOD.replace('var.database_volume_id', 'var.other'), 1),
        ("mount order", GOOD.replace('inline = ["bash ${local.remote_dir}/mount-database.sh", "bash ${local.remote_dir}/install-k3s.sh"',
                                    'inline = ["bash ${local.remote_dir}/install-k3s.sh", "bash ${local.remote_dir}/mount-database.sh"'), 1),
        ("hcloud provider", GOOD + '\nprovider "hcloud" {}\n', 1),
        ("aws required provider",
         GOOD.replace('other = { source = "x/y" }', 'aws = { source = "hashicorp/aws" }'), 1),
        ("data block", GOOD + '\ndata "hcloud_server" "n" {}\n', 2),
        ("remote state", GOOD + '\ndata "terraform_remote_state" "p" {}\n', 2),
        ("token variable", GOOD + '\nvariable "hcloud_token" {}\n', 1),
        ("foundation address", GOOD + '\nlocals { x = hcloud_server.node.ipv4_address }\n', 1),
        ("wrong key", GOOD.replace("app/terraform.tfstate", "platform/terraform.tfstate"), 1),
        ("no encryption", GOOD.replace("encryption {", "other_block {"), 1),
        ("no host key", GOOD.replace("    host_key    = var.node_host_key\n", ""), 1),
        ("no timeout", GOOD.replace('    timeout     = "5m"\n', ""), 1),
        ("literal host", GOOD.replace("var.node_ipv4", '"1.2.3.4"'), 1),
    ]
    errors = []
    for label, text, want in cases:
        got = len(check(text))
        if (want == 0) != (got == 0):
            errors.append(f"{label}: expected {'pass' if want == 0 else 'failure'}, got {got} failure(s)")
    if errors:
        print("app guardrails self-test FAILED:", file=sys.stderr)
        for e in errors:
            print(f"  - {e}", file=sys.stderr)
        return 1
    print("app guardrails self-test OK")
    return 0


def main(argv):
    mount = subprocess.run([sys.executable, os.path.join(HERE, "scripts/mount-database.test.py")])
    if mount.returncode:
        return mount.returncode
    bootstrap = subprocess.run([sys.executable, os.path.join(HERE, "scripts/install-k3s.test.py")])
    if bootstrap.returncode:
        return bootstrap.returncode
    if "--self-test" in argv:
        return self_test()
    failures = check(read_tf(HERE))
    if failures:
        print("app guardrails missing (ADR-0010, ADR-0014 Layer 2):", file=sys.stderr)
        for f in failures:
            print(f"  - {f}", file=sys.stderr)
        return 1
    print("app guardrails OK")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
