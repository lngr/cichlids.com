#!/usr/bin/env python3
"""Checks app/stack/bootstrap.sh without Docker or a database.

The Postgres check runs bootstrap.sh with a fake docker on PATH whose compose up succeeds and
whose pg_isready never does. With a two second wait the script has to stop with a non-zero exit
and the message "Postgres did not become ready", report no ready Postgres and start no ETL
command. The default check reads the legacy stack directory default, which has to lie inside the
repository.

Story: task-3.30
"""
import os
import re
import stat
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
BOOTSTRAP = os.path.join(HERE, "bootstrap.sh")

FAKE_DOCKER = """#!/usr/bin/env bash
echo "docker $*" >> "$FAKE_LOG"
case " $* " in
  *" pg_isready "*) exit 1 ;;
  *) exit 0 ;;
esac
"""

FAKE_DOTNET = """#!/usr/bin/env bash
echo "dotnet $*" >> "$FAKE_LOG"
exit 1
"""


def write_executable(path, content):
    with open(path, "w") as handle:
        handle.write(content)
    os.chmod(path, os.stat(path).st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)


def check_postgres_wait():
    failures = []
    with tempfile.TemporaryDirectory() as tmp:
        bin_dir = os.path.join(tmp, "bin")
        os.mkdir(bin_dir)
        log = os.path.join(tmp, "calls.log")
        write_executable(os.path.join(bin_dir, "docker"), FAKE_DOCKER)
        fake_dotnet = os.path.join(tmp, "dotnet")
        write_executable(fake_dotnet, FAKE_DOTNET)

        env = dict(os.environ)
        env["PATH"] = bin_dir + os.pathsep + env.get("PATH", "")
        env["FAKE_LOG"] = log
        env["DOTNET"] = fake_dotnet
        env["CICHLIDS_POSTGRES_WAIT_SECONDS"] = "2"

        result = subprocess.run(
            ["bash", BOOTSTRAP],
            env=env,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            universal_newlines=True,
            timeout=120,
        )
        output = result.stdout
        calls = open(log).read() if os.path.exists(log) else ""

    if result.returncode == 0:
        failures.append("bootstrap.sh exits with 0 although Postgres never becomes ready")
    if "Postgres did not become ready" not in output:
        failures.append("bootstrap.sh does not report 'Postgres did not become ready'")
    if "Postgres is up." in output:
        failures.append("bootstrap.sh reports 'Postgres is up.' although pg_isready always fails")
    if "pg_isready" not in calls:
        failures.append("bootstrap.sh never asks pg_isready")
    if "dotnet" in calls:
        failures.append("bootstrap.sh starts an ETL command although Postgres is not ready")

    if failures:
        failures.append("output was:\n" + output)
    return failures


def check_legacy_stack_default():
    with open(BOOTSTRAP) as handle:
        source = handle.read()
    match = re.search(r'^CICHLIDS_LEGACY_STACK_DIR="\$\{CICHLIDS_LEGACY_STACK_DIR:-([^}]*)\}"', source, re.MULTILINE)
    if match is None:
        return ["bootstrap.sh has no default for CICHLIDS_LEGACY_STACK_DIR"]
    default = match.group(1)
    failures = []
    if "/workspaces" in default:
        failures.append("the legacy stack default is an absolute /workspaces path: " + default)
    if not default.startswith("$REPO_ROOT/"):
        failures.append("the legacy stack default does not lie inside the repository: " + default)
    return failures


def main():
    failures = check_legacy_stack_default() + check_postgres_wait()
    for failure in failures:
        print("FAIL: " + failure)
    if failures:
        return 1
    print("bootstrap.sh checks passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
