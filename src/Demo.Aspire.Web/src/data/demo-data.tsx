// One snapshot, refreshed by one function.
//
// The flow panel draws a booking, its payment and its e-mail side by side and they have
// to agree with each other — a step reading "Payment captured" above one reading "Waiting…"
// is a bug in the page, not a thing the system did. Four independently cached queries can
// tear between them mid-flight, which is why there is no query library here and why these
// four requests go out together and land together.

import {
    createContext, use, useCallback, useEffect, useMemo, useRef, useState, type ReactNode,
} from "react";

import * as client from "../api/client.ts";
import type {
    BookingResponse, ChaosMode, NotificationEntry, PaymentListItem, ScreeningListItem, SeatMapView,
} from "../api/types.ts";
import { onEntry } from "../bus/tape-store.ts";
import { useToast } from "../components/Toast.tsx";
import { useSelection } from "./selection.tsx";

/**
 * The booking(s) whose flow is being drawn, resolved for the flow panel. Kept apart from
 * `bookings` because that list is whatever the address in the masthead has booked — and
 * during "Race a rival" the rival's booking belongs to someone else's address, so it is
 * never in it. The flow panel would otherwise have nothing to draw for the side that won.
 */
export interface TrackedBookings {
    mine: BookingResponse | null;
    rival: BookingResponse | null;
}

interface DemoData {
    screenings: ScreeningListItem[];
    /** False until the catalogue has been fetched once. An empty list means "none on
        sale"; before this flips, it only means "not asked yet", and the page should not
        claim the former while the latter is true. */
    screeningsLoaded: boolean;
    seatMap: SeatMapView | null;
    bookings: BookingResponse[];
    trackedBookings: TrackedBookings;
    payments: PaymentListItem[];
    mail: NotificationEntry[];
    chaosMode: ChaosMode;
    holdPolicySeconds: number;
    dashboardUrl: string;
    refresh: () => Promise<void>;
    setChaos: (mode: ChaosMode) => Promise<void>;
    setHoldSeconds: (seconds: number) => Promise<void>;
}

const DemoDataContext = createContext<DemoData | null>(null);

export function useDemoData(): DemoData {
    const data = use(DemoDataContext);
    if (!data) throw new Error("useDemoData outside DemoDataProvider");
    return data;
}

const settled = (booking: BookingResponse | null | undefined) =>
    booking?.status === "Confirmed" || booking?.status === "Cancelled";

