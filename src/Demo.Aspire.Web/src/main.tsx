// Demo Kino's front end. Everything it knows arrives through the gateway under /api, so
// the browser never learns that there are four services behind it.
//
// Placing a booking is one POST that returns 202. The rest of the story finishes on the
// message bus — the bus tape is what shows that happening, so this page only needs to poll
// a read model as a slow safety net, not as its main loop.

import { StrictMode } from "react";
import { createRoot } from "react-dom/client";

import { App } from "./App.tsx";
import "./app.css";

const root = document.getElementById("root");

if (!root) throw new Error("index.html is missing its #root element.");

createRoot(root).render(
    <StrictMode>
        <App />
    </StrictMode>,
);
