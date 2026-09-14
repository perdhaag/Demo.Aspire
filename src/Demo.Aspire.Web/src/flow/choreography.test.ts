// The table in choreography.ts is the join between the flow panel, the bus tape and the
// waterfall. If it is wrong, all three agree with each other and all three are wrong
// together — which is the worst failure this page can have, because it would look right.
//
// So these tests are mostly about coverage rather than behaviour: every integration event
// in Demo.Aspire.Contracts has to be routed, every consumer in the services has to be
// accounted for, and the ordinals have to be 1…6 with nothing missing.

import { expect, test } from "bun:test";

import type { BusTapeEntry, BusTapeKind, ServiceName } from "../api/types.ts";
import { STEPS, entriesForStep, firstSeen, stepById, stepOfEntry } from "./choreography.ts";

const entry = (event: string, kind: BusTapeKind, service: ServiceName): BusTapeEntry => ({
    messageId: `${event}:${kind}:${service}`,
    correlationId: "b1",
    event,
    service,
    kind,
    atUtc: "2026-01-01T12:00:00.000Z",
    durationMs: null,
    traceId: null,
    detail: null,
});

/** Every record in Demo.Aspire.Contracts that implements IIntegrationEvent. */
const CONTRACTS = [
    "BookingPlaced", "BookingConfirmed", "BookingCancelled",
    "SeatsHeld", "SeatHoldRejected", "SeatHoldExpired",
    "PaymentAuthorizationRequested", "PaymentCaptured", "PaymentDeclined",
] as const;

/** Every IConsumer<T> across the four services, as of this commit. */
const CONSUMERS: readonly [string, ServiceName][] = [
    ["BookingPlaced", "screenings"],       // HoldSeatsConsumer
    ["BookingConfirmed", "screenings"],    // SellSeatsConsumer
    ["BookingCancelled", "screenings"],    // ReleaseSeatsConsumer
    ["SeatsHeld", "bookings"],             // AwaitPayment
    ["SeatHoldRejected", "bookings"],      // CancelBooking
    ["SeatHoldExpired", "bookings"],       // CancelBooking
    ["PaymentDeclined", "bookings"],       // CancelBooking
    ["PaymentCaptured", "bookings"],       // ConfirmBooking
    ["PaymentAuthorizationRequested", "payments"], // AuthorizePayment
    ["BookingConfirmed", "notifications"], // SendTicketConsumer
    ["BookingCancelled", "notifications"], // SendCancellationConsumer
];

test("the six steps are 1…6, each with a service and a transport", () => {
    expect(STEPS.map(step => step.ordinal)).toEqual([1, 2, 3, 4, 5, 6]);
    expect(STEPS.every(step => step.services.length > 0)).toBe(true);

    // One step over HTTP, one step whose messages a timer can raise, four plain bus hops.
    expect(STEPS.filter(step => step.transport === "http").map(step => step.id)).toEqual(["placed"]);

    // Only the last step has more than one service, because only it is a fan-out.
    expect(STEPS.filter(step => step.services.length > 1).map(step => step.id))
        .toEqual(["delivered"]);

    expect(STEPS.every(step => step.touches.length > 0)).toBe(true);
    expect(STEPS.map(step => stepById(step.id))).toEqual([...STEPS]);
});

test("every published contract belongs to exactly one step", () => {
    const routed = CONTRACTS.map(event => stepOfEntry(entry(event, "Published", "bookings")));

    expect(routed.filter(step => step === null)).toHaveLength(0);

    // And each one is named by the definition of the step it is routed to, so the chips in
    // the flow panel cannot advertise a message the tape would file somewhere else.
    for (const event of CONTRACTS) {
        const step = stepOfEntry(entry(event, "Published", "bookings"))!;
        expect(stepById(step).publishes).toContain(event);
    }
});

test("every consumer in the four services is routed to a step", () => {
    for (const [event, service] of CONSUMERS) {
        expect(stepOfEntry(entry(event, "Consumed", service))).not.toBeNull();
    }
});

test("a publish and its consume are different steps — the gap between them is the queue", () => {
    // This is the distinction the whole table exists for. BookingPlaced published is step 1
    // finishing; BookingPlaced consumed is step 2 starting. Collapsing the two would put
    // the queue wait inside a step instead of between two of them.
    expect(stepOfEntry(entry("BookingPlaced", "Published", "bookings"))).toBe("placed");
    expect(stepOfEntry(entry("BookingPlaced", "Consumed", "screenings"))).toBe("held");

    expect(stepOfEntry(entry("PaymentCaptured", "Published", "payments"))).toBe("charged");
    expect(stepOfEntry(entry("PaymentCaptured", "Consumed", "bookings"))).toBe("settled");
});

test("one message consumed by two services is the same step for both", () => {
    expect(stepOfEntry(entry("BookingConfirmed", "Consumed", "screenings"))).toBe("delivered");
    expect(stepOfEntry(entry("BookingConfirmed", "Consumed", "notifications"))).toBe("delivered");
});

test("a fault is routed like the consume that threw, because that is what it is", () => {
    expect(stepOfEntry(entry("PaymentAuthorizationRequested", "Faulted", "payments")))
        .toBe("charged");
});

test("a message the table has never heard of is left unrouted rather than guessed at", () => {
    expect(stepOfEntry(entry("RefundIssued", "Published", "payments"))).toBeNull();

    // A real event consumed by a service that does not consume it is equally unrouted —
    // the routing is per consumer, not per event.
    expect(stepOfEntry(entry("PaymentCaptured", "Consumed", "notifications"))).toBeNull();
});

test("entriesForStep picks a step's rows out of a whole flow", () => {
    const entries = [
        entry("BookingPlaced", "Published", "bookings"),
        entry("BookingPlaced", "Consumed", "screenings"),
        entry("SeatsHeld", "Published", "screenings"),
        entry("SeatsHeld", "Consumed", "bookings"),
    ];

    expect(entriesForStep(entries, "held").map(row => row.messageId)).toEqual([
        "BookingPlaced:Consumed:screenings",
        "SeatsHeld:Published:screenings",
    ]);

    expect(entriesForStep(entries, "charged")).toHaveLength(0);
});

test("firstSeen finds the publish, and nothing when the message never happened", () => {
    const entries = [
        entry("SeatsHeld", "Consumed", "bookings"),
        entry("SeatsHeld", "Published", "screenings"),
    ];

    expect(firstSeen(entries, "SeatsHeld")?.service).toBe("screenings");
    expect(firstSeen(entries, "SeatsHeld", "Consumed")?.service).toBe("bookings");
    expect(firstSeen(entries, "SeatHoldExpired")).toBeNull();
});
