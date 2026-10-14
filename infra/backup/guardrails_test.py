#!/usr/bin/env python3
"""Asserts the day-one Layer 1 safeguards (ADR-0014) on the backup project's immutable buckets:
lifecycle prevent_destroy and versioning on both the database PITR backup and the media-master
backup copy. These buckets hold the irreplaceable data of last resort (ADR-0013), kept in their
own project so no automation cloud token can reach them (ADR-0017). Run before adding the
safeguards it fails (red); with them in place it passes (green).

Parses the .tf sources directly (brace-matched resource blocks) so it needs no cloud
credentials and runs offline in CI.
Story: task-2.16
"""
import contextlib
import copy
import io
import importlib.util
from pathlib import Path
import glob
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))

# (resource type, resource name) — every critical backup bucket. Buckets carry no Hetzner
# delete-protection flag (that is a Cloud-resource attribute); their immutability comes from
# versioning plus Object Lock Compliance and the prevent_destroy lifecycle.
CRITICAL = [
    ("aws_s3_bucket", "database_backup"),
    ("aws_s3_bucket", "media_master_backup"),
]


def load_blocks():
    """Return {(type, name): block_body} for every resource block in the directory."""
    blocks = {}
    for path in glob.glob(os.path.join(HERE, "*.tf")):
        with open(path, encoding="utf-8") as fh:
            text = fh.read()
        for m in re.finditer(r'resource\s+"([^"]+)"\s+"([^"]+)"\s*\{', text):
            rtype, rname = m.group(1), m.group(2)
            depth, i = 1, m.end()
            while i < len(text) and depth > 0:
                if text[i] == "{":
                    depth += 1
                elif text[i] == "}":
                    depth -= 1
                i += 1
            blocks[(rtype, rname)] = text[m.end():i - 1]
    return blocks


def main(blocks=None):
    if blocks is None:
        blocks = load_blocks()
    failures = []

    for rtype, rname in CRITICAL:
        body = blocks.get((rtype, rname))
        if body is None:
            failures.append(f"{rtype}.{rname}: critical resource is missing")
            continue
        if not re.search(r"prevent_destroy\s*=\s*true", body):
            failures.append(f"{rtype}.{rname}: missing lifecycle prevent_destroy = true")

    for _, bucket in CRITICAL:
        vbody = blocks.get(("aws_s3_bucket_versioning", bucket))
        if vbody is None or "Enabled" not in vbody:
            failures.append(f"aws_s3_bucket_versioning.{bucket}: versioning is not Enabled")

    policy_spec = importlib.util.spec_from_file_location(
        'separation', Path(HERE).parents[1] / 'tools/check-project-separation.py')
    policy = importlib.util.module_from_spec(policy_spec)
    policy_spec.loader.exec_module(policy)
    failures += policy.check_writer_policy(policy.read_tf(HERE))

    exposure_spec = importlib.util.spec_from_file_location(
        'exposure', Path(HERE).parents[1] / 'tools/check-bucket-exposure.py')
    exposure = importlib.util.module_from_spec(exposure_spec)
    exposure_spec.loader.exec_module(exposure)
    failures += exposure.check(blocks, backup=True)

    if failures:
        print("backup guardrails missing (ADR-0014):", file=sys.stderr)
        for f in failures:
            print(f"  - {f}", file=sys.stderr)
        return 1
    print(f"backup guardrails OK ({len(CRITICAL)} critical backup buckets protected)")
    return 0


