// Step 1 — the catalogue, served through the gateway's Redis output cache.

import { dayAndTime, money } from "../api/format.ts";
import type { ScreeningListItem } from "../api/types.ts";
import { useDemoData } from "../data/demo-data.tsx";
import { useSelection } from "../data/selection.tsx";

export function ScreeningList() {
    const { screenings, screeningsLoaded } = useDemoData();
    const { screeningId, selectScreening } = useSelection();

    return (
        <section aria-labelledby="step-screenings">
            <h2 id="step-screenings">Tonight&rsquo;s screenings</h2>

            <div className="screenings">
                {screenings.length === 0
                    ? <p className="empty">
                        {screeningsLoaded ? "No screenings are on sale." : "Loading…"}
                    </p>
                    : screenings.map(screening =>
                        <ScreeningCard
                            key={screening.id}
                            screening={screening}
                            chosen={screening.id === screeningId}
                            onChoose={() => selectScreening(screening.id)}
                        />)}
            </div>

            <p className="aside">
                This list is output-cached in Redis by the gateway for ten seconds, so the seat
                counts here can lag the seat map below by a moment. That is the cache working,
                not a bug &mdash; the seat map is read straight through.
            </p>
        </section>
    );
}

interface ScreeningCardProps {
    screening: ScreeningListItem;
    chosen: boolean;
    onChoose: () => void;
}

function ScreeningCard({ screening, chosen, onChoose }: ScreeningCardProps) {
    const taken = screening.totalSeats - screening.availableSeats;
    const sold = screening.totalSeats === 0 ? 0 : (taken / screening.totalSeats) * 100;
    const soldOut = screening.availableSeats === 0;

    return (
        <button type="button" className="screening" aria-pressed={chosen} onClick={onChoose}>
            <span className="film">{screening.filmTitle}</span>
            <span className="where">{screening.auditorium}</span>

            <span className="meta">
                <span>{dayAndTime.format(new Date(screening.startsAtUtc))}</span>
                <span className="price">{money(screening.ticketPrice, screening.currency)}</span>
            </span>

            {/* The bar fills with what is still free rather than with what has gone, so a
                house that is emptying reads the same way the seat map below it does. */}
            <span className="bar"><i style={{ width: `${100 - sold}%` }} /></span>

            <span className={soldOut ? "free none" : "free"}>
                {soldOut ? "Sold out" : `${screening.availableSeats} of ${screening.totalSeats} seats free`}
            </span>
        </button>
    );
}
