// File: JunoSidebar.Wpf/Services/Agents/IAgent.cs

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JunoSidebar.Wpf.Services.LLM;
using JunoSidebar.Wpf.Services.Tools;

namespace JunoSidebar.Wpf.Services.Agents
{
    /// <summary>
    /// Base interface for all autonomous agents
    /// </summary>
    public interface IAgent
    {
        /// <summary>
        /// Unique identifier for this agent
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Human-readable name
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Description of what this agent does
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Agent capabilities/tags
        /// </summary>
        List<string> Capabilities { get; }

        /// <summary>
        /// Current state of the agent
        /// </summary>
        AgentState State { get; }

        /// <summary>
        /// Tools this agent has access to
        /// </summary>
        List<string> AvailableTools { get; }

        /// <summary>
        /// Initialize the agent with configuration
        /// </summary>
        Task InitializeAsync(AgentConfiguration configuration, CancellationToken cancellationToken = default);

        /// <summary>
        /// Execute a task
        /// </summary>
        Task<AgentResult> ExecuteAsync(AgentTask task, IProgress<AgentProgress>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Check if this agent can handle a given task
        /// </summary>
        Task<bool> CanHandleAsync(string taskDescription, CancellationToken cancellationToken = default);

        /// <summary>
        /// Pause current execution
        /// </summary>
        Task PauseAsync();

        /// <summary>
        /// Resume paused execution
        /// </summary>
        Task ResumeAsync();

        /// <summary>
        /// Cancel current execution
        /// </summary>
        Task CancelAsync();
    }

    /// <summary>
    /// Configuration for an agent
    /// </summary>
    public class AgentConfiguration
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Capabilities { get; set; } = new();
        public string LlmProvider { get; set; } = string.Empty;
        public string LlmModel { get; set; } = string.Empty;
        public string SystemPrompt { get; set; } = string.Empty;
        public List<string> Tools { get; set; } = new();
        public AgentSettings Settings { get; set; } = new();
    }

    /// <summary>
    /// Agent-specific settings
    /// </summary>
    public class AgentSettings
    {
        public int MaxIterations { get; set; } = 10;
        public int TimeoutSeconds { get; set; } = 300;
        public bool AutoApproveTools { get; set; } = false;
        public float Temperature { get; set; } = 0.7f;
        public int? MaxTokens { get; set; }
        public Dictionary<string, object>? CustomSettings { get; set; }
    }

    /// <summary>
    /// Task to be executed by an agent
    /// </summary>
    public class AgentTask
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Description { get; set; } = string.Empty;
        public Dictionary<string, object> Parameters { get; set; } = new();
        public List<LLMMessage> ConversationHistory { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int Priority { get; set; } = 0;
    }

    /// <summary>
    /// Result of agent execution
    /// </summary>
    public class AgentResult
    {
        public string TaskId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? Result { get; set; }
        public string? ErrorMessage { get; set; }
        public List<AgentAction> ActionsPerformed { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
        public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
        public long DurationMs { get; set; }
    }

    /// <summary>
    /// Action performed by agent during execution
    /// </summary>
    public class AgentAction
    {
        public string Type { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? ToolName { get; set; }
        public Dictionary<string, object>? ToolParameters { get; set; }
        public string? ToolResult { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public bool RequiredApproval { get; set; }
        public bool Approved { get; set; }
    }

    /// <summary>
    /// Progress update from agent
    /// </summary>
    public class AgentProgress
    {
        public string TaskId { get; set; } = string.Empty;
        public AgentState State { get; set; }
        public int CurrentIteration { get; set; }
        public int MaxIterations { get; set; }
        public string? CurrentStep { get; set; }
        public string? Message { get; set; }
        public float ProgressPercent { get; set; }
    }

    /// <summary>
    /// State of an agent
    /// </summary>
    public enum AgentState
    {
        Idle,
        Initializing,
        Planning,
        Executing,
        WaitingForApproval,
        Paused,
        Completed,
        Failed,
        Cancelled
    }
}
