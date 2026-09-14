# Implementation plan — five features to make the demo visible

> **Historical.** All five features shipped, and the front end has since been rewritten
> from the `app.js` this document refers to into React — see
> [react-ui-plan.md](react-ui-plan.md). The file names below (`wwwroot/app.js`,
> `wwwroot/app.css`) no longer exist; the behaviour they describe does, in
> `src/Demo.Aspire.Web`.

Five features, in the order they should be built. Each one is written so it can be
picked up on its own, but features 2–5 all read from the event feed that feature 1
introduces, so **feature 1 comes first**.

| # | Feature | What the audience sees | Size |
|---|---------|------------------------|------|
| 1 | Live bus tape (SSE) | Messages scroll past as they are published and consumed | Large |
| 2 | Short holds and a live countdown | Held seats tick down and go back on sale by themselves | Medium |
| 3 | Chaos strip | Pause, slow or break Payments and watch the flow survive it | Medium |
| 4 | Race a rival booker | Two flows side by side; one wins the seats, one is refused | Small |
| 5 | Trace waterfall | Where the ~900 ms actually went, queue wait included | Medium |

The marquee combination is **2 + 3**: turn on short holds, pause Payments, book seats,
and the audience watches the hold lapse and the seats return to the pool without anyone
touching anything. That path exists in the code today (`SeatHoldSweeper`) and has never
been demonstrable, because three minutes is longer than any demo's attention span.

---

## Phase 0 — app host wiring

Two prerequisites, both in `src/Demo.Aspire.AppHost/AppHost.cs`.

**Bookings and Payments need Redis.** They have no cache reference today; the bus tape
writes from every service, so both gain one:

```csharp
var bookings = builder.AddProject<Projects.Demo_Aspire_Bookings>(ResourceNames.Services.Bookings)
    .WithReference(bookingsDb).WaitFor(bookingsDb)
    .WithReference(cache).WaitFor(cache)          // ← new: the bus tape writes here
    .WithReference(messaging).WaitFor(messaging)
    …
```

Same two lines for `payments`. Worth a comment saying why, since "Payments needs a
cache" is otherwise a surprising claim about a service that caches nothing.

**The gateway needs the dashboard's URL** for the deep links in feature 5. In Aspire 13
the dashboard is served by the app host itself, so its own `ASPNETCORE_URLS` is the
source:

```csharp
var dashboardUrl = builder.Configuration["ASPNETCORE_URLS"]?.Split(';')[0];

builder.AddProject<Projects.Demo_Aspire_Gateway>(ResourceNames.Services.Gateway)
    …
    .WithEnvironment("Demo__DashboardUrl", dashboardUrl ?? string.Empty);
```

⚠️ **Verify at runtime.** Confirm the value that arrives is the dashboard origin and
that a trace opens at `{dashboardUrl}/traces/detail/{traceId}`. If the route differs,
fix the template in one place (`Demo.Aspire.Gateway/Program.cs`). Everything downstream
treats an empty value as "hide the link", so a wrong guess degrades rather than breaks.

---

## Phase 1 — the live bus tape

Today `app.js` polls `/api/bookings/{id}` every 500 ms while a booking is in flight. The
page therefore claims "everything past *Placed* happens on RabbitMQ" while doing the
exact opposite of watching RabbitMQ. This replaces that with a feed of the real thing.

### 1.1 The tape entry and writer — `Platform/Messaging/BusTap.cs` (new)

```csharp
public sealed record BusTapEntry(
    Guid MessageId,
    Guid CorrelationId,
    string Event,            // "BookingPlaced" — the contract's short name
    string Service,          // "bookings" — the Aspire resource name
    BusTapKind Kind,         // Published | Consumed | Faulted
    DateTimeOffset AtUtc,
    double? DurationMs,      // consume only: how long the handler took
    string? TraceId,         // W3C trace id, for the dashboard deep link
    string? Detail);         // fault message or a one-line summary

public enum BusTapKind { Published, Consumed, Faulted }
```

`BusTap` is a singleton over `IConnectionMultiplexer` doing two writes per entry:

