// The bus tape: every integration event published, consumed or faulted on by any of the
// five services, streamed live over Server-Sent Events. This is what "everything past
// Placed happens on RabbitMQ" actually looks like, rather than an assertion the page
// makes while quietly polling instead.
//
// The stream itself lives in bus/tape-store.ts, outside React — the feed can burst
// several entries a second, and a component owning that would re-render the seat map
// every time a message crossed the bus. Everything here is view: how an entry reads, how
// the rail becomes a dock, and which entries the filter lets through.

import { useEffect, useMemo, useRef, useState } from "react";

import { preciseClock } from "../api/format.ts";
import type { BusTapeEntry } from "../api/types.ts";
import { useSelection } from "../data/selection.tsx";
import { useMediaQuery } from "../hooks/useMediaQuery.ts";
import { useTape } from "../hooks/useTape.ts";
import { serviceColour, TapeRow, tapeRowDetail, tapeRowLabel } from "./TapeRow.tsx";

// Below the two-column breakpoint the tape stops being a rail beside the page and
// becomes a dock pinned to the bottom of the screen (see the media query in app.css). It
// matters because the tape is the whole argument the page is making: stacked at the end
// of a phone-length document it would sit some four screens below the seat you just
// booked, which is the same as not being there. Collapsed it costs one row.
const DOCK_BREAKPOINT = "(max-width: 900px)";

// The id the handle's aria-controls points at. There is exactly one tape on the page, so
// a literal reads better here than a generated id that could only ever take one value.
const TAPE_BODY_ID = "tape-body";

export function BusTape() {
    const { entries, connection } = useTape();
    const { tracked } = useSelection();

    const isDock = useMediaQuery(DOCK_BREAKPOINT);

    const [open, setOpen] = useState(false);
    const [onlyTracked, setOnlyTracked] = useState(false);

    // The correlation id for a whole booking's flow is the booking id itself — stamped
    // onto CorrelationContext by PlaceBookingHandler at the very start — so tracking a
    // booking is the same thing as watching a correlation. `rival` is null outside of
    // "Race a rival", hence dropping the nulls.
    const trackedIds = useMemo(
        () => [tracked?.mine, tracked?.rival].filter(id => id != null),
        [tracked]);

    // Whether the dock has already opened itself for the booking currently being watched.
    // A ref rather than state because nothing renders differently for it: it only decides
    // whether the next matching entry is allowed to open the dock.
    const autoOpened = useRef(false);

    // A new booking to watch earns one more automatic open, the way track() and
    // trackRace() reset state.tapeAutoOpened in app.js. Declared before the effect below
    // so that it runs first in a commit where both fire.
    useEffect(() => {
        autoOpened.current = false;
    }, [tracked]);

    const newest = entries.at(-1);

    // Opened the first time this booking reaches the bus — the moment the tape has
    // something to say about what you just did — and then left wherever the reader put
    // it. On a wide screen the rail is already open beside the page, the handle is
    // display:none, and none of this runs.
    useEffect(() => {
        if (!isDock || autoOpened.current) return;
        if (!newest || !trackedIds.includes(newest.correlationId)) return;

        autoOpened.current = true;
        setOpen(true);
    }, [isDock, newest, trackedIds]);

    // A `filter` before the map, rather than app.js's loop setting `hidden` on every row
    // already in the list: the rows are derived from the entries either way, so there is
    // no second pass over the DOM to keep in step with the first.
    const rows = onlyTracked
        ? entries.filter(entry => trackedIds.includes(entry.correlationId))
        : entries;

    return (
        <aside className={open ? "tape is-open" : "tape"} aria-label="Live bus tape">
            {/* On a phone this button is the dock's handle, carrying a peek row with the
                newest message. Hidden on a wide screen, where the whole tape is already
                visible beside the page and there is nothing to open. */}
            <button
                className="tape-grab"
                type="button"
                aria-expanded={open}
                aria-controls={TAPE_BODY_ID}
                onClick={() => setOpen(current => !current)}
            >
                <span className="grab-bar" aria-hidden="true"></span>
                <TapePeek entry={newest} />
            </button>

            <div className="tape-body" id={TAPE_BODY_ID}>
                <div className="tape-head">
                    <h2>Bus tape</h2>
                    {/* EventSource reconnects on its own; this only reflects that state. */}
                    <span className="tape-status" data-connected={String(connection === "live")}>
                        <span className="dot-live" aria-hidden="true"></span>
                        <span>{{ connecting: "connecting…", live: "live", reconnecting: "reconnecting…" }[connection]}</span>
                    </span>
                </div>

                <label className="tape-filter">
                    <input
                        type="checkbox"
                        checked={onlyTracked}
                        onChange={event => setOnlyTracked(event.target.checked)}
                    />
                    This booking only
                </label>

                <ol className="tape-rows">
                    {entries.length === 0
                        ? <li className="tape-empty">Waiting for the first message…</li>
                        : rows.map(entry => (
                            // The tape legitimately holds two entries with the same event
                            // name — the publish and the matching consume — so the kind
                            // belongs in the key. Without it React reuses one row for the
                            // other and the service colours flicker as the pair arrives.
                            <TapeRow
                                key={`${entry.correlationId}:${entry.event}:${entry.kind}:${entry.atUtc}`}
                                entry={entry}
                                tracked={trackedIds.includes(entry.correlationId)}
                            />
                        ))}
                </ol>
            </div>
        </aside>
    );
}

/**
 * The newest entry, kept on the collapsed dock's handle. This is what makes the dock
 * worth opening: it keeps moving while you are still choosing seats, so the bus is
 * visibly busy rather than a claim the page makes about itself. Derived from the entries
 * rather than written separately as each one arrives, which is one fewer thing to keep in
 * step with the rail below it.
 */
function TapePeek({ entry }: { entry: BusTapeEntry | undefined }) {
    if (!entry) {
        return (
            <span className="tape-peek">
                <span className="svc">Bus tape</span>
                <span className="event">Waiting for the first message…</span>
                <span className="at"></span>
                <span className="detail">Tap to open the tape</span>
            </span>
        );
    }

    return (
        <span className="tape-peek">
            <span className="svc" style={serviceColour(entry)}>{entry.service}</span>
            <span className="event">{tapeRowLabel(entry)}</span>
            <span className="at">{preciseClock.format(new Date(entry.atUtc))}</span>
            <span className="detail">{tapeRowDetail(entry) || "Tap to open the tape"}</span>
        </span>
    );
}
