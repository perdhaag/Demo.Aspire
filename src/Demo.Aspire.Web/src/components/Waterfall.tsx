// Draws what flow/waterfall.ts worked out. There is no arithmetic left in here on
// purpose: every offset arrives as a percentage string, so this component is only the
// difference between a number and a rectangle.

import type { CSSProperties } from "react";

import type { WaterfallLayout } from "../flow/waterfall.ts";

export function Waterfall({ layout }: { layout: WaterfallLayout }) {
    return (
        <div className="waterfall">
            <div className="wf-gaptrack">
                {layout.gaps.map(gap => (
                    <span
                        key={gap.event}
                        className="wf-gap"
                        style={{ left: gap.left, width: gap.width }}
                    >
                        {gap.label && (
                            <span className="wf-gap-label" style={{ left: gap.label.left }}>
                                {gap.label.text}
                            </span>
                        )}
                    </span>
                ))}
            </div>

            {layout.lanes.map(lane => (
                <div className="wf-lane" key={lane.service}>
                    <span className="wf-lane-label">{lane.service}</span>
                    <div className="wf-track">
                        {lane.bars.map(bar => (
                            <span
                                key={bar.event}
                                className="wf-bar"
                                title={bar.title}
                                // The bar takes its colour from the same per-service custom
                                // property the tape and the flow dots use, so one hue always
                                // means one service across the whole page. React's style type
                                // has no room for a custom property, hence the cast.
                                style={{
                                    left: bar.left,
                                    width: bar.width,
                                    "--tape-row-color": `var(--svc-${bar.service})`,
                                } as CSSProperties}
                            />
                        ))}
                    </div>
                </div>
            ))}

            <div className="wf-axis">
                <span>0 ms</span>
                <span>total {Math.round(layout.totalMs)} ms</span>
            </div>
        </div>
    );
}
