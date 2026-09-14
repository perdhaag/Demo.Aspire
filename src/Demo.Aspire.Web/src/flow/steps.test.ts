// The flow panel's whole claim is that it is reporting what five services decided, so the
// interesting cases are the ones where a step says something other than the obvious: a
// cancellation that reads as a compensating action, a payment that is not slow but paused,
// and — since the six-step split — a step that never ran at all rather than one still
// waiting for something that is never coming.

import { expect, test } from "bun:test";

import { formatPayCountdown } from "../api/format.ts";
import type {
    BookingResponse, BusTapeEntry, BusTapeKind, NotificationEntry, PaymentListItem, ServiceName,
} from "../api/types.ts";
import { flowNote, flowSteps, markerFor } from "./steps.ts";

const PLACED = "2026-01-01T12:00:00.000Z";

const booking = (overrides: Partial<BookingResponse> = {}): BookingResponse => ({
    bookingId: "b1",
    screeningId: "s1",
    filmTitle: "Stalker",
    auditorium: "Sal 1",
    screeningStartsAtUtc: "2026-01-01T19:00:00.000Z",
    customerEmail: "ada@example.com",
    seats: ["A1", "A2"],
    total: 260,
    currency: "NOK",
    status: "AwaitingPayment",
    placedAtUtc: PLACED,
    seatsHeldAtUtc: "2026-01-01T12:00:00.200Z",
    finishedAtUtc: null,
    payBeforeUtc: "2026-01-01T12:03:00.000Z",
    paymentReference: null,
    cancellationReason: null,
    ...overrides,
});

const captured = (): PaymentListItem => ({
    paymentId: "p1",
    bookingId: "b1",
    payer: "ada@example.com",
    amount: 260,
    currency: "NOK",
    status: "Captured",
    reference: "AUTH-4711",
    declineReason: null,
    decidedAtUtc: "2026-01-01T12:00:01.000Z",
});

const declined = (): PaymentListItem => ({
    ...captured(),
    status: "Declined",
    reference: null,
    declineReason: "insufficient funds",
});

/** A tape row for this booking. The flow reads the tape for the things no read model
    records: when Bookings asked to be charged, and whether Screenings has sold the seats. */
const tape = (
    event: string,
    kind: BusTapeKind,
    service: ServiceName,
    atUtc = "2026-01-01T12:00:00.500Z",
): BusTapeEntry => ({
    messageId: `${event}:${kind}:${service}`,
    correlationId: "b1",
    event,
    service,
    kind,
    atUtc,
    durationMs: null,
    traceId: null,
    detail: null,
});

/** Both halves of step 6 having landed: the seats sold, the mail sent. */
const delivered = (event: "BookingConfirmed" | "BookingCancelled"): BusTapeEntry[] => [
    tape(event, "Published", "bookings"),
    tape(event, "Consumed", "screenings", "2026-01-01T12:00:01.800Z"),
];

const sent = (outcome: string): NotificationEntry => ({
    messageId: "m1",
    bookingId: "b1",
    recipient: "ada@example.com",
    subject: "Your tickets for Stalker",
    outcome,
    sentAtUtc: "2026-01-01T12:00:02.000Z",
});

test("a confirmed booking reads as six finished steps, one service each", () => {
    const steps = flowSteps(
        booking({ status: "Confirmed", finishedAtUtc: "2026-01-01T12:00:01.500Z" }),
        captured(),
        sent("Sent"),
        "None",
        delivered("BookingConfirmed"));

    expect(steps.map(step => step.state))
        .toEqual(["done", "done", "done", "done", "done", "done"]);

    expect(steps.map(step => step.what)).toEqual([
        "Booking placed", "Seats held", "Payment requested", "Payment captured",
        "Booking confirmed", "Seats sold and ticket sent",
    ]);

    // The ordinals are what the bus tape stamps on this flow's rows, so they have to be
    // 1…6 with nothing skipped — that is the join between the two views.
    expect(steps.map(step => step.ordinal)).toEqual([1, 2, 3, 4, 5, 6]);
    expect(steps.map(step => step.id)).toEqual([
        "placed", "held", "authorized", "charged", "settled", "delivered",
    ]);

    // One step, and only one, was reached over HTTP. That is the demo's whole argument.
    expect(steps.filter(step => step.transport === "http").map(step => step.id)).toEqual(["placed"]);

    // The payment reference is the note; the countdown is over, so nothing is ticking.
    expect(steps[3]!.note).toBe("AUTH-4711");
    expect(steps[1]!.payBeforeUtc).toBeNull();
});

