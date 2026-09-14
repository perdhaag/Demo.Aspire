// One line of the bus tape, plus the two pure helpers that phrase it. The helpers live
// here rather than inside the component because the collapsed dock's peek row shows the
// newest entry in exactly the same words — one message, two places, one wording.

import type { CSSProperties } from "react";

import { preciseClock } from "../api/format.ts";
import type { BusTapeEntry } from "../api/types.ts";

/**
 * "SeatsHeld published", "SeatsHeld consumed", "PaymentRequested failed on". The verb
 * carries the kind because the event name alone cannot: the same event appears twice on
 * the tape, once where it was published and once where it was consumed, and reading the
 * pair back to back is the whole point of the rail.
 */
export function tapeRowLabel(entry: BusTapeEntry): string {
    const verb = entry.kind === "Published" ? "published"
        : entry.kind === "Faulted" ? "failed on"
        : "consumed";

    return `${entry.event} ${verb}`;
}

/** The second line: how long the handler took, and whatever the service had to add. */
export function tapeRowDetail(entry: BusTapeEntry): string {
    const parts: string[] = [];

    if (entry.durationMs != null) parts.push(`${Math.round(entry.durationMs)} ms`);
    if (entry.detail) parts.push(entry.detail);

    return parts.join(" — ");
}

/**
 * The service colour is handed to CSS as a custom property rather than a class, so that
 * `--svc-*` in app.css stays the single place a service's colour is decided. `service`
 * is a union type, which makes the interpolation below a checked contract against those
 * custom properties instead of a template string nobody ever verifies.
 */
export const serviceColour = (entry: BusTapeEntry): CSSProperties =>
    ({ "--tape-row-color": `var(--svc-${entry.service})` }) as CSSProperties;

export function TapeRow({ entry, tracked }: { entry: BusTapeEntry; tracked: boolean }) {
    const detail = tapeRowDetail(entry);

    return (
        <li
            className="tape-row"
            data-kind={entry.kind}
            // Even on an unfiltered tape this is what makes your own booking's messages
            // stand out from the traffic every other visitor is generating.
            data-tracked={String(tracked)}
            style={serviceColour(entry)}
        >
            <span className="svc">{entry.service}</span>
            <span className="event">{tapeRowLabel(entry)}</span>
            <span className="at">{preciseClock.format(new Date(entry.atUtc))}</span>
            {detail ? <span className="detail">{detail}</span> : null}
        </li>
    );
}
