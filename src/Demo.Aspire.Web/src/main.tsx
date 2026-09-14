// Demo Kino's front end. Everything it knows arrives through the gateway under /api, so
// the browser never learns that there are four services behind it.
//
// Phase 0 scaffold: this renders a placeholder so the toolchain — bun, TypeScript 7, and
// the gateway's build target — can be verified end to end before any of the page itself
// is moved across. See docs/react-ui-plan.md.

import { StrictMode } from "react";
import { createRoot } from "react-dom/client";

import { App } from "./App.tsx";

const root = document.getElementById("root");

if (!root) throw new Error("index.html is missing its #root element.");

createRoot(root).render(
    <StrictMode>
        <App />
    </StrictMode>,
);
