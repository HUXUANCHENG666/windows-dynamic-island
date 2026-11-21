#!/usr/bin/env bash
# Publish a self-contained, single-file win-x64 build, plus the Whisper
# native folders that must sit next to the exe:
#   runtimes/win-x64            CPU
#   runtimes/vulkan/win-x64     Vulkan (same DLL names, separate folder)
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

RID="$OUT/runtimes/win-x64"
if [ ! -f "$RID/whisper.dll" ]; then
  echo "error: whisper.dll was not copied to $RID" >&2
  exit 1
fi

# The Vulkan package may also drop host-OS natives (e.g. linux-x64) into the
# publish tree. Keep only the Windows folders the app actually loads.
if [ -d "$OUT/runtimes/vulkan" ]; then
  find "$OUT/runtimes/vulkan" -mindepth 1 -maxdepth 1 -type d ! -name 'win-x64' -exec rm -rf {} +
fi

VULKAN="$OUT/runtimes/vulkan/win-x64"
if [ -f "$VULKAN/whisper.dll" ] && [ -f "$VULKAN/ggml-vulkan-whisper.dll" ]; then
  echo "vulkan natives present in $VULKAN"
elif [ -n "${WINDIR:-}" ] || [ -d /c/Windows ] || [ -d /mnt/c/Windows ]; then
  echo "error: Vulkan whisper natives were not copied to $VULKAN" >&2
  exit 1
else
  echo "warning: Vulkan win-x64 natives not in publish output (ok on this host if the package layout was unavailable)"
fi

# On Windows CI, also place the VC++ runtime next to the Whisper natives so
# LoadLibrary can resolve them even if the machine has no redistributable yet.
copy_vc() {
  local name="$1"
  local candidate
  for candidate in \
      "/c/Windows/System32/$name" \
      "/mnt/c/Windows/System32/$name" \
      "${WINDIR:-}/System32/$name" \
      "${SYSTEMROOT:-}/System32/$name"; do
    if [ -n "$candidate" ] && [ -f "$candidate" ]; then
      cp "$candidate" "$RID/$name"
      echo "copied $name from $candidate"
      return 0
    fi
  done
  return 1
}
if copy_vc vcruntime140.dll; then
  copy_vc vcruntime140_1.dll || true
  copy_vc msvcp140.dll || true
fi

echo "published to $OUT"
