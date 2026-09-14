import { useSyncExternalStore } from "react";

/** Below the two-column breakpoint the bus tape becomes a dock; this is how it knows. */
export function useMediaQuery(query: string): boolean {
    const list = matchMedia(query);

    return useSyncExternalStore(
        listener => {
            list.addEventListener("change", listener);
            return () => list.removeEventListener("change", listener);
        },
        () => list.matches);
}