test("step 6 is one message and two services, drawn as both", () => {
    const steps = flowSteps(
        booking({ status: "Confirmed", finishedAtUtc: "2026-01-01T12:00:01.500Z" }),
        captured(),
        sent("Sent"),
        "None",
        delivered("BookingConfirmed"));

    expect(steps[5]!.outcomes.map(outcome => [outcome.service, outcome.what])).toEqual([
        ["screenings", "Seats sold"],
        ["notifications", "Sent"],
    ]);

    // Screenings turning the hold into a sale leaves no trace in any read model the page
    // fetches — only a row on the tape — so without entries it is still outstanding.
    const blind = flowSteps(
        booking({ status: "Confirmed", finishedAtUtc: "2026-01-01T12:00:01.500Z" }),
        captured(), sent("Sent"), "None");

    expect(blind[5]!.state).toBe("pending");
    expect(blind[5]!.outcomes[0]!.state).toBe("pending");
});

test("a declined payment cancels the booking and the last three steps say so", () => {
    const cancelled = booking({
        status: "Cancelled",
        finishedAtUtc: "2026-01-01T12:00:01.500Z",
        cancellationReason: "Payment declined",
    });

    const steps = flowSteps(
        cancelled,
        declined(),
        sent("Sent · payment declined"),
        "None",
        delivered("BookingCancelled"));

    expect(steps.map(step => step.state))
        .toEqual(["done", "done", "done", "failed", "failed", "failed"]);

    expect(steps[3]!.what).toBe("Payment declined");
    expect(steps[3]!.note).toBe("insufficient funds");
    expect(steps[4]!.what).toBe("Booking cancelled");
    expect(steps[4]!.who).toContain("puts the seats back");

    // The seats going back on sale is a step that happened, not an absence of one.
    expect(steps[5]!.outcomes[0]!.what).toBe("Seats back on sale");
    expect(steps[5]!.outcomes[0]!.note).toContain("compensating action");

    expect(flowNote(booking({ status: "Cancelled" })))
        .toBe("The seats went straight back on sale — that is the compensating action, not a rollback.");
});

test("a refused hold skips the two steps that never ran", () => {
    // No hold means nobody ever asked to be charged and nobody ever charged anything.
    // Drawing those as "waiting" would leave the page claiming, for good, that something
    // is still on its way.
    const steps = flowSteps(
        booking({
            status: "Cancelled",
            seatsHeldAtUtc: null,
            payBeforeUtc: null,
            finishedAtUtc: "2026-01-01T12:00:00.400Z",
            cancellationReason: "Seats unavailable",
        }),
        null, null, "None");

    expect(steps.map(step => step.state))
        // Step 6 is pending, not waiting: BookingCancelled is out, and Screenings and
        // Notifications simply have not been heard from yet.
        .toEqual(["done", "failed", "skipped", "skipped", "failed", "pending"]);

    expect(steps[1]!.what).toBe("Seats refused");
    expect(steps[1]!.message).toBe("SeatHoldRejected");

    // Screenings' own reason, on the step that decided it — not only three steps later on
    // the cancellation that followed from it.
    expect(steps[1]!.note).toBe("Seats unavailable");

    expect(steps[2]!.what).toBe("Never asked");
    expect(steps[3]!.what).toBe("Never charged");
    expect(steps[3]!.note).toBe("the hold was gone before the money was asked for");
});

