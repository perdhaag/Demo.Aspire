// The three read models the demo writes to, side by side: what Bookings thinks happened,
// what Payments decided, and what Notifications sent. Nothing here is the page's own
// state — it is the same snapshot every other panel is drawn from, which is the point:
// the tabs let you check the story the flow panel tells against the services' own books.

import { useState } from "react";

import { clock, money } from "../api/format.ts";
import { useDemoData } from "../data/demo-data.tsx";
import { DataTable, Pill } from "./DataTable.tsx";

type Tab = "bookings" | "payments" | "mail";

export function Ledgers() {
    const { bookings, payments, mail } = useDemoData();
    const [tab, setTab] = useState<Tab>("bookings");

    const tabProps = (which: Tab) => ({
        role: "tab",
        id: `tab-${which}`,
        "aria-controls": `pane-${which}`,
        "aria-selected": tab === which,
        onClick: () => setTab(which),
    });

    return (
        <section aria-labelledby="ledgers">
            <h2 id="ledgers"><span className="step">·</span>Behind the scenes</h2>

            <div className="tabs" role="tablist">
                <button {...tabProps("bookings")}>Your bookings</button>
                <button {...tabProps("payments")}>Payments</button>
                <button {...tabProps("mail")}>Mail sent</button>
            </div>

            <div
                className="panel pane"
                id="pane-bookings"
                role="tabpanel"
                aria-labelledby="tab-bookings"
                hidden={tab !== "bookings"}
            >
                <DataTable
                    headings={["Film", "Seats", "Total", "Status", "Detail"]}
                    rows={bookings}
                    empty="No bookings for this address yet."
                    keyOf={booking => booking.bookingId}
                    cells={booking => <>
                        <td>{booking.filmTitle}</td>
                        <td>{booking.seats.join(", ")}</td>
                        <td>{money(booking.total, booking.currency)}</td>
                        <td><Pill value={booking.status} /></td>
                        <td className="wrap">
                            {booking.paymentReference ?? booking.cancellationReason ?? "—"}
                        </td>
                    </>}
                />
            </div>

            <div
                className="panel pane"
                id="pane-payments"
                role="tabpanel"
                aria-labelledby="tab-payments"
                hidden={tab !== "payments"}
            >
                <DataTable
                    headings={["Payer", "Amount", "Status", "Reference or reason", "Decided"]}
                    rows={payments}
                    empty="Payments has not been asked for anything yet."
                    keyOf={payment => payment.paymentId}
                    cells={payment => <>
                        <td>{payment.payer}</td>
                        <td>{money(payment.amount, payment.currency)}</td>
                        <td><Pill value={payment.status} /></td>
                        <td className="wrap">
                            {payment.reference ?? payment.declineReason ?? "—"}
                        </td>
                        <td>{clock.format(new Date(payment.decidedAtUtc))}</td>
                    </>}
                />
            </div>

            <div
                className="panel pane"
                id="pane-mail"
                role="tabpanel"
                aria-labelledby="tab-mail"
                hidden={tab !== "mail"}
            >
                <DataTable
                    headings={["To", "Subject", "Outcome", "Sent"]}
                    rows={mail}
                    empty="Nothing has been sent yet."
                    keyOf={entry => entry.messageId}
                    cells={entry => <>
                        <td>{entry.recipient}</td>
                        <td className="wrap">{entry.subject}</td>
                        <td className="wrap">{entry.outcome}</td>
                        <td>{clock.format(new Date(entry.sentAtUtc))}</td>
                    </>}
                />
            </div>
        </section>
    );
}
