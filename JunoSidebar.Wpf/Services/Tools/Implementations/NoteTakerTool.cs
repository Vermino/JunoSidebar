// File: JunoSidebar.Wpf/Services/Tools/Implementations/NoteTakerTool.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;

namespace JunoSidebar.Wpf.Services.Tools.Implementations
{
    /// <summary>
    /// Tool for creating and managing persistent notes
    /// </summary>
    public class NoteTakerTool : ITool
    {
        private readonly string _notesDirectory;

        public string Id => "note_taker";
        public string Name => "Note Taker";
        public string Description => "Creates, retrieves, and manages persistent notes";
        public string Version => "1.0.0";
        public IEnumerable<string> RequiredPermissions => new[] { "filesystem.write", "filesystem.read" };
        public bool AllowsAutomaticExecution => false;
        public bool RequiresUserInterface => false;
        public Dictionary<string, object> Config { get; set; } = new();

        public ToolConfigSchema ConfigSchema => new ToolConfigSchema
        {
            Properties = new List<ToolConfigProperty>
            {
                new ToolConfigProperty
                {
                    Name = "notesDirectory",
                    DisplayName = "Notes Directory",
                    Description = "Directory where notes are stored",
                    Type = ToolPropertyType.DirectoryPath,
                    DefaultValue = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "JunoSidebar", "Notes")
                }
            }
        };

        public NoteTakerTool(string? notesDirectory = null)
        {
            _notesDirectory = notesDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "JunoSidebar",
                "Notes");

