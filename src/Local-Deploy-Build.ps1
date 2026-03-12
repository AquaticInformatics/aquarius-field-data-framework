param(
    [string]$Version = "26.1",
    [string]$Framework = "net10",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Local Deploy Build ===" -ForegroundColor Cyan
Write-Host "Version: $Version" -ForegroundColor Green
Write-Host "Framework: $Framework" -ForegroundColor Green
Write-Host "Configuration: $Configuration" -ForegroundColor Green
Write-Host ""

$srcDir = $PSScriptRoot
$rootDir = Split-Path $srcDir -Parent
$solutionFile = Join-Path $srcDir "FieldDataFramework.sln"
$nuspecFile = Join-Path $srcDir "Aquarius.FieldDataFramework.nuspec"
$releaseDir = Join-Path $rootDir "releases\$Version"

if (-not (Test-Path $solutionFile)) {
    Write-Error "Solution file not found: $solutionFile"
    exit 1
}

Write-Host "Creating release directory: $releaseDir" -ForegroundColor Yellow
if (Test-Path $releaseDir) {
    Remove-Item $releaseDir -Recurse -Force
}
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

Write-Host "Updating NuGet package version to $Version..." -ForegroundColor Yellow
if (Test-Path $nuspecFile) {
    $nuspecContent = Get-Content $nuspecFile -Raw
    $nuspecContent = $nuspecContent -replace '<version>.*?</version>', "<version>$Version</version>"
    $nuspecContent = $nuspecContent -replace '0\.0\.0', $Version
    Set-Content $nuspecFile -Value $nuspecContent
}

Write-Host ""
Write-Host "Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore $solutionFile
if ($LASTEXITCODE -ne 0) {
    Write-Error "Package restore failed"
    exit 1
}

Write-Host ""
Write-Host "Building solution..." -ForegroundColor Yellow
dotnet build $solutionFile -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed"
    exit 1
}

Write-Host ""
Write-Host "Creating NuGet package..." -ForegroundColor Yellow
Push-Location $rootDir
nuget pack $nuspecFile
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    Write-Error "NuGet pack failed. Ensure nuget.exe is in PATH and all files exist."
    exit 1
}
Pop-Location

Write-Host ""
Write-Host "Collecting artifacts..." -ForegroundColor Yellow

function Copy-Artifact {
    param(
        [string]$SourcePath,
        [string]$DestName,
        [string]$Type = "file"
    )

    $fullSource = Join-Path $rootDir $SourcePath

    if (-not (Test-Path $fullSource)) {
        Write-Warning "Artifact not found: $fullSource"
        return
    }

    $destPath = Join-Path $releaseDir $DestName

    if ($Type -eq "zip") {
        Write-Host "  - Zipping $DestName..." -ForegroundColor Gray
        if (Test-Path "$destPath.zip") {
            Remove-Item "$destPath.zip" -Force
        }
        Compress-Archive -Path $fullSource -DestinationPath "$destPath.zip" -Force
    }
    elseif ($Type -eq "file") {
        Write-Host "  - Copying $DestName..." -ForegroundColor Gray
        Copy-Item $fullSource -Destination $destPath -Force
    }
}

if ($Framework -eq "net10") {
    $fwPath = "net10.0"
    $artifactSuffix = "-net10"
} elseif ($Framework -eq "net472") {
    $fwPath = ""
    $artifactSuffix = ""
} elseif ($Framework -eq "net48") {
    $fwPath = ""
    $artifactSuffix = ""
} else {
    $fwPath = $Framework
    $artifactSuffix = "-$Framework"
}

if ($Framework -eq "net10") {
    Copy-Artifact "src\PluginPackager\bin\$Configuration\$fwPath" "PluginPackager$artifactSuffix" "zip"
    Copy-Artifact "src\PluginTester\bin\$Configuration\$fwPath" "PluginTester$artifactSuffix" "zip"
    Copy-Artifact "src\JsonFieldData\deploy\$Configuration\$fwPath\JsonFieldData.plugin" "JsonFieldData$artifactSuffix.plugin" "file"
    Copy-Artifact "src\MultiFile\deploy\$Configuration\$fwPath\MultiFile.plugin" "MultiFile$artifactSuffix.plugin" "file"
    Copy-Artifact "src\MultiFile.Configurator\bin\$Configuration\$fwPath" "MultiFile.Configurator$artifactSuffix" "zip"
    Copy-Artifact "src\FieldVisitHotFolderService\bin\$Configuration\$fwPath" "FieldVisitHotFolderService$artifactSuffix" "zip"
} else {
    Copy-Artifact "src\Library" "FieldDataPluginFramework" "zip"
    Copy-Artifact "src\PluginPackager\bin\$Configuration\PluginPackager.exe" "PluginPackager.exe" "file"
    Copy-Artifact "src\PluginTester\bin\$Configuration\PluginTester.exe" "PluginTester.exe" "file"
    Copy-Artifact "src\JsonFieldData\deploy\$Configuration\JsonFieldData.plugin" "JsonFieldData.plugin" "file"
    Copy-Artifact "src\MultiFile\deploy\$Configuration\MultiFile.plugin" "MultiFile.plugin" "file"
    Copy-Artifact "src\MultiFile.Configurator\bin\$Configuration\MultiFile.Configurator.exe" "MultiFile.Configurator.exe" "file"
    Copy-Artifact "src\FieldVisitHotFolderService\bin\$Configuration" "FieldVisitHotFolderService" "zip"
}

Write-Host "  - Copying NuGet packages..." -ForegroundColor Gray
Get-ChildItem -Path $rootDir -Filter "Aquarius.FieldDataFramework.*.nupkg" | ForEach-Object {
    Copy-Item $_.FullName -Destination $releaseDir -Force
}

Write-Host ""
Write-Host "=== Build Complete ===" -ForegroundColor Green
Write-Host "Artifacts saved to: $releaseDir" -ForegroundColor Green
Write-Host ""
Write-Host "Artifact list:" -ForegroundColor Cyan
Get-ChildItem $releaseDir | ForEach-Object {
    Write-Host "  - $($_.Name)" -ForegroundColor Gray
}
