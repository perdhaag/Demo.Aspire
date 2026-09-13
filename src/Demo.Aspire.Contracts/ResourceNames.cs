namespace Demo.Aspire.Contracts;

/// <summary>
/// The names the app host gives to the infrastructure it starts. They end up as
/// <c>ConnectionStrings__&lt;name&gt;</c> environment variables, so both sides have to
/// agree on the spelling &mdash; which is exactly the kind of agreement that belongs in
/// the shared contracts assembly rather than in four separate string literals.
/// </summary>
public static class ResourceNames
{
    public const string Messaging = "messaging";

    public const string Cache = "cache";

    public const string ScreeningsDatabase = "screenings-db";

    public const string BookingsDatabase = "bookings-db";

    public const string PaymentsDatabase = "payments-db";

    public const string Mail = "mail";

    public static class Services
    {
        public const string Screenings = "screenings";

        public const string Bookings = "bookings";

        public const string Payments = "payments";

        public const string Notifications = "notifications";

        public const string Gateway = "gateway";
    }
}
