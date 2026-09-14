// The six steps of one booking's flow, derived from the three read models that describe it
// and from the bus tape that carried it.
//
// Each step names the service that made the decision and the message it published, because
// that is the thing worth seeing: no single component is driving this, they are just
// reacting to each other. The step ids come from flow/choreography.ts, which is also what
// tells the tape and the waterfall which step each of *their* rows belongs to — that shared
// id is the whole reason a hover on one lights up the other two.
//
// Six steps, not the five this used to draw. The old step 3, "taking payment", covered both
// Bookings publishing PaymentAuthorizationRequested and Payments deciding, so it was the
// one step that could not be attributed to a single service. Splitting it makes the rule
// uniform — one step is one service, one decision, one message — and lines the page up with
// the table in the README.
//
// There is no DOM in here on purpose: five states across six steps, two of which depend on
// the chaos mode, is the interesting half and it is testable without standing the
// distributed app up first.

import { clock } from "../api/format.ts";
import type {
    BookingResponse, BusTapeEntry, ChaosMode, IsoDate, NotificationEntry, PaymentListItem,
    ServiceName,
} from "../api/types.ts";
import {
    firstSeen, stepById, type FlowStepId, type Touch, type Transport,
} from "./choreography.ts";

/**
 * `skipped` is new, and it earns its place: a booking whose hold was refused never asks to
 * be charged. Drawing that step as "waiting" would be a lie the page tells forever, and
 * leaving it out would break the numbering the tape now cross-references.
 */
export type FlowStepState = "done" | "failed" | "pending" | "waiting" | "skipped";

/** One service's reaction, where a step has more than one. Only `delivered` does. */
export interface StepOutcome {
    service: ServiceName;
    what: string;
    at: IsoDate | null;
    state: FlowStepState;
    note: string | null;
}

export interface FlowStep {
    id: FlowStepId;
    /** 1…6, and the same number the bus tape stamps on the rows belonging to this step. */
    ordinal: number;
    /** What happened, in the reader's words. */
    what: string;
    /** Who decided. Two only on `delivered`, where one message starts two services. */
    services: readonly ServiceName[];
    /** Which service decided it, and what it published as a result. */
    who: string;
    /** When it happened, or null while it still has not. */
    at: IsoDate | null;
    state: FlowStepState;
    /** The detail after the dot: a reference, a decline reason, a subject line. */
    note: string | null;
    /** How this step was reached. Exactly one step in the flow is `http`. */
    transport: Transport;
    /** The message it published, once it is known which of the candidates it was. */
    message: string | null;
    /** What it touched on the way past: the databases, the cache, the bus, the mail. */
    touches: readonly Touch[];
    /** One row per reacting service, for the step where one message starts two. */
    outcomes: readonly StepOutcome[];
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
    skipped: "–",
};

export const markerFor = (state: FlowStepState): string => MARKERS[state];

/** The later of two instants, either of which may not have happened. */
const latest = (left: IsoDate | null, right: IsoDate | null): IsoDate | null =>
    left && right ? (Date.parse(left) >= Date.parse(right) ? left : right) : left ?? right;

