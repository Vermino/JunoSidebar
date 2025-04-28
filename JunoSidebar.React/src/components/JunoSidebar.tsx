// File: JunoSidebar.React/src/components/JunoSidebar.tsx
import React, { useState, useEffect, useCallback } from 'react';
import { Mic, X, ChevronRight, ChevronLeft, Calendar, Mail, FileText, Database, BarChart2, Clock, Settings, CheckCircle, Search, AlertCircle } from 'lucide-react';
import WpfBridge from '../bridge/WpfBridge';

/**
 * JunoSidebar - Main sidebar component that communicates with the WPF host
 */
const JunoSidebar: React.FC = () => {
    // State
    const [activeState, setActiveState] = useState<'idle' | 'listening' | 'processing' | 'responding'>('idle');
    const [expanded, setExpanded] = useState(false);
    const [audioLevels, setAudioLevels] = useState(Array(20).fill(0));
    const [currentQuery, setCurrentQuery] = useState('');
    const [currentResponse, setCurrentResponse] = useState('');
    const [showNotification, setShowNotification] = useState(false);

    // Handle toggle sidebar expansion
    const handleToggleExpanded = useCallback(() => {
        const newExpandedState = !expanded;
        setExpanded(newExpandedState);

        // Notify WPF of state change
        WpfBridge.setExpanded(newExpandedState);
    }, [expanded]);

    // Listen for WPF messages about expansion state
    useEffect(() => {
        const unsubscribe = WpfBridge.on('setExpanded', (data: any) => {
            if (data && typeof data.expanded === 'boolean') {
                setExpanded(data.expanded);
            }
        });

        // Cleanup
        return unsubscribe;
    }, []);

    // Listen for resize messages
    useEffect(() => {
        const unsubscribe = WpfBridge.on('resize', (data: any) => {
            // Handle resize events if needed - we don't need to do anything here
            // as the WPF host handles the actual resizing, but we could react to it
        });

        return unsubscribe;
    }, []);

    // Listen for initialize message from WPF
    useEffect(() => {
        const unsubscribe = WpfBridge.on('init', (data: any) => {
            if (data && typeof data.expanded === 'boolean') {
                setExpanded(data.expanded);
            }
        });

        return unsubscribe;
    }, []);

    // Listen for showSettings message from WPF
    useEffect(() => {
        const unsubscribe = WpfBridge.on('showSettings', () => {
            // Show settings UI here
            alert('Settings functionality would be implemented here');
        });

        return unsubscribe;
    }, []);

    // Simulate audio level changes when in listening state
    useEffect(() => {
        if (activeState === 'listening') {
            const interval = setInterval(() => {
                setAudioLevels(prev =>
                    prev.map(() => Math.random() * 0.8 + 0.2)
                );
            }, 100);
            return () => clearInterval(interval);
        } else {
            setAudioLevels(Array(20).fill(0));
        }
    }, [activeState]);

    // Demo function to simulate voice assistant interaction
    const runDemo = useCallback(async () => {
        // Set query
        setActiveState('listening');
        setCurrentQuery('Hey Juno, what meetings do I have today?');
        await new Promise(r => setTimeout(r, 2000));

        // Processing
        setActiveState('processing');
        await new Promise(r => setTimeout(r, 1500));

        // Responding
        setActiveState('responding');
        setExpanded(true);
        WpfBridge.setExpanded(true);
        setCurrentResponse("You have 3 meetings scheduled for today: Team Standup at 10:00 AM, Product Review at 2:00 PM, and Client Call with Acme Inc. at 4:30 PM.");
        setShowNotification(true);

        // Reset after delay
        await new Promise(r => setTimeout(r, 6000));
        setShowNotification(false);
        await new Promise(r => setTimeout(r, 2000));
        setActiveState('idle');
        setCurrentQuery('');
        setCurrentResponse('');
    }, []);

    // Get color based on state
    const getStateColor = () => {
        switch(activeState) {
            case 'listening': return 'blue';
            case 'processing': return 'purple';
            case 'responding': return 'green';
            default: return 'gray';
        }
    };

    // Recent activity items
    const recentActivity = [
        { time: '10:45 AM', action: 'Created spreadsheet from meeting notes', icon: <FileText size={14} /> },
        { time: '9:30 AM', action: 'Set reminder for 2:00 PM meeting', icon: <Clock size={14} /> },
        { time: 'Yesterday', action: 'Generated Q1 sales report summary', icon: <BarChart2 size={14} /> },
    ];

    // Tool shortcuts
    const tools = [
        { name: 'Calendar', icon: <Calendar size={16} /> },
        { name: 'Email', icon: <Mail size={16} /> },
        { name: 'Files', icon: <FileText size={16} /> },
        { name: 'Database', icon: <Database size={16} /> },
        { name: 'Analytics', icon: <BarChart2 size={16} /> },
    ];

    // Today's meetings
    const meetings = [
        { time: '10:00 AM', title: 'Team Standup', duration: '30m' },
        { time: '2:00 PM', title: 'Product Review', duration: '1h' },
        { time: '4:30 PM', title: 'Client Call: Acme Inc.', duration: '45m' },
    ];

    return (
        <div className="h-screen flex">
            {/* Juno Sidebar */}
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

                {/* Voice Assistant Section */}
                <div className={`border-b ${expanded ? 'p-4' : 'py-4 px-2'}`}>
                    {expanded ? (
                        <div>
                            <div className="mb-4 text-center">
                                <div className="mb-2">
                                    <div className={`w-16 h-16 rounded-full mx-auto flex items-center justify-center bg-${getStateColor()}-100`}>
                                        <Mic size={24} className={`text-${getStateColor()}-500`} />
                                    </div>
                                </div>
                                <p className={`text-${getStateColor()}-500 font-medium text-sm`}>
                                    {activeState === 'idle' ? 'Say "Hey Juno"' :
                                        activeState === 'listening' ? 'Listening...' :
                                            activeState === 'processing' ? 'Processing...' : 'Speaking...'}
                                </p>
                            </div>

                            {/* Audio visualization */}
                            {activeState !== 'idle' && (
                                <div className="h-8 flex items-end justify-center gap-1 mb-4">
                                    {audioLevels.map((level, i) => (
                                        <div
                                            key={i}
                                            className={`w-1 bg-${getStateColor()}-400 rounded-full transition-all duration-100`}
                                            style={{ height: `${level * 100}%` }}
                                        />
                                    ))}
                                </div>
                            )}

                            {/* Query and response */}
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
                    ) : (
                        <div className="flex flex-col items-center">
                            <div
                                className={`w-10 h-10 rounded-full flex items-center justify-center mb-1 bg-${getStateColor()}-100`}
                                onClick={runDemo}
                            >
                                <Mic size={20} className={`text-${getStateColor()}-500`} />
                            </div>
                            <div className={`w-2 h-2 rounded-full bg-${getStateColor()}-500`}></div>
                        </div>
                    )}
                </div>

                {/* Tools Section */}
                {expanded ? (
                    <div className="p-4 border-b">
                        <h3 className="text-xs font-semibold text-gray-500 mb-3">QUICK TOOLS</h3>
                        <div className="grid grid-cols-3 gap-2">
                            {tools.map((tool, index) => (
                                <button
                                    key={index}
                                    className="p-2 border rounded hover:bg-gray-50 flex flex-col items-center justify-center"
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
                            >
                                {tool.icon}
                            </button>
                        ))}
                    </div>
                )}

                {/* Today's Schedule - Only shown when expanded */}
                {expanded && (
                    <div className="p-4 border-b">
                        <div className="flex justify-between items-center mb-3">
                            <h3 className="text-xs font-semibold text-gray-500">TODAY'S SCHEDULE</h3>
                            <button className="text-xs text-blue-500">View All</button>
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

                {/* Recent Activity - Only shown when expanded */}
                {expanded && (
                    <div className="p-4 border-b flex-1 overflow-y-auto">
                        <div className="flex justify-between items-center mb-3">
                            <h3 className="text-xs font-semibold text-gray-500">RECENT ACTIVITY</h3>
                            <button className="text-xs text-blue-500">Clear</button>
                        </div>

                        <div className="space-y-3">
                            {recentActivity.map((activity, index) => (
                                <div key={index} className="flex items-start">
                                    <div className="p-1.5 bg-green-100 rounded mr-3 mt-0.5">
                                        <div className="text-green-500">{activity.icon}</div>
                                    </div>
                                    <div className="flex-1">
                                        <p className="text-sm">{activity.action}</p>
                                        <p className="text-xs text-gray-500">{activity.time}</p>
                                    </div>
                                </div>
                            ))}
                        </div>
                    </div>
                )}

                {/* Footer */}
                <div className={expanded ? 'p-4 border-t' : 'py-3 px-2 border-t'}>
                    {expanded ? (
                        <div className="flex justify-between">
                            <button className="p-2 rounded hover:bg-gray-100 text-gray-500">
                                <Settings size={18} />
                            </button>
                            <p className="text-xs text-gray-400 self-center">Juno v1.0.3</p>
                            <div className="w-6"></div>
                        </div>
                    ) : (
                        <button
                            className="p-2 rounded-full hover:bg-gray-100 text-gray-500 mx-auto block"
                            title="Settings"
                        >
                            <Settings size={16} />
                        </button>
                    )}
                </div>
            </div>

            {/* Notification when active */}
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
                                <button className="text-sm text-blue-500">
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
        </div>
    );
};

export default JunoSidebar;