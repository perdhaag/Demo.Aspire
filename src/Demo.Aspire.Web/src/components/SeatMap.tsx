// Step 2 — the seat map, a Redis-cached projection of the Screening aggregate.

import { useRef } from "react";

import type { SeatMapView, SeatView } from "../api/types.ts";
import { useDemoData } from "../data/demo-data.tsx";
import { useSelection } from "../data/selection.tsx";
import { useHoldRings } from "../hooks/useHoldRings.ts";

export function SeatMap({ seatMap }: { seatMap: SeatMapView }) {
    const { holdPolicySeconds } = useDemoData();
    const { selected, toggleSeat } = useSelection();
    const seatmap = useRef<HTMLDivElement>(null);

    useHoldRings(seatmap, holdPolicySeconds);

    // A seat number is its row letter followed by its place in the row — "C7" — so the
    // letter is what groups and the number after it is what orders. Map.groupBy leaves the
    // rows in the order the seat map listed them; the sort inside a row is only there to
    // put seat 10 after seat 9 rather than after seat 1.
    const rows = [...Map.groupBy(seatMap.seats, seat => seat.number.slice(0, 1))];

    return (
        <>
            <div className="screen" aria-hidden="true"><span>SCREEN</span></div>

            <div className="seatmap-scroll">
                <div className="seatmap" ref={seatmap}>
                    {rows.map(([row, seats]) =>
                        <div className="seat-row" key={row}>
                            <span className="row-label">{row}</span>

                            {[...seats]
                                .sort((left, right) => Number(left.number.slice(1)) - Number(right.number.slice(1)))
                                .map(seat =>
                                    <Seat
                                        key={seat.number}
                                        seat={seat}
                                        picked={selected.has(seat.number)}
                                        onPick={() => toggleSeat(seat.number)}
                                    />)}

                            <span className="row-label">{row}</span>
                        </div>)}
                </div>
            </div>

            <ul className="legend">
                <li><span className="swatch is-free"></span>Free</li>
                <li><span className="swatch is-picked"></span>Yours</li>
                <li><span className="swatch is-held"></span>On hold</li>
                <li><span className="swatch is-sold"></span>Sold</li>
            </ul>
        </>
    );
}

interface SeatProps {
    seat: SeatView;
    picked: boolean;
    onPick: () => void;
}

// Whether this seat is chosen is a prop. The page this replaces had no such thing: after
// every click it walked all ninety-odd seat buttons and parsed the seat number back out
// of each one's aria-label to decide what to write into its aria-pressed — reading the
// DOM to recover state the page was already holding in a Set. Deleting that loop is the
// clearest single answer to "why rewrite a page that works".
//
// data-status and data-hold-expires are read by app.css and by useHoldRings respectively,
// which is why they are attributes rather than anything React would call state.
function Seat({ seat, picked, onPick }: SeatProps) {
    return (
        <button
            type="button"
            className="seat"
            disabled={seat.status !== "Available"}
            title={`${seat.number} — ${seat.status}`}
            aria-label={`Seat ${seat.number}, ${seat.status}`}
            aria-pressed={picked}
            data-status={seat.status}
            data-hold-expires={seat.holdExpiresAtUtc ?? undefined}
            onClick={onPick}
        >
            {seat.number.slice(1)}
        </button>
    );
}
