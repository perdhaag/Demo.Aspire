// What the reader has chosen, as opposed to what the services have said. It sits outside
// the data provider because the data provider reads it: which screening's seats to fetch,
// whose bookings to list, and which booking's flow to resolve all come from here.

import { createContext, use, useCallback, useMemo, useState, type ReactNode } from "react";

import { useToast } from "../components/Toast.tsx";
import { readUrlParam, writeUrlParam } from "../hooks/useUrlParam.ts";

export const RIVAL_EMAIL = "rival@example.com";

/** The booking(s) whose flow is being drawn. `rival` is null outside "Race a rival". */
export interface Tracked {
    mine: string | null;
    rival: string | null;
}

interface Selection {
    email: string;
    setEmail: (email: string) => void;
    screeningId: string | null;
    selectScreening: (screeningId: string) => void;
    selected: ReadonlySet<string>;
    toggleSeat: (number: string) => void;
    clearSeats: () => void;
    tracked: Tracked | null;
    track: (bookingId: string) => void;
    trackRace: (mine: string | null, rival: string | null) => void;
    stopTracking: () => void;
}

const SelectionContext = createContext<Selection | null>(null);

export function useSelection(): Selection {
    const selection = use(SelectionContext);
    if (!selection) throw new Error("useSelection outside SelectionProvider");
    return selection;
}

export function SelectionProvider({ children }: { children: ReactNode }) {
    const toast = useToast();

    const [email, setEmail] = useState("ada@example.com");
    const [screeningId, setScreeningId] = useState<string | null>(() => readUrlParam("screening"));
    const [selected, setSelected] = useState<ReadonlySet<string>>(() => new Set());
    const [tracked, setTracked] = useState<Tracked | null>(() => {
        const booking = readUrlParam("booking");
        return booking ? { mine: booking, rival: null } : null;
    });

    const selectScreening = useCallback((id: string) => {
        setScreeningId(id);
        setSelected(new Set());
        writeUrlParam("screening", id);
    }, []);

    const toggleSeat = useCallback((number: string) => {
        setSelected(current => {
            const next = new Set(current);

            if (next.has(number)) {
                next.delete(number);
            } else if (next.size >= 8) {
                // The same rule lives in the Screening aggregate; this is only a courtesy.
                toast("A single booking may hold at most 8 seats.");
                return current;
            } else {
                next.add(number);
            }

            return next;
        });
    }, [toast]);

    const clearSeats = useCallback(() => setSelected(new Set()), []);

    const track = useCallback((bookingId: string) => {
        setTracked({ mine: bookingId, rival: null });
        writeUrlParam("booking", bookingId);
    }, []);

    // "Race a rival" tracks two bookings at once, so the URL stops pointing at a single
    // booking — there is no one booking to deep-link to once two are racing for the seats.
    const trackRace = useCallback((mine: string | null, rival: string | null) => {
        setTracked({ mine, rival });
        writeUrlParam("booking", null);
    }, []);

    const stopTracking = useCallback(() => setTracked(null), []);

    const value = useMemo(() => ({
        email, setEmail, screeningId, selectScreening, selected, toggleSeat, clearSeats,
        tracked, track, trackRace, stopTracking,
    }), [email, screeningId, selectScreening, selected, toggleSeat, clearSeats, tracked,
        track, trackRace, stopTracking]);

    return <SelectionContext value={value}>{children}</SelectionContext>;
}
