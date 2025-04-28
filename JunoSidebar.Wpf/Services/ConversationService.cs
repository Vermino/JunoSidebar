// File: JunoSidebar/JunoSidebar.Wpf/Services/ConversationService.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JunoSidebar.Wpf.Models;
using JunoSidebar.Wpf.Services.LLM;
using JunoSidebar.Wpf.Services.Tools;
using JunoSidebar.Wpf.Services.Voice;

namespace JunoSidebar.Wpf.Services
{
    /// <summary>
    /// Service for managing conversations with the AI assistant.
    /// </summary>
    public class ConversationService
    {
        private readonly ILLMClient _llmClient;
        private readonly ContextManager _contextManager;
        private readonly PersonalityManager _personalityManager;
        private readonly ToolRegistry _toolRegistry;
        private readonly VoiceService _voiceService;
        
        private CancellationTokenSource? _currentConversationCts;
        private bool _isProcessing = false;
        
        /// <summary>
        /// Event raised when the assistant state changes.
        /// </summary>
        public event EventHandler<AssistantStateChangedEventArgs>? AssistantStateChanged;
        
        /// <summary>
        /// Event raised when the user query is updated.
        /// </summary>
        public event EventHandler<string>? QueryUpdated;
        
        /// <summary>
        /// Event raised when the assistant response is updated.
        /// </summary>
        public event EventHandler<ResponseUpdateEventArgs>? ResponseUpdated;
        
        /// <summary>
        /// Event raised when a tool is executed.
        /// </summary>
        public event EventHandler<ToolExecutedEventArgs>? ToolExecuted;
        
        /// <summary>
        /// Gets the current state of the assistant.
        /// </summary>
        public AssistantState CurrentState { get; private set; } = AssistantState.Idle;

        /// <summary>
        /// Initializes a new instance of the ConversationService class.
        /// </summary>
        /// <param name="llmClient">The LLM client to use for generating responses.</param>
        /// <param name="contextManager">The context manager for maintaining conversation history.</param>
        /// <param name="personalityManager">The personality manager for personality-specific behavior.</param>
        /// <param name="toolRegistry">The tool registry for tool discovery and execution.</param>
        /// <param name="voiceService">The voice service for speech input and output.</param>
        public ConversationService(
            ILLMClient llmClient,
            ContextManager contextManager,
            PersonalityManager personalityManager,
            ToolRegistry toolRegistry,
            VoiceService voiceService)
        {
            _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
            _contextManager = contextManager ?? throw new ArgumentNullException(nameof(contextManager));
            _personalityManager = personalityManager ?? throw new ArgumentNullException(nameof(personalityManager));
            _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
            _voiceService = voiceService ?? throw new ArgumentNullException(nameof(voiceService));
            
            // Set up event handlers
            _voiceService.WakeWordDetected += OnWakeWordDetected;
            _voiceService.SpeechRecognized += OnSpeechRecognized;
        }

        /// <summary>
        /// Processes a user query and generates a response.
        /// </summary>
        /// <param name="query">The user's query text.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task ProcessQueryAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return;
            
            // Cancel any ongoing conversation
            CancelCurrentConversation();
            
            // Create a new cancellation token source
            _currentConversationCts = new CancellationTokenSource();
            var cancellationToken = _currentConversationCts.Token;
            
