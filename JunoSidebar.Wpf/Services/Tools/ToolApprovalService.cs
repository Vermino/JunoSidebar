// File: JunoSidebar.Wpf/Services/Tools/ToolApprovalService.cs

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace JunoSidebar.Wpf.Services.Tools
{
    /// <summary>
    /// Service for managing tool execution approvals and audit logging
    /// </summary>
    public class ToolApprovalService
    {
        private readonly string _auditLogPath;
        private readonly PermissionManager _permissionManager;
        private readonly ConcurrentDictionary<string, PendingApproval> _pendingApprovals;
        private readonly SemaphoreSlim _logSemaphore;

        public event EventHandler<ToolApprovalRequest>? ApprovalRequired;
        public event EventHandler<ToolExecutionEvent>? ToolExecuted;

        public ToolApprovalService(PermissionManager permissionManager, string? auditLogDirectory = null)
        {
            _permissionManager = permissionManager ?? throw new ArgumentNullException(nameof(permissionManager));
            _pendingApprovals = new ConcurrentDictionary<string, PendingApproval>();
            _logSemaphore = new SemaphoreSlim(1, 1);

            var logDir = auditLogDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "JunoSidebar",
                "AuditLogs");

            Directory.CreateDirectory(logDir);
            _auditLogPath = Path.Combine(logDir, $"tool_execution_{DateTime.UtcNow:yyyy-MM}.json");

            Debug.WriteLine($"ToolApprovalService initialized with audit log: {_auditLogPath}");
        }

        /// <summary>
        /// Request approval to execute a tool
        /// </summary>
        public async Task<bool> RequestApprovalAsync(
            string toolId,
            string toolName,
            Dictionary<string, object> parameters,
            string description,
            string? contextInfo = null,
            CancellationToken cancellationToken = default)
        {
            // Check if tool requires approval
            if (!RequiresApproval(toolId, parameters))
            {
                Debug.WriteLine($"Tool {toolName} does not require approval");
                return true;
            }

            var requestId = Guid.NewGuid().ToString();
            var approvalSource = new TaskCompletionSource<bool>();

            var request = new ToolApprovalRequest
            {
                RequestId = requestId,
                ToolId = toolId,
                ToolName = toolName,
                Parameters = parameters,
                Description = description,
                ContextInfo = contextInfo,
                RequestedAt = DateTime.UtcNow
            };

            var pending = new PendingApproval
            {
                Request = request,
                CompletionSource = approvalSource
            };

            _pendingApprovals.TryAdd(requestId, pending);

            Debug.WriteLine($"Requesting approval for tool: {toolName}");

            // Raise event for UI
            ApprovalRequired?.Invoke(this, request);

            // Wait for approval with timeout
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await using (linkedCts.Token.Register(() => approvalSource.TrySetCanceled()))
                {
                    return await approvalSource.Task;
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"Approval request timed out or was cancelled for tool: {toolName}");
                return false;
            }
            finally
            {
                _pendingApprovals.TryRemove(requestId, out _);
            }
        }

        /// <summary>
        /// Approve a pending tool execution request
        /// </summary>
        public bool ApproveRequest(string requestId)
        {
            if (_pendingApprovals.TryGetValue(requestId, out var pending))
            {
                Debug.WriteLine($"Approved tool execution: {pending.Request.ToolName}");
                pending.CompletionSource.TrySetResult(true);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Deny a pending tool execution request
        /// </summary>
        public bool DenyRequest(string requestId)
        {
            if (_pendingApprovals.TryGetValue(requestId, out var pending))
            {
                Debug.WriteLine($"Denied tool execution: {pending.Request.ToolName}");
                pending.CompletionSource.TrySetResult(false);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Log a tool execution
        /// </summary>
        public async Task LogExecutionAsync(
            string toolId,
            string toolName,
            Dictionary<string, object> parameters,
            ToolResult result,
            string? userId = null,
            bool wasApproved = false)
        {
            var executionEvent = new ToolExecutionEvent
            {
                ToolId = toolId,
                ToolName = toolName,
                Parameters = parameters,
                Success = result.Success,
                ErrorMessage = result.Error,
                Timestamp = DateTime.UtcNow,
                UserId = userId,
                RequiredApproval = RequiresApproval(toolId, parameters),
                WasApproved = wasApproved
            };

            ToolExecuted?.Invoke(this, executionEvent);

            await LogToFileAsync(executionEvent);
        }

        /// <summary>
        /// Get all pending approval requests
        /// </summary>
        public List<ToolApprovalRequest> GetPendingApprovals()
        {
            return _pendingApprovals.Values.Select(p => p.Request).ToList();
        }

        /// <summary>
        /// Read audit log entries for a specific time range
        /// </summary>
        public async Task<List<ToolExecutionEvent>> ReadAuditLogAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            if (!File.Exists(_auditLogPath))
            {
                return new List<ToolExecutionEvent>();
            }

            try
            {
                var json = await File.ReadAllTextAsync(_auditLogPath);
                var events = JsonSerializer.Deserialize<List<ToolExecutionEvent>>(json) ?? new();

                // Filter by date range if specified
                if (startDate.HasValue)
                {
                    events = events.Where(e => e.Timestamp >= startDate.Value).ToList();
                }

                if (endDate.HasValue)
                {
                    events = events.Where(e => e.Timestamp <= endDate.Value).ToList();
                }

                return events;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading audit log: {ex.Message}");
                return new List<ToolExecutionEvent>();
            }
        }

        private bool RequiresApproval(string toolId, Dictionary<string, object> parameters)
        {
            // Check if tool is marked as requiring approval
            var permission = _permissionManager.GetToolPermission(toolId);

            if (permission == ToolPermissionLevel.AlwaysRequireApproval)
            {
                return true;
            }

            if (permission == ToolPermissionLevel.NeverRequireApproval)
            {
                return false;
            }

            // Check for dangerous operations in parameters
            return ContainsDangerousParameters(parameters);
        }

        private bool ContainsDangerousParameters(Dictionary<string, object> parameters)
        {
            // Check for potentially dangerous parameter values
            var dangerousKeywords = new[] { "delete", "remove", "drop", "truncate", "destroy", "format" };

            foreach (var param in parameters.Values)
            {
                var stringValue = param?.ToString()?.ToLower();
                if (stringValue != null && dangerousKeywords.Any(k => stringValue.Contains(k)))
                {
                    return true;
                }
            }

            return false;
        }

        private async Task LogToFileAsync(ToolExecutionEvent executionEvent)
        {
            await _logSemaphore.WaitAsync();

            try
            {
                List<ToolExecutionEvent> events;

                if (File.Exists(_auditLogPath))
                {
                    var existingJson = await File.ReadAllTextAsync(_auditLogPath);
                    events = JsonSerializer.Deserialize<List<ToolExecutionEvent>>(existingJson) ?? new();
                }
                else
                {
                    events = new List<ToolExecutionEvent>();
                }

                events.Add(executionEvent);

                // Keep only last 1000 entries per file
                if (events.Count > 1000)
                {
                    events = events.TakeLast(1000).ToList();
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                var json = JsonSerializer.Serialize(events, options);
                await File.WriteAllTextAsync(_auditLogPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error writing to audit log: {ex.Message}");
            }
            finally
            {
                _logSemaphore.Release();
            }
        }

        private class PendingApproval
        {
            public required ToolApprovalRequest Request { get; set; }
            public required TaskCompletionSource<bool> CompletionSource { get; set; }
        }
    }

    /// <summary>
    /// Request for tool execution approval
    /// </summary>
    public class ToolApprovalRequest
    {
        public string RequestId { get; set; } = string.Empty;
        public string ToolId { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public Dictionary<string, object> Parameters { get; set; } = new();
        public string Description { get; set; } = string.Empty;
        public string? ContextInfo { get; set; }
        public DateTime RequestedAt { get; set; }
    }

    /// <summary>
    /// Tool execution event for audit logging
    /// </summary>
    public class ToolExecutionEvent
    {
        public string ToolId { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public Dictionary<string, object> Parameters { get; set; } = new();
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }
        public string? UserId { get; set; }
        public bool RequiredApproval { get; set; }
        public bool WasApproved { get; set; }
    }
}
