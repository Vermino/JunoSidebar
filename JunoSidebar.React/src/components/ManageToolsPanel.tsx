// File: JunoSidebar.React\src\components\ManageToolsPanel.tsx

import React, { useState, useEffect } from 'react';
import {
    X,
    CheckCircle,
    AlertCircle,
    Info,
    ExternalLink,
    ToggleLeft,
    ToggleRight,
    Settings,
    RefreshCw
} from 'lucide-react';
import WpfBridge from '../bridge/WpfBridge';

interface ManageToolsPanelProps {
    isOpen: boolean;
    onClose: () => void;
}

interface ToolPermission {
    name: string;
    displayName: string;
    description: string;
    isGranted: boolean;
}

interface Tool {
    id: string;
    name: string;
    description: string;
    version: string;
    enabled: boolean;
    requiredPermissions: string[];
    hasPermission: boolean;
}

const ManageToolsPanel: React.FC<ManageToolsPanelProps> = ({ isOpen, onClose }) => {
    const [tools, setTools] = useState<Tool[]>([]);
    const [selectedTool, setSelectedTool] = useState<Tool | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [permissions, setPermissions] = useState<{ [key: string]: ToolPermission }>({});

    useEffect(() => {
        if (isOpen) {
            fetchTools();
            fetchPermissions();
        }
    }, [isOpen]);

    const fetchTools = () => {
        setLoading(true);
        setError(null);
        WpfBridge.sendMessage('manageTools');
    };

    const fetchPermissions = () => {
        WpfBridge.sendMessage('getPermissions');
    };

    useEffect(() => {
        const unsubscribe = WpfBridge.on('toolsData', (data: any) => {
            if (data && Array.isArray(data.tools)) {
                console.log('Received tools:', data.tools);
                setTools(data.tools);
                if (data.tools.length > 0 && !selectedTool) {
                    setSelectedTool(data.tools[0]);
                }
                setLoading(false);
            }
        });

        const permissionsUnsubscribe = WpfBridge.on('permissionsData', (data: any) => {
            if (data && data.permissions) {
                console.log('Received permissions:', data.permissions);
                setPermissions(data.permissions);
            }
        });

        const errorUnsubscribe = WpfBridge.on('toolsError', (data: any) => {
            if (data && data.error) {
                setError(data.error);
                setLoading(false);
            }
        });

        return () => {
            unsubscribe();
            permissionsUnsubscribe();
            errorUnsubscribe();
        };
    }, [selectedTool]);

    const handleToolToggle = (toolId: string, enabled: boolean) => {
        WpfBridge.sendMessage('toggleTool', { id: toolId, enabled });
        setTools(prevTools =>
            prevTools.map(tool =>
                tool.id === toolId ? { ...tool, enabled } : tool
            )
        );
    };

    const handleRequestPermission = (permissionName: string) => {
        WpfBridge.sendMessage('requestPermission', { name: permissionName });
    };

    const handleRefresh = () => {
        fetchTools();
    };

    if (!isOpen) return null;

    return (
        <div className="fixed inset-0 bg-black bg-opacity-30 flex justify-end z-50 animate-slide-in">
            <div className="bg-white w-full max-w-2xl h-full flex flex-col shadow-lg overflow-hidden">
                {/* Header */}
                <div className="flex items-center justify-between px-4 py-3 border-b">
                    <h2 className="text-lg font-semibold">Manage Tools</h2>
                    <button
                        className="text-gray-500 hover:text-gray-700 p-1 rounded-full hover:bg-gray-100"
                        onClick={onClose}
                    >
                        <X size={20} />
                    </button>
                </div>

                {/* Content */}
                <div className="flex flex-1 overflow-hidden">
                    {/* Tool List */}
                    <div className="w-2/5 border-r overflow-y-auto">
                        <div className="p-3 border-b flex items-center justify-between">
                            <h3 className="font-medium">Available Tools</h3>
                            <button
                                className="p-1 rounded hover:bg-gray-100 text-gray-500"
                                onClick={handleRefresh}
                                title="Refresh tools"
                            >
                                <RefreshCw size={16} />
                            </button>
                        </div>
                        {loading ? (
                            <div className="p-4 text-center text-gray-500">
                                <p>Loading tools...</p>
                            </div>
                        ) : error ? (
                            <div className="p-4 text-center text-red-500">
                                <p>{error}</p>
                                <button
                                    className="mt-2 px-3 py-1 bg-blue-100 text-blue-600 rounded text-sm"
                                    onClick={handleRefresh}
                                >
                                    Retry
                                </button>
                            </div>
                        ) : (
                            <div>
                                {tools.map(tool => (
                                    <div
                                        key={tool.id}
                                        className={`p-3 border-b cursor-pointer flex items-center justify-between ${
                                            selectedTool?.id === tool.id ? 'bg-blue-50' : 'hover:bg-gray-50'
                                        }`}
                                        onClick={() => setSelectedTool(tool)}
                                    >
                                        <div className="flex-1">
                                            <div className="font-medium text-sm">{tool.name}</div>
                                            <div className="text-xs text-gray-500 truncate">{tool.description}</div>
                                        </div>
                                        <div className="ml-2 flex items-center">
                                            {tool.hasPermission ? (
                                                <CheckCircle size={16} className="text-green-500" />
                                            ) : (
                                                <AlertCircle size={16} className="text-orange-500" />
                                            )}
                                            <button
                                                className="ml-2"
                                                onClick={(e) => {
                                                    e.stopPropagation();
                                                    handleToolToggle(tool.id, !tool.enabled);
                                                }}
                                            >
                                                {tool.enabled ? (
                                                    <ToggleRight className="text-blue-500" size={20} />
                                                ) : (
                                                    <ToggleLeft className="text-gray-400" size={20} />
                                                )}
                                            </button>
                                        </div>
                                    </div>
                                ))}
                                {tools.length === 0 && (
                                    <div className="p-4 text-center text-gray-500">
                                        <p>No tools available</p>
                                    </div>
                                )}
                            </div>
                        )}
                    </div>

                    {/* Tool Details */}
                    <div className="w-3/5 overflow-y-auto">
                        {selectedTool ? (
                            <div className="p-4">
                                <div className="mb-4">
                                    <h3 className="text-lg font-semibold">{selectedTool.name}</h3>
                                    <p className="text-sm text-gray-600">{selectedTool.description}</p>
                                    <div className="mt-2 text-xs text-gray-500">Version: {selectedTool.version}</div>
                                </div>

                                <div className="border-t pt-4 mt-4">
                                    <div className="flex items-center justify-between mb-3">
                                        <span className="font-medium">Enable Tool</span>
                                        <button
                                            className="focus:outline-none"
                                            onClick={() => handleToolToggle(selectedTool.id, !selectedTool.enabled)}
                                        >
                                            {selectedTool.enabled ? (
                                                <ToggleRight className="text-blue-500" size={24} />
                                            ) : (
                                                <ToggleLeft className="text-gray-400" size={24} />
                                            )}
                                        </button>
                                    </div>

                                    <div className="mb-4">
                                        <p className="text-sm text-gray-600">
                                            {selectedTool.enabled
                                                ? "This tool is enabled and can be used by the assistant."
                                                : "This tool is disabled and will not be used by the assistant."}
                                        </p>
                                    </div>

                                    {selectedTool.requiredPermissions.length > 0 && (
                                        <div className="mt-6">
                                            <h4 className="font-medium mb-2">Required Permissions</h4>
                                            <div className="space-y-3 mt-3">
                                                {selectedTool.requiredPermissions.map(permissionName => {
                                                    const permission = permissions[permissionName];
                                                    return (
                                                        <div key={permissionName} className="bg-gray-50 rounded p-3">
                                                            <div className="flex items-center justify-between">
                                                                <div className="font-medium text-sm">
                                                                    {permission
                                                                        ? permission.displayName
                                                                        : permissionName}
                                                                </div>
                                                                {permission ? (
                                                                    permission.isGranted ? (
                                                                        <span className="px-2 py-1 bg-green-100 text-green-600 text-xs rounded-full">
                                                                            Granted
                                                                        </span>
                                                                    ) : (
                                                                        <button
                                                                            className="px-2 py-1 bg-blue-100 text-blue-600 text-xs rounded"
                                                                            onClick={() => handleRequestPermission(permissionName)}
                                                                        >
                                                                            Request
                                                                        </button>
                                                                    )
                                                                ) : (
                                                                    <span className="px-2 py-1 bg-orange-100 text-orange-600 text-xs rounded-full">
                                                                        Unknown
                                                                    </span>
                                                                )}
                                                            </div>
                                                            {permission && (
                                                                <p className="text-xs text-gray-600 mt-1">
                                                                    {permission.description}
                                                                </p>
                                                            )}
                                                        </div>
                                                    );
                                                })}
                                            </div>
                                        </div>
                                    )}

                                    <div className="mt-6">
                                        <button
                                            className="px-3 py-2 border rounded hover:bg-gray-50 text-sm flex items-center"
                                            onClick={() => WpfBridge.sendMessage('configureToolSettings', { id: selectedTool.id })}
                                        >
                                            <Settings size={16} className="mr-2" />
                                            Configure Tool Settings
                                        </button>
                                    </div>
                                </div>
                            </div>
                        ) : (
                            <div className="p-6 flex flex-col items-center justify-center h-full text-gray-500">
                                <Info size={48} className="mb-4 text-gray-300" />
                                <p>Select a tool to view its details</p>
                                {tools.length === 0 && !loading && (
                                    <button
                                        className="mt-4 px-4 py-2 bg-blue-100 text-blue-600 rounded"
                                        onClick={handleRefresh}
                                    >
                                        Refresh Tools
                                    </button>
                                )}
                            </div>
                        )}
                    </div>
                </div>

                {/* Footer */}
                <div className="border-t p-4 flex items-center justify-between">
                    <span className="text-xs text-gray-500">
                        Tools extend Juno's capabilities to interact with external systems.
                    </span>
                    <button
                        className="px-3 py-1 bg-gray-100 text-gray-600 rounded text-sm flex items-center"
                        onClick={() => WpfBridge.sendMessage('openToolDocumentation')}
                    >
                        <ExternalLink size={14} className="mr-1" />
                        Documentation
                    </button>
                </div>
            </div>
        </div>
    );
};

export default ManageToolsPanel;