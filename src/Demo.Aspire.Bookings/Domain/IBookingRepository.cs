using Demo.Aspire.SharedKernel;

namespace Demo.Aspire.Bookings.Domain;

public interface IBookingRepository : IRepository<Booking, BookingId>
{
    Task<IReadOnlyList<Booking>> FindForCustomerAsync(
        EmailAddress customer,
        CancellationToken cancellationToken = default);
}
