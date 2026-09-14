// Demo Kino's front end. Everything it knows arrives through the gateway under /api,
// so the browser never learns that there are four services behind it.
//
// Placing a booking is one POST that returns 202. The rest of the story finishes on the
// message bus — the bus tape (see "Step 3.5" below) is what shows that happening, so
// this page only needs to poll a read model as a slow safety net, not as its main loop.

const API = '/api';

const el = id => document.getElementById(id);

const state = {
    screenings: [],
    screeningId: null,
    seatMap: null,
    selected: new Set(),
    tracked: null,      // the booking whose flow we are drawing
};

/* ── Transport ────────────────────────────────────────────────────────────── */

async function api(path, init) {
    const response = await fetch(API + path, init);

    if (!response.ok) {
        // The services answer failures as RFC 9457 problem details, so there is almost
        // always something better to show than the status text.
        const problem = await response.json().catch(() => null);
        throw new Error(problem?.detail ?? problem?.title ?? response.statusText);
    }

    return response.status === 204 ? null : response.json();
}

let toastTimer;

function toast(message) {
    const node = el('toast');
    node.textContent = message;
    node.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { node.hidden = true; }, 6000);
}

/* ── Formatting ───────────────────────────────────────────────────────────── */

const clock = new Intl.DateTimeFormat(undefined, { hour: '2-digit', minute: '2-digit' });
const dayAndTime = new Intl.DateTimeFormat(undefined, {
    weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit',
});
const preciseClock = new Intl.DateTimeFormat(undefined, {
    hour: '2-digit', minute: '2-digit', second: '2-digit', fractionalSecondDigits: 3,
});

const money = (amount, currency) =>
    `${Number(amount).toFixed(2).replace(/\.00$/, '')} ${currency}`;

const sinceStart = (from, to) => {
    const ms = new Date(to) - new Date(from);
    return ms < 1000 ? `+${Math.max(ms, 0)} ms` : `+${(ms / 1000).toFixed(1)} s`;
};

function replace(parent, ...children) {
    parent.replaceChildren(...children.flat().filter(Boolean));
}

function node(tag, props = {}, ...children) {
    const element = Object.assign(document.createElement(tag), props);
    element.append(...children.flat().filter(child => child !== null && child !== undefined));
    return element;
}

/* ── Step 1: the catalogue ────────────────────────────────────────────────── */

async function loadScreenings() {
    state.screenings = await api('/screenings');

    if (state.screenings.length === 0) {
        replace(el('screenings'), node('p', { className: 'empty' }, 'No screenings are on sale.'));
        return;
    }

    replace(el('screenings'), state.screenings.map(screening => {
        const taken = screening.totalSeats - screening.availableSeats;
        const sold = screening.totalSeats === 0 ? 0 : (taken / screening.totalSeats) * 100;

        const card = node('button',
            { className: 'screening', type: 'button' },
            node('span', { className: 'film', textContent: screening.filmTitle }),
            node('span', { className: 'where', textContent: screening.auditorium }),
            node('span', { className: 'meta' },
                node('span', { textContent: dayAndTime.format(new Date(screening.startsAtUtc)) }),
                node('span', { className: 'price', textContent: money(screening.ticketPrice, screening.currency) })),
            node('span', { className: 'bar' },
                node('i', { style: `width:${100 - sold}%` })),
            node('span', {
                className: screening.availableSeats === 0 ? 'free none' : 'free',
                textContent: screening.availableSeats === 0
                    ? 'Sold out'
                    : `${screening.availableSeats} of ${screening.totalSeats} seats free`,
            }));

        card.setAttribute('aria-pressed', String(screening.id === state.screeningId));
        card.onclick = () => selectScreening(screening.id);
        return card;
    }));
}

/* ── Step 2: the seat map ─────────────────────────────────────────────────── */

async function selectScreening(screeningId) {
    state.screeningId = screeningId;
    state.selected.clear();

    const url = new URL(location.href);
    url.searchParams.set('screening', screeningId);
    history.replaceState(null, '', url);

    await Promise.all([loadScreenings(), loadSeatMap()]);
    el('seating').hidden = false;
}

async function loadSeatMap() {
    if (!state.screeningId) return;

    state.seatMap = await api(`/screenings/${state.screeningId}/seats`);
    const rows = Map.groupBy(state.seatMap.seats, seat => seat.number[0]);

    replace(el('seatmap'), [...rows].map(([row, seats]) => node('div', { className: 'seat-row' },
        node('span', { className: 'row-label', textContent: row }),
        seats
            .sort((left, right) => Number(left.number.slice(1)) - Number(right.number.slice(1)))
            .map(seat => {
                const button = node('button', {
                    className: 'seat',
                    type: 'button',
                    textContent: seat.number.slice(1),
                    disabled: seat.status !== 'Available',
                    title: `${seat.number} — ${seat.status}`,
                });

                button.dataset.status = seat.status;
                button.setAttribute('aria-label', `Seat ${seat.number}, ${seat.status}`);
                button.setAttribute('aria-pressed', String(state.selected.has(seat.number)));
                button.onclick = () => toggleSeat(seat.number);
                return button;
            }),
        node('span', { className: 'row-label', textContent: row }))));

    updateTally();
}

