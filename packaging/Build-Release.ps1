[CmdletBinding()]
param(
    [ValidateSet("Release", "Debug")]
    [string] $Configuration = "Release",

    [ValidatePattern("^\d+\.\d+\.\d+$")]
    [string] $Version,

    [switch] $CleanArtifacts
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$artifactRoot = Join-Path $repoRoot "release-artifacts"
$publishRoot = Join-Path $artifactRoot "publish"
$appProject = Join-Path $repoRoot "src\OrganizeContacts.App\OrganizeContacts.App.csproj"
$solution = Join-Path $repoRoot "OrganizeContacts.sln"
$iconFile = Join-Path $repoRoot "src\OrganizeContacts.App\Assets\OrganizeContacts.ico"
$wixSource = Join-Path $PSScriptRoot "OrganizeContacts.wxs"

function Invoke-Native {
    param(
        [Parameter(Mandatory)] [string] $Command,
        [Parameter(Mandatory)] [string[]] $Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $Command $($Arguments -join ' ')"
    }
}

function Assert-UnderRoot {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Root
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
    $rootPrefix = "$fullRoot$([System.IO.Path]::DirectorySeparatorChar)"
    if ($fullPath -ne $fullRoot -and -not $fullPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside the artifact directory: $fullPath"
    }

    return $fullPath
}

function Remove-ArtifactDirectory {
    param([Parameter(Mandatory)] [string] $Path)

    $safePath = Assert-UnderRoot -Path $Path -Root $artifactRoot
    if (Test-Path -LiteralPath $safePath) {
        Remove-Item -LiteralPath $safePath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $safePath -Force | Out-Null
}

function Remove-GeneratedPath {
    param([Parameter(Mandatory)] [string] $Path)

    $safePath = Assert-UnderRoot -Path $Path -Root $artifactRoot
    if (Test-Path -LiteralPath $safePath) {
        Remove-Item -LiteralPath $safePath -Recurse -Force
    }
}

if (-not (Test-Path -LiteralPath $appProject)) {
    throw "Application project was not found: $appProject"
}
if (-not (Test-Path -LiteralPath $solution)) {
    throw "Solution was not found: $solution"
}
if (-not (Test-Path -LiteralPath $iconFile)) {
    throw "Application icon was not found: $iconFile"
}
if (-not (Test-Path -LiteralPath $wixSource)) {
    throw "WiX source was not found: $wixSource"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml] $projectXml = Get-Content -Raw -LiteralPath $appProject
    $Version = @($projectXml.Project.PropertyGroup.Version | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })[0].ToString()
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must be a three-part numeric version, got '$Version'"
}

Write-Host "Auditing direct and transitive NuGet dependencies..."
$auditJson = & dotnet list $solution package --vulnerable --include-transitive --format json --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "NuGet vulnerability audit failed with exit code $LASTEXITCODE"
}
$audit = ($auditJson -join [Environment]::NewLine) | ConvertFrom-Json
$vulnerablePackages = @(
    foreach ($project in @($audit.projects)) {
        foreach ($framework in @($project.frameworks)) {
            $packages = @($framework.topLevelPackages) + @($framework.transitivePackages)
            foreach ($package in $packages) {
                if ($null -eq $package) { continue }
                $vulnerabilities = @($package.vulnerabilities | Where-Object { $null -ne $_ })
                if ($vulnerabilities.Count -gt 0) {
                    "$($package.id) $($package.resolvedVersion) ($($vulnerabilities.Count) advisory/advisories)"
                }
            }
        }
    }
)
if ($vulnerablePackages.Count -gt 0) {
    throw "Release blocked by vulnerable NuGet packages: $($vulnerablePackages -join '; ')"
}
Write-Host "NuGet dependency audit passed."

$portableRoot = Join-Path $artifactRoot "OrganizeContacts-$Version-win-x64"
$portableZip = Join-Path $artifactRoot "OrganizeContacts-$Version-win-x64-portable.zip"
$installerPath = Join-Path $artifactRoot "OrganizeContacts-$Version-win-x64.msi"
$sbomPath = Join-Path $artifactRoot "OrganizeContacts-$Version-sbom.json"
$checksumsPath = Join-Path $artifactRoot "SHA256SUMS"

