// File: JunoSidebar.React/scripts/copy-build.js
const fs = require('fs');
const path = require('path');

console.log('Starting copy-build script...');

// Define paths
const sourceDir = path.resolve(__dirname, '../build');
const targetDir = path.resolve(__dirname, '../../JunoSidebar.Wpf/wwwroot');

// Check if source directory exists
if (!fs.existsSync(sourceDir)) {
    console.error(`Error: Source directory does not exist: ${sourceDir}`);
    process.exit(1);
}

// Create target directory if it doesn't exist
if (!fs.existsSync(targetDir)) {
    console.log(`Creating target directory: ${targetDir}`);
    fs.mkdirSync(targetDir, { recursive: true });
}

/**
 * Copy a file from source to target
 */
function copyFile(source, target) {
    const targetFile = target;

    // Create target directory if it doesn't exist
    const targetDir = path.dirname(targetFile);
    if (!fs.existsSync(targetDir)) {
        fs.mkdirSync(targetDir, { recursive: true });
    }

    fs.copyFileSync(source, targetFile);
    console.log(`Copied: ${path.relative(sourceDir, source)} -> ${path.relative(targetDir, targetFile)}`);
}

/**
 * Copy directory recursively
 */
function copyDirectory(source, target) {
    // Create target directory if it doesn't exist
    if (!fs.existsSync(target)) {
        fs.mkdirSync(target, { recursive: true });
    }

    // Get all files in the source directory
    const files = fs.readdirSync(source);

    // Copy each file/directory
    for (const file of files) {
        const sourceFile = path.join(source, file);
        const targetFile = path.join(target, file);
        const stats = fs.statSync(sourceFile);

        if (stats.isDirectory()) {
            copyDirectory(sourceFile, targetFile);
        } else {
            copyFile(sourceFile, targetFile);
        }
    }
}

// Copy the build directory to the target directory
try {
    console.log(`Copying from ${sourceDir} to ${targetDir}`);
    copyDirectory(sourceDir, targetDir);
    console.log('Build files copied successfully!');
} catch (error) {
    console.error('Error copying build files:', error);
    process.exit(1);
}