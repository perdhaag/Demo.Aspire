// Short holds: a demo switch on Screenings' hold policy.
//
// The policy lives on the server, so the checkbox is drawn from it rather than from its
// own state: the provider reads it once at start-up, and setHoldSeconds() posts the new
// value, keeps the answer the server gave and raises the toast. That leaves nothing here
// to remember — the box is checked whenever a hold is short enough to sit through.

import { useDemoData } from "../data/demo-data.tsx";

export function ShortHoldsSwitch() {
    const { holdPolicySeconds, setHoldSeconds } = useDemoData();

    return (
        <label
            className="switch"
            title="Seat holds normally last three minutes; this makes a new one expire in 20 seconds, so SeatHoldSweeper releasing the seats is something you can actually watch."
        >
            <input
                type="checkbox"
                checked={holdPolicySeconds <= 30}
                onChange={event => { void setHoldSeconds(event.target.checked ? 20 : 180); }}
            />
            <span>Short holds (20s)</span>
        </label>
    );
}