def self_test():
    """Exercise backup resource protection and versioning failures."""
    good = load_blocks()
    cases = [("protected backup buckets", good, 0)]
    for kind, name in CRITICAL:
        missing = copy.deepcopy(good)
        missing.pop((kind, name))
        cases.append((f"missing {name}", missing, 1))
        changed = copy.deepcopy(good)
        changed[(kind, name)] = re.sub(r"prevent_destroy\s*=\s*true", "prevent_destroy = false", changed[(kind, name)])
        cases.append((f"unprotected {name}", changed, 1))
        changed = copy.deepcopy(good)
        changed[("aws_s3_bucket_versioning", name)] = 'status = "Suspended"'
        cases.append((f"disabled versioning {name}", changed, 1))
    for acl in ['public-read', 'public-read-write', 'authenticated-read', 'var.bucket_acl']:
        changed = copy.deepcopy(good)
        name = 'database_backup'
        changed[("aws_s3_bucket_acl", "exposure")] = f'bucket = aws_s3_bucket.{name}.id\nacl = "{acl}"'
        cases.append((f"unsafe ACL {acl}", changed, 1))
    changed = copy.deepcopy(good)
    changed[("aws_s3_bucket_acl", "exposure")] = 'access_control_policy { grant { grantee { type = "Group" uri = "http://acs.amazonaws.com/groups/global/AllUsers" } permission = "READ" } }'
    cases.append(("anonymous ACL grant", changed, 1))
    name = 'database_backup'
    for suffix in ['website_configuration', 'cors_configuration']:
        changed = copy.deepcopy(good)
        changed[(f"aws_s3_bucket_{suffix}", "exposure")] = f'bucket = aws_s3_bucket.{name}.id'
        cases.append((suffix, changed, 1))
    for attribute in ['acl = "public-read"', 'website { index_document = "index.html" }', 'cors_rule { allowed_origins = ["*"] }']:
        changed = copy.deepcopy(good)
        changed[("aws_s3_bucket", name)] += '\n' + attribute
        cases.append((f"inline {attribute}", changed, 1))
    changed = copy.deepcopy(good)
    changed[("aws_s3_bucket_public_access_block", "exposure")] = 'block_public_acls = false'
    cases.append(("disabled public ACL block", changed, 1))
    for principal in ['"*"', '{ AWS = "*" }', '{ AWS = ["*"] }', '{ CanonicalUser = "" }', '{ AWS = "arn:aws:iam:::user/p1:other" }']:
        changed = copy.deepcopy(good)
        changed[("aws_s3_bucket_policy", "exposure")] = f'policy = jsonencode({{ Statement = [{{ Effect = "Allow" Principal = {principal} Action = "s3:GetObject" Resource = "*" }}] }})'
        cases.append((f"extra policy {principal}", changed, 1))
    writer = good[("aws_s3_bucket_policy", "database_writer")]
    for label, replacement in [
        ("wildcard writer", writer.replace('arn:aws:iam:::user/p${var.database_writer_primary_project_id}:${var.database_writer_access_key_id}', '*')),
        ("other writer", writer.replace('${var.database_writer_access_key_id}', 'other')),
        ("extra writer action", writer.replace('"s3:PutObject"', '"s3:PutObject", "s3:DeleteObject"')),
        ("list on objects", writer.replace('["s3:ListBucket"]', '["s3:GetObject"]')),
        ("extra AWS principal", writer.replace('Principal = { AWS =', 'Principal = { CanonicalUser = "anonymous", AWS =')),
    ]:
        changed = copy.deepcopy(good)
        changed[("aws_s3_bucket_policy", "database_writer")] = replacement
        cases.append((label, changed, 1))
    for label, body in [
        ('comment hides public ACL', '# acl = "private"\nacl = "public-read"'),
        ('dynamic grants', 'dynamic "access_control_policy" { for_each = [1] content { grant {} } }'),
        ('ACL expression unresolved', 'acl = var.acl'),
    ]:
        changed = copy.deepcopy(good)
        changed[("aws_s3_bucket_acl", "exposure")] = body
        cases.append((label, changed, 1))
    for kind, body in [
        ('aws_s3_bucket_public_access_block', 'block_public_acls = true ? false : true\nignore_public_acls = true\nblock_public_policy = true\nrestrict_public_buckets = true'),
        ('aws_s3_bucket_acl', 'acl = "private"\ndynamic "access_control_policy" { for_each = var.grants content {} }'),
    ]:
        changed = copy.deepcopy(good)
        changed[(kind, "exposure")] = body
        cases.append((f"computed exposure {kind}", changed, 1))
    errors = []
    for label, blocks, expected in cases:
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            got = main(blocks)
        if got != expected:
            print(f"backup guardrails self-test FAILED: {label}", file=sys.stderr)
            errors.append(label)
    if errors:
        return 1
    print(f"backup guardrails self-test OK ({len(cases)} cases)")
    return 0


if __name__ == "__main__":
    if '--self-test' in sys.argv:
        policy_spec = importlib.util.spec_from_file_location(
            'separation', Path(HERE).parents[1] / 'tools/check-project-separation.py')
        policy = importlib.util.module_from_spec(policy_spec)
        policy_spec.loader.exec_module(policy)
        sys.exit(self_test() or policy.self_test())
    sys.exit(main())
