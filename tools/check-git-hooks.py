#!/usr/bin/env python3
"""Asserts the repository's Git hooks are present and wired.

A secret must be caught before it leaves the machine, not only in CI. This check guards that
the pre-push hook exists, is executable, runs the plaintext-secret scanner, and that the
devcontainer activates the committed hooks via core.hooksPath. The hook is a local early
warning (bypassable with --no-verify); the CI secrets-scan job stays the enforced backstop.

Runs offline with no third-party dependencies. `--self-test` exercises the checks against
fixtures so the guard cannot silently rot.
Story: task-2.15
"""
import os
import stat
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)

POSTCREATE = os.path.join(".devcontainer", "postCreateCommand.sh")


def check_repo(root):
    failures = []
    hook = os.path.join(root, ".githooks", "pre-push")
    if not os.path.isfile(hook):
        failures.append(".githooks/pre-push is missing")
    else:
        if not (os.stat(hook).st_mode & stat.S_IXUSR):
            failures.append(".githooks/pre-push is not executable")
        text = _read(hook)
        if "check-no-plaintext-secrets" not in text:
            failures.append(".githooks/pre-push does not run the secret scanner")
    post = os.path.join(root, POSTCREATE)
    post_text = _read(post)
    if "core.hooksPath" not in post_text or ".githooks" not in post_text:
        failures.append("postCreateCommand.sh does not set core.hooksPath to .githooks")
    return failures


def _read(path):
    try:
        with open(path, encoding="utf-8") as fh:
            return fh.read()
    except OSError:
        return ""


def main(argv):
    if "--self-test" in argv:
        return self_test()
    failures = check_repo(ROOT)
    if failures:
        print("git hooks not wired:", file=sys.stderr)
        for f in failures:
            print(f"  - {f}", file=sys.stderr)
        return 1
    print("git hooks OK (pre-push secret scan wired)")
    return 0


def _rmtree(path):
    """Minimal recursive remove (the sandbox Python lacks shutil)."""
    for base, dirs, files in os.walk(path, topdown=False):
        for f in files:
            os.remove(os.path.join(base, f))
        for d in dirs:
            os.rmdir(os.path.join(base, d))
    os.rmdir(path)


def self_test():
    import tempfile

    errors = []
    root = tempfile.mkdtemp()
    try:
        os.makedirs(os.path.join(root, ".githooks"))
        os.makedirs(os.path.join(root, ".devcontainer"))
        hook = os.path.join(root, ".githooks", "pre-push")
        # Missing everything -> failures expected.
        if len(check_repo(root)) < 2:
            errors.append("expected failures for an unwired repo")
        # Good fixture.
        with open(hook, "w") as fh:
            fh.write("#!/usr/bin/env bash\npython3 tools/check-no-plaintext-secrets.py\n")
        os.chmod(hook, 0o755)
        with open(os.path.join(root, POSTCREATE), "w") as fh:
            fh.write("git config core.hooksPath .githooks\n")
        if check_repo(root):
            errors.append("good fixture should pass")
        # Non-executable hook -> failure.
        os.chmod(hook, 0o644)
        if not any("not executable" in f for f in check_repo(root)):
            errors.append("non-executable hook should fail")
    finally:
        _rmtree(root)
    if errors:
        print("git-hooks self-test FAILED:", file=sys.stderr)
        for e in errors:
            print(f"  - {e}", file=sys.stderr)
        return 1
    print("git-hooks self-test OK")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
