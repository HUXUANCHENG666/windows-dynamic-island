#!/usr/bin/env bash
# Publish a self-contained, single-file win-x64 build.
# Works on Windows and, with the .NET 8 SDK, on Linux/macOS.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-$ROOT/artifacts/publish}"
dotnet publish "$ROOT/src/DynamicIsland/DynamicIsland.csproj" \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  -p:EnableWindowsTargeting=true \
  -o "$OUT"
echo "published to $OUT"
