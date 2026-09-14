// For iterating on the UI against an Aspire app that is already running. Not the
// supported way to show the demo — `aspire run` is, and it serves the built bundle out of
// the gateway's wwwroot like everything else.
//
// Normally you never start this by hand: the app host declares it as the `web` resource
// and hands it both values below, so pressing Start on it in the dashboard is the whole
// workflow. Run standalone and it needs the gateway's address, which is not fixed across
// runs — take it from the dashboard:
//
//   GATEWAY_URL=http://localhost:5152 bun run dev

import index from "./src/index.html";

const gateway = process.env.GATEWAY_URL ?? "http://localhost:5152";

const server = Bun.serve({
    // The app host allocates the port and passes it here; 5173 is only the standalone
    // default, and a fixed port would make two app hosts on one machine collide.
    port: Number(process.env.PORT ?? 5173),
    development: { hmr: true },
    routes: {
        // Anything the page asks of /api goes to the real gateway, the SSE bus tape
        // included — passed through as a stream rather than buffered, or the tape would
        // arrive all at once at the end of the demo instead of during it.
        //
        // This has to be a route rather than a `fetch` fallback: routes are matched
        // before `fetch` is ever consulted, so "/*" below would answer /api/screenings
        // with the page's own HTML and the browser would try to parse it as JSON.
        "/api/*": request => {
            const url = new URL(request.url);

            return fetch(gateway + url.pathname + url.search, {
                method: request.method,
                headers: request.headers,
                body: request.body,
                duplex: "half",
            } as RequestInit);
        },

        "/*": index,
    },
});

console.log(`Demo Kino UI on ${server.url} — /api proxied to ${gateway}`);
