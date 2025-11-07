// File: JunoSidebar.Wpf/Services/Agents/AgentExecutor.cs

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JunoSidebar.Wpf.Services.Agents
{
    /// <summary>
    /// Orchestrates agent execution and manages task queue
    /// </summary>
    public class AgentExecutor
    {
        private readonly AgentRegistry _agentRegistry;
        private readonly ConcurrentDictionary<string, AgentExecution> _runningExecutions;
        private readonly TaskQueue _taskQueue;
        private readonly SemaphoreSlim _executionSemaphore;
        private readonly int _maxConcurrentAgents;

        public event EventHandler<AgentProgress>? ProgressUpdated;
        public event EventHandler<AgentResult>? ExecutionCompleted;
        public event EventHandler<ToolApprovalRequest>? ToolApprovalRequested;

        public AgentExecutor(AgentRegistry agentRegistry, int maxConcurrentAgents = 3)
        {
            _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
            _runningExecutions = new ConcurrentDictionary<string, AgentExecution>();
            _taskQueue = new TaskQueue();
            _maxConcurrentAgents = maxConcurrentAgents;
            _executionSemaphore = new SemaphoreSlim(maxConcurrentAgents, maxConcurrentAgents);

            Debug.WriteLine($"AgentExecutor initialized with max {maxConcurrentAgents} concurrent agents");
        }

        /// <summary>
        /// Execute a task with the most appropriate agent
        /// </summary>
        public async Task<AgentResult> ExecuteTaskAsync(
            AgentTask task,
            string? preferredAgentId = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Select agent
                IAgent? agent = null;

                if (!string.IsNullOrEmpty(preferredAgentId))
                {
                    agent = _agentRegistry.GetAgent(preferredAgentId);
                }

                if (agent == null)
                {
                    agent = await SelectBestAgentAsync(task, cancellationToken);
                }

                if (agent == null)
                {
                    return new AgentResult
                    {
                        TaskId = task.Id,
                        Success = false,
                        ErrorMessage = "No suitable agent found for this task"
                    };
                }

                Debug.WriteLine($"Selected agent '{agent.Name}' for task: {task.Description}");

                // Wait for available execution slot
                await _executionSemaphore.WaitAsync(cancellationToken);

                try
                {
                    // Track execution
                    var execution = new AgentExecution
                    {
                        TaskId = task.Id,
                        AgentId = agent.Id,
                        StartTime = DateTime.UtcNow
                    };
                    _runningExecutions.TryAdd(task.Id, execution);

                    // Execute with progress reporting
                    var progress = new Progress<AgentProgress>(p =>
                    {
                        ProgressUpdated?.Invoke(this, p);
                    });

                    var result = await agent.ExecuteAsync(task, progress, cancellationToken);

                    // Update execution record
                    execution.EndTime = DateTime.UtcNow;
                    execution.Result = result;

                    ExecutionCompleted?.Invoke(this, result);

                    return result;
                }
                finally
                {
                    _executionSemaphore.Release();
                    _runningExecutions.TryRemove(task.Id, out _);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error executing task: {ex.Message}");
                return new AgentResult
                {
                    TaskId = task.Id,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Queue a task for execution
        /// </summary>
        public async Task<string> QueueTaskAsync(AgentTask task, string? preferredAgentId = null)
        {
            _taskQueue.Enqueue(task);

            Debug.WriteLine($"Task queued: {task.Description}");

            // Start processing queue in background
            _ = Task.Run(async () =>
            {
                await ProcessQueueAsync(preferredAgentId, CancellationToken.None);
            });

            return task.Id;
        }

        /// <summary>
        /// Get status of all running executions
        /// </summary>
        public List<AgentExecutionStatus> GetRunningExecutions()
        {
            return _runningExecutions.Values.Select(e => new AgentExecutionStatus
            {
                TaskId = e.TaskId,
                AgentId = e.AgentId,
                StartTime = e.StartTime,
                State = _agentRegistry.GetAgent(e.AgentId)?.State ?? AgentState.Idle
            }).ToList();
        }

        /// <summary>
        /// Cancel a running execution
        /// </summary>
        public async Task<bool> CancelExecutionAsync(string taskId)
        {
            if (_runningExecutions.TryGetValue(taskId, out var execution))
            {
                var agent = _agentRegistry.GetAgent(execution.AgentId);
                if (agent != null)
                {
                    await agent.CancelAsync();
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Pause a running execution
        /// </summary>
        public async Task<bool> PauseExecutionAsync(string taskId)
        {
            if (_runningExecutions.TryGetValue(taskId, out var execution))
            {
                var agent = _agentRegistry.GetAgent(execution.AgentId);
                if (agent != null)
                {
                    await agent.PauseAsync();
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Resume a paused execution
        /// </summary>
        public async Task<bool> ResumeExecutionAsync(string taskId)
        {
            if (_runningExecutions.TryGetValue(taskId, out var execution))
            {
                var agent = _agentRegistry.GetAgent(execution.AgentId);
                if (agent != null)
                {
                    await agent.ResumeAsync();
                    return true;
                }
            }

            return false;
        }

        private async Task<IAgent?> SelectBestAgentAsync(AgentTask task, CancellationToken cancellationToken)
        {
            var agents = _agentRegistry.GetAllAgents();

            // Score each agent based on their capabilities
            var scored = new List<(IAgent agent, int score)>();

            foreach (var agent in agents)
            {
                if (await agent.CanHandleAsync(task.Description, cancellationToken))
                {
                    // Simple scoring based on capability match
                    int score = CalculateAgentScore(agent, task);
                    scored.Add((agent, score));
                }
            }

            // Return highest scoring agent
            return scored.OrderByDescending(x => x.score).FirstOrDefault().agent;
        }

        private int CalculateAgentScore(IAgent agent, AgentTask task)
        {
            int score = 0;

            // Check for capability keywords in task description
            var taskLower = task.Description.ToLower();

            foreach (var capability in agent.Capabilities)
            {
                if (taskLower.Contains(capability.ToLower()))
                {
                    score += 10;
                }
            }

            // Prefer agents with fewer capabilities (more specialized)
            score += (10 - Math.Min(agent.Capabilities.Count, 10));

            return score;
        }

        private async Task ProcessQueueAsync(string? preferredAgentId, CancellationToken cancellationToken)
        {
            while (_taskQueue.TryDequeue(out var task))
            {
                await ExecuteTaskAsync(task, preferredAgentId, cancellationToken);
            }
        }

        private class AgentExecution
        {
            public string TaskId { get; set; } = string.Empty;
            public string AgentId { get; set; } = string.Empty;
            public DateTime StartTime { get; set; }
            public DateTime? EndTime { get; set; }
            public AgentResult? Result { get; set; }
        }
    }

    /// <summary>
    /// Status of a running agent execution
    /// </summary>
    public class AgentExecutionStatus
    {
        public string TaskId { get; set; } = string.Empty;
        public string AgentId { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public AgentState State { get; set; }
    }

    /// <summary>
    /// Request for tool approval from an agent
    /// </summary>
    public class ToolApprovalRequest
    {
        public string TaskId { get; set; } = string.Empty;
        public string AgentId { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public Dictionary<string, object> Parameters { get; set; } = new();
        public string Description { get; set; } = string.Empty;
        public TaskCompletionSource<bool> ApprovalSource { get; set; } = new();
    }

    /// <summary>
    /// Simple task queue for agent tasks
    /// </summary>
    public class TaskQueue
    {
        private readonly ConcurrentQueue<AgentTask> _queue;

        public int Count => _queue.Count;

        public TaskQueue()
        {
            _queue = new ConcurrentQueue<AgentTask>();
        }

        public void Enqueue(AgentTask task)
        {
            _queue.Enqueue(task);
        }

        public bool TryDequeue(out AgentTask? task)
        {
            return _queue.TryDequeue(out task);
        }

        public List<AgentTask> GetAll()
        {
            return _queue.ToList();
        }

        public void Clear()
        {
            while (_queue.TryDequeue(out _)) { }
        }
    }
}
