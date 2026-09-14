using Demo.Aspire.Screenings.Domain;
using Demo.Aspire.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Demo.Aspire.Screenings.Infrastructure;

internal sealed class ScreeningConfiguration : IEntityTypeConfiguration<Screening>
{
    public void Configure(EntityTypeBuilder<Screening> builder)
    {
        builder.ToTable("screenings");

        builder.HasKey(screening => screening.Id);

        builder.Property(screening => screening.Id)
            .HasConversion(id => id.Value, value => new ScreeningId(value))
            .ValueGeneratedNever();

        builder.Property(screening => screening.FilmTitle).HasMaxLength(200).IsRequired();
        builder.Property(screening => screening.Auditorium).HasMaxLength(100).IsRequired();
        builder.Property(screening => screening.StartsAtUtc).IsRequired();

        builder.ComplexProperty(screening => screening.TicketPrice, price =>
        {
            price.Property(money => money.Amount).HasColumnName("ticket_price_amount").HasPrecision(18, 2);
            price.Property(money => money.Currency).HasColumnName("ticket_price_currency").HasMaxLength(3);
        });

        builder.Property<uint>("Version").IsRowVersion().HasColumnName("xmin");

        builder.HasMany(screening => screening.Seats)
            .WithOne()
            .HasForeignKey(ScreeningForeignKey)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(screening => screening.Seats)
            .HasField("_seats")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(screening => screening.StartsAtUtc);
    }

    internal const string ScreeningForeignKey = "ScreeningId";
}

internal sealed class SeatConfiguration : IEntityTypeConfiguration<Seat>
{
    public void Configure(EntityTypeBuilder<Seat> builder)
    {
        builder.ToTable("seats");

        builder.HasKey(seat => seat.Id);

        builder.Property(seat => seat.Id)
            .HasConversion(id => id.Value, value => new SeatId(value))
            .ValueGeneratedNever();

        builder.Property(seat => seat.Number)
            .HasConversion(new SeatNumberConverter(), new SeatNumberComparer())
            .HasColumnName("seat_number")
            .HasMaxLength(4)
            .IsRequired();

        builder.Property(seat => seat.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Property(seat => seat.HeldBy).HasConversion(new BookingReferenceConverter());
        builder.Property(seat => seat.HoldExpiresAtUtc);

        builder.HasIndex(ScreeningConfiguration.ScreeningForeignKey, nameof(Seat.Number)).IsUnique();
        builder.HasIndex(seat => seat.HoldExpiresAtUtc);
    }
}

internal sealed class SeatNumberConverter()
    : ValueConverter<SeatNumber, string>(
        seat => seat.ToString(),
        text => SeatNumber.Parse(text).Value);

internal sealed class SeatNumberComparer()
    : ValueComparer<SeatNumber>(
        (left, right) => left.Equals(right),
        seat => seat.GetHashCode());

internal sealed class BookingReferenceConverter()
    : ValueConverter<BookingReference, Guid>(
        reference => reference.Value,
        value => new BookingReference(value));
