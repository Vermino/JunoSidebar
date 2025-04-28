// File: JunoSidebar/JunoSidebar.Wpf/Services/Tools/PermissionManager.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace JunoSidebar.Wpf.Services.Tools
{
    /// <summary>
    /// Manages permissions for tool access and execution.
    /// </summary>
    public class PermissionManager
    {
        private readonly Dictionary<string, PermissionState> _permissions = new();
        private readonly string _permissionsFilePath;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Event raised when a permission state changes.
        /// </summary>
        public event EventHandler<PermissionChangedEventArgs>? PermissionChanged;

        /// <summary>
        /// Initializes a new instance of the PermissionManager class.
        /// </summary>
        /// <param name="permissionsDirectory">The directory where permissions data is stored.</param>
        public PermissionManager(string? permissionsDirectory = null)
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string directoryPath = permissionsDirectory ?? Path.Combine(appDataPath, "JunoSidebar");
            
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
            
            _permissionsFilePath = Path.Combine(directoryPath, "permissions.json");
            
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true
            };
        }

        /// <summary>
        /// Initializes the permission system by loading saved permissions.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task InitializeAsync()
        {
            await LoadPermissionsAsync();
            RegisterDefaultPermissions();
        }

        /// <summary>
        /// Checks if the specified permission is granted.
        /// </summary>
        /// <param name="permissionName">The name of the permission to check.</param>
        /// <returns>True if the permission is granted, false otherwise.</returns>
        public bool HasPermission(string permissionName)
        {
            if (_permissions.TryGetValue(permissionName, out var state))
            {
                return state.IsGranted;
            }
            
            // Default to denied for unknown permissions
            return false;
        }

        /// <summary>
        /// Gets the current state of the specified permission.
        /// </summary>
        /// <param name="permissionName">The name of the permission to get.</param>
        /// <returns>The permission state, or null if the permission is not registered.</returns>
        public PermissionState? GetPermissionState(string permissionName)
        {
            return _permissions.TryGetValue(permissionName, out var state) ? state : null;
        }

        /// <summary>
        /// Gets all registered permissions.
        /// </summary>
        /// <returns>A dictionary of permission names and their states.</returns>
        public IReadOnlyDictionary<string, PermissionState> GetAllPermissions()
        {
            return _permissions;
        }

        /// <summary>
        /// Registers a new permission.
        /// </summary>
        /// <param name="permissionName">The name of the permission.</param>
        /// <param name="displayName">The display name of the permission.</param>
        /// <param name="description">A description of what the permission allows.</param>
        /// <param name="category">The category of the permission.</param>
        /// <param name="defaultState">The default state of the permission.</param>
        /// <returns>True if the permission was registered, false if it already exists.</returns>
        public bool RegisterPermission(
            string permissionName, 
            string displayName, 
            string description, 
            PermissionCategory category,
            bool defaultState = false)
        {
            if (_permissions.ContainsKey(permissionName))
            {
                return false;
            }
            
            var permissionState = new PermissionState
            {
                Name = permissionName,
                DisplayName = displayName,
                Description = description,
                Category = category,
                IsGranted = defaultState,
                LastModified = DateTime.UtcNow
            };
            
            _permissions[permissionName] = permissionState;
            SavePermissionsAsync().ConfigureAwait(false);
            
            OnPermissionChanged(new PermissionChangedEventArgs(permissionName, permissionState));
            return true;
        }

        /// <summary>
        /// Sets the state of a permission.
        /// </summary>
        /// <param name="permissionName">The name of the permission to set.</param>
        /// <param name="isGranted">Whether the permission should be granted.</param>
        /// <returns>True if the permission was set, false if it doesn't exist.</returns>
        public async Task<bool> SetPermissionAsync(string permissionName, bool isGranted)
        {
            if (!_permissions.TryGetValue(permissionName, out var state))
            {
                return false;
            }
            
            state.IsGranted = isGranted;
            state.LastModified = DateTime.UtcNow;
            
            await SavePermissionsAsync();
            
            OnPermissionChanged(new PermissionChangedEventArgs(permissionName, state));
            return true;
        }

        /// <summary>
        /// Requests a permission from the user.
        /// </summary>
        /// <param name="permissionName">The name of the permission to request.</param>
        /// <param name="reason">The reason why the permission is needed.</param>
        /// <returns>True if the permission was granted, false otherwise.</returns>
        public async Task<bool> RequestPermissionAsync(string permissionName, string reason)
        {
            if (!_permissions.TryGetValue(permissionName, out var state))
            {
                return false;
            }
            
            if (state.IsGranted)
            {
                return true;
            }
            
            // In a real implementation, this would show a UI dialog to the user
            // For now, we'll simulate always granting the permission
            bool granted = true; // Simulated user response
            
            if (granted)
            {
                return await SetPermissionAsync(permissionName, true);
            }
            
            return false;
        }

        // Private helper methods

        private async Task LoadPermissionsAsync()
        {
            if (!File.Exists(_permissionsFilePath))
            {
                return;
            }
            
            try
            {
                string json = await File.ReadAllTextAsync(_permissionsFilePath);
                var permissions = JsonSerializer.Deserialize<Dictionary<string, PermissionState>>(json, _jsonOptions);
                
                if (permissions != null)
                {
                    foreach (var permission in permissions)
                    {
                        _permissions[permission.Key] = permission.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading permissions: {ex.Message}");
            }
        }

        private async Task SavePermissionsAsync()
        {
            try
            {
                string json = JsonSerializer.Serialize(_permissions, _jsonOptions);
                await File.WriteAllTextAsync(_permissionsFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving permissions: {ex.Message}");
            }
        }

        private void RegisterDefaultPermissions()
        {
            // Register default permissions if they don't exist already
            RegisterBasicPermissions();
            RegisterFileSystemPermissions();
            RegisterNetworkPermissions();
            RegisterSystemPermissions();
        }

        private void RegisterBasicPermissions()
        {
            RegisterPermission(
                "tools.execution",
                "Execute Tools",
                "Allows the assistant to execute tools on your behalf.",
                PermissionCategory.Basic,
                true
            );
            
            RegisterPermission(
                "tools.auto_execution",
                "Automatic Tool Execution",
                "Allows the assistant to automatically execute tools without confirmation.",
                PermissionCategory.Basic,
                false
            );
        }

        private void RegisterFileSystemPermissions()
        {
            RegisterPermission(
                "filesystem.read",
                "Read Files",
                "Allows the assistant to read files on your device.",
                PermissionCategory.FileSystem,
                false
            );
            
            RegisterPermission(
                "filesystem.write",
                "Write Files",
                "Allows the assistant to create or modify files on your device.",
                PermissionCategory.FileSystem,
                false
            );
            
            RegisterPermission(
                "filesystem.delete",
                "Delete Files",
                "Allows the assistant to delete files on your device.",
                PermissionCategory.FileSystem,
                false
            );
        }

        private void RegisterNetworkPermissions()
        {
            RegisterPermission(
                "network.http",
                "Internet Access",
                "Allows the assistant to make HTTP requests to the internet.",
                PermissionCategory.Network,
                true
            );
            
            RegisterPermission(
                "network.download",
                "Download Files",
                "Allows the assistant to download files from the internet.",
                PermissionCategory.Network,
                false
            );
        }

        private void RegisterSystemPermissions()
        {
            RegisterPermission(
                "system.info",
                "System Information",
                "Allows the assistant to access information about your system.",
                PermissionCategory.System,
                true
            );
            
            RegisterPermission(
                "system.clipboard",
                "Clipboard Access",
                "Allows the assistant to read from and write to the clipboard.",
                PermissionCategory.System,
                false
            );
        }

        private void OnPermissionChanged(PermissionChangedEventArgs args)
        {
            PermissionChanged?.Invoke(this, args);
        }
    }

    /// <summary>
    /// Represents the state of a permission.
    /// </summary>
    public class PermissionState
    {
        /// <summary>
        /// Gets or sets the name of the permission.
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the display name of the permission.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the description of the permission.
        /// </summary>
        public string Description { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the category of the permission.
        /// </summary>
        public PermissionCategory Category { get; set; }
        
        /// <summary>
        /// Gets or sets whether the permission is granted.
        /// </summary>
        public bool IsGranted { get; set; }
        
        /// <summary>
        /// Gets or sets when the permission was last modified.
        /// </summary>
        public DateTime LastModified { get; set; }
    }

    /// <summary>
    /// Categories of permissions.
    /// </summary>
    public enum PermissionCategory
    {
        /// <summary>
        /// Basic permissions for core functionality.
        /// </summary>
        Basic,
        
        /// <summary>
        /// Permissions related to file system access.
        /// </summary>
        FileSystem,
        
        /// <summary>
        /// Permissions related to network access.
        /// </summary>
        Network,
        
        /// <summary>
        /// Permissions related to system functionality.
        /// </summary>
        System,
        
        /// <summary>
        /// Permissions related to user data.
        /// </summary>
        UserData,
        
        /// <summary>
        /// Permissions related to external services.
        /// </summary>
        ExternalServices
    }

    /// <summary>
    /// Event args for permission changes.
    /// </summary>
    public class PermissionChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the name of the permission that changed.
        /// </summary>
        public string PermissionName { get; }
        
        /// <summary>
        /// Gets the new state of the permission.
        /// </summary>
        public PermissionState State { get; }
        
        /// <summary>
        /// Initializes a new instance of the PermissionChangedEventArgs class.
        /// </summary>
        /// <param name="permissionName">The name of the permission that changed.</param>
        /// <param name="state">The new state of the permission.</param>
        public PermissionChangedEventArgs(string permissionName, PermissionState state)
        {
            PermissionName = permissionName;
            State = state;
        }
    }
}