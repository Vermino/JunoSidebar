// File: JunoSidebar/JunoSidebar.React/src/components/SettingsPanel.tsx
import React, { useState, useEffect } from 'react';
import {
    X,
    Save,
    Server,
    Mic,
    Volume2,
    Shield,
    CloudLightning,
    Info,
    ChevronRight,
    RefreshCw
} from 'lucide-react';
import WpfBridge from '../bridge/WpfBridge';

interface SettingsPanelProps {
    isOpen: boolean;
    onClose: () => void;
}

interface LLMProvider {
    id: string;
    displayName: string;
}

interface LLMModel {
    id: string;
    displayName: string;
}

interface AudioDevice {
    index: number;
    name: string;
}

interface Settings {
    llm: {
        provider: string;
        model: string;
        baseUrl: string;
        apiKey: string;
        temperature: number;
        maxTokens: number;
    };
    voice: {
        inputEnabled: boolean;
        outputEnabled: boolean;
        wakeWord: string;
        provider: string;
        voice: string;
        speed: number;
        inputDeviceIndex: number;
        outputDeviceIndex: number;
    };
    ui: {
        startupBehavior: 'minimized' | 'expanded' | 'remember';
        theme: 'light' | 'dark' | 'system';
        alwaysOnTop: boolean;
    };
}

enum SettingsTab {
    LLM = 'llm',
    Voice = 'voice',
    Privacy = 'privacy',
    Tools = 'tools',
    About = 'about'
}

