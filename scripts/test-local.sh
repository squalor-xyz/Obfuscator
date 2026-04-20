#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ARTIFACTS_DIR="$ROOT_DIR/artifacts/local-test"
NUGET_DIR="$ARTIFACTS_DIR/nuget"
TOOL_DIR="$ARTIFACTS_DIR/tool"
GENERATED_CSV="$ARTIFACTS_DIR/fake.csv"
SEMICONDUCTOR_CSV="$ARTIFACTS_DIR/fake-semiconductor.csv"
OBFUSCATED_CSV="$ARTIFACTS_DIR/fake.obfuscated.csv"
MANIFEST_FILE="$ARTIFACTS_DIR/fake.obf"
RESTORED_CSV="$ARTIFACTS_DIR/fake.deobfuscated.csv"
SEMICONDUCTOR_OBFUSCATED_CSV="$ARTIFACTS_DIR/fake-semiconductor.obfuscated.csv"
SEMICONDUCTOR_MANIFEST_FILE="$ARTIFACTS_DIR/fake-semiconductor.obf"
SEMICONDUCTOR_RESTORED_CSV="$ARTIFACTS_DIR/fake-semiconductor.deobfuscated.csv"
LOCAL_VERSION="0.0.0-local"
DETERMINISTIC_KEY="local-test-key"
MANIFEST_PASSPHRASE="local-test-passphrase"

rm -rf "$ARTIFACTS_DIR"
mkdir -p "$NUGET_DIR" "$TOOL_DIR"

cd "$ROOT_DIR"

echo "==> Restoring solution"
dotnet restore Obfuscator.slnx

echo "==> Running tests"
dotnet test Obfuscator.slnx --no-restore

echo "==> Packing library"
dotnet pack Obfuscator/Obfuscator.csproj -c Release --no-restore -p:Version="$LOCAL_VERSION" -o "$NUGET_DIR"

echo "==> Packing CLI"
dotnet pack Obfuscator.Cli/Obfuscator.Cli.csproj -c Release --no-restore -p:Version="$LOCAL_VERSION" -o "$NUGET_DIR"

echo "==> Installing CLI from local package output"
dotnet tool install \
  --tool-path "$TOOL_DIR" \
  --add-source "$NUGET_DIR" \
  --prerelease \
  Squalor.Obfuscator.Cli

echo "==> Running CLI help"
"$TOOL_DIR/obfuscator" --help

echo "==> Generating sample CSV"
"$TOOL_DIR/obfuscator" generate \
  --config "$ROOT_DIR/Obfuscator/ExampleConfig.json" \
  --output "$GENERATED_CSV"

test -f "$GENERATED_CSV"

echo "==> Generating semiconductor sample CSV"
"$TOOL_DIR/obfuscator" generate \
  --config "$ROOT_DIR/Obfuscator/ExampleConfigSemiconductor.json" \
  --output "$SEMICONDUCTOR_CSV"

test -f "$SEMICONDUCTOR_CSV"

echo "==> Obfuscating sample CSV"
"$TOOL_DIR/obfuscator" obfuscate \
  --input "$GENERATED_CSV" \
  --output "$OBFUSCATED_CSV" \
  --manifest "$MANIFEST_FILE" \
  --deterministic-key "$DETERMINISTIC_KEY" \
  --passphrase "$MANIFEST_PASSPHRASE"

test -f "$OBFUSCATED_CSV"
test -f "$MANIFEST_FILE"

echo "==> Deobfuscating sample CSV"
"$TOOL_DIR/obfuscator" deobfuscate \
  --input "$OBFUSCATED_CSV" \
  --manifest "$MANIFEST_FILE" \
  --output "$RESTORED_CSV" \
  --deterministic-key "$DETERMINISTIC_KEY" \
  --passphrase "$MANIFEST_PASSPHRASE"

test -f "$RESTORED_CSV"

echo "==> Obfuscating semiconductor sample CSV"
"$TOOL_DIR/obfuscator" obfuscate \
  --input "$SEMICONDUCTOR_CSV" \
  --output "$SEMICONDUCTOR_OBFUSCATED_CSV" \
  --manifest "$SEMICONDUCTOR_MANIFEST_FILE" \
  --deterministic-key "$DETERMINISTIC_KEY" \
  --passphrase "$MANIFEST_PASSPHRASE"

test -f "$SEMICONDUCTOR_OBFUSCATED_CSV"
test -f "$SEMICONDUCTOR_MANIFEST_FILE"

echo "==> Deobfuscating semiconductor sample CSV"
"$TOOL_DIR/obfuscator" deobfuscate \
  --input "$SEMICONDUCTOR_OBFUSCATED_CSV" \
  --manifest "$SEMICONDUCTOR_MANIFEST_FILE" \
  --output "$SEMICONDUCTOR_RESTORED_CSV" \
  --deterministic-key "$DETERMINISTIC_KEY" \
  --passphrase "$MANIFEST_PASSPHRASE"

test -f "$SEMICONDUCTOR_RESTORED_CSV"

echo
echo "Local test completed successfully."
echo "Artifacts:"
echo "  Packages: $NUGET_DIR"
echo "  Tool:     $TOOL_DIR/obfuscator"
echo "  Sample:   $GENERATED_CSV"
echo "  Sample:   $OBFUSCATED_CSV"
echo "  Sample:   $MANIFEST_FILE"
echo "  Sample:   $RESTORED_CSV"
echo "  Sample:   $SEMICONDUCTOR_CSV"
echo "  Sample:   $SEMICONDUCTOR_OBFUSCATED_CSV"
echo "  Sample:   $SEMICONDUCTOR_MANIFEST_FILE"
echo "  Sample:   $SEMICONDUCTOR_RESTORED_CSV"
