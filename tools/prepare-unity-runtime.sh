#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Unity Build Automation's Windows builder runs Bash scripts. Its documented
# Windows image includes PowerShell but does not guarantee a .NET SDK, so use
# the PowerShell staging script there (it bootstraps .NET 10 when needed).
if [[ "${OS:-}" == "Windows_NT" ]] || command -v powershell.exe >/dev/null 2>&1; then
  PS_SCRIPT="$ROOT/tools/Prepare-UnityRuntime.ps1"
  if command -v cygpath >/dev/null 2>&1; then
    PS_SCRIPT="$(cygpath -w "$PS_SCRIPT")"
  fi
  powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "$PS_SCRIPT"
  exit $?
fi

PROJECT="$ROOT/src/Nightwire.Runtime/Nightwire.Runtime.csproj"
PUBLISH_DIR="$ROOT/.nightwire-unity-publish"
DEST="$ROOT/unity/Assets/Plugins/Nightwire"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "ERROR: .NET SDK is required to stage the Unity runtime." >&2
  exit 1
fi

rm -rf "$PUBLISH_DIR"
dotnet publish "$PROJECT" --configuration Release --framework netstandard2.1 --output "$PUBLISH_DIR" -p:CopyLocalLockFileAssemblies=true

mkdir -p "$DEST"
find "$DEST" -maxdepth 1 -type f -name '*.dll' -delete
copied=0
while IFS= read -r -d '' file; do
  cp "$file" "$DEST/"
  copied=$((copied + 1))
done < <(find "$PUBLISH_DIR" -maxdepth 1 -type f -name '*.dll' -print0)

if [[ $copied -eq 0 ]]; then
  echo "ERROR: publish produced no DLLs; refusing to create an incomplete Unity import." >&2
  exit 1
fi

{
  echo '<?xml version="1.0" encoding="utf-8"?>'
  echo '<linker>'
  for file in "$DEST"/*.dll; do
    assembly="$(basename "$file" .dll)"
    echo "  <assembly fullname=\"$assembly\" preserve=\"all\" />"
  done
  echo '</linker>'
} > "$DEST/link.xml"

echo "Staged $copied assemblies in unity/Assets/Plugins/Nightwire."
