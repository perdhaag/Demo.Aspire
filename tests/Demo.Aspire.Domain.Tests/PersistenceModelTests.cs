using Demo.Aspire.Bookings.Infrastructure;
using Demo.Aspire.Payments.Infrastructure;
using Demo.Aspire.Screenings.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Demo.Aspire.Tests;

/// <summary>
/// A mapping mistake &mdash; a value object EF cannot materialise, a converter without a
/// comparer &mdash; normally shows up the first time a container starts. Building the
/// model and asking for its DDL needs no server at all, so these run in milliseconds and
/// catch the same class of mistake.
/// </summary>
public sealed class PersistenceModelTests
{
    private const string OfflineConnectionString = "Host=offline;Database=none;Username=none;Password=none";

    private static DbContextOptions<TContext> Offline<TContext>()
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>().UseNpgsql(OfflineConnectionString).Options;

    [Fact]
    public void The_screenings_schema_maps_seats_money_and_the_outbox()
    {
        using var context = new ScreeningsDbContext(Offline<ScreeningsDbContext>());

        var script = context.Database.GenerateCreateScript();

        script.ShouldContain("screenings.screenings");
        script.ShouldContain("ticket_price_amount");
        script.ShouldContain("ticket_price_currency");
        script.ShouldContain("seat_number");
        script.ShouldContain("OutboxMessage");
        script.ShouldContain("InboxState");
    }

    [Fact]
    public void The_bookings_schema_flattens_the_value_objects()
    {
        using var context = new BookingsDbContext(Offline<BookingsDbContext>());

        var script = context.Database.GenerateCreateScript();

        script.ShouldContain("bookings.bookings");
        script.ShouldContain("total_amount");
        script.ShouldContain("film_title");
        script.ShouldContain("seats");
        script.ShouldContain("OutboxMessage");
    }

    [Fact]
    public void The_payments_schema_allows_one_decision_per_booking()
    {
        using var context = new PaymentsDbContext(Offline<PaymentsDbContext>());

        var script = context.Database.GenerateCreateScript();

        script.ShouldContain("payments.payments");
        script.ShouldContain("CREATE UNIQUE INDEX");
        script.ShouldContain("OutboxMessage");
    }

    [Fact]
    public void Every_aggregate_carries_a_concurrency_token()
    {
        using var screenings = new ScreeningsDbContext(Offline<ScreeningsDbContext>());
        using var bookings = new BookingsDbContext(Offline<BookingsDbContext>());

        screenings.Model.FindEntityType(typeof(Screenings.Domain.Screening))!
            .FindProperty("Version")!.IsConcurrencyToken.ShouldBeTrue();

        bookings.Model.FindEntityType(typeof(Bookings.Domain.Booking))!
            .FindProperty("Version")!.IsConcurrencyToken.ShouldBeTrue();
    }
}
