// The choreography, written down once.
//
// The page draws one booking's journey three times over — as six steps, as a tape of
// messages, and as a trace waterfall — and until this file existed nothing said that they
// were the same thing. A reader had to join them by hand: is "SeatsHeld consumed" the step
// that says "Seats held", or the one after it? Which bar is it?
//
// So: every step has an id, and every row on the bus tape can be asked which step it
// belongs to. That single answer is what lets a hover on one of them light up the other
// two — see flow/focus.ts for the highlight and app.css for the dimming.
//
// Nothing on the server knows about any of this. There is no saga and no state machine;
// `Booking` is the process manager and its legal transitions live in the aggregate. What
// crosses the wire is a BusTapEntry carrying an event name, a kind, a service and a
// correlation id, and that is enough — the table below is the static knowledge that turns
// those four fields into "step 4".
//
// The rule the table follows, uniformly: **one step is one service making one decision and
// publishing one message.** That is also why there are six steps here and not the five the
// page used to draw — "taking payment" was two services, so it was the one step that could
// not be attributed to anybody.

import type { BusTapeEntry, ServiceName } from "../api/types.ts";

export type FlowStepId = "placed" | "held" | "authorized" | "charged" | "settled" | "delivered";

/** How a step was reached. The demo's whole argument is that only the first is HTTP. */
export type Transport = "http" | "bus" | "clock";

export type Infra = "http" | "postgres" | "rabbitmq" | "redis" | "smtp";

/** One thing a step touched, as a chip: the infrastructure, and why it was there. */
export interface Touch {
    infra: Infra;
    what: string;
}

export interface StepDefinition {
    id: FlowStepId;
    /** 1…6. After this change these are the only numerals on the page that mean "step". */
    ordinal: number;
    /** Who decides. Two services only on `delivered`, which is a fan-out of one message. */
    services: readonly ServiceName[];
    transport: Transport;
    /** The messages this step publishes — which one it was depends on how it went. */
    publishes: readonly string[];
    touches: readonly Touch[];
}

export const STEPS: readonly StepDefinition[] = [
    {
        id: "placed",
        ordinal: 1,
        services: ["bookings"],
        transport: "http",
        publishes: ["BookingPlaced"],
        touches: [
            // The one synchronous call between two contexts in the whole system, and an
            // anti-corruption layer rather than a shared table — see ScreeningCatalogClient.
            { infra: "http", what: "Bookings asks Screenings for the offer" },
            { infra: "postgres", what: "aggregate and outbox row commit together" },
            { infra: "rabbitmq", what: "BookingPlaced leaves the outbox" },
        ],
    },
    {
        id: "held",
        ordinal: 2,
        services: ["screenings"],
        transport: "bus",
        publishes: ["SeatsHeld", "SeatHoldRejected", "SeatHoldExpired"],
        touches: [
            { infra: "postgres", what: "xmin decides a race; the inbox drops a redelivery" },
            { infra: "redis", what: "the seat-map projection is invalidated" },
        ],
    },
    {
        id: "authorized",
        ordinal: 3,
        services: ["bookings"],
        transport: "bus",
        publishes: ["PaymentAuthorizationRequested"],
        touches: [
            { infra: "postgres", what: "the booking moves to AwaitingPayment" },
        ],
    },
    {
        id: "charged",
        ordinal: 4,
        services: ["payments"],
        transport: "bus",
        publishes: ["PaymentCaptured", "PaymentDeclined"],
        touches: [
            { infra: "postgres", what: "the Payment aggregate records the decision" },
            // Not infrastructure the demo runs, which is exactly what the chaos strip is
            // switching when it pauses, slows or breaks "the card network".
            { infra: "http", what: "the simulated card network answers" },
        ],
    },
    {
        id: "settled",
        ordinal: 5,
        services: ["bookings"],
        transport: "bus",
        publishes: ["BookingConfirmed", "BookingCancelled"],
        touches: [
            { infra: "postgres", what: "Booking.Confirm or Booking.Cancel, then the outbox" },
        ],
    },
    {
        id: "delivered",
        ordinal: 6,
        services: ["screenings", "notifications"],
        transport: "bus",
        publishes: [],
        touches: [
            { infra: "postgres", what: "Screenings turns the hold into a sale, or releases it" },
            { infra: "redis", what: "Notifications has no database: SET NX is its ledger" },
            { infra: "smtp", what: "the ticket, or the apology, reaches Mailpit" },
        ],
    },
];

export const stepById = (id: FlowStepId): StepDefinition =>
    // Every id in the union is in the table above, so this cannot miss.
    STEPS.find(step => step.id === id)!;

/**
 * Where each message belongs.
 *
 * `published` is the step whose *outcome* the message announces; `consumed` is the step
 * whose *work* it starts, which is why the two differ for every message here. BookingPlaced
 * published is step 1 finishing; BookingPlaced consumed is step 2 beginning. The stretch
 * between them is the queue wait the waterfall draws as a hairline, and it belongs to
 * neither step — it is the gap between them, which is the point.
 *
 * The consume side is keyed by service because one message can start two different services'
 * work: BookingConfirmed is consumed by Screenings (sell the seats) and by Notifications
 * (send the ticket), independently, neither knowing about the other.
 */
interface Routing {
    published: FlowStepId;
    consumed: Partial<Record<ServiceName, FlowStepId>>;
}

const ROUTING: Record<string, Routing> = {
    BookingPlaced: { published: "placed", consumed: { screenings: "held" } },

    SeatsHeld: { published: "held", consumed: { bookings: "authorized" } },
    SeatHoldRejected: { published: "held", consumed: { bookings: "settled" } },
    // The only message in the system published by a clock rather than by another message.
    SeatHoldExpired: { published: "held", consumed: { bookings: "settled" } },

    PaymentAuthorizationRequested: { published: "authorized", consumed: { payments: "charged" } },

    PaymentCaptured: { published: "charged", consumed: { bookings: "settled" } },
    PaymentDeclined: { published: "charged", consumed: { bookings: "settled" } },

    BookingConfirmed: {
        published: "settled",
        consumed: { screenings: "delivered", notifications: "delivered" },
    },
    BookingCancelled: {
        published: "settled",
        consumed: { screenings: "delivered", notifications: "delivered" },
    },
};

/**
 * Which step a tape row belongs to, or null for a message the table has never heard of —
 * a new contract, say. An unrecognised row still draws; it simply never lights up, the
 * way `Pill` renders a status the stylesheet has no colour for.
 *
 * A Faulted row is a consume that threw, so it is routed like a consume: the retry that
 * MassTransit is about to make belongs to the step whose work failed.
 */
export function stepOfEntry(entry: BusTapeEntry): FlowStepId | null {
    const routing = ROUTING[entry.event];
    if (!routing) return null;

    return entry.kind === "Published"
        ? routing.published
        : routing.consumed[entry.service] ?? null;
}

/** The tape rows belonging to one step, in arrival order. */
export const entriesForStep = (
    entries: readonly BusTapeEntry[],
    id: FlowStepId,
): readonly BusTapeEntry[] => entries.filter(entry => stepOfEntry(entry) === id);

/** The first time an event was seen, in either direction. Used to date steps the read
    models cannot date — Bookings never records when it asked to be charged. */
export function firstSeen(
    entries: readonly BusTapeEntry[],
    event: string,
    kind: BusTapeEntry["kind"] = "Published",
): BusTapeEntry | null {
    return entries.find(entry => entry.event === event && entry.kind === kind) ?? null;
}
