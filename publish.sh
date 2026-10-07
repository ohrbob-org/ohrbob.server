#!/usr/bin/env bash
set -euo pipefail

# Build and publish ohrbob images to Docker Hub.
# Images: ohrbob.api, ohrbob.player, ohrbob.web (see docker-compose.yml)
# Tag is taken from git: git describe --tags --always
#
# Usage:
#   ./publish.sh [--skip-login] [--latest] [--tag <tag>] [--dry-run]
#   DOCKER_REGISTRY=kaiwegner/ ./publish.sh --latest
#
# Env overrides:
#   DOCKER_REGISTRY  registry prefix with trailing slash (default: kaiwegner/)
#   GIT_COMMIT       build arg (default: git rev-parse --short HEAD)
# The image tag always comes from git (git describe --tags --always),
# unless --tag <tag> is passed explicitly. An ambient RELEASE_TAG in the
# environment is intentionally ignored so a stale `export RELEASE_TAG=beta`
# (e.g. from sourcing .env) can never leak into a published tag.

cd "$(dirname "$0")"

DOCKER_REGISTRY="${DOCKER_REGISTRY:-kaiwegner/}"
RELEASE_TAG_AMBIENT="${RELEASE_TAG:-}"
GIT_COMMIT="${GIT_COMMIT:-$(git rev-parse --short HEAD)}"
GIT_TAG="$(git describe --tags --always)"
TAG_OVERRIDE=""

SKIP_LOGIN=0
PUSH_LATEST=0
DRY_RUN=0
while [ $# -gt 0 ]; do
  case "$1" in
    --skip-login) SKIP_LOGIN=1; shift ;;
    --latest) PUSH_LATEST=1; shift ;;
    --dry-run) DRY_RUN=1; SKIP_LOGIN=1; shift ;;
    --tag)
      if [ $# -lt 2 ]; then
        echo "--tag requires a value: --tag <tag>" >&2
        exit 1
      fi
      TAG_OVERRIDE="$2"
      shift 2
      ;;
    --tag=*)
      TAG_OVERRIDE="${1#--tag=}"
      shift
      ;;
    -h|--help)
      sed -n '2,18p' "$0"
      exit 0
      ;;
    *)
      echo "Unknown argument: $1 (see --help)" >&2
      exit 1
      ;;
  esac
done

if [ -n "$TAG_OVERRIDE" ]; then
  RELEASE_TAG="$TAG_OVERRIDE"
  echo "Notice: using explicit --tag override: $RELEASE_TAG (git says: $GIT_TAG)"
else
  RELEASE_TAG="$GIT_TAG"
  if [ -n "${RELEASE_TAG_AMBIENT:-}" ] && [ "$RELEASE_TAG_AMBIENT" != "$RELEASE_TAG" ]; then
    echo "Notice: ignoring ambient RELEASE_TAG=$RELEASE_TAG_AMBIENT, using git tag: $RELEASE_TAG"
  fi
fi

# Ensure trailing slash on registry prefix
[[ "$DOCKER_REGISTRY" == */ ]] || DOCKER_REGISTRY="$DOCKER_REGISTRY/"

echo "Registry:    $DOCKER_REGISTRY"
echo "Release tag: $RELEASE_TAG"
echo "Git commit:  $GIT_COMMIT"

export DOCKER_REGISTRY RELEASE_TAG GIT_COMMIT

if [ "$DRY_RUN" -eq 1 ]; then
  echo "--- dry run: resolved images ---"
  docker compose config | grep "image:"
  exit 0
fi

if [ "$SKIP_LOGIN" -eq 0 ]; then
  docker login -u "${DOCKER_USER:-kaiwegner}"
fi

docker compose build
docker compose push ohrbob.api ohrbob.player ohrbob.web

if [ "$PUSH_LATEST" -eq 1 ]; then
  for img in ohrbob.api ohrbob.player ohrbob.web; do
    docker tag "$DOCKER_REGISTRY$img:$RELEASE_TAG" "$DOCKER_REGISTRY$img:latest"
    docker push "$DOCKER_REGISTRY$img:latest"
  done
fi

echo "Published:"
for img in ohrbob.api ohrbob.player ohrbob.web; do
  echo "  $DOCKER_REGISTRY$img:$RELEASE_TAG"
done
