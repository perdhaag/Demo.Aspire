import { useEffect, type RefObject } from "react";

/**
 * One rAF loop for every held seat currently on screen, rather than a timer per seat.
 *
 * --hold-pct assumes the seat's full hold length was holdPolicySeconds, which is true
 * unless the policy changed mid-hold — close enough for a countdown, and the seat map
 * does not carry when a hold actually began.
 *
 * This deliberately stays outside React: it writes a CSS custom property onto seats React
 * has already rendered, sixty times a second, and re-rendering the whole seat map at that
 * rate to animate one number would be all of React's cost for none of its benefit. The
 * query is scoped to the seat map's own element so the loop cannot reach seats belonging
 * to a screening that is no longer on screen.
 */
export function useHoldRings(container: RefObject<HTMLElement | null>, holdPolicySeconds: number) {
    useEffect(() => {
        const totalMs = holdPolicySeconds * 1000;
        let frame = requestAnimationFrame(function tick() {
            const now = Date.now();

            for (const seat of container.current?.querySelectorAll<HTMLElement>(".seat[data-hold-expires]") ?? []) {
                const remainingMs = Date.parse(seat.dataset.holdExpires ?? "") - now;
                const pct = Math.max(0, Math.min(100, (remainingMs / totalMs) * 100));
                seat.style.setProperty("--hold-pct", pct.toFixed(1));
            }

            frame = requestAnimationFrame(tick);
        });

        return () => cancelAnimationFrame(frame);
    }, [container, holdPolicySeconds]);
}
