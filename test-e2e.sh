#!/usr/bin/env bash
# Runs the E2E suite as three concurrent shards:
#
#   1. TestCastsA - serial classes that reconfigure podcast-testcasts instance A.
#   2. TestCastsB - serial classes that reconfigure podcast-testcasts instance B.
#   3. everything else - independent fixtures, parallelised by NUnit.
#
# podcast-testcasts keeps one global configuration per server, so the testcasts
# classes are split over two servers (`testcasts` on 5004 and `testcasts-b` on
# 5005) and each shard runs its classes one at a time. The shards touch disjoint
# resources and allocate unique accounts, so they can overlap.
#
# Usage:
#   ./test-e2e.sh                 # reuse the current build
#   ./test-e2e.sh --build         # build once, then run
#   E2E_TESTCASTS_B_URL=http://localhost:5005 ./test-e2e.sh
set -uo pipefail

cd "$(dirname "$0")"

if [[ -f .env ]]; then
    set -a
    # shellcheck disable=SC1091
    . ./.env
    set +a
fi

# Host-side URLs used by the test process to configure testcasts and poll /health.
testcasts_a_url="${E2E_TESTCASTS_A_URL:-http://localhost:${TESTCASTS_PORT:-5004}}"
testcasts_b_url="${E2E_TESTCASTS_B_URL:-http://localhost:${TESTCASTS_B_PORT:-5005}}"
# Feed URLs baked into stations, i.e. as reachable by the Api/Player containers.
testcasts_a_feed="${E2E_TESTCASTS_A_FEED_URL:-http://testcasts:5004}"
testcasts_b_feed="${E2E_TESTCASTS_B_FEED_URL:-http://testcasts-b:5004}"

for url in "$testcasts_a_url/health" "$testcasts_b_url/health"; do
    if ! curl -fsS --max-time 3 -o /dev/null "$url"; then
        echo "==> testcasts is not reachable at $url" >&2
        echo "    start both instances first: docker compose up -d testcasts testcasts-b" >&2
        exit 1
    fi
done

if [[ "${1:-}" == "--build" ]]; then
    echo "==> building"
    dotnet build Ohrbob.E2ETests || exit 1
fi

results_dir="${E2E_RESULTS_DIR:-/tmp/opencode/ohrbob-e2e}"
mkdir -p "$results_dir"

echo "==> shard 1/3: testcasts A (serial, $testcasts_a_url)"
OHRBOB_TESTCASTS_URL="$testcasts_a_url" OHRBOB_TESTCASTS_FEED_URL="$testcasts_a_feed" \
dotnet test Ohrbob.E2ETests --no-build \
    --filter "TestCategory=TestCastsA" \
    --results-directory "$results_dir" \
    --logger "trx;LogFileName=testcasts-a.trx" &
pid_a=$!

echo "==> shard 2/3: testcasts B (serial, $testcasts_b_url)"
OHRBOB_TESTCASTS_URL="$testcasts_b_url" OHRBOB_TESTCASTS_FEED_URL="$testcasts_b_feed" \
dotnet test Ohrbob.E2ETests --no-build \
    --filter "TestCategory=TestCastsB" \
    --results-directory "$results_dir" \
    --logger "trx;LogFileName=testcasts-b.trx" &
pid_b=$!

echo "==> shard 3/3: parallel fixtures"
dotnet test Ohrbob.E2ETests --no-build \
    --filter "TestCategory!=TestCasts" \
    --results-directory "$results_dir" \
    --logger "trx;LogFileName=parallel.trx" &
pid_parallel=$!

status=0
wait "$pid_a" || status=1
wait "$pid_b" || status=1
wait "$pid_parallel" || status=1

if [[ "$status" -ne 0 ]]; then
    echo "==> E2E failed; results in $results_dir" >&2
else
    echo "==> E2E passed; results in $results_dir"
fi
exit "$status"
