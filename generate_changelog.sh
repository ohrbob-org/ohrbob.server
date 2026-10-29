#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

echo "Generating changelog.md..."
echo "# Changelog" > changelog.md
echo "" >> changelog.md
# Only commits since the previous release tag (skip the tag we are building, if HEAD is on it).
BASE_TAG="$(git describe --tags --abbrev=0 2>/dev/null || true)"
if [ -n "$BASE_TAG" ] && [ "$(git rev-list -n1 "$BASE_TAG")" = "$(git rev-parse HEAD)" ]; then
  BASE_TAG="$(git describe --tags --abbrev=0 "$BASE_TAG^" 2>/dev/null || true)"
fi
git log --no-merges --pretty=format:"* %h %s (%an)" "${BASE_TAG:+$BASE_TAG..}HEAD" >> changelog.md

echo "Done."
