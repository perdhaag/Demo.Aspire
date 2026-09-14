import { GlobalRegistrator } from "@happy-dom/global-registrator";

// A real origin, not happy-dom's default about:blank: the page reads ?screening= and
// ?booking= out of location.search, and a relative history.replaceState cannot resolve
// against about:blank, so without this every URL-driven test silently sees no parameters.
GlobalRegistrator.register({ url: "http://localhost/" });
