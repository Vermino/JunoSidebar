// File: JunoSidebar.Wpf/Services/Agents/GeneralAgent.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JunoSidebar.Wpf.Services.LLM;
using JunoSidebar.Wpf.Services.LLM.Providers;

namespace JunoSidebar.Wpf.Services.Agents
{
    /// <summary>
    /// General-purpose agent for handling various tasks
    /// </summary>
    public class GeneralAgent : IAgent
    {
        private AgentConfiguration? _configuration;
        private ILLMProvider? _llmProvider;
        private LLMProviderFactory? _providerFactory;
        private AgentState _state = AgentState.Idle;
        private CancellationTokenSource? _executionCts;

        public string Id => _configuration?.Id ?? "general-agent";
        public string Name => _configuration?.Name ?? "General Agent";
        public string Description => _configuration?.Description ?? "General-purpose assistant";
        public List<string> Capabilities => _configuration?.Capabilities ?? new();
        public AgentState State => _state;
        public List<string> AvailableTools => _configuration?.Tools ?? new();

        public async Task InitializeAsync(AgentConfiguration configuration, CancellationToken cancellationToken = default)
        {
            _state = AgentState.Initializing;
            _configuration = configuration;

            try
            {
                // Initialize LLM provider
                _providerFactory = new LLMProviderFactory();
                _llmProvider = await _providerFactory.GetProviderAsync(configuration.LlmProvider, cancellationToken);

                if (_llmProvider == null)
                {
                    throw new Exception($"Failed to initialize LLM provider: {configuration.LlmProvider}");
                }

                _state = AgentState.Idle;
                Debug.WriteLine($"GeneralAgent '{Name}' initialized successfully");
            }
            catch (Exception ex)
            {
                _state = AgentState.Failed;
                Debug.WriteLine($"Error initializing GeneralAgent: {ex.Message}");
                throw;
            }
        }

        public async Task<AgentResult> ExecuteAsync(
            AgentTask task,
            IProgress<AgentProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            var actions = new List<AgentAction>();

            try
            {
                _state = AgentState.Planning;
                _executionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                ReportProgress(progress, task.Id, "Analyzing task...", 0);

                // Build conversation with system prompt and task
                var messages = new List<LLMMessage>();

                if (!string.IsNullOrEmpty(_configuration?.SystemPrompt))
                {
                    messages.Add(new LLMMessage
                    {
                        Role = "system",
                        Content = _configuration.SystemPrompt
                    });
                }

                // Add conversation history if provided
                messages.AddRange(task.ConversationHistory);

                // Add the task as a user message
                messages.Add(new LLMMessage
                {
                    Role = "user",
                    Content = task.Description
                });

                _state = AgentState.Executing;
                ReportProgress(progress, task.Id, "Executing task...", 25);

                // Execute with LLM
                var result = await ExecuteWithLLMAsync(messages, task, actions, progress, _executionCts.Token);

                stopwatch.Stop();

                _state = AgentState.Completed;
                ReportProgress(progress, task.Id, "Task completed", 100);

                return new AgentResult
                {
                    TaskId = task.Id,
                    Success = true,
                    Result = result,
                    ActionsPerformed = actions,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    CompletedAt = DateTime.UtcNow
                };
            }
            catch (OperationCanceledException)
            {
                _state = AgentState.Cancelled;
                return new AgentResult
                {
                    TaskId = task.Id,
                    Success = false,
                    ErrorMessage = "Task was cancelled",
                    ActionsPerformed = actions,
                    DurationMs = stopwatch.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                _state = AgentState.Failed;
                Debug.WriteLine($"Error executing task: {ex.Message}");

                return new AgentResult
                {
                    TaskId = task.Id,
                    Success = false,
                    ErrorMessage = ex.Message,
                    ActionsPerformed = actions,
                    DurationMs = stopwatch.ElapsedMilliseconds
                };
            }
            finally
            {
                _executionCts?.Dispose();
                _executionCts = null;
            }
        }

        public Task<bool> CanHandleAsync(string taskDescription, CancellationToken cancellationToken = default)
        {
            // General agent can handle most tasks
            return Task.FromResult(true);
        }

        public Task PauseAsync()
        {
            if (_state == AgentState.Executing)
            {
                _state = AgentState.Paused;
                Debug.WriteLine($"Agent '{Name}' paused");
            }
            return Task.CompletedTask;
        }

        public Task ResumeAsync()
        {
            if (_state == AgentState.Paused)
            {
                _state = AgentState.Executing;
                Debug.WriteLine($"Agent '{Name}' resumed");
            }
            return Task.CompletedTask;
        }

        public Task CancelAsync()
        {
            _executionCts?.Cancel();
            _state = AgentState.Cancelled;
            Debug.WriteLine($"Agent '{Name}' cancelled");
            return Task.CompletedTask;
        }

        private async Task<string> ExecuteWithLLMAsync(
            List<LLMMessage> messages,
            AgentTask task,
            List<AgentAction> actions,
            IProgress<AgentProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (_llmProvider == null || _configuration == null)
            {
                throw new InvalidOperationException("Agent not properly initialized");
            }

            var options = new LLMRequestOptions
            {
                Temperature = _configuration.Settings.Temperature,
                MaxTokens = _configuration.Settings.MaxTokens
            };

            // For now, do a simple single-shot execution
            // TODO: Implement iterative execution with tool calls

            var action = new AgentAction
            {
                Type = "llm_query",
                Description = "Querying LLM for response",
                Timestamp = DateTime.UtcNow
            };
            actions.Add(action);

            var result = await _llmProvider.GetChatCompletionAsync(
                messages,
                _configuration.LlmModel,
                options,
                cancellationToken);

            action.ToolResult = result.Content;

            return result.Content;
        }

        private void ReportProgress(
            IProgress<AgentProgress>? progress,
            string taskId,
            string message,
            float percent)
        {
            progress?.Report(new AgentProgress
            {
                TaskId = taskId,
                State = _state,
                CurrentStep = message,
                Message = message,
                ProgressPercent = percent,
                CurrentIteration = 0,
                MaxIterations = _configuration?.Settings.MaxIterations ?? 10
            });
        }
    }
}
