import React, { useEffect } from 'react';
import JunoSidebar from './components/JunoSidebar';
import WpfBridge from './bridge/WpfBridge';

const App: React.FC = () => {
    useEffect(() => {
        // Request initial state from WPF when component mounts
        WpfBridge.getState();

        // Setup global error handling
        const originalConsoleError = console.error;
        console.error = (...args) => {
            // Log the error to the original console
            originalConsoleError(...args);

            // Send the error to WPF host for logging
            if (args.length > 0 && args[0] instanceof Error) {
                const error = args[0];
                WpfBridge.sendMessage('error', {
                    message: error.message,
                    stack: error.stack
                });
            }
        };

        // Cleanup on unmount
        return () => {
            console.error = originalConsoleError;
        };
    }, []);

    return (
        <div className="app">
            <JunoSidebar />
        </div>
    );
};

export default App;