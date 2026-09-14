import { useEffect, useSyncExternalStore } from "react";

import { clear, getSnapshot, subscribe, type FlowFocus } from "../flow/focus.ts";

/**
 * The step currently being looked at. Subscribe from a component only if it draws steps —
 * the whole reason the focus lives outside React is that a hover must not reach the seat
 * map (see flow/focus.ts).
 */
export const useFlowFocus = (): FlowFocus | null => useSyncExternalStore(subscribe, getSnapshot);

/** Escape unpins a pinned highlight, wherever the pointer happens to be. */
export function useEscapeClearsFocus() {
    useEffect(() => {
        const onKeyDown = (event: KeyboardEvent) => { if (event.key === "Escape") clear(); };

        document.addEventListener("keydown", onKeyDown);
        return () => document.removeEventListener("keydown", onKeyDown);
    }, []);
}
