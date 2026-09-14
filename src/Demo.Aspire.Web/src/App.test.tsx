// The one test that renders the whole page. Every other test here is a pure function;
// this is the only thing that proves the three providers nest in a workable order, that
// every section mounts against a realistic payload, and that the bus tape can be
// subscribed to without a real EventSource behind it.

import { afterEach, beforeEach, expect, test } from "bun:test";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";

import { App } from "./App.tsx";

const screeningId = "11111111-1111-1111-1111-111111111111";
const bookingId = "22222222-2222-2222-2222-222222222222";

const trackedBooking = {
    bookingId,
    screeningId,
    filmTitle: "The Third Man",
    auditorium: "Sal 1",
    screeningStartsAtUtc: "2026-09-14T19:00:00Z",
    customerEmail: "ada@example.com",
    seats: ["A1"],
    total: 145,
    currency: "NOK",
    // Deliberately still in flight: the data provider stops tracking a booking the moment
    // every side of it has settled, so a Confirmed one would take its own flow panel off
    // the page before this test could look at it.
    status: "AwaitingPayment",
    placedAtUtc: "2026-09-14T18:00:00.000Z",
    seatsHeldAtUtc: "2026-09-14T18:00:00.200Z",
    finishedAtUtc: null,
    payBeforeUtc: "2099-01-01T00:00:00.000Z",
    paymentReference: null,
    cancellationReason: null,
};

const routes: Record<string, unknown> = {
    "/api/screenings": [{
        id: screeningId,
        filmTitle: "The Third Man",
        auditorium: "Sal 1",
        startsAtUtc: "2026-09-14T19:00:00Z",
        ticketPrice: 145,
        currency: "NOK",
        totalSeats: 4,
        availableSeats: 3,
    }],
    [`/api/screenings/${screeningId}/seats`]: {
        screeningId,
        filmTitle: "The Third Man",
        auditorium: "Sal 1",
        startsAtUtc: "2026-09-14T19:00:00Z",
        ticketPrice: 145,
        currency: "NOK",
        availableSeats: 3,
        seats: [
            { number: "A1", status: "Available", holdExpiresAtUtc: null },
            { number: "A2", status: "Sold", holdExpiresAtUtc: null },
            { number: "A10", status: "Held", holdExpiresAtUtc: "2099-01-01T00:00:00Z" },
        ],
    },
    "/api/payments?take=25": [],
    "/api/notifications?take=25": [],
    "/api/payments/chaos": { mode: "Slow" },
    "/api/screenings/hold-policy": { seconds: 20 },
    "/api/demo": { dashboardUrl: "" },
    "/api/events/recent?take=100": [],
};

let originalFetch: typeof fetch;
let originalEventSource: unknown;

beforeEach(() => {
    originalFetch = globalThis.fetch;
    originalEventSource = (globalThis as Record<string, unknown>)["EventSource"];

    globalThis.fetch = ((input: RequestInfo | URL) => {
        const path = String(input);
        const body = path === `/api/bookings/${bookingId}` ? trackedBooking
            : path.startsWith("/api/bookings") ? []
            : routes[path];

        return Promise.resolve(new Response(JSON.stringify(body ?? null), {
            status: 200,
            headers: { "content-type": "application/json" },
        }));
    }) as typeof fetch;

    // The tape store opens one of these on first subscribe and never closes it, so the
    // stub only has to exist — nothing here drives it.
    (globalThis as Record<string, unknown>)["EventSource"] = class {
        addEventListener() {}
        onopen: (() => void) | null = null;
        onerror: (() => void) | null = null;
    };
});

afterEach(() => {
    // Testing Library only registers its own automatic cleanup for test runners that
    // expose a global afterEach at import time, which bun:test does not — so without
    // this the first test's tree is still mounted during the second.
    cleanup();

    globalThis.fetch = originalFetch;
    (globalThis as Record<string, unknown>)["EventSource"] = originalEventSource;
});

