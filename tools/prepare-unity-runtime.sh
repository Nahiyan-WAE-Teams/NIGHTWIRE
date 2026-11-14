#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/Nightwire.Runtime/Nightwire.Runtime.csproj"
PUBLISH_DIR="$ROOT/.nightwire-unity-publish"
DEST="$ROOT/unity/Assets/Plugins/Nightwire"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "ERROR: .NET SDK is required. Install a current SDK before staging the Unity runtime." >&2
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
echo "Review plugin platform settings in Unity, then compile Editor and Android IL2CPP before release."
