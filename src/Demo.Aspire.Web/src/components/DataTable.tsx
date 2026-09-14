import type { ReactNode } from "react";

interface DataTableProps<T> {
    headings: string[];
    rows: T[];
    /** Shown instead of the table — not as a row inside it — when there is nothing yet. */
    empty: string;
    keyOf: (row: T) => string;
    cells: (row: T) => ReactNode;
}

export function DataTable<T>({ headings, rows, empty, keyOf, cells }: DataTableProps<T>) {
    if (rows.length === 0) {
        return <p className="empty">{empty}</p>;
    }

    return (
        <table>
            <thead>
                <tr>{headings.map(heading => <th key={heading}>{heading}</th>)}</tr>
            </thead>
            <tbody>
                {rows.map(row => <tr key={keyOf(row)}>{cells(row)}</tr>)}
            </tbody>
        </table>
    );
}

// The status is the class name: app.css colours .pill.Confirmed, .pill.Captured,
// .pill.Cancelled, .pill.Declined, .pill.Placed and .pill.AwaitingPayment directly, so a
// status the stylesheet has not heard of simply renders uncoloured rather than wrongly.
export const Pill = ({ value }: { value: string }) => <span className={`pill ${value}`}>{value}</span>;
