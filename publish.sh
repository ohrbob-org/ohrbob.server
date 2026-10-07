#!/usr/bin/env bash
set -euo pipefail

# Build and publish ohrbob images to Docker Hub.
# Images: ohrbob.api, ohrbob.player, ohrbob.web (see docker-compose.yml)
# Tag is taken from git: git describe --tags --always
#
# Usage:
#   ./publish.sh [--skip-login] [--latest]
#   DOCKER_REGISTRY=kaiwegner/ ./publish.sh --latest
#
# Env overrides:
#   DOCKER_REGISTRY  registry prefix with trailing slash (default: kaiwegner/)
#   RELEASE_TAG      docker tag (default: git describe --tags --always)
#   GIT_COMMIT       build arg (default: git rev-parse --short HEAD)

cd "$(dirname "$0")"

DOCKER_REGISTRY="${DOCKER_REGISTRY:-kaiwegner/}"
RELEASE_TAG="${RELEASE_TAG:-$(git describe --tags --always)}"
GIT_COMMIT="${GIT_COMMIT:-$(git rev-parse --short HEAD)}"

SKIP_LOGIN=0
PUSH_LATEST=0
for arg in "$@"; do
  case "$arg" in
    --skip-login) SKIP_LOGIN=1 ;;
    --latest) PUSH_LATEST=1 ;;
    -h|--help)
      sed -n '2,14p' "$0"
      exit 0
      ;;
    *)
      echo "Unknown argument: $arg (see --help)" >&2
      exit 1
      ;;
  esac
done

# Ensure trailing slash on registry prefix
[[ "$DOCKER_REGISTRY" == */ ]] || DOCKER_REGISTRY="$DOCKER_REGISTRY/"

echo "Registry:    $DOCKER_REGISTRY"
echo "Release tag: $RELEASE_TAG"
echo "Git commit:  $GIT_COMMIT"

if [ "$SKIP_LOGIN" -eq 0 ]; then
  docker login -u "${DOCKER_USER:-kaiwegner}"
fi

export DOCKER_REGISTRY RELEASE_TAG GIT_COMMIT

docker compose build
docker compose push

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
