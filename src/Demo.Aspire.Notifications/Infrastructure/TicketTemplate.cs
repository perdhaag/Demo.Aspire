using System.Globalization;
using System.Net;

namespace Demo.Aspire.Notifications.Infrastructure;

/// <summary>
/// The one place that knows what a ticket looks like. Keeping the wording here rather
/// than in the consumers means the two slices differ only in what they have to say.
/// </summary>
internal static class TicketTemplate
{
    public static string Confirmation(
        string filmTitle,
        string auditorium,
        DateTimeOffset startsAtUtc,
        IReadOnlyList<string> seats,
        decimal amount,
        string currency,
        string paymentReference,
        Guid bookingId) =>
        Wrap(
            "Your tickets are confirmed",
            $"""
             <p>Thank you &mdash; your seats are yours.</p>
             <table cellpadding="6" style="border-collapse:collapse">
               <tr><td><strong>Film</strong></td><td>{Escape(filmTitle)}</td></tr>
               <tr><td><strong>Auditorium</strong></td><td>{Escape(auditorium)}</td></tr>
               <tr><td><strong>Starts</strong></td><td>{startsAtUtc.UtcDateTime.ToString("dddd d MMMM, HH:mm", CultureInfo.InvariantCulture)} UTC</td></tr>
               <tr><td><strong>Seats</strong></td><td>{Escape(string.Join(", ", seats))}</td></tr>
               <tr><td><strong>Paid</strong></td><td>{amount.ToString("0.00", CultureInfo.InvariantCulture)} {Escape(currency)}</td></tr>
               <tr><td><strong>Payment</strong></td><td><code>{Escape(paymentReference)}</code></td></tr>
               <tr><td><strong>Booking</strong></td><td><code>{bookingId}</code></td></tr>
             </table>
             <p style="color:#666">Show this e-mail at the door.</p>
             """);

    public static string Cancellation(
        IReadOnlyList<string> seats,
        string reason,
        Guid bookingId) =>
        Wrap(
            "Your booking did not go through",
            $"""
             <p>We could not complete your booking, and you have not been charged.</p>
             <table cellpadding="6" style="border-collapse:collapse">
               <tr><td><strong>Seats</strong></td><td>{Escape(string.Join(", ", seats))}</td></tr>
               <tr><td><strong>Reason</strong></td><td>{Escape(reason)}</td></tr>
               <tr><td><strong>Booking</strong></td><td><code>{bookingId}</code></td></tr>
             </table>
             <p style="color:#666">The seats are back on sale, so do try again.</p>
             """);

    private static string Wrap(string heading, string body) =>
        $"""
         <html><body style="font-family:system-ui,sans-serif;max-width:34rem">
           <h2 style="margin-bottom:0">Demo Kino</h2>
           <h3 style="margin-top:.25rem;font-weight:500">{Escape(heading)}</h3>
           {body}
         </body></html>
         """;

    private static string Escape(string value) => WebUtility.HtmlEncode(value);
}