export function DemoDataProvider({ children }: { children: ReactNode }) {
    const toast = useToast();
    const { email, screeningId, tracked, stopTracking } = useSelection();

    const [screenings, setScreenings] = useState<ScreeningListItem[]>([]);
    const [screeningsLoaded, setScreeningsLoaded] = useState(false);
    const [seatMap, setSeatMap] = useState<SeatMapView | null>(null);
    const [bookings, setBookings] = useState<BookingResponse[]>([]);
    const [trackedBookings, setTrackedBookings] = useState<TrackedBookings>(
        { mine: null, rival: null });
    const [payments, setPayments] = useState<PaymentListItem[]>([]);
    const [mail, setMail] = useState<NotificationEntry[]>([]);
    const [chaosMode, setChaosMode] = useState<ChaosMode>("None");
    const [holdPolicySeconds, setHoldPolicySeconds] = useState(180);
    const [dashboardUrl, setDashboardUrl] = useState("");

    // refresh() is called from a 4s timer, from the SSE stream and from click handlers;
    // reading the live values through a ref keeps it from being rebuilt — and every
    // effect that depends on it from being torn down — on every keystroke in the e-mail
    // field.
    const live = useRef({ email, screeningId, tracked, stopTracking });
    live.current = { email, screeningId, tracked, stopTracking };

    const loadScreenings = useCallback(async () => {
        try {
            setScreenings(await client.listScreenings());
        } finally {
            // Even a failed fetch has been asked: the catalogue stops saying "Loading…"
            // and says what it actually knows, which is nothing.
            setScreeningsLoaded(true);
        }
    }, []);

    const loadSeatMap = useCallback(async () => {
        const id = live.current.screeningId;
        if (id) setSeatMap(await client.getSeatMap(id));
    }, []);

    const refresh = useCallback(async () => {
        const { email: address, tracked: watching, stopTracking: stop } = live.current;

        const [nextBookings, nextPayments, nextMail, chaos] = await Promise.all([
            address.includes("@") ? client.listBookings(address).catch(() => []) : [],
            client.listPayments().catch(() => []),
            client.listNotifications().catch(() => []),
            client.getChaos().catch(() => null),
        ]);

        setBookings(nextBookings);
        setPayments(nextPayments);
        setMail(nextMail);

        // Re-synced here, not only from a click, because "Fail twice, then succeed"
        // clears itself back to None server-side once it has burned through its failures.
        if (chaos) setChaosMode(chaos.mode);

        if (!watching) {
            setTrackedBookings({ mine: null, rival: null });
            return;
        }

        const resolve = async (id: string | null) => id
            ? nextBookings.find(candidate => candidate.bookingId === id)
                ?? await client.getBooking(id).catch(() => null)
            : null;

        const [mine, rival] = await Promise.all([resolve(watching.mine), resolve(watching.rival)]);

        setTrackedBookings({ mine: mine ?? null, rival: rival ?? null });

        // Both sides have to be finished before the seat map is worth reloading — a race
        // still in flight is exactly the moment not to.
        if (settled(mine) && (!watching.rival || settled(rival))) {
            await Promise.all([loadScreenings(), loadSeatMap()]).catch(() => {});
            stop();
        }
    }, [loadScreenings, loadSeatMap]);

    const setChaos = useCallback(async (mode: ChaosMode) => {
        try {
            setChaosMode((await client.postChaos(mode)).mode);
        } catch (error) {
            toast((error as Error).message);
        }
    }, [toast]);

    const setHoldSeconds = useCallback(async (seconds: number) => {
        try {
            const policy = await client.postHoldPolicy(seconds);
            setHoldPolicySeconds(policy.seconds);
            toast(`New seat holds now last ${policy.seconds}s.`);
        } catch (error) {
            toast((error as Error).message);
        }
    }, [toast]);

    // ── Start-up ─────────────────────────────────────────────────────────────────
    useEffect(() => {
        loadScreenings().catch((error: Error) =>
            toast(`Could not reach the gateway: ${error.message}`));

        // Each of these three only degrades the page if it fails: the switch stays at its
        // default, the link stays hidden, the flow keeps drawing.
        client.getHoldPolicy().then(policy => setHoldPolicySeconds(policy.seconds)).catch(() => {});
        client.getDemoInfo().then(info => setDashboardUrl(info.dashboardUrl)).catch(() => {});
        refresh().catch(() => {});
    }, [loadScreenings, refresh, toast]);

    // The seat map follows whichever screening is chosen, including the one restored from
    // ?screening= before the first render.
    useEffect(() => {
        if (screeningId) loadSeatMap().catch(() => {});
    }, [screeningId, loadSeatMap]);

    // ── A slow safety net, not the main loop ─────────────────────────────────────
    // The bus tape is what actually notices that a tracked booking has moved. This tick
    // only covers the gap: a page that loaded before the tape connected, or a tape entry
    // Redis never delivered.
    useEffect(() => {
        const timer = setInterval(() => {
            refresh()
                .then(() => { if (!live.current.tracked) return loadScreenings(); })
                .catch((error: unknown) => console.warn("refresh failed", error));
        }, 4000);

        return () => clearInterval(timer);
    }, [refresh, loadScreenings]);

    // ── What the tape is for ─────────────────────────────────────────────────────
    // A message for the flow being drawn means that flow just moved: look now instead of
    // waiting up to four seconds. Debounced because the read model the flow queries is
    // written a moment *after* the message that announces it — deleting this delay makes
    // the page reliably fetch the state from just before the step it is reacting to.
    useEffect(() => {
        let timer: ReturnType<typeof setTimeout>;

        return onEntry(entry => {
            const watching = live.current.tracked;
            if (!watching) return;

            if (entry.correlationId === watching.mine || entry.correlationId === watching.rival) {
                clearTimeout(timer);
                timer = setTimeout(() => { refresh().catch(() => {}); }, 150);
            }
        });
    }, [refresh]);

    const value = useMemo(() => ({
        screenings, screeningsLoaded, seatMap, bookings, trackedBookings, payments, mail,
        chaosMode, holdPolicySeconds, dashboardUrl, refresh, setChaos, setHoldSeconds,
    }), [screenings, screeningsLoaded, seatMap, bookings, trackedBookings, payments, mail,
        chaosMode, holdPolicySeconds, dashboardUrl, refresh, setChaos, setHoldSeconds]);

    return <DemoDataContext value={value}>{children}</DemoDataContext>;
}
