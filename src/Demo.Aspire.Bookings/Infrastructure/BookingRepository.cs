using Demo.Aspire.Bookings.Domain;
using Demo.Aspire.Platform.Domain;
using Demo.Aspire.Platform.Persistence;
using Demo.Aspire.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Demo.Aspire.Bookings.Infrastructure;

internal sealed class BookingRepository(BookingsDbContext dbContext) : IBookingRepository
{
    public Task<Booking?> FindAsync(BookingId id, CancellationToken cancellationToken = default) =>
        dbContext.Bookings.FirstOrDefaultAsync(booking => booking.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Booking>> FindForCustomerAsync(
        EmailAddress customer,
        CancellationToken cancellationToken = default) =>
        await dbContext.Bookings
            .AsNoTracking()
            .Where(booking => booking.Customer == customer)
            .OrderByDescending(booking => booking.PlacedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Booking aggregate, CancellationToken cancellationToken = default) =>
        await dbContext.Bookings.AddAsync(aggregate, cancellationToken);
}

internal sealed class BookingsUnitOfWork(BookingsDbContext dbContext, IDomainEventDispatcher dispatcher)
    : EfUnitOfWork<BookingsDbContext>(dbContext, dispatcher);
