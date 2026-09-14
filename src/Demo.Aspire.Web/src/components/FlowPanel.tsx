// One booking's flow: the five steps, the trace waterfall underneath them, and the line
// that says what the whole thing amounted to. Two of these sit side by side during
// "Race a rival", drawn from the same snapshot so they cannot disagree with each other.

import { useEffect, useState, type ReactNode } from "react";

import { formatPayCountdown, sinceStart } from "../api/format.ts";
import type {
    BookingResponse, BusTapeEntry, ChaosMode, IsoDate, NotificationEntry, PaymentListItem,
} from "../api/types.ts";
import { flowNote, flowSteps, markerFor, type FlowStep } from "../flow/steps.ts";
import { buildWaterfall } from "../flow/waterfall.ts";
import { Waterfall } from "./Waterfall.tsx";

/** The last 30s of a seat hold, where the countdown turns amber. */
const WARN_MS = 30_000;

interface FlowPanelProps {
    /** Load-bearing on the rival panel: app.css switches to two columns on :has() of it. */
    panelId: string;
    label: ReactNode;
    labelHidden: boolean;
    /**
     * Null in the window between placing a booking and the first refresh resolving it —
     * the panel is already on screen by then, because the reader clicked to put it there.
     */
    booking: BookingResponse | null;
    payment: PaymentListItem | undefined;
    mail: NotificationEntry | undefined;
    entries: readonly BusTapeEntry[];
    chaosMode: ChaosMode;
}

export function FlowPanel(
    { panelId, label, labelHidden, booking, payment, mail, entries, chaosMode }: FlowPanelProps,
) {
    const steps = booking ? flowSteps(booking, payment, mail, chaosMode) : [];
    const waterfall = booking ? buildWaterfall(entries, booking.placedAtUtc) : null;

    return (
        <div className="panel flow-panel" id={panelId}>
            <p className="flow-panel-label" hidden={labelHidden}>{label}</p>

            <ol className="flow">
                {steps.map((step, index) => step.payBeforeUtc !== null
                    ? (
                        <PayCountdownStep
                            key={index}
                            step={step}
                            placedAtUtc={booking?.placedAtUtc ?? ""}
                            payBeforeUtc={step.payBeforeUtc}
                        />
                    )
                    : <Step key={index} step={step} placedAtUtc={booking?.placedAtUtc ?? ""} />)}
            </ol>

            {waterfall && <Waterfall layout={waterfall} />}

            <p className="aside">{booking ? flowNote(booking) : ""}</p>
        </div>
    );
}

interface StepProps {
    step: FlowStep;
    /** Every timestamp is shown as an offset from this, not as a wall clock. */
    placedAtUtc: IsoDate;
    /** Set only by PayCountdownStep, and only while a hold is still being waited on. */
    remainingMs?: number;
}

function Step({ step, placedAtUtc, remainingMs }: StepProps) {
    const counting = remainingMs !== undefined;
    const detail = counting ? formatPayCountdown(remainingMs) : step.note;

    return (
        <li
            data-state={step.state}
            className={counting && remainingMs > 0 && remainingMs <= WARN_MS ? "countdown-warn" : undefined}
        >
            <span className="dot">{markerFor(step.state)}</span>
            <span className="what">{step.what}</span>
            <span className="when">{step.at ? sinceStart(placedAtUtc, step.at) : ""}</span>
            <span className="who">{detail ? `${step.who} · ${detail}` : step.who}</span>
        </li>
    );
}

/**
 * The seat-hold step while payment is still outstanding: "Xs left to pay", counted down
 * every second regardless of when the flow was last redrawn, so the number moves smoothly
 * instead of jumping only when a refresh happens to land.
 *
 * The vanilla page did this by writing straight into the DOM, because re-rendering the
 * whole flow list every second would fight the browser's own focus and hover state on it.
 * The React equivalent is structural rather than a convention: the interval lives at the
 * leaf, so a tick re-renders this one step and can reach nothing around it. The <li> is
 * inside the leaf rather than only the countdown <span> because the amber warning is
 * driven by the same remaining value — splitting them would put the parent back on the
 * hook for re-rendering every second, which is the thing being avoided.
 */
function PayCountdownStep({ step, placedAtUtc, payBeforeUtc }: {
    step: FlowStep;
    placedAtUtc: IsoDate;
    payBeforeUtc: IsoDate;
}) {
    const [remainingMs, setRemainingMs] = useState(() => Date.parse(payBeforeUtc) - Date.now());

    useEffect(() => {
        const tick = () => setRemainingMs(Date.parse(payBeforeUtc) - Date.now());

        // Once immediately: a hold that changed length (the short-holds switch) would
        // otherwise show the previous deadline for up to a second after it moved.
        tick();

        const timer = setInterval(tick, 1000);
        return () => clearInterval(timer);
    }, [payBeforeUtc]);

    return <Step step={step} placedAtUtc={placedAtUtc} remainingMs={remainingMs} />;
}