* `LPUSH bus:tape` + `LTRIM bus:tape 0 199` — a capped history, so a browser that
  connects late still sees the last booking's story. This is the same capped-feed
  pattern `NotificationLog` already uses; copy its shape deliberately.
* `PUBLISH bus:events <json>` — the live channel the gateway subscribes to.

Use a `JsonSerializerContext` for the entry, as `NotificationJsonContext` does.

**Failures must be swallowed.** A demo aid may never fail a business message: wrap the
write in try/catch and log at debug. Note this in the class comment — it is the
difference between observability and coupling.

### 1.2 The observers — `Platform/Messaging/BusTapObservers.cs` (new)

* `BusTapPublishObserver : IPublishObserver` — `PostPublish` writes a `Published` entry.
* `BusTapConsumeObserver : IConsumeObserver` — `PostConsume` writes `Consumed` with
  `context.ReceiveContext.ElapsedTime.TotalMilliseconds`; `ConsumeFault` writes
  `Faulted` with the exception message and `context.GetRetryAttempt()`.

Both read `CorrelationId` off `IIntegrationEvent` — that interface exists precisely for
this, and the correlation id is the booking id, so the whole conversation groups for
free. Take `TraceId` from `Activity.Current?.TraceId`.

### 1.3 Registration — `Platform/Messaging/MessagingExtensions.cs`

Add an opt-in extension member next to the existing two:

```csharp
/// <summary>
/// Tees every message this service publishes or consumes onto a Redis feed, so the
/// demo UI can show the choreography rather than assert that it happens. Requires a
/// Redis client to have been registered first.
/// </summary>
public IHostApplicationBuilder AddBusTap()
```

It registers the `BusTap` singleton and adds both observers inside `AddMassTransit`
(`registration.AddPublishObserver<…>()` / `AddConsumeObserver<…>()`).

The service name comes from `builder.Configuration["OTEL_SERVICE_NAME"]` — Aspire sets
it to the resource name (`screenings`, `bookings`, …) — falling back to
`builder.Environment.ApplicationName`.

`Demo.Aspire.Platform.csproj` gains `Aspire.StackExchange.Redis`. That is a real
widening of Platform's dependencies; the alternative is an `IBusTap` abstraction with
the Redis implementation per service, which is more ceremony than a demo aid deserves.
Say so in the csproj comment.

Each service's `Program.cs` then calls `builder.AddBusTap()` after its Redis
registration and before/after `AddMessaging` (order does not matter). Bookings and
Payments each need `builder.AddRedisClient(ResourceNames.Cache);` added, matching
Notifications.

### 1.4 The SSE endpoint — `Gateway/Program.cs`

.NET 10's `TypedResults.ServerSentEvents` does the protocol, which keeps this to about
thirty lines and is itself worth pointing at during the demo:

```csharp
app.MapGet("/api/events", (IConnectionMultiplexer redis, CancellationToken ct) =>
    TypedResults.ServerSentEvents(Tape.StreamAsync(redis, ct), eventType: "bus"));
```

`Tape.StreamAsync` bridges Redis pub/sub to `IAsyncEnumerable<BusTapEntry>` through an
unbounded `Channel<BusTapEntry>`: subscribe on entry, unsubscribe in a `finally`. A
second endpoint, `GET /api/events/recent`, returns the capped `bus:tape` list so the
page can draw history before the stream starts.

Map both **before** `app.MapReverseProxy()`. No YARP route matches `/api/events` today,
so no `appsettings.json` change is needed — but add a comment saying the gateway now
serves one route of its own, because that is a genuine change to what this project is.

The gateway needs `builder.AddRedisClient(ResourceNames.Cache)` alongside its existing
`AddRedisOutputCache`.

### 1.5 The rail — `wwwroot/index.html`, `app.css`, `app.js`

* A new `<aside class="tape">` in a two-column layout beside the main content; it
  collapses below the content under ~900 px.
* One row per entry: `+1.2 s`, a service badge coloured per service, the event name, a
  short correlation id, and the duration for consume rows. Faults are red and say
  `retry 2`.
