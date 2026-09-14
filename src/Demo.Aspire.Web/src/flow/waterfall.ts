// The trace waterfall, built entirely from the bus tape's own timestamps — not from a
// real trace backend, which is exactly why the caption beside it says so. Fine to within
// a millisecond or two on one machine sharing one clock; not a substitute for the Aspire
// dashboard's own trace view, which is what the link beside it is for.
//
// Everything here is arithmetic over entries the tape store already holds, with no DOM
// and no React in it: the component below it only has to turn percentages into inline
// styles. That keeps the interesting parts — pairing a publish with its consume, working
// out where the queue wait went — testable against a handful of literal entries.
//
// Every bar and every gap now also carries the step it belongs to, so that focusing a step
// in the flow panel dims everything here that is not part of it. The step ids come from
// flow/choreography.ts, the same table the tape rows are stamped from.

import type { BusTapeEntry, IsoDate, ServiceName } from "../api/types.ts";
import { stepOfEntry, type FlowStepId } from "./choreography.ts";

export const SERVICE_LANE_ORDER: readonly ServiceName[] =
    ["screenings", "bookings", "payments", "notifications", "gateway"];

/** A published event and one consume of it, if one has been seen yet. */
export interface Hop {
    published: BusTapeEntry;
    consumed: BusTapeEntry | null;
}

/**
 * Pairs each event's Published row with every service that consumed it — one hop per
 * consumer, not one per event.
 *
 * That distinction is the whole point of the panel. `BookingConfirmed` is consumed by
 * Screenings (sell the seats) *and* by Notifications (send the ticket), independently and
 * with neither aware of the other; `BookingCancelled` likewise. Keeping only the first
 * consume, as this used to, silently dropped one of the two — so the clearest argument in
 * the demo for publishing rather than calling was the one thing the waterfall never drew.
 *
 * A Faulted row is a retry that did not (yet) finish the hop, so it is left out here,
 * though it already showed up in the tape rail above.
 */
export function buildHops(entries: readonly BusTapeEntry[]): Hop[] {
    const published = new Map<string, BusTapeEntry>();
    const consumes: BusTapeEntry[] = [];
    const seen = new Set<string>();

    for (const entry of entries) {
        if (entry.kind === "Published") {
            if (!published.has(entry.event)) published.set(entry.event, entry);
            continue;
        }

        if (entry.kind !== "Consumed") continue;

        // First consume per service: a redelivery the inbox swallowed is not a second hop.
        const key = `${entry.event}:${entry.service}`;
        if (seen.has(key)) continue;

        seen.add(key);
        consumes.push(entry);
    }

    const hops: Hop[] = [];
    const answered = new Set<string>();

    for (const consumed of consumes) {
        const publish = published.get(consumed.event);

        // A consume whose publish fell off the far end of the 200-row tape has no start,
        // and a bar without one would be drawn from the wrong place.
        if (!publish) continue;

        answered.add(consumed.event);
        hops.push({ published: publish, consumed });
    }

    for (const [event, publish] of published) {
        if (!answered.has(event)) hops.push({ published: publish, consumed: null });
    }

    return hops;
}

/** One consume, drawn as a bar in its service's lane. Offsets are already percentages. */
export interface WaterfallBar {
    event: string;
    service: ServiceName;
    step: FlowStepId | null;
    left: string;
    width: string;
    title: string;
}

export interface WaterfallLane {
    service: ServiceName;
    bars: WaterfallBar[];
}

/** The stretch between a publish and its own consume: time the message spent queued. */
export interface WaterfallGap {
    event: string;
    /** Whose pick-up was being waited for — two consumers of one message wait separately. */
    service: ServiceName;
    /** The step this wait was in front of, so focusing that step lights the wait too. */
    step: FlowStepId | null;
    left: string;
    width: string;
    /** Only the widest gap is labelled; the rest draw as a bare line. */
    label: { text: string; left: string } | null;
}

export interface WaterfallLayout {
    gaps: WaterfallGap[];
    lanes: WaterfallLane[];
    totalMs: number;
}

/**
 * Lays the hops out relative to the moment the booking was placed, so both panels of a
 * race are read on their own timeline rather than a shared wall clock. Returns null when
 * there is nothing to draw yet — the tape may well arrive after the booking does.
 */
export function buildWaterfall(
    entries: readonly BusTapeEntry[],
    placedAtUtc: IsoDate,
): WaterfallLayout | null {
    const start = Date.parse(placedAtUtc);

    const hops = buildHops(entries).map(hop => ({
        event: hop.published.event,
        service: hop.consumed?.service ?? null,
        step: hop.consumed ? stepOfEntry(hop.consumed) : null,
        publishedAtMs: Date.parse(hop.published.atUtc) - start,
        consumeStartMs: hop.consumed
            ? Date.parse(hop.consumed.atUtc) - start - (hop.consumed.durationMs ?? 0)
            : null,
        consumeEndMs: hop.consumed ? Date.parse(hop.consumed.atUtc) - start : null,
    }));

    if (hops.length === 0) return null;

    const totalMs = Math.max(...hops.map(hop => hop.consumeEndMs ?? hop.publishedAtMs), 1);
    const pct = (ms: number) => `${Math.max(0, Math.min(100, (ms / totalMs) * 100)).toFixed(2)}%`;

    // The gap between a publish and the hop's own consume is queue wait — the point of
    // this whole panel. The widest one gets a label; the others still draw, so the shape
    // of "where the time went" is visible even without reading a number.
    const gaps = hops
        .filter(hop => hop.service !== null
            && hop.consumeStartMs !== null && hop.consumeStartMs > hop.publishedAtMs)
        .map(hop => ({
            event: hop.event,
            service: hop.service!,
            step: hop.step,
            startMs: hop.publishedAtMs,
            endMs: hop.consumeStartMs ?? hop.publishedAtMs,
        }));

    const widest = gaps.reduce<typeof gaps[number] | null>(
        (widest, gap) =>
            widest === null || gap.endMs - gap.startMs > widest.endMs - widest.startMs ? gap : widest,
        null);

    return {
        totalMs,
        gaps: gaps.map(gap => ({
            event: gap.event,
            service: gap.service,
            step: gap.step,
            left: pct(gap.startMs),
            width: pct(gap.endMs - gap.startMs),
            label: gap === widest
                ? {
                    text: `${Math.round(gap.endMs - gap.startMs)} ms queue wait`,
                    left: pct((gap.startMs + gap.endMs) / 2),
                }
                : null,
        })),
        // A hop nobody has consumed yet has no lane to sit in — it still counts towards
        // the total, so the axis does not shrink back when the consume finally lands.
        lanes: SERVICE_LANE_ORDER
            .map(service => ({
                service,
                bars: hops
                    .filter(hop => hop.service === service
                        && hop.consumeStartMs !== null && hop.consumeEndMs !== null)
                    .map(hop => {
                        const from = hop.consumeStartMs ?? 0;
                        const to = hop.consumeEndMs ?? 0;

                        return {
                            event: hop.event,
                            service,
                            step: hop.step,
                            left: pct(from),
                            width: pct(to - from),
                            title: `${service}: ${hop.event}, ${Math.round(to - from)} ms`,
                        };
                    }),
            }))
            .filter(lane => lane.bars.length > 0),
    };
}
