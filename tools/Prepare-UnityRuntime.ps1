$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root "src/Nightwire.Runtime/Nightwire.Runtime.csproj"
$Publish = Join-Path $Root ".nightwire-unity-publish"
$Destination = Join-Path $Root "unity/Assets/Plugins/Nightwire"

$DotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $DotnetCommand) {
    # Unity Build Automation Windows images do not guarantee a preinstalled .NET SDK.
    # Bootstrap the supported .NET 10 SDK into the builder user's profile.
    $SdkInstall = Join-Path $env:USERPROFILE ".dotnet"
    $Installer = Join-Path $env:TEMP "nightwire-dotnet-install.ps1"
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $Installer
    & $Installer -Channel "10.0" -InstallDir $SdkInstall -NoPath
    if ($LASTEXITCODE -ne 0) { throw "Installing the .NET 10 SDK failed with exit code $LASTEXITCODE" }
    $env:DOTNET_ROOT = $SdkInstall
    $env:PATH = "$SdkInstall;$SdkInstall\tools;$env:PATH"
    $Dotnet = Join-Path $SdkInstall "dotnet.exe"
} else {
    $Dotnet = $DotnetCommand.Source
}

if (Test-Path $Publish) { Remove-Item $Publish -Recurse -Force }
& $Dotnet publish $Project --configuration Release --framework netstandard2.1 --output $Publish -p:CopyLocalLockFileAssemblies=true
if ($LASTEXITCODE -ne 0) { throw "Nightwire.Runtime publish failed with exit code $LASTEXITCODE" }

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
Write-Host "Staged $($Assemblies.Count) runtime/dependency assemblies in unity/Assets/Plugins/Nightwire."
