/**
 * ?screening=<id> makes a seat map shareable and lets a headless browser reach step 2;
 * ?booking=<id> reopens a flow that has already finished. That is the whole of this
 * page's routing, which is why there is no router.
 */
export const readUrlParam = (name: string): string | null =>
    new URLSearchParams(location.search).get(name);

export function writeUrlParam(name: string, value: string | null): void {
    const url = new URL(location.href);

    if (value === null) url.searchParams.delete(name);
    else url.searchParams.set(name, value);

    history.replaceState(null, "", url);
}
