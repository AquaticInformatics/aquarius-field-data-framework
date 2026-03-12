param(
    [string]$Version = "26.1",
    [string]$Framework = "net10",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Local Plugin Tests ===" -ForegroundColor Cyan
Write-Host "Version: $Version" -ForegroundColor Green
Write-Host "Framework: $Framework" -ForegroundColor Green
Write-Host ""

$srcDir = $PSScriptRoot
$rootDir = Split-Path $srcDir -Parent
$releaseDir = Join-Path $rootDir "releases\$Version"

if (-not (Test-Path $releaseDir)) {
    Write-Error "Release directory not found: $releaseDir"
    Write-Host "Run Local-Deploy-Build.ps1 first to create artifacts" -ForegroundColor Yellow
    exit 1
}

if ($Framework -eq "net10") {
    $fwPath = "net10.0"
    $artifactSuffix = "-net10"
    $frameworkDll = Join-Path $srcDir "Library\$fwPath\FieldDataPluginFramework.dll"
} else {
    $fwPath = "net472"
    $artifactSuffix = ""
    $frameworkDll = Join-Path $srcDir "Library\FieldDataPluginFramework.dll"
}

$pluginTesterZip = Join-Path $releaseDir "PluginTester$artifactSuffix.zip"
$pluginTesterExe = Join-Path $releaseDir "PluginTester$artifactSuffix\PluginTester.exe"

if ($Framework -eq "net10") {
    $nestedExe = Join-Path $releaseDir "PluginTester$artifactSuffix\net10.0\PluginTester.exe"
    if (Test-Path $nestedExe) {
        $pluginTesterExe = $nestedExe
    }
}

if ($Framework -ne "net10" -and (Test-Path (Join-Path $releaseDir "PluginTester.exe"))) {
    $pluginTesterExe = Join-Path $releaseDir "PluginTester.exe"
}

if (-not (Test-Path $pluginTesterExe) -and (Test-Path $pluginTesterZip)) {
    Write-Host "Extracting PluginTester..." -ForegroundColor Yellow
    $extractDir = Join-Path $releaseDir "PluginTester$artifactSuffix"
    Expand-Archive -Path $pluginTesterZip -DestinationPath $extractDir -Force

    if ($Framework -eq "net10") {
        $nestedExe = Join-Path $extractDir "net10.0\PluginTester.exe"
        if (Test-Path $nestedExe) {
            $pluginTesterExe = $nestedExe
        }
    }
}

if (-not (Test-Path $pluginTesterExe)) {
    Write-Error "PluginTester not found: $pluginTesterExe"
    exit 1
}

if (-not (Test-Path $frameworkDll)) {
    Write-Error "Framework DLL not found: $frameworkDll"
    exit 1
}

Write-Host "Using PluginTester: $pluginTesterExe" -ForegroundColor Gray
Write-Host "Using Framework: $frameworkDll" -ForegroundColor Gray
Write-Host ""

$testWorkspace = Join-Path $rootDir "test-results\$Version-$Framework"
if (Test-Path $testWorkspace) {
    Remove-Item $testWorkspace -Recurse -Force
}
New-Item -ItemType Directory -Path $testWorkspace -Force | Out-Null

$testResults = @()

function Test-Plugin {
    param(
        [string]$Name,
        [string]$PluginPath,
        [string]$DataPath,
        [hashtable]$Settings = @{}
    )

    Write-Host "Testing: $Name" -ForegroundColor Cyan

    if (-not (Test-Path $PluginPath)) {
        Write-Warning "Plugin not found: $PluginPath"
        return @{ Name = $Name; Status = "Skipped"; Error = "Plugin not found" }
    }

    if (-not (Test-Path $DataPath)) {
        Write-Warning "Test data not found: $DataPath"
        return @{ Name = $Name; Status = "Skipped"; Error = "Test data not found" }
    }

    $settingsArgs = @()
    foreach ($key in $Settings.Keys) {
        $value = $Settings[$key]
        $settingsArgs += "-Setting=$key=$value"
    }

    try {
        $output = & $pluginTesterExe `
            -Verbose=True `
            -FrameworkAssemblyPath="$frameworkDll" `
            -Plugin="$PluginPath" `
            -Data="$DataPath" `
            @settingsArgs 2>&1

        $exitCode = $LASTEXITCODE

        if ($exitCode -eq 0) {
            Write-Host "  PASSED" -ForegroundColor Green
            return @{ Name = $Name; Status = "Passed"; Output = $output }
        } else {
            Write-Host "  FAILED (Exit code: $exitCode)" -ForegroundColor Red
            return @{ Name = $Name; Status = "Failed"; Error = "Exit code $exitCode"; Output = $output }
        }
    }
    catch {
        Write-Host "  ERROR: $_" -ForegroundColor Red
        return @{ Name = $Name; Status = "Error"; Error = $_.Exception.Message }
    }
}

Write-Host "`n--- JSON Plugin ---" -ForegroundColor Yellow
$jsonPlugin = Join-Path $releaseDir "JsonFieldData$artifactSuffix.plugin"
$jsonDataDir = Join-Path $srcDir "JsonFieldData\data"

$jsonFiles = Get-ChildItem -Path $jsonDataDir -Filter "*.json" -ErrorAction SilentlyContinue

if ($jsonFiles.Count -eq 0) {
    Write-Warning "No JSON test data files found in $jsonDataDir"
    $testResults += @{ Name = "JSON"; Status = "Skipped"; Error = "No test data files" }
} else {
    Write-Host "Found $($jsonFiles.Count) JSON test files" -ForegroundColor Gray

    foreach ($jsonFile in $jsonFiles) {
        $testName = "JSON-$($jsonFile.BaseName)"
        $result = Test-Plugin -Name $testName -PluginPath $jsonPlugin -DataPath $jsonFile.FullName
        $testResults += $result
    }
}

Write-Host "`n--- MultiFile Plugin ---" -ForegroundColor Yellow

Push-Location $testWorkspace
try {
    $multiFileDir = Join-Path $testWorkspace "MultiFile"
    New-Item -ItemType Directory -Path $multiFileDir -Force | Out-Null
    Set-Location $multiFileDir

    $multiFilePlugin = Join-Path $releaseDir "MultiFile$artifactSuffix.plugin"
    if (-not (Test-Path $multiFilePlugin)) {
        Write-Warning "MultiFile plugin not found"
        $testResults += @{ Name = "MultiFile"; Status = "Skipped"; Error = "Plugin not found" }
    }
    else {
        $dataDir = Join-Path $multiFileDir "data"
        New-Item -ItemType Directory -Path $dataDir -Force | Out-Null

        $sourceDataDir = Join-Path $srcDir "JsonFieldData\data"
        $jsonFiles = Get-ChildItem -Path $sourceDataDir -Filter "*.json" -ErrorAction SilentlyContinue

        if ($jsonFiles.Count -eq 0) {
            Write-Warning "No test data files found"
            $testResults += @{ Name = "MultiFile"; Status = "Skipped"; Error = "No test data" }
        }
        else {
            Write-Host "Copying $($jsonFiles.Count) JSON files for MultiFile test..." -ForegroundColor Gray

            foreach ($file in $jsonFiles) {
                Copy-Item $file.FullName -Destination $dataDir -Force
            }

            $configPath = Join-Path $multiFileDir "multifile-config.json"
            $config = @{
                Plugins = @(
                    @{
                        AssemblyQualifiedTypeName = "JsonFieldData.Plugin, JsonFieldData"
                    }
                )
            }
            $config | ConvertTo-Json -Depth 10 | Set-Content $configPath

            $zipPath = Join-Path $multiFileDir "multi-data.zip"
            Compress-Archive -Path "$dataDir\*" -DestinationPath $zipPath -Force

            $jsonPlugin = Join-Path $releaseDir "JsonFieldData$artifactSuffix.plugin"
            if (Test-Path $jsonPlugin) {
                Copy-Item $jsonPlugin -Destination $multiFileDir -Force
            }

            $result = Test-Plugin `
                -Name "MultiFile" `
                -PluginPath $multiFilePlugin `
                -DataPath $zipPath `
                -Settings @{ Config = $configPath }

            $testResults += $result
        }
    }
}
finally {
    Pop-Location
}

Write-Host "`n=== Test Report ===" -ForegroundColor Cyan
$passed = ($testResults | Where-Object { $_.Status -eq "Passed" }).Count
$failed = ($testResults | Where-Object { $_.Status -eq "Failed" -or $_.Status -eq "Error" }).Count
$skipped = ($testResults | Where-Object { $_.Status -eq "Skipped" }).Count

foreach ($result in $testResults) {
    $color = switch ($result.Status) {
        "Passed" { "Green" }
        "Failed" { "Red" }
        "Error" { "Red" }
        "Skipped" { "Yellow" }
        default { "Gray" }
    }
    
    Write-Host "$($result.Status.ToUpper()): $($result.Name)" -ForegroundColor $color
    if ($result.Error) {
        Write-Host "  Error: $($result.Error)" -ForegroundColor Red
    }
}

Write-Host "`n$passed passed, $failed failed, $skipped skipped" -ForegroundColor $(if ($failed -gt 0) { "Red" } else { "Green" })

$reportPath = Join-Path $testWorkspace "test-report.json"
$testResults | ConvertTo-Json -Depth 10 | Set-Content $reportPath
Write-Host "`nDetailed results saved to: $reportPath" -ForegroundColor Gray

if ($failed -gt 0) {
    exit 1
}
