// File: JunoSidebar/JunoSidebar.React/src/components/VoiceControls.tsx
import React, { useState, useEffect } from 'react';
import { Mic, MicOff, Volume2, VolumeX, Power, PowerOff } from 'lucide-react';
import WpfBridge from '../bridge/WpfBridge';

interface VoiceControlsProps {
    expanded: boolean;
    activeState: 'idle' | 'listening' | 'processing' | 'responding';
    onStateChange: (state: 'idle' | 'listening' | 'processing' | 'responding') => void;
}

const VoiceControls: React.FC<VoiceControlsProps> = ({
                                                         expanded,
                                                         activeState,
                                                         onStateChange
                                                     }) => {
    const [voiceInputEnabled, setVoiceInputEnabled] = useState(true);
    const [voiceOutputEnabled, setVoiceOutputEnabled] = useState(true);
    const [audioLevels, setAudioLevels] = useState<number[]>(Array(20).fill(0));
    const [isOnline, setIsOnline] = useState(true);

    useEffect(() => {
        // Request voice settings from backend
        WpfBridge.sendMessage('getVoiceSettings');

        // Listen for voice settings updates
        const settingsUnsubscribe = WpfBridge.on('voiceSettings', (data: any) => {
            if (data) {
                setVoiceInputEnabled(data.inputEnabled);
                setVoiceOutputEnabled(data.outputEnabled);
            }
        });

        // Listen for audio level updates - these are real microphone levels from the backend
        const audioLevelsUnsubscribe = WpfBridge.on('audioLevels', (data: any) => {
            if (data && Array.isArray(data.levels)) {
                setAudioLevels(data.levels);
            }
        });

        // Listen for state changes from the backend
        const stateChangeUnsubscribe = WpfBridge.on('assistantStateChanged', (data: any) => {
            if (data && data.state) {
                onStateChange(data.state as 'idle' | 'listening' | 'processing' | 'responding');
            }
        });

        return () => {
            settingsUnsubscribe();
            audioLevelsUnsubscribe();
            stateChangeUnsubscribe();
        };
    }, [onStateChange]);

    const toggleOnlineStatus = () => {
        const newStatus = !isOnline;
        setIsOnline(newStatus);

        // Send power on/off message to backend
        if (newStatus) {
            // Power on - start always-on wake word detection
            WpfBridge.sendMessage('powerOn');
            onStateChange('idle');
        } else {
            // Power off - stop all listening
            WpfBridge.sendMessage('powerOff');
            onStateChange('idle');
        }
    };

    const handleMicToggle = () => {
        if (!isOnline) return;

        if (activeState === 'listening') {
            // If we're already listening, stop listening
            WpfBridge.sendMessage('stopListening');
            onStateChange('idle');
        } else if (activeState === 'idle') {
            // Start listening
            WpfBridge.sendMessage('startListening');
            onStateChange('listening');
        }
    };

    const handleVoiceOutputToggle = () => {
        const newState = !voiceOutputEnabled;
        setVoiceOutputEnabled(newState);
        WpfBridge.sendMessage('setVoiceOutput', { enabled: newState });
    };

    const handleStopResponding = () => {
        if (activeState === 'responding') {
            WpfBridge.sendMessage('stopResponding');
            onStateChange('idle');
        }
    };

    const getStateColor = () => {
        if (!isOnline) return 'gray';

        switch (activeState) {
            case 'listening': return 'blue';
            case 'processing': return 'purple';
            case 'responding': return 'green';
            default: return 'gray';
        }
    };

    if (!expanded) {
        // Collapsed view - just show a simple icon with the current state
        return (
            <div className="flex justify-center py-2">
                <div
                    className={`w-10 h-10 rounded-full flex items-center justify-center cursor-pointer bg-${getStateColor()}-100`}
                    onClick={isOnline ? handleMicToggle : toggleOnlineStatus}
                >
                    {!isOnline ? (
                        <PowerOff size={20} className="text-gray-500" />
                    ) : (
                        activeState === 'listening' ? (
                            <MicOff size={20} className={`text-${getStateColor()}-500`} />
                        ) : (
                            <Mic size={20} className={`text-${getStateColor()}-500`} />
                        )
                    )}
                </div>
            </div>
        );
    }

    return (
        <div className="p-4">
            <div className="mb-4 text-center">
                <div className="mb-2">
                    <div
                        className={`w-16 h-16 rounded-full mx-auto flex items-center justify-center ${
                            isOnline ? `bg-${getStateColor()}-100` : 'bg-gray-100'
                        } cursor-pointer`}
                        onClick={isOnline ? handleMicToggle : toggleOnlineStatus}
                    >
                        {!isOnline ? (
                            <PowerOff size={24} className="text-gray-500" />
                        ) : activeState === 'responding' ? (
                            <Power size={24} className={`text-${getStateColor()}-500`} onClick={handleStopResponding} />
                        ) : activeState === 'listening' ? (
                            <MicOff size={24} className={`text-${getStateColor()}-500`} />
                        ) : (
                            <Mic size={24} className={`text-${getStateColor()}-500`} />
                        )}
                    </div>
                </div>
                <p className={`font-medium text-sm ${
                    isOnline ? `text-${getStateColor()}-500` : 'text-gray-500'
                }`}>
                    {!isOnline ? 'Offline' :
                        activeState === 'idle' ? 'Say "Hey Juno"' :
                            activeState === 'listening' ? 'Listening...' :
                                activeState === 'processing' ? 'Processing...' : 'Speaking...'}
                </p>
            </div>

            {/* Audio level visualization - now showing real microphone levels from backend */}
            <div className="h-8 flex items-end justify-center gap-1 mb-4">
                {audioLevels.map((level, i) => (
                    <div
                        key={i}
                        className={`w-1 ${
                            isOnline
                                ? `bg-${activeState === 'listening' ? 'blue' : getStateColor()}-400`
                                : 'bg-gray-300'
                        } rounded-full transition-all duration-100`}
                        style={{ height: `${Math.max(2, level * 100)}%` }}
                    />
                ))}
            </div>

            {/* Control buttons */}
            <div className="flex justify-center gap-2">
                <button
                    className={`p-2 rounded-full ${
                        isOnline
                            ? (voiceInputEnabled ? 'bg-gray-100 text-gray-700' : 'bg-gray-200 text-gray-400')
                            : 'bg-gray-100 text-gray-400 opacity-50'
                    }`}
                    onClick={() => {
                        if (isOnline) {
                            const newState = !voiceInputEnabled;
                            setVoiceInputEnabled(newState);
                            WpfBridge.sendMessage('setVoiceInput', { enabled: newState });
                        }
                    }}
                    title={voiceInputEnabled ? "Disable Voice Input" : "Enable Voice Input"}
                    disabled={!isOnline}
                >
                    {voiceInputEnabled ? <Mic size={20} /> : <MicOff size={20} />}
                </button>

                <button
                    className={`p-2 rounded-full ${
                        isOnline
                            ? (voiceOutputEnabled ? 'bg-gray-100 text-gray-700' : 'bg-gray-200 text-gray-400')
                            : 'bg-gray-100 text-gray-400 opacity-50'
                    }`}
                    onClick={() => {
                        if (isOnline) {
                            handleVoiceOutputToggle();
                        }
                    }}
                    title={voiceOutputEnabled ? "Disable Voice Output" : "Enable Voice Output"}
                    disabled={!isOnline}
                >
                    {voiceOutputEnabled ? <Volume2 size={20} /> : <VolumeX size={20} />}
                </button>

                <button
                    className={`p-2 rounded-full ${isOnline ? 'bg-green-100 text-green-500' : 'bg-red-100 text-red-500'}`}
                    onClick={toggleOnlineStatus}
                    title={isOnline ? "Go Offline" : "Go Online"}
                >
                    {isOnline ? <Power size={20} /> : <PowerOff size={20} />}
                </button>
            </div>
        </div>
    );
};

export default VoiceControls;