// The flow panel's whole claim is that it is reporting what five services decided, so the
// interesting cases are the ones where a step says something other than the obvious: a
// cancellation that reads as a compensating action, and a payment that is not slow but
// paused. Those are what these cover.

import { expect, test } from "bun:test";

import { formatPayCountdown } from "../api/format.ts";
import type { BookingResponse, NotificationEntry, PaymentListItem } from "../api/types.ts";
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

const sent = (outcome: string): NotificationEntry => ({
    messageId: "m1",
    bookingId: "b1",
    recipient: "ada@example.com",
    subject: "Your tickets for Stalker",
    outcome,
    sentAtUtc: "2026-01-01T12:00:02.000Z",
});

test("a confirmed booking reads as five finished steps", () => {
    const steps = flowSteps(
        booking({ status: "Confirmed", finishedAtUtc: "2026-01-01T12:00:01.500Z" }),
        captured(),
        sent("Sent"),
        "None");

    expect(steps.map(step => step.state)).toEqual(["done", "done", "done", "done", "done"]);
    expect(steps.map(step => step.what)).toEqual([
        "Booking placed", "Seats held", "Payment captured", "Booking confirmed", "Sent",
    ]);

    // The payment reference is the note; the countdown is over, so nothing is ticking.
    expect(steps[2]!.note).toBe("AUTH-4711");
    expect(steps[1]!.payBeforeUtc).toBeNull();
});

test("a declined payment cancels the booking and the last three steps say so", () => {
    const steps = flowSteps(
        booking({
            status: "Cancelled",
            finishedAtUtc: "2026-01-01T12:00:01.500Z",
            cancellationReason: "Payment declined",
        }),
        declined(),
        sent("Sent · payment declined"),
        "None");

    expect(steps.map(step => step.state)).toEqual(["done", "done", "failed", "failed", "failed"]);
    expect(steps[2]!.what).toBe("Payment declined");
    expect(steps[2]!.note).toBe("insufficient funds");
    expect(steps[3]!.what).toBe("Booking cancelled");
    expect(steps[3]!.who).toContain("puts the seats back");

    expect(flowNote(booking({ status: "Cancelled" })))
        .toBe("The seats went straight back on sale — that is the compensating action, not a rollback.");
});

test("a booking still in flight is pending on payment and waiting on everything after it", () => {
    const steps = flowSteps(booking(), null, null, "None");

    expect(steps.map(step => step.state)).toEqual(["done", "done", "pending", "waiting", "waiting"]);
    expect(steps[2]!.what).toBe("Taking payment…");
    expect(steps[2]!.note).toBeNull();
    expect(steps[3]!.what).toBe("Waiting…");
    expect(steps[4]!.what).toBe("E-mail");

    // The hold is the thing being waited on, so this step carries the deadline the
    // countdown ticks against.
    expect(steps[1]!.payBeforeUtc).toBe("2026-01-01T12:03:00.000Z");
    expect(steps[1]!.note).toContain("held until");
});

test("Pause Payments is named, because a paused payment and a slow one look identical", () => {
    const steps = flowSteps(booking(), null, null, "Paused");

    expect(steps[2]!.what).toBe("Payments is paused…");
    expect(steps[2]!.note).toBe("the message is waiting on the queue, not retrying");
    expect(steps[2]!.state).toBe("pending");
});

test("a booking the read model has not caught up with is still only holding seats", () => {
    const steps = flowSteps(
        booking({ filmTitle: "(unknown)", seatsHeldAtUtc: null, payBeforeUtc: null }),
        null, null, "None");

    expect(steps[1]!.what).toBe("Holding seats…");
    expect(steps[1]!.state).toBe("pending");
    expect(steps[1]!.payBeforeUtc).toBeNull();

    // …unless it was cancelled before the seats were ever held, which is a failure, not
    // a step still in progress.
    const cancelled = flowSteps(
        booking({ filmTitle: "(unknown)", status: "Cancelled", seatsHeldAtUtc: null, payBeforeUtc: null }),
        null, null, "None");

    expect(cancelled[1]!.what).toBe("Seats held");
    expect(cancelled[1]!.state).toBe("failed");
});

test("the markers are the four glyphs the timeline draws", () => {
    expect(markerFor("done")).toBe("✓");
    expect(markerFor("failed")).toBe("✕");
    expect(markerFor("pending")).toBe("·");
    expect(markerFor("waiting")).toBe("");
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
