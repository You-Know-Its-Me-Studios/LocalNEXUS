# Produces everything a release consists of: the installer, and the plain zip for people who
# would rather not run one.
#
#   .\release.ps1
#
# Both come out of dist\release\. The application itself is published by publish.ps1, which this
# calls rather than reimplements, so a release build and a development build are the same build.

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$release = Join-Path $dist 'release'
$installerProject = Join-Path $root 'src\LocalNEXUS.Installer\LocalNEXUS.Installer.csproj'
$payloadDir = Join-Path $root 'src\LocalNEXUS.Installer\Payload'

# The version, read from the one place it is declared. Nothing here writes it down, so the number
# on the artefact names is the number the binaries inside them report.
$propsPath = Join-Path $root 'Directory.Build.props'
$versionNode = ([xml](Get-Content $propsPath -Raw)).SelectSingleNode('/Project/PropertyGroup/Version')

if (-not $versionNode) {
    Write-Error "No Version element in $propsPath. That is where the version is declared."
    exit 1
}

$version = $versionNode.InnerText.Trim()

Write-Host "Version $version"

Write-Host "Publishing the application"
& (Join-Path $root 'publish.ps1')

if ($LASTEXITCODE -ne 0) {
    Write-Error "publish.ps1 failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

$appExe = Join-Path $dist 'LocalNEXUS.exe'

if (-not (Test-Path $appExe)) {
    Write-Error "publish.ps1 finished but $appExe is missing."
    exit 1
}

New-Item -ItemType Directory -Force $release | Out-Null

# ---------------------------------------------------------------------------
# The zip. Everything in dist except the release folder itself, so whoever takes it gets the
# application and whatever engines this machine happened to have.
# ---------------------------------------------------------------------------
Write-Host "Building the zip"

$zipPath = Join-Path $release "LocalNEXUS-$version-win-x64.zip"
$staging = Join-Path $dist 'zip-staging'

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force $staging | Out-Null

Copy-Item $appExe $staging
foreach ($folder in @('vendor')) {
    $source = Join-Path $dist $folder
    if (Test-Path $source) { Copy-Item $source $staging -Recurse -Force }
}

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zipPath
Remove-Item $staging -Recurse -Force

Write-Host "  $zipPath"

# ---------------------------------------------------------------------------
# The installer. The application is embedded rather than downloaded, so it is copied into the
# payload folder first and the project picks it up from there.
# ---------------------------------------------------------------------------
Write-Host "Building the installer"

New-Item -ItemType Directory -Force $payloadDir | Out-Null

# The application is embedded rather than downloaded, which is why it is not on the fetch list.
$payloadPath = Join-Path $payloadDir 'LocalNEXUS.exe'
if (Test-Path $payloadPath) { Remove-Item $payloadPath -Force }

Copy-Item $appExe $payloadPath -Force

$payloadMb = [Math]::Round((Get-Item $payloadPath).Length / 1MB, 1)
Write-Host ("  payload staged, {0} MB" -f $payloadMb)

$installerOut = Join-Path $dist 'installer'
if (Test-Path $installerOut) { Remove-Item $installerOut -Recurse -Force }

# Self contained and single file, which matters for more than tidiness: the installer copies
# itself into the install directory to serve as the uninstaller, and a copy of a framework
# dependent executable left behind without its dependencies will not run.
dotnet publish $installerProject `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    --output $installerOut

if ($LASTEXITCODE -ne 0) {
    Write-Error "Building the installer failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

$setupExe = Join-Path $installerOut 'LocalNEXUS-Setup.exe'

if (-not (Test-Path $setupExe)) {
    Write-Error "The installer published but $setupExe is missing."
    exit 1
}

$target = Join-Path $release "LocalNEXUS-$version-setup.exe"
Copy-Item $setupExe $target -Force

# ---------------------------------------------------------------------------
# Signing.
#
# Not signed today, and adding it is this block rather than a change to anything above. Set
# LOCALNEXUS_SIGNTOOL to the signing command and it runs over both artefacts. An unsigned
# installer gets a SmartScreen warning, which is the known cost of not doing this yet.
# ---------------------------------------------------------------------------
if ($env:LOCALNEXUS_SIGNTOOL) {
    Write-Host "Signing"

    foreach ($artefact in @($target)) {
        & cmd /c "$env:LOCALNEXUS_SIGNTOOL `"$artefact`""

        if ($LASTEXITCODE -ne 0) {
            Write-Error "Signing $artefact failed with exit code $LASTEXITCODE"
            exit $LASTEXITCODE
        }
    }
}
else {
    Write-Host "Not signed. Set LOCALNEXUS_SIGNTOOL to a signing command to sign the installer."
}

$setupSize = [Math]::Round((Get-Item $target).Length / 1MB, 1)
$zipSize = [Math]::Round((Get-Item $zipPath).Length / 1MB, 1)

Write-Host ""
Write-Host "Release artefacts in $release"
Write-Host ("  LocalNEXUS-$version-setup.exe     {0} MB" -f $setupSize)
Write-Host ("  LocalNEXUS-$version-win-x64.zip   {0} MB" -f $zipSize)