            Directory.CreateDirectory(_notesDirectory);
            Debug.WriteLine($"NoteTakerTool initialized with directory: {_notesDirectory}");
        }

        public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            try
            {
                if (!parameters.TryGetValue("action", out var actionObj))
                {
                    return ToolResult.CreateError("Action parameter is required (create, read, list, delete)");
                }

                string action = actionObj.ToString()!.ToLower();

                return action switch
                {
                    "create" => await CreateNoteAsync(parameters),
                    "read" => await ReadNoteAsync(parameters),
                    "list" => await ListNotesAsync(parameters),
                    "delete" => await DeleteNoteAsync(parameters),
                    "search" => await SearchNotesAsync(parameters),
                    _ => ToolResult.CreateError($"Unknown action: {action}")
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in NoteTakerTool: {ex.Message}");
                return ToolResult.CreateError($"Error: {ex.Message}");
            }
        }

        public string GetHelp()
        {
            return @"Note Taker Tool

Manages persistent notes with create, read, list, and delete operations.

Actions:
1. create - Create a new note
   Parameters: title, content, tags (optional)

2. read - Read a specific note
   Parameters: noteId

3. list - List all notes
   Parameters: tag (optional filter)

4. delete - Delete a note
   Parameters: noteId

5. search - Search notes by keyword
   Parameters: query

Example:
{
  ""action"": ""create"",
  ""title"": ""Meeting Notes"",
  ""content"": ""Discussion about project timeline"",
  ""tags"": [""meeting"", ""project""]
}";
        }

        public bool ValidateConfig()
        {
            return Directory.Exists(_notesDirectory);
        }

        public ToolParameterSchema GetParameterSchema()
        {
            return new ToolParameterSchema
            {
                Properties = new List<ToolParameterProperty>
                {
                    new ToolParameterProperty
                    {
                        Name = "action",
                        DisplayName = "Action",
                        Description = "Action to perform (create, read, list, delete, search)",
                        Type = ToolPropertyType.Enum,
                        ExposeToLLM = true
                    },
                    new ToolParameterProperty
                    {
                        Name = "title",
                        DisplayName = "Title",
                        Description = "Note title (for create action)",
                        Type = ToolPropertyType.String,
                        ExposeToLLM = true
                    },
                    new ToolParameterProperty
                    {
                        Name = "content",
                        DisplayName = "Content",
                        Description = "Note content (for create action)",
                        Type = ToolPropertyType.String,
                        ExposeToLLM = true
                    },
                    new ToolParameterProperty
                    {
                        Name = "noteId",
                        DisplayName = "Note ID",
                        Description = "Note identifier (for read/delete actions)",
                        Type = ToolPropertyType.String,
                        ExposeToLLM = true
                    }
                },
                Required = new List<string> { "action" }
            };
        }

        private async Task<ToolResult> CreateNoteAsync(Dictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("content", out var contentObj))
            {
                return ToolResult.CreateError("Content parameter is required for create action");
            }

            var note = new Note
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = parameters.TryGetValue("title", out var titleObj) ? titleObj.ToString()! : "Untitled",
                Content = contentObj.ToString()!,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            if (parameters.TryGetValue("tags", out var tagsObj) && tagsObj is IEnumerable<object> tags)
            {
                note.Tags = tags.Select(t => t.ToString()!).ToList();
            }

            var notePath = Path.Combine(_notesDirectory, $"{note.Id}.json");
            var json = JsonSerializer.Serialize(note, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(notePath, json);

            Debug.WriteLine($"Created note: {note.Title} ({note.Id})");

            return ToolResult.CreateSuccess(new
            {
                noteId = note.Id,
                title = note.Title,
                createdAt = note.CreatedAt
            });
        }

        private async Task<ToolResult> ReadNoteAsync(Dictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("noteId", out var noteIdObj))
            {
                return ToolResult.CreateError("NoteId parameter is required for read action");
            }

            string noteId = noteIdObj.ToString()!;
            var notePath = Path.Combine(_notesDirectory, $"{noteId}.json");

            if (!File.Exists(notePath))
            {
                return ToolResult.CreateError($"Note not found: {noteId}");
            }

            var json = await File.ReadAllTextAsync(notePath);
            var note = JsonSerializer.Deserialize<Note>(json);

            return ToolResult.CreateSuccess(note);
        }

        private async Task<ToolResult> ListNotesAsync(Dictionary<string, object> parameters)
        {
            var noteFiles = Directory.GetFiles(_notesDirectory, "*.json");
            var notes = new List<Note>();

            foreach (var file in noteFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var note = JsonSerializer.Deserialize<Note>(json);
                    if (note != null)
                    {
                        notes.Add(note);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error reading note file {file}: {ex.Message}");
                }
            }

            // Filter by tag if specified
            if (parameters.TryGetValue("tag", out var tagObj))
            {
                string tag = tagObj.ToString()!;
                notes = notes.Where(n => n.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)).ToList();
            }

            // Sort by updated date
            notes = notes.OrderByDescending(n => n.UpdatedAt).ToList();

            var summary = notes.Select(n => new
            {
                noteId = n.Id,
                title = n.Title,
                tags = n.Tags,
                createdAt = n.CreatedAt,
                updatedAt = n.UpdatedAt,
                contentPreview = n.Content.Length > 100 ? n.Content.Substring(0, 100) + "..." : n.Content
            });

            return ToolResult.CreateSuccess(new
            {
                totalNotes = notes.Count,
                notes = summary
            });
        }

        private Task<ToolResult> DeleteNoteAsync(Dictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("noteId", out var noteIdObj))
            {
                return Task.FromResult(ToolResult.CreateError("NoteId parameter is required for delete action"));
            }

            string noteId = noteIdObj.ToString()!;
            var notePath = Path.Combine(_notesDirectory, $"{noteId}.json");

            if (!File.Exists(notePath))
            {
                return Task.FromResult(ToolResult.CreateError($"Note not found: {noteId}"));
            }

            File.Delete(notePath);
            Debug.WriteLine($"Deleted note: {noteId}");

            return Task.FromResult(ToolResult.CreateSuccess(new { noteId = noteId, deleted = true }));
        }

        private async Task<ToolResult> SearchNotesAsync(Dictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("query", out var queryObj))
            {
                return ToolResult.CreateError("Query parameter is required for search action");
            }

            string query = queryObj.ToString()!.ToLower();
            var noteFiles = Directory.GetFiles(_notesDirectory, "*.json");
            var matchingNotes = new List<Note>();

            foreach (var file in noteFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var note = JsonSerializer.Deserialize<Note>(json);

                    if (note != null &&
                        (note.Title.ToLower().Contains(query) ||
                         note.Content.ToLower().Contains(query) ||
                         note.Tags.Any(t => t.ToLower().Contains(query))))
                    {
                        matchingNotes.Add(note);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error searching note file {file}: {ex.Message}");
                }
            }

            var results = matchingNotes.Select(n => new
            {
                noteId = n.Id,
                title = n.Title,
                contentPreview = n.Content.Length > 150 ? n.Content.Substring(0, 150) + "..." : n.Content,
                tags = n.Tags,
                updatedAt = n.UpdatedAt
            });

            return ToolResult.CreateSuccess(new
            {
                query = query,
                matchCount = matchingNotes.Count,
                matches = results
            });
        }

        private class Note
        {
            public string Id { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Content { get; set; } = string.Empty;
            public List<string> Tags { get; set; } = new();
            public DateTime CreatedAt { get; set; }
            public DateTime UpdatedAt { get; set; }
        }
    }
}
