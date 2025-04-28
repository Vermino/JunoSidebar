using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace JunoSidebar.Wpf.Services.Tools
{
    public interface ITool
    {
        string Id { get; }
        string Name { get; }
        string Description { get; }
        string Version { get; }
        IEnumerable<string> RequiredPermissions { get; }
        ToolConfigSchema ConfigSchema { get; }
        Dictionary<string, object> Config { get; set; }
        Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters);
        string GetHelp();
        bool ValidateConfig();
        ToolParameterSchema GetParameterSchema();
        bool AllowsAutomaticExecution { get; }
        bool RequiresUserInterface { get; }
    }

    public class ToolResult
    {
        public bool Success { get; set; }
        public object Output { get; set; } = new object();
        public string? Error { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    
        // Renamed static methods to avoid conflicts with properties
        public static ToolResult CreateSuccess(object output, Dictionary<string, object>? metadata = null)
        {
            return new ToolResult
            {
                Success = true,
                Output = output,
                Metadata = metadata ?? new Dictionary<string, object>()
            };
        }

        public static ToolResult CreateError(string error, Dictionary<string, object>? metadata = null)
        {
            return new ToolResult
            {
                Success = false,
                Error = error,
                Metadata = metadata ?? new Dictionary<string, object>()
            };
        }
    }

    public class ToolConfigSchema
    {
        public List<ToolConfigProperty> Properties { get; set; } = new List<ToolConfigProperty>();
        public List<string> Required { get; set; } = new List<string>();
    }

    public class ToolConfigProperty
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ToolPropertyType Type { get; set; }
        public object? DefaultValue { get; set; }
        public List<EnumOption>? Options { get; set; }
        public ValidationConstraints? Validation { get; set; }
    }

    public class ToolParameterSchema
    {
        public List<ToolParameterProperty> Properties { get; set; } = new List<ToolParameterProperty>();
        public List<string> Required { get; set; } = new List<string>();
    }

    public class ToolParameterProperty
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ToolPropertyType Type { get; set; }
        public object? DefaultValue { get; set; }
        public List<EnumOption>? Options { get; set; }
        public ValidationConstraints? Validation { get; set; }
        public bool ExposeToLLM { get; set; } = true;
    }

    public enum ToolPropertyType
    {
        String,
        Integer,
        Number,
        Boolean,
        Enum,
        Date,
        DateTime,
        FilePath,
        DirectoryPath,
        Object,
        Array,
        SecureString
    }

    public class EnumOption
    {
        public string Value { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class ValidationConstraints
    {
        public double? Minimum { get; set; }
        public double? Maximum { get; set; }
        public int? MinLength { get; set; }
        public int? MaxLength { get; set; }
        public string? Pattern { get; set; }
    }
}