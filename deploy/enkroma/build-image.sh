#!/usr/bin/env bash
# Builds the EnkrOma-branded server image.
#
#   ./build-image.sh [version]
#
# Steps:
#   1. Checks out the noVNC submodule (required at runtime).
#   2. Applies EnkrOma branding from .branding/enkroma.json (bakes in the brand name, publisher,
#      colors, icons, server URL, and the fleet enrollment secret).
#   3. Builds the server image, which also publishes the Blazor WASM client.
#
# NOTE: step 2 edits source files in the working tree (BrandingConstants.cs, icons, csproj metadata).
# Run it on a clean checkout, or revert those edits afterward (git restore .) once the image is built.
# A fresh clone or CI checkout is the cleanest place to run this.

set -euo pipefail

VERSION="${1:-1.0.0}"
IMAGE="${ENKROMA_IMAGE:-enkroma/controlr:${VERSION}}"

# Resolve the repo root (this script lives in deploy/enkroma).
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
CONFIG="${REPO_ROOT}/.branding/enkroma.json"

command -v docker >/dev/null || { echo "docker not found on PATH"; exit 1; }
command -v pwsh   >/dev/null || { echo "pwsh (PowerShell) not found on PATH; needed to apply branding"; exit 1; }
[ -f "${CONFIG}" ] || { echo "Branding config not found: ${CONFIG}"; exit 1; }

cd "${REPO_ROOT}"

echo ">> Initializing submodules (noVNC)..."
git submodule update --init --recursive

echo ">> Applying EnkrOma branding (version ${VERSION})..."
pwsh "${REPO_ROOT}/.scripts/Invoke-Customize.ps1" -ConfigPath "${CONFIG}" -Version "${VERSION}"

echo ">> Building image ${IMAGE}..."
docker build \
  -f "${REPO_ROOT}/ControlR.Web.Server/Dockerfile" \
  --build-arg CURRENT_VERSION="${VERSION}" \
  -t "${IMAGE}" \
  "${REPO_ROOT}"

echo ""
echo ">> Done: ${IMAGE}"
echo ">> Set ENKROMA_IMAGE=${IMAGE} in deploy/enkroma/.env, then: cd deploy/enkroma && docker compose up -d"
echo ">> Reminder: 'git restore .' in the repo root to drop the branding edits from your working tree."
