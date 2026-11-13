#!/usr/bin/env bash
# Runs the concurrency test suite several times in a row (default 20) and stops at the first
# failure. A race that only shows up now and then is still a bug; this is how Phase 2 proves
# the suite does not flake. Each run starts its own SQL Server container, so Docker must run.
#
#   scripts/repeat-concurrency-tests.sh        # 20 runs
#   scripts/repeat-concurrency-tests.sh 5      # 5 runs
set -euo pipefail

runs="${1:-20}"
project="tests/LedgerCore.ConcurrencyTests"

cd "$(dirname "$0")/.."
dotnet build "$project" -c Release --nologo -v quiet

for i in $(seq 1 "$runs"); do
  echo "Run $i of $runs"
  dotnet test "$project" -c Release --no-build --nologo -v quiet
done

echo "All $runs runs passed."
