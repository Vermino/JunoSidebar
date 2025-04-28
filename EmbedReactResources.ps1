# File: EmbedReactResources.ps1
# Script to build and embed React resources into the .NET project

param (
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

# Define paths
$scriptDir = $PSScriptRoot
$reactProjectPath = Join-Path -Path $scriptDir -ChildPath "JunoSidebar.React"
$wpfProjectPath = Join-Path -Path $scriptDir -ChildPath "JunoSidebar.Wpf"
$buildOutputPath = Join-Path -Path $reactProjectPath -ChildPath "build"
$resourcesDir = Join-Path -Path $wpfProjectPath -ChildPath "Resources\JunoUI"
$csprojPath = Join-Path -Path $wpfProjectPath -ChildPath "JunoSidebar.Wpf.csproj"

# Ensure the resources directory exists
if (!(Test-Path -Path $resourcesDir)) {
    Write-Host "Creating resources directory: $resourcesDir"
    New-Item -Path $resourcesDir -ItemType Directory -Force
}

# Check if Node.js is installed
try {
    $nodeVersion = node --version
    Write-Host "Node.js version: $nodeVersion"
} catch {
    Write-Error "Node.js is not installed or not in PATH. Please install Node.js and npm."
    exit 1
}

# Check if npm is installed
try {
    $npmVersion = npm --version
    Write-Host "npm version: $npmVersion"
} catch {
    Write-Error "npm is not installed or not in PATH. Please install npm."
    exit 1
}

# Build React project
Write-Host "Building React project..."
Push-Location $reactProjectPath
try {
    # Install dependencies if node_modules doesn't exist
    if (!(Test-Path -Path "node_modules")) {
        Write-Host "Installing npm dependencies..."
        npm install
    }

    # Build the React project
    Write-Host "Running npm build..."
    npm run build

    if (!(Test-Path -Path $buildOutputPath)) {
        Write-Error "Build failed - output directory not found: $buildOutputPath"
        exit 1
    }
} finally {
    Pop-Location
}

# Clear existing resources
Remove-Item -Path "$resourcesDir\*" -Recurse -Force -ErrorAction SilentlyContinue

# Copy build output to resources directory
Write-Host "Copying build output to resources directory..."
Copy-Item -Path "$buildOutputPath\*" -Destination $resourcesDir -Recurse -Force

# Update project file to include all resources
Write-Host "Updating .csproj file to include resources..."

# Read the existing csproj file
$csproj = [xml](Get-Content $csprojPath)

# Define the namespace
$ns = "http://schemas.microsoft.com/developer/msbuild/2003"

# Find or create the ItemGroup for embedded resources
$itemGroup = $csproj.Project.ItemGroup | Where-Object { $_.EmbeddedResource -ne $null }
if ($itemGroup -eq $null) {
    $itemGroup = $csproj.CreateElement("ItemGroup", $ns)
    $csproj.Project.AppendChild($itemGroup)
}

# Remove existing embedded resources that match our pattern
$nodesToRemove = @()
if ($itemGroup.EmbeddedResource -ne $null) {
    foreach ($resource in $itemGroup.EmbeddedResource) {
        if ($resource.Include -like "Resources\JunoUI\*") {
            $nodesToRemove += $resource
        }
    }
}

foreach ($node in $nodesToRemove) {
    $itemGroup.RemoveChild($node)
}

# Add new embedded resources
$files = Get-ChildItem -Path $resourcesDir -Recurse -File
foreach ($file in $files) {
    $relativePath = $file.FullName.Substring($wpfProjectPath.Length + 1).Replace("\", "/")
    
    $embedResource = $csproj.CreateElement("EmbeddedResource", $ns)
    $includeAttr = $csproj.CreateAttribute("Include")
    $includeAttr.Value = $relativePath
    $embedResource.Attributes.Append($includeAttr)
    
    $itemGroup.AppendChild($embedResource)
}

# Save the updated csproj file
$csproj.Save($csprojPath)

Write-Host "Resources embedded successfully."
Write-Host "Building WPF project in $Configuration configuration..."

# Build the WPF project
$buildProcess = Start-Process -FilePath "dotnet" -ArgumentList "build", $wpfProjectPath, "-c", $Configuration -NoNewWindow -PassThru -Wait
if ($buildProcess.ExitCode -ne 0) {
    Write-Error "WPF project build failed with exit code $($buildProcess.ExitCode)"
    exit 1
}

Write-Host "Build completed successfully!"