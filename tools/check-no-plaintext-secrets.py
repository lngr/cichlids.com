#!/usr/bin/env python3
"""Fails CI if a tracked file contains a plaintext secret or token.

The operator runbook and the infrastructure code must never carry real credentials:
secrets live only in the operator's environment and, encrypted, via SOPS+age (ADR-0010,
ADR-0014). This check is the machine guard for that rule — it scans the working tree and
refuses high-signal secret material (private keys, cloud access keys, age secret keys, and
keyword=value assignments that look like live tokens).

Scope: every Git-tracked text file, except the curated legacy area under `workspace-legacy/`,
which deliberately preserves the historical platform's plaintext credentials as recovered
evidence (they are treated as compromised and are out of scope here). Lock files are skipped
because their content hashes resemble tokens without being secrets. A line carrying the
gitleaks allow marker is accepted deliberately; it marks local development and test values.

Runs offline with no third-party dependencies. `--self-test` verifies the detector itself
(catches planted secrets, passes placeholders) so the guard cannot silently rot.
Story: task-2.3
"""
import os
import re
import subprocess
import sys

# Directories/files that are out of scope. workspace-legacy holds recovered legacy
# credentials on purpose; lock files carry token-shaped content hashes.
EXCLUDED_PREFIXES = ("workspace-legacy/",)
EXCLUDED_BASENAMES = (".terraform.lock.hcl",)
EXCLUDED_SUFFIXES = (".lock",)

# Values that read like secrets but are obviously placeholders, not live material.
PLACEHOLDER = re.compile(
    r"""(?ix)
    ( \$\{ .* \}          # ${VAR} / ${TF_VAR_x} interpolation
    | < .* >              # <your-token>
    | \.\.\.              # AAAA... truncation in examples
    | x{3,}               # xxxx redaction
    | \b(example|changeme|placeholder|redacted|dummy|sample|your[-_]?\w+|fixme|todo)\b
    )""",
)

# (label, compiled pattern). Patterns are written so this file does not match itself.
PATTERNS = [
    ("private key block", re.compile(r"-----BEGIN (?:[A-Z0-9 ]+ )?PRIVATE KEY-----")),
    ("AWS access key id", re.compile(r"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b")),
    ("age secret key", re.compile(r"AGE-SECRET-KEY-1[0-9A-Z]{20,}")),
    (
        "credential assignment",
        # No leading word boundary so prefixed identifiers (hcloud_token, s3_secret_key)
        # are caught; the trailing assignment anchor keeps it from matching mid-word.
        re.compile(
            r"""(?ix)
            (?:token|secret|password|passwd|api[_-]?key|access[_-]?key|secret[_-]?key|client[_-]?secret)
            \s* [:=] \s*
            (?P<quote>['"]?) (?P<val>[A-Za-z0-9/+_-]{16,}) ['"]?
            """,
        ),
    ),
]

# An unquoted value made of letters and underscores only is a reference to another identifier
# (CancellationToken = cancellationToken), never literal secret material: generated tokens
# always mix in digits or punctuation.
IDENTIFIER = re.compile(r"[A-Za-z_]+")

# Inline marker that deliberately accepts a line, for instance a local development default.
# The spelling follows gitleaks, so gitleaks honours the same markers.
ALLOW_MARKER = "gitleaks" + ":allow"


def tracked_files(root):
    out = subprocess.run(
        ["git", "-C", root, "ls-files", "-z"],
        check=True, capture_output=True, text=True,
    ).stdout
    return [p for p in out.split("\0") if p]


def in_scope(path):
    if path.startswith(EXCLUDED_PREFIXES):
        return False
    base = os.path.basename(path)
    if base in EXCLUDED_BASENAMES or base.endswith(EXCLUDED_SUFFIXES):
        return False
    return True


def scan_text(text):
    """Return a list of (line_number, label) for every secret-looking hit in text."""
    findings = []
    for lineno, line in enumerate(text.splitlines(), start=1):
        if ALLOW_MARKER in line:
            continue
        for label, pattern in PATTERNS:
            m = pattern.search(line)
            if not m:
                continue
            if label == "credential assignment":
                # Obvious placeholders are allowed so that documenting
                # "export TF_VAR_token=<...>" in the runbook passes.
                if PLACEHOLDER.search(m.group("val")):
                    continue
                if not m.group("quote") and IDENTIFIER.fullmatch(m.group("val")):
                    continue
            findings.append((lineno, label))
    return findings


def scan_files(root, paths):
    failures = []
    for rel in paths:
        if not in_scope(rel):
            continue
        full = os.path.join(root, rel)
        try:
            with open(full, "rb") as fh:
                raw = fh.read()
        except OSError:
            continue
        if b"\0" in raw:  # binary
            continue
        text = raw.decode("utf-8", errors="replace")
        for lineno, label in scan_text(text):
            failures.append(f"{rel}:{lineno}: possible {label}")
    return failures


def self_test():
    """Detector smoke test: planted secrets must trip, placeholders must not."""
    # Fixtures are assembled from fragments so the patterns never match this source file
    # itself (the runtime-joined strings are whole and must trip the detector).
    akia = "AKIA" + "ABCDEFGHIJ123456"           # 16-char tail
    age = "AGE-SECRET-KEY-1" + "A" * 40
    pem = "-----BEGIN OPENSSH " + "PRIVATE KEY-----"
    token_line = "hcloud_token = " + '"' + "abcd1234efgh5678ijkl" + '"'
    quoted_letters = "SecretKey = " + '"' + "cichlids-dev-secret" + '";'
    unquoted_env = "GITHUB_TOKEN=" + "ghp_" + "abcd1234efgh5678ijkl"
    allow_marker = "gitleaks" + ":allow"
    positives = [
        f"aws_key = {akia}",
        f"sops_key: {age}",
        pem,
        token_line,
        quoted_letters,
        unquoted_env,
    ]
    negatives = [
        "export TF_VAR_hcloud_token=<your-routine-token>",
        'ssh_public_key = "ssh-ed25519 AAAA... operator@host"',
        "the routine token has no delete rights",
        "password: changeme-example",
        "CancellationToken = cancellationToken,",
        "AccessKey = ObjectStoreAccessKey,",
        f"{quoted_letters} // {allow_marker}",
        f"{token_line}  # {allow_marker}",
    ]
    errors = []
    for s in positives:
        if not scan_text(s):
            errors.append(f"missed secret: {s!r}")
    for s in negatives:
        if scan_text(s):
            errors.append(f"false positive: {s!r}")
    if errors:
        print("secret detector self-test FAILED:", file=sys.stderr)
        for e in errors:
            print(f"  - {e}", file=sys.stderr)
        return 1
    print("secret detector self-test OK")
    return 0


def main(argv):
    if "--self-test" in argv:
        return self_test()
    root = subprocess.run(
        ["git", "rev-parse", "--show-toplevel"],
        check=True, capture_output=True, text=True,
    ).stdout.strip()
    explicit = [a for a in argv[1:] if not a.startswith("-")]
    paths = explicit or tracked_files(root)
    failures = scan_files(root, paths)
    if failures:
        print("plaintext secrets found (must never be committed):", file=sys.stderr)
        for f in failures:
            print(f"  - {f}", file=sys.stderr)
        return 1
    print(f"no plaintext secrets in {len(paths)} scanned path(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
