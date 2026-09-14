import { useDemoData } from "../data/demo-data.tsx";
import { RIVAL_EMAIL, useSelection } from "../data/selection.tsx";
import { Checkout } from "./Checkout.tsx";
import { SeatMap } from "./SeatMap.tsx";

// The page this replaces kept this section in the document from the start and cleared its
// `hidden` attribute once a screening had been chosen. Not rendering it at all says the
// same thing to a reader and to a screen reader, and it takes the seat map's rAF loop
// (see useHoldRings) down with it when there is nothing left to count down.
export function SeatingSection() {
    const { seatMap } = useDemoData();
    const { screeningId } = useSelection();

    if (!screeningId) return null;

    return (
        <section aria-labelledby="step-seats">
            <h2 id="step-seats">Choose your seats</h2>

            <div className="panel seatmap-panel">
                {seatMap && <SeatMap seatMap={seatMap} />}

                <Checkout screeningId={screeningId} />

                <p className="aside">
                    Book as an address containing <code>decline</code> &mdash;
                    say <code>decline@example.com</code> &mdash; and Payments refuses the
                    charge, so you can watch the seats go back on sale.{" "}
                    <strong>Race a rival</strong> books the same seats as{" "}
                    <code>{RIVAL_EMAIL}</code> in the same instant, so you can watch
                    Screenings let exactly one of you keep them.
                </p>
            </div>
        </section>
    );
}