if ($CleanArtifacts) {
    $safeArtifactRoot = Assert-UnderRoot -Path $artifactRoot -Root $artifactRoot
    if (Test-Path -LiteralPath $safeArtifactRoot) {
        Remove-Item -LiteralPath $safeArtifactRoot -Recurse -Force
    }
}
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
Remove-ArtifactDirectory -Path $publishRoot
Remove-ArtifactDirectory -Path $portableRoot
foreach ($artifact in @($portableZip, $installerPath, $sbomPath, $checksumsPath)) {
    $safeArtifact = Assert-UnderRoot -Path $artifact -Root $artifactRoot
    if (Test-Path -LiteralPath $safeArtifact) {
        Remove-Item -LiteralPath $safeArtifact -Force
    }
}

Write-Host "Publishing OrganizeContacts $Version ($Configuration, win-x64)..."
Invoke-Native -Command "dotnet" -Arguments @(
    "publish",
    $appProject,
    "--configuration", $Configuration,
    "--runtime", "win-x64",
    "--self-contained", "false",
    "--output", $publishRoot,
    "/p:Version=$Version"
)

$publishedExe = Join-Path $publishRoot "OrganizeContacts.exe"
if (-not (Test-Path -LiteralPath $publishedExe)) {
    throw "Publish completed without the expected executable: $publishedExe"
}

Get-ChildItem -LiteralPath $publishRoot -Force | Copy-Item -Destination $portableRoot -Recurse -Force
Compress-Archive -LiteralPath $portableRoot -DestinationPath $portableZip -CompressionLevel Optimal

$wix = Get-Command wix -ErrorAction SilentlyContinue
if ($null -eq $wix) {
    throw "WiX Toolset is required to build the MSI. Install the wix .NET tool before running this script."
}

Write-Host "Building unsigned MSI..."
Invoke-Native -Command $wix.Source -Arguments @(
    "build",
    $wixSource,
    "-arch", "x64",
    "-d", "ProductVersion=$Version",
    "-d", "PublishDir=$publishRoot",
    "-d", "IconFile=$iconFile",
    "-o", $installerPath,
    "-pdbtype", "none"
)

$cycloneDx = Get-Command dotnet-CycloneDX -ErrorAction SilentlyContinue
if ($null -ne $cycloneDx) {
    Write-Host "Writing CycloneDX SBOM..."
    Invoke-Native -Command $cycloneDx.Source -Arguments @(
        $solution,
        "--output", $artifactRoot,
        "--filename", (Split-Path -Leaf $sbomPath),
        "--output-format", "Json",
        "--exclude-test-projects",
        "--configuration", $Configuration
    )
} else {
    Write-Warning "CycloneDX is not installed; writing the .NET package manifest as the SBOM fallback."
    $packageManifest = & dotnet list $solution package --include-transitive --format json --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet list package failed with exit code $LASTEXITCODE"
    }
    $packageManifest | Set-Content -LiteralPath $sbomPath -Encoding utf8NoBOM
}

if (-not (Test-Path -LiteralPath $sbomPath)) {
    throw "SBOM generation completed without the expected file: $sbomPath"
}

$checksumLines = foreach ($artifact in @($portableZip, $installerPath, $sbomPath)) {
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $artifact).Hash.ToLowerInvariant()
    "$hash  $([System.IO.Path]::GetFileName($artifact))"
}
$checksumLines | Set-Content -LiteralPath $checksumsPath -Encoding ascii

# The archive and MSI are the distributable outputs; remove their intermediate
# publish trees so release-artifacts stays safe to inspect and share.
Remove-GeneratedPath -Path $publishRoot
Remove-GeneratedPath -Path $portableRoot

Write-Host "Release artifacts:"
Get-ChildItem -LiteralPath $artifactRoot -File |
    Where-Object { $_.Name -ne "publish" } |
    Select-Object Name, Length, LastWriteTime |
    Format-Table -AutoSize
