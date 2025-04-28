// File: JunoSidebar/JunoSidebar.React/src/components/PersonalitySelector.tsx
import React, { useState, useEffect } from 'react';
import { ChevronDown, Check } from 'lucide-react';
import WpfBridge from '../bridge/WpfBridge';

export interface Personality {
    id: string;
    name: string;
    description: string;
    avatar: string;
    builtIn: boolean;
}

interface PersonalitySelectorProps {
    expanded: boolean;
}

const PersonalitySelector: React.FC<PersonalitySelectorProps> = ({ expanded }) => {
    const [isOpen, setIsOpen] = useState(false);
    const [personalities, setPersonalities] = useState<Personality[]>([]);
    const [currentPersonality, setCurrentPersonality] = useState<Personality | null>(null);

    useEffect(() => {
        // Request personalities from backend
        WpfBridge.sendMessage('getPersonalities');

        // Listen for personalities data
        const unsubscribe = WpfBridge.on('personalitiesData', (data: any) => {
            if (data.personalities && Array.isArray(data.personalities)) {
                setPersonalities(data.personalities);

                // Set current personality if available
                if (data.currentPersonalityId) {
                    const current = data.personalities.find((p: Personality) => p.id === data.currentPersonalityId);
                    if (current) {
                        setCurrentPersonality(current);
                    }
                }
            }
        });

        // Listen for personality changes
        const personalityChangeUnsubscribe = WpfBridge.on('personalityChanged', (data: any) => {
            if (data.personality) {
                setCurrentPersonality(data.personality);
            }
        });

        return () => {
            unsubscribe();
            personalityChangeUnsubscribe();
        };
    }, []);

    const handleSelectPersonality = (personality: Personality) => {
        WpfBridge.sendMessage('setPersonality', { id: personality.id });
        setCurrentPersonality(personality);
        setIsOpen(false);
    };

    // Handle click outside to close dropdown
    useEffect(() => {
        const handleClickOutside = (event: MouseEvent) => {
            const target = event.target as HTMLElement;
            if (!target.closest('.personality-selector')) {
                setIsOpen(false);
            }
        };

        document.addEventListener('mousedown', handleClickOutside);
        return () => {
            document.removeEventListener('mousedown', handleClickOutside);
        };
    }, []);

    if (!expanded) {
        // Minimal view for collapsed sidebar
        return (
            <div className="p-2 border-b">
                <div className="flex items-center justify-center">
                    <div className="w-10 h-10 rounded-full flex items-center justify-center bg-blue-100 text-blue-500">
                        {currentPersonality?.avatar ? (
                            <span>{currentPersonality.avatar.substring(0, 1).toUpperCase()}</span>
                        ) : (
                            <span>J</span>
                        )}
                    </div>
                </div>
            </div>
        );
    }

    return (
        <div className="personality-selector relative border-b p-4">
            <div
                className="flex items-center justify-between cursor-pointer px-3 py-2 hover:bg-gray-50 rounded"
                onClick={() => setIsOpen(!isOpen)}
            >
                <div className="flex items-center">
                    <div className="w-8 h-8 rounded-full flex items-center justify-center bg-blue-100 text-blue-500 mr-3">
                        {currentPersonality?.avatar ? (
                            <span>{currentPersonality.avatar.substring(0, 1).toUpperCase()}</span>
                        ) : (
                            <span>J</span>
                        )}
                    </div>
                    <div>
                        <p className="font-medium text-sm">{currentPersonality?.name || "General Assistant"}</p>
                        <p className="text-xs text-gray-500">Active Personality</p>
                    </div>
                </div>
                <ChevronDown size={16} className={`text-gray-400 transition-transform ${isOpen ? 'rotate-180' : ''}`} />
            </div>

            {isOpen && (
                <div className="absolute z-10 mt-1 w-full bg-white rounded-md shadow-lg max-h-60 overflow-y-auto border">
                    <div className="p-2">
                        <p className="text-xs font-semibold text-gray-500 mb-1 px-2 uppercase">Select Personality</p>
                        {personalities.map((personality) => (
                            <div
                                key={personality.id}
                                className={`flex items-center px-3 py-2 cursor-pointer rounded ${
                                    currentPersonality?.id === personality.id ? 'bg-blue-50' : 'hover:bg-gray-50'
                                }`}
                                onClick={() => handleSelectPersonality(personality)}
                            >
                                <div className="w-8 h-8 rounded-full flex items-center justify-center bg-blue-100 text-blue-500 mr-3">
                                    <span>{personality.avatar.substring(0, 1).toUpperCase()}</span>
                                </div>
                                <div className="flex-1">
                                    <p className="font-medium text-sm">{personality.name}</p>
                                    <p className="text-xs text-gray-500 truncate">{personality.description}</p>
                                </div>
                                {currentPersonality?.id === personality.id && (
                                    <Check size={16} className="text-blue-500 ml-2" />
                                )}
                            </div>
                        ))}
                    </div>
                    <div className="border-t p-2">
                        <button
                            className="w-full text-left px-3 py-2 text-sm text-blue-500 hover:bg-gray-50 rounded"
                            onClick={() => {
                                WpfBridge.sendMessage('showPersonalitySettings');
                                setIsOpen(false);
                            }}
                        >
                            Manage Personalities...
                        </button>
                    </div>
                </div>
            )}
        </div>
    );
};

export default PersonalitySelector;