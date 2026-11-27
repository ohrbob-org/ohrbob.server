#!/usr/bin/env bash
# Runs the E2E suite as two concurrent shards:
#
#   1. TestCasts  - classes that reconfigure the single shared podcast-testcasts
#                   server. They are [NonParallelizable], so NUnit runs them one
#                   at a time inside this shard.
#   2. everything else - independent fixtures, parallelised by NUnit.
#
# The shards touch disjoint resources (only shard 1 talks to testcasts), so they
# can overlap. Both allocate unique accounts, so they may share the API and Mongo.
#
# Usage:
#   ./test-e2e.sh                 # reuse the current build
#   ./test-e2e.sh --build         # build once, then run
#   OHRBOB_TESTCASTS_FEED_URL=http://testcasts:5004 ./test-e2e.sh
set -uo pipefail

cd "$(dirname "$0")"

if [[ -f .env ]]; then
    set -a
    # shellcheck disable=SC1091
    . ./.env
    set +a
fi

# The Api/Player reach testcasts by service name when they run in Docker.
export OHRBOB_TESTCASTS_FEED_URL="${OHRBOB_TESTCASTS_FEED_URL:-http://testcasts:5004}"

if [[ "${1:-}" == "--build" ]]; then
    echo "==> building"
    dotnet build Ohrbob.E2ETests || exit 1
fi

results_dir="${E2E_RESULTS_DIR:-/tmp/ohrbob-e2e}"
mkdir -p "$results_dir"

echo "==> shard 1/2: testcasts (serial)"
dotnet test Ohrbob.E2ETests --no-build \
    --filter "TestCategory=TestCasts" \
    --results-directory "$results_dir" \
    --logger "trx;LogFileName=testcasts.trx" &
serial_pid=$!

echo "==> shard 2/2: parallel fixtures"
dotnet test Ohrbob.E2ETests --no-build \
    --filter "TestCategory!=TestCasts" \
    --results-directory "$results_dir" \
    --logger "trx;LogFileName=parallel.trx" &
parallel_pid=$!

status=0
wait "$serial_pid" || status=1
wait "$parallel_pid" || status=1

if [[ "$status" -ne 0 ]]; then
    echo "==> E2E failed; results in $results_dir" >&2
else
    echo "==> E2E passed; results in $results_dir"
fi
exit "$status"
