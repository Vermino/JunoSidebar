// File: JunoSidebar/JunoSidebar.React/src/components/VoiceControls.tsx
import React, { useState, useEffect } from 'react';
import { Mic, MicOff, Volume2, VolumeX, StopCircle } from 'lucide-react';
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
    const [isManualListening, setIsManualListening] = useState(false);

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

        // Listen for audio level updates
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

    useEffect(() => {
        // Simulate audio levels when in listening mode
        if (activeState === 'listening') {
            const interval = setInterval(() => {
                // In a real implementation, these would come from the backend
                // For now, we'll just simulate random levels
                const simulatedLevels = Array(20).fill(0).map(() => Math.random() * 0.8 + 0.2);
                setAudioLevels(simulatedLevels);
            }, 100);

            return () => clearInterval(interval);
        } else {
            setAudioLevels(Array(20).fill(0));
        }
    }, [activeState]);

    const handleMicToggle = () => {
        if (activeState === 'listening') {
            // If we're already listening, stop listening
            WpfBridge.sendMessage('stopListening');
            onStateChange('idle');
            setIsManualListening(false);
        } else {
            // Start listening
            WpfBridge.sendMessage('startListening');
            onStateChange('listening');
            setIsManualListening(true);
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
                    onClick={handleMicToggle}
                >
                    <Mic size={20} className={`text-${getStateColor()}-500`} />
                </div>
            </div>
        );
    }

    return (
        <div className="p-4">
            <div className="mb-4 text-center">
                <div className="mb-2">
                    <div
                        className={`w-16 h-16 rounded-full mx-auto flex items-center justify-center bg-${getStateColor()}-100 cursor-pointer`}
                        onClick={handleMicToggle}
                    >
                        {activeState === 'responding' ? (
                            <StopCircle size={24} className={`text-${getStateColor()}-500`} />
                        ) : (
                            activeState === 'listening' || isManualListening ? (
                                <MicOff size={24} className={`text-${getStateColor()}-500`} />
                            ) : (
                                <Mic size={24} className={`text-${getStateColor()}-500`} />
                            )
                        )}
                    </div>
                </div>
                <p className={`text-${getStateColor()}-500 font-medium text-sm`}>
                    {activeState === 'idle' ? 'Say "Hey Juno"' :
                        activeState === 'listening' ? 'Listening...' :
                            activeState === 'processing' ? 'Processing...' : 'Speaking...'}
                </p>
            </div>

            {/* Audio level visualization */}
            {activeState === 'listening' && (
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

            {/* Control buttons */}
            <div className="flex justify-center gap-2">
                <button
                    className={`p-2 rounded-full ${voiceInputEnabled ? 'bg-gray-100 text-gray-700' : 'bg-gray-200 text-gray-400'}`}
                    onClick={() => {
                        const newState = !voiceInputEnabled;
                        setVoiceInputEnabled(newState);
                        WpfBridge.sendMessage('setVoiceInput', { enabled: newState });
                    }}
                    title={voiceInputEnabled ? "Disable Voice Input" : "Enable Voice Input"}
                >
                    {voiceInputEnabled ? <Mic size={20} /> : <MicOff size={20} />}
                </button>

                <button
                    className={`p-2 rounded-full ${voiceOutputEnabled ? 'bg-gray-100 text-gray-700' : 'bg-gray-200 text-gray-400'}`}
                    onClick={handleVoiceOutputToggle}
                    title={voiceOutputEnabled ? "Disable Voice Output" : "Enable Voice Output"}
                >
                    {voiceOutputEnabled ? <Volume2 size={20} /> : <VolumeX size={20} />}
                </button>

                {activeState === 'responding' && (
                    <button
                        className="p-2 rounded-full bg-red-100 text-red-500"
                        onClick={handleStopResponding}
                        title="Stop Response"
                    >
                        <StopCircle size={20} />
                    </button>
                )}
            </div>
        </div>
    );
};

export default VoiceControls;