* Rows whose correlation id matches `state.tracked` are highlighted; there is a
  "this booking only" filter toggle.
* `EventSource('/api/events')` with the browser's own reconnect. Seed from
  `/api/events/recent` on load.

**Polling changes, and this is the point of the feature.** Delete the 500 ms hot poll.
Instead, an arriving tape entry for the tracked correlation id schedules one
`refresh()` (debounced ~150 ms, because the read model is written a moment after the
message is published). Keep a slow 4 s poll as a safety net and say so in a comment —
the honest version is "push tells us when to look", not "push replaces the read model".

### 1.6 Colour

Give each of the five services one colour and use it everywhere — the badge, the
waterfall lane in feature 5, the flow step's dot. Define them as CSS custom properties
in `app.css` (`--svc-screenings`, …). This is the single cheapest thing that makes the
demo readable from the back of a room.

---

## Phase 2 — short holds and a live countdown

### 2.1 The domain change — `Screenings/Domain/Screening.cs`

`HoldDuration` is a `static readonly` field read inside `HoldSeats`. Make the policy an
argument instead, which is the more correct model anyway — how long a hold lasts is a
commercial decision, not a fact about the aggregate:

```csharp
/// <summary>The hold a screening gets when the caller states no other policy.</summary>
public static readonly TimeSpan DefaultHoldDuration = TimeSpan.FromMinutes(3);

public Result<SeatHold> HoldSeats(
    BookingReference booking,
    IReadOnlyCollection<SeatNumber> requested,
    DateTimeOffset now,
    TimeSpan holdDuration);
```

`tests/Demo.Aspire.Domain.Tests/ScreeningTests.cs` references `Screening.HoldDuration`
on lines 56 and 95; both become `Screening.DefaultHoldDuration` and the call sites pass
it. Add one new test: a hold of an explicitly short duration expires when the clock
passes it. Domain tests stay container-free.

### 2.2 The runtime switch — `Screenings/Features/HoldPolicy/` (new slice)

```csharp
public sealed class HoldPolicy               // singleton
{
    public TimeSpan Duration { get; set; } = Screening.DefaultHoldDuration;

    /// <summary>A 20-second hold that is only swept every 15 seconds is a 35-second hold.</summary>
    public TimeSpan SweepInterval => TimeSpan.FromSeconds(Math.Clamp(Duration.TotalSeconds / 5, 2, 15));
}
```

Endpoints `GET /screenings/hold-policy` and `POST /screenings/hold-policy`
(`{ "seconds": 20 }`, clamped to 10…600). `HoldSeatsConsumer` injects the singleton and
passes `policy.Duration` to `HoldSeats`.

A mutable singleton is the right call here over `IOptionsMonitor`: there is no
configuration source to write to, and the demo needs the change to be instant and
obvious. Say that in the comment so it does not read as sloppiness.

### 2.3 The sweeper — `Screenings/Features/ExpireSeatHolds/SeatHoldSweeper.cs`

`Interval` is a const 15 s. Inject `HoldPolicy` and set `timer.Period =
policy.SweepInterval` on every tick (`PeriodicTimer.Period` is settable). With short
holds on, the sweep runs every 4 s, so the lapse is seen almost as soon as it happens.

### 2.4 Seat-level expiry — `Screenings/Infrastructure/SeatMapProjection.cs`

`SeatView` gains one field:

```csharp
public sealed record SeatView(string Number, string Status, DateTimeOffset? HoldExpiresAtUtc);
```

`Project` fills it from `seat.HoldExpiresAtUtc` for held seats. The cached projection
shape changes, which is harmless: the key is version-free but the cache is ephemeral and
invalidation already fires on every seat change.

### 2.5 The UI

* Held seats carry a thin depleting ring drawn with a CSS `conic-gradient` driven by a
  single `requestAnimationFrame` loop over `holdExpiresAtUtc`. One loop for the whole
  map, not one timer per seat.
* The tracked booking's flow step 2 reads `2:47 left to pay`, counting down from
  `payBeforeUtc`, which `BookingResponse` already returns. Under 30 s it turns amber.
