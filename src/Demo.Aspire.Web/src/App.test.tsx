import { expect, test } from "bun:test";
import { render, screen } from "@testing-library/react";

import { App } from "./App.tsx";

// A smoke test, and for now that is the whole point of it: it proves bun, TypeScript 7,
// JSX, happy-dom and React Testing Library are all wired together before anything worth
// testing is written. The functions worth testing arrive in phases 5 and 7 — see
// docs/react-ui-plan.md.
test("renders", () => {
    render(<App />);

    expect(screen.getByRole("heading", { name: "Demo Kino" })).toBeDefined();
});
