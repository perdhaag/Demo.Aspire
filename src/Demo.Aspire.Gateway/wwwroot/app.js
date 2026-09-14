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
    tracked: null,          // the booking whose flow we are drawing
    holdPolicySeconds: 180, // refreshed from GET /screenings/hold-policy; the seat
                            // countdown ring assumes this as the hold's full length,
                            // since the seat map does not carry when a hold began.
    chaosMode: 'None',      // refreshed from GET /payments/chaos on every tick, since
                            // "Failing" clears itself server-side without a click.
    entriesByCorrelation: new Map(), // bus tape entries seen this session, grouped by
                                     // the booking id they belong to — this is what the
                                     // trace waterfall is built from.
    dashboardUrl: '',       // from GET /api/demo; empty just hides the dashboard link.
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
                if (seat.holdExpiresAtUtc) button.dataset.holdExpires = seat.holdExpiresAtUtc;
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

// One rAF loop for every held seat currently on screen, rather than a timer per seat.
// --hold-pct assumes the seat's full hold length was state.holdPolicySeconds, which is
// true unless the policy changed mid-hold — close enough for a countdown, and the seat
// map does not carry when a hold actually began.
function tickHoldRings() {
    const now = Date.now();

    for (const button of document.querySelectorAll('.seat[data-hold-expires]')) {
        const remainingMs = new Date(button.dataset.holdExpires).getTime() - now;
        const totalMs = state.holdPolicySeconds * 1000;
        const pct = Math.max(0, Math.min(100, (remainingMs / totalMs) * 100));
        button.style.setProperty('--hold-pct', pct.toFixed(1));
    }

    requestAnimationFrame(tickHoldRings);
}

requestAnimationFrame(tickHoldRings);

/* ── Short holds: a demo switch on Screenings' hold policy ───────────────── */

async function loadHoldPolicy() {
    const policy = await api('/screenings/hold-policy');
    state.holdPolicySeconds = policy.seconds;
    el('short-holds').checked = policy.seconds <= 30;
}

el('short-holds').onchange = async () => {
    const seconds = el('short-holds').checked ? 20 : 180;

    try {
        const policy = await api('/screenings/hold-policy', {
            method: 'POST',
            headers: { 'content-type': 'application/json' },
            body: JSON.stringify({ seconds }),
        });

        state.holdPolicySeconds = policy.seconds;
        toast(`New seat holds now last ${policy.seconds}s.`);
    } catch (error) {
        toast(error.message);
    }
};

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
    el('race').disabled = count === 0;
}

const RIVAL_EMAIL = 'rival@example.com';

function placeBooking(customerEmail, seats) {
    return api('/bookings', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ screeningId: state.screeningId, customerEmail, seats }),
    });
}

