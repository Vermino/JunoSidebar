using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JunoSidebar.Wpf.Services.Tools.Implementations;

namespace JunoSidebar.Wpf.Services.Tools
{
    public static class ToolRegistryInitializer
    {
        public static async Task RegisterBuiltInToolsAsync(ToolRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }
            var webSearchTool = new WebSearchTool();
            registry.RegisterTool(webSearchTool);
            registry.RegisterTool(PlaceholderToolFactory.CreateNoteTakingTool());
            registry.RegisterTool(PlaceholderToolFactory.CreateCalculatorTool());
            registry.RegisterTool(PlaceholderToolFactory.CreateSystemInfoTool());
            registry.RegisterTool(PlaceholderToolFactory.CreateSchedulerTool());
            await LoadExternalToolsAsync(registry);
        }

        private static Task LoadExternalToolsAsync(ToolRegistry registry)
        {
            return Task.CompletedTask;
        }
    }

    public static class PlaceholderToolFactory
    {
        public static ITool CreateNoteTakingTool()
        {
            return new FactoryPlaceholderTool(
                "note_taking",
                "Note Taking",
                "Creates and manages notes and reminders.",
                new string[] { "filesystem.read", "filesystem.write" },
                new ToolParameterSchema
                {
                    Properties = new List<ToolParameterProperty>
                    {
                        new ToolParameterProperty
                        {
                            Name = "content",
                            DisplayName = "Note Content",
                            Description = "The content of the note to create.",
                            Type = ToolPropertyType.String
                        },
                        new ToolParameterProperty
                        {
                            Name = "title",
                            DisplayName = "Note Title",
                            Description = "The title of the note.",
                            Type = ToolPropertyType.String
                        }
                    },
                    Required = new List<string> { "content" }
                });
        }

        public static ITool CreateCalculatorTool()
        {
            return new FactoryPlaceholderTool(
                "calculator",
                "Calculator",
                "Performs mathematical calculations.",
                Array.Empty<string>(),
                new ToolParameterSchema
                {
                    Properties = new List<ToolParameterProperty>
                    {
                        new ToolParameterProperty
                        {
                            Name = "expression",
                            DisplayName = "Expression",
                            Description = "The mathematical expression to evaluate.",
                            Type = ToolPropertyType.String
                        }
                    },
                    Required = new List<string> { "expression" }
                });
        }

        public static ITool CreateSystemInfoTool()
        {
            return new FactoryPlaceholderTool(
                "system_info",
                "System Information",
                "Provides information about the system and hardware.",
                new string[] { "system.info" },
                new ToolParameterSchema
                {
                    Properties = new List<ToolParameterProperty>
                    {
                        new ToolParameterProperty
                        {
                            Name = "infoType",
                            DisplayName = "Information Type",
                            Description = "The type of system information to retrieve.",
                            Type = ToolPropertyType.Enum,
                            Options = new List<EnumOption>
                            {
                                new EnumOption { Value = "os", Label = "Operating System" },
                                new EnumOption { Value = "cpu", Label = "CPU" },
                                new EnumOption { Value = "memory", Label = "Memory" },
                                new EnumOption { Value = "disk", Label = "Disk Space" },
                                new EnumOption { Value = "all", Label = "All Information" }
                            },
                            DefaultValue = "all"
                        }
                    }
                });
        }

        public static ITool CreateSchedulerTool()
        {
            return new FactoryPlaceholderTool(
                "scheduler",
                "Scheduler",
                "Manages calendar events and reminders.",
                new string[] { "filesystem.read", "filesystem.write" },
                new ToolParameterSchema
                {
                    Properties = new List<ToolParameterProperty>
                    {
                        new ToolParameterProperty
                        {
                            Name = "action",
                            DisplayName = "Action",
                            Description = "The action to perform.",
                            Type = ToolPropertyType.Enum,
                            Options = new List<EnumOption>
                            {
                                new EnumOption { Value = "create", Label = "Create Event" },
                                new EnumOption { Value = "list", Label = "List Events" },
                                new EnumOption { Value = "delete", Label = "Delete Event" }
                            },
                            DefaultValue = "list"
                        },
                        new ToolParameterProperty
                        {
                            Name = "title",
                            DisplayName = "Event Title",
                            Description = "The title of the event.",
                            Type = ToolPropertyType.String
                        },
                        new ToolParameterProperty
                        {
                            Name = "dateTime",
                            DisplayName = "Date and Time",
                            Description = "The date and time of the event.",
                            Type = ToolPropertyType.DateTime
                        },
                        new ToolParameterProperty
                        {
                            Name = "duration",
                            DisplayName = "Duration (minutes)",
                            Description = "The duration of the event in minutes.",
                            Type = ToolPropertyType.Integer,
                            DefaultValue = 30
                        }
                    },
                    Required = new List<string> { "action" }
                });
        }
    }

    internal class FactoryPlaceholderTool : ITool
    {
        private readonly string _id;
        private readonly string _name;
        private readonly string _description;
        private readonly string[] _requiredPermissions;
        private readonly ToolParameterSchema _parameterSchema;
        public string Id => _id;
        public string Name => _name;
        public string Description => _description;
        public string Version => "1.0.0";
        public IEnumerable<string> RequiredPermissions => _requiredPermissions;
        public ToolConfigSchema ConfigSchema => new ToolConfigSchema();
        public Dictionary<string, object> Config { get; set; } = new Dictionary<string, object>();
        public bool AllowsAutomaticExecution => true;
        public bool RequiresUserInterface => false;

        public FactoryPlaceholderTool(
            string id,
            string name,
            string description,
            string[] requiredPermissions,
            ToolParameterSchema parameterSchema)
        {
            _id = id;
            _name = name;
            _description = description;
            _requiredPermissions = requiredPermissions;
            _parameterSchema = parameterSchema;
        }

        public Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            return Task.FromResult(ToolResult.CreateSuccess($"Executed {_name} with parameters: {string.Join(", ", parameters.Keys)}"));
        }

        public string GetHelp()
        {
            return $"{_name}\n{new string('-', _name.Length)}\n{_description}\n\nThis is a placeholder implementation for demonstration purposes.";
        }

        public ToolParameterSchema GetParameterSchema()
        {
            return _parameterSchema;
        }

        public bool ValidateConfig()
        {
            return true;
        }
    }
}