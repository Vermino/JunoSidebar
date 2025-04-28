// File: JunoSidebar.React/src/bridge/WpfBridge.ts

/**
 * Bridge for communication between React and WPF host
 */
class WpfBridge {
    private static instance: WpfBridge;
    private listeners: Map<string, Function[]> = new Map();
    private isWebView2: boolean;

    private constructor() {
        // Check if running in WebView2
        this.isWebView2 = this.checkIsWebView2();

        // Setup message listener from WPF
        window.addEventListener('message', this.handleMessageFromWpf.bind(this));

        console.log(`WpfBridge initialized. Running in WebView2: ${this.isWebView2}`);
    }

    /**
     * Check if running in WebView2
     */
    private checkIsWebView2(): boolean {
        return 'chrome' in window && 'webview' in (window as any).chrome;
    }

    /**
     * Get singleton instance
     */
    public static getInstance(): WpfBridge {
        if (!WpfBridge.instance) {
            WpfBridge.instance = new WpfBridge();
        }
        return WpfBridge.instance;
    }

    /**
     * Send message to WPF
     */
    public sendMessage(action: string, data: any = {}): void {
        const message = {
            action,
            ...data
        };

        if (this.isWebView2) {
            try {
                const messageString = JSON.stringify(message);
                (window as any).chrome.webview.postMessage(messageString);
                console.log(`Sent message to WPF: ${action}`, data);
            } catch (error) {
                console.error('Error sending message to WPF:', error);
            }
        } else {
            // When running in browser during development
            console.log('Message to WPF (dev mode):', { action, data });

            // In dev mode, simulate responses for testing
            this.simulateWpfResponse(action, data);
        }
    }

    /**
     * Simulate WPF responses when running in browser (development mode)
     */
    private simulateWpfResponse(action: string, data: any): void {
        // Simulate typical WPF responses for development testing
        switch (action) {
            case 'toggleExpanded':
                setTimeout(() => {
                    this.emit('setExpanded', { expanded: data.expanded });
                }, 100);
                break;

            case 'getState':
                setTimeout(() => {
                    this.emit('appState', {
                        version: '1.0.0-dev',
                        expanded: false
                    });
                }, 100);
                break;
        }
    }

    /**
     * Handle incoming message from WPF
     */
    private handleMessageFromWpf(event: MessageEvent): void {
        try {
            const message = typeof event.data === 'string'
                ? JSON.parse(event.data)
                : event.data;

            if (message && message.action) {
                console.log(`Received message from WPF: ${message.action}`, message);
                this.emit(message.action, message);
            }
        } catch (error) {
            console.error('Error handling message from WPF:', error);
        }
    }

    /**
     * Register event listener
     */
    public on(event: string, callback: Function): () => void {
        if (!this.listeners.has(event)) {
            this.listeners.set(event, []);
        }

        const listeners = this.listeners.get(event);
        listeners?.push(callback);

        // Return unsubscribe function
        return () => {
            const index = listeners?.indexOf(callback) ?? -1;
            if (index !== -1 && listeners) {
                listeners.splice(index, 1);
            }
        };
    }

    /**
     * Emit event to listeners
     */
    private emit(event: string, data: any): void {
        if (this.listeners.has(event)) {
            this.listeners.get(event)?.forEach(callback => {
                try {
                    callback(data);
                } catch (error) {
                    console.error(`Error in listener for event ${event}:`, error);
                }
            });
        }
    }

    /**
     * Toggle sidebar expanded state
     */
    public toggleExpanded(expanded: boolean): void {
        this.sendMessage('toggleExpanded', { expanded });
    }

    /**
     * Set sidebar expanded state
     */
    public setExpanded(expanded: boolean): void {
        this.sendMessage('setExpanded', { expanded });
    }

    /**
     * Request current application state from WPF
     */
    public getState(): void {
        this.sendMessage('getState');
    }
}

// Export singleton instance
export default WpfBridge.getInstance();