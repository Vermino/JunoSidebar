using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Linq;
using System.Security;
using System.IO;
using System.Threading;

namespace JunoSidebar.Wpf.Services.Tools
{
    public static class ToolSandbox
    {
        private const int DefaultExecutionTimeLimit = 30000;
        private const long DefaultMemoryLimit = 100 * 1024 * 1024;

        public static async Task<ToolResult> ExecuteInSandboxAsync(
            ITool tool,
            Dictionary<string, object> parameters,
            int? timeLimit = null,
            long? memoryLimit = null)
        {
            if (tool == null)
            {
                return ToolResult.CreateError("Tool cannot be null");
            }

            using var cts = new CancellationTokenSource(timeLimit ?? DefaultExecutionTimeLimit);
            try
            {
                var validationResult = ValidateParameters(tool, parameters);
                if (!validationResult.Success)
                {
                    return validationResult;
                }

                var resourceMonitoring = MonitorResourcesAsync(
                    cts.Token,
                    memoryLimit ?? DefaultMemoryLimit);
                var executeTask = ExecuteToolWithIsolationAsync(tool, parameters, cts.Token);
                var completedTask = await Task.WhenAny(executeTask, resourceMonitoring);

                if (completedTask == resourceMonitoring)
                {
                    cts.Cancel();
                    var monitorResult = await resourceMonitoring;
                    return ToolResult.CreateError($"Tool execution aborted: {monitorResult}");
                }
                else
                {
                    return await executeTask;
                }
            }
            catch (OperationCanceledException)
            {
                return ToolResult.CreateError("Tool execution timed out");
            }
            catch (Exception ex)
            {
                return ToolResult.CreateError($"Tool execution failed: {ex.Message}");
            }
        }

        private static ToolResult ValidateParameters(ITool tool, Dictionary<string, object> parameters)
        {
            try
            {
                var parameterSchema = tool.GetParameterSchema();
                foreach (var requiredParam in parameterSchema.Required)
                {
                    if (!parameters.ContainsKey(requiredParam))
                    {
                        return ToolResult.CreateError($"Missing required parameter: {requiredParam}");
                    }
                }

                foreach (var param in parameters)
                {
                    var paramProperty = parameterSchema.Properties.FirstOrDefault(p => p.Name == param.Key);
                    if (paramProperty == null)
                    {
                        return ToolResult.CreateError($"Unknown parameter: {param.Key}");
                    }
                    
                    bool isValid = IsValidParameterValue(param.Value, paramProperty);
                    if (!isValid)
                    {
                        return ToolResult.CreateError($"Invalid value for parameter '{param.Key}'");
                    }
                }

                return ToolResult.CreateSuccess("Parameters validated successfully");
            }
            catch (Exception ex)
            {
                return ToolResult.CreateError($"Parameter validation failed: {ex.Message}");
            }
        }

        private static bool IsValidParameterValue(object value, ToolParameterProperty property)
        {
            switch (property.Type)
            {
                case ToolPropertyType.String:
                    return value is string;
                case ToolPropertyType.Integer:
                    return value is int || (value is string strInt && int.TryParse(strInt, out _));
                case ToolPropertyType.Number:
                    return value is double || value is float || value is decimal || 
                           (value is string strNum && double.TryParse(strNum, out _));
                case ToolPropertyType.Boolean:
                    return value is bool || (value is string strBool && bool.TryParse(strBool, out _));
                case ToolPropertyType.Date:
                case ToolPropertyType.DateTime:
                    return value is DateTime || (value is string strDate && DateTime.TryParse(strDate, out _));
                case ToolPropertyType.Enum:
                    if (property.Options == null || property.Options.Count == 0)
                        return true;
                    return value is string strEnum && property.Options.Any(o => o.Value == strEnum);
                case ToolPropertyType.FilePath:
                    return value is string strPath && File.Exists(strPath);
                case ToolPropertyType.DirectoryPath:
                    return value is string strDir && Directory.Exists(strDir);
                default:
                    return true; 
            }
        }

        private static async Task<ToolResult> ExecuteToolWithIsolationAsync(
            ITool tool,
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken)
        {
            try
            {
                var safeParameters = SanitizeParameters(parameters);
                var result = await tool.ExecuteAsync(safeParameters);
                cancellationToken.ThrowIfCancellationRequested();
                return result;
            }
            catch (OperationCanceledException)
            {
                throw; 
            }
            catch (SecurityException ex)
            {
                return ToolResult.CreateError($"Security violation: {ex.Message}");
            }
            catch (Exception ex)
            {
                return ToolResult.CreateError($"Execution error: {ex.Message}");
            }
        }

        private static Dictionary<string, object> SanitizeParameters(Dictionary<string, object> parameters)
        {
            var sanitized = new Dictionary<string, object>();
            foreach (var param in parameters)
            {
                if (param.Value is string stringValue)
                {
                    sanitized[param.Key] = SanitizeString(stringValue);
                }
                else
                {
                    sanitized[param.Key] = param.Value;
                }
            }
            return sanitized;
        }

        private static string SanitizeString(string input)
        {
            string sanitized = input
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;")
                .Replace("/", "&#47;")
                .Replace("\\", "&#92;")
                .Replace("`", "&#96;");
            return sanitized;
        }

        private static async Task<string> MonitorResourcesAsync(
            CancellationToken cancellationToken,
            long memoryLimit)
        {
            var process = Process.GetCurrentProcess();
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    process.Refresh();
                    if (process.WorkingSet64 > memoryLimit)
                    {
                        return $"Memory limit exceeded: {process.WorkingSet64 / (1024 * 1024)} MB > {memoryLimit / (1024 * 1024)} MB";
                    }
                    await Task.Delay(500, cancellationToken);
                }
                return "Monitoring cancelled";
            }
            catch (OperationCanceledException)
            {
                return "Monitoring cancelled";
            }
            catch (Exception ex)
            {
                return $"Error monitoring resources: {ex.Message}";
            }
        }
    }
}