el('checkout').onsubmit = async event => {
    event.preventDefault();
    el('book').disabled = true;
    el('race').disabled = true;

    try {
        const placed = await placeBooking(el('email').value.trim(), [...state.selected].sort());

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

// Both bookings ask for the same seats on the same screening in the same instant.
// Screenings' seat map is the one consistency boundary in this system (see
// Screening.HoldSeats), so exactly one of the two can actually hold them — the other's
// flow shows a rejected hold rather than a rollback, because there was never anything
// to roll back.
el('race').onclick = async () => {
    el('book').disabled = true;
    el('race').disabled = true;

    const seats = [...state.selected].sort();
    const email = el('email').value.trim();

    const [mine, rival] = await Promise.allSettled([
        placeBooking(email, seats),
        placeBooking(RIVAL_EMAIL, seats),
    ]);

    if (mine.status === 'rejected' && rival.status === 'rejected') {
        toast(mine.reason.message);
    } else {
        trackRace(
            mine.status === 'fulfilled' ? mine.value.bookingId : null,
            rival.status === 'fulfilled' ? rival.value.bookingId : null);

        if (mine.status === 'rejected') toast(`Your booking: ${mine.reason.message}`);
        if (rival.status === 'rejected') toast(`Rival's booking: ${rival.reason.message}`);

        state.selected.clear();
        await refresh();
        el('flow-section').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    updateTally();
};

/* ── Chaos: a demo switch on Payments' simulated card network ────────────── */

const CHAOS_NOTES = {
    None: 'Payments is behaving normally.',
    Paused: 'Payments is paused — an authorization already in flight stays unacknowledged on the queue. Nothing is retrying it; it is simply waiting.',
    Slow: 'Every authorization now takes about five seconds.',
    Failing: 'The next authorization or two will fail. MassTransit’s retry policy keeps trying — the tape will show the faults, then a success.',
};

function applyChaosMode(mode) {
    state.chaosMode = mode;
    el('chaos-pause').setAttribute('aria-pressed', String(mode === 'Paused'));
    el('chaos-slow').setAttribute('aria-pressed', String(mode === 'Slow'));
    el('chaos-fail').setAttribute('aria-pressed', String(mode === 'Failing'));
    el('chaos-note').textContent = CHAOS_NOTES[mode] ?? '';
}

async function setChaosMode(mode) {
    try {
        applyChaosMode((await api('/payments/chaos', {
            method: 'POST',
            headers: { 'content-type': 'application/json' },
            body: JSON.stringify({ mode }),
        })).mode);
    } catch (error) {
        toast(error.message);
    }
}

// Pause and Slow are switches — clicking an active one turns it back off. "Fail twice,
// then succeed" is a one-shot action: the server counts the failures down and returns
// itself to None on its own, which is why it is never rendered as an active toggle for
// longer than the next poll takes to notice.
el('chaos-pause').onclick = () => setChaosMode(state.chaosMode === 'Paused' ? 'None' : 'Paused');
el('chaos-slow').onclick = () => setChaosMode(state.chaosMode === 'Slow' ? 'None' : 'Slow');
el('chaos-fail').onclick = () => setChaosMode('Failing');

/* ── Step 3: the flow ─────────────────────────────────────────────────────── */

const FLOW_PANELS = {
    mine: { list: el('flow-mine'), note: el('flow-note-mine'), waterfall: el('waterfall-mine') },
    rival: { list: el('flow-rival'), note: el('flow-note-rival'), waterfall: el('waterfall-rival') },
};

function clearFlowPanels() {
    for (const panel of Object.values(FLOW_PANELS)) {
        replace(panel.list);
        panel.note.textContent = '';
        replace(panel.waterfall);
        panel.waterfall.hidden = true;
    }
}

function track(bookingId) {
    clearFlowPanels();
    state.tracked = { mine: bookingId, rival: null };
    el('flow-section').hidden = false;
    el('flow-label-mine').hidden = true;
    el('flow-panel-rival').hidden = true;

    const url = new URL(location.href);
    url.searchParams.set('booking', bookingId);
    history.replaceState(null, '', url);
}

// "Race a rival" tracks two bookings at once, so the two flow panels both get a label
// and the URL stops pointing at a single booking — there is no one booking to deep-link
// to once there are two racing for the same seats.
function trackRace(mineId, rivalId) {
    clearFlowPanels();
    state.tracked = { mine: mineId, rival: rivalId };
    el('flow-section').hidden = false;
    el('flow-label-mine').hidden = false;
    el('flow-panel-rival').hidden = false;

    const url = new URL(location.href);
    url.searchParams.delete('booking');
    history.replaceState(null, '', url);
}

// Each step names the service that made the decision, because that is the thing worth
// seeing: no single component is driving this, they are just reacting to each other.
function drawFlow(booking, payment, mail, panel) {
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
            // While payment is still outstanding, tickHoldCountdowns overwrites the
            // static note above with a live "Xs left to pay" every second, turning
            // amber under 30s — the short-holds switch is only worth having if a
            // viewer can watch the number count down, not read a fixed timestamp.
            payBeforeUtc: booking.payBeforeUtc && !payment && !failed ? booking.payBeforeUtc : null,
        },
        {
            // The chaos strip's "Pause Payments" is named here on purpose: without it,
            // a paused payment and a slow one look identical — both just say "pending".
            what: payment
                ? (payment.status === 'Captured' ? 'Payment captured' : 'Payment declined')
                : (!failed && state.chaosMode === 'Paused' ? 'Payments is paused…' : 'Taking payment…'),
            who: 'Payments — publishes the outcome either way',
            at: payment?.decidedAtUtc ?? null,
            state: payment ? (payment.status === 'Captured' ? 'done' : 'failed') : (failed ? 'failed' : 'pending'),
            note: payment?.reference ?? payment?.declineReason
                ?? (!payment && !failed && state.chaosMode === 'Paused'
                    ? 'the message is waiting on the queue, not retrying'
                    : null),
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

    replace(panel.list, steps.map(step => {
        const item = node('li', {},
            node('span', { className: 'dot', textContent: markerFor(step.state) }),
            node('span', { className: 'what', textContent: step.what }),
            node('span', { className: 'when', textContent: step.at ? sinceStart(placed, step.at) : '' }),
            node('span', { className: 'who', textContent: step.note ? `${step.who} · ${step.note}` : step.who }));

        item.dataset.state = step.state;

        if (step.payBeforeUtc) {
            item.dataset.payBefore = step.payBeforeUtc;
            item.dataset.whoBase = step.who;
        }

        return item;
    }));

    panel.note.textContent = booking.status === 'Confirmed'
        ? `Seats ${booking.seats.join(', ')} for ${booking.filmTitle} are yours. The ticket is in the Mailpit inbox, linked from the Aspire dashboard.`
        : failed
            ? 'The seats went straight back on sale — that is the compensating action, not a rollback.'
            : 'Every step below is a message. Nothing is orchestrating them.';

    tickHoldCountdowns();
}

const markerFor = state => ({ done: '✓', failed: '✕', pending: '·', waiting: '' })[state] ?? '';

/* ── Trace waterfall ──────────────────────────────────────────────────────── */
//
// Built entirely from the bus tape's own timestamps (recordEntryForWaterfall, above) —
// not a real trace backend, which is exactly why the caption beside it says so. Fine to
// within a millisecond or two on one machine sharing one clock; not a substitute for
// the Aspire dashboard's own trace view, which is what the link beside it is for.

const SERVICE_LANE_ORDER = ['screenings', 'bookings', 'payments', 'notifications', 'gateway'];

// Pairs each event's Published row with the first Consumed row for the same event —
// a Faulted row is a retry that did not (yet) finish the hop, so it is left out of the
// waterfall itself, though it already showed up in the tape rail above.
function buildHops(entries) {
    const byEvent = new Map();

    for (const entry of entries) {
        const bucket = byEvent.get(entry.event) ?? { published: null, consumed: null };

        if (entry.kind === 'Published') bucket.published = entry;
        else if (entry.kind === 'Consumed' && !bucket.consumed) bucket.consumed = entry;

        byEvent.set(entry.event, bucket);
    }

    return [...byEvent.values()].filter(hop => hop.published);
}

function drawWaterfall(booking, entries, container) {
    const hops = buildHops(entries).map(hop => ({
        service: hop.consumed?.service ?? null,
        publishedAtMs: new Date(hop.published.atUtc) - new Date(booking.placedAtUtc),
        consumeStartMs: hop.consumed
            ? new Date(hop.consumed.atUtc) - new Date(booking.placedAtUtc) - hop.consumed.durationMs
            : null,
        consumeEndMs: hop.consumed ? new Date(hop.consumed.atUtc) - new Date(booking.placedAtUtc) : null,
    }));

    if (hops.length === 0) {
        container.hidden = true;
        return;
    }

    const totalMs = Math.max(...hops.map(hop => hop.consumeEndMs ?? hop.publishedAtMs), 1);
    const pct = ms => `${Math.max(0, Math.min(100, (ms / totalMs) * 100)).toFixed(2)}%`;

    // The gap between a publish and the hop's own consume is queue wait — the point of
    // this whole panel. The widest one gets a label; the others still draw, so the
    // shape of "where the time went" is visible even without reading a number.
    const gaps = hops
        .filter(hop => hop.consumeStartMs !== null && hop.consumeStartMs > hop.publishedAtMs)
        .map(hop => ({ startMs: hop.publishedAtMs, endMs: hop.consumeStartMs }));

    const widestGap = gaps.reduce(
        (widest, gap) => (gap.endMs - gap.startMs > (widest?.endMs - widest?.startMs ?? -1) ? gap : widest),
        null);

    const gapTrack = node('div', { className: 'wf-gaptrack' }, gaps.map(gap => {
        const bar = node('span', {
            className: 'wf-gap',
            style: `left:${pct(gap.startMs)}; width:${pct(gap.endMs - gap.startMs)}`,
        });

        if (gap === widestGap) {
            bar.append(node('span', {
                className: 'wf-gap-label',
                style: `left:${pct((gap.startMs + gap.endMs) / 2)}`,
                textContent: `${Math.round(gap.endMs - gap.startMs)} ms queue wait`,
            }));
        }

        return bar;
    }));

    const lanes = SERVICE_LANE_ORDER
        .map(service => ({ service, bars: hops.filter(hop => hop.service === service) }))
        .filter(lane => lane.bars.length > 0)
        .map(lane => node('div', { className: 'wf-lane' },
            node('span', { className: 'wf-lane-label', textContent: lane.service }),
            node('div', { className: 'wf-track' }, lane.bars.map(hop => {
                const bar = node('span', {
                    className: 'wf-bar',
                    style: `left:${pct(hop.consumeStartMs)}; width:${pct(hop.consumeEndMs - hop.consumeStartMs)}`,
                    title: `${hop.service}: ${Math.round(hop.consumeEndMs - hop.consumeStartMs)} ms`,
                });

                bar.style.setProperty('--tape-row-color', `var(--svc-${lane.service})`);
                return bar;
            }))));

    replace(container, gapTrack, lanes, node('div', { className: 'wf-axis' },
        node('span', { textContent: '0 ms' }),
        node('span', { textContent: `total ${Math.round(totalMs)} ms` })));

    container.hidden = false;
}

// Runs every second regardless of when the flow was last redrawn, so "Xs left to pay"
// counts down smoothly between polls instead of jumping only when refresh() happens to
// fire. Ticking a countdown by re-rendering the whole flow list would also fight the
// browser's own focus and hover state on it, small as that risk is here.
function formatPayCountdown(remainingMs) {
    if (remainingMs <= 0) return 'expiring now…';

    const totalSeconds = Math.ceil(remainingMs / 1000);
    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;

    return minutes > 0 ? `${minutes}m ${seconds}s left to pay` : `${seconds}s left to pay`;
}

function tickHoldCountdowns() {
    const now = Date.now();

    for (const item of document.querySelectorAll('.flow li[data-pay-before]')) {
        const remaining = new Date(item.dataset.payBefore).getTime() - now;
        const who = item.querySelector('.who');

        who.textContent = `${item.dataset.whoBase} · ${formatPayCountdown(remaining)}`;
        item.classList.toggle('countdown-warn', remaining > 0 && remaining <= 30_000);
    }
}

setInterval(tickHoldCountdowns, 1000);

/* ── Step 3.5: the bus tape ───────────────────────────────────────────────── */
//
// Every service tees what it publishes and consumes onto a Redis feed; the gateway
// streams that feed here over Server-Sent Events (GET /api/events). This is the actual
// choreography happening, not an inference drawn from polling a read model — and an
// entry for the tracked booking is what schedules the one poll that keeps the flow
// above up to date, instead of a fixed-interval loop guessing when to look.

const MAX_TAPE_ROWS = 200;

let tapeRefreshTimer;

// state.tracked is { mine, rival } — rival is null outside of "Race a rival" — since
// the correlation id for a whole booking's flow is the booking id itself (see
// PlaceBookingHandler, which stamps it onto CorrelationContext at the very start).
function trackedCorrelationIds() {
    return state.tracked ? [state.tracked.mine, state.tracked.rival].filter(Boolean) : [];
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

const MAX_TRACKED_CORRELATIONS = 50;

// Grouped by correlation id so the trace waterfall can be rebuilt for whichever
// booking(s) are tracked, without asking the gateway for anything it has not already
// streamed here. Capped the same way the tape rail itself is capped, so a long-running
// demo session cannot grow this without bound.
function recordEntryForWaterfall(entry) {
    if (!state.entriesByCorrelation.has(entry.correlationId)) {
        if (state.entriesByCorrelation.size >= MAX_TRACKED_CORRELATIONS) {
            const oldest = state.entriesByCorrelation.keys().next().value;
            state.entriesByCorrelation.delete(oldest);
        }

        state.entriesByCorrelation.set(entry.correlationId, []);
    }

    state.entriesByCorrelation.get(entry.correlationId).push(entry);
}

function appendTapeRow(entry) {
    const rows = el('tape-rows');

    rows.querySelector('.tape-empty')?.remove();
    rows.append(tapeRow(entry));
    recordEntryForWaterfall(entry);

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

    const [bookings, payments, mail, chaos] = await Promise.all([
        email.includes('@') ? api(`/bookings?customerEmail=${encodeURIComponent(email)}`).catch(() => []) : [],
        api('/payments?take=25').catch(() => []),
        api('/notifications?take=25').catch(() => []),
        api('/payments/chaos').catch(() => null),
    ]);

    drawBookings(bookings);
    drawPayments(payments);
    drawMail(mail);

    // Re-synced here, not only from a click, because "Failing" clears itself back to
    // None server-side once it has burned through its failures.
    if (chaos) applyChaosMode(chaos.mode);

    if (state.tracked) {
        const resolveBooking = async id => id && (
            bookings.find(candidate => candidate.bookingId === id)
                ?? await api(`/bookings/${id}`).catch(() => null));

        const { mine, rival } = state.tracked;
        const [mineBooking, rivalBooking] = await Promise.all([resolveBooking(mine), resolveBooking(rival)]);

        if (mineBooking) {
            drawFlow(
                mineBooking,
                payments.find(payment => payment.bookingId === mine),
                mail.find(entry => entry.bookingId === mine),
                FLOW_PANELS.mine);
            drawWaterfall(mineBooking, state.entriesByCorrelation.get(mine) ?? [], el('waterfall-mine'));
        }

        if (rival && rivalBooking) {
            drawFlow(
                rivalBooking,
                payments.find(payment => payment.bookingId === rival),
                mail.find(entry => entry.bookingId === rival),
                FLOW_PANELS.rival);
            drawWaterfall(rivalBooking, state.entriesByCorrelation.get(rival) ?? [], el('waterfall-rival'));
        }

        // Both sides need to be finished before the seat map is worth reloading — a
        // race that is still in flight is exactly the moment not to.
        if (settled(mineBooking) && (!rival || settled(rivalBooking))) {
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

// The dashboard link is fetched once, not per booking: it names the environment, not
// anything about one flow. An empty dashboardUrl (see AppHost.cs) just leaves it hidden.
async function loadDemoInfo() {
    const { dashboardUrl } = await api('/demo');

    if (dashboardUrl) {
        state.dashboardUrl = dashboardUrl;
        el('dashboard-link').href = dashboardUrl;
        el('dashboard-link').hidden = false;
    }
}

connectTape();
loadHoldPolicy().catch(() => {}); // the switch just stays at its default if this fails
loadDemoInfo().catch(() => {}); // the link just stays hidden if this fails
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
