// The two pure helpers behind every line of the tape. They are worth a test because the
// tape is the page's evidence: an entry phrased wrongly — a consume reading as a publish,
// a duration silently dropped — makes the choreography look like something it is not.

import { describe, expect, test } from "bun:test";

import type { BusTapeEntry } from "../api/types.ts";
import { tapeRowDetail, tapeRowLabel } from "./TapeRow.tsx";

const entry = (over: Partial<BusTapeEntry>): BusTapeEntry => ({
    messageId: "m1",
    correlationId: "b1",
    event: "SeatsHeld",
    service: "bookings",
    kind: "Published",
    atUtc: "2026-09-14T10:00:00.000Z",
    durationMs: null,
    traceId: null,
    detail: null,
    ...over,
});

describe("tapeRowLabel", () => {
    test("names the verb for each kind", () => {
        expect(tapeRowLabel(entry({ kind: "Published" }))).toBe("SeatsHeld published");
        expect(tapeRowLabel(entry({ kind: "Consumed" }))).toBe("SeatsHeld consumed");
        expect(tapeRowLabel(entry({ kind: "Faulted" }))).toBe("SeatsHeld failed on");
    });
});

describe("tapeRowDetail", () => {
    test("is empty when the service said nothing and took no measurable time", () => {
        expect(tapeRowDetail(entry({}))).toBe("");
    });

    test("rounds the duration rather than printing the wire's fractional milliseconds", () => {
        expect(tapeRowDetail(entry({ durationMs: 12.6 }))).toBe("13 ms");
    });

    test("joins duration and detail in that order", () => {
        expect(tapeRowDetail(entry({ durationMs: 4, detail: "seats A1, A2" })))
            .toBe("4 ms — seats A1, A2");
    });

    test("keeps a zero duration, which is not the same as no duration at all", () => {
        expect(tapeRowDetail(entry({ durationMs: 0 }))).toBe("0 ms");
    });
});
