// Chaos: a demo switch on Payments' simulated card network.
//
// Not a step in the customer's journey — a control over Payments' one dependency, so the
// reliability this system claims (the outbox, the retry policy) is something you can make
// happen on purpose rather than take on faith.

import type { ChaosMode } from "../api/types.ts";
import { useDemoData } from "../data/demo-data.tsx";

const CHAOS_NOTES: Record<ChaosMode, string> = {
    None: "Payments is behaving normally.",
    Paused: "Payments is paused — an authorization already in flight stays unacknowledged on the queue. Nothing is retrying it; it is simply waiting.",
    Slow: "Every authorization now takes about five seconds.",
    Failing: "The next authorization or two will fail. MassTransit’s retry policy keeps trying — the tape will show the faults, then a success.",
};

export function ChaosStrip() {
    const { chaosMode, setChaos } = useDemoData();

    // Pause and Slow are switches — clicking an active one turns it back off. "Fail twice,
    // then succeed" is a one-shot action: the server counts the failures down and returns
    // itself to None on its own, which is why it is never rendered as an active toggle for
    // longer than the next poll takes to notice, and why the provider re-reads the mode on
    // every refresh instead of only on a click.
    const toggle = (mode: ChaosMode) => () => { void setChaos(chaosMode === mode ? "None" : mode); };

    return (
        <section className="chaos" aria-labelledby="chaos-heading">
            <h2 id="chaos-heading">
                <span className="chaos-mark" aria-hidden="true">⚡</span>Payments: demo control
            </h2>
            <div className="panel">
                <div className="chaos-toggles" role="group" aria-label="Chaos controls for Payments">
                    <button
                        type="button"
                        className="chip"
                        aria-pressed={chaosMode === "Paused"}
                        onClick={toggle("Paused")}
                    >
                        Pause Payments
                    </button>
                    <button
                        type="button"
                        className="chip"
                        aria-pressed={chaosMode === "Slow"}
                        onClick={toggle("Slow")}
                    >
                        Slow (5s)
                    </button>
                    <button
                        type="button"
                        className="chip"
                        aria-pressed={chaosMode === "Failing"}
                        onClick={() => { void setChaos("Failing"); }}
                    >
                        Fail twice, then succeed
                    </button>
                </div>
                <p className="aside">{CHAOS_NOTES[chaosMode]}</p>
            </div>
        </section>
    );
}
