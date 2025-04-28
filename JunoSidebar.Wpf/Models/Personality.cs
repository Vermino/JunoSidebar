// File: JunoSidebar/JunoSidebar.Wpf/Models/Personality.cs
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace JunoSidebar.Wpf.Models
{
    /// <summary>
    /// Represents an assistant personality with specific voice settings,
    /// system prompts, and preferred tools.
    /// </summary>
    public class Personality
    {
        /// <summary>
        /// Gets or sets the display name of the personality.
        /// </summary>
        [YamlMember(Alias = "name")]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the unique identifier for the personality.
        /// </summary>
        [YamlMember(Alias = "id")]
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the system prompt that defines the personality's behavior.
        /// </summary>
        [YamlMember(Alias = "system_prompt")]
        [JsonPropertyName("systemPrompt")]
        public string SystemPrompt { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a short description of the personality.
        /// </summary>
        [YamlMember(Alias = "description")]
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the avatar/icon for the personality.
        /// </summary>
        [YamlMember(Alias = "avatar")]
        [JsonPropertyName("avatar")]
        public string Avatar { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the voice settings for the personality.
        /// </summary>
        [YamlMember(Alias = "voice")]
        [JsonPropertyName("voice")]
        public VoiceSettings Voice { get; set; } = new VoiceSettings();

        /// <summary>
        /// Gets or sets the list of tool IDs this personality prefers to use.
        /// </summary>
        [YamlMember(Alias = "tools")]
        [JsonPropertyName("tools")]
        public List<string> Tools { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets additional preferences for this personality.
        /// </summary>
        [YamlMember(Alias = "preferences")]
        [JsonPropertyName("preferences")]
        public Dictionary<string, string> Preferences { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Gets or sets whether this personality is a built-in (non-editable) personality.
        /// </summary>
        [YamlMember(Alias = "built_in")]
        [JsonPropertyName("builtIn")]
        public bool BuiltIn { get; set; } = false;

        /// <summary>
        /// Gets or sets when this personality was created.
        /// </summary>
        [YamlMember(Alias = "created_at")]
        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Gets or sets when this personality was last modified.
        /// </summary>
        [YamlMember(Alias = "updated_at")]
        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Creates a deep clone of this personality.
        /// </summary>
        /// <returns>A new Personality instance with the same values.</returns>
        public Personality Clone()
        {
            var clone = new Personality
            {
                Name = Name,
                Id = Id,
                SystemPrompt = SystemPrompt,
                Description = Description,
                Avatar = Avatar,
                Voice = Voice.Clone(),
                BuiltIn = BuiltIn,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt
            };

            // Clone collections
            clone.Tools.AddRange(Tools);
            foreach (var preference in Preferences)
            {
                clone.Preferences[preference.Key] = preference.Value;
            }

            return clone;
        }
    }

    /// <summary>
    /// Voice settings for a personality.
    /// </summary>
    public class VoiceSettings
    {
        /// <summary>
        /// Gets or sets the voice provider (e.g., "elevenlabs", "azure", "local").
        /// </summary>
        [YamlMember(Alias = "provider")]
        [JsonPropertyName("provider")]
        public string Provider { get; set; } = "system";

        /// <summary>
        /// Gets or sets the voice identifier within the provider's catalog.
        /// </summary>
        [YamlMember(Alias = "voice_id")]
        [JsonPropertyName("voiceId")]
        public string VoiceId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the voice playback speed factor.
        /// </summary>
        [YamlMember(Alias = "speed")]
        [JsonPropertyName("speed")]
        public float Speed { get; set; } = 1.0f;

        /// <summary>
        /// Gets or sets the voice pitch adjustment.
        /// </summary>
        [YamlMember(Alias = "pitch")]
        [JsonPropertyName("pitch")]
        public float Pitch { get; set; } = 1.0f;

        /// <summary>
        /// Gets or sets the voice stability parameter (provider-specific).
        /// </summary>
        [YamlMember(Alias = "stability")]
        [JsonPropertyName("stability")]
        public float Stability { get; set; } = 0.5f;

        /// <summary>
        /// Gets or sets the voice similarity boost parameter (provider-specific).
        /// </summary>
        [YamlMember(Alias = "similarity_boost")]
        [JsonPropertyName("similarityBoost")]
        public float SimilarityBoost { get; set; } = 0.75f;

        /// <summary>
        /// Gets or sets additional voice settings (provider-specific).
        /// </summary>
        [YamlMember(Alias = "settings")]
        [JsonPropertyName("settings")]
        public Dictionary<string, string> Settings { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Creates a deep clone of these voice settings.
        /// </summary>
        /// <returns>A new VoiceSettings instance with the same values.</returns>
        public VoiceSettings Clone()
        {
            var clone = new VoiceSettings
            {
                Provider = Provider,
                VoiceId = VoiceId,
                Speed = Speed,
                Pitch = Pitch,
                Stability = Stability,
                SimilarityBoost = SimilarityBoost
            };

            // Clone settings dictionary
            foreach (var setting in Settings)
            {
                clone.Settings[setting.Key] = setting.Value;
            }

            return clone;
        }
    }
}