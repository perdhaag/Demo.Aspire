// One booking's flow: the six steps, the trace waterfall underneath them, and the line
// that says what the whole thing amounted to. Two of these sit side by side during
// "Race a rival", drawn from the same snapshot so they cannot disagree with each other.
//
// A step here is also a handle. Hovering, tabbing onto or clicking one publishes it to
// flow/focus.ts, and the bus tape and the waterfall dim everything that is not part of it —
// which is the answer to the question the page could not previously answer: *which* of
// those rows is this step?

import { Fragment, useEffect, useState, type ReactNode } from "react";

import { formatPayCountdown, sinceStart } from "../api/format.ts";
import type {
    BookingResponse, BusTapeEntry, ChaosMode, IsoDate, NotificationEntry, PaymentListItem,
} from "../api/types.ts";
import { entriesForStep, type Infra, type Transport } from "../flow/choreography.ts";
import * as focusStore from "../flow/focus.ts";
import { flowNote, flowSteps, markerFor, type FlowStep, type StepOutcome } from "../flow/steps.ts";
import { buildWaterfall } from "../flow/waterfall.ts";
import { useFlowFocus } from "../hooks/useFlowFocus.ts";
import { serviceVar, tapeRowLabel } from "./TapeRow.tsx";
import { Waterfall } from "./Waterfall.tsx";

/** The last 30s of a seat hold, where the countdown turns amber. */
const WARN_MS = 30_000;

/** How the step was reached. Exactly one step in the whole flow is not a message. */
const TRANSPORT: Record<Transport, { label: string; title: string }> = {
    http: { label: "HTTP", title: "One synchronous request, answered 202 before anything else happened." },
    bus: { label: "RabbitMQ", title: "A message, picked up whenever the consumer got to it." },
    clock: { label: "a timer", title: "Nothing published this. SeatHoldSweeper noticed a deadline." },
};

const INFRA: Record<Infra, string> = {
    http: "HTTP",
    postgres: "Postgres",
    rabbitmq: "RabbitMQ",
    redis: "Redis",
    smtp: "SMTP",
};

interface FlowPanelProps {
    /** Load-bearing on the rival panel: app.css switches to two columns on :has() of it. */
    panelId: string;
    /** The booking id, which is also the correlation id on every tape row of this flow. */
    correlationId: string | null;
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
    { panelId, correlationId, label, labelHidden, booking, payment, mail, entries, chaosMode }:
    FlowPanelProps,
) {
    const focus = useFlowFocus();

    // A focus belonging to the other panel of a race must not light this one up: two
    // bookings can sit on the same step without being the same flow.
    const focused = focus && focus.correlationId === correlationId ? focus : null;

    const steps = booking ? flowSteps(booking, payment, mail, chaosMode, entries) : [];
    const waterfall = booking ? buildWaterfall(entries, booking.placedAtUtc) : null;

    return (
        <div
            className="panel flow-panel"
            id={panelId}
        >
            <p className="flow-panel-label" hidden={labelHidden}>{label}</p>

            <ol className="flow">
                {steps.map(step => (
                    <Fragment key={step.id}>
                        <StepItem
                            step={step}
                            correlationId={correlationId}
                            carriedBy={entriesForStep(entries, step.id)}
                            placedAtUtc={booking?.placedAtUtc ?? ""}
                            focused={focused?.step === step.id}
                            dimmed={focused !== null && focused.step !== step.id}
                            pinned={focused?.step === step.id && focused.pinned}
                        />

                        {/* The one line on the page that says where the request ends. Above
                            it, a customer waiting on a socket; below it, five services
                            reacting to each other with nobody waiting on anything. */}
                        {step.id === "placed" && (
                            <li className="flow-boundary" aria-hidden="true">
                                HTTP stops here — everything below is a message
                            </li>
                        )}
                    </Fragment>
                ))}
            </ol>

            {waterfall && <Waterfall layout={waterfall} focus={focused?.step ?? null} />}

            <p className="aside">{booking ? flowNote(booking) : ""}</p>
        </div>
    );
}

interface StepItemProps {
    step: FlowStep;
    correlationId: string | null;
    /** The tape rows that carried this step — the very rows it lights up when focused. */
    carriedBy: readonly BusTapeEntry[];
    /** Every timestamp is shown as an offset from this, not as a wall clock. */
    placedAtUtc: IsoDate;
    focused: boolean;
    /** Another step of this same flow is focused, so this one steps back out of the way. */
    dimmed: boolean;
    pinned: boolean;
}

function StepItem(props: StepItemProps) {
    // The seat-hold step, while payment is still outstanding, needs a number that moves
    // every second. Isolating it in its own component keeps that interval at the leaf.
    return props.step.payBeforeUtc !== null
        ? <PayCountdownStep {...props} payBeforeUtc={props.step.payBeforeUtc} />
        : <Step {...props} />;
}

