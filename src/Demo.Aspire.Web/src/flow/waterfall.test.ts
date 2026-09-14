// The waterfall is reconstructed, not measured, so the arithmetic that reconstructs it is
// the part worth pinning down: which rows pair into a hop, which row is deliberately not
// one, and where the queue wait lands once everything is a percentage of the total.

import { expect, test } from "bun:test";

import type { BusTapeEntry, BusTapeKind, ServiceName } from "../api/types.ts";
import { buildHops, buildWaterfall } from "./waterfall.ts";

const PLACED = "2026-01-01T12:00:00.000Z";

/** `atMs` is milliseconds after the booking was placed, which is how the panel reads. */
const entry = (
    event: string,
    kind: BusTapeKind,
    service: ServiceName,
    atMs: number,
    durationMs: number | null = null,
): BusTapeEntry => ({
    messageId: `${event}:${kind}:${service}`,
    correlationId: "b1",
    event,
    service,
    kind,
    atUtc: new Date(Date.parse(PLACED) + atMs).toISOString(),
    durationMs,
    traceId: null,
    detail: null,
});

test("a publish and the first consume of the same event make one hop", () => {
    const hops = buildHops([
        entry("BookingPlaced", "Published", "bookings", 0),
        entry("BookingPlaced", "Consumed", "screenings", 100, 20),
        // Notifications consumes the same event, but the hop already has its consume —
        // the first one is what the waterfall is timing.
        entry("BookingPlaced", "Consumed", "notifications", 300, 5),
    ]);

    expect(hops).toHaveLength(1);
    expect(hops[0]!.published.service).toBe("bookings");
    expect(hops[0]!.consumed?.service).toBe("screenings");
});

test("a Faulted row is a retry, not a completed hop, so it is left out", () => {
    const hops = buildHops([
        entry("SeatsHeld", "Published", "screenings", 0),
        entry("SeatsHeld", "Faulted", "payments", 50, 10),
    ]);

    expect(hops[0]!.consumed).toBeNull();

    // And a faulted row on its own is not a hop at all: nothing published it here.
    expect(buildHops([entry("SeatsHeld", "Faulted", "payments", 50, 10)])).toHaveLength(0);
});

test("a published event nobody has consumed yet is still a hop", () => {
    const hops = buildHops([entry("BookingConfirmed", "Published", "bookings", 900)]);

    expect(hops).toHaveLength(1);
    expect(hops[0]!.consumed).toBeNull();
});

test("nothing on the tape means nothing to draw", () => {
    expect(buildWaterfall([], PLACED)).toBeNull();
});

test("hops lay out as percentages of the slowest one, in service-lane order", () => {
    const layout = buildWaterfall([
        entry("BookingPlaced", "Published", "bookings", 0),
        entry("BookingPlaced", "Consumed", "screenings", 100, 20),
        entry("SeatsHeld", "Published", "screenings", 120),
        entry("SeatsHeld", "Consumed", "payments", 400, 50),
    ], PLACED)!;

    expect(layout.totalMs).toBe(400);

    // Screenings comes before payments because SERVICE_LANE_ORDER says so, not because
    // it happened to consume first.
    expect(layout.lanes.map(lane => lane.service)).toEqual(["screenings", "payments"]);

    // The consume started 20ms before it was recorded, so the bar runs 80ms → 100ms.
    expect(layout.lanes[0]!.bars[0]).toMatchObject({
        left: "20.00%",
        width: "5.00%",
        title: "screenings: 20 ms",
    });
});

test("the widest queue wait is the one that gets a label", () => {
    const layout = buildWaterfall([
        entry("BookingPlaced", "Published", "bookings", 0),
        entry("BookingPlaced", "Consumed", "screenings", 100, 20),
        entry("SeatsHeld", "Published", "screenings", 120),
        entry("SeatsHeld", "Consumed", "payments", 400, 50),
    ], PLACED)!;

    // 0ms → 80ms waiting for Screenings, 120ms → 350ms waiting for Payments.
    expect(layout.gaps.map(gap => gap.width)).toEqual(["20.00%", "57.50%"]);

    expect(layout.gaps[0]!.label).toBeNull();
    expect(layout.gaps[1]!.label).toEqual({ text: "230 ms queue wait", left: "58.75%" });
});

test("a consume with no queue wait in front of it draws no gap", () => {
    const layout = buildWaterfall([
        entry("BookingPlaced", "Published", "bookings", 0),
        entry("BookingPlaced", "Consumed", "screenings", 40, 40),
    ], PLACED)!;

    expect(layout.gaps).toHaveLength(0);
    expect(layout.lanes).toHaveLength(1);
    expect(layout.lanes[0]!.bars[0]!.width).toBe("100.00%");
});

test("an unconsumed hop keeps the axis honest without claiming a lane", () => {
    const layout = buildWaterfall([
        entry("BookingPlaced", "Published", "bookings", 0),
        entry("BookingPlaced", "Consumed", "screenings", 100, 20),
        entry("BookingConfirmed", "Published", "bookings", 800),
    ], PLACED)!;

    expect(layout.totalMs).toBe(800);
    expect(layout.lanes).toHaveLength(1);
    expect(layout.lanes[0]!.service).toBe("screenings");
});
