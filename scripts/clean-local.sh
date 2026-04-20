#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ARTIFACTS_DIR="$ROOT_DIR/artifacts/local-test"

if [[ -d "$ARTIFACTS_DIR" ]]; then
  rm -rf "$ARTIFACTS_DIR"
  echo "Removed $ARTIFACTS_DIR"
else
  echo "Nothing to clean at $ARTIFACTS_DIR"
fi