function Step(
    { step, correlationId, carriedBy, placedAtUtc, focused, dimmed, pinned, remainingMs }:
    StepItemProps & { remainingMs?: number },
) {
    const counting = remainingMs !== undefined;
    const detail = counting ? formatPayCountdown(remainingMs) : step.note;
    const settled = step.state === "done" || step.state === "failed";

    // A step with no booking behind it yet has nothing to correlate with, so it is inert
    // rather than a handle that lights nothing up.
    const interactive = correlationId !== null;

    return (
        <li
            data-state={step.state}
            data-step={step.id}
            data-focused={String(focused)}
            data-dim={String(dimmed)}
            className={counting && remainingMs > 0 && remainingMs <= WARN_MS ? "countdown-warn" : undefined}
            // The deciding service's hue, so the dot, the tape rows and the waterfall bars
            // for this step are all the same colour.
            style={serviceVar(step.services[0]!)}
        >
            {interactive && (
                // A transparent button over the whole row rather than a clickable <li>: the
                // row stays a grid of spans, and the keyboard gets a real control with a
                // real pressed state instead of a div pretending to be one.
                <button
                    type="button"
                    className="step-hit"
                    aria-pressed={pinned}
                    aria-label={`Step ${step.ordinal}, ${step.what} — highlight this step on the bus tape and the waterfall`}
                    onPointerEnter={() => focusStore.hover(correlationId, step.id)}
                    onPointerLeave={() => focusStore.unhover()}
                    onFocus={() => focusStore.hover(correlationId, step.id)}
                    onBlur={() => focusStore.unhover()}
                    onClick={() => focusStore.toggle(correlationId, step.id)}
                />
            )}

            {/* The ordinal is the cross-reference: the same number is stamped on every tape
                row belonging to this step. It stays visible after the step settles, and the
                ✓ or ✕ goes beside the headline instead of replacing it. */}
            <span className="dot">{step.ordinal}</span>

            <span className="what">
                {step.what}
                {settled && <span className="marker" aria-hidden="true">{markerFor(step.state)}</span>}
            </span>

            <span className="when">{step.at ? sinceStart(placedAtUtc, step.at) : ""}</span>

            <span className="who">{detail ? `${step.who} · ${detail}` : step.who}</span>

            {step.outcomes.length > 0 && (
                <ul className="fanout">
                    {step.outcomes.map(outcome =>
                        <FanoutRow key={outcome.service} outcome={outcome} placedAtUtc={placedAtUtc} />)}
                </ul>
            )}

            <span className="chips">
                <span className="chip-transport" data-transport={step.transport} title={TRANSPORT[step.transport].title}>
                    {TRANSPORT[step.transport].label}
                </span>

                {step.message && <code className="chip-msg">{step.message}</code>}

                {step.touches.map(touch =>
                    <span className="chip-infra" key={`${touch.infra}:${touch.what}`}>{INFRA[touch.infra]}</span>)}
            </span>

            {/* Kept in the document and revealed by app.css when the step is focused, so
                that hovering a step both lights it up elsewhere and says what it did here.
                Rendering it conditionally would tear it out from under a screen reader that
                had just reached it. */}
            <dl className="step-detail">
                <div>
                    <dt>{TRANSPORT[step.transport].label}</dt>
                    <dd>{TRANSPORT[step.transport].title}</dd>
                </div>

                {step.touches.map(touch => (
                    <div key={`${touch.infra}:${touch.what}`}>
                        <dt>{INFRA[touch.infra]}</dt>
                        <dd>{touch.what}</dd>
                    </div>
                ))}

                {/* Named rather than counted, and in the tape's own words, so that what is
                    lit up over in the rail is what was promised here. */}
                {carriedBy.length > 0 && (
                    <div>
                        <dt>Bus tape</dt>
                        <dd>{carriedBy.map(tapeRowLabel).join(", ")}</dd>
                    </div>
                )}
            </dl>
        </li>
    );
}

/** Step 6: one message, two services, neither aware of the other. */
function FanoutRow({ outcome, placedAtUtc }: { outcome: StepOutcome; placedAtUtc: IsoDate }) {
    return (
        <li data-state={outcome.state} style={serviceVar(outcome.service)}>
            <span className="fanout-svc">{outcome.service}</span>
            <span className="fanout-what">{outcome.what}</span>
            <span className="fanout-when">{outcome.at ? sinceStart(placedAtUtc, outcome.at) : ""}</span>
            {outcome.note && <span className="fanout-note">{outcome.note}</span>}
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
function PayCountdownStep({ payBeforeUtc, ...rest }: StepItemProps & { payBeforeUtc: IsoDate }) {
    const [remainingMs, setRemainingMs] = useState(() => Date.parse(payBeforeUtc) - Date.now());

    useEffect(() => {
        const tick = () => setRemainingMs(Date.parse(payBeforeUtc) - Date.now());

        // Once immediately: a hold that changed length (the short-holds switch) would
        // otherwise show the previous deadline for up to a second after it moved.
        tick();

        const timer = setInterval(tick, 1000);
        return () => clearInterval(timer);
    }, [payBeforeUtc]);

    return <Step {...rest} remainingMs={remainingMs} />;
}
