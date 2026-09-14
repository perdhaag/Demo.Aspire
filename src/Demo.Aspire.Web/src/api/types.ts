// The shapes the gateway serves under /api, mirroring the C# records one for one. Kept
// by hand rather than generated: every service maps /openapi/v1.json, but the gateway
// does not proxy those routes, and codegen that needs the whole distributed app to be
// *running* is a poor trade for eleven records.
//
// The unions matter more than the shapes do. `service` in particular is written straight
// into `var(--svc-${entry.service})` in three places; a union makes the CSS custom
// properties and the C# service names one checked contract instead of two conventions.

export type SeatStatus = "Available" | "Held" | "Sold";
export type BookingStatus = "Placed" | "AwaitingPayment" | "Confirmed" | "Cancelled";
export type PaymentStatus = "Captured" | "Declined";
export type ChaosMode = "None" | "Paused" | "Slow" | "Failing";
export type BusTapeKind = "Published" | "Consumed" | "Faulted";
export type ServiceName = "screenings" | "bookings" | "payments" | "notifications" | "gateway";

/** Dates arrive as ISO-8601 strings and stay that way until something formats them. */
export type IsoDate = string;

export interface ScreeningListItem {
    id: string;
    filmTitle: string;
    auditorium: string;
    startsAtUtc: IsoDate;
    ticketPrice: number;
    currency: string;
    totalSeats: number;
    availableSeats: number;
}

export interface SeatView {
    number: string;
    status: SeatStatus;
    holdExpiresAtUtc: IsoDate | null;
}

export interface SeatMapView {
    screeningId: string;
    filmTitle: string;
    auditorium: string;
    startsAtUtc: IsoDate;
    ticketPrice: number;
    currency: string;
    availableSeats: number;
    seats: SeatView[];
}

export interface BookingResponse {
    bookingId: string;
    screeningId: string;
    filmTitle: string;
    auditorium: string;
    screeningStartsAtUtc: IsoDate;
    customerEmail: string;
    seats: string[];
    total: number;
    currency: string;
    status: BookingStatus;
    placedAtUtc: IsoDate;
    seatsHeldAtUtc: IsoDate | null;
    finishedAtUtc: IsoDate | null;
    payBeforeUtc: IsoDate | null;
    paymentReference: string | null;
    cancellationReason: string | null;
}

export interface PlacedBookingResponse {
    bookingId: string;
    status: BookingStatus;
    total: number;
    currency: string;
    seats: string[];
}

export interface PaymentListItem {
    paymentId: string;
    bookingId: string;
    payer: string;
    amount: number;
    currency: string;
    status: PaymentStatus;
    reference: string | null;
    declineReason: string | null;
    decidedAtUtc: IsoDate;
}

export interface NotificationEntry {
    messageId: string;
    bookingId: string;
    recipient: string;
    subject: string;
    outcome: string;
    sentAtUtc: IsoDate;
}

export interface HoldPolicyResponse {
    seconds: number;
}

export interface ChaosResponse {
    mode: ChaosMode;
}

export interface DemoInfo {
    dashboardUrl: string;
}

export interface BusTapeEntry {
    messageId: string;
    correlationId: string;
    event: string;
    service: ServiceName;
    kind: BusTapeKind;
    atUtc: IsoDate;
    durationMs: number | null;
    traceId: string | null;
    detail: string | null;
}
