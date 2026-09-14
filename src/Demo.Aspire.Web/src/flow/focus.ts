// Which step is being looked at, and on whose behalf.
//
// A module-level store rather than context, for the same reason bus/tape-store.ts is one:
// the seat map must not re-render because a pointer crossed a step in the flow panel. Only
// the three components that actually draw a step subscribe — the flow panel, the bus tape
// and the waterfall — and everything below them is untouched.
//
// The correlation id is half of the key because "Race a rival" puts two panels on screen.
// Focusing step 4 on the rival's panel must not light up the rows of your own booking; they
// are two different bookings that happen to be at the same step.

import type { FlowStepId } from "./choreography.ts";

export interface FlowFocus {
    /** The booking being looked at — which is also the correlation id on every tape row. */
    correlationId: string;
    step: FlowStepId;
    /**
     * A click pins the highlight so it survives moving the pointer over to the tape to read
     * the rows it just lit up; a hover does not. Escape, or clicking the same step again,
     * unpins.
     */
    pinned: boolean;
}

let focus: FlowFocus | null = null;

const listeners = new Set<() => void>();

function set(next: FlowFocus | null) {
    if (next?.correlationId === focus?.correlationId
        && next?.step === focus?.step
        && next?.pinned === focus?.pinned) {
        return;
    }

    focus = next;
    for (const listener of listeners) listener();
}

/** Hovering or tabbing onto a step. Ignored while another step is pinned. */
export function hover(correlationId: string, step: FlowStepId) {
    if (focus?.pinned) return;
    set({ correlationId, step, pinned: false });
}

/** Leaving a step. Leaves a pinned highlight exactly where it was. */
export function unhover() {
    if (focus?.pinned) return;
    set(null);
}

/** Clicking a step pins it; clicking the pinned one clears it. */
export function toggle(correlationId: string, step: FlowStepId) {
    const current = focus;
    const same = current?.correlationId === correlationId && current.step === step;

    set(same && current.pinned ? null : { correlationId, step, pinned: true });
}

export const clear = () => set(null);

export function subscribe(listener: () => void): () => void {
    listeners.add(listener);
    return () => listeners.delete(listener);
}

/** Referentially stable between changes — useSyncExternalStore loops if it is not. */
export const getSnapshot = (): FlowFocus | null => focus;
