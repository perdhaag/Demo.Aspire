// Every date crosses the wire as an ISO-8601 string and is turned into something a
// person can read exactly here. One place to change if that ever stops being true.

export const clock = new Intl.DateTimeFormat(undefined, { hour: "2-digit", minute: "2-digit" });

export const dayAndTime = new Intl.DateTimeFormat(undefined, {
    weekday: "short", day: "numeric", month: "short", hour: "2-digit", minute: "2-digit",
});

export const preciseClock = new Intl.DateTimeFormat(undefined, {
    hour: "2-digit", minute: "2-digit", second: "2-digit", fractionalSecondDigits: 3,
});

export const money = (amount: number, currency: string): string =>
    `${Number(amount).toFixed(2).replace(/\.00$/, "")} ${currency}`;

/** How far after the booking was placed something happened. */
export const sinceStart = (from: string, to: string): string => {
    const ms = Date.parse(to) - Date.parse(from);
    return ms < 1000 ? `+${Math.max(ms, 0)} ms` : `+${(ms / 1000).toFixed(1)} s`;
};

export const at = (value: string): Date => new Date(value);

export function formatPayCountdown(remainingMs: number): string {
    if (remainingMs <= 0) return "expiring now…";

    const totalSeconds = Math.ceil(remainingMs / 1000);
    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;

    return minutes > 0 ? `${minutes}m ${seconds}s left to pay` : `${seconds}s left to pay`;
}
