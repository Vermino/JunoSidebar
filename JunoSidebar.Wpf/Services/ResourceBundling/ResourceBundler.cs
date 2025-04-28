// File: JunoSidebar.Wpf/Services/ResourceBundling/ResourceBundler.cs

using System;
using System.IO;
using System.Threading.Tasks;
using System.Reflection;
using System.Diagnostics;
using System.Linq;

namespace JunoSidebar.Wpf.Services.ResourceBundling
{
    public static class ResourceBundler
    {
        public static async Task<string> ExtractAndGetResourcePath()
        {
            try
            {
                Debug.WriteLine("ResourceBundler: Starting resource resolution");
                string targetFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "JunoSidebar",
                    "Resources"
                );
                
                // Ensure target directory exists
                if (!Directory.Exists(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                }
                
                // In debug mode, check for development server first
                if (App.IsDebugMode)
                {
                    try
                    {
                        Debug.WriteLine("ResourceBundler: Checking for development server...");
                        if (await ReactDevServer.IsRunningAsync())
                        {
                            string devServerUrl = ReactDevServer.GetUrl();
                            Debug.WriteLine($"ResourceBundler: ✓ Development server detected, using {devServerUrl}");
                            return devServerUrl;
                        }
                        else
                        {
                            Debug.WriteLine("ResourceBundler: ✗ Development server not detected");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ResourceBundler: ✗ Error checking development server: {ex.Message}");
                    }
                }

                // Check for local wwwroot directory in debug mode
                if (App.IsDebugMode)
                {
                    string wwwrootPath = Path.Combine(
                        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
                        "wwwroot"
                    );
                    Debug.WriteLine($"ResourceBundler: Checking for wwwroot at {wwwrootPath}");
                    if (Directory.Exists(wwwrootPath))
                    {
                        string debugIndexPath = Path.Combine(wwwrootPath, "index.html");
                        if (File.Exists(debugIndexPath))
                        {
                            Debug.WriteLine($"ResourceBundler: Found index.html at {debugIndexPath}");
                            return GetFileUri(debugIndexPath);
                        }
                        else
                        {
                            Debug.WriteLine($"ResourceBundler: index.html not found in wwwroot");
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"ResourceBundler: wwwroot folder not found");
                    }
                }

                // Extract embedded resources as a fallback
                Debug.WriteLine("ResourceBundler: Trying to extract embedded resources");
                var assembly = Assembly.GetExecutingAssembly();
                string[] resourceNames = assembly.GetManifestResourceNames();
                Debug.WriteLine($"ResourceBundler: Found {resourceNames.Length} embedded resources");
                foreach (var name in resourceNames)
                {
                    Debug.WriteLine($"ResourceBundler: Resource: {name}");
                }

                // Check if we have any UI resources
                bool hasUIResources = resourceNames.Any(r => r.Contains("wwwroot") || r.Contains("JunoUI"));
                
                if (hasUIResources)
                {
                    // Clear the target directory to avoid stale files
                    if (Directory.Exists(targetFolder))
                    {
                        try
                        {
                            foreach (var file in Directory.GetFiles(targetFolder, "*", SearchOption.AllDirectories))
                            {
                                try
                                {
                                    File.Delete(file);
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"ResourceBundler: Warning - could not delete file {file}: {ex.Message}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"ResourceBundler: Warning - error cleaning files: {ex.Message}");
                        }
                    }
                    
                    string indexPath = Path.Combine(targetFolder, "index.html");
                    Debug.WriteLine("ResourceBundler: Extracting embedded resources");
                    await ExtractEmbeddedResources(targetFolder);
                    
                    if (File.Exists(indexPath))
                    {
                        Debug.WriteLine($"ResourceBundler: Using extracted index.html at {indexPath}");
                        
                        // Modify the index.html to add diagnostics and base URL
                        try
                        {
                            string htmlContent = File.ReadAllText(indexPath);
                            bool modified = false;
                            
                            // Add base URL if missing
                            if (!htmlContent.Contains("<base") && htmlContent.Contains("<head>"))
                            {
                                string baseUrl = Path.GetDirectoryName(indexPath).Replace("\\", "/");
                                if (!baseUrl.EndsWith("/")) baseUrl += "/";
                                string baseTag = $"<base href=\"file://{baseUrl}\">";
                                htmlContent = htmlContent.Replace("<head>", "<head>" + baseTag);
                                modified = true;
                                Debug.WriteLine($"ResourceBundler: Added base tag to index.html: {baseTag}");
                            }
                            
                            // Add diagnostic script before closing body tag
                            if (htmlContent.Contains("</body>"))
                            {
                                string diagnosticScript = @"
                                <script>
                                    console.log('Resource loading diagnostics running...');
                                    function diagCheck() {
                                        var rootElement = document.getElementById('root');
                                        console.log('Root element exists:', rootElement !== null);
                                        if (rootElement) {
                                            console.log('Root element children:', rootElement.childNodes.length);
                                            if (rootElement.childNodes.length === 0) {
                                                console.log('React may not have mounted. Adding diagnostic info...');
                                            }
                                        }
                                        console.log('React defined:', typeof React !== 'undefined');
                                        console.log('ReactDOM defined:', typeof ReactDOM !== 'undefined');
                                        var scripts = document.getElementsByTagName('script');
                                        console.log('Scripts loaded:', scripts.length);
                                    }
                                    setTimeout(diagCheck, 1000);
                                </script>";
                                
                                htmlContent = htmlContent.Replace("</body>", diagnosticScript + "</body>");
                                modified = true;
                                Debug.WriteLine("ResourceBundler: Added diagnostic script to index.html");
                            }
                            
                            if (modified)
                            {
                                File.WriteAllText(indexPath, htmlContent);
                                Debug.WriteLine("ResourceBundler: Updated index.html with fixes");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"ResourceBundler: Error modifying HTML: {ex.Message}");
                        }
                        
                        // Create a fallback.html file in case index.html fails
                        try
                        {
                            string fallbackPath = Path.Combine(targetFolder, "fallback.html");
                            string fallbackHtml = CreateFallbackHtml(targetFolder);
                            File.WriteAllText(fallbackPath, fallbackHtml);
                            Debug.WriteLine($"ResourceBundler: Created fallback HTML at {fallbackPath}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"ResourceBundler: Error creating fallback HTML: {ex.Message}");
                        }
                        
                        return GetFileUri(indexPath);
                    }
                    else
                    {
                        Debug.WriteLine("ResourceBundler: index.html was not extracted");
                        
                        // Try to use fallback.html or any other HTML file
                        string fallbackPath = Path.Combine(targetFolder, "fallback.html");
                        if (File.Exists(fallbackPath))
                        {
                            Debug.WriteLine($"ResourceBundler: Using fallback HTML at {fallbackPath}");
                            return GetFileUri(fallbackPath);
                        }
                        
                        var htmlFiles = Directory.GetFiles(targetFolder, "*.html", SearchOption.AllDirectories);
                        if (htmlFiles.Length > 0)
                        {
                            Debug.WriteLine($"ResourceBundler: Using alternative HTML file: {htmlFiles[0]}");
                            return GetFileUri(htmlFiles[0]);
                        }
                    }
                }
                else
                {
                    Debug.WriteLine("ResourceBundler: No UI resources found in embedded resources");
                }

                // Generate a fallback HTML file if all else fails
                Debug.WriteLine("ResourceBundler: Generating fallback HTML");
                string errorHtml = CreateFallbackHtml(targetFolder);
                string errorPath = Path.Combine(targetFolder, "error.html");
                File.WriteAllText(errorPath, errorHtml);
                Debug.WriteLine($"ResourceBundler: Using fallback HTML at {errorPath}");
                return GetFileUri(errorPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ResourceBundler: Error preparing resources: {ex.Message}");
                Debug.WriteLine($"ResourceBundler: Stack trace: {ex.StackTrace}");
                
                // Create a minimal error page as last resort
                string minimalErrorHtml = $@"
                    <!DOCTYPE html>
                    <html>
                    <head>
                        <title>Juno Error</title>
                        <style>
                            body {{ font-family: sans-serif; padding: 20px; line-height: 1.6; }}
                            .error {{ color: #e53e3e; background-color: #fff5f5; padding: 15px; border-left: 4px solid #e53e3e; }}
                            h1 {{ color: #2c5282; }}
                            pre {{ background-color: #f7fafc; padding: 10px; overflow: auto; white-space: pre-wrap; }}
                        </style>
                    </head>
                    <body>
                        <h1>Juno Resource Loading Error</h1>
                        <div class='error'>
                            <p>Could not load UI resources. Error details:</p>
                            <pre>{ex.Message}\n\n{ex.StackTrace}</pre>
                        </div>
                        <p>Please check the application logs for more details.</p>
                    </body>
                    </html>";
                
                string errorPath = Path.Combine(
                    Path.GetTempPath(),
                    "JunoResourceError.html"
                );
                File.WriteAllText(errorPath, minimalErrorHtml);
                return GetFileUri(errorPath);
            }
        }

private static string CreateFallbackHtml(string targetFolder)
{
    // Create a more detailed fallback HTML with diagnostics
    string diagnosticsLogHtml = "";
    try
    {
        var logEntries = new System.Collections.Generic.List<string>();
        
        // Check resource directory
        diagnosticsLogHtml += $"<h3>Resource Directory: {targetFolder}</h3>";
        diagnosticsLogHtml += "<ul>";
        
        if (Directory.Exists(targetFolder))
        {
            var files = Directory.GetFiles(targetFolder, "*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var relativePath = file.Replace(targetFolder, "").TrimStart('\\', '/');
                var fileInfo = new FileInfo(file);
                diagnosticsLogHtml += $"<li>{relativePath} ({fileInfo.Length} bytes)</li>";
            }
        }
        else
        {
            diagnosticsLogHtml += "<li>Resource directory does not exist!</li>";
        }
        
        diagnosticsLogHtml += "</ul>";
        
        // Check assembly resources
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames();
        
        diagnosticsLogHtml += $"<h3>Embedded Resources ({resourceNames.Length}):</h3>";
        diagnosticsLogHtml += "<ul>";
        foreach (var name in resourceNames)
        {
            diagnosticsLogHtml += $"<li>{name}</li>";
        }
        diagnosticsLogHtml += "</ul>";
    }
    catch (Exception ex)
    {
        diagnosticsLogHtml += $"<p>Error generating diagnostics: {ex.Message}</p>";
    }
    
    return $@"
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset='utf-8'>
            <title>Juno Resource Loading Fallback</title>
            <style>
                body {{ font-family: Arial, sans-serif; margin: 20px; line-height: 1.6; }}
                .error {{ color: #e53e3e; background-color: #fff5f5; padding: 15px; border-left: 4px solid #e53e3e; margin-bottom: 20px; }}
                .warning {{ color: #c05621; background-color: #fffaf0; padding: 15px; border-left: 4px solid #c05621; margin-bottom: 20px; }}
                h1 {{ color: #2c5282; }}
                h3 {{ color: #4a5568; margin-top: 30px; }}
                ul {{ margin-top: 10px; }}
                button {{ background-color: #4299e1; color: white; border: none; padding: 10px 15px; border-radius: 4px; cursor: pointer; }}
                button:hover {{ background-color: #3182ce; }}
                pre {{ background-color: #f7fafc; padding: 10px; overflow: auto; }}
            </style>
            <script>
                function reloadPage() {{
                    window.location.reload();
                }}
                function logNavigatorInfo() {{
                    const info = document.getElementById('navigator-info');
                    const data = {{
                        userAgentString: navigator.userAgent,
                        platform: navigator.platform,
                        webView: window.chrome && window.chrome.webview ? 'Available' : 'Not Available'
                    }};
                    info.textContent = JSON.stringify(data, null, 2);
                }}
                window.onload = logNavigatorInfo;
            </script>
        </head>
        <body>
            <h1>Juno Resource Loading Fallback</h1>
            <div class='warning'>
                <p>The main UI resources could not be loaded. This fallback page is being displayed instead.</p>
                <p>This could be due to:</p>
                <ul>
                    <li>Missing or incomplete build</li>
                    <li>Resources not properly embedded</li>
                    <li>Development server not running</li>
                    <li>File permission issues</li>
                </ul>
            </div>
            
            <button onclick='reloadPage()'>Try Again</button>
            
            <h3>Environment Information</h3>
            <pre id='navigator-info'>Loading...</pre>
            
            <div class='diagnostics'>
                <h2>Diagnostics Information</h2>
                {diagnosticsLogHtml}
            </div>
            
            <div class='error'>
                <p>Please check the application logs for more details.</p>
            </div>
        </body>
        </html>";
}

        private static async Task ExtractEmbeddedResources(string targetFolder)
        {
            try
            {
                Debug.WriteLine($"Extracting embedded resources to {targetFolder}");
                var assembly = Assembly.GetExecutingAssembly();
                string[] resourceNames = assembly.GetManifestResourceNames();
                Debug.WriteLine($"Found {resourceNames.Length} embedded resources:");
                foreach (string resourceName in resourceNames)
                {
                    Debug.WriteLine($"- {resourceName}");
                }
                
                int extractedCount = 0;
                foreach (string resourceName in resourceNames)
                {
                    if (!resourceName.Contains("JunoUI") && !resourceName.Contains("wwwroot"))
                        continue;
                    
                    try
                    {
                        string fileName;
                        
                        // Handle different resource naming patterns
                        if (resourceName.Contains("JunoUI"))
                        {
                            fileName = resourceName.Replace("JunoSidebar.Wpf.Resources.JunoUI.", "");
                        }
                        else if (resourceName.Contains("wwwroot"))
                        {
                            int wwwrootIndex = resourceName.IndexOf("wwwroot.");
                            if (wwwrootIndex >= 0)
                            {
                                fileName = resourceName.Substring(wwwrootIndex + 8); // Length of "wwwroot."
                            }
                            else
                            {
                                fileName = resourceName.Replace("JunoSidebar.Wpf.wwwroot.", "");
                            }
                        }
                        else
                        {
                            continue;
                        }
                        
                        // Process file path with directory structure
                        string filePath;
                        if (fileName.Contains("."))
                        {
                            string extension = "";
                            int lastDotIndex = fileName.LastIndexOf('.');
                            if (lastDotIndex > 0)
                            {
                                extension = fileName.Substring(lastDotIndex);
                                fileName = fileName.Substring(0, lastDotIndex);
                            }
                            
                            // Replace dots with directory separators for nested paths
                            fileName = fileName.Replace(".", Path.DirectorySeparatorChar.ToString());
                            filePath = Path.Combine(targetFolder, fileName + extension);
                        }
                        else
                        {
                            filePath = Path.Combine(targetFolder, fileName);
                        }
                        
                        // Create directory if it doesn't exist
                        string directoryPath = Path.GetDirectoryName(filePath);
                        if (!string.IsNullOrEmpty(directoryPath))
                        {
                            Directory.CreateDirectory(directoryPath);
                        }
                        
                        // Extract the resource to the target path
                        using (Stream resourceStream = assembly.GetManifestResourceStream(resourceName))
                        {
                            if (resourceStream != null)
                            {
                                using (FileStream fileStream = new FileStream(filePath, FileMode.Create))
                                {
                                    await resourceStream.CopyToAsync(fileStream);
                                }
                                Debug.WriteLine($"Extracted: {fileName} to {filePath}");
                                extractedCount++;
                            }
                            else
                            {
                                Debug.WriteLine($"Warning: Resource stream is null for {resourceName}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error extracting resource {resourceName}: {ex.Message}");
                    }
                }
                
                Debug.WriteLine($"Extraction complete. Extracted {extractedCount} resources.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error extracting resources: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private static string GetFileUri(string filePath)
        {
            return new Uri(filePath).AbsoluteUri;
        }
    }
}