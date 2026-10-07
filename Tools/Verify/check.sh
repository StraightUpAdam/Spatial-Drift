#!/bin/bash
# Offline compile check for Assets/Scripts (see make.ps1).  Prints only errors /
# warnings (deprecation CS0618 filtered) and "Build succeeded."  ~15 s.
# Usage:  bash Tools/Verify/check.sh [maxLines]
DIR="$(cd "$(dirname "$0")" && pwd)"
if [ ! -f "$DIR/Verify.csproj" ]; then
  powershell -NoProfile -ExecutionPolicy Bypass -File "$DIR/make.ps1" >/dev/null || exit 1
fi
cd "$DIR" && timeout 300 dotnet build Verify.csproj -nologo -v q 2>&1 \
  | grep -E "error CS|Build succeeded|warning CS" | grep -v "MSB3245\|CS0618" \
  | sed -E 's/\[[^]]*Verify.csproj\]//; s#.*Assets.Scripts.##' | sort -u | head -${1:-60}