test("the whole page mounts and draws what the gateway served", async () => {
    render(<App />);

    // Step 1, once the catalogue lands — which also proves the "Loading…" placeholder is
    // replaced rather than being the page's permanent state.
    expect(await screen.findByText("The Third Man")).toBeDefined();
    expect(screen.getByText("3 of 4 seats free")).toBeDefined();

    // The masthead switch reads the server's hold policy rather than its own state.
    const shortHolds = screen.getByRole("checkbox", { name: /Short holds/ });
    await waitFor(() => expect((shortHolds as HTMLInputElement).checked).toBe(true));

    // The chaos strip likewise: "Slow (5s)" is pressed because Payments said so.
    await waitFor(() =>
        expect(screen.getByRole("button", { name: "Slow (5s)" }).getAttribute("aria-pressed"))
            .toBe("true"));

    // The ledgers, and the empty state each one has when the demo has done nothing yet.
    expect(screen.getByRole("tab", { name: "Your bookings" })).toBeDefined();
    expect(screen.getByText("No bookings for this address yet.")).toBeDefined();

    // The bus tape rail, with nothing on it. "Bus tape" is deliberately ambiguous text —
    // the collapsed dock's peek row says it too — so ask for the heading.
    expect(screen.getByRole("heading", { name: "Bus tape" })).toBeDefined();
    expect(screen.getAllByText("Waiting for the first message…").length).toBeGreaterThan(0);

    // Nothing is being tracked, so the flow section does not exist at all.
    expect(document.getElementById("flow-section")).toBeNull();
});

test("a ?booking= deep link draws the six steps, and a step is a handle", async () => {
    history.replaceState(null, "", `/?booking=${bookingId}`);
    render(<App />);

    // Six steps, one service each, numbered the way the bus tape stamps them. The numerals
    // are the join between the flow panel and the rail, so they are worth asserting.
    const steps = await screen.findAllByRole("button", { name: /^Step \d, / });
    expect(steps).toHaveLength(6);

    expect(screen.getByText("Booking placed")).toBeDefined();
    expect(screen.getByText("Seats held")).toBeDefined();
    expect(screen.getByText("Payment requested")).toBeDefined();
    expect(screen.getByText("Taking payment…")).toBeDefined();

    // The line that says where the synchronous half of the flow ends.
    expect(screen.getByText("HTTP stops here — everything below is a message")).toBeDefined();

    // Clicking a step pins the highlight; clicking it again lets go. Nothing else on the
    // page is pressed as a result — the other five steps are the same control.
    const payment = steps[3]!;

    expect(payment.getAttribute("aria-pressed")).toBe("false");

    fireEvent.click(payment);
    expect(payment.getAttribute("aria-pressed")).toBe("true");
    expect(steps.filter(step => step.getAttribute("aria-pressed") === "true")).toHaveLength(1);

    fireEvent.click(payment);
    expect(payment.getAttribute("aria-pressed")).toBe("false");

    history.replaceState(null, "", "/");
});

test("a ?screening= deep link reaches the seat map", async () => {
    history.replaceState(null, "", `/?screening=${screeningId}`);
    render(<App />);

    // A1 is free and pickable, A2 is sold and is not.
    const free = await screen.findByRole("button", { name: "Seat A1, Available" });
    expect((free as HTMLButtonElement).disabled).toBe(false);
    expect(free.getAttribute("aria-pressed")).toBe("false");

    const sold = screen.getByRole("button", { name: "Seat A2, Sold" });
    expect((sold as HTMLButtonElement).disabled).toBe(true);

    // The held seat carries the attribute the rAF loop drives its countdown ring from.
    expect(screen.getByRole("button", { name: "Seat A10, Held" })
        .getAttribute("data-hold-expires")).toBe("2099-01-01T00:00:00Z");

    history.replaceState(null, "", "/");
});