export function flowSteps(
    booking: BookingResponse,
    payment: PaymentListItem | null | undefined,
    mail: NotificationEntry | null | undefined,
    chaosMode: ChaosMode,
    entries: readonly BusTapeEntry[] = [],
): FlowStep[] {
    const failed = booking.status === "Cancelled";
    const held = booking.payBeforeUtc !== null;

    // The clock-driven path. Only the tape can see it: a lapsed hold looks exactly like a
    // refused one in the read model, and the difference — a timer published this, not
    // another message — is the single most interesting thing about it.
    const expired = firstSeen(entries, "SeatHoldExpired") !== null;

    // Bookings never records *when* it asked to be charged, so this step's timestamp comes
    // from the tape or from nowhere. That is what it means for the tape to be the record.
    const requested = firstSeen(entries, "PaymentAuthorizationRequested");

    const common = (id: FlowStepId) => {
        const definition = stepById(id);

        return {
            id,
            ordinal: definition.ordinal,
            services: definition.services,
            transport: definition.transport,
            touches: definition.touches,
            outcomes: [] as StepOutcome[],
            payBeforeUtc: null as IsoDate | null,
        };
    };

    return [
        {
            ...common("placed"),
            what: "Booking placed",
            who: "Bookings — one POST, answered 202 before anything else has happened",
            at: booking.placedAtUtc,
            state: "done" as FlowStepState,
            note: null,
            message: "BookingPlaced",
        },
        holdStep(),
        authorizeStep(),
        chargeStep(),
        settleStep(),
        deliverStep(),
    ];

    function holdStep(): FlowStep {
        const base = { ...common("held"), who: "Screenings — the aggregate decides, nothing else can" };

        if (expired) {
            return {
                ...base,
                what: "Hold expired",
                // The sweeper is a timer, not a consumer, so this step was not reached by a
                // message at all. Saying so is the point of having a transport on a step.
                transport: "clock",
                at: booking.seatsHeldAtUtc,
                state: "failed",
                note: "SeatHoldSweeper noticed the deadline — no message asked it to",
                message: "SeatHoldExpired",
            };
        }

        if (held) {
            return {
                ...base,
                what: "Seats held",
                at: booking.seatsHeldAtUtc,
                state: "done",
                note: `held until ${clock.format(new Date(booking.payBeforeUtc!))}`,
                message: "SeatsHeld",
                // While payment is still outstanding the renderer overwrites the static note
                // above with a live "Xs left to pay" every second, turning amber under 30s —
                // the short-holds switch is only worth having if a viewer can watch the
                // number count down, rather than read a fixed timestamp.
                payBeforeUtc: payment || failed ? null : booking.payBeforeUtc,
            };
        }

        if (failed) {
            return {
                ...base,
                what: "Seats refused",
                at: booking.seatsHeldAtUtc,
                state: "failed",
                // Screenings said why, and Bookings wrote it down verbatim. "Seat G7 is no
                // longer available" belongs on the step that decided it, not only three
                // steps later on the cancellation that followed from it.
                note: booking.cancellationReason ?? "someone else had them, or the film had started",
                message: "SeatHoldRejected",
            };
        }

        return {
            ...base,
            what: "Holding seats…",
            at: booking.seatsHeldAtUtc,
            state: "pending",
            note: null,
            message: null,
        };
    }

    function authorizeStep(): FlowStep {
        const base = {
            ...common("authorized"),
            who: "Bookings — moves to AwaitingPayment and asks to be charged",
            message: held ? "PaymentAuthorizationRequested" : null,
        };

        if (held) {
            return {
                ...base,
                what: "Payment requested",
                at: requested?.atUtc ?? booking.seatsHeldAtUtc,
                state: "done",
                note: null,
            };
        }

        // No hold, no money: this step never ran and never will. Drawing it as "waiting"
        // would leave the page claiming, forever, that something is still coming.
        return failed
            ? { ...base, what: "Never asked", at: null, state: "skipped", note: "there was no hold to pay for" }
            : { ...base, what: "Waiting for the hold…", at: null, state: "waiting", note: null };
    }

    function chargeStep(): FlowStep {
        const base = { ...common("charged"), who: "Payments — publishes the outcome either way" };

        if (payment) {
            const captured = payment.status === "Captured";

            return {
                ...base,
                what: captured ? "Payment captured" : "Payment declined",
                at: payment.decidedAtUtc,
                state: captured ? "done" : "failed",
                note: payment.reference ?? payment.declineReason,
                message: captured ? "PaymentCaptured" : "PaymentDeclined",
            };
        }

        if (failed) {
            return {
                ...base,
                what: "Never charged",
                at: null,
                state: "skipped",
                // Two different stories end here, and they are worth telling apart. Either
                // there was never a hold to pay for, or there was one and it ran out while
                // Payments still had the message — which is what the short-holds switch and
                // "Pause Payments" exist to let you watch happen together.
                note: held
                    ? "the hold ran out before Payments answered"
                    : "the hold was gone before the money was asked for",
                message: null,
            };
        }

        // The chaos strip's "Pause Payments" is named here on purpose: without it, a paused
        // payment and a slow one look identical — both just say "pending".
        const paused = chaosMode === "Paused";

        return {
            ...base,
            what: paused ? "Payments is paused…" : "Taking payment…",
            at: null,
            state: held ? "pending" : "waiting",
            note: paused ? "the message is waiting on the queue, not retrying" : null,
            message: null,
        };
    }

    function settleStep(): FlowStep {
        const base = common("settled");

        if (booking.status === "Confirmed") {
            return {
                ...base,
                what: "Booking confirmed",
                who: "Bookings — publishes BookingConfirmed, and stops caring who listens",
                at: booking.finishedAtUtc,
                state: "done",
                note: null,
                message: "BookingConfirmed",
            };
        }

        if (failed) {
            return {
                ...base,
                what: "Booking cancelled",
                who: "Bookings — publishes BookingCancelled, so Screenings puts the seats back",
                at: booking.finishedAtUtc,
                state: "failed",
                note: booking.cancellationReason,
                message: "BookingCancelled",
            };
        }

        return {
            ...base,
            what: "Waiting…",
            who: "Bookings — publishes BookingConfirmed, so Screenings sells the seats",
            at: null,
            state: "waiting",
            note: null,
            message: null,
        };
    }

    /**
     * One message, two services, neither aware of the other. This is the clearest argument
     * in the demo for publishing rather than calling, and until the step was split out it
     * was drawn nowhere: Screenings turning the hold into a sale left no trace in any read
     * model the page fetches, only a row on the tape.
     */
    function deliverStep(): FlowStep {
        const settlement = entries.find(entry =>
            (entry.event === "BookingConfirmed" || entry.event === "BookingCancelled")
            && entry.kind === "Consumed"
            && entry.service === "screenings");

        const seats: StepOutcome = settlement
            ? {
                service: "screenings",
                what: failed ? "Seats back on sale" : "Seats sold",
                at: settlement.atUtc,
                state: failed ? "failed" : "done",
                note: failed ? "the compensating action, not a rollback" : "the hold became a sale",
            }
            : {
                service: "screenings",
                what: failed ? "Releasing the seats…" : "Selling the seats…",
                at: null,
                state: booking.finishedAtUtc ? "pending" : "waiting",
                note: null,
            };

        const email: StepOutcome = mail
            ? {
                service: "notifications",
                what: mail.outcome,
                at: mail.sentAtUtc,
                state: failed ? "failed" : "done",
                note: mail.subject,
            }
            : {
                service: "notifications",
                what: "E-mail",
                at: null,
                state: booking.finishedAtUtc ? "pending" : "waiting",
                note: null,
            };

        const outcomes = [seats, email];
        const landed = outcomes.filter(outcome => outcome.state === "done" || outcome.state === "failed");

        return {
            ...common("delivered"),
            what: landed.length === 2
                ? (failed ? "Seats released and apology sent" : "Seats sold and ticket sent")
                : landed.length === 1 ? "One of the two has reacted…"
                : "Nobody has reacted yet",
            who: "Screenings and Notifications — same message, two readers, no coordination",
            at: latest(seats.at, email.at),
            state: landed.length === 2 ? (failed ? "failed" : "done")
                : landed.length === 1 ? "pending"
                : booking.finishedAtUtc ? "pending" : "waiting",
            note: null,
            message: null,
            outcomes,
        };
    }
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
