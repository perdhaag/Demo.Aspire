import { useState, type FormEvent } from "react";

import * as client from "../api/client.ts";
import { money } from "../api/format.ts";
import { useDemoData } from "../data/demo-data.tsx";
import { RIVAL_EMAIL, useSelection } from "../data/selection.tsx";
import { useToast } from "./Toast.tsx";

// Step 3 is rendered by another component and only exists once something is being
// tracked, so this asks for it by id and shrugs if it is not there yet.
const scrollToFlow = () =>
    document.getElementById("flow-section")?.scrollIntoView({ behavior: "smooth", block: "nearest" });

export function Checkout({ screeningId }: { screeningId: string }) {
    const { seatMap, refresh } = useDemoData();
    const { email, selected, clearSeats, track, trackRace } = useSelection();
    const toast = useToast();

    // Placing a booking is one POST that returns 202; the rest of the story finishes on
    // the message bus. This flag therefore covers the POST alone — the moment it lands the
    // flow panel takes over telling the reader that something is still happening.
    const [placing, setPlacing] = useState(false);

    // Sorted so the tally reads in seat order, and so both halves of a race ask for the
    // seats in the same order as each other.
    const seats = [...selected].sort();
    const price = seatMap?.ticketPrice ?? 0;
    const currency = seatMap?.currency ?? "";
    const disabled = placing || seats.length === 0;

    const place = (customerEmail: string) =>
        client.placeBooking(screeningId, customerEmail, seats);

    async function book(event: FormEvent<HTMLFormElement>) {
        event.preventDefault();
        setPlacing(true);

        try {
            const placed = await place(email.trim());

            track(placed.bookingId);
            clearSeats();
            await refresh();
            scrollToFlow();
        } catch (error) {
            toast((error as Error).message);
        } finally {
            setPlacing(false);
        }
    }

    // Both bookings ask for the same seats on the same screening in the same instant.
    // Screenings' seat map is the one consistency boundary in this system (see
    // Screening.HoldSeats), so exactly one of the two can actually hold them — the other's
    // flow shows a rejected hold rather than a rollback, because there was never anything
    // to roll back.
    async function race() {
        setPlacing(true);

        try {
            const [mine, rival] = await Promise.allSettled([
                place(email.trim()),
                place(RIVAL_EMAIL),
            ]);

            // Neither side got as far as a booking id, so there is no flow to draw and no
            // reason to clear the seats: whatever refused them will refuse them again, and
            // the reader can read why and try something else.
            if (mine.status === "rejected" && rival.status === "rejected") {
                toast((mine.reason as Error).message);
                return;
            }

            trackRace(
                mine.status === "fulfilled" ? mine.value.bookingId : null,
                rival.status === "fulfilled" ? rival.value.bookingId : null);

            if (mine.status === "rejected") toast(`Your booking: ${(mine.reason as Error).message}`);
            if (rival.status === "rejected") toast(`Rival's booking: ${(rival.reason as Error).message}`);

            clearSeats();
            await refresh();
            scrollToFlow();
        } finally {
            setPlacing(false);
        }
    }

    return (
        <form className="checkout" onSubmit={book}>
            <div className="tally">
                <strong>{seats.length === 0 ? "No seats selected" : seats.join(", ")}</strong>
                <span>
                    {seats.length === 0
                        ? "Pick one or more seats above."
                        : `${seats.length} × ${money(price, currency)} = ${money(seats.length * price, currency)}`}
                </span>
            </div>

            <div className="checkout-actions">
                <button type="button" className="secondary" disabled={disabled} onClick={race}>
                    Race a rival
                </button>
                <button type="submit" className="primary" disabled={disabled}>
                    Book seats
                </button>
            </div>
        </form>
    );
}
