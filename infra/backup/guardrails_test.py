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
    for label, blocks, expected in cases:
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            got = main(blocks)
        if got != expected:
            print(f"backup guardrails self-test FAILED: {label}", file=sys.stderr)
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