test("a lapsed hold is the one step a timer reached, and it says so", () => {
    const steps = flowSteps(
        booking({
            status: "Cancelled",
            finishedAtUtc: "2026-01-01T12:03:01.000Z",
            cancellationReason: "Seat hold expired",
        }),
        null, null, "None",
        [tape("SeatHoldExpired", "Published", "screenings", "2026-01-01T12:03:00.000Z")]);

    expect(steps[1]!.what).toBe("Hold expired");
    expect(steps[1]!.state).toBe("failed");

    // Every other step in the flow was reached by a message. This one was reached by
    // SeatHoldSweeper noticing a deadline, which is the only clock in the system.
    expect(steps[1]!.transport).toBe("clock");
    expect(steps.filter(step => step.transport === "clock")).toHaveLength(1);

    // The seats *were* held and Bookings *did* ask to be charged, so the step that never
    // happened is the charge itself — and for the other reason than a refused hold.
    expect(steps[2]!.state).toBe("done");
    expect(steps[3]!.what).toBe("Never charged");
    expect(steps[3]!.note).toBe("the hold ran out before Payments answered");
});

test("a booking still in flight is pending on payment and waiting on everything after it", () => {
    const steps = flowSteps(booking(), null, null, "None");

    expect(steps.map(step => step.state))
        .toEqual(["done", "done", "done", "pending", "waiting", "waiting"]);

    expect(steps[3]!.what).toBe("Taking payment…");
    expect(steps[3]!.note).toBeNull();
    expect(steps[4]!.what).toBe("Waiting…");
    expect(steps[5]!.outcomes[1]!.what).toBe("E-mail");

    // The hold is the thing being waited on, so this step carries the deadline the
    // countdown ticks against.
    expect(steps[1]!.payBeforeUtc).toBe("2026-01-01T12:03:00.000Z");
    expect(steps[1]!.note).toContain("held until");
});

test("Pause Payments is named, because a paused payment and a slow one look identical", () => {
    const steps = flowSteps(booking(), null, null, "Paused");

    expect(steps[3]!.what).toBe("Payments is paused…");
    expect(steps[3]!.note).toBe("the message is waiting on the queue, not retrying");
    expect(steps[3]!.state).toBe("pending");
});

test("a booking the read model has not caught up with is still only holding seats", () => {
    const steps = flowSteps(
        booking({ filmTitle: "(unknown)", seatsHeldAtUtc: null, payBeforeUtc: null }),
        null, null, "None");

    expect(steps[1]!.what).toBe("Holding seats…");
    expect(steps[1]!.state).toBe("pending");
    expect(steps[1]!.payBeforeUtc).toBeNull();

    // The steps after it have not been skipped — nothing has decided anything yet.
    expect(steps[2]!.state).toBe("waiting");
    expect(steps[3]!.state).toBe("waiting");
});

test("the tape dates the step Bookings never wrote down", () => {
    // Bookings records when the seats were held and when the booking finished, but never
    // when it asked to be charged. The only record of that is the message itself.
    const steps = flowSteps(booking(), null, null, "None",
        [tape("PaymentAuthorizationRequested", "Published", "bookings", "2026-01-01T12:00:00.310Z")]);

    expect(steps[2]!.at).toBe("2026-01-01T12:00:00.310Z");
    expect(steps[2]!.message).toBe("PaymentAuthorizationRequested");

    // Without the tape it falls back to the hold, which is the closest thing it knows.
    expect(flowSteps(booking(), null, null, "None")[2]!.at).toBe("2026-01-01T12:00:00.200Z");
});

test("the markers are the glyphs the timeline draws beside a settled step", () => {
    expect(markerFor("done")).toBe("✓");
    expect(markerFor("failed")).toBe("✕");
    expect(markerFor("pending")).toBe("·");
    expect(markerFor("waiting")).toBe("");
    expect(markerFor("skipped")).toBe("–");
});

test("a confirmed booking's note names the seats and points at the inbox", () => {
    const note = flowNote(booking({ status: "Confirmed" }));

    expect(note).toContain("Seats A1, A2 for Stalker are yours.");
    expect(note).toContain("Mailpit");
});

test("the countdown counts seconds, then minutes, then gives up", () => {
    expect(formatPayCountdown(0)).toBe("expiring now…");
    expect(formatPayCountdown(-5_000)).toBe("expiring now…");

    // Rounded up, so a hold with 29.4s left says 30s rather than jumping two seconds at
    // the first tick.
    expect(formatPayCountdown(29_400)).toBe("30s left to pay");
    expect(formatPayCountdown(1)).toBe("1s left to pay");
    expect(formatPayCountdown(150_000)).toBe("2m 30s left to pay");
    expect(formatPayCountdown(60_000)).toBe("1m 0s left to pay");
});
