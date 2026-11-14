$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root "src/Nightwire.Runtime/Nightwire.Runtime.csproj"
$Publish = Join-Path $Root ".nightwire-unity-publish"
$Destination = Join-Path $Root "unity/Assets/Plugins/Nightwire"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK is required to stage the Unity runtime."
}

if (Test-Path $Publish) { Remove-Item $Publish -Recurse -Force }
dotnet publish $Project --configuration Release --framework netstandard2.1 --output $Publish -p:CopyLocalLockFileAssemblies=true
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
Get-ChildItem $Destination -Filter "*.dll" -File -ErrorAction SilentlyContinue | Remove-Item -Force
$Assemblies = Get-ChildItem $Publish -Filter "*.dll" -File
if ($Assemblies.Count -eq 0) { throw "Publish produced no DLLs; refusing incomplete Unity import." }
foreach ($Assembly in $Assemblies) { Copy-Item $Assembly.FullName $Destination }

$Entries = $Assemblies | ForEach-Object {
    '  <assembly fullname="{0}" preserve="all" />' -f $_.BaseName
}
$LinkXml = @('<?xml version="1.0" encoding="utf-8"?>', '<linker>') + $Entries + @('</linker>')
Set-Content -Path (Join-Path $Destination "link.xml") -Value $LinkXml -Encoding UTF8
Write-Host "Staged $($Assemblies.Count) assemblies in unity/Assets/Plugins/Nightwire."
Write-Host "Review plugin platform settings, then compile Editor and Android IL2CPP before release."
