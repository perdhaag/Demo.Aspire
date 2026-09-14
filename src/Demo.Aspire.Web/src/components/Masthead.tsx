import { useSelection } from "../data/selection.tsx";
import { ShortHoldsSwitch } from "./ShortHoldsSwitch.tsx";

export function Masthead() {
    const { email, setEmail } = useSelection();

    return (
        <header className="masthead">
            <div className="masthead-inner">
                <div className="brand">
                    <span className="brand-mark" aria-hidden="true">🎟</span>
                    <div>
                        <h1>Demo Kino</h1>
                        <p>Five services, one booking, no orchestrator.</p>
                    </div>
                </div>

                <ShortHoldsSwitch />

                <label className="identity">
                    <span>Booking as</span>
                    <input
                        type="email"
                        value={email}
                        spellCheck={false}
                        aria-label="Your e-mail address"
                        onChange={event => setEmail(event.target.value)}
                    />
                </label>
            </div>
        </header>
    );
}