function toggleSeat(number) {
    if (state.selected.has(number)) {
        state.selected.delete(number);
    } else if (state.selected.size >= 8) {
        // The same rule lives in the Screening aggregate; this is only a courtesy.
        toast('A single booking may hold at most 8 seats.');
        return;
    } else {
        state.selected.add(number);
    }

    for (const button of el('seatmap').querySelectorAll('.seat')) {
        const seat = button.getAttribute('aria-label').split(' ')[1].replace(',', '');
        button.setAttribute('aria-pressed', String(state.selected.has(seat)));
    }

    updateTally();
}

function updateTally() {
    const count = state.selected.size;
    const price = state.seatMap?.ticketPrice ?? 0;

    el('tally-seats').textContent = count === 0
        ? 'No seats selected'
        : `${[...state.selected].sort().join(', ')}`;

    el('tally-total').textContent = count === 0
        ? 'Pick one or more seats above.'
        : `${count} × ${money(price, state.seatMap.currency)} = ${money(count * price, state.seatMap.currency)}`;

    el('book').disabled = count === 0;
}

el('checkout').onsubmit = async event => {
    event.preventDefault();
    el('book').disabled = true;

    try {
        const placed = await api('/bookings', {
            method: 'POST',
            headers: { 'content-type': 'application/json' },
            body: JSON.stringify({
                screeningId: state.screeningId,
                customerEmail: el('email').value.trim(),
                seats: [...state.selected].sort(),
            }),
        });

        track(placed.bookingId);
        state.selected.clear();
        await refresh();
        el('flow-section').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    } catch (error) {
        toast(error.message);
    } finally {
        updateTally();
    }
};

/* ── Step 3: the flow ─────────────────────────────────────────────────────── */

function track(bookingId) {
    state.tracked = bookingId;
    el('flow-section').hidden = false;

    const url = new URL(location.href);
    url.searchParams.set('booking', bookingId);
    history.replaceState(null, '', url);
}


// Each step names the service that made the decision, because that is the thing worth
// seeing: no single component is driving this, they are just reacting to each other.
function drawFlow(booking, payment, mail) {
    if (!booking) return;

    const placed = booking.placedAtUtc;
    const failed = booking.status === 'Cancelled';

    const steps = [
        {
            what: 'Booking placed',
            who: 'Bookings — publishes BookingPlaced',
            at: placed,
            state: 'done',
        },
        {
            what: booking.filmTitle === '(unknown)' && !failed ? 'Holding seats…' : 'Seats held',
            who: 'Screenings — the aggregate decides, then publishes SeatsHeld',
            at: booking.seatsHeldAtUtc,
            state: booking.payBeforeUtc ? 'done' : (failed ? 'failed' : 'pending'),
            note: booking.payBeforeUtc
                ? `held until ${clock.format(new Date(booking.payBeforeUtc))}`
                : null,
        },
        {
            what: payment ? (payment.status === 'Captured' ? 'Payment captured' : 'Payment declined') : 'Taking payment…',
            who: 'Payments — publishes the outcome either way',
            at: payment?.decidedAtUtc ?? null,
            state: payment ? (payment.status === 'Captured' ? 'done' : 'failed') : (failed ? 'failed' : 'pending'),
            note: payment?.reference ?? payment?.declineReason ?? null,
        },
        {
            what: booking.status === 'Confirmed' ? 'Booking confirmed'
                : failed ? 'Booking cancelled'
                : 'Waiting…',
            who: failed
                ? 'Bookings — publishes BookingCancelled, so Screenings puts the seats back'
                : 'Bookings — publishes BookingConfirmed, so Screenings sells the seats',
            at: booking.finishedAtUtc,
            state: booking.status === 'Confirmed' ? 'done' : failed ? 'failed' : 'waiting',
            note: booking.cancellationReason,
        },
        {
            what: mail ? mail.outcome : 'E-mail',
            who: 'Notifications — renders it and sends it over SMTP to Mailpit',
            at: mail?.sentAtUtc ?? null,
            state: mail ? (failed ? 'failed' : 'done') : 'waiting',
            note: mail?.subject ?? null,
        },
    ];

    replace(el('flow'), steps.map(step => {
        const item = node('li', {},
            node('span', { className: 'dot', textContent: markerFor(step.state) }),
            node('span', { className: 'what', textContent: step.what }),
            node('span', { className: 'when', textContent: step.at ? sinceStart(placed, step.at) : '' }),
            node('span', { className: 'who', textContent: step.note ? `${step.who} · ${step.note}` : step.who }));

        item.dataset.state = step.state;
        return item;
    }));

    el('flow-note').textContent = booking.status === 'Confirmed'
        ? `Seats ${booking.seats.join(', ')} for ${booking.filmTitle} are yours. The ticket is in the Mailpit inbox, linked from the Aspire dashboard.`
        : failed
            ? 'The seats went straight back on sale — that is the compensating action, not a rollback.'
            : 'Every step below is a message. Nothing is orchestrating them.';
}

