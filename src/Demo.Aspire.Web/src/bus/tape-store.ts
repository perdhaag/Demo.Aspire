// The bus tape: every integration event published, consumed or faulted on by any of the
// five services, streamed over Server-Sent Events from GET /api/events.
//
// This is a module-level store rather than React state, for two reasons. The feed can
// burst several entries a second and each one appends to a ring buffer; putting that in
// a component would re-render the seat map every time a message crossed the bus. And a
// stream owned by a useEffect is opened twice and closed once under StrictMode, which is
// the quietest way imaginable to end up showing every message on the tape twice.

import { recentTape } from "../api/client.ts";
import type { BusTapeEntry } from "../api/types.ts";

const MAX_TAPE_ROWS = 200;
const MAX_TRACKED_CORRELATIONS = 50;

export interface TapeSnapshot {
    readonly entries: readonly BusTapeEntry[];
    readonly connected: boolean;
}

let snapshot: TapeSnapshot = { entries: [], connected: false };

const listeners = new Set<() => void>();
const entryListeners = new Set<(entry: BusTapeEntry) => void>();

// Grouped by correlation id — which for a whole booking's flow is the booking id itself,
// stamped onto CorrelationContext by PlaceBookingHandler at the very start — so the trace
// waterfall can be rebuilt for whichever booking is being watched without asking the
// gateway for anything it has already streamed here. Capped the same way the rail is, so
// a long demo session cannot grow it without bound.
const byCorrelation = new Map<string, BusTapeEntry[]>();

let started = false;

function emit() {
    for (const listener of listeners) listener();
}

function append(entry: BusTapeEntry) {
    const entries = [...snapshot.entries, entry];

    snapshot = {
        entries: entries.length > MAX_TAPE_ROWS ? entries.slice(-MAX_TAPE_ROWS) : entries,
        connected: snapshot.connected,
    };

    if (!byCorrelation.has(entry.correlationId)) {
        if (byCorrelation.size >= MAX_TRACKED_CORRELATIONS) {
            const oldest = byCorrelation.keys().next().value;
            if (oldest !== undefined) byCorrelation.delete(oldest);
        }

        byCorrelation.set(entry.correlationId, []);
    }

    byCorrelation.get(entry.correlationId)?.push(entry);

    emit();
    for (const listener of entryListeners) listener(entry);
}

function setConnected(connected: boolean) {
    if (snapshot.connected === connected) return;

    snapshot = { entries: snapshot.entries, connected };
    emit();
}

// Connected lazily on first subscribe and never disconnected. EventSource reconnects on
// its own, so there is nothing here worth tearing down — and a cleanup that closed it
// would make StrictMode's double mount visible as a dropped stream.
function start() {
    if (started) return;
    started = true;

    void recentTape()
        .then(entries => { for (const entry of entries) append(entry); })
        // A seed that failed to load is not worth failing the page over; the live stream
        // below carries on from here regardless.
        .catch(() => {})
        .finally(() => {
            const source = new EventSource("/api/events");

            source.addEventListener("bus", event =>
                append(JSON.parse((event as MessageEvent<string>).data) as BusTapeEntry));

            source.onopen = () => setConnected(true);
            source.onerror = () => setConnected(false);
        });
}

export function subscribe(listener: () => void): () => void {
    start();
    listeners.add(listener);
    return () => listeners.delete(listener);
}

/** Referentially stable between appends — useSyncExternalStore loops if it is not. */
export function getSnapshot(): TapeSnapshot {
    return snapshot;
}

export function entriesFor(correlationId: string): readonly BusTapeEntry[] {
    return byCorrelation.get(correlationId) ?? [];
}

/**
 * Fires for every entry as it arrives. The data provider uses this to notice that the
 * booking it is drawing has just moved, and to look now rather than wait for the next
 * safety-net poll.
 */
export function onEntry(listener: (entry: BusTapeEntry) => void): () => void {
    start();
    entryListeners.add(listener);
    return () => entryListeners.delete(listener);
}
