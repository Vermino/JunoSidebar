// File: JunoSidebar.React\src\components\JunoSidebar.tsx

import React, { useState, useEffect, useCallback } from 'react';
import { ChevronRight, ChevronLeft, Calendar, Mail, FileText, Database, BarChart2, Settings, CheckCircle, Hammer } from 'lucide-react';
import WpfBridge from '../bridge/WpfBridge';
import PersonalitySelector from './PersonalitySelector';
import VoiceControls from './VoiceControls';
import SettingsPanel from './SettingsPanel';
import ManageToolsPanel from './ManageToolsPanel';

const JunoSidebar: React.FC = () => {
    const [activeState, setActiveState] = useState<'idle' | 'listening' | 'processing' | 'responding'>('idle');
    const [expanded, setExpanded] = useState(false);
    const [currentQuery, setCurrentQuery] = useState('');
    const [currentResponse, setCurrentResponse] = useState('');
    const [showNotification, setShowNotification] = useState(false);
    const [settingsPanelOpen, setSettingsPanelOpen] = useState(false);
    const [manageToolsPanelOpen, setManageToolsPanelOpen] = useState(false);

    const handleToggleExpanded = useCallback(() => {
        const newExpandedState = !expanded;
        setExpanded(newExpandedState);
        WpfBridge.setExpanded(newExpandedState);
    }, [expanded]);

    useEffect(() => {
        const unsubscribe = WpfBridge.on('setExpanded', (data: any) => {
            if (data && typeof data.expanded === 'boolean') {
                setExpanded(data.expanded);
            }
        });
        return unsubscribe;
    }, []);

    useEffect(() => {
        const unsubscribe = WpfBridge.on('init', (data: any) => {
            if (data && typeof data.expanded === 'boolean') {
                setExpanded(data.expanded);
            }
        });
        return unsubscribe;
    }, []);

    useEffect(() => {
        const unsubscribe = WpfBridge.on('showSettings', () => {
            setSettingsPanelOpen(true);
        });
        return unsubscribe;
    }, []);

    useEffect(() => {
        const unsubscribe = WpfBridge.on('showManageTools', () => {
            setManageToolsPanelOpen(true);
        });
        return unsubscribe;
    }, []);

    useEffect(() => {
        const queryUnsubscribe = WpfBridge.on('queryUpdate', (data: any) => {
            if (data && data.query) {
                setCurrentQuery(data.query);
            }
        });
        const responseUnsubscribe = WpfBridge.on('responseUpdate', (data: any) => {
            if (data) {
                if (data.response !== undefined) {
                    setCurrentResponse(data.response);
                }
                if (data.state !== undefined) {
                    setActiveState(data.state);
                }
            }
        });
        return () => {
            queryUnsubscribe();
            responseUnsubscribe();
        };
    }, []);

    const runDemo = useCallback(async () => {
        setActiveState('listening');
        setCurrentQuery('Hey Juno, what meetings do I have today?');
        await new Promise(r => setTimeout(r, 2000));
        setActiveState('processing');
        await new Promise(r => setTimeout(r, 1500));
        setActiveState('responding');
        setExpanded(true);
        WpfBridge.setExpanded(true);
        setCurrentResponse("You have 3 meetings scheduled for today: Team Standup at 10:00 AM, Product Review at 2:00 PM, and Client Call with Acme Inc. at 4:30 PM.");
        setShowNotification(true);
        await new Promise(r => setTimeout(r, 6000));
        setShowNotification(false);
        await new Promise(r => setTimeout(r, 2000));
        setActiveState('idle');
        setCurrentQuery('');
        setCurrentResponse('');
    }, []);

    const tools = [
        { name: 'Calendar', icon: <Calendar size={16} /> },
        { name: 'Email', icon: <Mail size={16} /> },
        { name: 'Files', icon: <FileText size={16} /> },
        { name: 'Database', icon: <Database size={16} /> },
        { name: 'Analytics', icon: <BarChart2 size={16} /> },
    ];

    const meetings = [
        { time: '10:00 AM', title: 'Team Standup', duration: '30m' },
        { time: '2:00 PM', title: 'Product Review', duration: '1h' },
        { time: '4:30 PM', title: 'Client Call: Acme Inc.', duration: '45m' },
    ];

    const handleOpenManageTools = () => {
        setManageToolsPanelOpen(true);
    };

    return (
        <div className="h-screen flex">
            <div className={`bg-white border-l shadow-lg transition-all duration-300 ease-in-out flex flex-col h-full ${
                expanded ? 'w-80' : 'w-16'
            }`}>
                {/* Header */}
                <div className="h-16 border-b flex items-center px-4 justify-between">
                    {expanded ? (
                        <>
                            <h2 className="font-semibold">Juno Assistant</h2>
                            <button
                                className="p-1 rounded-full hover:bg-gray-100 text-gray-400"
                                onClick={handleToggleExpanded}
                            >
                                <ChevronRight size={16} />
                            </button>
                        </>
                    ) : (
                        <button
                            className="p-2 rounded-full hover:bg-gray-100 text-gray-400 mx-auto"
                            onClick={handleToggleExpanded}
                        >
                            <ChevronLeft size={16} />
                        </button>
                    )}
                </div>

                {/* Personality Selector */}
                <PersonalitySelector expanded={expanded} />

                {/* Voice Controls */}
                <div className={`border-b ${expanded ? '' : 'py-4 px-2'}`}>
                    <VoiceControls
                        expanded={expanded}
                        activeState={activeState}
                        onStateChange={setActiveState}
                    />
                    {expanded && (
                        <div className="px-4 pb-4">
                            {currentQuery && (
                                <div className="mb-3 p-2 bg-gray-50 rounded text-sm">
                                    <p className="text-gray-700">{currentQuery}</p>
                                </div>
                            )}
                            {currentResponse && (
                                <div className="p-2 bg-green-50 rounded text-sm border-l-2 border-green-500">
                                    <p className="text-gray-700">{currentResponse}</p>
                                </div>
                            )}
                            {activeState === 'idle' && !currentQuery && (
                                <div className="text-center mt-3">
                                    <button
                                        className="px-4 py-2 bg-blue-500 text-white rounded-full text-sm"
                                        onClick={runDemo}
                                    >
                                        Run Demo
                                    </button>
                                </div>
                            )}
                        </div>
                    )}
                </div>

                {/* Quick Tools */}
                {expanded ? (
                    <div className="p-4 border-b">
                        <div className="flex justify-between items-center mb-3">
                            <h3 className="text-xs font-semibold text-gray-500">QUICK TOOLS</h3>
                            <button
                                className="text-xs text-blue-500 flex items-center"
                                onClick={handleOpenManageTools}
                            >
                                Manage Tools
                            </button>
                        </div>
                        <div className="grid grid-cols-3 gap-2">
                            {tools.map((tool, index) => (
                                <button
                                    key={index}
                                    className="p-2 border rounded hover:bg-gray-50 flex flex-col items-center justify-center"
                                    onClick={() => WpfBridge.sendMessage('executeTool', { id: tool.name.toLowerCase() })}
                                >
                                    <div className="text-blue-500 mb-1">{tool.icon}</div>
                                    <span className="text-xs">{tool.name}</span>
                                </button>
                            ))}
                        </div>
                    </div>
                ) : (
                    <div className="py-3 px-2 border-b flex flex-col items-center gap-4">
                        {tools.slice(0, 3).map((tool, index) => (
                            <button
                                key={index}
                                className="p-2 rounded-full hover:bg-gray-100 text-gray-500"
                                title={tool.name}
                                onClick={() => WpfBridge.sendMessage('executeTool', { id: tool.name.toLowerCase() })}
                            >
                                {tool.icon}
                            </button>
                        ))}
                        <button
                            className="p-2 rounded-full hover:bg-gray-100 text-blue-500"
                            title="Manage Tools"
                            onClick={handleOpenManageTools}
                        >
                            <Hammer size={16} />
                        </button>
                    </div>
                )}

                {/* Today's Schedule */}
                {expanded && (
                    <div className="p-4 border-b">
                        <div className="flex justify-between items-center mb-3">
                            <h3 className="text-xs font-semibold text-gray-500">TODAY'S SCHEDULE</h3>
                            <button
                                className="text-xs text-blue-500"
                                onClick={() => WpfBridge.sendMessage('openCalendar')}
                            >
                                View All
                            </button>
                        </div>
                        <div className="space-y-2">
                            {meetings.map((meeting, index) => (
                                <div key={index} className="flex items-center p-2 hover:bg-gray-50 rounded">
                                    <div className="p-1.5 bg-blue-100 rounded mr-3">
                                        <Calendar size={14} className="text-blue-500" />
                                    </div>
                                    <div className="flex-1">
                                        <p className="text-sm font-medium">{meeting.title}</p>
                                        <div className="text-xs text-gray-500 flex gap-2">
                                            <span>{meeting.time}</span>
                                            <span>•</span>
                                            <span>{meeting.duration}</span>
                                        </div>
                                    </div>
                                </div>
                            ))}
                        </div>
                    </div>
                )}

                {/* Footer */}
                <div className={`mt-auto ${expanded ? 'p-4 border-t' : 'py-3 px-2 border-t'}`}>
                    {expanded ? (
                        <div className="flex justify-between">
                            <button
                                className="p-2 rounded hover:bg-gray-100 text-gray-500"
                                onClick={() => setSettingsPanelOpen(true)}
                            >
                                <Settings size={18} />
                            </button>
                            <p className="text-xs text-gray-400 self-center">Juno v1.0.0</p>
                            <div className="w-6"></div>
                        </div>
                    ) : (
                        <button
                            className="p-2 rounded-full hover:bg-gray-100 text-gray-500 mx-auto block"
                            title="Settings"
                            onClick={() => {
                                if (!expanded) {
                                    setExpanded(true);
                                    WpfBridge.setExpanded(true);
                                }
                                setSettingsPanelOpen(true);
                            }}
                        >
                            <Settings size={16} />
                        </button>
                    )}
                </div>
            </div>

            {/* Notification */}
            {showNotification && (
                <div className="fixed top-6 right-6 bg-white rounded-lg shadow-lg p-4 max-w-sm animate-slide-in">
                    <div className="flex">
                        <CheckCircle size={20} className="text-green-500 mt-0.5 mr-3 flex-shrink-0" />
                        <div>
                            <h3 className="font-medium">Meetings Retrieved</h3>
                            <p className="text-sm text-gray-600 mt-1">
                                3 meetings found for today
                            </p>
                            <div className="mt-3 flex justify-between">
                                <button
                                    className="text-sm text-blue-500"
                                    onClick={() => WpfBridge.sendMessage('openCalendar')}
                                >
                                    Open Calendar
                                </button>
                                <button
                                    className="text-xs text-gray-400"
                                    onClick={() => setShowNotification(false)}
                                >
                                    Dismiss
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            )}

            {/* Settings Panel */}
            <SettingsPanel
                isOpen={settingsPanelOpen}
                onClose={() => setSettingsPanelOpen(false)}
            />

            {/* Manage Tools Panel */}
            <ManageToolsPanel
                isOpen={manageToolsPanelOpen}
                onClose={() => setManageToolsPanelOpen(false)}
            />
        </div>
    );
};

export default JunoSidebar;