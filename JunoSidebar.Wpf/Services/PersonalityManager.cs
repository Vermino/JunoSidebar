// File: JunoSidebar/JunoSidebar.Wpf/Services/PersonalityManager.cs

using JunoSidebar.Wpf.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace JunoSidebar.Wpf.Services
{
    /// <summary>
    /// Manages personalities, including loading, saving, and switching between them.
    /// </summary>
    public class PersonalityManager
    {
        private readonly string _personalitiesDirectory;
        private readonly IDeserializer _yamlDeserializer;
        private readonly ISerializer _yamlSerializer;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly Dictionary<string, Personality> _personalities;

        /// <summary>
        /// Gets the currently active personality.
        /// </summary>
        public Personality? CurrentPersonality { get; private set; }

        /// <summary>
        /// Event raised when the active personality changes.
        /// </summary>
        public event EventHandler<PersonalityChangedEventArgs>? PersonalityChanged;

        /// <summary>
        /// Event raised when the collection of available personalities changes.
        /// </summary>
        public event EventHandler<EventArgs>? PersonalitiesCollectionChanged;

        /// <summary>
        /// Initializes a new instance of the PersonalityManager class.
        /// </summary>
        /// <param name="personalitiesDirectory">The directory where personality files are stored.</param>
        public PersonalityManager(string? personalitiesDirectory = null)
        {
            _personalitiesDirectory = personalitiesDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "JunoSidebar",
                "Personalities");

            // Ensure the personalities directory exists
            if (!Directory.Exists(_personalitiesDirectory))
            {
                Directory.CreateDirectory(_personalitiesDirectory);
            }

            // Initialize YAML serializer/deserializer with snake_case naming convention
            _yamlDeserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            _yamlSerializer = new SerializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            // Initialize JSON serializer options
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            // Initialize personalities dictionary
            _personalities = new Dictionary<string, Personality>();
        }

        /// <summary>
        /// Initializes the personality manager by loading built-in and custom personalities.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task InitializeAsync()
        {
            // Create initial personalities if none exist
            await CreateInitialPersonalitiesIfNeededAsync();

            // Load all personalities from disk
            await LoadAllPersonalitiesAsync();

            // Set default personality if none is active
            if (CurrentPersonality == null && _personalities.Count > 0)
            {
                await SetActivePersonalityAsync(_personalities.Values.FirstOrDefault()?.Id ?? string.Empty);
            }
        }

        /// <summary>
        /// Gets all available personalities.
        /// </summary>
        /// <returns>A collection of all available personalities.</returns>
        public IReadOnlyCollection<Personality> GetAllPersonalities()
        {
            return _personalities.Values;
        }

        /// <summary>
        /// Gets a personality by its ID.
        /// </summary>
        /// <param name="id">The ID of the personality to retrieve.</param>
        /// <returns>The personality with the specified ID, or null if not found.</returns>
        public Personality? GetPersonality(string id)
        {
            return _personalities.TryGetValue(id, out var personality) ? personality : null;
        }

        /// <summary>
        /// Sets the active personality by ID.
        /// </summary>
        /// <param name="id">The ID of the personality to set as active.</param>
        /// <returns>True if the personality was found and set as active, false otherwise.</returns>
        public async Task<bool> SetActivePersonalityAsync(string id)
        {
            if (!_personalities.TryGetValue(id, out var personality))
            {
                return false;
            }

            var previousPersonality = CurrentPersonality;
            CurrentPersonality = personality;

            // Save the current personality ID to settings
            await SaveCurrentPersonalityIdAsync(id);

            // Raise the event
            OnPersonalityChanged(new PersonalityChangedEventArgs(previousPersonality, CurrentPersonality));

            return true;
        }

        /// <summary>
        /// Creates a new custom personality.
        /// </summary>
        /// <param name="personality">The personality to create.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task CreatePersonalityAsync(Personality personality)
        {
            // Generate a unique ID if not provided
            if (string.IsNullOrEmpty(personality.Id))
            {
                personality.Id = Guid.NewGuid().ToString();
            }

            personality.BuiltIn = false;
            personality.CreatedAt = DateTime.UtcNow;
            personality.UpdatedAt = DateTime.UtcNow;

            // Save the personality to disk
            await SavePersonalityAsync(personality);

            // Add to the collection
            _personalities[personality.Id] = personality;

            // Notify about collection change
            OnPersonalitiesCollectionChanged();
        }

        /// <summary>
        /// Updates an existing personality.
        /// </summary>
        /// <param name="personality">The personality to update.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception cref="InvalidOperationException">Thrown if trying to update a built-in personality.</exception>
        public async Task UpdatePersonalityAsync(Personality personality)
        {
            // Check if the personality exists
            if (!_personalities.TryGetValue(personality.Id, out var existingPersonality))
            {
                throw new InvalidOperationException($"Personality with ID {personality.Id} does not exist.");
            }

            // Check if it's a built-in personality
            if (existingPersonality.BuiltIn)
            {
                throw new InvalidOperationException("Cannot update a built-in personality.");
            }

            personality.UpdatedAt = DateTime.UtcNow;
            personality.BuiltIn = false;

            // Save the personality to disk
            await SavePersonalityAsync(personality);

            // Update in the collection
            _personalities[personality.Id] = personality;

            // If this was the current personality, update the reference
            if (CurrentPersonality?.Id == personality.Id)
            {
                CurrentPersonality = personality;
                OnPersonalityChanged(new PersonalityChangedEventArgs(existingPersonality, personality));
            }

            // Notify about collection change
            OnPersonalitiesCollectionChanged();
        }

        /// <summary>
        /// Deletes a custom personality.
        /// </summary>
        /// <param name="id">The ID of the personality to delete.</param>
        /// <returns>True if the personality was deleted, false otherwise.</returns>
        /// <exception cref="InvalidOperationException">Thrown if trying to delete a built-in personality.</exception>
        public async Task<bool> DeletePersonalityAsync(string id)
        {
            // Check if the personality exists
            if (!_personalities.TryGetValue(id, out var personality))
            {
                return false;
            }

            // Check if it's a built-in personality
            if (personality.BuiltIn)
            {
                throw new InvalidOperationException("Cannot delete a built-in personality.");
            }

            // Check if it's the current personality
            if (CurrentPersonality?.Id == id)
            {
                // Find another personality to set as active
                var nextPersonality = _personalities.Values
                    .FirstOrDefault(p => p.Id != id);

                if (nextPersonality != null)
                {
                    await SetActivePersonalityAsync(nextPersonality.Id);
                }
                else
                {
                    CurrentPersonality = null;
                }
            }

            // Delete the personality file
            string filePath = GetPersonalityFilePath(id);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            // Remove from the collection
            _personalities.Remove(id);

            // Notify about collection change
            OnPersonalitiesCollectionChanged();

            return true;
        }

        /// <summary>
        /// Duplicates an existing personality with a new name.
        /// </summary>
        /// <param name="id">The ID of the personality to duplicate.</param>
        /// <param name="newName">The name for the duplicate personality.</param>
        /// <returns>The newly created personality, or null if the source personality was not found.</returns>
        public async Task<Personality?> DuplicatePersonalityAsync(string id, string newName)
        {
            // Check if the personality exists
            if (!_personalities.TryGetValue(id, out var personality))
            {
                return null;
            }

            // Create a clone of the personality
            var duplicate = personality.Clone();
            duplicate.Id = Guid.NewGuid().ToString();
            duplicate.Name = newName;
            duplicate.BuiltIn = false;
            duplicate.CreatedAt = DateTime.UtcNow;
            duplicate.UpdatedAt = DateTime.UtcNow;

            // Save the duplicate
            await SavePersonalityAsync(duplicate);

            // Add to the collection
            _personalities[duplicate.Id] = duplicate;

            // Notify about collection change
            OnPersonalitiesCollectionChanged();

            return duplicate;
        }

        // Private helper methods

        private async Task LoadAllPersonalitiesAsync()
        {
            _personalities.Clear();

            // Load built-in personalities from embedded resources
            foreach (var builtInPersonality in await LoadBuiltInPersonalitiesAsync())
            {
                _personalities[builtInPersonality.Id] = builtInPersonality;
            }

            // Load custom personalities from disk
            foreach (var file in Directory.GetFiles(_personalitiesDirectory, "*.yaml"))
            {
                try
                {
                    var personality = await LoadPersonalityFromFileAsync(file);
                    if (personality != null)
                    {
                        _personalities[personality.Id] = personality;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading personality from {file}: {ex.Message}");
                }
            }

            // Load the last active personality ID from settings
            string activePersonalityId = await LoadCurrentPersonalityIdAsync();
            if (!string.IsNullOrEmpty(activePersonalityId) &&
                _personalities.TryGetValue(activePersonalityId, out var activePersonality))
            {
                CurrentPersonality = activePersonality;
            }
            else if (_personalities.Count > 0)
            {
                // Default to the first personality
                CurrentPersonality = _personalities.Values.First();
            }

            // Notify about collection change
            OnPersonalitiesCollectionChanged();
            if (CurrentPersonality != null)
            {
                OnPersonalityChanged(new PersonalityChangedEventArgs(null, CurrentPersonality));
            }
        }

        private async Task<List<Personality>> LoadBuiltInPersonalitiesAsync()
        {
            var builtInPersonalities = new List<Personality>();

            // Define the built-in personalities
            var generalAssistant = new Personality
            {
                Name = "General Assistant",
                Id = "general-assistant",
                SystemPrompt =
                    "You are Juno, a helpful AI assistant. You are designed to be friendly, informative, and concise. " +
                    "You can help with a wide range of tasks, from answering questions to providing suggestions and assistance with planning.",
                Description = "A versatile AI assistant for general tasks and queries.",
                Avatar = "assistant",
                BuiltIn = true,
                Voice = new VoiceSettings
                {
                    Provider = "system",
                    VoiceId = "default",
                    Speed = 1.0f,
                    Pitch = 1.0f
                },
                Tools = new List<string> { "web_search", "note_taking", "calculator" },
                Preferences = new Dictionary<string, string>
                {
                    { "response_length", "balanced" },
                    { "creativity", "medium" },
                    { "formality", "casual" }
                }
            };

            var chef = new Personality
            {
                Name = "Chef Juno",
                Id = "chef",
                SystemPrompt =
                    "You are Chef Juno, a culinary expert who helps with meal planning, recipes, and cooking advice. " +
                    "You use cooking terminology and have a warm, encouraging tone. You're knowledgeable about ingredients, " +
                    "techniques, and can help adapt recipes for dietary restrictions.",
                Description = "A culinary expert for recipes, meal planning, and cooking advice.",
                Avatar = "chef",
                BuiltIn = true,
                Voice = new VoiceSettings
                {
                    Provider = "elevenlabs",
                    VoiceId = "antonio",
                    Speed = 1.1f,
                    Pitch = 1.0f
                },
                Tools = new List<string> { "mealie_connector", "ingredient_matcher", "recipe_suggester" },
                Preferences = new Dictionary<string, string>
                {
                    { "response_length", "concise" },
                    { "creativity", "high" },
                    { "formality", "casual" }
                }
            };

            var researcher = new Personality
            {
                Name = "Research Assistant",
                Id = "researcher",
                SystemPrompt =
                    "You are Juno, a research assistant specializing in gathering, analyzing, and summarizing information. " +
                    "You're precise, methodical, and focused on providing well-cited, factual information. " +
                    "You can help with literature reviews, data analysis, and presenting complex information clearly.",
                Description = "A scholarly assistant for research, analysis, and factual information.",
                Avatar = "researcher",
                BuiltIn = true,
                Voice = new VoiceSettings
                {
                    Provider = "system",
                    VoiceId = "academic",
                    Speed = 0.9f,
                    Pitch = 1.0f
                },
                Tools = new List<string> { "web_search", "document_analyzer", "citation_helper", "summarizer" },
                Preferences = new Dictionary<string, string>
                {
                    { "response_length", "comprehensive" },
                    { "creativity", "low" },
                    { "formality", "professional" }
                }
            };

            builtInPersonalities.Add(generalAssistant);
            builtInPersonalities.Add(chef);
            builtInPersonalities.Add(researcher);

            // Save built-in personalities to disk if they don't exist
            foreach (var personality in builtInPersonalities)
            {
                string filePath = GetPersonalityFilePath(personality.Id);
                if (!File.Exists(filePath))
                {
                    await SavePersonalityAsync(personality);
                }
            }

            return builtInPersonalities;
        }

        private async Task<Personality?> LoadPersonalityFromFileAsync(string filePath)
        {
            try
            {
                string yaml = await File.ReadAllTextAsync(filePath);
                var personality = _yamlDeserializer.Deserialize<Personality>(yaml);

                // Validate required fields
                if (string.IsNullOrEmpty(personality.Id) || string.IsNullOrEmpty(personality.Name))
                {
                    Console.WriteLine($"Invalid personality in {filePath}: Missing ID or Name");
                    return null;
                }

                return personality;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading personality file {filePath}: {ex.Message}");
                return null;
            }
        }

        private async Task SavePersonalityAsync(Personality personality)
        {
            string filePath = GetPersonalityFilePath(personality.Id);
            string yaml = _yamlSerializer.Serialize(personality);
            await File.WriteAllTextAsync(filePath, yaml);
        }

        private string GetPersonalityFilePath(string id)
        {
            return Path.Combine(_personalitiesDirectory, $"{id}.yaml");
        }

        private async Task CreateInitialPersonalitiesIfNeededAsync()
        {
            // Check if personalities directory is empty
            if (Directory.GetFiles(_personalitiesDirectory, "*.yaml").Length > 0)
            {
                return;
            }

            // Create initial built-in personalities
            await LoadBuiltInPersonalitiesAsync();
        }

        private async Task SaveCurrentPersonalityIdAsync(string id)
        {
            string settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "JunoSidebar",
                "settings.json");

            var settings = await LoadSettingsAsync();
            settings["currentPersonalityId"] = id;

            string json = JsonSerializer.Serialize(settings, _jsonOptions);
            await File.WriteAllTextAsync(settingsPath, json);
        }

        private async Task<string> LoadCurrentPersonalityIdAsync()
        {
            var settings = await LoadSettingsAsync();
            if (settings.TryGetValue("currentPersonalityId", out var id) && id is JsonElement element)
            {
                return element.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        private async Task<Dictionary<string, object>> LoadSettingsAsync()
        {
            string settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "JunoSidebar",
                "settings.json");

            if (!File.Exists(settingsPath))
            {
                return new Dictionary<string, object>();
            }

            try
            {
                string json = await File.ReadAllTextAsync(settingsPath);
                var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                return settings?.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value) ??
                       new Dictionary<string, object>();
            }
            catch (Exception)
            {
                return new Dictionary<string, object>();
            }
        }

        private void OnPersonalityChanged(PersonalityChangedEventArgs args)
        {
            PersonalityChanged?.Invoke(this, args);
        }

        private void OnPersonalitiesCollectionChanged()
        {
            PersonalitiesCollectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Event args for personality changes.
    /// </summary>
    public class PersonalityChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the previous active personality.
        /// </summary>
        public Personality? PreviousPersonality { get; }

        /// <summary>
        /// Gets the new active personality.
        /// </summary>
        public Personality? NewPersonality { get; }

        /// <summary>
        /// Initializes a new instance of the PersonalityChangedEventArgs class.
        /// </summary>
        /// <param name="previousPersonality">The previous active personality.</param>
        /// <param name="newPersonality">The new active personality.</param>
        public PersonalityChangedEventArgs(Personality? previousPersonality, Personality? newPersonality)
        {
            PreviousPersonality = previousPersonality;
            NewPersonality = newPersonality;
        }
    }
}