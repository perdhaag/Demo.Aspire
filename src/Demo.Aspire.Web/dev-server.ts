// For iterating on the UI against an Aspire app that is already running. Not the
// supported way to show the demo — `aspire run` is, and it serves the built bundle out
// of the gateway's wwwroot like everything else.
//
// The gateway's URL is not fixed across runs; take it from the Aspire dashboard.
//
//   GATEWAY_URL=http://localhost:5152 bun run dev

import index from "./src/index.html";

const gateway = process.env.GATEWAY_URL ?? "http://localhost:5152";

const server = Bun.serve({
    port: 5173,
    development: { hmr: true },
    routes: { "/*": index },

    // Anything the page asks of /api goes to the real gateway, the SSE bus tape
    // included — passed through as a stream rather than buffered, or the tape would
    // arrive all at once at the end of the demo instead of during it.
    async fetch(request) {
        const url = new URL(request.url);

        if (!url.pathname.startsWith("/api")) return new Response(null, { status: 404 });

        return fetch(gateway + url.pathname + url.search, {
            method: request.method,
            headers: request.headers,
            body: request.body,
            duplex: "half",
        } as RequestInit);
    },
});

console.log(`Demo Kino UI on ${server.url} — /api proxied to ${gateway}`);
