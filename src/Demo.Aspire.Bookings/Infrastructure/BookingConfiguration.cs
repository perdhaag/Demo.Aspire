using Demo.Aspire.Bookings.Domain;
using Demo.Aspire.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Demo.Aspire.Bookings.Infrastructure;

internal sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("bookings");

        builder.HasKey(booking => booking.Id);

        builder.Property(booking => booking.Id)
            .HasConversion(id => id.Value, value => new BookingId(value))
            .ValueGeneratedNever();

        builder.Property(booking => booking.Screening)
            .HasConversion(reference => reference.Value, value => new ScreeningReference(value))
            .IsRequired();

        builder.Property(booking => booking.Customer)
            .HasConversion(email => email.Value, value => EmailAddress.Create(value).Value)
            .HasMaxLength(256)
            .IsRequired();

        // Seats are a value, not a child table: they are chosen once and never edited
        // individually, so storing them as one column keeps the aggregate honest.
        builder.Property(booking => booking.Seats)
            .HasConversion(new SeatListConverter(), new SeatListComparer())
            .HasColumnName("seats")
            .HasMaxLength(64)
            .IsRequired();

        builder.ComplexProperty(booking => booking.Total, total =>
        {
            total.Property(money => money.Amount).HasColumnName("total_amount").HasPrecision(18, 2);
            total.Property(money => money.Currency).HasColumnName("total_currency").HasMaxLength(3);
        });

        builder.ComplexProperty(booking => booking.Details, details =>
        {
            details.Property(value => value.FilmTitle).HasColumnName("film_title").HasMaxLength(200);
            details.Property(value => value.Auditorium).HasColumnName("auditorium").HasMaxLength(100);
            details.Property(value => value.StartsAtUtc).HasColumnName("screening_starts_at_utc");
        });

        builder.Property(booking => booking.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(booking => booking.PaymentReference).HasMaxLength(64);
        builder.Property(booking => booking.CancellationReason).HasMaxLength(400);

        builder.Property<uint>("Version").IsRowVersion().HasColumnName("xmin");

        builder.HasIndex(booking => booking.Customer);
        builder.HasIndex(booking => booking.Screening);
    }
}

internal sealed class SeatListConverter()
    : ValueConverter<IReadOnlyList<SeatNumber>, string>(
        seats => string.Join(',', seats),
        text => Parse(text))
{
    private static IReadOnlyList<SeatNumber> Parse(string text) =>
    [
        .. text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => SeatNumber.Parse(part))
            .Where(seat => seat.IsSuccess)
            .Select(seat => seat.Value),
    ];
}

internal sealed class SeatListComparer()
    : ValueComparer<IReadOnlyList<SeatNumber>>(
        (left, right) => left!.SequenceEqual(right!),
        seats => seats.Aggregate(0, (hash, seat) => HashCode.Combine(hash, seat.GetHashCode())),
        seats => seats.ToArray());
