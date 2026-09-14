// Step 3: what happens next.
//
// The section only exists while something is being tracked — placing a booking is what
// puts it on screen, and the data provider stops tracking once every side of it has
// settled. That is also why the seating section can scroll to #flow-section by id: the
// element appears in the same commit as the booking that justified it.

import { entriesFor } from "../bus/tape-store.ts";
import { useDemoData } from "../data/demo-data.tsx";
import { RIVAL_EMAIL, useSelection } from "../data/selection.tsx";
import { useTape } from "../hooks/useTape.ts";
import { FlowPanel } from "./FlowPanel.tsx";

export function FlowSection() {
    const { trackedBookings, payments, mail, chaosMode, dashboardUrl } = useDemoData();
    const { tracked } = useSelection();

    // Subscribing to the tape is what redraws the waterfalls as messages arrive. The
    // store is a module-level ring buffer, so without this the entriesFor() calls below
    // would keep returning rows nothing had asked React to draw.
    useTape();

    if (!tracked) return null;

    // Two panels only during "Race a rival" — and the "You" label is pointless without a
    // second panel to tell it apart from.
    const racing = tracked.rival !== null;

    // The payment and the e-mail ledgers are global rather than per-address, so both
    // sides of a race can be found in them; the bookings themselves cannot, which is why
    // the provider resolves those separately.
    const forBooking = (id: string | null) => ({
        payment: id ? payments.find(payment => payment.bookingId === id) : undefined,
        mail: id ? mail.find(entry => entry.bookingId === id) : undefined,
        entries: id ? entriesFor(id) : [],
    });

    const mine = forBooking(tracked.mine);
    const rival = forBooking(tracked.rival);

    return (
        <section id="flow-section" aria-labelledby="step-flow">
            <h2 id="step-flow"><span className="step">3</span>What happens next</h2>

            <div className="flow-columns">
                <FlowPanel
                    panelId="flow-panel-mine"
                    label="You"
                    labelHidden={!racing}
                    booking={trackedBookings.mine}
                    payment={mine.payment}
                    mail={mine.mail}
                    entries={mine.entries}
                    chaosMode={chaosMode}
                />

                {racing && (
                    <FlowPanel
                        panelId="flow-panel-rival"
                        label={<>Rival · <code>{RIVAL_EMAIL}</code></>}
                        labelHidden={false}
                        booking={trackedBookings.rival}
                        payment={rival.payment}
                        mail={rival.mail}
                        entries={rival.entries}
                        chaosMode={chaosMode}
                    />
                )}
            </div>

            <p className="aside wf-caption">
                The timing above is reconstructed from message timestamps taken in five
                separate processes — fine to within a millisecond or two on one
                machine, not a substitute for a real trace.
                {/* The link names the environment rather than anything about one booking,
                    which is why it is fetched once and simply stays hidden when AppHost
                    has no dashboard URL to give. */}
                {dashboardUrl
                    ? <a href={dashboardUrl} target="_blank" rel="noopener">Open the Aspire dashboard{" →"}</a>
                    : null}
            </p>
        </section>
    );
}
