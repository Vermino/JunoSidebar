# File: BuildAndPackage.ps1
# Script to build React UI and package it with the WPF application for release

param (
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

# Define paths
$scriptDir = $PSScriptRoot
$reactProjectPath = Join-Path -Path $scriptDir -ChildPath "JunoSidebar.React"
$wpfProjectPath = Join-Path -Path $scriptDir -ChildPath "JunoSidebar.Wpf"
$reactBuildOutputPath = Join-Path -Path $reactProjectPath -ChildPath "build"
$wpfWwwrootPath = Join-Path -Path $wpfProjectPath -ChildPath "wwwroot"

Write-Host "======= Juno Sidebar Build & Package Script =======" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "React project: $reactProjectPath" -ForegroundColor Cyan
Write-Host "WPF project: $wpfProjectPath" -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan

# Check if Node.js and npm are installed
try {
    $nodeVersion = node --version
    $npmVersion = npm --version
    Write-Host "Node.js version: $nodeVersion" -ForegroundColor Green
    Write-Host "npm version: $npmVersion" -ForegroundColor Green
} catch {
    Write-Error "Node.js or npm is not installed or not in PATH. Please install Node.js and npm."
    exit 1
}

# Step 1: Build React project
Write-Host "`nStep 1: Building React project..." -ForegroundColor Yellow
Push-Location $reactProjectPath
try {
    # Install dependencies if node_modules doesn't exist
    if (!(Test-Path -Path "node_modules")) {
        Write-Host "Installing npm dependencies..." -ForegroundColor Gray
        npm install
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to install npm dependencies."
            exit 1
        }
    }

    # Build the React project
    Write-Host "Running npm build..." -ForegroundColor Gray
    npm run build
    if ($LASTEXITCODE -ne 0) {
        Write-Error "React build failed."
        exit 1
    }

    if (!(Test-Path -Path $reactBuildOutputPath)) {
        Write-Error "Build failed - output directory not found: $reactBuildOutputPath"
        exit 1
    }
    
    Write-Host "React build successful!" -ForegroundColor Green
} finally {
    Pop-Location
}

# Step 2: Copy React build output to WPF wwwroot folder
Write-Host "`nStep 2: Copying React build to WPF wwwroot folder..." -ForegroundColor Yellow

# Ensure wwwroot directory exists
if (!(Test-Path -Path $wpfWwwrootPath)) {
    New-Item -Path $wpfWwwrootPath -ItemType Directory -Force
    Write-Host "Created wwwroot directory" -ForegroundColor Gray
}

# Clear existing content in wwwroot
Get-ChildItem -Path $wpfWwwrootPath -Recurse | Remove-Item -Force -Recurse
Write-Host "Cleared existing content in wwwroot" -ForegroundColor Gray

# Copy React build files to wwwroot
Copy-Item -Path "$reactBuildOutputPath\*" -Destination $wpfWwwrootPath -Recurse -Force
Write-Host "Copied React build files to wwwroot" -ForegroundColor Green

# Step 3: Build WPF project
Write-Host "`nStep 3: Building WPF project..." -ForegroundColor Yellow
$outputPath = Join-Path -Path $scriptDir -ChildPath "build\$Configuration"

# Create output directory if it doesn't exist
if (!(Test-Path -Path $outputPath)) {
    New-Item -Path $outputPath -ItemType Directory -Force
}

# Build WPF project
dotnet publish $wpfProjectPath -c $Configuration -o $outputPath
if ($LASTEXITCODE -ne 0) {
    Write-Error "WPF build failed."
    exit 1
}

Write-Host "WPF build successful!" -ForegroundColor Green
Write-Host "Output path: $outputPath" -ForegroundColor Green

# Step 4: Create a zip file for distribution
Write-Host "`nStep 4: Creating distribution package..." -ForegroundColor Yellow
$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo("$outputPath\JunoSidebar.Wpf.exe")
$version = $versionInfo.FileVersion -replace ",", "."
if ([string]::IsNullOrEmpty($version)) {
    $version = "1.0.0"
}

$zipFileName = "JunoSidebar-v$version.zip"
$zipFilePath = Join-Path -Path $scriptDir -ChildPath "build\$zipFileName"

# Remove existing zip file if it exists
if (Test-Path -Path $zipFilePath) {
    Remove-Item -Path $zipFilePath -Force
    Write-Host "Removed existing zip file: $zipFilePath" -ForegroundColor Gray
}

# Create zip file
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($outputPath, $zipFilePath)

Write-Host "Created distribution package: $zipFilePath" -ForegroundColor Green

Write-Host "`n======= Build & Package Completed Successfully! =======" -ForegroundColor Cyan
Write-Host "Package: $zipFilePath" -ForegroundColor Cyan
Write-Host "Version: $version" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan