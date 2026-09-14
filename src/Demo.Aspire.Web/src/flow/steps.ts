// The five steps of one booking's flow, derived from the three read models that describe
// it and from nothing else.
//
// Each step names the service that made the decision, because that is the thing worth
// seeing: no single component is driving this, they are just reacting to each other.
//
// There is no DOM in here on purpose. In the vanilla page this logic and the <li> it
// produced were the same function; splitting them makes the interesting half — five
// states across four step kinds, two of which depend on the chaos mode — the first thing
// in this UI that can be tested without standing the distributed app up first.

import { clock } from "../api/format.ts";
import type {
    BookingResponse, ChaosMode, IsoDate, NotificationEntry, PaymentListItem,
} from "../api/types.ts";

export type FlowStepState = "done" | "failed" | "pending" | "waiting";

export interface FlowStep {
    /** What happened, in the reader's words. */
    what: string;
    /** Which service decided it, and what it published as a result. */
    who: string;
    /** When it happened, or null while it still has not. */
    at: IsoDate | null;
    state: FlowStepState;
    /** The detail after the dot: a reference, a decline reason, a subject line. */
    note: string | null;
    /**
     * Set only on the seat-hold step, and only while the hold is still the thing being
     * waited on. Its presence is what tells the renderer to replace the static note with
     * a live countdown — see PayCountdownStep in FlowPanel.tsx, which owns the ticking.
     */
    payBeforeUtc: IsoDate | null;
}

const MARKERS: Record<FlowStepState, string> = {
    done: "✓",
    failed: "✕",
    pending: "·",
    waiting: "",
};

export const markerFor = (state: FlowStepState): string => MARKERS[state];

export function flowSteps(
    booking: BookingResponse,
    payment: PaymentListItem | null | undefined,
    mail: NotificationEntry | null | undefined,
    chaosMode: ChaosMode,
): FlowStep[] {
    const failed = booking.status === "Cancelled";

    return [
        {
            what: "Booking placed",
            who: "Bookings — publishes BookingPlaced",
            at: booking.placedAtUtc,
            state: "done",
            note: null,
            payBeforeUtc: null,
        },
        {
            // Bookings answers the POST before Screenings has decided anything, so the
            // title is the tell: a booking the read model has not caught up with yet is
            // still called "(unknown)", and the seats are still only being asked for.
            what: booking.filmTitle === "(unknown)" && !failed ? "Holding seats…" : "Seats held",
            who: "Screenings — the aggregate decides, then publishes SeatsHeld",
            at: booking.seatsHeldAtUtc,
            state: booking.payBeforeUtc ? "done" : failed ? "failed" : "pending",
            note: booking.payBeforeUtc
                ? `held until ${clock.format(new Date(booking.payBeforeUtc))}`
                : null,
            // While payment is still outstanding the renderer overwrites the static note
            // above with a live "Xs left to pay" every second, turning amber under 30s —
            // the short-holds switch is only worth having if a viewer can watch the number
            // count down, rather than read a fixed timestamp.
            payBeforeUtc: booking.payBeforeUtc && !payment && !failed ? booking.payBeforeUtc : null,
        },
        {
            // The chaos strip's "Pause Payments" is named here on purpose: without it, a
            // paused payment and a slow one look identical — both just say "pending".
            what: payment
                ? payment.status === "Captured" ? "Payment captured" : "Payment declined"
                : !failed && chaosMode === "Paused" ? "Payments is paused…" : "Taking payment…",
            who: "Payments — publishes the outcome either way",
            at: payment?.decidedAtUtc ?? null,
            state: payment
                ? payment.status === "Captured" ? "done" : "failed"
                : failed ? "failed" : "pending",
            note: payment?.reference ?? payment?.declineReason
                ?? (!payment && !failed && chaosMode === "Paused"
                    ? "the message is waiting on the queue, not retrying"
                    : null),
            payBeforeUtc: null,
        },
        {
            what: booking.status === "Confirmed" ? "Booking confirmed"
                : failed ? "Booking cancelled"
                : "Waiting…",
            who: failed
                ? "Bookings — publishes BookingCancelled, so Screenings puts the seats back"
                : "Bookings — publishes BookingConfirmed, so Screenings sells the seats",
            at: booking.finishedAtUtc,
            state: booking.status === "Confirmed" ? "done" : failed ? "failed" : "waiting",
            note: booking.cancellationReason,
            payBeforeUtc: null,
        },
        {
            what: mail ? mail.outcome : "E-mail",
            who: "Notifications — renders it and sends it over SMTP to Mailpit",
            at: mail?.sentAtUtc ?? null,
            state: mail ? (failed ? "failed" : "done") : "waiting",
            note: mail?.subject ?? null,
            payBeforeUtc: null,
        },
    ];
}

/**
 * The line under the panel. It is the only place the page speaks in its own voice about
 * what just happened, so it says the thing the demo exists to say: a cancellation is a
 * compensating action, not a transaction being rolled back.
 */
export function flowNote(booking: BookingResponse): string {
    if (booking.status === "Confirmed") {
        return `Seats ${booking.seats.join(", ")} for ${booking.filmTitle} are yours. `
            + "The ticket is in the Mailpit inbox, linked from the Aspire dashboard.";
    }

    return booking.status === "Cancelled"
        ? "The seats went straight back on sale — that is the compensating action, not a rollback."
        : "Every step below is a message. Nothing is orchestrating them.";
}