const SettingsPanel: React.FC<SettingsPanelProps> = ({ isOpen, onClose }) => {
    const [activeTab, setActiveTab] = useState<SettingsTab>(SettingsTab.LLM);
    const [settings, setSettings] = useState<Settings>({
        llm: {
            provider: 'lmstudio',
            model: 'local-model',
            baseUrl: 'http://localhost:1234/v1',
            apiKey: '',
            temperature: 0.7,
            maxTokens: 1024
        },
        voice: {
            inputEnabled: true,
            outputEnabled: true,
            wakeWord: 'Hey Juno',
            provider: 'system',
            voice: 'default',
            speed: 1.0,
            inputDeviceIndex: -1,
            outputDeviceIndex: -1
        },
        ui: {
            startupBehavior: 'remember',
            theme: 'light',
            alwaysOnTop: true
        }
    });

    const [providers, setProviders] = useState<LLMProvider[]>([
        { id: 'lmstudio', displayName: 'LM Studio (Local)' }
    ]);

    const [models, setModels] = useState<LLMModel[]>([
        { id: 'local-model', displayName: 'Default Local Model' }
    ]);

    const [inputDevices, setInputDevices] = useState<AudioDevice[]>([
        { index: -1, name: 'Default Device' }
    ]);

    const [outputDevices, setOutputDevices] = useState<AudioDevice[]>([
        { index: -1, name: 'Default Device' }
    ]);

    const [isSaving, setIsSaving] = useState(false);
    const [saveMessage, setSaveMessage] = useState('');
    const [connectionStatus, setConnectionStatus] = useState<'unknown' | 'success' | 'error'>('unknown');

    useEffect(() => {
        if (isOpen) {
            // Request settings from backend
            WpfBridge.sendMessage('getSettings');

            // Request LLM providers
            WpfBridge.sendMessage('getLLMProviders');

            // Request audio devices
            WpfBridge.sendMessage('getAudioDevices');
        }
    }, [isOpen]);

    useEffect(() => {
        // Listen for settings data
        const settingsUnsubscribe = WpfBridge.on('settingsData', (data: any) => {
            if (data.settings) {
                setSettings(data.settings);
            }
        });

        // Listen for LLM providers
        const providersUnsubscribe = WpfBridge.on('llmProvidersData', (data: any) => {
            if (data.providers && Array.isArray(data.providers)) {
                setProviders(data.providers);
            }
        });

        // Listen for LLM models
        const modelsUnsubscribe = WpfBridge.on('llmModelsData', (data: any) => {
            if (data.models && Array.isArray(data.models)) {
                setModels(data.models);
            }
        });

        // Listen for audio devices
        const audioDevicesUnsubscribe = WpfBridge.on('audioDevicesData', (data: any) => {
            if (data.inputDevices && Array.isArray(data.inputDevices)) {
                setInputDevices(data.inputDevices);
            }
            if (data.outputDevices && Array.isArray(data.outputDevices)) {
                setOutputDevices(data.outputDevices);
            }
        });

        // Listen for settings save response
        const saveUnsubscribe = WpfBridge.on('settingsSaved', (data: any) => {
            setIsSaving(false);
            setSaveMessage(data.success ? 'Settings saved successfully' : 'Failed to save settings');

            setTimeout(() => {
                setSaveMessage('');
            }, 3000);
        });

        // Listen for LLM connection test results
        const connectionTestUnsubscribe = WpfBridge.on('llmConnectionTested', (data: any) => {
            setConnectionStatus(data.success ? 'success' : 'error');

            setTimeout(() => {
                setConnectionStatus('unknown');
            }, 5000);
        });

        return () => {
            settingsUnsubscribe();
            providersUnsubscribe();
            modelsUnsubscribe();
            audioDevicesUnsubscribe();
            saveUnsubscribe();
            connectionTestUnsubscribe();
        };
    }, []);

    const handleSaveSettings = () => {
        setIsSaving(true);
        WpfBridge.sendMessage('saveSettings', { settings });
    };

    const handleProviderChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
        const provider = e.target.value;
        setSettings(prev => ({
            ...prev,
            llm: {
                ...prev.llm,
                provider
            }
        }));

        // Request models for this provider
        WpfBridge.sendMessage('getLLMModels', { provider });
    };

    const handleRefreshAudioDevices = () => {
        WpfBridge.sendMessage('refreshAudioDevices');
    };

    const TabIcon = ({ tab }: { tab: SettingsTab }) => {
        switch (tab) {
            case SettingsTab.LLM:
                return <Server size={20} className="mr-2" />;
            case SettingsTab.Voice:
                return <Mic size={20} className="mr-2" />;
            case SettingsTab.Privacy:
                return <Shield size={20} className="mr-2" />;
            case SettingsTab.Tools:
                return <CloudLightning size={20} className="mr-2" />;
            case SettingsTab.About:
                return <Info size={20} className="mr-2" />;
        }
    };

    if (!isOpen) return null;

    return (
        <div className="fixed inset-0 bg-black bg-opacity-30 flex justify-end z-50 animate-slide-in">
            <div className="bg-white w-full max-w-2xl h-full flex flex-col shadow-lg overflow-hidden">
                {/* Header */}
                <div className="flex items-center justify-between px-4 py-3 border-b">
                    <h2 className="text-lg font-semibold">Settings</h2>
                    <button
                        className="text-gray-500 hover:text-gray-700 p-1 rounded-full hover:bg-gray-100"
                        onClick={onClose}
                    >
                        <X size={20} />
                    </button>
                </div>

                {/* Tabs Header */}
                <div className="border-b px-2 py-1">
                    <div className="grid grid-cols-3 gap-1">
                        {Object.values(SettingsTab).map(tab => (
                            <button
                                key={tab}
                                className={`px-3 py-2 flex items-center justify-center rounded ${
                                    activeTab === tab
                                        ? 'bg-blue-100 text-blue-600 font-medium'
                                        : 'text-gray-600 hover:text-gray-800 hover:bg-gray-50'
                                }`}
                                onClick={() => setActiveTab(tab)}
                            >
                                <TabIcon tab={tab} />
                                <span className="ml-1">{tab.charAt(0).toUpperCase() + tab.slice(1)}</span>
                            </button>
                        ))}
                    </div>
                </div>

                {/* Tab Content */}
                <div className="flex-1 overflow-y-auto p-6">
                    {activeTab === SettingsTab.LLM && (
                        <div>
                            <h3 className="text-lg font-semibold mb-4">Language Model Settings</h3>

                            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                                <div className="space-y-4">
                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1">LLM Provider</label>
                                        <select
                                            className="w-full border border-gray-300 rounded px-3 py-2 focus:outline-none focus:ring-1 focus:ring-blue-500"
                                            value={settings.llm.provider}
                                            onChange={handleProviderChange}
                                        >
                                            {providers.map(provider => (
                                                <option key={provider.id} value={provider.id}>
                                                    {provider.displayName}
                                                </option>
                                            ))}
                                        </select>
                                    </div>

                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1">Model</label>
                                        <select
                                            className="w-full border border-gray-300 rounded px-3 py-2 focus:outline-none focus:ring-1 focus:ring-blue-500"
                                            value={settings.llm.model}
                                            onChange={e => setSettings(prev => ({
                                                ...prev,
                                                llm: {
                                                    ...prev.llm,
                                                    model: e.target.value
                                                }
                                            }))}
                                        >
                                            {models.map(model => (
                                                <option key={model.id} value={model.id}>
                                                    {model.displayName}
                                                </option>
                                            ))}
                                        </select>
                                    </div>

                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1">API Base URL</label>
                                        <input
                                            type="text"
                                            className="w-full border border-gray-300 rounded px-3 py-2 focus:outline-none focus:ring-1 focus:ring-blue-500"
                                            value={settings.llm.baseUrl}
                                            onChange={e => setSettings(prev => ({
                                                ...prev,
                                                llm: {
                                                    ...prev.llm,
                                                    baseUrl: e.target.value
                                                }
                                            }))}
                                            placeholder="http://localhost:1234/v1"
                                        />
                                        <p className="text-xs text-gray-500 mt-1">
                                            For LM Studio, this is typically http://localhost:1234/v1
                                        </p>
                                    </div>
                                </div>

                                <div className="space-y-4">
                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1">API Key</label>
                                        <input
                                            type="password"
                                            className="w-full border border-gray-300 rounded px-3 py-2 focus:outline-none focus:ring-1 focus:ring-blue-500"
                                            value={settings.llm.apiKey}
                                            onChange={e => setSettings(prev => ({
                                                ...prev,
                                                llm: {
                                                    ...prev.llm,
                                                    apiKey: e.target.value
                                                }
                                            }))}
                                            placeholder="Not required for local models"
                                        />
                                    </div>

                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1">
                                            Temperature: {settings.llm.temperature.toFixed(1)}
                                        </label>
                                        <input
                                            type="range"
                                            min="0"
                                            max="1"
                                            step="0.1"
                                            className="w-full"
                                            value={settings.llm.temperature}
                                            onChange={e => setSettings(prev => ({
                                                ...prev,
                                                llm: {
                                                    ...prev.llm,
                                                    temperature: parseFloat(e.target.value)
                                                }
                                            }))}
                                        />
                                        <div className="flex justify-between text-xs text-gray-500">
                                            <span>More Focused</span>
                                            <span>More Creative</span>
                                        </div>
                                    </div>

                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1">
                                            Max Tokens: {settings.llm.maxTokens}
                                        </label>
                                        <input
                                            type="range"
                                            min="256"
                                            max="4096"
                                            step="256"
                                            className="w-full"
                                            value={settings.llm.maxTokens}
                                            onChange={e => setSettings(prev => ({
                                                ...prev,
                                                llm: {
                                                    ...prev.llm,
                                                    maxTokens: parseInt(e.target.value)
                                                }
                                            }))}
                                        />
                                        <div className="flex justify-between text-xs text-gray-500">
                                            <span>Shorter</span>
                                            <span>Longer</span>
                                        </div>
                                    </div>

                                    <div>
                                        <button
                                            className={`text-sm px-3 py-1 rounded flex items-center 
                        ${connectionStatus === 'unknown' ? 'bg-blue-100 text-blue-600 hover:bg-blue-200' :
                                                connectionStatus === 'success' ? 'bg-green-100 text-green-600' :
                                                    'bg-red-100 text-red-600'}`}
                                            onClick={() => WpfBridge.sendMessage('testLLMConnection')}
                                        >
                                            Test Connection
                                            <ChevronRight size={14} className="ml-1" />
                                        </button>
                                        {connectionStatus === 'success' && (
                                            <p className="text-xs text-green-600 mt-1">Connection successful!</p>
                                        )}
                                        {connectionStatus === 'error' && (
                                            <p className="text-xs text-red-600 mt-1">Connection failed. Check your settings.</p>
                                        )}
                                    </div>
                                </div>
                            </div>
                        </div>
                    )}

                    {activeTab === SettingsTab.Voice && (
                        <div>
                            <h3 className="text-lg font-semibold mb-4">Voice Settings</h3>

                            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                                <div className="space-y-4">
                                    <div className="flex items-center justify-between">
                                        <label className="text-sm font-medium text-gray-700">Enable Voice Input</label>
                                        <div className="relative inline-block w-10 mr-2 align-middle select-none">
                                            <input
                                                type="checkbox"
                                                id="toggle-voice-input"
                                                className="sr-only"
                                                checked={settings.voice.inputEnabled}
                                                onChange={e => setSettings(prev => ({
                                                    ...prev,
                                                    voice: {
                                                        ...prev.voice,
                                                        inputEnabled: e.target.checked
                                                    }
                                                }))}
                                            />
                                            <label
                                                htmlFor="toggle-voice-input"
                                                className={`block overflow-hidden h-6 rounded-full cursor-pointer ${
                                                    settings.voice.inputEnabled ? 'bg-blue-500' : 'bg-gray-300'
                                                }`}
                                            >
                        <span className={`absolute block w-4 h-4 rounded-full bg-white border-2 transform transition-transform duration-200 ease-in ${
                            settings.voice.inputEnabled ? 'translate-x-5 border-blue-500' : 'translate-x-0 border-gray-300'
                        }`}></span>
                                            </label>
                                        </div>
                                    </div>

                                    {settings.voice.inputEnabled && (
                                        <>
                                            <div>
                                                <label className="block text-sm font-medium text-gray-700 mb-1">Wake Word</label>
                                                <input
                                                    type="text"
                                                    className="w-full border border-gray-300 rounded px-3 py-2 focus:outline-none focus:ring-1 focus:ring-blue-500"
                                                    value={settings.voice.wakeWord}
                                                    onChange={e => setSettings(prev => ({
                                                        ...prev,
                                                        voice: {
                                                            ...prev.voice,
                                                            wakeWord: e.target.value
                                                        }
                                                    }))}
                                                    placeholder="Hey Juno"
                                                />
                                            </div>

                                            <div>
                                                <div className="flex justify-between items-center mb-1">
                                                    <label className="block text-sm font-medium text-gray-700">Input Device</label>
                                                    <button
                                                        className="text-xs text-blue-500 flex items-center"
                                                        onClick={handleRefreshAudioDevices}
                                                    >
                                                        <RefreshCw size={12} className="mr-1" />
                                                        Refresh
                                                    </button>
                                                </div>
                                                <select
                                                    className="w-full border border-gray-300 rounded px-3 py-2 focus:outline-none focus:ring-1 focus:ring-blue-500"
                                                    value={settings.voice.inputDeviceIndex}
                                                    onChange={e => setSettings(prev => ({
                                                        ...prev,
                                                        voice: {
                                                            ...prev.voice,
                                                            inputDeviceIndex: parseInt(e.target.value)
                                                        }
                                                    }))}
                                                >
                                                    {inputDevices.map(device => (
                                                        <option key={device.index} value={device.index}>
                                                            {device.name}
                                                        </option>
                                                    ))}
                                                </select>
                                            </div>
                                        </>
                                    )}
                                </div>

                                <div className="space-y-4">
                                    <div className="flex items-center justify-between">
                                        <label className="text-sm font-medium text-gray-700">Enable Voice Output</label>
                                        <div className="relative inline-block w-10 mr-2 align-middle select-none">
                                            <input
                                                type="checkbox"
                                                id="toggle-voice-output"
                                                className="sr-only"
                                                checked={settings.voice.outputEnabled}
                                                onChange={e => setSettings(prev => ({
                                                    ...prev,
                                                    voice: {
                                                        ...prev.voice,
                                                        outputEnabled: e.target.checked
                                                    }
                                                }))}
                                            />
                                            <label
                                                htmlFor="toggle-voice-output"
                                                className={`block overflow-hidden h-6 rounded-full cursor-pointer ${
                                                    settings.voice.outputEnabled ? 'bg-blue-500' : 'bg-gray-300'
                                                }`}
                                            >
                        <span className={`absolute block w-4 h-4 rounded-full bg-white border-2 transform transition-transform duration-200 ease-in ${
                            settings.voice.outputEnabled ? 'translate-x-5 border-blue-500' : 'translate-x-0 border-gray-300'
                        }`}></span>
                                            </label>
                                        </div>
                                    </div>

                                    {settings.voice.outputEnabled && (
                                        <>
                                            <div>
                                                <label className="block text-sm font-medium text-gray-700 mb-1">Voice Provider</label>
                                                <select
                                                    className="w-full border border-gray-300 rounded px-3 py-2 focus:outline-none focus:ring-1 focus:ring-blue-500"
                                                    value={settings.voice.provider}
                                                    onChange={e => setSettings(prev => ({
                                                        ...prev,
                                                        voice: {
                                                            ...prev.voice,
                                                            provider: e.target.value
                                                        }
                                                    }))}
                                                >
                                                    <option value="system">System (Default)</option>
                                                    <option value="elevenlabs">ElevenLabs</option>
                                                </select>
                                            </div>

                                            <div>
                                                <label className="block text-sm font-medium text-gray-700 mb-1">Voice</label>
                                                <select
                                                    className="w-full border border-gray-300 rounded px-3 py-2 focus:outline-none focus:ring-1 focus:ring-blue-500"
                                                    value={settings.voice.voice}
                                                    onChange={e => setSettings(prev => ({
                                                        ...prev,
                                                        voice: {
                                                            ...prev.voice,
                                                            voice: e.target.value
                                                        }
                                                    }))}
                                                >
                                                    <option value="default">Default</option>
                                                    <option value="male1">Male 1</option>
                                                    <option value="female1">Female 1</option>
                                                </select>
                                            </div>

                                            <div>
                                                <div className="flex justify-between items-center mb-1">
                                                    <label className="block text-sm font-medium text-gray-700">Output Device</label>
                                                    <button
                                                        className="text-xs text-blue-500 flex items-center"
                                                        onClick={handleRefreshAudioDevices}
                                                    >
                                                        <RefreshCw size={12} className="mr-1" />
                                                        Refresh
                                                    </button>
                                                </div>
                                                <select
                                                    className="w-full border border-gray-300 rounded px-3 py-2 focus:outline-none focus:ring-1 focus:ring-blue-500"
                                                    value={settings.voice.outputDeviceIndex}
                                                    onChange={e => setSettings(prev => ({
                                                        ...prev,
                                                        voice: {
                                                            ...prev.voice,
                                                            outputDeviceIndex: parseInt(e.target.value)
                                                        }
                                                    }))}
                                                >
                                                    {outputDevices.map(device => (
                                                        <option key={device.index} value={device.index}>
                                                            {device.name}
                                                        </option>
                                                    ))}
                                                </select>
                                            </div>

                                            <div>
                                                <label className="block text-sm font-medium text-gray-700 mb-1">
                                                    Speed: {settings.voice.speed.toFixed(1)}x
                                                </label>
                                                <input
                                                    type="range"
                                                    min="0.5"
                                                    max="2"
                                                    step="0.1"
                                                    className="w-full"
                                                    value={settings.voice.speed}
                                                    onChange={e => setSettings(prev => ({
                                                        ...prev,
                                                        voice: {
                                                            ...prev.voice,
                                                            speed: parseFloat(e.target.value)
                                                        }
                                                    }))}
                                                />
                                                <div className="flex justify-between text-xs text-gray-500">
                                                    <span>Slower</span>
                                                    <span>Faster</span>
                                                </div>
                                            </div>

                                            <div>
                                                <button
                                                    className="px-3 py-1 text-sm bg-blue-100 text-blue-600 rounded hover:bg-blue-200 flex items-center"
                                                    onClick={() => WpfBridge.sendMessage('testVoice')}
                                                >
                                                    <Volume2 size={14} className="mr-1" />
                                                    Test Voice
                                                </button>
                                            </div>
                                        </>
                                    )}
                                </div>
                            </div>
                        </div>
                    )}

                    {activeTab === SettingsTab.Privacy && (
                        <div>
                            <h3 className="text-lg font-semibold mb-4">Privacy Settings</h3>

                            <div className="space-y-6">
                                <p className="text-sm text-gray-600">
                                    Control what data Juno can access and how your information is stored.
                                </p>

                                <div className="border rounded-md p-4">
                                    <h4 className="font-medium">Local Processing</h4>
                                    <p className="text-sm text-gray-600 mt-1">
                                        Using LM Studio, all processing happens locally on your device. No data is sent to external servers.
                                    </p>
                                </div>

                                <div className="space-y-3">
                                    <button
                                        className="w-full text-left px-4 py-3 text-sm border rounded hover:bg-gray-50 flex items-center justify-between"
                                        onClick={() => WpfBridge.sendMessage('managePermissions')}
                                    >
                                        <span>Manage Tool Permissions</span>
                                        <ChevronRight size={16} />
                                    </button>

                                    <button
                                        className="w-full text-left px-4 py-3 text-sm border rounded hover:bg-gray-50 flex items-center justify-between"
                                        onClick={() => WpfBridge.sendMessage('clearConversationHistory')}
                                    >
                                        <span>Clear Conversation History</span>
                                        <ChevronRight size={16} />
                                    </button>
                                </div>
                            </div>
                        </div>
                    )}

                    {activeTab === SettingsTab.Tools && (
                        <div>
                            <h3 className="text-lg font-semibold mb-4">Tools Settings</h3>

                            <div className="space-y-6">
                                <p className="text-sm text-gray-600">
                                    Manage tools that extend Juno's capabilities.
                                </p>

                                <div className="space-y-3">
                                    <button
                                        className="w-full text-left px-4 py-3 text-sm border rounded hover:bg-gray-50 flex items-center justify-between"
                                        onClick={() => WpfBridge.sendMessage('manageTools')}
                                    >
                                        <span>Manage Tools</span>
                                        <ChevronRight size={16} />
                                    </button>

                                    <button
                                        className="w-full text-left px-4 py-3 text-sm border rounded hover:bg-gray-50 flex items-center justify-between"
                                        onClick={() => WpfBridge.sendMessage('toolDevelopmentEnvironment')}
                                    >
                                        <span>Tool Development Environment</span>
                                        <ChevronRight size={16} />
                                    </button>
                                </div>
                            </div>
                        </div>
                    )}

                    {activeTab === SettingsTab.About && (
                        <div>
                            <h3 className="text-lg font-semibold mb-4">About Juno AI Assistant</h3>

                            <div className="space-y-4 text-center max-w-md mx-auto">
                                <div className="flex justify-center mb-6">
                                    <div className="w-24 h-24 rounded-full bg-blue-100 flex items-center justify-center">
                                        <span className="text-3xl font-bold text-blue-500">J</span>
                                    </div>
                                </div>

                                <div>
                                    <h2 className="text-xl font-bold">Juno AI Assistant</h2>
                                    <p className="text-sm text-gray-600">Version 1.0.0</p>
                                </div>

                                <div className="text-sm text-gray-600">
                                    <p>A modular, extensible AI assistant with personality switching capabilities and a flexible tool system.</p>
                                </div>

                                <div>
                                    <a
                                        href="#"
                                        className="text-sm text-blue-500 hover:underline"
                                        onClick={() => WpfBridge.sendMessage('openDocumentation')}
                                    >
                                        Documentation
                                    </a>
                                </div>
                            </div>
                        </div>
                    )}
                </div>

                {/* Footer */}
                <div className="border-t p-4 flex items-center justify-between">
                    {saveMessage && (
                        <span className={`text-sm ${
                            saveMessage.includes('success') ? 'text-green-600' : 'text-red-600'
                        }`}>
              {saveMessage}
            </span>
                    )}
                    {!saveMessage && <span></span>}

                    <button
                        className="px-4 py-2 bg-blue-500 text-white rounded flex items-center disabled:bg-blue-300"
                        onClick={handleSaveSettings}
                        disabled={isSaving}
                    >
                        {isSaving ? (
                            <span>Saving...</span>
                        ) : (
                            <>
                                <Save size={16} className="mr-2" />
                                <span>Save Settings</span>
                            </>
                        )}
                    </button>
                </div>
            </div>
        </div>
    );
};

export default SettingsPanel;