const markerFor = state => ({ done: '✓', failed: '✕', pending: '·', waiting: '' })[state] ?? '';

/* ── Step 3.5: the bus tape ───────────────────────────────────────────────── */
//
// Every service tees what it publishes and consumes onto a Redis feed; the gateway
// streams that feed here over Server-Sent Events (GET /api/events). This is the actual
// choreography happening, not an inference drawn from polling a read model — and an
// entry for the tracked booking is what schedules the one poll that keeps the flow
// above up to date, instead of a fixed-interval loop guessing when to look.

const MAX_TAPE_ROWS = 200;

let tapeRefreshTimer;

// state.tracked is one booking id today and becomes { mine, rival } once "race a
// rival" is in play; this is the one place that needs to know which shape it has.
function trackedCorrelationIds() {
    if (!state.tracked) return [];
    return typeof state.tracked === 'string' ? [state.tracked] : Object.values(state.tracked).filter(Boolean);
}

function tapeRowLabel(entry) {
    const verb = entry.kind === 'Published' ? 'published' : entry.kind === 'Faulted' ? 'failed on' : 'consumed';
    return `${entry.event} ${verb}`;
}

function tapeRowDetail(entry) {
    const parts = [];
    if (entry.durationMs != null) parts.push(`${Math.round(entry.durationMs)} ms`);
    if (entry.detail) parts.push(entry.detail);
    return parts.join(' — ');
}

function tapeRow(entry) {
    const detail = tapeRowDetail(entry);

    const row = node('li', { className: 'tape-row' },
        node('span', { className: 'svc', textContent: entry.service }),
        node('span', { className: 'event', textContent: tapeRowLabel(entry) }),
        node('span', { className: 'at', textContent: preciseClock.format(new Date(entry.atUtc)) }),
        detail ? node('span', { className: 'detail', textContent: detail }) : null);

    row.dataset.kind = entry.kind;
    row.dataset.tracked = String(trackedCorrelationIds().includes(entry.correlationId));
    row.style.setProperty('--tape-row-color', `var(--svc-${entry.service})`);
    return row;
}

function applyTapeFilter() {
    const onlyTracked = el('tape-filter').checked;

    for (const row of el('tape-rows').children) {
        if (row.classList.contains('tape-empty')) continue;
        row.hidden = onlyTracked && row.dataset.tracked !== 'true';
    }
}

el('tape-filter').onchange = applyTapeFilter;

function appendTapeRow(entry) {
    const rows = el('tape-rows');

    rows.querySelector('.tape-empty')?.remove();
    rows.append(tapeRow(entry));

    while (rows.children.length > MAX_TAPE_ROWS) {
        rows.firstElementChild?.remove();
    }

    applyTapeFilter();

    // A message for the flow we are drawing means that flow just moved: look now
    // instead of waiting for the next safety-net poll. Debounced because the read
    // model the flow queries is written a moment after the message that reports it.
    if (trackedCorrelationIds().includes(entry.correlationId)) {
        clearTimeout(tapeRefreshTimer);
        tapeRefreshTimer = setTimeout(() => refresh().catch(() => {}), 150);
    }
}

function setTapeStatus(connected, text) {
    el('tape-status').dataset.connected = String(connected);
    el('tape-status-text').textContent = text;
}

async function connectTape() {
    try {
        for (const entry of await api('/events/recent?take=100')) {
            appendTapeRow(entry);
        }
    } catch {
        // The live stream connected below will carry on from here regardless; a seed
        // that failed to load is not worth failing the page over.
    }

    const source = new EventSource(`${API}/events`);

    source.addEventListener('bus', event => appendTapeRow(JSON.parse(event.data)));
    source.onopen = () => setTapeStatus(true, 'live');
    // EventSource reconnects on its own; this only reflects that state in the UI.
    source.onerror = () => setTapeStatus(false, 'reconnecting…');
}

