// Everything the page knows arrives through the gateway under /api, so the browser never
// learns that there are four services behind it.

import type {
    BookingResponse, BusTapeEntry, ChaosMode, ChaosResponse, DemoInfo, HoldPolicyResponse,
    NotificationEntry, PaymentListItem, PlacedBookingResponse, ScreeningListItem, SeatMapView,
} from "./types.ts";

const API = "/api";

export async function api<T>(path: string, init?: RequestInit): Promise<T> {
    const response = await fetch(API + path, init);

    if (!response.ok) {
        // The services answer failures as RFC 9457 problem details, so there is almost
        // always something better to show than the status text.
        const problem = await response.json().catch(() => null) as
            { detail?: string; title?: string } | null;

        throw new Error(problem?.detail ?? problem?.title ?? response.statusText);
    }

    return (response.status === 204 ? null : await response.json()) as T;
}

const json = (body: unknown): RequestInit => ({
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(body),
});

export const listScreenings = () => api<ScreeningListItem[]>("/screenings");

export const getSeatMap = (screeningId: string) =>
    api<SeatMapView>(`/screenings/${screeningId}/seats`);

export const listBookings = (customerEmail: string) =>
    api<BookingResponse[]>(`/bookings?customerEmail=${encodeURIComponent(customerEmail)}`);

export const getBooking = (bookingId: string) => api<BookingResponse>(`/bookings/${bookingId}`);

export const listPayments = () => api<PaymentListItem[]>("/payments?take=25");

export const listNotifications = () => api<NotificationEntry[]>("/notifications?take=25");

export const getChaos = () => api<ChaosResponse>("/payments/chaos");

export const getHoldPolicy = () => api<HoldPolicyResponse>("/screenings/hold-policy");

export const getDemoInfo = () => api<DemoInfo>("/demo");

export const recentTape = () => api<BusTapeEntry[]>("/events/recent?take=100");

export const placeBooking = (screeningId: string, customerEmail: string, seats: string[]) =>
    api<PlacedBookingResponse>("/bookings", json({ screeningId, customerEmail, seats }));

export const postChaos = (mode: ChaosMode) =>
    api<ChaosResponse>("/payments/chaos", json({ mode }));

export const postHoldPolicy = (seconds: number) =>
    api<HoldPolicyResponse>("/screenings/hold-policy", json({ seconds }));
