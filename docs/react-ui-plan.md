# Implementation plan — the UI in React, TypeScript 7 and Bun

> **Done.** All eight phases are implemented; `src/Demo.Aspire.Gateway/wwwroot` is build
> output now and `app.js` is gone. What the build actually turned up is recorded in
> [What it found](#what-it-found) at the end — including a bug the vanilla page had all
> along. The plan is left standing as written so the reasoning behind each decision is
> still readable next to the code it produced.

The front end today is one 930-line `app.js`, one 911-line `app.css` and a hand-written
`index.html`, served straight out of `src/Demo.Aspire.Gateway/wwwroot`. It works, and it
is deliberately dependency-free. What it is not is *editable by someone who did not write
it* — the page is a pile of `document.getElementById` handles mutated from fourteen
different places, and knowing whether a change to `drawFlow` breaks the bus tape requires
reading both.

This plan replaces it with React 19 + TypeScript 7, built by Bun, emitted into the same
`wwwroot` the gateway already serves. **No C# changes are required.** The gateway keeps
`UseDefaultFiles()`/`UseStaticFiles()`, YARP keeps its five routes, the SSE endpoint keeps
its shape, and `aspire run` stays a single command.

| Phase | What lands | Size |
|---|---|---|
| 0 | Toolchain: bun, TypeScript 7, build wired into the gateway's build | Medium |
| 1 | Shell — `index.html`, `<App>`, the CSS moved across, masthead/footer/toast | Small |
| 2 | Data layer — API types, `fetch` wrapper, the snapshot store, the bus-tape store | Large |
| 3 | Steps 1 and 2 — screenings, seat map, checkout, "Race a rival" | Large |
| 4 | The two demo switches — short holds, chaos strip | Small |
| 5 | Step 3 — flow panels, pay countdown, trace waterfall | Large |
| 6 | Ledgers — the three tabs | Small |
| 7 | The bus tape — rail, mobile dock, filter, peek row | Medium |
| 8 | Swap: React takes `/`, `app.js` is deleted, docs and screenshot updated | Small |

Phases 1–7 build at `wwwroot/next/` and are reachable at `/next` while the existing page
keeps serving `/`. That is a one-word change to `outdir` and it buys the single most
valuable thing in a rewrite of a UI whose whole purpose is to be *watched*: you can put
the two pages side by side in one running Aspire app and check that the new one tells the
same story. Phase 8 is when `/` changes hands.

---

## Decisions taken up front

**No server-state library.** TanStack Query is the obvious candidate and it is the wrong
one here. `refresh()` in `app.js` fetches bookings, payments, mail and the chaos mode in
one `Promise.all` and then draws the flow from all four *as one snapshot* — the flow step
that says "Payment captured" and the one that says "Booking confirmed" have to agree with
each other, and four independently-cached queries can tear between them mid-flight. One
provider holding one coherent snapshot, refreshed by one function, is both a faithful
translation and about 90 lines. Revisit if the page ever grows a second screen.

**The CSS moves across essentially verbatim.** All 911 lines of it. It is theme-aware, it
already drives itself off `data-*` attributes and `aria-pressed` — which is exactly what
React props render into — and rewriting it into CSS Modules or Tailwind at the same time
as the DOM would turn one reviewable change into two unreviewable ones. One global
`app.css`, imported once from the entry point. Splitting it per-component is a later,
independent commit if anyone wants it.

**Types are hand-written, not generated.** Every service maps `/openapi/v1.json`, but the
gateway does not proxy those routes, and standing up a codegen step that requires the
whole distributed app to be *running* is a poor trade for eleven records. One
`src/api/types.ts` mirroring the C# records, kept honest by the fact that the page breaks
loudly when it drifts. The OpenAPI documents are there if this ever gets big enough to
justify it.

**The bus tape lives outside React state.** It is a live SSE feed that can burst several
entries per second, each one appending to a 200-row ring buffer. A module-level store read
through `useSyncExternalStore` keeps those appends from re-rendering the seat map, and —
more importantly — keeps one `EventSource` open rather than the two that a `useEffect`
under StrictMode would otherwise open and close.

---

## Phase 0 — toolchain

Bun is not installed on this machine; mise already manages the .NET SDK, so it manages
this too:

```toml
# mise.toml
[tools]
dotnet = "10"
bun = "latest"
```

The app itself is a plain Bun project at `src/Demo.Aspire.Web/` — not a `.csproj`, and
not in the solution. It is a folder with a `package.json`.

```
src/Demo.Aspire.Web/
├── package.json
├── tsconfig.json
├── bunfig.toml
├── dev-server.ts
├── test-setup.ts
└── src/
    ├── index.html
    ├── main.tsx
    ├── app.css            ← moved from wwwroot, unchanged
    ├── api/
    ├── bus/
    ├── hooks/
    └── components/
```

```jsonc
// package.json
{
  "name": "demo-kino-web",
  "private": true,
  "type": "module",
  "scripts": {
    "dev": "bun run dev-server.ts",
    "build": "bun build ./src/index.html --outdir ../Demo.Aspire.Gateway/wwwroot/next --production --sourcemap=linked",
    "typecheck": "tsc --noEmit",
    "test": "bun test"
  },
  "dependencies": { "react": "^19.3.0", "react-dom": "^19.3.0" },
  "devDependencies": {
    "typescript": "^7.0.2",
    "@types/react": "^19.3.0",
    "@types/react-dom": "^19.3.0",
    "@testing-library/react": "^16",
    "@happy-dom/global-registrator": "^19"
  }
}
```

`--production` rather than `--minify`: it sets `NODE_ENV` for the bundle as well, which
is what drops React's development build. On the phase 0 scaffold that is the difference
between 430 KB and 212 KB, and it is entirely React either way.

`typescript@7` is the native (Go) compiler, stable since 7.0. It is used **only** for
`tsc --noEmit` — Bun strips the types during bundling and never invokes `tsc`. Two things
worth knowing before anyone is surprised by them:

* Set `"erasableSyntaxOnly": true` in `tsconfig.json`. Bun erases types rather than
  compiling them, so `enum`, `namespace` and constructor parameter properties are not
  available; having the compiler say so is better than having the bundler say nothing.
* **Rider's bundled TypeScript service is not `tsgo`.** The editor may be running an
  older `tsc` for inlay hints and red squiggles than `bun run typecheck` runs in the
  build. Check Rider's TypeScript settings point at `node_modules/typescript`, and treat
  `bun run typecheck` as the authority either way.

```jsonc
// tsconfig.json
{
  "compilerOptions": {
    "target": "esnext",
    "lib": ["esnext", "dom", "dom.iterable"],
    "module": "preserve",
    "moduleResolution": "bundler",
    "jsx": "react-jsx",
    "allowImportingTsExtensions": true,
    "strict": true,
    "noUncheckedIndexedAccess": true,
    "verbatimModuleSyntax": true,
    "erasableSyntaxOnly": true,
    "noEmit": true,
    "skipLibCheck": true
  },
  "include": ["src", "dev-server.ts"]
}
```

`lib` needs `esnext` specifically, not `es2022` — `loadSeatMap` uses `Map.groupBy`, and
the tape store will want `Iterator.prototype` helpers.

### Wiring the build into `dotnet build`

The demo's promise is that `aspire run` is the whole story, which means the bundle has to
exist without anyone remembering to build it. A target in `Demo.Aspire.Gateway.csproj`,
with `Inputs`/`Outputs` so it is skipped on an unchanged tree:

```xml
<PropertyGroup>
    <WebRoot>$(MSBuildThisFileDirectory)..\Demo.Aspire.Web\</WebRoot>
</PropertyGroup>

<Target Name="BuildWebUi" BeforeTargets="Build"
        Inputs="@(WebSource)" Outputs="wwwroot\index.html">
    <ItemGroup>
        <WebSource Include="$(WebRoot)src\**\*" Exclude="$(WebRoot)node_modules\**" />
        <WebSource Include="$(WebRoot)package.json;$(WebRoot)bun.lock" />
    </ItemGroup>
    <Exec Command="bun install --frozen-lockfile" WorkingDirectory="$(WebRoot)" />
    <Exec Command="bun run build" WorkingDirectory="$(WebRoot)" />
</Target>
```

Finding bun is the fiddly part, and it is worth doing properly rather than assuming.
mise installs it, but whether it is on `PATH` depends on whether the shell running the
build has mise activated — an IDE launched from a desktop menu generally has not, and
neither does a non-interactive shell. So a `ResolveBun` target probes `bun`, falls back
to `mise exec -- bun`, and only then gives up with a sentence rather than a stack trace:

```xml
<Target Name="ResolveBun" Condition="'$(SkipWebUi)' != 'true'">
    <Exec Command="bun --version" ContinueOnError="true" IgnoreExitCode="true" … >
        <Output TaskParameter="ExitCode" PropertyName="BunOnPath" />
    </Exec>
    <Exec Command="mise exec -- bun --version" Condition="'$(BunOnPath)' != '0'" … >
        <Output TaskParameter="ExitCode" PropertyName="BunViaMise" />
    </Exec>
    <PropertyGroup>
        <BunCommand Condition="'$(BunOnPath)' == '0'">bun</BunCommand>
        <BunCommand Condition="'$(BunOnPath)' != '0' and '$(BunViaMise)' == '0'">mise exec -- bun</BunCommand>
    </PropertyGroup>
    <Error Condition="'$(BunCommand)' == ''" Text="bun is required to build the UI …" />
</Target>
```

The fallback is not hypothetical: it is the branch that fires on this machine.

`dotnet test` touches only `tests/`, so it stays exactly as fast as it is today — 49
tests in under a second, no containers, no bun.

### Two things for `.gitignore`

```
node_modules/
src/Demo.Aspire.Gateway/wwwroot/next/
```

Only `next/` for now — the rest of `wwwroot` is still the hand-written page, and stays
tracked until phase 8 deletes it. That is when this line widens to all of `wwwroot`.
Commit `bun.lock`.

### The dev server

`bun run dev` is for iterating on the UI against an Aspire app that is already running.
Everything the page needs is under `/api` on the gateway, so the dev server is fifteen
lines: Bun's HTML entrypoint for HMR and React Fast Refresh, and one fallback that
forwards `/api/*` to the gateway.

```ts
// dev-server.ts
const gateway = process.env.GATEWAY_URL ?? "http://localhost:5152";

Bun.serve({
  port: 5173,
  development: { hmr: true },
  routes: { "/*": (await import("./src/index.html")).default },
  async fetch(request) {
    const url = new URL(request.url);
    if (!url.pathname.startsWith("/api")) return new Response(null, { status: 404 });
    // SSE included: pass the stream through untouched rather than buffering it.
    return fetch(gateway + url.pathname + url.search, {
      headers: request.headers, method: request.method, body: request.body,
      // @ts-expect-error — duplex is required for a streamed request body
      duplex: "half",
    });
  },
});
```

Take the gateway's URL from the Aspire dashboard; it is not fixed across runs. This is a
convenience, not the supported path — `aspire run` remains the way the demo is shown.

---

## Phase 1 — the shell

`src/index.html` is the current one with everything between `<body>` and `<footer>`
replaced by `<div id="root">`, and `<script type="module" src="./main.tsx">` at the end.
Keep the inline SVG favicon. `main.tsx` is `createRoot(...).render(<StrictMode><App/></StrictMode>)`.

Move `app.css` across untouched and `import "./app.css"` from `main.tsx`; Bun bundles it.

Three components that hold no interesting state:

* `<Masthead>` — the brand block, the short-holds switch, the e-mail input. The e-mail is
  lifted to `App` state because both the checkout and the bookings ledger read it.
* `<Footer>` — static text.
* `<Toast>` — the current `toast()` is a module-level `setTimeout` writing into a fixed
  element. In React it is a `useToast()` hook over `useState<string|null>` with a 6s timer,
  exposed through a context so any component can call `toast(message)` without prop-drilling
  it five levels. Small, but it is used from nine places, so it comes first.

At the end of this phase `/next` renders a page with the right chrome and nothing in it.

---

## Phase 2 — the data layer

### `src/api/types.ts`

One interface per response record. The unions matter more than the shapes, because they
are what turn four classes of runtime typo into compile errors:

```ts
export type SeatStatus   = "Available" | "Held" | "Sold";
export type BookingStatus = "Placed" | "AwaitingPayment" | "Confirmed" | "Cancelled";
export type PaymentStatus = "Captured" | "Declined";
export type ChaosMode    = "None" | "Paused" | "Slow" | "Failing";
export type BusTapeKind  = "Published" | "Consumed" | "Faulted";
export type ServiceName  = "screenings" | "bookings" | "payments" | "notifications" | "gateway";
```

`ServiceName` in particular: `app.js` writes `var(--svc-${entry.service})` into a style
property from a server-supplied string, in three separate places. A union makes the CSS
custom properties and the C# service names one checked contract.

Mirror `ScreeningListItem`, `SeatMapResponse`, `BookingResponse`, `PlacedBookingResponse`,
`PaymentListItem`, the notification log entry, `HoldPolicyResponse`, `ChaosResponse`,
`BusTapEntry` and `DemoInfo`. Dates stay `string` at the boundary — they are ISO-8601 off
the wire and every consumer wants a `Date` or a formatted string, never the raw value; a
`parseUtc` helper in `src/api/format.ts` is the single place that changes if that ever
stops being true.

### `src/api/client.ts`

The existing `api()` unchanged in behaviour — RFC 9457 problem details unwrapped into the
thrown `Error`, 204 as `null` — with a generic return type and the four call sites for
`POST` wrapped as named functions (`placeBooking`, `setChaosMode`, `setHoldPolicy`) so no
component writes a JSON header by hand.

### `src/bus/tape-store.ts`

A module-level store, not React state:

```ts
type TapeState = { entries: BusTapeEntry[]; connected: boolean };
```

It owns the ring buffer (200 rows), the correlation index (the 50-booking `Map` that the
waterfall is rebuilt from), the `EventSource`, and the `/api/events/recent?take=100` seed.
It exposes `subscribe(listener)` and `getSnapshot()` for `useSyncExternalStore`, plus
`onTrackedEntry(callback)` — the hook the snapshot store uses to know that a tracked
booking just moved and a refresh is worth doing now rather than in four seconds.

Two things to get right here, because both are silent when wrong:

* Connect lazily on first subscribe and never disconnect. `EventSource` reconnects on its
  own; a cleanup that closes it means StrictMode's double-mount opens two streams and
  closes one, and in production a re-render storm would thrash the connection.
* `getSnapshot()` must return a referentially stable value between appends, or
  `useSyncExternalStore` will loop. Swap the whole state object on append and return the
  same object otherwise.

### `src/data/DemoDataProvider.tsx`

The direct translation of `refresh()` and `tick()`. Holds:

```ts
{ bookings, payments, mail, chaosMode, screenings, seatMap, holdPolicySeconds, dashboardUrl }
```

and exposes `refresh()`. One `useEffect` runs the 4s safety-net tick; one subscribes to
`onTrackedEntry` with the existing 150 ms debounce. The debounce comment in `app.js` is
worth carrying over verbatim — the read model the flow queries is written a moment *after*
the message that announces it, and someone will eventually try to delete the delay.

---

## Phase 3 — the catalogue, the seat map and the checkout

`<ScreeningList>` is a straight map over `screenings`. `<SeatMap>` groups by row with
`Map.groupBy` as today and renders `<SeatRow>` → `<Seat>`.

This phase is where the rewrite actually pays. Today `toggleSeat` updates the selection
and then does this to keep the DOM in step:

```js
for (const button of el('seatmap').querySelectorAll('.seat')) {
    const seat = button.getAttribute('aria-label').split(' ')[1].replace(',', '');
    button.setAttribute('aria-pressed', String(state.selected.has(seat)));
}
```

It parses the seat number back out of an accessibility label. In React the whole thing is
`aria-pressed={selected.has(seat.number)}` on `<Seat>` and the loop disappears. Note this
in the commit message; it is the clearest single answer to "why bother rewriting a page
that works".

Selection state (`Set<string>`, max 8) lives in `App` because the checkout, the tally and
the race button all read it.

**The hold countdown rings stay imperative.** `tickHoldRings` runs on `requestAnimationFrame`
and writes `--hold-pct` onto every held seat. Do not move that into React state — 60 Hz of
`setState` to animate a CSS custom property is exactly the thing React is bad at. Keep it
as a `useHoldRings(seatMapRef)` hook: one effect, one rAF loop, `querySelectorAll` inside
the ref, `style.setProperty`. The same reasoning already written into the comment above
that function applies unchanged; keep the comment.

`<Checkout>` carries the tally, **Book seats** and **Race a rival**. `Promise.allSettled`
over two `placeBooking` calls is unchanged; what changes is that the "exactly one of you
keeps them" result is rendered from state rather than by toggling `hidden` on two panels.

URL state (`?screening=`, `?booking=`) goes in a `useUrlParam(name)` hook over
`history.replaceState` — no router. There is one screen.

---

## Phase 4 — the two demo switches

`<ChaosStrip>` is three buttons over `chaosMode` from the provider. Pause and Slow toggle;
Fail is one-shot and clears itself server-side, which is why the mode is re-read on every
refresh rather than only on click — carry that comment across too.

`<ShortHoldsSwitch>` in the masthead reads and writes `/screenings/hold-policy` and updates
`holdPolicySeconds`, which the hold rings divide by.

---

## Phase 5 — the flow

`<FlowPanel>` takes `{ booking, payment, mail, label }` and derives the five steps exactly
as `drawFlow` does — that function is already pure apart from its final `replace()` call,
so it becomes `flowSteps(booking, payment, mail, chaosMode): FlowStep[]` in
`src/flow/steps.ts` with no DOM in it at all. That makes it the first thing in this UI
that can be unit-tested, and it is worth testing: five states across four step kinds,
including the two that depend on the chaos mode.

`<FlowStep>` renders one `<li>` with `data-state`. The "Xs left to pay" countdown becomes
its own leaf component with a 1 Hz `setInterval` — at the leaf, so a tick re-renders one
`<span>` and cannot disturb focus or hover anywhere else. That is the same concern the
current comment raises; React makes it structural rather than a convention.

`<Waterfall>` is likewise a pure function (`buildHops` + the percentage arithmetic) feeding
a dumb component. Both hop-building and the widest-gap selection are worth tests.

---

## Phase 6 — the ledgers

Three tabs, three tables, one `<DataTable>` taking headings, rows and an empty message.
The current tab switching walks `[role="tab"]` and toggles `hidden`; it becomes
`useState<"bookings"|"payments"|"mail">`. Keep the ARIA wiring — `role="tablist"`,
`aria-controls`, `aria-selected` — exactly as it is.

---

## Phase 7 — the bus tape

`<BusTape>` reads the store through `useSyncExternalStore`. Rows are keyed by
`${correlationId}:${event}:${kind}:${atUtc}` — the tape can legitimately hold two entries
with the same event name (a publish and a consume), so the key needs all four parts or
React will reuse the wrong row and the colours will flicker.

The mobile dock: `useMediaQuery("(max-width: 900px)")` for `tapeDock`, `isOpen` state, and
the auto-open-once-per-booking behaviour that `state.tapeAutoOpened` encodes. The peek row
is the newest entry, which is now simply `entries.at(-1)` rather than a separate
`updateTapePeek` write — one fewer thing to keep in step.

The "This booking only" filter stops being a loop that sets `hidden` on every row and
becomes a `filter` before the map.

---

## Phase 8 — the swap

1. `outdir` back to `../Demo.Aspire.Gateway/wwwroot`.
2. Delete `wwwroot/app.js`, `wwwroot/index.html`, `wwwroot/app.css` — they are `src/Demo.Aspire.Web/src/`'s problem now, and `wwwroot` becomes build output.
3. README: the "Watching it happen" section describes behaviour, not implementation, so it
   mostly stands. The **Running it** section gains a line saying the UI is built by bun
   during `dotnet build`, and the troubleshooting section gains bun alongside the SDK notes.
4. Re-capture `docs/ui.png`. The README already admits the screenshot predates the bus
   tape, the chaos strip and "Race a rival"; this is the moment to fix that.
5. `run-on-phone.sh` needs a look — it serves the demo on the tailnet, and the dev server's
   port is new if anyone wants HMR on a phone.

---

## Testing

`bun test` with happy-dom and React Testing Library, run from `src/Demo.Aspire.Web/`. The
point is not coverage; it is that phases 5 and 7 extract four genuinely tricky pure
functions that have never been testable:

* `flowSteps()` — the five-step derivation, across confirmed / declined / cancelled /
  in-flight / paused-payments.
* `buildHops()` — publish/consume pairing, and that a `Faulted` row is excluded from the
  waterfall but not from the tape.
* The tape ring buffer and the 50-entry correlation eviction.
* `formatPayCountdown()` and `sinceStart()` — pure string formatting, two lines each,
  and the kind of thing that is wrong at the minute boundary for a year before anyone
  notices.

`dotnet test` is untouched and still needs no containers.

---

## What could go wrong

**Bun becomes a build prerequisite.** Today anyone with the .NET SDK can build this repo.
After phase 0 they need bun as well. The `SkipWebUi` escape hatch and the `CheckBun`
error message are what keep that from being a wall; mise makes it one command for anyone
who already has mise.

**`wwwroot` becomes generated.** Anyone with a stale working tree will have an old bundle
sitting there after a `git pull` that deleted the sources. Worth a line in the README.

**StrictMode and the SSE stream.** Covered by the module-level store, but it is the single
most likely way to end up with a tape that shows every message twice. If that appears,
that is where to look.

**The hold rings and the pay countdown are the two places where React is the wrong tool**
and the plan deliberately keeps them imperative and leaf-local respectively. A future
cleanup that "simplifies" either into ordinary state will make the page visibly worse on
a phone; both deserve the comments that are already written for them.

**Nothing here changes the story the demo tells.** If a phase starts requiring a C# change
to work, that is the signal that the rewrite has drifted — the whole front end talks to
`/api`, and there is no reason for that to stop being true.

---

## What it found

Five things surfaced during the build that the plan did not anticipate. Four of them were
defects, and none were in the parts anyone would have predicted.

**The waterfall's "queue wait" label has never once rendered.** `drawWaterfall` picks the
widest publish-to-consume gap with

```js
gaps.reduce((widest, gap) =>
    gap.endMs - gap.startMs > (widest?.endMs - widest?.startMs ?? -1) ? gap : widest, null)
```

`-` binds tighter than `??`, so on the first iteration the right-hand side is
`(undefined - undefined) ?? -1` — and that is `NaN`, not `-1`, because `NaN` is not
nullish. Every comparison against `NaN` is false, the accumulator stays `null`, and
`gap === widestGap` never matches for any gap. Extracting the arithmetic into a pure
function and writing a test for it is what exposed this; the React version shows the
label. This is the clearest argument in the whole exercise for the plan's insistence that
phases 5 and 7 extract testable functions.

**The race could leave its buttons permanently disabled.** The old handler re-enabled them
by calling `updateTally()` after `await refresh()`, so a refresh that threw never got
there. It is a `finally` now.

**Three defects were in the new data layer, not the old page**, and every one of them only
appeared once a real component consumed it: tracked bookings were resolved through
`GET /bookings/{id}` and then discarded (so a race's winning panel had nothing to draw,
because the rival's booking belongs to a different address and is never in `bookings`);
there was no way to tell "catalogue not fetched yet" from "no screenings on sale", so the
page briefly claimed the latter; and the tape's `connected` boolean could not distinguish a
stream that had never opened from one that had opened and dropped, so the rail announced
"reconnecting…" from first paint. The lesson is the unglamorous one — a data layer written
against a plan rather than against a caller is a data layer with holes in it.

**`bun` was already installed here, and still could not be found.** mise had it, but mise
is activated per shell, so a non-interactive shell and an IDE launched from a desktop menu
both see nothing on `PATH`. Hence `ResolveBun` probing `bun` and then `mise exec -- bun`
rather than the single `CheckBun` guard the plan sketched.

**Two small things the plan got wrong on its own terms.** `--minify` alone leaves React's
development build in the bundle; `--production` sets `NODE_ENV` too and halves it, 430 KB
to 212 KB. And happy-dom starts its document at `about:blank`, where a relative
`history.replaceState` cannot resolve — so every test of the `?screening=` and `?booking=`
deep links silently saw no parameters until the registrator was given a real origin.
