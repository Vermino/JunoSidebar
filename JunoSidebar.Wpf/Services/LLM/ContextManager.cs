// File: JunoSidebar/JunoSidebar.Wpf/Services/LLM/ContextManager.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace JunoSidebar.Wpf.Services.LLM
{
    /// <summary>
    /// Manages conversation context for LLM interactions, including history management,
    /// context windowing, and persistence.
    /// </summary>
    public class ContextManager
    {
        private readonly List<LLMMessage> _messages = new();
        private readonly int _maxContextTokens;
        private readonly int _reservedTokens;
        private readonly string _persistencePath;
        private readonly JsonSerializerOptions _jsonOptions;
        
        // Simple token estimation (can be replaced with a more accurate model)
        private const int EstimatedTokensPerChar = 4;
        
        /// <summary>
        /// Event raised when the message history is updated.
        /// </summary>
        public event EventHandler<MessageHistoryChangedEventArgs>? HistoryChanged;
        
        /// <summary>
        /// Gets the current total estimated token count of the conversation.
        /// </summary>
        public int EstimatedTokenCount => _messages.Sum(m => 
            (m.Content.Length + (m.Role?.Length ?? 0) + (m.Name?.Length ?? 0)) / EstimatedTokensPerChar);
        
        /// <summary>
        /// Gets the current message count in the conversation.
        /// </summary>
        public int MessageCount => _messages.Count;
        
        /// <summary>
        /// Gets a read-only view of the current messages.
        /// </summary>
        public IReadOnlyList<LLMMessage> Messages => _messages.AsReadOnly();
        
        /// <summary>
        /// Gets or sets the conversation identifier.
        /// </summary>
        public string ConversationId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Initializes a new instance of the ContextManager class.
        /// </summary>
        /// <param name="maxContextTokens">Maximum number of tokens to maintain in context.</param>
        /// <param name="reservedTokens">Number of tokens to reserve for the next response.</param>
        /// <param name="persistencePath">Directory path for persisting conversations.</param>
        public ContextManager(
            int maxContextTokens = 8192,
            int reservedTokens = 1024,
            string? persistencePath = null)
        {
            _maxContextTokens = maxContextTokens;
            _reservedTokens = reservedTokens;
            _persistencePath = persistencePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "JunoSidebar",
                "Conversations");
            
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            
            // Ensure the persistence directory exists
            if (!Directory.Exists(_persistencePath))
            {
                Directory.CreateDirectory(_persistencePath);
            }
        }
        
        /// <summary>
        /// Adds a system message to the conversation.
        /// </summary>
        /// <param name="content">The message content.</param>
        /// <returns>The added message.</returns>
        public LLMMessage AddSystemMessage(string content)
        {
            var message = new LLMMessage
            {
                Role = "system",
                Content = content
            };
            
            _messages.Add(message);
            OnHistoryChanged(new MessageHistoryChangedEventArgs(message, MessageChangeType.Added));
            return message;
        }
        
        /// <summary>
        /// Adds a user message to the conversation.
        /// </summary>
        /// <param name="content">The message content.</param>
        /// <param name="name">Optional name of the user.</param>
        /// <returns>The added message.</returns>
        public LLMMessage AddUserMessage(string content, string? name = null)
        {
            var message = new LLMMessage
            {
                Role = "user",
                Content = content,
                Name = name
            };
            
            _messages.Add(message);
            TrimContextIfNeeded();
            OnHistoryChanged(new MessageHistoryChangedEventArgs(message, MessageChangeType.Added));
            return message;
        }
        
        /// <summary>
        /// Adds an assistant message to the conversation.
        /// </summary>
        /// <param name="content">The message content.</param>
        /// <returns>The added message.</returns>
        public LLMMessage AddAssistantMessage(string content)
        {
            var message = new LLMMessage
            {
                Role = "assistant",
                Content = content
            };
            
            _messages.Add(message);
            TrimContextIfNeeded();
            OnHistoryChanged(new MessageHistoryChangedEventArgs(message, MessageChangeType.Added));
            return message;
        }
        
        /// <summary>
        /// Adds a tool message to the conversation.
        /// </summary>
        /// <param name="content">The message content.</param>
        /// <param name="toolName">The name of the tool.</param>
        /// <returns>The added message.</returns>
        public LLMMessage AddToolMessage(string content, string toolName)
        {
            var message = new LLMMessage
            {
                Role = "tool",
                Content = content,
                Name = toolName
            };
            
            _messages.Add(message);
            TrimContextIfNeeded();
            OnHistoryChanged(new MessageHistoryChangedEventArgs(message, MessageChangeType.Added));
            return message;
        }
        
        /// <summary>
        /// Gets messages formatted for LLM input with context management applied.
        /// </summary>
        /// <returns>List of messages for LLM input.</returns>
        public List<LLMMessage> GetMessagesForLLM()
        {
            // Create a copy to avoid modifying the original list
            return new List<LLMMessage>(_messages);
        }
        
        /// <summary>
        /// Clears all messages from the conversation history.
        /// </summary>
        public void ClearHistory()
        {
            _messages.Clear();
            OnHistoryChanged(new MessageHistoryChangedEventArgs(null, MessageChangeType.Cleared));
        }
        
        /// <summary>
        /// Saves the current conversation to disk.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task SaveConversationAsync()
        {
            var filePath = Path.Combine(_persistencePath, $"{ConversationId}.json");
            var conversation = new ConversationData
            {
                Id = ConversationId,
                Timestamp = DateTime.UtcNow,
                Messages = _messages
            };
            
            using FileStream fs = new FileStream(filePath, FileMode.Create);
            await JsonSerializer.SerializeAsync(fs, conversation, _jsonOptions);
        }
        
        /// <summary>
        /// Loads a conversation from disk.
        /// </summary>
        /// <param name="conversationId">The ID of the conversation to load.</param>
        /// <returns>True if the conversation was successfully loaded, false otherwise.</returns>
        public async Task<bool> LoadConversationAsync(string conversationId)
        {
            var filePath = Path.Combine(_persistencePath, $"{conversationId}.json");
            if (!File.Exists(filePath))
            {
                return false;
            }
            
            try
            {
                using FileStream fs = new FileStream(filePath, FileMode.Open);
                var conversation = await JsonSerializer.DeserializeAsync<ConversationData>(fs, _jsonOptions);
                
                if (conversation == null)
                {
                    return false;
                }
                
                _messages.Clear();
                _messages.AddRange(conversation.Messages);
                ConversationId = conversation.Id;
                
                OnHistoryChanged(new MessageHistoryChangedEventArgs(null, MessageChangeType.Loaded));
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading conversation: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Gets a list of available saved conversations.
        /// </summary>
        /// <returns>A list of conversation metadata.</returns>
        public async Task<List<ConversationMetadata>> GetSavedConversationsAsync()
        {
            var result = new List<ConversationMetadata>();
            
            try
            {
                var files = Directory.GetFiles(_persistencePath, "*.json");
                foreach (var file in files)
                {
                    try
                    {
                        using FileStream fs = new FileStream(file, FileMode.Open);
                        var conversation = await JsonSerializer.DeserializeAsync<ConversationData>(fs, _jsonOptions);
                        
                        if (conversation != null)
                        {
                            result.Add(new ConversationMetadata
                            {
                                Id = conversation.Id,
                                Timestamp = conversation.Timestamp,
                                MessageCount = conversation.Messages.Count
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error reading conversation file {file}: {ex.Message}");
                    }
                }
                
                // Sort by timestamp, newest first
                return result.OrderByDescending(c => c.Timestamp).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accessing conversation directory: {ex.Message}");
                return result;
            }
        }
        
        /// <summary>
        /// Deletes a saved conversation.
        /// </summary>
        /// <param name="conversationId">The ID of the conversation to delete.</param>
        /// <returns>True if the conversation was successfully deleted, false otherwise.</returns>
        public bool DeleteConversation(string conversationId)
        {
            var filePath = Path.Combine(_persistencePath, $"{conversationId}.json");
            if (!File.Exists(filePath))
            {
                return false;
            }
            
            try
            {
                File.Delete(filePath);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting conversation: {ex.Message}");
                return false;
            }
        }
        
        // Private helper methods
        
        private void TrimContextIfNeeded()
        {
            // This is a simple token counting approach that can be improved
            // with more accurate token counting for specific LLM models
            int availableTokens = _maxContextTokens - _reservedTokens;
            if (EstimatedTokenCount <= availableTokens)
            {
                return;
            }
            
            // Keep system messages
            var systemMessages = _messages.Where(m => m.Role == "system").ToList();
            var nonSystemMessages = _messages.Where(m => m.Role != "system").ToList();
            
            while (nonSystemMessages.Count > 0 && 
                   EstimatedTokenCount > availableTokens)
            {
                // Remove oldest non-system message
                var oldestMessage = nonSystemMessages[0];
                nonSystemMessages.RemoveAt(0);
                _messages.Remove(oldestMessage);
                
                OnHistoryChanged(new MessageHistoryChangedEventArgs(oldestMessage, MessageChangeType.Removed));
            }
            
            // Ensure system messages are preserved
            _messages.Clear();
            _messages.AddRange(systemMessages);
            _messages.AddRange(nonSystemMessages);
        }
        
        private void OnHistoryChanged(MessageHistoryChangedEventArgs args)
        {
            HistoryChanged?.Invoke(this, args);
        }
        
        // Nested types for data structures
        
        /// <summary>
        /// Represents the serialized form of a conversation.
        /// </summary>
        private class ConversationData
        {
            public string Id { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; }
            public List<LLMMessage> Messages { get; set; } = new List<LLMMessage>();
        }
    }
    
    /// <summary>
    /// Metadata about a saved conversation.
    /// </summary>
    public class ConversationMetadata
    {
        /// <summary>
        /// Gets or sets the conversation ID.
        /// </summary>
        public string Id { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets when the conversation was saved.
        /// </summary>
        public DateTime Timestamp { get; set; }
        
        /// <summary>
        /// Gets or sets the number of messages in the conversation.
        /// </summary>
        public int MessageCount { get; set; }
    }
    
    /// <summary>
    /// Enum representing types of changes to the message history.
    /// </summary>
    public enum MessageChangeType
    {
        /// <summary>
        /// A message was added to the history.
        /// </summary>
        Added,
        
        /// <summary>
        /// A message was removed from the history.
        /// </summary>
        Removed,
        
        /// <summary>
        /// The history was cleared.
        /// </summary>
        Cleared,
        
        /// <summary>
        /// A conversation was loaded.
        /// </summary>
        Loaded
    }
    
    /// <summary>
    /// Event args for message history changes.
    /// </summary>
    public class MessageHistoryChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the message that was changed.
        /// </summary>
        public LLMMessage? Message { get; }
        
        /// <summary>
        /// Gets the type of change that occurred.
        /// </summary>
        public MessageChangeType ChangeType { get; }
        
        /// <summary>
        /// Initializes a new instance of the MessageHistoryChangedEventArgs class.
        /// </summary>
        /// <param name="message">The message that was changed.</param>
        /// <param name="changeType">The type of change that occurred.</param>
        public MessageHistoryChangedEventArgs(LLMMessage? message, MessageChangeType changeType)
        {
            Message = message;
            ChangeType = changeType;
        }
    }
}