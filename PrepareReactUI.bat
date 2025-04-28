@echo off
echo ===== Preparing React UI for Juno Sidebar =====
echo.

REM Check if node is installed
where node >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Node.js is not installed or not in PATH.
    echo Please install Node.js from https://nodejs.org/
    pause
    exit /b 1
)

REM Navigate to React project directory
cd JunoSidebar.React

REM Ensure package.json has the correct homepage setting
echo Checking package.json...
findstr /C:"\"homepage\": \"./\"" package.json >nul
if %ERRORLEVEL% NEQ 0 (
    echo WARNING: homepage setting might be missing in package.json
    echo Adding "homepage": "./" to package.json would help with file paths
    echo Please update package.json manually or rebuild project
)

REM Install dependencies if needed
if not exist node_modules (
    echo Installing npm dependencies...
    call npm install
    if %ERRORLEVEL% NEQ 0 (
        echo ERROR: Failed to install npm dependencies.
        pause
        exit /b 1
    )
)

REM Build the React project
echo Building React project...
call npm run build
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: React build failed.
    pause
    exit /b 1
)

REM Check if build directory exists
if not exist build (
    echo ERROR: Build directory not found.
    pause
    exit /b 1
)

REM Copy build output to WPF wwwroot folder
echo Copying build output to WPF wwwroot folder...
if exist ..\JunoSidebar.Wpf\wwwroot (
    rmdir /s /q ..\JunoSidebar.Wpf\wwwroot
)
mkdir ..\JunoSidebar.Wpf\wwwroot
xcopy /E /I /Y build\* ..\JunoSidebar.Wpf\wwwroot

REM Copy the fallback.html file to wwwroot if it exists
if exist ..\JunoSidebar.Wpf\Resources\fallback.html (
    echo Copying fallback.html to wwwroot...
    copy ..\JunoSidebar.Wpf\Resources\fallback.html ..\JunoSidebar.Wpf\wwwroot\fallback.html
)

echo.
echo ===== React UI preparation complete! =====
echo You can now build the JunoSidebar.Wpf project in Release mode.
echo.

pause