* A **Short holds (20 s)** switch in the masthead, next to the e-mail field, posting to
  `/api/screenings/hold-policy`. Its state is read back on load so a reloaded page
  tells the truth.

`/api/screenings/hold-policy` is matched by the existing `screenings` catch-all YARP
route — but the `screenings-catalogue` route is `Order: 0` on the exact path
`/api/screenings`, so there is no conflict. No gateway config change.

---

## Phase 3 — the chaos strip

### 3.1 The switch — `Payments/Features/SimulateFailure/` (new slice)

```csharp
public enum ChaosMode { None, Paused, Slow, Failing }

public sealed class ChaosSwitch              // singleton
{
    public ChaosMode Mode { get; private set; }
    public int RemainingFailures { get; private set; }

    public void Set(ChaosMode mode, int failures = 2);
    public bool TryConsumeFailure();          // Failing burns down to None
    public Task WaitWhilePausedAsync(CancellationToken ct);
}
```

`WaitWhilePausedAsync` awaits a `TaskCompletionSource` that `Set(ChaosMode.None)`
completes. Pausing therefore means *the consumer is holding the message, unacked* —
which is the honest depiction of a stuck service and is exactly what RabbitMQ does in
real life. Guard it with a 5-minute ceiling so a forgotten pause cannot wedge the demo.

Endpoints `GET /payments/chaos` and `POST /payments/chaos` (`{ "mode": "paused" }`),
reached through the existing `/api/payments/{**catch-all}` route.

### 3.2 Wiring it in

* `AuthorizePaymentConsumer.Consume` — first line becomes
  `await chaos.WaitWhilePausedAsync(context.CancellationToken);`. Everything after it is
  unchanged, including the "already decided" check, so un-pausing cannot double-charge.
* `SimulatedPaymentGateway.AuthorizeAsync` — `Slow` multiplies the configured latency to
  5 s; `Failing` throws `InvalidOperationException("The card network refused the
  connection.")` while `TryConsumeFailure()` returns true.

`Failing` is the best of the three to watch, because the existing retry policy
(`200, 500, 1000, 2000, 5000 ms`) is already in `MessagingExtensions` and the tape now
shows those attempts as red rows followed by a green one. Nothing is being added to make
that work — it is being *revealed*.

### 3.3 The UI

A strip above the flow: three toggles (**Pause Payments**, **Slow (5 s)**, **Fail twice,
then succeed**) and a pill showing the live mode, polled with the rest. While paused, the
flow's step 3 reads `Waiting for Payments — it is paused` in amber rather than looking
broken.

Put one line of prose under it: *the browser is not retrying anything; the message is
simply still on the queue.* That sentence is the whole lesson.

---

## Phase 4 — race a rival booker

Almost entirely front end.

* `drawFlow(booking, payment, mail, container)` — take the container as an argument.
* `state.tracked` becomes `state.tracked = { mine, rival }`; `refresh()` resolves both.
* `#flow-section` becomes a two-column grid when a race is on, headed **You** and
  **Rival (rival@example.com)**.
* A **Race a rival** button next to **Book seats** fires both `POST /bookings` calls in
  one `Promise.allSettled`, same screening, same seats, different customer.

No server change is expected. **Verify** that the loser's `cancellationReason` reads
well in the flow: `CancelBookingConsumer` renders it as *"The seats could not be held:
Seat B4 is no longer available."*, which is already the right sentence.

