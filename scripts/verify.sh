#!/usr/bin/env bash
# The gate for this repo. The feedback skills run it, and CI runs it on each
# pull request. The last line of output is `verify: green` or `verify: red`.
set -uo pipefail

cd "$(git rev-parse --show-toplevel)"

status=0
step() {
  echo "verify: $*"
  "$@" || status=1
}

step ./scripts/check-banned-patterns.sh
step dotnet build TwoPoint.slnx --nologo
step dotnet test TwoPoint.slnx --nologo --no-build

if [ "$status" -eq 0 ]; then
  echo "verify: green"
else
  echo "verify: red"
  exit 1
fi