            try
            {
                // Update the UI
                QueryUpdated?.Invoke(this, query);
                SetAssistantState(AssistantState.Processing);
                _isProcessing = true;
                
                // Add the user message to the context
                _contextManager.AddUserMessage(query);
                
                // Get the current personality
                var personality = _personalityManager.CurrentPersonality;
                if (personality == null)
                {
                    throw new InvalidOperationException("No active personality selected.");
                }
                
                // Prepare messages for the LLM
                var messages = _contextManager.GetMessagesForLLM();
                
                // Ensure the system message is set correctly for the current personality
                if (messages.Count == 0 || messages[0].Role != "system")
                {
                    messages.Insert(0, new LLMMessage
                    {
                        Role = "system",
                        Content = personality.SystemPrompt
                    });
                }
                else if (messages[0].Role == "system")
                {
                    messages[0].Content = personality.SystemPrompt;
                }
                
                // Start the streaming response
                SetAssistantState(AssistantState.Responding);
                
                // Buffer for collecting the full response
                var responseBuffer = new System.Text.StringBuilder();
                
                // Process the streaming response
                await foreach (var chunk in _llmClient.GetStreamingChatCompletionAsync(
                    messages,
                    model: "local-model", // This should come from settings
                    temperature: 0.7f, // This should come from settings
                    cancellationToken: cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                    
                    // Append the chunk to the buffer
                    responseBuffer.Append(chunk.Content);
                    
                    // Send the partial response to the UI
                    ResponseUpdated?.Invoke(this, new ResponseUpdateEventArgs
                    {
                        Response = responseBuffer.ToString(),
                        IsComplete = chunk.IsFinal
                    });
                    
                    // Check if this is the final chunk
                    if (chunk.IsFinal)
                    {
                        // Add the assistant message to the context
                        _contextManager.AddAssistantMessage(responseBuffer.ToString());
                        
                        // Speak the response if voice output is enabled
                        if (_voiceService.OutputEnabled)
                        {
                            await _voiceService.SpeakAsync(responseBuffer.ToString());
                        }
                        
                        // Process any tool calls in the response
                        await ProcessToolCallsAsync(responseBuffer.ToString(), cancellationToken);
                        
                        // Set the state back to idle
                        SetAssistantState(AssistantState.Idle);
                        
                        // Save the conversation history
                        await _contextManager.SaveConversationAsync();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Operation was cancelled, set state back to idle
                SetAssistantState(AssistantState.Idle);
            }
            catch (Exception ex)
            {
                // Handle errors
                Console.WriteLine($"Error processing query: {ex.Message}");
                ResponseUpdated?.Invoke(this, new ResponseUpdateEventArgs
                {
                    Response = $"I'm sorry, but an error occurred: {ex.Message}",
                    IsComplete = true
                });
                SetAssistantState(AssistantState.Idle);
            }
            finally
            {
                _isProcessing = false;
                _currentConversationCts?.Dispose();
                _currentConversationCts = null;
            }
        }

        /// <summary>
        /// Cancels the current conversation.
        /// </summary>
        public void CancelCurrentConversation()
        {
            if (_isProcessing && _currentConversationCts != null)
            {
                _currentConversationCts.Cancel();
            }
            
            // Stop any ongoing speech
            _voiceService.StopSpeaking();
        }

        /// <summary>
        /// Starts listening for user input.
        /// </summary>
        public void StartListening()
        {
            if (CurrentState == AssistantState.Idle)
            {
                SetAssistantState(AssistantState.Listening);
                _voiceService.StartListening();
            }
        }

        /// <summary>
        /// Stops listening for user input.
        /// </summary>
        public void StopListening()
        {
            if (CurrentState == AssistantState.Listening)
            {
                SetAssistantState(AssistantState.Idle);
                _voiceService.StopListening();
            }
        }

        /// <summary>
        /// Clears the current conversation history.
        /// </summary>
        public void ClearConversation()
        {
            _contextManager.ClearHistory();
        }

        /// <summary>
        /// Executes a tool by its ID with the given parameters.
        /// </summary>
        /// <param name="toolId">The ID of the tool to execute.</param>
        /// <param name="parameters">The parameters to pass to the tool.</param>
        /// <returns>The result of the tool execution.</returns>
        public async Task<ToolResult> ExecuteToolAsync(string toolId, Dictionary<string, object> parameters)
        {
            try
            {
                var result = await _toolRegistry.ExecuteToolAsync(toolId, parameters);
                
                ToolExecuted?.Invoke(this, new ToolExecutedEventArgs
                {
                    ToolId = toolId,
                    Result = result
                });
                
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing tool {toolId}: {ex.Message}");
                return ToolResult.CreateError($"Error executing tool: {ex.Message}");
            }
        }

        // Private helper methods

        private void SetAssistantState(AssistantState newState)
        {
            if (CurrentState == newState)
                return;
            
            CurrentState = newState;
            AssistantStateChanged?.Invoke(this, new AssistantStateChangedEventArgs(newState));
        }

        private async Task ProcessToolCallsAsync(string response, CancellationToken cancellationToken)
        {
            // This is a placeholder for tool call processing
            // In a more advanced implementation, this would:
            // 1. Parse the response for tool calls
            // 2. Execute the requested tools
            // 3. Add the results back to the context
            // 4. Generate a follow-up response if needed
            
            // For now, we'll just look for simple patterns like "I'll search for that" or "Let me find that for you"
            if ((response.Contains("I'll search for that", StringComparison.OrdinalIgnoreCase) ||
                 response.Contains("Let me find that for you", StringComparison.OrdinalIgnoreCase) ||
                 response.Contains("I'll look that up", StringComparison.OrdinalIgnoreCase)) &&
                _toolRegistry.GetTool("web_search") != null)
            {
                // This is a very simple heuristic approach
                // A more robust implementation would use a proper tool calling framework
                
                await Task.Delay(1000, cancellationToken); // Simulate some processing time
                
                var searchResult = await _toolRegistry.ExecuteToolAsync("web_search", new Dictionary<string, object>
                {
                    ["query"] = "Juno AI Assistant"
                });
                
                ToolExecuted?.Invoke(this, new ToolExecutedEventArgs
                {
                    ToolId = "web_search",
                    Result = searchResult
                });
            }
        }

        // Event handlers

        private void OnWakeWordDetected(object? sender, EventArgs e)
        {
            SetAssistantState(AssistantState.Listening);
        }

        private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
        {
            if (CurrentState == AssistantState.Listening)
            {
                // Only process speech if we're in listening mode
                Task.Run(() => ProcessQueryAsync(e.Text)).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Represents the state of the assistant.
    /// </summary>
    public enum AssistantState
    {
        /// <summary>
        /// The assistant is idle, waiting for the wake word or direct input.
        /// </summary>
        Idle,
        
        /// <summary>
        /// The assistant is actively listening for user input.
        /// </summary>
        Listening,
        
        /// <summary>
        /// The assistant is processing the user input.
        /// </summary>
        Processing,
        
        /// <summary>
        /// The assistant is responding to the user.
        /// </summary>
        Responding
    }

    /// <summary>
    /// Event args for assistant state changes.
    /// </summary>
    public class AssistantStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the new state of the assistant.
        /// </summary>
        public AssistantState State { get; }
        
        /// <summary>
        /// Initializes a new instance of the AssistantStateChangedEventArgs class.
        /// </summary>
        /// <param name="state">The new state of the assistant.</param>
        public AssistantStateChangedEventArgs(AssistantState state)
        {
            State = state;
        }
    }

    /// <summary>
    /// Event args for response updates.
    /// </summary>
    public class ResponseUpdateEventArgs : EventArgs
    {
        /// <summary>
        /// Gets or sets the response text.
        /// </summary>
        public string Response { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets a value indicating whether this is the complete response.
        /// </summary>
        public bool IsComplete { get; set; }
    }

    /// <summary>
    /// Event args for tool execution.
    /// </summary>
    public class ToolExecutedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets or sets the ID of the tool that was executed.
        /// </summary>
        public string ToolId { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the result of the tool execution.
        /// </summary>
        public ToolResult Result { get; set; } = new ToolResult();
    }
}