/* ── Ledgers ──────────────────────────────────────────────────────────────── */

function table(headings, rows, emptyMessage) {
    if (rows.length === 0) {
        return node('p', { className: 'empty' }, emptyMessage);
    }

    return node('table', {},
        node('thead', {}, node('tr', {}, headings.map(heading => node('th', { textContent: heading })))),
        node('tbody', {}, rows));
}

const pill = value => node('span', { className: `pill ${value}`, textContent: value });

function drawBookings(bookings) {
    replace(el('pane-bookings'), table(
        ['Film', 'Seats', 'Total', 'Status', 'Detail'],
        bookings.map(booking => node('tr', {},
            node('td', { textContent: booking.filmTitle }),
            node('td', { textContent: booking.seats.join(', ') }),
            node('td', { textContent: money(booking.total, booking.currency) }),
            node('td', {}, pill(booking.status)),
            node('td', { className: 'wrap', textContent: booking.paymentReference ?? booking.cancellationReason ?? '—' }))),
        'No bookings for this address yet.'));
}

function drawPayments(payments) {
    replace(el('pane-payments'), table(
        ['Payer', 'Amount', 'Status', 'Reference or reason', 'Decided'],
        payments.map(payment => node('tr', {},
            node('td', { textContent: payment.payer }),
            node('td', { textContent: money(payment.amount, payment.currency) }),
            node('td', {}, pill(payment.status)),
            node('td', { className: 'wrap', textContent: payment.reference ?? payment.declineReason ?? '—' }),
            node('td', { textContent: clock.format(new Date(payment.decidedAtUtc)) }))),
        'Payments has not been asked for anything yet.'));
}

function drawMail(mail) {
    replace(el('pane-mail'), table(
        ['To', 'Subject', 'Outcome', 'Sent'],
        mail.map(entry => node('tr', {},
            node('td', { textContent: entry.recipient }),
            node('td', { className: 'wrap', textContent: entry.subject }),
            node('td', { className: 'wrap', textContent: entry.outcome }),
            node('td', { textContent: clock.format(new Date(entry.sentAtUtc)) }))),
        'Nothing has been sent yet.'));
}

for (const tab of document.querySelectorAll('[role="tab"]')) {
    tab.onclick = () => {
        for (const other of document.querySelectorAll('[role="tab"]')) {
            const selected = other === tab;
            other.setAttribute('aria-selected', String(selected));
            el(other.getAttribute('aria-controls')).hidden = !selected;
        }
    };
}

/* ── Polling ──────────────────────────────────────────────────────────────── */

const settled = booking => booking?.status === 'Confirmed' || booking?.status === 'Cancelled';

async function refresh() {
    const email = el('email').value.trim();

    const [bookings, payments, mail] = await Promise.all([
        email.includes('@') ? api(`/bookings?customerEmail=${encodeURIComponent(email)}`).catch(() => []) : [],
        api('/payments?take=25').catch(() => []),
        api('/notifications?take=25').catch(() => []),
    ]);

    drawBookings(bookings);
    drawPayments(payments);
    drawMail(mail);

    if (state.tracked) {
        const booking = bookings.find(candidate => candidate.bookingId === state.tracked)
            ?? await api(`/bookings/${state.tracked}`).catch(() => null);

        drawFlow(
            booking,
            payments.find(payment => payment.bookingId === state.tracked),
            mail.find(entry => entry.bookingId === state.tracked));

        if (settled(booking)) {
            await Promise.all([loadScreenings(), loadSeatMap()]);
            state.tracked = null;
        }
    }
}

// A slow safety net, not the main loop: the bus tape is what actually notices a tracked
// booking has moved (see appendTapeRow above) and asks for a near-immediate refresh.
// This tick only covers the gap — a page that loaded before the tape connected, or a
// tape entry that Redis never delivered.
async function tick() {
    try {
        await refresh();
        if (!state.tracked) await loadScreenings();
    } catch (error) {
        console.warn('refresh failed', error);
    } finally {
        setTimeout(tick, 4000);
    }
}

el('email').onchange = () => refresh().catch(() => {});

connectTape();
await loadScreenings().catch(error => toast(`Could not reach the gateway: ${error.message}`));

// ?screening=<id> makes a seat map shareable, and lets a headless browser reach step 2.
const requested = new URLSearchParams(location.search).get('screening');

if (requested && state.screenings.some(screening => screening.id === requested)) {
    await selectScreening(requested).catch(() => {});
}

// ?booking=<id> reopens a flow that has already finished.
const revisiting = new URLSearchParams(location.search).get('booking');

if (revisiting) {
    track(revisiting);
}

await refresh().catch(() => {});
setTimeout(tick, 1500);