Be accurate about what this proves. Both bookings pass `PlaceBookingHandler`'s courtesy
availability check, then Screenings decides — so what is always demonstrated is **the
aggregate as the consistency boundary**. Whether the two messages are consumed
concurrently and the `xmin` guard fires is genuinely racy. When it does fire, the retry
policy swallows it and the tape shows a red `Faulted` row followed by a successful
retry — so the concurrency story becomes visible when it happens, and is not claimed
when it does not. Add one line to `HoldSeatsConsumer`'s logging for
`DbUpdateConcurrencyException` so the tape's `Detail` says `optimistic concurrency —
retrying`.

---

## Phase 5 — the trace waterfall

### 5.1 Where the data comes from

Not from the Aspire dashboard: it has no queryable API. The tape from phase 1 already
carries everything needed — service, event, publish time, consume time, consume
duration — so the waterfall is a second rendering of data the page already has.

⚠️ **The honest caveat, and it belongs in the UI, not just here:** the bars are built
from wall-clock timestamps taken in five processes. On one developer machine sharing one
clock that is fine to within a millisecond or two. Label the panel *"reconstructed from
message timestamps"* and put the dashboard link right next to it for the real thing.

### 5.2 Rendering — `app.js` + `app.css`

Below the flow, one lane per service in its own colour, x-axis in ms since
`booking.placedAtUtc`:

* A solid bar from consume-start to consume-end for every `Consumed` entry
  (start = `AtUtc − DurationMs`).
* The gap between a `Published` entry and its matching `Consumed` entry is drawn as a
  hairline — **that empty space is the queue wait, and it is the most instructive thing
  on the screen**. Label the largest one.
* An axis with three ticks and a total at the right.

Plain absolutely-positioned divs in a grid; no charting library, nothing to load from a
CDN.

### 5.3 The dashboard link

`GET /api/demo` on the gateway returns `{ "dashboardUrl": "…" }` from the
`Demo:DashboardUrl` configuration set in phase 0. When it is non-empty, each waterfall
and each tape row links to `{dashboardUrl}/traces/detail/{traceId}`; when it is empty
the link is simply absent.

---

## Test and documentation impact

**Tests** (`tests/Demo.Aspire.Domain.Tests`, no containers — keep it that way):

* `ScreeningTests` — update the two `Screening.HoldDuration` references, pass the
  duration at each call site, add one short-hold expiry test.
* New `HoldPolicyTests` — the sweep interval clamp at 10 s, 3 min and the extremes.
* New `ChaosSwitchTests` — `Failing` burns down to `None` after exactly N failures;
  `WaitWhilePausedAsync` completes when the mode is cleared.
* `PersistenceModelTests` needs no change: nothing here touches the EF model.

**README.md** — the current text says the page "only polls", which stops being true in
phase 1. Sections to revise: the flow table (add the tape), the infrastructure table
(Redis gains "the bus tape feed"), and a new **Demo script** section giving the four
things to do in front of an audience, in order:

1. Book two seats and watch the tape fill.
2. Book as `decline@example.com` and watch the seats come back.
3. Short holds + pause Payments: watch the hold lapse on its own.
4. Race a rival for the same seats.

**`docs/ui.png`** will be stale once the rail and the waterfall exist; re-shoot it.

---

## Order of work, and what can be parallelised

```
Phase 0  app host wiring            ─┐
Phase 1  bus tape + SSE + rail       ├─ sequential; everything below reads the tape
Phase 2  hold policy + countdown    ─┘  (independent of 1 — can be done alongside)
Phase 3  chaos strip                 ─┐
Phase 4  race a rival                 ├─ independent of each other
Phase 5  trace waterfall             ─┘
```

Phase 2 touches the domain and the tests and shares no code with phase 1, so the two can
proceed in parallel. Phases 3–5 all want the tape in place first.

## Risks, in the order they are likely to bite

1. **The dashboard URL and trace-detail route** (phase 0 / 5.3) — guessed from the app
   host's own `ASPNETCORE_URLS`. Verify with one `aspire run`. Degrades to a hidden link.
2. **`Platform` gaining a Redis dependency** (1.3) — a deliberate widening of a project
   whose comment currently says it knows about "EF Core, MassTransit or ASP.NET Core".
   Update that comment or the codebase starts lying about itself.
3. **Pausing by holding the message** (3.1) — correct RabbitMQ behaviour, but a forgotten
   pause looks like a hang. The 5-minute ceiling and the amber flow label are what make
   it read as deliberate.
4. **SSE through YARP** (1.4) — avoided entirely by serving the route from the gateway
   itself rather than proxying it. If it is ever moved behind YARP, response buffering
   will need disabling.
5. **The waterfall's cross-process clocks** (5.1) — fine on one machine, wrong the moment
   anything is remote. Labelled in the UI rather than hidden.
