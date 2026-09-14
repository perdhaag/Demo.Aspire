import { createContext, use, useCallback, useRef, useState, type ReactNode } from "react";

const ToastContext = createContext<(message: string) => void>(() => {});

/** Called from nine places, none of which should have to be handed a setter. */
export const useToast = () => use(ToastContext);

export function ToastProvider({ children }: { children: ReactNode }) {
    const [message, setMessage] = useState<string | null>(null);
    const timer = useRef<ReturnType<typeof setTimeout>>(undefined);

    const toast = useCallback((text: string) => {
        setMessage(text);
        clearTimeout(timer.current);
        timer.current = setTimeout(() => setMessage(null), 6000);
    }, []);

    return (
        <ToastContext value={toast}>
            {children}
            <div className="toast" role="status" aria-live="polite" hidden={message === null}>
                {message}
            </div>
        </ToastContext>
    );
}
